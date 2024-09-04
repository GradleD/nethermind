// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Int256;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Nethermind.Serialization.Rlp;

public class AuthorizationTupleDecoder : IRlpStreamDecoder<AuthorizationTuple>, IRlpValueDecoder<AuthorizationTuple>
{
    public AuthorizationTuple Decode(RlpStream stream, RlpBehaviors rlpBehaviors = RlpBehaviors.None)
    {
        int length = stream.ReadSequenceLength();
        int check = length + stream.Position;

        var chainId = stream.DecodeULong();
        Address? codeAddress = stream.DecodeAddress();
        ulong nonce = stream.DecodeULong();

        ulong yParity = stream.DecodeULong();
        UInt256 r = stream.DecodeUInt256();
        UInt256 s = stream.DecodeUInt256();
        if (!rlpBehaviors.HasFlag(RlpBehaviors.AllowExtraBytes))
            stream.Check(check);
        return new AuthorizationTuple(
            chainId,
            codeAddress!,
            nonce,
            yParity,
            r,
            s);
    }

    public AuthorizationTuple Decode(ref Rlp.ValueDecoderContext decoderContext, RlpBehaviors rlpBehaviors = RlpBehaviors.None)
    {
        int length = decoderContext.ReadSequenceLength();
        int check = length + decoderContext.Position;

        var chainId = decoderContext.DecodeULong();
        Address codeAddress = decoderContext.DecodeAddress();

        ulong nonce = decoderContext.DecodeULong();

        ulong yParity = decoderContext.DecodeULong();
        UInt256 r = decoderContext.DecodeUInt256();
        UInt256 s = decoderContext.DecodeUInt256();
        if (!rlpBehaviors.HasFlag(RlpBehaviors.AllowExtraBytes))
            decoderContext.Check(check);
        return new AuthorizationTuple(
            chainId,
            codeAddress!,
            nonce,
            yParity,
            r,
            s);
    }

    public RlpStream Encode(AuthorizationTuple item, RlpBehaviors rlpBehaviors = RlpBehaviors.None)
    {
        RlpStream stream = new(GetLength(item, rlpBehaviors));
        Encode(stream, item, rlpBehaviors);
        return stream;
    }

    public void Encode(RlpStream stream, AuthorizationTuple item, RlpBehaviors rlpBehaviors = RlpBehaviors.None)
    {
        int contentLength = GetContentLength(item);
        stream.StartSequence(contentLength);
        stream.Encode(item.ChainId);
        stream.Encode(item.CodeAddress);
        stream.Encode(item.Nonce);
        stream.Encode(item.AuthoritySignature.RecoveryId);
        stream.Encode(new UInt256(item.AuthoritySignature.R, true));
        stream.Encode(new UInt256(item.AuthoritySignature.S, true));
    }

    public RlpStream EncodeWithoutSignature(ulong chainId, Address codeAddress, ulong nonce)
    {
        int contentLength = GetContentLengthWithoutSig(chainId, codeAddress, nonce);
        var totalLength = Rlp.LengthOfSequence(contentLength);
        RlpStream stream = new(totalLength);
        EncodeWithoutSignature(stream, chainId, codeAddress, nonce);
        return stream;
    }

    public void EncodeWithoutSignature(RlpStream stream, ulong chainId, Address codeAddress, ulong nonce)
    {
        int contentLength = GetContentLengthWithoutSig(chainId, codeAddress, nonce);
        stream.StartSequence(contentLength);
        stream.Encode(chainId);
        stream.Encode(codeAddress ?? throw new RlpException($"Invalid tx {nameof(AuthorizationTuple)} format - address is null"));
        stream.Encode(nonce);
    }

    public int GetLength(AuthorizationTuple item, RlpBehaviors rlpBehaviors) => Rlp.LengthOfSequence(GetContentLength(item));

    private static int GetContentLength(AuthorizationTuple tuple) =>
        GetContentLengthWithoutSig(tuple.ChainId, tuple.CodeAddress, tuple.Nonce)
        + Rlp.LengthOf(tuple.AuthoritySignature.RecoveryId)
        + Rlp.LengthOf(new UInt256(tuple.AuthoritySignature.R.AsSpan(), true))
        + Rlp.LengthOf(new UInt256(tuple.AuthoritySignature.S.AsSpan(), true));

    private static int GetContentLengthWithoutSig(ulong chainId, Address codeAddress, ulong nonce) =>
        Rlp.LengthOf(chainId)
        + Rlp.LengthOf(codeAddress)
        + Rlp.LengthOf(nonce);

    [DoesNotReturn]
    [StackTraceHidden]
    private static UInt256 ThrowInvalidNonceRlpException() =>
        throw new RlpException("Invalid nonce length in authorization tuple.");
}
