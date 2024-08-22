// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Core.Crypto;
using Nethermind.Crypto;
using Nethermind.Int256;
using Nethermind.Logging;
using System;
using System.Diagnostics.CodeAnalysis;

namespace Nethermind.Core;
public class AuthorizationTuple(
    ulong chainId,
    Address codeAddress,
    ulong nonce,
    Signature sig,
    Address? authority = null)
{
    public AuthorizationTuple(
        ulong chainId,
        Address codeAddress,
        ulong nonce,
        ulong yParity,
        byte[] r,
        byte[] s,
        Address? authority = null) : this(chainId, codeAddress, nonce, new Signature(r, s, yParity + Signature.VOffset), authority)
    { }

    public ulong ChainId { get; } = chainId;
    public Address CodeAddress { get; } = codeAddress ?? throw new ArgumentNullException(nameof(codeAddress));
    public ulong Nonce { get; } = nonce;
    public Signature AuthoritySignature { get; } = sig ?? throw new ArgumentNullException(nameof(sig));

    /// <summary>
    /// <see cref="Authority"/> may be recovered at a later point.
    /// </summary>
    public Address? Authority { get; set; } = authority;
}