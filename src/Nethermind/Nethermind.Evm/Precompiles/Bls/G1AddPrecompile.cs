// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Core;
using Nethermind.Core.Specs;

using G1 = Nethermind.Crypto.Bls.P1;

namespace Nethermind.Evm.Precompiles.Bls;

/// <summary>
/// https://eips.ethereum.org/EIPS/eip-2537
/// </summary>
public class G1AddPrecompile : IPrecompile<G1AddPrecompile>
{
    public static readonly G1AddPrecompile Instance = new();

    private G1AddPrecompile()
    {
    }

    public static Address Address { get; } = Address.FromNumber(0x0b);

    public long BaseGasCost(IReleaseSpec releaseSpec) => 500L;

    public long DataGasCost(in ReadOnlyMemory<byte> inputData, IReleaseSpec releaseSpec) => 0L;

    public (ReadOnlyMemory<byte>, bool) Run(in ReadOnlyMemory<byte> inputData, IReleaseSpec releaseSpec)
    {
        const int expectedInputLength = 2 * BlsParams.LenG1;
        if (inputData.Length != expectedInputLength)
        {
            return IPrecompile.Failure;
        }

        try
        {
            G1 x = BlsExtensions.DecodeG1(inputData[..BlsParams.LenG1].Span, out bool xInfinity);
            G1 y = BlsExtensions.DecodeG1(inputData[BlsParams.LenG1..].Span, out bool yInfinity);

            if (xInfinity)
            {
                // x == inf
                return (inputData[BlsParams.LenG1..], true);
            }

            if (yInfinity)
            {
                // y == inf
                return (inputData[..BlsParams.LenG1], true);
            }

            G1 res = x.Add(y);
            return (res.Encode(), true);
        }
        catch (BlsExtensions.BlsPrecompileException)
        {
            return IPrecompile.Failure;
        }
    }
}
