// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Evm.Tracing;
using Nethermind.State;

namespace Nethermind.Optimism;

public class OptimismBlockReceiptTracer : BlockReceiptsTracer
{
    private readonly IOptimismSpecHelper _opSpecHelper;
    private readonly IWorldStateProvider _worldStateProvider;

    public OptimismBlockReceiptTracer(IOptimismSpecHelper opSpecHelper, IWorldStateProvider worldStateProvider)
    {
        _opSpecHelper = opSpecHelper;
        _worldStateProvider = worldStateProvider;
    }

    private (ulong?, ulong?) GetDepositReceiptData(BlockHeader header)
    {
        ArgumentNullException.ThrowIfNull(CurrentTx);

        ulong? depositNonce = null;
        ulong? version = null;
        IWorldState worldStateToUse = _worldStateProvider.GetGlobalWorldState(header);
        if (CurrentTx.IsDeposit())
        {
            depositNonce = worldStateToUse.GetNonce(CurrentTx.SenderAddress!).ToUInt64(null);
            // We write nonce after tx processing, so need to subtract one
            if (depositNonce > 0)
            {
                depositNonce--;
            }
            if (_opSpecHelper.IsCanyon(header))
            {
                version = 1;
            }
        }

        return (depositNonce, version);
    }

    protected override TxReceipt BuildReceipt(Address recipient, long spentGas, byte statusCode, LogEntry[] logEntries, Hash256? stateRoot)
    {
        (ulong? depositNonce, ulong? version) = GetDepositReceiptData(Block.Header);

        Transaction transaction = CurrentTx!;
        OptimismTxReceipt txReceipt = new()
        {
            Logs = logEntries,
            TxType = transaction.Type,
            Bloom = logEntries.Length == 0 ? Bloom.Empty : new Bloom(logEntries),
            GasUsedTotal = Block.GasUsed,
            StatusCode = statusCode,
            Recipient = transaction.IsContractCreation ? null : recipient,
            BlockHash = Block.Hash,
            BlockNumber = Block.Number,
            Index = _currentIndex,
            GasUsed = spentGas,
            Sender = transaction.SenderAddress,
            ContractAddress = transaction.IsContractCreation ? recipient : null,
            TxHash = transaction.Hash,
            PostTransactionState = stateRoot,
            DepositNonce = depositNonce,
            DepositReceiptVersion = version
        };

        return txReceipt;
    }
}
