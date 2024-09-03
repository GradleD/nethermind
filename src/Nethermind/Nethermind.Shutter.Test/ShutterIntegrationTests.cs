// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Nethermind.Core.Test.Builders;
using Nethermind.Merge.Plugin;
using Nethermind.Merge.Plugin.Data;
using Nethermind.Merge.Plugin.Test;
using Nethermind.Consensus.Producers;
using Nethermind.Core;
using System.Threading;
using Nethermind.Abi;
using Nethermind.Blockchain;
using Nethermind.Crypto;
using Nethermind.Blockchain.Receipts;
using Nethermind.Logging;
using Nethermind.Core.Specs;
using Nethermind.State;
using Nethermind.Shutter.Config;
using Nethermind.Blockchain.Find;

namespace Nethermind.Shutter.Test;

[TestFixture]
public class ShutterIntegrationTests : EngineModuleTests
{
    private readonly int _buildingSlot = (int)ShutterTestsCommon.InitialSlot;
    private readonly ulong _buildingSlotTimestamp = ShutterTestsCommon.InitialSlotTimestamp;

    private class ShutterApiSimulatorNoBlockTimeout(IAbiEncoder abiEncoder, IReadOnlyBlockTree blockTree, IEthereumEcdsa ecdsa, ILogFinder logFinder, IReceiptStorage receiptStorage, ILogManager logManager, ISpecProvider specProvider, ITimestamper timestamper, IWorldStateManager worldStateManager, IShutterConfig cfg, Dictionary<ulong, byte[]> validatorsInfo, Random rnd) : ShutterApiSimulator(abiEncoder, blockTree, ecdsa, logFinder, receiptStorage, logManager, specProvider, timestamper, worldStateManager, cfg, validatorsInfo, rnd)
    {
        public override TimeSpan BlockWaitCutoff { get => TimeSpan.MaxValue; }
    }

    private class ShutterTestBlockchainNoBlockTimeout(Random rnd, ITimestamper timestamper) : ShutterTestBlockchain(rnd, timestamper)
    {
        protected override ShutterApiSimulator CreateShutterApi(Random rnd, ITimestamper timestamper)
            => new ShutterApiSimulatorNoBlockTimeout(
                ShutterTestsCommon.AbiEncoder, BlockTree.AsReadOnly(), EthereumEcdsa, LogFinder, ReceiptStorage,
                LogManager, SpecProvider, timestamper, WorldStateManager, ShutterTestsCommon.Cfg, [], rnd
            );
    }

    [Test]
    public async Task Can_load_when_keys_arrive_before_block()
    {
        Random rnd = new(ShutterTestsCommon.Seed);
        Timestamper timestamper = ShutterTestsCommon.InitTimestamper(_buildingSlotTimestamp - 5, 0);
        PayloadAttributes payloadAttributes = new()
        {
            Timestamp = _buildingSlotTimestamp
        };

        using var chain = (ShutterTestBlockchainNoBlockTimeout)await new ShutterTestBlockchainNoBlockTimeout(rnd, timestamper).Build(ShutterTestsCommon.SpecProvider);
        IEngineRpcModule rpc = CreateEngineModule(chain);

        ExecutionPayload initialPayload = CreateParentBlockRequestOnHead(chain.BlockTree);
        IReadOnlyList<ExecutionPayload> executionPayloads = await ProduceBranchV1(rpc, chain, _buildingSlot - 2, initialPayload, true, null, 5);
        ExecutionPayload lastPayload = executionPayloads[executionPayloads.Count - 1];

        chain.Api!.SetEventSimulator(ShutterTestsCommon.InitEventSimulator(rnd));

        // keys arrive, waits for head block before loading
        chain.Api.AdvanceSlot(20);

        _ = Task.Run(async () =>
        {
            // block is a bit late
            await Task.Delay(1000);
            IReadOnlyList<ExecutionPayload> payloads = await ProduceBranchV1(rpc, chain, 1, lastPayload, true, null, 5);
            lastPayload = payloads[0];

            chain.Api.TriggerNewHeadBlock(new(chain.BlockTree!.Head!));
        });

        // no events loaded initially
        var txs = chain.Api.TxSource.GetTransactions(chain.BlockTree!.Head!.Header, 0, payloadAttributes).ToList();
        Assert.That(txs, Has.Count.EqualTo(0));

        await chain.Api.TxSource.WaitForTransactions(ShutterTestsCommon.InitialSlot, new CancellationToken());
        txs = chain.Api.TxSource.GetTransactions(chain.BlockTree!.Head!.Header, 0, payloadAttributes).ToList();
        Assert.That(txs, Has.Count.EqualTo(20));

        IReadOnlyList<ExecutionPayload> payloads = await ProduceBranchV1(rpc, chain, 1, lastPayload, true, null, 5);
        lastPayload = payloads[0];
        lastPayload.TryGetBlock(out Block? b);
        Assert.That(b!.Transactions, Has.Length.EqualTo(20));
    }

    [Test]
    public async Task Can_load_when_previous_block_missed()
    {
        Random rnd = new(ShutterTestsCommon.Seed);
        Timestamper timestamper = ShutterTestsCommon.InitTimestamper(_buildingSlotTimestamp - 5, 0);
        PayloadAttributes payloadAttributes = new()
        {
            Timestamp = _buildingSlotTimestamp
        };

        using var chain = (ShutterTestBlockchain)await new ShutterTestBlockchain(rnd, timestamper).Build(ShutterTestsCommon.SpecProvider);
        IEngineRpcModule rpc = CreateEngineModule(chain);
        IReadOnlyList<ExecutionPayload> executionPayloads = await ProduceBranchV1(rpc, chain, _buildingSlot - 1, CreateParentBlockRequestOnHead(chain.BlockTree), true, null, 5);
        ExecutionPayload lastPayload = executionPayloads[executionPayloads.Count - 1];

        chain.Api!.SetEventSimulator(ShutterTestsCommon.InitEventSimulator(rnd));

        // waits to load transactions for head block
        chain.Api.AdvanceSlot(20);

        // no events loaded initially
        var txs = chain.Api.TxSource.GetTransactions(chain.BlockTree!.Head!.Header, 0, payloadAttributes).ToList();
        Assert.That(txs, Has.Count.EqualTo(0));

        // allow waiting for block to timeout
        await Task.Delay((int)chain.Api.BlockWaitCutoff.TotalMilliseconds + 200);

        txs = chain.Api.TxSource.GetTransactions(chain.BlockTree!.Head!.Header, 0, payloadAttributes).ToList();
        Assert.That(txs, Has.Count.EqualTo(20));

        IReadOnlyList<ExecutionPayload> payloads = await ProduceBranchV1(rpc, chain, 1, lastPayload, true, null, 5);
        lastPayload = payloads[0];
        lastPayload.TryGetBlock(out Block? b);
        Assert.That(b!.Transactions, Has.Length.EqualTo(20));
    }


    [Test]
    public async Task Can_load_when_block_arrives_before_keys()
    {
        Random rnd = new(ShutterTestsCommon.Seed);
        Timestamper timestamper = ShutterTestsCommon.InitTimestamper(_buildingSlotTimestamp - 5, 0);
        PayloadAttributes payloadAttributes = new()
        {
            Timestamp = _buildingSlotTimestamp
        };

        using var chain = (ShutterTestBlockchain)await new ShutterTestBlockchain(rnd, timestamper).Build(ShutterTestsCommon.SpecProvider);
        IEngineRpcModule rpc = CreateEngineModule(chain);
        IReadOnlyList<ExecutionPayload> executionPayloads = await ProduceBranchV1(rpc, chain, _buildingSlot - 2, CreateParentBlockRequestOnHead(chain.BlockTree), true, null, 5);
        ExecutionPayload lastPayload = executionPayloads[executionPayloads.Count - 1];

        chain.Api!.SetEventSimulator(ShutterTestsCommon.InitEventSimulator(rnd));

        // no events loaded initially
        var txs = chain.Api.TxSource.GetTransactions(chain.BlockTree!.Head!.Header, 0, payloadAttributes).ToList();
        Assert.That(txs, Has.Count.EqualTo(0));

        chain.Api.TriggerNewHeadBlock(new(Build.A.Block.WithTimestamp(_buildingSlotTimestamp - 5).TestObject));
        timestamper.SetTimestamp((long)_buildingSlotTimestamp);
        chain.Api.AdvanceSlot(20);

        IReadOnlyList<ExecutionPayload> payloads = await ProduceBranchV1(rpc, chain, 1, lastPayload, true, null, 5);
        lastPayload = payloads[0];

        await Task.Delay(100);

        txs = chain.Api.TxSource.GetTransactions(chain.BlockTree!.Head!.Header, 0, payloadAttributes).ToList();
        Assert.That(txs, Has.Count.EqualTo(20));

        payloads = await ProduceBranchV1(rpc, chain, 1, lastPayload, true, null, 5);
        lastPayload = payloads[0];
        lastPayload.TryGetBlock(out Block? b);
        Assert.That(b!.Transactions, Has.Length.EqualTo(20));
    }

    [Test]
    [NonParallelizable]
    public async Task Can_increment_metric_on_missed_keys()
    {
        Random rnd = new(ShutterTestsCommon.Seed);
        long time = 1;
        Timestamper timestamper = new(time);

        Metrics.KeysMissed = 0;

        using var chain = (ShutterTestBlockchain)await new ShutterTestBlockchain(rnd, timestamper).Build(ShutterTestsCommon.SpecProvider);
        IEngineRpcModule rpc = CreateEngineModule(chain);

        ExecutionPayload lastPayload = CreateParentBlockRequestOnHead(chain.BlockTree);
        for (int i = 0; i < 5; i++)
        {
            // KeysMissed will be incremented when get_payload is called
            lastPayload = (await ProduceBranchV1(rpc, chain, 1, lastPayload, true, null, 5))[0];

            time += (long)ShutterTestsCommon.SlotLength.TotalSeconds;
            timestamper.SetTimestamp(time);
        }

        // longer delay between fcu and get_payload, should timeout waiting for keys
        var payloadImprovementDelay = TimeSpan.FromMilliseconds(ShutterTestsCommon.Cfg.MaxKeyDelay + 200);
        lastPayload = (await ProduceBranchV1(rpc, chain, 1, lastPayload, true, null, 5, payloadImprovementDelay))[0];

        Assert.That(Metrics.KeysMissed, Is.EqualTo(6));
    }

}