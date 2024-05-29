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
public class G2AddPrecompile : IPrecompile<G2AddPrecompile>
{
    public static G2AddPrecompile Instance = new G2AddPrecompile();

    private G2AddPrecompile()
    {
    }

    public static Address Address { get; } = Address.FromNumber(0x0e);

    public long BaseGasCost(IReleaseSpec releaseSpec)
    {
        return 800L;
    }

    public long DataGasCost(in ReadOnlyMemory<byte> inputData, IReleaseSpec releaseSpec)
    {
        return 0L;
    }

    public (ReadOnlyMemory<byte>, bool) Run(in ReadOnlyMemory<byte> inputData, IReleaseSpec releaseSpec)
    {
        const int expectedInputLength = 2 * BlsParams.LenG2;
        if (inputData.Length != expectedInputLength)
        {
            return (Array.Empty<byte>(), false);
        }

        (byte[], bool) result;

        try
        {
            G2? x = BlsExtensions.G2FromUntrimmed(inputData[..BlsParams.LenG2]);
            G2? y = BlsExtensions.G2FromUntrimmed(inputData[BlsParams.LenG2..]);

            if (!x.HasValue)
            {
                // x == inf
                return (inputData[BlsParams.LenG2..], true);
            }

            if (!y.HasValue)
            {
                // y == inf
                return (inputData[..BlsParams.LenG2], true);
            }

            if (x.Value.on_curve() && y.Value.on_curve())
            {
                G2 res = x.Value.add(y.Value);
                if (res.is_inf())
                {
                    result = (Enumerable.Repeat<byte>(0, 256).ToArray(), true);
                }
                else
                {
                    result = (res.ToBytesUntrimmed(), true);
                }
            }
            else
            {
                result = (Array.Empty<byte>(), false);
            }
        }
        catch (Exception)
        {
            result = (Array.Empty<byte>(), false);
        }

        return result;
    }
}
