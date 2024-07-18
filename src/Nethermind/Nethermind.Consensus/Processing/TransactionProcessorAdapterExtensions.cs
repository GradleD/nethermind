// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Core;
using Nethermind.Evm;
using Nethermind.Evm.Tracing;
using Nethermind.Evm.TransactionProcessing;
using Nethermind.State;

namespace Nethermind.Consensus.Processing;

internal static class TransactionProcessorAdapterExtensions
{
    public static TransactionResult ProcessTransaction(this ITransactionProcessorAdapter transactionProcessor,
        in BlockExecutionContext blkCtx,
        Transaction currentTx,
        BlockExecutionTracer executionTracer,
        ProcessingOptions processingOptions,
        IWorldState stateProvider)
    {
        if (processingOptions.ContainsFlag(ProcessingOptions.DoNotVerifyNonce))
        {
            currentTx.Nonce = stateProvider.GetNonce(currentTx.SenderAddress!);
        }

        using ITxTracer tracer = executionTracer.StartNewTxTrace(currentTx);
        TransactionResult result = transactionProcessor.Execute(currentTx, stateProvider, in blkCtx, executionTracer);
        executionTracer.EndTxTrace();
        return result;
    }
}
