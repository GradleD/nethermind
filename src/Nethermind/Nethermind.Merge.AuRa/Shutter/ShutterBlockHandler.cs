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
    ReadOnlyTxProcessingEnvFactory readOnlyTxProcessingEnvFactory, IAbiEncoder abiEncoder,
    Dictionary<ulong, byte[]> validatorsInfo,
    ShutterEon eon,
    ShutterTxLoader txLoader,
    ILogManager logManager) : IShutterBlockHandler
{
    private readonly ILogger _logger = logManager.GetClassLogger();
    private bool _haveCheckedRegistered = false;
    public void OnBlockProcessed(Block head, TxReceipt[] receipts)
    {
        int headerAge = (int)(head.Header.Timestamp - (ulong)DateTimeOffset.Now.ToUnixTimeSeconds());
        if (headerAge < 10)
        {
            if (!_haveCheckedRegistered)
            {
                CheckRegistered(head.Header, validatorsInfo, readOnlyTxProcessingEnvFactory);
                _haveCheckedRegistered = true;
            }
            eon.Update(head.Header);
            txLoader.OnNewReceipts(receipts, head.Number);
        }
    }

    private void CheckRegistered(BlockHeader parent, Dictionary<ulong, byte[]> validatorsInfo, ReadOnlyTxProcessingEnvFactory envFactory)
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
            if (_logger.IsInfo) _logger.Info($"All Shutter validators are registered.");
        }
        else
        {
            if (_logger.IsError) _logger.Error($"Validators not registered to Shutter with the following indices: [{string.Join(", ", unregistered)}]");
        }
    }

}
