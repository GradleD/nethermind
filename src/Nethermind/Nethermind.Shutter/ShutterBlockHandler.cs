// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using Nethermind.Consensus.Processing;
using Nethermind.Core;
using Nethermind.Evm.TransactionProcessing;
using Nethermind.Shutter.Contracts;
using Nethermind.Logging;
using Nethermind.Abi;
using Nethermind.Blockchain.Receipts;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;
using Nethermind.Core.Caching;
using Nethermind.Core.Crypto;
using Nethermind.Blockchain;
using Nethermind.Core.Collections;
using Nethermind.Shutter.Config;

namespace Nethermind.Shutter;

public class ShutterBlockHandler : IShutterBlockHandler
{
    private readonly ILogger _logger;
    private readonly ShutterTime _time;
    private readonly IShutterEon _eon;
    private readonly IReceiptFinder _receiptFinder;
    private readonly ShutterTxLoader _txLoader;
    private readonly Dictionary<ulong, byte[]> _validatorsInfo;
    private readonly ILogManager _logManager;
    private readonly IAbiEncoder _abiEncoder;
    private readonly IBlockTree _blockTree;
    private readonly ReadOnlyBlockTree _readOnlyBlockTree;
    private readonly ulong _chainId;
    private readonly IShutterConfig _cfg;
    private readonly TimeSpan _slotLength;
    private readonly TimeSpan _blockWaitCutoff;
    private readonly ReadOnlyTxProcessingEnvFactory _envFactory;
    private bool _haveCheckedRegistered = false;
    private readonly ConcurrentDictionary<ulong, BlockWaitTask> _blockWaitTasks = new();
    private readonly LruCache<ulong, Hash256?> _slotToBlockHash = new(5, "Slot to block hash mapping");
    private readonly object _syncObject = new();

    public ShutterBlockHandler(
        ulong chainId,
        IShutterConfig cfg,
        ReadOnlyTxProcessingEnvFactory envFactory,
        IBlockTree blockTree,
        IAbiEncoder abiEncoder,
        IReceiptFinder receiptFinder,
        Dictionary<ulong, byte[]> validatorsInfo,
        IShutterEon eon,
        ShutterTxLoader txLoader,
        ShutterTime time,
        ILogManager logManager,
        TimeSpan slotLength,
        TimeSpan blockWaitCutoff)
    {
        _chainId = chainId;
        _cfg = cfg;
        _logger = logManager.GetClassLogger();
        _time = time;
        _validatorsInfo = validatorsInfo;
        _eon = eon;
        _receiptFinder = receiptFinder;
        _txLoader = txLoader;
        _blockTree = blockTree;
        _readOnlyBlockTree = blockTree.AsReadOnly();
        _abiEncoder = abiEncoder;
        _logManager = logManager;
        _envFactory = envFactory;
        _slotLength = slotLength;
        _blockWaitCutoff = blockWaitCutoff;

        _blockTree.NewHeadBlock += OnNewHeadBlock;
        if (_logger.IsInfo) _logger.Info($"Shutter registered block handler.");
    }

    private void OnNewHeadBlock(object? _, BlockEventArgs e)
    {
        Block head = e.Block;
        if (_time.IsBlockUpToDate(head))
        {
            if (_logger.IsInfo) _logger.Info($"Shutter block handler {head.Number}");

            if (!_haveCheckedRegistered)
            {
                CheckAllValidatorsRegistered(head.Header, _validatorsInfo);
                _haveCheckedRegistered = true;
            }
            _eon.Update(head.Header);
            _txLoader.LoadFromReceipts(head, _receiptFinder.Get(head), _eon.GetCurrentEonInfo()!.Value.Eon);

            lock (_syncObject)
            {
                ulong slot = _time.GetSlot(head.Timestamp * 1000);
                _slotToBlockHash.Set(slot, head.Hash);

                if (_blockWaitTasks.Remove(slot, out BlockWaitTask waitTask))
                {
                    waitTask.Tcs.TrySetResult(head);
                    waitTask.Dispose();
                }
            }
        }
        else if (_logger.IsInfo)
        {
            _logger.Warn($"Shutter block handler not running, outdated block {head.Number}");
        }
    }

    public async Task<Block?> WaitForBlockInSlot(ulong slot, CancellationToken cancellationToken)
    {
        TaskCompletionSource<Block?>? tcs = null;
        lock (_syncObject)
        {
            if (_slotToBlockHash.TryGet(slot, out Hash256? blockHash))
            {
                return _readOnlyBlockTree.FindBlock(blockHash!, BlockTreeLookupOptions.None);
            }

            if (_logger.IsInfo) _logger.Info($"Waiting for block in {slot} to get Shutter transactions.");

            long offset = _time.GetCurrentOffsetMs(slot);
            long waitTime = (long)_blockWaitCutoff.TotalMilliseconds - offset;
            if (waitTime <= 0)
            {
                if (_logger.IsInfo) _logger.Info($"Shutter no longer waiting for block in slot {slot}, offset of {offset}ms is after cutoff of {(int)_blockWaitCutoff.TotalMilliseconds}ms.");
                return null;
            }
            waitTime = Math.Min(waitTime, 2 * (long)_slotLength.TotalMilliseconds);

            var timeoutSource = new CancellationTokenSource((int)waitTime);
            var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
            CancellationTokenRegistration ctr = source.Token.Register(() => CancelWaitForBlock(slot));
            tcs = new();
            _blockWaitTasks.GetOrAdd(slot, _ => new()
            {
                Tcs = tcs,
                TimeoutSource = timeoutSource,
                LinkedSource = source,
                CancellationRegistration = ctr
            });
        }
        return await tcs.Task;
    }

    private void CancelWaitForBlock(ulong slot)
    {
        _blockWaitTasks.Remove(slot, out BlockWaitTask cancelledWaitTask);
        cancelledWaitTask.Tcs.TrySetResult(null);
        cancelledWaitTask.Dispose();
    }

    private void CheckAllValidatorsRegistered(BlockHeader parent, Dictionary<ulong, byte[]> validatorsInfo)
    {
        if (validatorsInfo.Count == 0)
        {
            return;
        }

        IReadOnlyTxProcessingScope scope = _envFactory.Create().Build(parent.StateRoot!);
        ITransactionProcessor processor = scope.TransactionProcessor;

        ValidatorRegistryContract validatorRegistryContract = new(processor, _abiEncoder, new(_cfg.ValidatorRegistryContractAddress!), _logManager, _chainId, _cfg.ValidatorRegistryMessageVersion!);
        if (validatorRegistryContract.IsRegistered(parent, validatorsInfo, out HashSet<ulong> unregistered))
        {
            if (_logger.IsInfo) _logger.Info($"All Shutter validator keys are registered.");
        }
        else if (_logger.IsError)
        {
            _logger.Error($"Validators not registered to Shutter with the following indices: [{string.Join(", ", unregistered)}]");
        }
    }

    public void Dispose()
    {
        if (_logger.IsInfo) _logger.Info($"Shutter deregistered block handler.");
        _blockTree.NewHeadBlock -= OnNewHeadBlock;
        _blockWaitTasks.ForEach(x => x.Value.Dispose());
    }

    private readonly struct BlockWaitTask : IDisposable
    {
        public TaskCompletionSource<Block?> Tcs { get; init; }
        public CancellationTokenSource TimeoutSource { get; init; }
        public CancellationTokenSource LinkedSource { get; init; }
        public CancellationTokenRegistration CancellationRegistration { get; init; }

        public void Dispose()
        {
            TimeoutSource.Dispose();
            LinkedSource.Dispose();
            CancellationRegistration.Dispose();
        }
    }
}
