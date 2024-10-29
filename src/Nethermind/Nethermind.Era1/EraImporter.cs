// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Concurrent;
using System.IO.Abstractions;
using Autofac.Features.AttributeFilters;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Receipts;
using Nethermind.Blockchain.Synchronization;
using Nethermind.Consensus.Validators;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Specs;
using Nethermind.Db;
using Nethermind.Logging;

namespace Nethermind.Era1;
public class EraImporter : IEraImporter
{
    private readonly IFileSystem _fileSystem;
    private readonly IBlockTree _blockTree;
    private readonly IBlockValidator _blockValidator;
    private readonly IReceiptStorage _receiptStorage;
    private readonly ISpecProvider _specProvider;
    private readonly string _networkName;
    private readonly ILogger _logger;
    private readonly int _maxEra1Size;
    private readonly ITunableDb _blocksDb;
    private readonly ITunableDb _receiptsDb;
    private readonly ISyncConfig _syncConfig;

    public EraImporter(
        IFileSystem fileSystem,
        IBlockTree blockTree,
        IBlockValidator blockValidator,
        IReceiptStorage receiptStorage,
        ISpecProvider specProvider,
        ILogManager logManager,
        IEraConfig eraConfig,
        ISyncConfig syncConfig,
        [KeyFilter(DbNames.Blocks)] ITunableDb blocksDb,
        [KeyFilter(DbNames.Receipts)] ITunableDb receiptsDb,
        [KeyFilter(EraComponentKeys.NetworkName)] string networkName)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _blockTree = blockTree;
        _blockValidator = blockValidator;
        _receiptStorage = receiptStorage ?? throw new ArgumentNullException(nameof(receiptStorage));
        _specProvider = specProvider ?? throw new ArgumentNullException(nameof(specProvider));
        _receiptsDb = receiptsDb;
        _blocksDb = blocksDb;
        _logger = logManager.GetClassLogger<EraImporter>();
        if (string.IsNullOrWhiteSpace(networkName)) throw new ArgumentException("Cannot be null or whitespace.", nameof(specProvider));
        _networkName = networkName.Trim().ToLower();
        _maxEra1Size = eraConfig.MaxEra1Size;
        _syncConfig = syncConfig;
    }

    public async Task Import(string src, long start, long end, string? accumulatorFile, CancellationToken cancellation = default)
    {
        _receiptsDb.Tune(ITunableDb.TuneType.HeavyWrite);
        _blocksDb.Tune(ITunableDb.TuneType.HeavyWrite);
        try
        {
            await ImportInternal(src, start, end, accumulatorFile, true, cancellation);
        }
        finally
        {
            _receiptsDb.Tune(ITunableDb.TuneType.Default);
            _blocksDb.Tune(ITunableDb.TuneType.Default);
        }
    }

    private async Task ImportInternal(
        string src,
        long startNumber,
        long end,
        string? accumulatorFile,
        bool processBlock,
        CancellationToken cancellation)
    {
        if (!_fileSystem.Directory.Exists(src))
        {
            throw new EraImportException($"The directory given for import '{src}' does not exist.");
        }

        HashSet<ValueHash256>? trustedAccumulators = null;
        if (accumulatorFile != null)
        {
            string[] lines = await _fileSystem.File.ReadAllLinesAsync(accumulatorFile, cancellation);
            trustedAccumulators = lines.Select(s => new ValueHash256(s)).ToHashSet();
        }
        using EraStore eraStore = new(src, trustedAccumulators, _specProvider, _networkName, _fileSystem, _maxEra1Size);

        long lastBlockInStore = eraStore.LastBlock;
        if (end == 0) end = long.MaxValue;
        if (end != long.MaxValue && lastBlockInStore < end)
        {
            throw new EraImportException($"The directory given for import '{src}' have highest block number {lastBlockInStore} which is lower then last requested block {end}.");
        }
        if (end == long.MaxValue)
        {
            end = lastBlockInStore;
        }

        if (_logger.IsInfo) _logger.Info($"Starting history import from {startNumber} to {end}");

        DateTime lastProgress = DateTime.Now;
        DateTime startTime = DateTime.Now;
        TimeSpan elapsed = TimeSpan.Zero;
        long totalblocks = end - startNumber + 1;
        long blocksProcessed = 0;
        long blocksProcessedAtLastLog = 0;

        using BlockTreeSuggestPacer pacer = new BlockTreeSuggestPacer(_blockTree);
        long blockNumber = startNumber;

        long suggestFromBlock = (_blockTree.Head?.Number ?? 0) + 1;
        if (_syncConfig.FastSync && suggestFromBlock == 1)
        {
            // Its syncing right now. So no state.
            suggestFromBlock = long.MaxValue;
        }

        // I wish I could say that EraStore can be run used in parallel in any way you like but I could not make it so.
        // This make the `blockNumber` aligned to era file boundary so that when running parallel, each thread does not
        // work on the same era file as other thread.
        long nextEraStart = eraStore.NextEraStart(blockNumber);
        if (nextEraStart <= end)
        {
            for (; blockNumber < nextEraStart; blockNumber++)
            {
                await ImportBlock(blockNumber);
            }
        }

        // Earlier part can be parallelized
        long partitionSize = _maxEra1Size;
        if (startNumber + partitionSize < suggestFromBlock)
        {
            ConcurrentQueue<long> partitionStartBlocks = new ConcurrentQueue<long>();
            for (; blockNumber + partitionSize < suggestFromBlock && blockNumber + partitionSize < end; blockNumber += partitionSize)
            {
                partitionStartBlocks.Enqueue(blockNumber);
            }

            Task[] importTasks = Enumerable.Range(0, 8).Select((_) =>
            {
                return Task.Run(async () =>
                {
                    while (partitionStartBlocks.TryDequeue(out long partitionStartBlock))
                    {
                        for (long i = 0; i < partitionSize; i++)
                        {
                            await ImportBlock(i + partitionStartBlock);
                        }
                    }
                });
            }).ToArray();

            await Task.WhenAll(importTasks);
        }

        for (; blockNumber <= end; blockNumber++)
        {
            await ImportBlock(blockNumber);
        }
        elapsed = DateTime.Now.Subtract(lastProgress);
        LogImportProgress(DateTime.Now.Subtract(startTime), blocksProcessedAtLastLog, elapsed, blocksProcessed, totalblocks);

        if (_logger.IsInfo) _logger.Info($"Finished history import from {startNumber} to {end}");

        async Task ImportBlock(long blockNumber)
        {
            cancellation.ThrowIfCancellationRequested();

            (Block? b, TxReceipt[]? r) = await eraStore.FindBlockAndReceipts(blockNumber, cancellation: cancellation);
            if (b is null)
            {
                throw new EraImportException($"Unable to find block info for block {blockNumber}");
            }
            if (r is null)
            {
                throw new EraImportException($"Unable to find receipt for block {blockNumber}");
            }

            if (b.IsGenesis)
            {
                return;
            }

            if (b.IsBodyMissing)
            {
                throw new EraImportException($"Unexpected block without a body found for block number {blockNumber}. Archive might be corrupted.");
            }

            if (processBlock && suggestFromBlock <= b.Number)
            {
                await pacer.WaitForQueue(b.Number, cancellation);
                await SuggestAndProcessBlock(b);
            }
            else
                InsertBlockAndReceipts(b, r);

            blocksProcessed++;
            if (blocksProcessed % 10000 == 0)
            {
                elapsed = DateTime.Now.Subtract(lastProgress);
                LogImportProgress(DateTime.Now.Subtract(startTime), blocksProcessed - blocksProcessedAtLastLog, elapsed, blocksProcessed, totalblocks);
                lastProgress = DateTime.Now;
                blocksProcessedAtLastLog = blocksProcessed;
            }
        }
    }

    private void LogImportProgress(
        TimeSpan elapsed,
        long blocksProcessedSinceLast,
        TimeSpan elapsedSinceLastLog,
        long totalBlocksProcessed,
        long totalBlocks)
    {
        if (_logger.IsInfo)
            _logger.Info($"Import progress: | {totalBlocksProcessed,10}/{totalBlocks} blocks  | elapsed {elapsed:hh\\:mm\\:ss} | {blocksProcessedSinceLast / elapsedSinceLastLog.TotalSeconds,10:0.00} Blk/s ");
    }

    private void InsertBlockAndReceipts(Block b, TxReceipt[] r)
    {
        if (_blockTree.FindBlock(b.Number) is null)
            _blockTree.Insert(b, BlockTreeInsertBlockOptions.SaveHeader | BlockTreeInsertBlockOptions.SkipCanAcceptNewBlocks, bodiesWriteFlags: WriteFlags.DisableWAL);
        if (!_receiptStorage.HasBlock(b.Number, b.Hash!))
            _receiptStorage.Insert(b, r);
    }

    private async Task SuggestAndProcessBlock(Block block)
    {
        // Well... this is weird
        block.Header.TotalDifficulty = null;

        if (!_blockValidator.ValidateSuggestedBlock(block, out string? error))
        {
            throw new EraImportException($"Invalid block in Era1 archive. {error}");
        }

        var addResult = await _blockTree.SuggestBlockAsync(block, BlockTreeSuggestOptions.ShouldProcess);
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
                // Hmm... this is weird. Could be beacon body. In any the head should be before this block
                // so it should get to this block eventually.
                break;
            default:
                throw new NotSupportedException($"Not supported value of {nameof(AddBlockResult)} = {addResult}");
        }
    }
}
