// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Serialization.Rlp;

namespace Nethermind.Optimism.CL;

public class ChannelDecoder
{
    public static (BatchV1[], byte[]) DecodeChannel(Frame frame)
    {
        // TODO: avoid allocation
        var memoryStream = new MemoryStream();
        if ((frame.FrameData[0] & 0x0F) == 8 || (frame.FrameData[0] & 0x0F) == 15)
        {
            // zlib
            // TODO: test
            var deflateStream = new DeflateStream(new MemoryStream(frame.FrameData[2..]), CompressionMode.Decompress);
            deflateStream.CopyTo(memoryStream);
        } else if (frame.FrameData[0] == 1)
        {
            // brotli
            BrotliStream stream = new BrotliStream(new MemoryStream(frame.FrameData[1..]), CompressionMode.Decompress);
            stream.CopyTo(memoryStream);
        }
        else
        {
            throw new Exception($"Unsupported compression algorithm {frame.FrameData[0]}");
        }

        byte[] decompressed = memoryStream.ToArray();

        RlpStream rlpStream = new RlpStream(decompressed);
        List<BatchV1> batches = new();
        // while (rlpStream.Position < rlpStream.Length)
        // {
            BatchV1 batch = BatchDecoder.Instance.DecodeSpanBatch(rlpStream, RlpBehaviors.AllowExtraBytes);
            batches.Add(batch);
        // }
        return (batches.ToArray(), decompressed);
    }
}

public struct BatchV0
{
    public Hash256 ParentHash;
    public ulong EpochNumber;
    public Hash256 EpochHash;
    public ulong Timestamp;
    public byte[][] Transactions;
}

public struct BatchV1
{
    public ulong RelTimestamp;
    public ulong L1OriginNum;
    public byte[] ParentCheck; // 20 bytes
    public byte[] L1OriginCheck; // 20 bytes
    public ulong BlockCount;
    public BigInteger OriginBits;
    public ulong[] BlockTxCounts;
    public byte[][] Txs;
}
