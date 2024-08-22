// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using Nethermind.Core;
using Nethermind.Evm.Tracing;

namespace Nethermind.Evm.TransactionProcessing
{
    public class ChangeableTransactionProcessorAdapter : ITransactionProcessorAdapter
    {
        public ITransactionProcessorAdapter CurrentAdapter { get; set; }
        public ITransactionProcessor TransactionProcessor { get; }

        private ChangeableTransactionProcessorAdapter(ITransactionProcessorAdapter adapter)
        {
            CurrentAdapter = adapter;
        }

        public ChangeableTransactionProcessorAdapter(ITransactionProcessor transactionProcessor)
            : this(new ExecuteTransactionProcessorAdapter(transactionProcessor))
        {
            TransactionProcessor = transactionProcessor;
        }

        public TransactionResult Execute(Transaction transaction, in BlockExecutionContext blkCtx, ITxTracer txTracer,
            Dictionary<Address, AccountOverride>? stateOverride = null) =>
            CurrentAdapter.Execute(transaction, in blkCtx, txTracer, stateOverride);
    }
}
