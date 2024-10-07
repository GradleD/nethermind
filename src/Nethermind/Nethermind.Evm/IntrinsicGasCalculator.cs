// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Nethermind.Core;
using Nethermind.Core.Eip2930;
using Nethermind.Core.Extensions;
using Nethermind.Core.Specs;
using Nethermind.Int256;
namespace Nethermind.Evm;

public static class IntrinsicGasCalculator
{
    public static long Calculate(Transaction transaction, IReleaseSpec releaseSpec, out long floorGas) =>
        GasCostOf.Transaction
        + DataCost(transaction, releaseSpec, out floorGas)
        + CreateCost(transaction, releaseSpec)
        + AccessListCost(transaction, releaseSpec)
        + AuthorizationListCost(transaction, releaseSpec);

    private static long CreateCost(Transaction transaction, IReleaseSpec releaseSpec) =>
        transaction.IsContractCreation && releaseSpec.IsEip2Enabled ? GasCostOf.TxCreate : 0;

    private static long DataCost(Transaction transaction, IReleaseSpec releaseSpec, out long floorGas)
    {
        long txDataNonZeroMultiplier = releaseSpec.IsEip2028Enabled
            ? GasCostOf.TxDataNonZeroMultiplierEip2028
            : GasCostOf.TxDataNonZeroMultiplier;
        Span<byte> data = transaction.Data.GetValueOrDefault().Span;

        int totalZeros = data.CountZeros();

        long baseDataCost = transaction.IsContractCreation && releaseSpec.IsEip3860Enabled
            ? EvmPooledMemory.Div32Ceiling((UInt256)data.Length) * GasCostOf.InitCodeWord
            : 0;

        var tokensInCallData = totalZeros + (data.Length - totalZeros) * txDataNonZeroMultiplier;

        floorGas = releaseSpec.IsEip7623Enabled
            ? GasCostOf.Transaction + tokensInCallData * GasCostOf.TotalCostFloorPerTokenEip7623
            : 0;

        return baseDataCost + tokensInCallData * GasCostOf.TxDataZero;
    }

    private static long AccessListCost(Transaction transaction, IReleaseSpec releaseSpec)
    {
        AccessList? accessList = transaction.AccessList;
        if (accessList is not null)
        {
            if (!releaseSpec.UseTxAccessLists)
            {
                ThrowInvalidDataException(releaseSpec);
            }

            (int addressesCount, int storageKeysCount) = accessList.Count;
            return addressesCount * GasCostOf.AccessAccountListEntry + storageKeysCount * GasCostOf.AccessStorageListEntry;
        }

        return 0;

        [DoesNotReturn]
        [StackTraceHidden]
        static void ThrowInvalidDataException(IReleaseSpec releaseSpec)
        {
            throw new InvalidDataException($"Transaction with an authorization list received within the context of {releaseSpec.Name}. Eip-7702 is not enabled.");
        }
    }

    private static long AuthorizationListCost(Transaction transaction, IReleaseSpec releaseSpec)
    {
        AuthorizationTuple[]? transactionAuthorizationList = transaction.AuthorizationList;

        if (transactionAuthorizationList is not null)
        {
            if (!releaseSpec.IsAuthorizationListEnabled)
            {
                ThrowInvalidDataException(releaseSpec);
            }

            return transactionAuthorizationList.Length * GasCostOf.NewAccount;
        }

        return 0;

        [DoesNotReturn]
        [StackTraceHidden]
        static void ThrowInvalidDataException(IReleaseSpec releaseSpec)
        {
            throw new InvalidDataException($"Transaction with an authorization list received within the context of {releaseSpec.Name}. Eip-7702 is not enabled.");
        }
    }
}
