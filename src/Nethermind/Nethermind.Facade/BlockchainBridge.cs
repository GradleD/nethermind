// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Filters;
using Nethermind.Blockchain.Find;
using Nethermind.Blockchain.Receipts;
using Nethermind.Core;
using Nethermind.Core.Attributes;
using Nethermind.Core.Crypto;
using Nethermind.Crypto;
using Nethermind.Int256;
using Nethermind.Evm;
using Nethermind.Evm.Tracing;
using Nethermind.Trie;
using Nethermind.TxPool;
using Block = Nethermind.Core.Block;
using System.Threading;
using Nethermind.Consensus.Processing;
using Nethermind.Core.Eip2930;
using Nethermind.Core.Specs;
using Nethermind.Evm.TransactionProcessing;
using Nethermind.Facade.Filters;
using Nethermind.State;
using Nethermind.Core.Extensions;
using Nethermind.Config;
using Nethermind.Evm.CodeAnalysis;
using Nethermind.Facade.Proxy.Models.MultiCall;
using System.Transactions;
using Microsoft.CSharp.RuntimeBinder;
using Transaction = Nethermind.Core.Transaction;
using Nethermind.Specs;

namespace Nethermind.Facade
{
    public interface IBlockchainBridgeFactory
    {
        IBlockchainBridge CreateBlockchainBridge();
    }

    [Todo(Improve.Refactor, "I want to remove BlockchainBridge, split it into something with logging, state and tx processing. Then we can start using independent modules.")]
    public class BlockchainBridge : IBlockchainBridge
    {
        private readonly ReadOnlyTxProcessingEnv _processingEnv;
        private readonly MultiCallReadOnlyBlocksProcessingEnv _multiCallProcessingEnv;
        private readonly ITxPool _txPool;
        private readonly IFilterStore _filterStore;
        private readonly IEthereumEcdsa _ecdsa;
        private readonly ITimestamper _timestamper;
        private readonly IFilterManager _filterManager;
        private readonly IReceiptFinder _receiptFinder;
        private readonly ILogFinder _logFinder;
        private readonly ISpecProvider _specProvider;
        private readonly IBlocksConfig _blocksConfig;

        public BlockchainBridge(ReadOnlyTxProcessingEnv processingEnv,
            MultiCallReadOnlyBlocksProcessingEnv multiCallProcessingEnv,
            ITxPool? txPool,
            IReceiptFinder? receiptStorage,
            IFilterStore? filterStore,
            IFilterManager? filterManager,
            IEthereumEcdsa? ecdsa,
            ITimestamper? timestamper,
            ILogFinder? logFinder,
            ISpecProvider specProvider,
            IBlocksConfig blocksConfig,
            bool isMining)
        {
            _processingEnv = processingEnv ?? throw new ArgumentNullException(nameof(processingEnv));
            _multiCallProcessingEnv = multiCallProcessingEnv ?? throw new ArgumentNullException(nameof(multiCallProcessingEnv));
            _txPool = txPool ?? throw new ArgumentNullException(nameof(_txPool));
            _receiptFinder = receiptStorage ?? throw new ArgumentNullException(nameof(receiptStorage));
            _filterStore = filterStore ?? throw new ArgumentNullException(nameof(filterStore));
            _filterManager = filterManager ?? throw new ArgumentNullException(nameof(filterManager));
            _ecdsa = ecdsa ?? throw new ArgumentNullException(nameof(ecdsa));
            _timestamper = timestamper ?? throw new ArgumentNullException(nameof(timestamper));
            _logFinder = logFinder ?? throw new ArgumentNullException(nameof(logFinder));
            _specProvider = specProvider ?? throw new ArgumentNullException(nameof(specProvider));
            _blocksConfig = blocksConfig;
            IsMining = isMining;
        }

        public Block? HeadBlock
        {
            get
            {
                return _processingEnv.BlockTree.Head;
            }
        }

        public bool IsMining { get; }

        public (TxReceipt? Receipt, TxGasInfo? GasInfo, int LogIndexStart) GetReceiptAndGasInfo(Keccak txHash)
        {
            Keccak blockHash = _receiptFinder.FindBlockHash(txHash);
            if (blockHash is not null)
            {
                Block? block = _processingEnv.BlockTree.FindBlock(blockHash, BlockTreeLookupOptions.RequireCanonical);
                if (block is not null)
                {
                    TxReceipt[] txReceipts = _receiptFinder.Get(block);
                    TxReceipt txReceipt = txReceipts.ForTransaction(txHash);
                    int logIndexStart = txReceipts.GetBlockLogFirstIndex(txReceipt.Index);
                    Transaction tx = block.Transactions[txReceipt.Index];
                    bool is1559Enabled = _specProvider.GetSpecFor1559(block.Number).IsEip1559Enabled;
                    return (txReceipt, tx.GetGasInfo(is1559Enabled, block.Header), logIndexStart);
                }
            }

            return (null, null, 0);
        }

        public (TxReceipt? Receipt, Transaction Transaction, UInt256? baseFee) GetTransaction(Keccak txHash)
        {
            Keccak blockHash = _receiptFinder.FindBlockHash(txHash);
            if (blockHash is not null)
            {
                Block block = _processingEnv.BlockTree.FindBlock(blockHash, BlockTreeLookupOptions.TotalDifficultyNotNeeded);
                TxReceipt txReceipt = _receiptFinder.Get(block).ForTransaction(txHash);
                return (txReceipt, block?.Transactions[txReceipt.Index], block?.BaseFeePerGas);
            }

            if (_txPool.TryGetPendingTransaction(txHash, out Transaction? transaction))
            {
                return (null, transaction, null);
            }

            return (null, null, null);
        }

        public TxReceipt? GetReceipt(Keccak txHash)
        {
            Keccak? blockHash = _receiptFinder.FindBlockHash(txHash);
            return blockHash is not null ? _receiptFinder.Get(blockHash).ForTransaction(txHash) : null;
        }


        public class MultiCallOutput
        {
            public string? Error { get; set; }

            public List<MultiCallBlockResult> Items { get; set; }
        }

        public class CallOutput
        {
            public CallOutput()
            {
            }

            public CallOutput(byte[] outputData, long gasSpent, string error, bool inputError = false)
            {
                Error = error;
                OutputData = outputData;
                GasSpent = gasSpent;
                InputError = inputError;
            }

            public string? Error { get; set; }

            public byte[] OutputData { get; set; }

            public long GasSpent { get; set; }

            public bool InputError { get; set; }

            public AccessList? AccessList { get; set; }
        }

        public CallOutput Call(BlockHeader header, Transaction tx, CancellationToken cancellationToken)
        {
            CallOutputTracer callOutputTracer = new();
            (bool Success, string Error) tryCallResult = TryCallAndRestore(header, tx, false,
                callOutputTracer.WithCancellation(cancellationToken));
            return new CallOutput
            {
                Error = tryCallResult.Success ? callOutputTracer.Error : tryCallResult.Error,
                GasSpent = callOutputTracer.GasSpent,
                OutputData = callOutputTracer.ReturnValue,
                InputError = !tryCallResult.Success
            };
        }



        public MultiCallOutput MultiCall(BlockHeader header, MultiCallPayload<Transaction> payload, CancellationToken cancellationToken)
        {
            MultiCallBlockTracer multiCallOutputTracer = new();
            MultiCallOutput result = new();
            try
            {
                (bool Success, string Error) tryMultiCallResult = TryMultiCallTrace(header, payload,
                    multiCallOutputTracer.WithCancellation(cancellationToken));

                if (!tryMultiCallResult.Success)
                {
                    result.Error = tryMultiCallResult.Error;
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.ToString();
            }

            result.Items = multiCallOutputTracer._results;
            return result;
        }

        public CallOutput EstimateGas(BlockHeader header, Transaction tx, CancellationToken cancellationToken)
        {
            using IReadOnlyTransactionProcessor? readOnlyTransactionProcessor = _processingEnv.Build(header.StateRoot!);

            EstimateGasTracer estimateGasTracer = new();
            (bool Success, string Error) tryCallResult = TryCallAndRestore(
                header,
                tx,
                true,
                estimateGasTracer.WithCancellation(cancellationToken));

            GasEstimator gasEstimator = new(readOnlyTransactionProcessor, _processingEnv.StateProvider,
                _specProvider, _blocksConfig);
            long estimate = gasEstimator.Estimate(tx, header, estimateGasTracer, cancellationToken);

            return new CallOutput
            {
                Error = tryCallResult.Success ? estimateGasTracer.Error : tryCallResult.Error,
                GasSpent = estimate,
                InputError = !tryCallResult.Success
            };
        }

        public CallOutput CreateAccessList(BlockHeader header, Transaction tx, CancellationToken cancellationToken, bool optimize)
        {
            CallOutputTracer callOutputTracer = new();
            AccessTxTracer accessTxTracer = optimize
                ? new(tx.SenderAddress,
                    tx.GetRecipient(tx.IsContractCreation ? _processingEnv.StateReader.GetNonce(header.StateRoot, tx.SenderAddress) : 0))
                : new();

            (bool Success, string Error) tryCallResult = TryCallAndRestore(header, tx, false,
                new CompositeTxTracer(callOutputTracer, accessTxTracer).WithCancellation(cancellationToken));

            return new CallOutput
            {
                Error = tryCallResult.Success ? callOutputTracer.Error : tryCallResult.Error,
                GasSpent = accessTxTracer.GasSpent,
                OutputData = callOutputTracer.ReturnValue,
                InputError = !tryCallResult.Success,
                AccessList = accessTxTracer.AccessList
            };
        }

        private (bool Success, string Error) TryCallAndRestore(
            BlockHeader blockHeader,
            Transaction transaction,
            bool treatBlockHeaderAsParentBlock,
            ITxTracer tracer)
        {
            try
            {
                CallAndRestore(blockHeader, transaction, treatBlockHeaderAsParentBlock, tracer);
                return (true, string.Empty);
            }
            catch (InsufficientBalanceException ex)
            {
                return (false, ex.Message);
            }
        }


        private Dictionary<Address, UInt256> NonceDictionary = new();
        private void CallAndRestore(
            BlockHeader blockHeader,
            Transaction transaction,
            bool treatBlockHeaderAsParentBlock,
            ITxTracer tracer)
        {
            transaction.SenderAddress ??= Address.SystemUser;

            Keccak stateRoot = blockHeader.StateRoot!;
            using IReadOnlyTransactionProcessor transactionProcessor = _processingEnv.Build(stateRoot);

            if (transaction.Nonce == 0)
            {
                transaction.Nonce = GetNonce(stateRoot, transaction.SenderAddress);
            }

            BlockHeader callHeader = treatBlockHeaderAsParentBlock
                ? new(
                    blockHeader.Hash!,
                    Keccak.OfAnEmptySequenceRlp,
                    Address.Zero,
                    UInt256.Zero,
                    blockHeader.Number + 1,
                    blockHeader.GasLimit,
                    Math.Max(blockHeader.Timestamp + _blocksConfig.SecondsPerSlot, _timestamper.UnixTime.Seconds),
                    Array.Empty<byte>())
                : new(
                    blockHeader.ParentHash!,
                    blockHeader.UnclesHash!,
                    blockHeader.Beneficiary!,
                    blockHeader.Difficulty,
                    blockHeader.Number,
                    blockHeader.GasLimit,
                    blockHeader.Timestamp,
                    blockHeader.ExtraData);

            IReleaseSpec releaseSpec = _specProvider.GetSpec(callHeader);
            callHeader.BaseFeePerGas = treatBlockHeaderAsParentBlock
                ? BaseFeeCalculator.Calculate(blockHeader, releaseSpec)
                : blockHeader.BaseFeePerGas;

            if (releaseSpec.IsEip4844Enabled)
            {
                callHeader.BlobGasUsed = BlobGasCalculator.CalculateBlobGas(transaction);
                callHeader.ExcessBlobGas = treatBlockHeaderAsParentBlock
                    ? BlobGasCalculator.CalculateExcessBlobGas(blockHeader, releaseSpec)
                    : blockHeader.ExcessBlobGas;
            }
            callHeader.MixHash = blockHeader.MixHash;
            callHeader.IsPostMerge = blockHeader.Difficulty == 0;
            transaction.Hash = transaction.CalculateHash();
            transactionProcessor.CallAndRestore(transaction, callHeader, tracer);
        }

        private (bool Success, string Error) TryMultiCallTrace(BlockHeader parent, MultiCallPayload<Transaction> payload,
           IBlockTracer tracer)
        {
            using (MultiCallReadOnlyBlocksProcessingEnv? env = _multiCallProcessingEnv.Clone(payload.TraceTransfers))
            {
                var processor = env.GetProcessor();
                var firstBlock = payload.BlockStateCalls.FirstOrDefault();
                var startStateRoot = parent.StateRoot;
                if (firstBlock?.BlockOverrides?.Number != null
                    && firstBlock?.BlockOverrides?.Number > UInt256.Zero
                    && firstBlock?.BlockOverrides?.Number < (ulong)long.MaxValue)
                {
                    BlockHeader? searchResult =
                        _multiCallProcessingEnv.BlockTree.FindHeader((long)firstBlock?.BlockOverrides.Number);
                    if (searchResult != null)
                    {
                        startStateRoot = searchResult.StateRoot;
                    }
                }

                foreach (BlockStateCall<Transaction>? callInputBlock in payload.BlockStateCalls)
                {
                    BlockHeader callHeader = null;
                    if (callInputBlock.BlockOverrides == null)
                    {
                        callHeader = new BlockHeader(
                            parent.Hash,
                            Keccak.OfAnEmptySequenceRlp,
                            Address.Zero,
                            UInt256.Zero,
                            parent.Number + 1,
                            parent.GasLimit,
                            parent.Timestamp + 1,
                            Array.Empty<byte>());
                        callHeader.BaseFeePerGas = BaseFeeCalculator.Calculate(parent, _specProvider.GetSpec(parent));
                    }
                    else
                    {
                        callHeader = callInputBlock.BlockOverrides.GetBlockHeader(parent, _blocksConfig);
                    }

                    env.StateProvider.StateRoot = parent.StateRoot;

                    callHeader.MixHash = parent.MixHash;
                    callHeader.IsPostMerge = parent.Difficulty == 0;
                    Block? currentBlock = new(callHeader, Array.Empty<Transaction>(), Array.Empty<BlockHeader>());

                    var currentSpec = env.SpecProvider.GetSpec(currentBlock.Header);
                    if (callInputBlock.StateOverrides != null)
                    {
                        ModifyAccounts(callInputBlock.StateOverrides, env.StateProvider, currentSpec);
                    }

                    env.StateProvider.Commit(currentSpec);
                    env.StateProvider.CommitTree(currentBlock.Number);
                    env.StateProvider.RecalculateStateRoot();

                    var transactions = callInputBlock.Calls;
                    foreach (Transaction transaction in transactions)
                    {
                        transaction.SenderAddress ??= Address.SystemUser;

                        Keccak stateRoot = callHeader.StateRoot!;

                        if (transaction.Nonce == 0)
                        {
                            try
                            {
                                transaction.Nonce = env.StateProvider.GetAccount(transaction.SenderAddress).Nonce;
                            }
                            catch (TrieException)
                            {
                                // Transaction from unknown account
                            }
                        }

                        transaction.Hash = transaction.CalculateHash();
                    }

                    currentBlock = currentBlock.WithReplacedBody(currentBlock.Body.WithChangedTransactions(transactions.ToArray()));


                    currentBlock.Header.StateRoot = env.StateProvider.StateRoot;
                    currentBlock.Header.IsPostMerge = true; //ToDo: Seal if necessary before merge 192 BPB
                    currentBlock.Header.Hash = currentBlock.Header.CalculateHash();

                    ProcessingOptions processingFlags = ProcessingOptions.ForceProcessing |
                                                        ProcessingOptions.DoNotVerifyNonce |
                                                        ProcessingOptions.IgnoreParentNotOnMainChain |
                                                        ProcessingOptions.MarkAsProcessed |
                                                        ProcessingOptions.StoreReceipts;

                    if (!payload.Validation)
                    {
                        processingFlags |= ProcessingOptions.NoValidation;
                    }

                    Block[]? currentBlocks = processor.Process(env.StateProvider.StateRoot,
                        new List<Block> { currentBlock }, processingFlags, tracer);

                    var processedBlock = currentBlocks.FirstOrDefault();
                    if (processedBlock != null)
                    {
                        parent = processedBlock.Header;
                    }
                    else
                    {
                        return (false, $"Processing failed at block {currentBlock.Number}");
                    }
                }
            }

            return (true, "");
        }

        public ulong GetChainId()
        {
            return _processingEnv.BlockTree.ChainId;
        }

        private UInt256 GetNonce(Keccak stateRoot, Address address)
        {
            UInt256 nonce = 0;
            if (!NonceDictionary.TryGetValue(address, out nonce))
            {
                try
                {
                    nonce = _processingEnv.StateReader.GetNonce(stateRoot, address);
                }
                catch (Exception)
                {
                    // TODO: handle missing state exception, may be account needs to be created
                }

                NonceDictionary[address] = nonce;
            }
            else
            {
                nonce += 1;
                NonceDictionary[address] = nonce;
            }

            return nonce;
        }

        //Apply changes to accounts and contracts states including precompiles
        private void ModifyAccounts(Dictionary<Address, AccountOverride> StateOverrides, IWorldState? StateProvider, IReleaseSpec? CurrentSpec)
        {
            Account? acc;

            foreach (KeyValuePair<Address, AccountOverride> overrideData in StateOverrides)
            {
                Address address = overrideData.Key;
                AccountOverride? accountOverride = overrideData.Value;

                bool accExists = false;
                try
                {
                    accExists = StateProvider.AccountExists(address);
                }
                catch (Exception)
                {
                    // ignored
                }

                UInt256 balance = 0;
                if (accountOverride.Balance != null)
                {
                    balance = accountOverride.Balance.Value;
                }

                UInt256 nonce = 0;
                if (accountOverride.Nonce != null)
                {
                    nonce = accountOverride.Nonce.Value;
                }


                if (!accExists)
                {
                    StateProvider.CreateAccount(address, balance, nonce);
                    acc = StateProvider.GetAccount(address);
                }
                else
                    acc = StateProvider.GetAccount(address);

                UInt256 accBalance = acc.Balance;
                if (accountOverride.Balance != null && accBalance > balance)
                    StateProvider.SubtractFromBalance(address, accBalance - balance, CurrentSpec);

                else if (accountOverride.Balance != null && accBalance < balance)
                    StateProvider.AddToBalance(address, balance - accBalance, CurrentSpec);


                UInt256 accNonce = acc.Nonce;
                if (accountOverride.Nonce != null && accNonce > nonce)
                {
                    UInt256 iters = accNonce - nonce;
                    for (UInt256 i = 0; i < iters; i++)
                    {
                        StateProvider.DecrementNonce(address);
                    }
                }
                else if (accountOverride.Nonce != null && accNonce < accountOverride.Nonce)
                {
                    UInt256 iters = nonce - accNonce;
                    for (UInt256 i = 0; i < iters; i++)
                    {
                        StateProvider.IncrementNonce(address);
                    }
                }

                if (accountOverride.Code != null)
                {
                    _multiCallProcessingEnv.VirtualMachine.SetCodeOverwrite(StateProvider, CurrentSpec, address,
                        new CodeInfo(accountOverride.Code), accountOverride.MovePrecompileToAddress);
                }

                //TODO: discuss if clean slate is a must
                if (accountOverride.State is not null)
                {
                    foreach (KeyValuePair<UInt256, ValueKeccak> storage in accountOverride.State)
                        StateProvider.Set(new StorageCell(address, storage.Key),
                            storage.Value.ToByteArray().WithoutLeadingZeros().ToArray());
                }

                if (accountOverride.StateDiff is not null)
                {
                    foreach (KeyValuePair<UInt256, ValueKeccak> storage in accountOverride.StateDiff)
                        StateProvider.Set(new StorageCell(address, storage.Key),
                            storage.Value.ToByteArray().WithoutLeadingZeros().ToArray());
                }
                StateProvider.Commit(CurrentSpec);
            }
        }

        public bool FilterExists(int filterId) => _filterStore.FilterExists(filterId);
        public FilterType GetFilterType(int filterId) => _filterStore.GetFilterType(filterId);
        public FilterLog[] GetFilterLogs(int filterId) => _filterManager.GetLogs(filterId);

        public IEnumerable<FilterLog> GetLogs(
            BlockParameter fromBlock,
            BlockParameter toBlock,
            object? address = null,
            IEnumerable<object>? topics = null,
            CancellationToken cancellationToken = default)
        {
            LogFilter filter = _filterStore.CreateLogFilter(fromBlock, toBlock, address, topics, false);
            return _logFinder.FindLogs(filter, cancellationToken);
        }

        public bool TryGetLogs(int filterId, out IEnumerable<FilterLog> filterLogs, CancellationToken cancellationToken = default)
        {
            LogFilter? filter;
            filterLogs = null;
            if ((filter = _filterStore.GetFilter<LogFilter>(filterId)) is not null)
                filterLogs = _logFinder.FindLogs(filter, cancellationToken);

            return filter is not null;
        }

        public int NewFilter(BlockParameter? fromBlock, BlockParameter? toBlock,
            object? address = null, IEnumerable<object>? topics = null)
        {
            LogFilter filter = _filterStore.CreateLogFilter(fromBlock ?? BlockParameter.Latest, toBlock ?? BlockParameter.Latest, address, topics);
            _filterStore.SaveFilter(filter);
            return filter.Id;
        }

        public int NewBlockFilter()
        {
            BlockFilter filter = _filterStore.CreateBlockFilter(_processingEnv.BlockTree.Head!.Number);
            _filterStore.SaveFilter(filter);
            return filter.Id;
        }

        public int NewPendingTransactionFilter()
        {
            PendingTransactionFilter filter = _filterStore.CreatePendingTransactionFilter();
            _filterStore.SaveFilter(filter);
            return filter.Id;
        }

        public void UninstallFilter(int filterId) => _filterStore.RemoveFilter(filterId);
        public FilterLog[] GetLogFilterChanges(int filterId) => _filterManager.PollLogs(filterId);
        public Keccak[] GetBlockFilterChanges(int filterId) => _filterManager.PollBlockHashes(filterId);

        public void RecoverTxSenders(Block block)
        {
            TxReceipt[] receipts = _receiptFinder.Get(block);
            if (block.Transactions.Length == receipts.Length)
            {
                for (int i = 0; i < block.Transactions.Length; i++)
                {
                    Transaction transaction = block.Transactions[i];
                    TxReceipt receipt = receipts[i];
                    transaction.SenderAddress ??= receipt.Sender ?? RecoverTxSender(transaction);
                }
            }
            else
            {
                for (int i = 0; i < block.Transactions.Length; i++)
                {
                    Transaction transaction = block.Transactions[i];
                    transaction.SenderAddress ??= RecoverTxSender(transaction);
                }
            }
        }

        public Keccak[] GetPendingTransactionFilterChanges(int filterId) =>
            _filterManager.PollPendingTransactionHashes(filterId);

        public Address? RecoverTxSender(Transaction tx) => _ecdsa.RecoverAddress(tx);

        public void RunTreeVisitor(ITreeVisitor treeVisitor, Keccak stateRoot)
        {
            _processingEnv.StateReader.RunTreeVisitor(treeVisitor, stateRoot);
        }

        public IEnumerable<FilterLog> FindLogs(LogFilter filter, CancellationToken cancellationToken = default)
        {
            return _logFinder.FindLogs(filter, cancellationToken);
        }
    }
}
