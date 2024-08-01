// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Threading;
using System.Threading.Tasks;
using Nethermind.Consensus.Producers;
using Nethermind.Core;
using Nethermind.Core.Extensions;
using Nethermind.Evm.Tracing;
using Nethermind.Int256;
using Nethermind.Merge.Plugin.Data;
using Nethermind.State;

namespace Nethermind.Merge.Plugin.BlockProduction.Boost;

public class BoostBlockImprovementContext : IBlockImprovementContext
{
    private readonly IBoostRelay _boostRelay;
    private readonly IWorldStateManager _worldStateManager;
    private readonly FeesTracer _feesTracer = new();
    private CancellationTokenSource? _cancellationTokenSource;

    public BoostBlockImprovementContext(Block currentBestBlock,
        IManualBlockProductionTrigger blockProductionTrigger,
        TimeSpan timeout,
        BlockHeader parentHeader,
        PayloadAttributes payloadAttributes,
        IBoostRelay boostRelay,
        IWorldStateManager worldStateManager,
        DateTimeOffset startDateTime)
    {
        _boostRelay = boostRelay;
        _worldStateManager = worldStateManager;
        _cancellationTokenSource = new CancellationTokenSource(timeout);
        CurrentBestBlock = currentBestBlock;
        StartDateTime = startDateTime;
        ImprovementTask = StartImprovingBlock(blockProductionTrigger, parentHeader, payloadAttributes, _cancellationTokenSource.Token);
    }

    private async Task<Block?> StartImprovingBlock(
        IManualBlockProductionTrigger blockProductionTrigger,
        BlockHeader parentHeader,
        PayloadAttributes payloadAttributes,
        CancellationToken cancellationToken)
    {

        payloadAttributes = await _boostRelay.GetPayloadAttributes(payloadAttributes, cancellationToken);
        _worldStateManager.GetGlobalStateReader(parentHeader).TryGetAccount(parentHeader.StateRoot!, payloadAttributes.SuggestedFeeRecipient, out AccountStruct account);
        UInt256 balanceBefore = account.Balance;
        Block? block = await blockProductionTrigger.BuildBlock(parentHeader, cancellationToken, _feesTracer, payloadAttributes);
        if (block is not null)
        {
            CurrentBestBlock = block;
            BlockFees = _feesTracer.Fees;
            _worldStateManager.GetGlobalStateReader(parentHeader).TryGetAccount(parentHeader.StateRoot!, payloadAttributes.SuggestedFeeRecipient, out account);
            await _boostRelay.SendPayload(new BoostExecutionPayloadV1 { Block = new ExecutionPayload(block), Profit = account.Balance - balanceBefore }, cancellationToken);
        }

        return CurrentBestBlock;
    }

    public Task<Block?> ImprovementTask { get; }
    public Block? CurrentBestBlock { get; private set; }
    public UInt256 BlockFees { get; private set; }
    public bool Disposed { get; private set; }
    public DateTimeOffset StartDateTime { get; }

    public void Dispose()
    {
        Disposed = true;
        CancellationTokenExtensions.CancelDisposeAndClear(ref _cancellationTokenSource);
    }
}
