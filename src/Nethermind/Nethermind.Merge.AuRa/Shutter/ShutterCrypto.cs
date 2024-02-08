// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.IdentityModel.Tokens;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Int256;
using Nethermind.Crypto;

[assembly: InternalsVisibleTo("Nethermind.Merge.AuRa.Test")]

namespace Nethermind.Merge.AuRa.Shutter;

using G1 = Bls.P1;
using G2 = Bls.P2;
using GT = Bls.PT;

internal class ShutterCrypto
{
    internal static readonly BigInteger BlsSubgroupOrder = new([0x73, 0xed, 0xa7, 0x53, 0x29, 0x9d, 0x7d, 0x48, 0x33, 0x39, 0xd8, 0x08, 0x09, 0xa1, 0xd8, 0x05, 0x53, 0xbd, 0xa4, 0x02, 0xff, 0xfe, 0x5b, 0xfe, 0xff, 0xff, 0xff, 0xff, 0x00, 0x00, 0x00, 0x01], true, true);
    public struct EncryptedMessage
    {
        public G2 c1;
        public Bytes32 c2;
        public IEnumerable<Bytes32> c3;
    }

    public static G1 ComputeIdentity(Bytes32 identityPrefix, Address sender)
    {
        Span<byte> identity = stackalloc byte[52];
        identityPrefix.Unwrap().CopyTo(identity);
        sender.Bytes.CopyTo(identity[32..]);
        // todo: reverse?
        return new G1(Keccak.Compute(identity).Bytes.ToArray());
    }

    public static byte[] Decrypt(EncryptedMessage encryptedMessage, G1 key)
    {
        Bytes32 sigma = RecoverSigma(encryptedMessage, key);
        IEnumerable<Bytes32> keys = ComputeBlockKeys(sigma, encryptedMessage.c3.Count());
        IEnumerable<Bytes32> decryptedBlocks = Enumerable.Zip(keys, encryptedMessage.c3, XorBlocks);
        byte[] msg = UnpadAndJoin(decryptedBlocks);

        UInt256 r = ComputeR(sigma, msg);
        G2 expectedC1 = ComputeC1(r);
        if (!expectedC1.is_equal(encryptedMessage.c1))
        {
            throw new Exception("Could not decrypt message.");
        }

        return msg;
    }

    public static Bytes32 RecoverSigma(EncryptedMessage encryptedMessage, G1 decryptionKey)
    {
        GT p = new(decryptionKey, encryptedMessage.c1);
        Bytes32 key = HashGTToBlock(p);
        Bytes32 sigma = XorBlocks(encryptedMessage.c2, key);
        return sigma;
    }

    public static UInt256 ComputeR(Bytes32 sigma, ReadOnlySpan<byte> msg)
    {
        return HashBlocksToInt([sigma, HashBytesToBlock(msg)]);
    }

    public static G2 ComputeC1(UInt256 r)
    {
        return G2.generator().mult(r.ToLittleEndian());
    }

    // helper functions
    public static IEnumerable<Bytes32> ComputeBlockKeys(Bytes32 sigma, int n)
    {
        // suffix_length = max((n.bit_length() + 7) // 8, 1)
        // suffixes = [n.to_bytes(suffix_length, "big")]
        // preimages = [sigma + suffix for suffix in suffixes]
        // keys = [hash_bytes_to_block(preimage) for preimage in preimages]
        // return keys
        // int bitLength = 0;
        // for (int x = n; x > 0; x >>= 1)
        // {
        //     bitLength++;
        // }

        // int suffixLength = int.Max((bitLength + 7) / 8, 1);

        // todo: is actual implementation correct?
        return Enumerable.Range(0, n).Select(suffix =>
        {
            Span<byte> preimage = stackalloc byte[36];
            sigma.Unwrap().CopyTo(preimage);
            BinaryPrimitives.WriteInt32BigEndian(preimage[32..], suffix);
            return HashBytesToBlock(preimage);
        });
    }

    public static Bytes32 XorBlocks(Bytes32 x, Bytes32 y)
    {
        return new(x.Unwrap().Xor(y.Unwrap()));
    }

    public static byte[] UnpadAndJoin(IEnumerable<Bytes32> blocks)
    {
        if (blocks.IsNullOrEmpty())
        {
            return [];
        }

        Bytes32 lastBlock = blocks.Last();
        byte n = lastBlock.Unwrap().Last();

        if (n == 0 || n > 32)
        {
            throw new Exception("Invalid padding length");
        }

        byte[] res = new byte[(blocks.Count() * 32) - n];

        for (int i = 0; i < blocks.Count() - 1; i++)
        {
            blocks.ElementAt(i).Unwrap().CopyTo(res.AsSpan()[(i * 32)..]);
        }

        for (int i = 0; i < (32 - n); i++)
        {
            res[((blocks.Count() - 1) * 32) + i] = lastBlock.Unwrap()[i];
        }

        return res;
    }

    public static Bytes32 HashBytesToBlock(ReadOnlySpan<byte> bytes)
    {
        return new(Keccak.Compute(bytes).Bytes);
    }

    public static UInt256 HashBlocksToInt(IEnumerable<Bytes32> blocks)
    {
        Span<byte> combinedBlocks = stackalloc byte[blocks.Count() * 32];

        for (int i = 0; i < blocks.Count(); i++)
        {
            blocks.ElementAt(i).Unwrap().CopyTo(combinedBlocks[(32 * i)..]);
        }

        Span<byte> hash = Keccak.Compute(combinedBlocks).Bytes;
        BigInteger v = new BigInteger(hash, true, true) % BlsSubgroupOrder;

        return new(v.ToBigEndianByteArray(32));
    }

    public static Bytes32 HashGTToBlock(GT p)
    {
        return HashBytesToBlock(p.final_exp().to_bendian());
    }

    public static GT GTExp(GT x, UInt256 exp)
    {
        GT a = x;
        GT acc = GT.one();

        for (; exp > 0; exp >>= 1)
        {
            if ((exp & 1) == 1)
            {
                acc.mul(a);
            }
            a.sqr();
        }

        return acc;
    }
}
