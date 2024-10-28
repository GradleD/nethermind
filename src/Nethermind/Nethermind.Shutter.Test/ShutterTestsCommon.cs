
// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using Nethermind.Abi;
using Nethermind.Api;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Receipts;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.Crypto;
using Nethermind.Facade.Find;
using Nethermind.KeyStore.Config;
using Nethermind.Logging;
using Nethermind.Shutter.Config;
using Nethermind.Specs;
using Nethermind.State;
using NSubstitute;

using static Nethermind.Merge.Plugin.Test.EngineModuleTests;

namespace Nethermind.Shutter.Test;
class ShutterTestsCommon
{
    public const int Seed = 100;
    public const ulong InitialSlot = 21;
    public const ulong InitialSlotTimestamp = 1 + 21 * 5;
    public const ulong Threshold = 10;
    public const int ChainId = BlockchainIds.Chiado;
    public const ulong GenesisTimestamp = 1;
    public static readonly TimeSpan SlotLength = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan BlockUpToDateCutoff = TimeSpan.FromSeconds(5);
    public static readonly ISpecProvider SpecProvider = ChiadoSpecProvider.Instance;
    public static readonly IEthereumEcdsa Ecdsa = new EthereumEcdsa(ChainId);
    public static readonly ILogManager LogManager = LimboLogs.Instance;
    public static readonly AbiEncoder AbiEncoder = new();
    public static readonly ShutterConfig Cfg = new()
    {
        InstanceID = 0,
        ValidatorRegistryContractAddress = Address.Zero.ToString(),
        ValidatorRegistryMessageVersion = 0,
        KeyBroadcastContractAddress = Address.Zero.ToString(),
        KeyperSetManagerContractAddress = Address.Zero.ToString(),
        SequencerContractAddress = Address.Zero.ToString(),
        EncryptedGasLimit = 21000 * 20,
        Validator = true
    };

    public static ShutterApiSimulator InitApi(Random rnd, ITimestamper? timestamper = null, ShutterEventSimulator? eventSimulator = null)
    {
        INethermindApi api = Substitute.For<INethermindApi>();
        api.AbiEncoder.Returns(AbiEncoder);
        api.EthereumEcdsa.Returns(Ecdsa);
        api.Timestamper.Returns(timestamper ?? Substitute.For<ITimestamper>());
        api.Config<IShutterConfig>().Returns(Cfg);

        return new(eventSimulator ?? InitEventSimulator(rnd), api, [], rnd);
    }

    public static ShutterApiSimulator InitApi(Random rnd, MergeTestBlockchain chain, ITimestamper? timestamper = null, ShutterEventSimulator? eventSimulator = null)
    {
        INethermindApi api = Substitute.For<INethermindApi>();
        api.AbiEncoder.Returns(AbiEncoder);
        api.EthereumEcdsa.Returns(chain.EthereumEcdsa);
        api.Timestamper.Returns(timestamper ?? Substitute.For<ITimestamper>());
        api.BlockTree.Returns(chain.BlockTree);
        api.LogFinder.Returns(chain.LogFinder);
        api.ReceiptStorage.Returns(chain.ReceiptStorage);
        api.LogManager.Returns(chain.LogManager);
        api.SpecProvider.Returns(chain.SpecProvider);
        api.WorldStateManager.Returns(chain.WorldStateManager);
        api.Config<IShutterConfig>().Returns(Cfg);

        return new(eventSimulator ?? InitEventSimulator(rnd), api, [], rnd);
    }

    public static ShutterEventSimulator InitEventSimulator(Random rnd)
        => new(
            rnd,
            ChainId,
            Threshold,
            InitialSlot,
            AbiEncoder,
            new(Cfg.SequencerContractAddress!)
        );

    public static Timestamper InitTimestamper(ulong slotTimestamp, ulong offsetMs)
    {
        ulong timestampMs = slotTimestamp * 1000 + offsetMs;
        var blockTime = DateTimeOffset.FromUnixTimeMilliseconds((long)timestampMs);
        return new(blockTime.UtcDateTime);
    }
}
