// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nethermind.Api;
using Nethermind.Api.Extensions;
using Nethermind.Consensus;
using Nethermind.Consensus.Transactions;
using Nethermind.Core;
using Nethermind.Shutter.Config;
using Nethermind.Merge.Plugin;
using Nethermind.Logging;
using System.IO;
using Nethermind.Serialization.Json;
using System.Threading;
using Autofac;
using Nethermind.Config;
using Nethermind.Init.Steps;
using Nethermind.Specs.ChainSpecStyle;

namespace Nethermind.Shutter;

public class ShutterPlugin(IShutterConfig shutterConfig, IMergeConfig mergeConfig, ChainSpec chainSpec) : IConsensusWrapperPlugin
{
    public string Name => "Shutter";
    public string Description => "Shutter plugin for AuRa post-merge chains";
    public string Author => "Nethermind";
    public bool ConsensusWrapperEnabled => Enabled;
    public bool Enabled => shutterConfig!.Enabled && mergeConfig!.Enabled && chainSpec.SealEngineType is SealEngineType.AuRa;

    public int Priority => PluginPriorities.Shutter;

    private INethermindApi? _api;
    private IBlocksConfig? _blocksConfig;
    private ShutterApi? _shutterApi;
    private ILogger _logger;
    private readonly CancellationTokenSource _cts = new();

    public class ShutterLoadingException(string message, Exception? innerException = null) : Exception(message, innerException);

    public Task Init(INethermindApi nethermindApi)
    {
        _api = nethermindApi;
        _blocksConfig = _api.Config<IBlocksConfig>();
        _logger = _api.LogManager.GetClassLogger();
        if (_logger.IsInfo) _logger.Info($"Initializing Shutter plugin.");
        return Task.CompletedTask;
    }

    public Task InitRpcModules()
    {
        if (ConsensusWrapperEnabled)
        {
            if (_api!.BlockProducer is null) throw new ArgumentNullException(nameof(_api.BlockProducer));

            if (_logger.IsInfo) _logger.Info("Initializing Shutter block improvement.");
            _api.BlockImprovementContextFactory = _shutterApi!.GetBlockImprovementContextFactory(_api.BlockProducer);
        }
        return Task.CompletedTask;
    }

    public IBlockProducer InitBlockProducer(IBlockProducerFactory consensusPlugin, ITxSource? txSource)
    {
        if (ConsensusWrapperEnabled)
        {
            if (_api!.BlockTree is null) throw new ArgumentNullException(nameof(_api.BlockTree));
            if (_api.EthereumEcdsa is null) throw new ArgumentNullException(nameof(_api.SpecProvider));
            if (_api.LogFinder is null) throw new ArgumentNullException(nameof(_api.LogFinder));
            if (_api.SpecProvider is null) throw new ArgumentNullException(nameof(_api.SpecProvider));
            if (_api.ReceiptFinder is null) throw new ArgumentNullException(nameof(_api.ReceiptFinder));
            if (_api.WorldStateManager is null) throw new ArgumentNullException(nameof(_api.WorldStateManager));

            if (_logger.IsInfo) _logger.Info("Initializing Shutter block producer.");

            try
            {
                shutterConfig!.Validate();
            }
            catch (ArgumentException e)
            {
                throw new ShutterLoadingException("Invalid Shutter config", e);
            }

            Dictionary<ulong, byte[]> validatorsInfo = [];
            if (shutterConfig!.ValidatorInfoFile is not null)
            {
                try
                {
                    validatorsInfo = LoadValidatorInfo(shutterConfig!.ValidatorInfoFile);
                }
                catch (Exception e)
                {
                    throw new ShutterLoadingException("Could not load Shutter validator info file", e);
                }
            }

            _shutterApi = new ShutterApi(
                _api.AbiEncoder,
                _api.BlockTree,
                _api.EthereumEcdsa,
                _api.LogFinder,
                _api.ReceiptFinder,
                _api.LogManager,
                _api.SpecProvider,
                _api.Timestamper,
                _api.WorldStateManager,
                shutterConfig,
                validatorsInfo,
                TimeSpan.FromSeconds(_blocksConfig!.SecondsPerSlot)
            );

            _ = _shutterApi.StartP2P(_cts);
        }

        return consensusPlugin.InitBlockProducer(_shutterApi is null ? txSource : _shutterApi.TxSource.Then(txSource));
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Dispose();
        await (_shutterApi?.DisposeAsync() ?? default);
    }

    private static Dictionary<ulong, byte[]> LoadValidatorInfo(string fp)
    {
        FileStream fstream = new(fp, FileMode.Open, FileAccess.Read, FileShare.None);
        return new EthereumJsonSerializer().Deserialize<Dictionary<ulong, byte[]>>(fstream);
    }

    private class ShutterModule : Module
    {
        protected override void Load(ContainerBuilder builder)
        {
            base.Load(builder);
            builder.AddIStepsFromAssembly(GetType().Assembly);
        }
    }
}
