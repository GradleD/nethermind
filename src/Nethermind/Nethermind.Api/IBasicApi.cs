// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Nethermind.Abi;
using Nethermind.Api.Extensions;
using Nethermind.Blockchain.Synchronization;
using Nethermind.Config;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.Core.Timers;
using Nethermind.Crypto;
using Nethermind.Db;
using Nethermind.KeyStore;
using Nethermind.Logging;
using Nethermind.Serialization.Json;
using Nethermind.Specs.ChainSpecStyle;
using Nethermind.Synchronization;
using Nethermind.Synchronization.ParallelSync;

namespace Nethermind.Api
{
    public interface IBasicApi
    {
        DisposableStack DisposeStack { get; }

        IAbiEncoder AbiEncoder { get; }
        ChainSpec ChainSpec { get; set; }
        IConfigProvider ConfigProvider { get; set; }
        ICryptoRandom CryptoRandom { get; }
        IDbProvider? DbProvider { get; set; }
        IDbFactory? DbFactory { get; set; }
        IEthereumEcdsa? EthereumEcdsa { get; set; }
        IJsonSerializer EthereumJsonSerializer { get; set; }
        IFileSystem FileSystem { get; set; }
        IKeyStore? KeyStore { get; set; }
        ILogManager LogManager { get; set; }
        ProtectedPrivateKey? OriginalSignerKey { get; set; }
        IReadOnlyList<INethermindPlugin> Plugins { get; }
        string SealEngineType { get; set; }
        ISpecProvider? SpecProvider { get; set; }
        ISyncModeSelector SyncModeSelector { get; set; }
        ISyncProgressResolver? SyncProgressResolver { get; set; }
        IBetterPeerStrategy? BetterPeerStrategy { get; set; }
        ITimestamper Timestamper { get; }
        ITimerFactory TimerFactory { get; }
        IProcessExitSource? ProcessExit { get; set; }

        public IConsensusPlugin? GetConsensusPlugin() =>
            Plugins
                .OfType<IConsensusPlugin>()
                .SingleOrDefault(cp => cp.SealEngineType == SealEngineType);

        public IEnumerable<IConsensusWrapperPlugin> GetConsensusWrapperPlugins() =>
            Plugins.OfType<IConsensusWrapperPlugin>().Where(p => p.Enabled);

        public IEnumerable<ISynchronizationPlugin> GetSynchronizationPlugins() =>
            Plugins.OfType<ISynchronizationPlugin>();

        public IServiceCollection ConfigureBasicApiServices()
        {
            IServiceCollection sc = new ServiceCollection()
                .AddSingleton(SpecProvider!)
                .AddSingleton(DbProvider!)
                .AddSingleton(ChainSpec)
                .AddSingletonIfNotNull(BetterPeerStrategy)
                .AddSingleton(ConfigProvider.GetConfig<ISyncConfig>())
                .AddSingleton(LogManager);

            string[] dbNames = [DbNames.State, DbNames.Code, DbNames.Metadata, DbNames.Blocks];
            foreach (string dbName in dbNames)
            {
                sc.AddKeyedSingleton<IDb>(dbName, DbProvider!.GetDb<IDb>(dbName));
                sc.AddKeyedSingleton<IDbMeta>(dbName, DbProvider!.GetDb<IDb>(dbName));
            }

            sc.AddSingleton<IColumnsDb<ReceiptsColumns>>(DbProvider!.GetColumnDb<ReceiptsColumns>(DbNames.Receipts));

            foreach (var kv in DbProvider.GetAllDbMeta())
            {
                // The key here is large case for some reason...
                sc.AddKeyedSingleton<IDbMeta>(kv.Key, kv.Value);
            }

            return sc;
        }
    }
}
