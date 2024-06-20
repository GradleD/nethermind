// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;

namespace Nethermind.Crypto;

using G1 = Bls.P1;
using G2 = Bls.P2;
using GT = Bls.PT;

public class BlsSigner
{
    internal static readonly string Cryptosuite = "BLS_SIG_BLS12381G2_XMD:SHA-256_SSWU_RO_POP_";
    internal static int InputLength = 64;

    public static Signature Sign(PrivateKey privateKey, ReadOnlySpan<byte> message)
    {
        G2 p = new();
        p.hash_to(message.ToArray(), Cryptosuite);
        p.sign_with(new Bls.SecretKey(privateKey.Bytes, Bls.ByteOrder.LittleEndian));
        Signature s = new()
        {
            Bytes = p.compress()
        };
        return s;
    }

    public static bool Verify(PublicKey publicKey, Signature signature, ReadOnlySpan<byte> message)
    {
        try
        {
            G2 sig = new(signature.Bytes);
            GT p1 = new(sig, G1.generator());

            G2 m = new();
            m.hash_to(message.ToArray(), Cryptosuite);
            G1 pk = new(publicKey.Bytes);
            GT p2 = new(m, pk);

            return GT.finalverify(p1, p2);
        }
        catch (Bls.Exception)
        {
            // point not on curve
            return false;
        }
    }

    public static PublicKey GetPublicKey(PrivateKey privateKey)
    {
        Bls.SecretKey sk = new(privateKey.Bytes, Bls.ByteOrder.LittleEndian);
        G1 p = new(sk);
        PublicKey pk = new()
        {
            Bytes = p.compress()
        };
        return pk;
    }

    public struct PrivateKey
    {
        public byte[] Bytes = new byte[32];
        public PrivateKey()
        {
        }
    }

    public struct PublicKey
    {
        public byte[] Bytes = new byte[48];

        public PublicKey()
        {
        }
    }

    public struct Signature
    {
        public byte[] Bytes = new byte[96];

        public Signature()
        {
        }
    }
}
