// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Core;
using Nethermind.Evm.CodeAnalysis;
using Nethermind.Int256;

namespace Nethermind.Evm
{
    public readonly struct ExecutionEnvironment
    {
        public ExecutionEnvironment
        (
            CodeInfo codeInfo,
            Address executingAccount,
            Address caller,
            Address? codeSource,
            ReadOnlyMemory<byte> inputData,
            in TxExecutionContext txExecutionContext,
            UInt256 transferValue,
            UInt256 value,
            bool isSystemExecutionEnv,
            int callDepth = 0)
        {
            CodeInfo = codeInfo;
            ExecutingAccount = executingAccount;
            Caller = caller;
            CodeSource = codeSource;
            InputData = inputData;
            TxExecutionContext = txExecutionContext;
            TransferValue = transferValue;
            Value = value;
            CallDepth = callDepth;
            IsSystemEnv = isSystemExecutionEnv;
        }

        /// <summary>
        /// Parsed bytecode for the current call.
        /// </summary>
        public readonly CodeInfo CodeInfo = codeInfo;

        /// <summary>
        /// Currently executing account (in DELEGATECALL this will be equal to caller).
        /// </summary>
        public readonly Address ExecutingAccount = executingAccount;

        /// <summary>
        /// Caller
        /// </summary>
        public readonly Address Caller = caller;

        /// <summary>
        /// Bytecode source (account address).
        /// </summary>
        public readonly Address? CodeSource = codeSource;

        /// <summary>
        /// Parameters / arguments of the current call.
        /// </summary>
        public readonly ReadOnlyMemory<byte> InputData = inputData;

        /// <summary>
        /// Transaction originator
        /// </summary>
        public readonly TxExecutionContext TxExecutionContext = txExecutionContext;

        /// <summary>
        /// ETH value transferred in this call.
        /// </summary>
        public readonly UInt256 TransferValue = transferValue;

        /// <summary>
        /// Value information passed (it is different from transfer value in DELEGATECALL.
        /// DELEGATECALL behaves like a library call and it uses the value information from the caller even
        /// as no transfer happens.
        /// </summary>
        public readonly UInt256 Value = value;

        /// <example>If we call TX -> DELEGATECALL -> CALL -> STATICCALL then the call depth would be 3.</example>
        public readonly int CallDepth;

        /// <summary>
        /// this field keeps track of wether the execution envirement was initiated by a systemTx.
        public readonly bool IsSystemEnv;
    }
}
