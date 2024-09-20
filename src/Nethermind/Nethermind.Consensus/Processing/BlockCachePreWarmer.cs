// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.ObjectPool;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Specs;
using Nethermind.Core.Threading;
using Nethermind.Core.Cpu;
using Nethermind.Evm;
using Nethermind.Evm.Tracing;
using Nethermind.Evm.TransactionProcessing;
using Nethermind.Logging;
using Nethermind.State;
using Nethermind.Core.Eip2930;
using System.Linq;
using System.Collections.Generic;

namespace Nethermind.Consensus.Processing;

public sealed class BlockCachePreWarmer(ReadOnlyTxProcessingEnvFactory envFactory, ISpecProvider specProvider, ILogManager logManager, PreBlockCaches? preBlockCaches = null) : IBlockCachePreWarmer
{
    private readonly ObjectPool<IReadOnlyTxProcessorSource> _envPool = new DefaultObjectPool<IReadOnlyTxProcessorSource>(new ReadOnlyTxProcessingEnvPooledObjectPolicy(envFactory), Environment.ProcessorCount);
    private readonly ObjectPool<SystemTransaction> _systemTransactionPool = new DefaultObjectPool<SystemTransaction>(new DefaultPooledObjectPolicy<SystemTransaction>(), Environment.ProcessorCount);
    private readonly ILogger _logger = logManager.GetClassLogger<BlockCachePreWarmer>();

    public Task PreWarmCaches(Block suggestedBlock, Hash256? parentStateRoot, AccessList? systemTxAccessList, CancellationToken cancellationToken = default)
    {
        if (preBlockCaches is not null)
        {
            if (preBlockCaches.ClearCaches())
            {
                if (_logger.IsWarn) _logger.Warn("Caches are not empty. Clearing them.");
            }

            var physicalCoreCount = RuntimeInformation.PhysicalCoreCount;
            if (!IsGenesisBlock(parentStateRoot) && physicalCoreCount > 2 && !cancellationToken.IsCancellationRequested)
            {
                ParallelOptions parallelOptions = new() { MaxDegreeOfParallelism = physicalCoreCount - 1, CancellationToken = cancellationToken };

                // Run address warmer ahead of transactions warmer, but queue to ThreadPool so it doesn't block the txs
                var addressWarmer = new AddressWarmer(parallelOptions, suggestedBlock, parentStateRoot, systemTxAccessList, this);
                ThreadPool.UnsafeQueueUserWorkItem(addressWarmer, preferLocal: false);
                // Do not pass cancellation token to the task, we don't want exceptions to be thrown in main processing thread
                return Task.Run(() => PreWarmCachesParallel(suggestedBlock, parentStateRoot, parallelOptions, addressWarmer, cancellationToken));
            }
        }

        return Task.CompletedTask;
    }

    // Parent state root is null for genesis block
    private static bool IsGenesisBlock(Hash256? parentStateRoot) => parentStateRoot is null;

    public bool ClearCaches() => preBlockCaches?.ClearCaches() ?? false;

    private void PreWarmCachesParallel(Block suggestedBlock, Hash256 parentStateRoot, ParallelOptions parallelOptions, AddressWarmer addressWarmer, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;

        try
        {
            if (_logger.IsDebug) _logger.Debug($"Started pre-warming caches for block {suggestedBlock.Number}.");

            IReleaseSpec spec = specProvider.GetSpec(suggestedBlock.Header);
            WarmupTransactions(parallelOptions, spec, suggestedBlock, parentStateRoot);
            WarmupWithdrawals(parallelOptions, spec, suggestedBlock, parentStateRoot);

            if (_logger.IsDebug) _logger.Debug($"Finished pre-warming caches for block {suggestedBlock.Number}.");
        }
        catch (OperationCanceledException)
        {
            if (_logger.IsDebug) _logger.Debug($"Pre-warming caches cancelled for block {suggestedBlock.Number}.");
        }
        finally
        {
            // Don't compete task until address warmer is also done.
            addressWarmer.Wait();
        }
    }

    private void WarmupWithdrawals(ParallelOptions parallelOptions, IReleaseSpec spec, Block block, Hash256 stateRoot)
    {
        if (parallelOptions.CancellationToken.IsCancellationRequested) return;
        if (spec.WithdrawalsEnabled && block.Withdrawals is not null)
        {
            int progress = 0;
            Parallel.For(0, block.Withdrawals.Length, parallelOptions,
                _ =>
                {
                    IReadOnlyTxProcessorSource env = _envPool.Get();
                    int i = 0;
                    try
                    {
                        using IReadOnlyTxProcessingScope scope = env.Build(stateRoot);
                        // Process withdrawals in sequential order, rather than partitioning scheme from Parallel.For
                        // Interlocked.Increment returns the incremented value, so subtract 1 to start at 0
                        i = Interlocked.Increment(ref progress) - 1;
                        scope.WorldState.WarmUp(block.Withdrawals[i].Address);
                    }
                    catch (Exception ex)
                    {
                        if (_logger.IsDebug) _logger.Error($"Error pre-warming withdrawal {i}", ex);
                    }
                    finally
                    {
                        _envPool.Return(env);
                    }
                });
        }
    }

    private void WarmupTransactions(ParallelOptions parallelOptions, IReleaseSpec spec, Block block, Hash256 stateRoot)
    {
        if (parallelOptions.CancellationToken.IsCancellationRequested) return;

        Transaction[] txs = block.Transactions;
        // Group tx by sender address so nonces are correct (e.g. contract deployment)
        List<IGrouping<Address, (Transaction, int)>> txGroups = [.. txs
            .Select((tx, index) => (tx, index))
            .GroupBy(indexedTx => indexedTx.tx.SenderAddress)
            .OrderBy(g => g.Min(itx => itx.index))];

        int progress = 0;
        Parallel.For(0, txGroups.Count, parallelOptions, _ =>
        {
            using ThreadExtensions.Disposable handle = Thread.CurrentThread.BoostPriority();
            IReadOnlyTxProcessorSource env = _envPool.Get();
            SystemTransaction systemTransaction = _systemTransactionPool.Get();
            try
            {
                // Process transactions in sequential order, rather than partitioning scheme from Parallel.For
                // Interlocked.Increment returns the incremented value, so subtract 1 to start at 0
                int groupId = Interlocked.Increment(ref progress) - 1;
                // If the transaction has already been processed or being processed, exit early
                IGrouping<Address, (Transaction, int)> group = txGroups[groupId];

                // Skip if main processing is ahead of last tx in group
                if (block.TransactionProcessed > group.Last().Item2) return;

                using IReadOnlyTxProcessingScope scope = env.Build(stateRoot);
                foreach ((Transaction tx, int i) in group)
                {
                    tx.CopyTo(systemTransaction);
                    RunTransaction(spec, block, systemTransaction, scope, i);
                }
            }
            catch (Exception ex)
            {
                if (_logger.IsDebug) _logger.Error($"Error pre-warming cache {systemTransaction?.Hash}", ex);
            }
            finally
            {
                _systemTransactionPool.Return(systemTransaction);
                _envPool.Return(env);
            }
        });
    }

    private void RunTransaction(IReleaseSpec spec, Block block, SystemTransaction tx, IReadOnlyTxProcessingScope scope, int i)
    {
        if (spec.UseTxAccessLists)
        {
            scope.WorldState.WarmUp(tx.AccessList); // eip-2930
        }
        TransactionResult result = scope.TransactionProcessor.Trace(tx, new BlockExecutionContext(block.Header.Clone()), NullTxTracer.Instance);

        if (_logger.IsTrace) _logger.Trace($"Finished pre-warming cache for tx[{i}] {tx.Hash} with {result}");
    }

    private class AddressWarmer(ParallelOptions parallelOptions, Block block, Hash256 stateRoot, AccessList? systemTxAccessList, BlockCachePreWarmer preWarmer)
        : IThreadPoolWorkItem
    {
        private readonly Block Block = block;
        private readonly Hash256 StateRoot = stateRoot;
        private readonly BlockCachePreWarmer PreWarmer = preWarmer;
        private readonly AccessList? SystemTxAccessList = systemTxAccessList;
        private readonly ManualResetEventSlim _doneEvent = new(initialState: false);

        public void Wait() => _doneEvent.Wait();

        void IThreadPoolWorkItem.Execute()
        {
            IReadOnlyTxProcessorSource env = null;
            try
            {
                if (parallelOptions.CancellationToken.IsCancellationRequested) return;
                env = PreWarmer._envPool.Get();
                using IReadOnlyTxProcessingScope scope = env.Build(StateRoot);
                WarmupAddresses(parallelOptions, Block, scope);
            }
            catch (Exception ex)
            {
                if (PreWarmer._logger.IsDebug) PreWarmer._logger.Error($"Error pre-warming addresses", ex);
            }
            finally
            {
                if (env is not null) PreWarmer._envPool.Return(env);
                _doneEvent.Set();
            }
        }

        private void WarmupAddresses(ParallelOptions parallelOptions, Block block, IReadOnlyTxProcessingScope scope)
        {
            if (parallelOptions.CancellationToken.IsCancellationRequested) return;

            if (SystemTxAccessList is not null)
            {
                scope.WorldState.WarmUp(SystemTxAccessList);
            }

            int progress = 0;
            Parallel.For(0, block.Transactions.Length, parallelOptions,
            _ =>
            {
                int i = 0;
                try
                {
                    // Process addresses in sequential order, rather than partitioning scheme from Parallel.For
                    // Interlocked.Increment returns the incremented value, so subtract 1 to start at 0
                    i = Interlocked.Increment(ref progress) - 1;
                    Transaction tx = block.Transactions[i];
                    Address? sender = tx.SenderAddress;
                    if (sender is not null)
                    {
                        scope.WorldState.WarmUp(sender);
                    }
                    Address to = tx.To;
                    if (to is not null)
                    {
                        scope.WorldState.WarmUp(to);
                    }
                }
                catch (Exception ex)
                {
                    if (PreWarmer._logger.IsDebug) PreWarmer._logger.Error($"Error pre-warming addresses {i}", ex);
                }
            });
        }
    }

    private class ReadOnlyTxProcessingEnvPooledObjectPolicy(ReadOnlyTxProcessingEnvFactory envFactory) : IPooledObjectPolicy<IReadOnlyTxProcessorSource>
    {
        public IReadOnlyTxProcessorSource Create() => envFactory.Create();
        public bool Return(IReadOnlyTxProcessorSource obj) => true;
    }
}
