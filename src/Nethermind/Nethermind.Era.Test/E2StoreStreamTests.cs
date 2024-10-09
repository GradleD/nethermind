// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using DotNetty.Buffers;
using FluentAssertions;
using Nethermind.Serialization.Rlp;
using Snappier;

namespace Nethermind.Era1.Test;
internal class E2StoreStreamTests
{
    [TestCase(EntryTypes.Version)]
    [TestCase(EntryTypes.CompressedHeader)]
    [TestCase(EntryTypes.CompressedBody)]
    [TestCase(EntryTypes.CompressedReceipts)]
    [TestCase(EntryTypes.Accumulator)]
    [TestCase(EntryTypes.BlockIndex)]
    public async Task WriteEntry_WritingAnEntry_WritesCorrectHeaderType(ushort type)
    {
        using MemoryStream stream = new MemoryStream();
        using E2StoreStream sut = new E2StoreStream(stream);

        await sut.WriteEntry(type, Array.Empty<byte>());

        Assert.That(BitConverter.ToInt16(stream.ToArray()), Is.EqualTo(type));
    }

    [TestCase(6)]
    [TestCase(20)]
    [TestCase(32)]
    public async Task WriteEntry_WritingAnEntry_WritesCorrectLengthInHeader(int length)
    {
        using MemoryStream stream = new MemoryStream();
        using E2StoreStream sut = new E2StoreStream(stream);

        await sut.WriteEntry(EntryTypes.CompressedHeader, new byte[length]);

        Assert.That(BitConverter.ToInt32(stream.ToArray(), 2), Is.EqualTo(length));
    }

    [TestCase(1)]
    [TestCase(5)]
    [TestCase(12)]
    public async Task WriteEntry_WritingAnEntry_ReturnCorrectNumberofBytesWritten(int length)
    {
        using MemoryStream stream = new MemoryStream();
        using E2StoreStream sut = new E2StoreStream(stream);

        int result = await sut.WriteEntry(EntryTypes.CompressedHeader, new byte[length]);

        Assert.That(result, Is.EqualTo(length + E2StoreStream.HeaderSize));
    }


    [Test]
    public async Task WriteEntry_WritingAnEntry_ZeroesAtCorrectIndexesInHeader()
    {
        using MemoryStream stream = new MemoryStream();
        using E2StoreStream sut = new E2StoreStream(stream);

        await sut.WriteEntry(EntryTypes.CompressedHeader, new byte[] { 0xff, 0xff, 0xff, 0xff });
        byte[] bytes = stream.ToArray();

        Assert.That(bytes[6], Is.EqualTo(0));
        Assert.That(bytes[7], Is.EqualTo(0));
    }

    [Test]
    public async Task WriteEntry_WritingEntryValue_BytesAreWrittenToStream()
    {
        using MemoryStream stream = new MemoryStream();
        using E2StoreStream sut = new E2StoreStream(stream);

        byte[] bytes = new byte[] { 0x0f, 0xf0, 0xff, 0xff };
        await sut.WriteEntry(EntryTypes.CompressedHeader, bytes);
        byte[] result = stream.ToArray();

        Assert.That(new ArraySegment<byte>(result, E2StoreStream.HeaderSize, bytes.Length), Is.EquivalentTo(bytes));
    }

    [Test]
    public async Task WriteEntryAsSnappy_WritingEntryValue_WritesEncodedBytesToStream()
    {
        using MemoryStream stream = new MemoryStream();
        using E2StoreStream sut = new E2StoreStream(stream);
        byte[] bytes = new byte[] { 0x0f, 0xf0, 0xff, 0xff };

        await sut.WriteEntryAsSnappy(EntryTypes.CompressedHeader, bytes);
        stream.Position = E2StoreStream.HeaderSize;
        using var snappy = new SnappyStream(stream, System.IO.Compression.CompressionMode.Decompress);
        byte[] buffer = new byte[32];

        Assert.That(() => snappy.Read(buffer), Throws.Nothing);
    }

    [Test]
    public async Task WriteEntryAsSnappy_WritingEntryValue_ReturnsCompressedSize()
    {
        using MemoryStream stream = new MemoryStream();
        using E2StoreStream sut = new E2StoreStream(stream);
        byte[] bytes = new byte[] { 0x0f, 0xf0, 0xff, 0xff };

        int result = await sut.WriteEntryAsSnappy(EntryTypes.CompressedHeader, bytes);

        Assert.That(result, Is.EqualTo(stream.Length));
    }

    [Test]
    public async Task ReadEntryValue_ReadingValueBytesOfEntry_ReturnsBytes()
    {
        using MemoryStream stream = new MemoryStream();
        using E2StoreStream sut = new E2StoreStream(stream);
        byte[] bytes = new byte[] { 0x0f, 0xf0, 0xff, 0xff };
        long originalPosition = stream.Position;
        await sut.WriteEntry(EntryTypes.Accumulator, bytes);
        IByteBuffer buffer = UnpooledByteBufferAllocator.Default.Buffer(bytes.Length);
        try
        {
            stream.Position = originalPosition;
            var readBytes = await sut.ReadEntryAndDecode(buf => buf.ReadAllBytesAsArray(), EntryTypes.Accumulator, default);
            Assert.That(readBytes, Is.EquivalentTo(bytes));
        }
        finally
        {
            buffer.Release();
        }
    }

    [Test]
    public async Task ReadEntryValue_ReadingValueBytesOfEntry_ReturnsBytesRead()
    {
        using MemoryStream stream = new MemoryStream();
        using E2StoreStream sut = new E2StoreStream(stream);
        byte[] bytes = new byte[] { 0x0f, 0xf0, 0xff, 0xff };
        long position = stream.Position;
        await sut.WriteEntry(EntryTypes.Accumulator, bytes);
        stream.Position = position;

        var readBytes = await sut.ReadEntryAndDecode(buf => buf.ReadAllBytesAsArray(), EntryTypes.Accumulator, default);
        Assert.That(readBytes, Is.EquivalentTo(bytes));

        Assert.That(readBytes.Length, Is.EqualTo(bytes.Length));
    }

    [Test]
    public async Task ReadEntryValueAsSnappy_ReadingValueBytesOfEntry_ReturnsDecompressedBytes()
    {
        using MemoryStream stream = new();
        using E2StoreStream sut = new E2StoreStream(stream);
        byte[] bytes = new byte[] { 0x0f, 0xf0, 0xff, 0xff };
        MemoryStream compressed = new();
        using SnappyStream snappy = new SnappyStream(compressed, System.IO.Compression.CompressionMode.Compress);
        snappy.Write(bytes);
        snappy.Flush();
        byte[] compressedBytes = compressed.ToArray();
        long originalPosition = stream.Position;
        await sut.WriteEntry(EntryTypes.CompressedHeader, compressedBytes);
        IByteBuffer buffer = UnpooledByteBufferAllocator.Default.Buffer(32);
        try
        {
            stream.Position = originalPosition;

            var readBytes = await sut.ReadSnappyCompressedEntryAndDecode(buf => buf.ReadAllBytesAsArray(), EntryTypes.CompressedHeader, default);
            Assert.That(readBytes, Is.EquivalentTo(bytes));
        }
        finally
        {
            buffer.Release();
        }
    }

    [TestCase(6)]
    [TestCase(14)]
    [TestCase(24)]
    public async Task ReadEntryAt_ReadingEntryAtDifferentOffset_ReturnsCorrectEntry(int offset)
    {
        using MemoryStream stream = new();
        using E2StoreStream sut = new E2StoreStream(stream);
        byte[] bytes = new byte[] { 0xff, 0xff, 0xff, 0xff };
        stream.SetLength(offset);
        stream.Seek(0, SeekOrigin.End);
        await sut.WriteEntry(EntryTypes.CompressedHeader, bytes);

        Entry result = await sut.ReadEntryAt(offset);

        Assert.That(result.Type, Is.EqualTo(EntryTypes.CompressedHeader));
        Assert.That(result.Length, Is.EqualTo(bytes.Length));
    }

    [Test]
    public void Test()
    {
        //TODO possible optimization avoid alloc snappy stream by not disposing the stream and reusing like this
        using MemoryStream stream = new();
        byte[] bytes = new byte[] { 0x0f, 0xf0, 0xff, 0xff };
        byte[] bytes1 = new byte[] { 0x1, 0x2, 0x3, 0x4 };
        MemoryStream compressed = new();
        using SnappyStream snappy = new SnappyStream(compressed, System.IO.Compression.CompressionMode.Compress);
        snappy.Write(bytes);
        snappy.Flush();
        byte[] compressedBytes = compressed.ToArray();
        compressed.Seek(0, SeekOrigin.Begin);
        using SnappyStream snappyDecom = new SnappyStream(compressed, System.IO.Compression.CompressionMode.Decompress);
        var decomBytes = new byte[bytes.Length];
        snappyDecom.Read(decomBytes, 0, decomBytes.Length);
        decomBytes.Should().BeEquivalentTo(bytes);

        compressed.SetLength(0);
        var snappyHeader = new byte[]
        {
            0xff, 0x06, 0x00, 0x00, 0x73, 0x4e, 0x61, 0x50, 0x70, 0x59
        };
        compressed.Write(snappyHeader, 0, snappyHeader.Length);
        snappy.Write(bytes1);
        snappy.Flush();
        compressed.Seek(0, SeekOrigin.Begin);

        snappyDecom.Read(decomBytes, 0, decomBytes.Length);
        decomBytes.Should().BeEquivalentTo(bytes1);

    }

}
