// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Core;
using Nethermind.Int256;

namespace Nethermind.Serialization.Rlp.RlpWriter;

public sealed class RlpStreamWriter(RlpStream stream) : IRlpWriter
{
    public void Write(byte value)
    {
        stream.Encode(value);
    }

    public void Write(int value)
    {
        stream.Encode(value);
    }

    public void Write(ulong value)
    {
        stream.Encode(value);
    }

    public void Write(ReadOnlySpan<byte> value)
    {
        stream.Encode(value);
    }

    public void Write(UInt256 value)
    {
        stream.Encode(value);
    }

    public void Write(long value)
    {
        stream.Encode(value);
    }

    public void Write(Address value)
    {
        stream.Encode(value);
    }

    public void Write(Memory<byte>? value)
    {
        stream.Encode(value);
    }

    public void Write(byte[] value)
    {
        stream.Encode(value);
    }

    public void Write(Rlp value)
    {
        stream.Encode(value);
    }

    public void Write(byte[]?[] value)
    {
        stream.Encode(value);
    }
}
