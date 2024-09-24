// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Linq;
using Nethermind.Core;
using Nethermind.Core.Specs;
using G1 = Nethermind.Crypto.Bls.P1;
using System.Collections.Generic;
using System.Threading.Tasks;
using Nethermind.Core.Extensions;
using Nethermind.Core.Collections;

namespace Nethermind.Evm.Precompiles.Bls;

/// <summary>
/// https://eips.ethereum.org/EIPS/eip-2537
/// </summary>
public class G1MultiMulPrecompile : IPrecompile<G1MultiMulPrecompile>
{
    public static readonly G1MultiMulPrecompile Instance = new();

    private G1MultiMulPrecompile()
    {
    }

    public static Address Address { get; } = Address.FromNumber(0x0d);

    public long BaseGasCost(IReleaseSpec releaseSpec) => 0L;

    public long DataGasCost(ReadOnlyMemory<byte> inputData, IReleaseSpec releaseSpec)
    {
        int k = inputData.Length / ItemSize;
        return 12000L * k * Discount.For(k) / 1000;
    }

    private const int ItemSize = 160;

    public (ReadOnlyMemory<byte>, bool) Run(ReadOnlyMemory<byte> inputData, IReleaseSpec releaseSpec)
    {
        if (inputData.Length % ItemSize > 0 || inputData.Length == 0)
        {
            return IPrecompile.Failure;
        }

        try
        {
            int nItems = inputData.Length / ItemSize;

            long[] rawPoints = new long[nItems * 18];
            byte[] rawScalars = new byte[nItems * 32];
            using ArrayPoolList<int> pointDestinations = new(nItems);
            
            int npoints = 0;
            for (int i = 0; i < nItems; i++)
            {
                int offset = i * ItemSize;
                ReadOnlySpan<byte> rawPoint = inputData[offset..(offset + BlsParams.LenG1)].Span;

                // exclude infinity points
                int dest = rawPoint.ContainsAnyExcept((byte)0) ? npoints++ : -1;
                pointDestinations.Add(dest);
            }

            if (npoints == 0)
            {
                return (Enumerable.Repeat<byte>(0, 128).ToArray(), true);
            }

            bool fail = false;
            Parallel.ForEach(pointDestinations, (dest, _, i) => {
                if (dest != -1)
                {
                    int offset = (int)i * ItemSize;
                    ReadOnlySpan<byte> rawPoint = inputData[offset..(offset + BlsParams.LenG1)].Span;
                    ReadOnlySpan<byte> rawScalar = inputData[(offset + BlsParams.LenG1)..(offset + ItemSize)].Span;

                    G1 p = new(rawPoints.AsSpan()[((int)i * 18)..]);
                    p.DecodeRaw(rawPoint);

                    if (!p.InGroup())
                    {
                        fail = true;
                    }

                    for (int j = 0; j < 32; j++)
                    {
                        rawScalars[(dest * 32) + j] = rawScalar[31 - j];
                    }
                }
            });

            if (fail)
            {
                return IPrecompile.Failure;
            }

            G1 res = new G1().MultiMult(rawPoints, rawScalars, npoints);
            return (res.EncodeRaw(), true);
        }
        catch (BlsExtensions.BlsPrecompileException)
        {
            return IPrecompile.Failure;
        }
    }
}
