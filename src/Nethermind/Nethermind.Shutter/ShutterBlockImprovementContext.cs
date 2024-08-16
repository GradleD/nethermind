// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Consensus;
using Nethermind.Shutter.Config;
using Nethermind.Consensus.Producers;
using Nethermind.Core;
using Nethermind.Core.Extensions;
using Nethermind.Core.Specs;
using Nethermind.Int256;
using Nethermind.Logging;
using Nethermind.Specs;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nethermind.Shutter;

public class ShutterBlockImprovementContextFactory(
    IBlockProducer blockProducer,
    ShutterTxSource shutterTxSource,
    IShutterConfig shutterConfig,
    ISpecProvider spec,
    ILogManager logManager) : IBlockImprovementContextFactory
{
    private readonly ulong _genesisTimestampMs = 1000 * (spec.ChainId == BlockchainIds.Chiado ? ChiadoSpecProvider.BeaconChainGenesisTimestamp : GnosisSpecProvider.BeaconChainGenesisTimestamp);

    public IBlockImprovementContext StartBlockImprovementContext(
        Block currentBestBlock,
        BlockHeader parentHeader,
        PayloadAttributes payloadAttributes,
        DateTimeOffset startDateTime) =>
        new ShutterBlockImprovementContext(blockProducer,
                                           shutterTxSource,
                                           shutterConfig,
                                           currentBestBlock,
                                           parentHeader,
                                           payloadAttributes,
                                           startDateTime,
                                           _genesisTimestampMs,
                                           GnosisSpecProvider.SlotLength,
                                           logManager);
    public bool KeepImproving => false;
}

public class ShutterBlockImprovementContext : IBlockImprovementContext
{
    public Task<Block?> ImprovementTask { get; }

    public Block? CurrentBestBlock { get; private set; }

    public bool Disposed { get; private set; }
    public DateTimeOffset StartDateTime { get; }

    public UInt256 BlockFees => 0;

    private CancellationTokenSource? _cancellationTokenSource;
    private readonly ILogger _logger;
    private readonly IBlockProducer _blockProducer;
    private readonly IShutterTxSignal _txSignal;
    private readonly IShutterConfig _shutterConfig;
    private readonly BlockHeader _parentHeader;
    private readonly PayloadAttributes _payloadAttributes;
    private readonly ulong _slotTimestampMs;
    private readonly ulong _genesisTimestampMs;
    private readonly TimeSpan _slotLength;

    internal ShutterBlockImprovementContext(
        IBlockProducer blockProducer,
        IShutterTxSignal shutterTxSignal,
        IShutterConfig shutterConfig,
        Block currentBestBlock,
        BlockHeader parentHeader,
        PayloadAttributes payloadAttributes,
        DateTimeOffset startDateTime,
        ulong genesisTimestampMs,
        TimeSpan slotLength,
        ILogManager logManager)
    {
        if (slotLength == TimeSpan.Zero)
        {
            throw new ArgumentException("Cannot be zero.", nameof(slotLength));
        }

        _slotTimestampMs = payloadAttributes.Timestamp * 1000;
        if (_slotTimestampMs < genesisTimestampMs)
        {
            throw new ArgumentOutOfRangeException(nameof(genesisTimestampMs), genesisTimestampMs, $"Genesis timestamp (ms) cannot be after the payload timestamp ({_slotTimestampMs}).");
        }

        _cancellationTokenSource = new CancellationTokenSource();
        CurrentBestBlock = currentBestBlock;
        StartDateTime = startDateTime;
        _logger = logManager.GetClassLogger();
        _blockProducer = blockProducer;
        _txSignal = shutterTxSignal;
        _shutterConfig = shutterConfig;
        _parentHeader = parentHeader;
        _payloadAttributes = payloadAttributes;
        _genesisTimestampMs = genesisTimestampMs;
        _slotLength = slotLength;

        ImprovementTask = Task.Run(ImproveBlock);
    }

    public void Dispose()
    {
        Disposed = true;
        CancellationTokenExtensions.CancelDisposeAndClear(ref _cancellationTokenSource);
    }

    private async Task<Block?> ImproveBlock()
    {
        _logger.Debug("Running Shutter block improvement.");

        ulong slot;
        long offset;
        try
        {
            (slot, offset) = ShutterHelpers.GetBuildingSlotAndOffset(_slotTimestampMs, _genesisTimestampMs);
        }
        catch (ShutterHelpers.ShutterSlotCalulationException e)
        {
            _logger.Warn($"Could not calculate Shutter building slot: {e}");
            await TryBuildShutterBlock(0);
            return CurrentBestBlock;
        }

        // set default block without waiting for Shutter keys
        bool didBuildShutterBlock = await TryBuildShutterBlock(slot);
        if (didBuildShutterBlock)
        {
            return CurrentBestBlock;
        }

        long waitTime = _shutterConfig.MaxKeyDelay - offset;
        if (waitTime <= 0)
        {
            _logger.Warn($"Cannot await Shutter decryption keys for slot {slot}, offset of {offset}ms is too late.");
            return CurrentBestBlock;
        }
        waitTime = Math.Min(waitTime, 2 * (long)_slotLength.TotalMilliseconds);

        _logger.Debug($"Awaiting Shutter decryption keys for {slot} at offset {offset}ms. Timeout in {waitTime}ms...");

        using var timeoutSource = new CancellationTokenSource((int)waitTime);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource!.Token, timeoutSource.Token);

        try
        {
            await _txSignal.WaitForTransactions(slot, source.Token);
        }
        catch (OperationCanceledException)
        {
            if (timeoutSource.IsCancellationRequested && _logger.IsWarn)
            {
                _logger.Warn($"Shutter decryption keys not received in time for slot {slot}.");
            }

            return CurrentBestBlock;
        }

        // should succeed after waiting for transactions
        await TryBuildShutterBlock(slot);

        return CurrentBestBlock;
    }

    // builds normal block as fallback
    private async Task<bool> TryBuildShutterBlock(ulong slot)
    {
        bool hasShutterTxs = _txSignal.HaveTransactionsArrived(slot);
        Block? result = await _blockProducer.BuildBlock(_parentHeader, null, _payloadAttributes, _cancellationTokenSource!.Token);
        if (result is not null)
        {
            CurrentBestBlock = result;
        }
        return hasShutterTxs;
    }
}