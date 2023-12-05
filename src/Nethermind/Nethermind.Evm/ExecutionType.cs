// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Runtime.CompilerServices;

namespace Nethermind.Evm
{
    public static class ExecutionTypeExtensions
    {
        // did not want to use flags here specifically
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsAnyCreate(this ExecutionType executionType) =>
            executionType is ExecutionType.CREATE or ExecutionType.CREATE2;
    }

    // ReSharper disable InconsistentNaming IdentifierTypo
    public enum ExecutionType
    {
        TRANSACTION,
        CALL,
        STATICCALL,
        CALLCODE,
        DELEGATECALL,
        CREATE,
        CREATE2
    }
    // ReSharper restore IdentifierTypo InconsistentNaming
}
