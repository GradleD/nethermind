// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Nethermind.Api;
using Nethermind.Api.Extensions;
using Nethermind.Blockchain;
using Nethermind.Consensus;
using Nethermind.Consensus.Transactions;
using Nethermind.Core;
using Nethermind.Shutter;
using Nethermind.Shutter.Config;
using Nethermind.Merge.AuRa;
using Nethermind.Merge.Plugin;
using Nethermind.Merge.Plugin.BlockProduction;
using Nethermind.Consensus.Processing;
using Multiformats.Address;
using Nethermind.Serialization.Json;
using Nethermind.Evm.TransactionProcessing;
using Nethermind.Shutter.Contracts;
using Nethermind.Logging;

namespace Nethermind.Shutter
{
    public class ShutterPlugin : IConsensusWrapperPlugin
    {
        public string Name => "Shutter";
        public string Description => "Shutter plugin for AuRa post-merge chains";
        public string Author => "Nethermind";
        public bool Enabled => _shutterConfig!.Enabled && _mergeConfig!.Enabled &&
                                           _api!.ChainSpec.SealEngineType is SealEngineType.BeaconChain or SealEngineType.Clique or SealEngineType.Ethash;

        private INethermindApi? _api = null!;
        private IMergeConfig? _mergeConfig = null!;
        private IShutterConfig? _shutterConfig;
        private ShutterP2P? _shutterP2P;
        private EventHandler<BlockEventArgs>? _eonUpdateHandler;
        private ShutterTxSource? _shutterTxSource = null;

        public class ShutterLoadingException(string message, Exception? innerException = null) : Exception(message, innerException);

        public Task Init(INethermindApi nethermindApi)
        {
            _api = nethermindApi;
            _mergeConfig = _api.Config<IMergeConfig>();
            _shutterConfig = _api.Config<IShutterConfig>();
            return Task.CompletedTask;
        }

        public Task InitRpcModules()
        {
            // todo: how to do this?
            // IBlockImprovementContextFactory? blockImprovementContextFactory = null;
            // if (_shutterConfig!.Enabled)
            // {
            //     blockImprovementContextFactory = new ShutterBlockImprovementContextFactory(
            //         _api.BlockProducer!,
            //         _shutterTxSource!,
            //         _shutterConfig,
            //         _api.SpecProvider!,
            //         _api.LogManager);
            // }
            // base.InitRpcModulesInternal(blockImprovementContextFactory, false);
            return Task.CompletedTask;
        }


        public IBlockProducer InitBlockProducer(IBlockProducerFactory consensusPlugin, ITxSource? txSource)
        {
            if (Enabled)
            {
                if (_api!.AbiEncoder is null) throw new ArgumentNullException(nameof(_api.AbiEncoder));
                if (_api.BlockTree is null) throw new ArgumentNullException(nameof(_api.BlockTree));
                if (_api.EthereumEcdsa is null) throw new ArgumentNullException(nameof(_api.SpecProvider));
                if (_api.LogFinder is null) throw new ArgumentNullException(nameof(_api.LogFinder));
                if (_api.LogManager is null) throw new ArgumentNullException(nameof(_api.LogManager));
                if (_api.SpecProvider is null) throw new ArgumentNullException(nameof(_api.SpecProvider));
                if (_api.WorldStateManager is null) throw new ArgumentNullException(nameof(_api.WorldStateManager));

                Logging.ILogger logger = _api.LogManager.GetClassLogger();

                ShutterHelpers.ValidateConfig(_shutterConfig!);

                Dictionary<ulong, byte[]> validatorsInfo = [];
                if (_shutterConfig!.ValidatorInfoFile is not null)
                {
                    try
                    {
                        validatorsInfo = ShutterHelpers.LoadValidatorInfo(_shutterConfig!.ValidatorInfoFile);
                    }
                    catch (Exception e)
                    {
                        throw new ShutterLoadingException("Could not load Shutter validator info file", e);
                    }
                }

                IReadOnlyBlockTree readOnlyBlockTree = _api.BlockTree!.AsReadOnly();
                ReadOnlyTxProcessingEnvFactory readOnlyTxProcessingEnvFactory = new(_api.WorldStateManager!, readOnlyBlockTree, _api.SpecProvider, _api.LogManager);

                ShutterEon shutterEon = new(readOnlyBlockTree, readOnlyTxProcessingEnvFactory, _api.AbiEncoder!, _shutterConfig, logger);
                bool haveCheckedRegistered = false;
                _eonUpdateHandler = (_, e) =>
                {
                    int headerAge = (int)(e.Block.Header.Timestamp - (ulong)DateTimeOffset.Now.ToUnixTimeSeconds());
                    if (headerAge < 10)
                    {
                        if (!haveCheckedRegistered)
                        {
                            CheckValidatorsRegistered(e.Block.Header, validatorsInfo, readOnlyTxProcessingEnvFactory, logger);
                            haveCheckedRegistered = true;
                        }
                        shutterEon.Update(e.Block.Header);
                    }
                };
                _api.BlockTree!.NewHeadBlock += _eonUpdateHandler;

                ShutterTxLoader txLoader = new(_api.LogFinder!, _shutterConfig, _api.SpecProvider!, _api.EthereumEcdsa!, readOnlyBlockTree, _api.LogManager);
                _shutterTxSource = new ShutterTxSource(txLoader, _shutterConfig, _api.SpecProvider!, _api.LogManager);

                ShutterMessageHandler shutterMessageHandler = new(_shutterConfig, _shutterTxSource, shutterEon, _api.LogManager);
                _shutterP2P = new(shutterMessageHandler.OnDecryptionKeysReceived, _shutterConfig, _api.LogManager);
                _shutterP2P.Start(_shutterConfig.KeyperP2PAddresses!);
            }

            return consensusPlugin.InitBlockProducer(_shutterTxSource.Then(txSource));
        }


        public async ValueTask DisposeAsync()
        {
            if (_eonUpdateHandler is not null)
            {
                _api!.BlockTree!.NewHeadBlock -= _eonUpdateHandler;
            }
            await (_shutterP2P?.DisposeAsync() ?? default);
        }

        private void CheckValidatorsRegistered(BlockHeader parent, Dictionary<ulong, byte[]> validatorsInfo, ReadOnlyTxProcessingEnvFactory envFactory, ILogger logger)
        {
            if (validatorsInfo.Count == 0)
            {
                return;
            }

            IReadOnlyTxProcessingScope scope = envFactory.Create().Build(parent.StateRoot!);
            ITransactionProcessor processor = scope.TransactionProcessor;

            ValidatorRegistryContract validatorRegistryContract = new(processor, _api!.AbiEncoder!, new(_shutterConfig!.ValidatorRegistryContractAddress!), logger, _api.SpecProvider!.ChainId, _shutterConfig.ValidatorRegistryMessageVersion);
            if (validatorRegistryContract.IsRegistered(parent, validatorsInfo, out HashSet<ulong> unregistered))
            {
                if (logger.IsInfo) logger.Info($"All Shutter validators are registered.");
            }
            else
            {
                if (logger.IsError) logger.Error($"Validators not registered to Shutter with the following indices: [{string.Join(", ", unregistered)}]");
            }
        }

    }
}