// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Consensus;
using Nethermind.Consensus.AuRa.Config;
using Nethermind.Consensus.Producers;
using Nethermind.Core;
using Nethermind.Core.Extensions;
using Nethermind.Core.Specs;
using Nethermind.Int256;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.BlockProduction;
using Nethermind.Specs;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nethermind.Merge.AuRa.Shutter;
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
}

public class ShutterBlockImprovementContext : IBlockImprovementContext
{
    public Task<Block?> ImprovementTask { get; }

    public Block? CurrentBestBlock { get; private set; }

    public bool Disposed { get; private set; }
    public DateTimeOffset StartDateTime { get; }

    public UInt256 BlockFees => 0;

    private CancellationTokenSource? _cancellationTokenSource;
    private ILogger _logger;

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

        ulong slotTimestampMs = payloadAttributes.Timestamp * 1000;
        if (slotTimestampMs < genesisTimestampMs)
        {
            throw new ArgumentOutOfRangeException(nameof(genesisTimestampMs), genesisTimestampMs, $"Genesis timestamp (ms) cannot be after the payload timestamp ({slotTimestampMs}).");
        }

        _cancellationTokenSource = new CancellationTokenSource();
        CurrentBestBlock = currentBestBlock;
        StartDateTime = startDateTime;
        _logger = logManager.GetClassLogger();

        ImprovementTask = Task.Run(async () =>
        {
            // set default block without waiting for Shutter keys
            Block? result = await blockProducer.BuildBlock(parentHeader, null, payloadAttributes, _cancellationTokenSource.Token);
            if (result is not null)
            {
                CurrentBestBlock = result;
            }

            (ulong slot, short offset)? slotAndOffset = ShutterHelpers.GetBuildingSlotAndOffset(slotTimestampMs, genesisTimestampMs, slotLength);

            if (slotAndOffset is null)
            {
                // building for outdated slot
                return CurrentBestBlock;
            }

            ulong slot = slotAndOffset.Value.slot;
            short offset = slotAndOffset.Value.offset;
            int waitTime = shutterConfig.MaxKeyDelay - offset;
            if (waitTime <= 0)
            {
                return CurrentBestBlock;
            }

            using var timeoutSource = new CancellationTokenSource(waitTime);
            using var source = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token, timeoutSource.Token);

            try
            {
                await shutterTxSignal.WaitForTransactions(slot, source.Token);
            }
            catch (OperationCanceledException)
            {

                if (!_cancellationTokenSource.IsCancellationRequested && _logger.IsWarn)
                {
                    _logger.Warn($"Shutter decryption keys not received in time for slot {slot}.");
                }

                return CurrentBestBlock;
            }

            result = await blockProducer.BuildBlock(parentHeader, null, payloadAttributes, _cancellationTokenSource.Token);
            if (result is not null)
            {
                CurrentBestBlock = result;
            }

            return result;
        });
    }

    public void Dispose()
    {
        Disposed = true;
        CancellationTokenExtensions.CancelDisposeAndClear(ref _cancellationTokenSource);
    }
}
