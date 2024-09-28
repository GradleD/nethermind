// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Int256;

namespace Nethermind.Crypto
{
    public class NullEthereumEcdsa : IEthereumEcdsa
    {
        public static NullEthereumEcdsa Instance { get; } = new();

        public ulong ChainId => 0;

        private NullEthereumEcdsa()
        {
        }

        public Signature Sign(PrivateKey privateKey, Hash256 message)
        {
            throw new InvalidOperationException($"{nameof(NullEthereumEcdsa)} does not expect any calls");
        }

        public PublicKey RecoverPublicKey(Signature signature, Hash256 message)
        {
            throw new InvalidOperationException($"{nameof(NullEthereumEcdsa)} does not expect any calls");
        }

        public CompressedPublicKey RecoverCompressedPublicKey(Signature signature, Hash256 message)
        {
            throw new InvalidOperationException($"{nameof(NullEthereumEcdsa)} does not expect any calls");
        }

        public void Sign(PrivateKey privateKey, Transaction tx, bool _)
        {
            throw new InvalidOperationException($"{nameof(NullEthereumEcdsa)} does not expect any calls");
        }

        public Address RecoverAddress(Transaction tx, bool useSignatureChainId = false)
        {
            throw new InvalidOperationException($"{nameof(NullEthereumEcdsa)} does not expect any calls");
        }

        public Address RecoverAddress(Signature signature, Hash256 message)
        {
            throw new InvalidOperationException($"{nameof(NullEthereumEcdsa)} does not expect any calls");
        }

        public Address RecoverAddress(Span<byte> signatureBytes, Hash256 message)
        {
            throw new InvalidOperationException($"{nameof(NullEthereumEcdsa)} does not expect any calls");
        }

        public bool Verify(Address sender, Transaction tx)
        {
            throw new InvalidOperationException($"{nameof(NullEthereumEcdsa)} does not expect any calls");
        }

        public Address? RecoverAddress(AuthorizationTuple tuple)
        {
            throw new InvalidOperationException($"{nameof(NullEthereumEcdsa)} does not expect any calls");
        }
    }
}
