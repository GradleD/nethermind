// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using Nethermind.Consensus.Processing;
using Nethermind.Core;
using Nethermind.Evm.TransactionProcessing;
using Nethermind.Merge.AuRa.Shutter.Contracts;
using Nethermind.Logging;
using Nethermind.Abi;

namespace Nethermind.Merge.AuRa.Shutter;

public class ShutterBlockHandler(
    ulong chainId,
    string validatorRegistryContractAddress,
    ulong validatorRegistryMessageVersion,
    ReadOnlyTxProcessingEnvFactory envFactory, IAbiEncoder abiEncoder,
    Dictionary<ulong, byte[]> validatorsInfo,
    ShutterEon eon,
    ILogManager logManager) : IShutterBlockHandler
{
    private readonly ILogger _logger = logManager.GetClassLogger();
    private bool _haveCheckedRegistered = false;
    public void OnNewHeadBlock(Block head)
    {
        int headerAge = (int)(head.Header.Timestamp - (ulong)DateTimeOffset.Now.ToUnixTimeSeconds());
        if (headerAge < 10)
        {
            if (!_haveCheckedRegistered)
            {
                CheckAllValidatorsRegistered(head.Header, validatorsInfo);
                _haveCheckedRegistered = true;
            }
            eon.Update(head.Header);
        }
    }

    private void CheckAllValidatorsRegistered(BlockHeader parent, Dictionary<ulong, byte[]> validatorsInfo)
    {
        if (validatorsInfo.Count == 0)
        {
            return;
        }

        IReadOnlyTxProcessingScope scope = envFactory.Create().Build(parent.StateRoot!);
        ITransactionProcessor processor = scope.TransactionProcessor;

        ValidatorRegistryContract validatorRegistryContract = new(processor, abiEncoder, new(validatorRegistryContractAddress), _logger, chainId, validatorRegistryMessageVersion);
        if (validatorRegistryContract.IsRegistered(parent, validatorsInfo, out HashSet<ulong> unregistered))
        {
            if (_logger.IsInfo) _logger.Info($"All Shutter validator keys are registered.");
        }
        else
        {
            if (_logger.IsError) _logger.Error($"Validators not registered to Shutter with the following indices: [{string.Join(", ", unregistered)}]");
        }
    }

}
