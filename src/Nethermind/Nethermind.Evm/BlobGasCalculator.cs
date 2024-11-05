// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.IO;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.Int256;

namespace Nethermind.Evm;

public static class BlobGasCalculator
{
    public static ulong CalculateBlobGas(int blobCount) =>
        (ulong)blobCount * Eip4844Constants.GasPerBlob;

    public static ulong CalculateBlobGas(Transaction transaction) =>
        CalculateBlobGas(transaction.BlobVersionedHashes?.Length ?? 0);

    public static ulong CalculateBlobGas(Transaction[] transactions)
    {
        int blobCount = 0;
        foreach (Transaction tx in transactions)
        {
            if (tx.SupportsBlobs)
            {
                blobCount += tx.BlobVersionedHashes!.Length;
            }
        }

        return CalculateBlobGas(blobCount);
    }

    public static bool TryCalculateBlobBaseFee(BlockHeader header, Transaction transaction, out UInt256 blobBaseFee, IReleaseSpec spec)
    {
        if (!TryCalculateFeePerBlobGas(header.ExcessBlobGas, out UInt256 feePerBlobGas, header.TargetBlobCount, spec))
        {
            blobBaseFee = UInt256.MaxValue;
            return false;
        }
        return !UInt256.MultiplyOverflow(CalculateBlobGas(transaction), feePerBlobGas, out blobBaseFee);
    }

    public static bool TryCalculateFeePerBlobGas(BlockHeader header, out UInt256 feePerBlobGas, IReleaseSpec spec)
    {
        return TryCalculateFeePerBlobGas(header.ExcessBlobGas, out feePerBlobGas, header.TargetBlobCount, spec);
    }

    public static bool TryCalculateFeePerBlobGas(ulong? excessBlobGas, out UInt256 feePerBlobGas, UInt256? targetBlobCount, IReleaseSpec? spec)
    {
        static bool FakeExponentialOverflow(UInt256 factor, UInt256 num, UInt256 denominator, out UInt256 feePerBlobGas)
        {
            UInt256 output = UInt256.Zero;

            if (UInt256.MultiplyOverflow(factor, denominator, out UInt256 numAccum))
            {
                feePerBlobGas = UInt256.MaxValue;
                return true;
            }

            for (UInt256 i = 1; numAccum > 0; i++)
            {
                if (UInt256.AddOverflow(output, numAccum, out output))
                {
                    feePerBlobGas = UInt256.MaxValue;
                    return true;
                }

                if (UInt256.MultiplyOverflow(numAccum, num, out UInt256 updatedNumAccum))
                {
                    feePerBlobGas = UInt256.MaxValue;
                    return true;
                }

                if (UInt256.MultiplyOverflow(i, denominator, out UInt256 multipliedDeniminator))
                {
                    feePerBlobGas = UInt256.MaxValue;
                    return true;
                }

                numAccum = updatedNumAccum / multipliedDeniminator;
            }

            feePerBlobGas = output / denominator;
            return false;
        }

        var denominator = spec?.IsEip7742Enabled ?? false
            ? Eip7742Constants.BlobGasPriceUpdateFraction * targetBlobCount
              ?? throw new InvalidDataException("header is missing target blob count")
            : Eip4844Constants.BlobGasPriceUpdateFraction;

        feePerBlobGas = UInt256.MaxValue;
        return excessBlobGas is not null && !FakeExponentialOverflow(Eip4844Constants.MinBlobGasPrice, excessBlobGas.Value, denominator, out feePerBlobGas);
    }

    public static ulong? CalculateExcessBlobGas(BlockHeader? parentBlockHeader, IReleaseSpec releaseSpec, BlockHeader header)
    {
        if (!releaseSpec.IsEip4844Enabled)
        {
            return null;
        }

        if (parentBlockHeader is null)
        {
            return 0;
        }

        ulong excessBlobGas = parentBlockHeader.ExcessBlobGas ?? 0;
        excessBlobGas += parentBlockHeader.BlobGasUsed ?? 0;
        var targetBlobCount = releaseSpec.IsEip7742Enabled
            ? header.TargetBlobCount * Eip4844Constants.GasPerBlob
              ?? throw new InvalidDataException("header is missing target blob count")
            : Eip4844Constants.TargetBlobGasPerBlock;

        return excessBlobGas < targetBlobCount
            ? 0
            : excessBlobGas - targetBlobCount;
    }
}
