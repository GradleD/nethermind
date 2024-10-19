// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.IO.Abstractions;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Receipts;
using Nethermind.Consensus.Validators;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Specs;
using Nethermind.Int256;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Nethermind.Blockchain.Era1;
using Nethermind.Era1;
using Nethermind.Serialization.Rlp;
using Nethermind.State.Proofs;
using Nethermind.Core.Extensions;
using Nethermind.Logging;

namespace Nethermind.JsonRpc.Modules;
public class EraImporter : IEraImporter
{
    private const int MergeBlock = 15537393;
    private readonly IFileSystem _fileSystem;
    private readonly IBlockTree _blockTree;
    private readonly IBlockValidator _blockValidator;
    private readonly IReceiptStorage _receiptStorage;
    private readonly ISpecProvider _specProvider;
    private readonly int _epochSize;
    private readonly string _networkName;
    private readonly ReceiptMessageDecoder _receiptDecoder;
    private readonly ILogger _logger;

    public TimeSpan ProgressInterval { get; set; } = TimeSpan.FromSeconds(10);

    public EraImporter(
        IFileSystem fileSystem,
        IBlockTree blockTree,
        IBlockValidator blockValidator,
        IReceiptStorage receiptStorage,
        ISpecProvider specProvider,
        ILogManager logManager,
        string networkName,
        int epochSize = EraWriter.MaxEra1Size)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _blockTree = blockTree;
        _blockValidator = blockValidator;
        _receiptStorage = receiptStorage ?? throw new ArgumentNullException(nameof(receiptStorage));
        _specProvider = specProvider ?? throw new ArgumentNullException(nameof(specProvider));
        _receiptDecoder = new();
        _logger = logManager.GetClassLogger<EraImporter>();
        this._epochSize = epochSize;
        if (string.IsNullOrWhiteSpace(networkName)) throw new ArgumentException("Cannot be null or whitespace.", nameof(specProvider));
        _networkName = networkName.Trim().ToLower();
    }

    public async Task Import(string src, long start, long end, string? accumulatorFile, CancellationToken cancellation = default)
    {
        // TODO: End not handled missing

        if (_logger.IsInfo) _logger.Info($"Starting history import from {start} to {end}");
        if (!string.IsNullOrEmpty(accumulatorFile))
        {
            await VerifyEraFiles(src, accumulatorFile, cancellation);
        }
        await ImportInternal(src, start, false, cancellation);

        if (_logger.IsInfo) _logger.Info($"Finished history import from {start} to {end}");
    }

    public Task ImportAsArchiveSync(string src, CancellationToken cancellation)
    {
        return ImportInternal(src, _blockTree.Head?.Number + 1 ?? 0, true, cancellation);
    }

    private async Task ImportInternal(
        string src,
        long startNumber,
        bool processBlock,
        CancellationToken cancellation)
    {
        EraStore eraStore = new(src, _networkName, _fileSystem);

        long startEpoch = startNumber / _epochSize;

        if (!eraStore.HasEpoch(startEpoch))
        {
            throw new EraImportException($"No {_networkName} epochs found for block {startNumber} in '{src}'");
        }

        DateTime lastProgress = DateTime.Now;
        long epochProcessed = 0;
        DateTime startTime = DateTime.Now;
        long txProcessed = 0;
        long totalblocks = 0;
        int blocksProcessed = 0;

        for (long i = startEpoch; eraStore.HasEpoch(i); i++)
        {
            using EraReader eraReader = eraStore.GetReader(i);

            await foreach ((Block b, TxReceipt[] r, UInt256 td) in eraReader)
            {
                cancellation.ThrowIfCancellationRequested();

                if (b.IsGenesis)
                {
                    continue;
                }

                if (b.Number < startNumber)
                {
                    continue;
                }

                if (b.IsBodyMissing)
                {
                    throw new EraImportException($"Unexpected block without a body found in '{eraStore.GetReaderPath(i)}'. Archive might be corrupted.");
                }

                if (processBlock)
                    await SuggestBlock(b, r, processBlock);
                else
                    InsertBlockAndReceipts(b, r);

                blocksProcessed++;
                txProcessed += b.Transactions.Length;
                TimeSpan elapsed = DateTime.Now.Subtract(lastProgress);
                if (elapsed > ProgressInterval)
                {
                    LogImportProgress(DateTime.Now.Subtract(startTime), blocksProcessed, txProcessed, totalblocks, epochProcessed, eraStore.EpochCount);
                    lastProgress = DateTime.Now;
                }
            }
            epochProcessed++;
        }
        LogImportProgress(DateTime.Now.Subtract(startTime), blocksProcessed, txProcessed, totalblocks, epochProcessed, eraStore.EpochCount);
    }

    private void LogImportProgress(
        TimeSpan elapsed,
        long totalBlocksProcessed,
        long txProcessed,
        long totalBlocks,
        long epochProcessed,
        long totalEpochs)
    {
        if (_logger.IsInfo)
            _logger.Info($"Import progress: | {totalBlocksProcessed,10}/{totalBlocks} blocks  |  {epochProcessed}/{totalEpochs} epochs  |  elapsed {elapsed:hh\\:mm\\:ss}");
    }

    private void InsertBlockAndReceipts(Block b, TxReceipt[] r)
    {
        _blockTree.Insert(b, BlockTreeInsertBlockOptions.SaveHeader | BlockTreeInsertBlockOptions.SkipCanAcceptNewBlocks, bodiesWriteFlags: WriteFlags.DisableWAL);
        _receiptStorage.Insert(b, r);
    }

    private async Task SuggestBlock(Block block, TxReceipt[] receipts, bool processBlock)
    {
        if (!_blockValidator.ValidateSuggestedBlock(block, out string? error))
        {
            throw new EraImportException($"Invalid block in Era1 archive. {error}");
        }

        var options = processBlock ? BlockTreeSuggestOptions.ShouldProcess : BlockTreeSuggestOptions.None;
        var addResult = await _blockTree.SuggestBlockAsync(block, options);
        switch (addResult)
        {
            case AddBlockResult.AlreadyKnown:
                return;
            case AddBlockResult.CannotAccept:
                throw new EraImportException("Rejected block in Era1 archive");
            case AddBlockResult.UnknownParent:
                throw new EraImportException("Unknown parent for block in Era1 archive");
            case AddBlockResult.InvalidBlock:
                throw new EraImportException("Invalid block in Era1 archive");
            case AddBlockResult.Added:
                if (!processBlock) _receiptStorage.Insert(block, receipts);
                break;
            default:
                throw new NotSupportedException($"Not supported value of {nameof(AddBlockResult)} = {addResult}");
        }
    }


    /// <summary>
    /// Verifies all era1 archives from a directory, with an expected accumulator list from a hex encoded file.
    /// </summary>
    /// <param name="eraDirectory"></param>
    /// <param name="accumulatorFile"></param>
    /// <param name="cancellation"></param>
    /// <exception cref="EraVerificationException">If the verification fails.</exception>
    public async Task VerifyEraFiles(string eraDirectory, string accumulatorFile, CancellationToken cancellation = default)
    {
        if (!_fileSystem.Directory.Exists(eraDirectory))
            throw new EraImportException($"Directory does not exist '{eraDirectory}'");
        if (!_fileSystem.File.Exists(accumulatorFile))
            throw new EraImportException($"Accumulator file does not exist '{accumulatorFile}'");

        var eraStore = new EraStore(eraDirectory, _networkName, _fileSystem);

        string[] lines = await _fileSystem.File.ReadAllLinesAsync(accumulatorFile, cancellation);
        var accumulators = lines.Select(s => new ValueHash256(s)).ToHashSet();
        await eraStore.VerifyAll(_specProvider, cancellation, accumulators, LogVerificationProgress);
    }

    private void ValidateReceipts(Block block, TxReceipt[] blockReceipts)
    {
        Hash256 receiptsRoot = new ReceiptTrie<TxReceipt>(_specProvider.GetSpec(block.Header), blockReceipts, _receiptDecoder).RootHash;

        if (receiptsRoot != block.ReceiptsRoot)
        {
            throw new EraImportException($"Wrong receipts root in Era1 archive for block {block.ToString(Block.Format.Short)}.");
        }
    }

    private void LogVerificationProgress(VerificationProgressArgs args)
    {
        if (_logger.IsInfo)
            _logger.Info($"Verification progress: {args.Processed,10}/{args.TotalToProcess} archives  |  elapsed {args.Elapsed:hh\\:mm\\:ss}  |  {args.Processed / args.Elapsed.TotalSeconds,10:0.00} archives/s");
    }
}
