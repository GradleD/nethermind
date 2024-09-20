// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Linq;
using Nethermind.Core;
using Nethermind.Core.Specs;

using G2 = Nethermind.Crypto.Bls.P2;

namespace Nethermind.Evm.Precompiles.Bls;

/// <summary>
/// https://eips.ethereum.org/EIPS/eip-2537
/// </summary>
public class G2MulPrecompile : IPrecompile<G2MulPrecompile>
{
    public static readonly G2MulPrecompile Instance = new();

    private G2MulPrecompile()
    {
    }

    public static Address Address { get; } = Address.FromNumber(0x0f);

    public long BaseGasCost(IReleaseSpec releaseSpec) => 45000L;

    public long DataGasCost(in ReadOnlyMemory<byte> inputData, IReleaseSpec releaseSpec) => 0L;

    public (ReadOnlyMemory<byte>, bool) Run(in ReadOnlyMemory<byte> inputData, IReleaseSpec releaseSpec)
    {
        const int expectedInputLength = BlsParams.LenG2 + BlsParams.LenFr;

        if (inputData.Length != expectedInputLength)
        {
            return IPrecompile.Failure;
        }

        try
        {
            G2 x = BlsExtensions.DecodeG2(inputData[..BlsParams.LenG2].Span, out bool xInfinity);
            if (xInfinity)
            {
                // x == inf
                return (Enumerable.Repeat<byte>(0, 256).ToArray(), true);
            }

            if (!x.InGroup())
            {
                return IPrecompile.Failure;
            }

            byte[] scalar = inputData[BlsParams.LenG2..].ToArray().Reverse().ToArray();

            if (scalar.All(x => x == 0))
            {
                return (Enumerable.Repeat<byte>(0, 256).ToArray(), true);
            }

            G2 res = x.Mult(scalar);
            return (res.Encode(), true);
        }
        catch (BlsExtensions.BlsPrecompileException)
        {
            return IPrecompile.Failure;
        }
    }
}
