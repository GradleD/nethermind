// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

#nullable enable
using System;
using System.IO.Abstractions;
using Autofac;
using Nethermind.Api;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Filters;
using Nethermind.Blockchain.Receipts;
using Nethermind.Blockchain.Services;
using Nethermind.Config;
using Nethermind.Consensus;
using Nethermind.Consensus.Comparers;
using Nethermind.Consensus.Processing;
using Nethermind.Consensus.Producers;
using Nethermind.Consensus.Rewards;
using Nethermind.Consensus.Validators;
using Nethermind.Core.Specs;
using Nethermind.Crypto;
using Nethermind.Db;
using Nethermind.Logging;
using Nethermind.Network;
using Nethermind.Db.Blooms;
using Nethermind.Evm.TransactionProcessing;
using Nethermind.Facade.Eth;
using Nethermind.Grpc;
using Nethermind.JsonRpc.Modules;
using Nethermind.JsonRpc.Modules.Eth.GasPrice;
using Nethermind.KeyStore;
using Nethermind.Monitoring;
using Nethermind.Network.Rlpx;
using Nethermind.Serialization.Json;
using Nethermind.Specs.ChainSpecStyle;
using Nethermind.State;
using Nethermind.State.Repositories;
using Nethermind.Stats;
using Nethermind.Synchronization;
using Nethermind.Synchronization.ParallelSync;
using Nethermind.Synchronization.Peers;
using Nethermind.Trie.Pruning;
using Nethermind.TxPool;
using Nethermind.Wallet;
using Nethermind.Sockets;
using Nethermind.Specs;
using Nethermind.Trie;
using NSubstitute;
using Nethermind.Blockchain.Blocks;
using Nethermind.Core;
using Nethermind.Core.Container;
using Nethermind.Facade.Find;
using Nethermind.Runner.Modules;

namespace Nethermind.Runner.Test.Ethereum
{
    public static class Build
    {
        public static NethermindApi ContextWithMocks(Action<ContainerBuilder>? containerConfigurer = null)
        {
            ContainerBuilder containerBuilder = new ContainerBuilder()
                .AddInstance(Substitute.For<IConfigProvider>())
                .AddInstance(Substitute.For<IJsonSerializer>())
                .AddInstance<ILogManager>(LimboLogs.Instance)
                .AddSingleton<ChainSpec>()
                .AddModule(new BaseModule())
                .AddModule(new CoreModule())
                .AddModule(new RunnerModule())
                .AddInstance(Substitute.For<IProcessExitSource>())
                .AddInstance(Substitute.For<ISpecProvider>()); // need more complete chainspec to use ISpecProvider

            containerConfigurer?.Invoke(containerBuilder);

            IContainer container = containerBuilder.Build();

            var api = container.Resolve<INethermindApi>();
            api.Enode = Substitute.For<IEnode>();
            api.TxPool = Substitute.For<ITxPool>();
            api.Wallet = Substitute.For<IWallet>();
            api.BlockTree = Substitute.For<IBlockTree>();
            api.SyncServer = Substitute.For<ISyncServer>();
            api.DbProvider = TestMemDbProvider.Init();
            api.PeerManager = Substitute.For<IPeerManager>();
            api.PeerPool = Substitute.For<IPeerPool>();
            api.EthereumEcdsa = Substitute.For<IEthereumEcdsa>();
            api.MainBlockProcessor = Substitute.For<IBlockProcessor>();
            api.ReceiptStorage = Substitute.For<IReceiptStorage>();
            api.ReceiptFinder = Substitute.For<IReceiptFinder>();
            api.BlockValidator = Substitute.For<IBlockValidator>();
            api.RewardCalculatorSource = Substitute.For<IRewardCalculatorSource>();
            api.TxPoolInfoProvider = Substitute.For<ITxPoolInfoProvider>();
            api.StaticNodesManager = Substitute.For<IStaticNodesManager>();
            api.BloomStorage = Substitute.For<IBloomStorage>();
            api.Sealer = Substitute.For<ISealer>();
            api.BlockchainProcessor = Substitute.For<IBlockchainProcessor>();
            api.BlockProducer = Substitute.For<IBlockProducer>();
            api.DiscoveryApp = Substitute.For<IDiscoveryApp>();
            api.EngineSigner = Substitute.For<ISigner>();
            api.FileSystem = Substitute.For<IFileSystem>();
            api.FilterManager = Substitute.For<IFilterManager>();
            api.FilterStore = Substitute.For<IFilterStore>();
            api.GrpcServer = Substitute.For<IGrpcServer>();
            api.HeaderValidator = Substitute.For<IHeaderValidator>();
            api.IpResolver = Substitute.For<IIPResolver>();
            api.KeyStore = Substitute.For<IKeyStore>();
            api.LogFinder = Substitute.For<ILogFinder>();
            api.MonitoringService = Substitute.For<IMonitoringService>();
            api.ProtocolsManager = Substitute.For<IProtocolsManager>();
            api.ProtocolValidator = Substitute.For<IProtocolValidator>();
            api.RlpxPeer = Substitute.For<IRlpxHost>();
            api.SealValidator = Substitute.For<ISealValidator>();
            api.SessionMonitor = Substitute.For<ISessionMonitor>();
            api.WorldState = Substitute.For<IWorldState>();
            api.StateReader = Substitute.For<IStateReader>();
            api.TransactionProcessor = Substitute.For<ITransactionProcessor>();
            api.TxSender = Substitute.For<ITxSender>();
            api.BlockProcessingQueue = Substitute.For<IBlockProcessingQueue>();
            api.EngineSignerStore = Substitute.For<ISignerStore>();
            api.NodeStatsManager = Substitute.For<INodeStatsManager>();
            api.RpcModuleProvider = Substitute.For<IRpcModuleProvider>();
            api.SyncPeerPool = Substitute.For<ISyncPeerPool>();
            api.PeerDifficultyRefreshPool = Substitute.For<IPeerDifficultyRefreshPool>();
            api.WebSocketsManager = Substitute.For<IWebSocketsManager>();
            api.ChainLevelInfoRepository = Substitute.For<IChainLevelInfoRepository>();
            api.TrieStore = Substitute.For<ITrieStore>();
            api.BlockProducerEnvFactory = Substitute.For<IBlockProducerEnvFactory>();
            api.TransactionComparerProvider = Substitute.For<ITransactionComparerProvider>();
            api.GasPriceOracle = Substitute.For<IGasPriceOracle>();
            api.EthSyncingInfo = Substitute.For<IEthSyncingInfo>();
            api.HealthHintService = Substitute.For<IHealthHintService>();
            api.TxValidator = new TxValidator(MainnetSpecProvider.Instance.ChainId);
            api.UnclesValidator = Substitute.For<IUnclesValidator>();
            api.BlockProductionPolicy = Substitute.For<IBlockProductionPolicy>();
            api.BetterPeerStrategy = Substitute.For<IBetterPeerStrategy>();
            api.ReceiptMonitor = Substitute.For<IReceiptMonitor>();
            api.BadBlocksStore = Substitute.For<IBlockStore>();

            api.ApiWithNetworkServiceContainer = new ContainerBuilder()
                .AddInstance(Substitute.For<ISyncModeSelector>())
                .AddInstance(Substitute.For<ISyncProgressResolver>())
                .AddInstance(Substitute.For<ISynchronizer>())
                .Build();

            api.WorldStateManager = new ReadOnlyWorldStateManager(api.DbProvider, Substitute.For<IReadOnlyTrieStore>(), LimboLogs.Instance);
            api.NodeStorageFactory = new NodeStorageFactory(INodeStorage.KeyScheme.HalfPath, LimboLogs.Instance);

            return (NethermindApi)api;
        }
    }
}
