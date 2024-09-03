// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Blockchain;
using Nethermind.Crypto;
using Nethermind.Core;
using Nethermind.Evm.TransactionProcessing;
using Nethermind.Abi;
using Nethermind.Shutter.Contracts;
using Nethermind.Logging;
using Nethermind.Shutter.Config;
using Nethermind.Consensus.Processing;
using Nethermind.Core.Crypto;

namespace Nethermind.Shutter;

public class ShutterEon(
    IReadOnlyBlockTree blockTree,
    ReadOnlyTxProcessingEnvFactory envFactory,
    IAbiEncoder abiEncoder,
    IShutterConfig shutterConfig,
    ILogManager logManager) : IShutterEon
{
    private IShutterEon.Info? _currentInfo;
    private readonly Address _keyBroadcastContractAddress = new(shutterConfig.KeyBroadcastContractAddress!);
    private readonly Address _keyperSetManagerContractAddress = new(shutterConfig.KeyperSetManagerContractAddress!);
    private readonly ILogger _logger = logManager.GetClassLogger();

    public IShutterEon.Info? GetCurrentEonInfo() => _currentInfo;

    public void Update(BlockHeader header)
    {
        Hash256 stateRoot = blockTree.Head!.StateRoot!;
        IReadOnlyTxProcessingScope scope = envFactory.Create().Build(stateRoot);
        ITransactionProcessor processor = scope.TransactionProcessor;

        try
        {
            KeyperSetManagerContract keyperSetManagerContract = new(processor, abiEncoder, _keyperSetManagerContractAddress);
            ulong eon = keyperSetManagerContract.GetKeyperSetIndexByBlock(header, (ulong)header.Number + 1);

            if (_currentInfo is null || _currentInfo.Value.Eon != eon)
            {
                Address keyperSetContractAddress = keyperSetManagerContract.GetKeyperSetAddress(header, eon);
                KeyperSetContract keyperSetContract = new(processor, abiEncoder, keyperSetContractAddress);

                if (keyperSetContract.IsFinalized(header))
                {
                    ulong threshold = keyperSetContract.GetThreshold(header);
                    Address[] addresses = keyperSetContract.GetMembers(header);

                    KeyBroadcastContract keyBroadcastContract = new(processor, abiEncoder, _keyBroadcastContractAddress);
                    byte[] eonKeyBytes = keyBroadcastContract.GetEonKey(blockTree.Head!.Header, eon);
                    Bls.P2 key = new(eonKeyBytes);

                    // update atomically
                    _currentInfo = new()
                    {
                        Eon = eon,
                        Key = key,
                        Threshold = threshold,
                        Addresses = addresses
                    };

                    Metrics.Eon = eon;
                    Metrics.Threshold = (int)threshold;
                    Metrics.Keypers = addresses.Length;

                    _logger.Info($"Shutter eon={_currentInfo.Value.Eon} threshold={_currentInfo.Value.Threshold} #keypers={_currentInfo.Value.Addresses.Length}");
                }
                else
                {
                    _logger.Error("Cannot use unfinalised Shutter keyper set contract.");
                }
            }
        }
        catch (AbiException e)
        {
            _logger.Error($"Error when calling Shutter Keyper contracts.", e);
        }
        catch (Bls.Exception e)
        {
            _logger.Error($"Invalid Shutter Eon key ", e);
        }
    }
}