// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using DotNetty.Buffers;
using Nethermind.Core.Collections;
using Snappier;
namespace Nethermind.Era1;

internal class E2Store : IDisposable
{
    internal const int HeaderSize = 8;
    internal const int ValueSizeLimit = 1024 * 1024 * 50;

    private readonly Stream _stream;
    private BlockIndex? _blockIndex;
    private bool _disposedValue;

    private EraMetadata? _metadata;
    private MemoryStream? _compressedData;

    public long StreamLength => _stream.Length;
    public EraMetadata Metadata => GetMetadata();

    public long Position => _stream.Position;

    public static E2Store ForWrite(Stream stream)
    {
        if (!stream.CanWrite)
            throw new ArgumentException("Stream must be writeable.", nameof(stream));
        return new(stream);
    }
    public static async Task<E2Store> ForRead(Stream stream, CancellationToken cancellation)
    {
        if (!stream.CanRead)
            throw new ArgumentException("Stream must be readable.", nameof(stream));
        E2Store store = new(stream);
        await store.SetMetaData(cancellation);
        return store;
    }
    internal E2Store(Stream stream)
    {
        _stream = stream;
    }

    private EraMetadata GetMetadata()
    {
        if (_metadata is null)
        {
            _metadata = ReadEraMetaData().GetAwaiter().GetResult();
        }
        return _metadata;
    }

    private async Task SetMetaData(CancellationToken token)
    {
        _metadata = await ReadEraMetaData(token);
    }

    public Task<int> WriteEntryAsSnappy(UInt16 type, Memory<byte> bytes, CancellationToken cancellation = default)
    {
        return WriteEntry(type, bytes, true, cancellation);
    }
    public Task<int> WriteEntry(UInt16 type, Memory<byte> bytes, CancellationToken cancellation = default)
    {
        return WriteEntry(type, bytes, false, cancellation);
    }

    private async Task<int> WriteEntry(UInt16 type, Memory<byte> bytes, bool asSnappy, CancellationToken cancellation = default)
    {
        using ArrayPoolList<byte> headerBuffer = new(HeaderSize);
        //See https://github.com/google/snappy/blob/main/framing_format.txt
        if (asSnappy && bytes.Length > 0)
        {
            //TODO find a way to write directly to file, and still return the number of bytes written
            EnsureCompressedStream(bytes.Length);

            using SnappyStream compressor = new(_compressedData!, CompressionMode.Compress, true);

            await compressor!.WriteAsync(bytes, cancellation);
            await compressor.FlushAsync();

            bytes = _compressedData!.ToArray();
        }

        headerBuffer.Add((byte)type);
        headerBuffer.Add((byte)(type >> 8));
        int length = bytes.Length;
        headerBuffer.Add((byte)(length));
        headerBuffer.Add((byte)(length >> 8));
        headerBuffer.Add((byte)(length >> 16));
        headerBuffer.Add((byte)(length >> 24));
        headerBuffer.Add(0);
        headerBuffer.Add(0);

        ReadOnlyMemory<byte> headerMemory = headerBuffer.AsReadOnlyMemory(0, HeaderSize);
        await _stream.WriteAsync(headerMemory, cancellation);
        if (length > 0)
        {
            await _stream.WriteAsync(bytes, cancellation);
        }

        return length + HeaderSize;
    }

    private async Task<EraMetadata> ReadEraMetaData(CancellationToken token = default)
    {
        long l = _stream!.Length;
        if (_stream.Length < 16)
            throw new EraFormatException($"Data is not in a valid Era format.");

        if (_blockIndex == null)
            _blockIndex = await BlockIndex.InitializeIndex(_stream, token);

        return new EraMetadata(_blockIndex.Start, _blockIndex.Count, l);
    }

    // Reads the header metadata at the given offset.
    private async Task<HeaderData> ReadEntryHeader(CancellationToken token = default)
    {
        var buf = ArrayPool<byte>.Shared.Rent(HeaderSize);
        try
        {
            var read = await _stream.ReadAsync(buf.AsMemory(0, HeaderSize), token);
            if (read != HeaderSize)
                throw new EraFormatException($"Entry header could not be read at position {_stream.Position}.");
            if (buf[6] != 0 || buf[7] != 0)
                throw new EraFormatException($"Reserved header bytes has invalid values at position {_stream.Position}.");
            HeaderData h = new()
            {
                Type = BitConverter.ToUInt16(buf, 0),
                Length = BitConverter.ToUInt32(buf, 2)
            };
            if (h.Length + _stream.Position > StreamLength)
                throw new EraFormatException($"Entry has an invalid length of {h.Length} at position {_stream.Position}, which is longer than stream length of {StreamLength}.");
            return h;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    public long BlockOffset(long blockNumber) => _blockIndex!.BlockOffset(blockNumber);

    public async Task<long> ReadValueAt(long off, CancellationToken token = default)
    {
        CheckStreamBounds(off);

        byte[] buf = ArrayPool<byte>.Shared.Rent(8);
        try
        {
            _stream.Position = off;
            await _stream.ReadAsync(buf, 0, 8, token);
            return BitConverter.ToInt64(buf);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buf);
        }
    }

    public async Task<Entry> ReadEntryCurrentPosition(CancellationToken token = default)
    {
        var eHeader = await ReadEntryHeader(token);

        Entry e = new(eHeader.Type, _stream.Position - HeaderSize, eHeader.Length);

        if (eHeader.Length > ValueSizeLimit)
            throw new EraException($"Entry exceeds the maximum size limit of {ValueSizeLimit}. Entry is {eHeader.Length}.");
        if (eHeader.Length == 0)
            //Empty entry?
            return e;

        return e;
    }

    public Task<Entry> ReadEntryAt(long off, CancellationToken token = default)
    {
        CheckStreamBounds(off);
        _stream.Position = off;
        return ReadEntryCurrentPosition(token);
    }

    public async Task<int> ReadEntryValueAsSnappy(IByteBuffer buffer, Entry e, CancellationToken cancellation = default)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (e.ValueOffset + e.Length > StreamLength) throw new EraFormatException($"Entry has a length ({e.Length}) and offset ({e.Offset}) that would read beyond the length of the stream.");
        //TODO is this necessary?
        buffer.EnsureWritable((int)e.Length * 4, true);

        using StreamSegment streamSegment = new(_stream, e.ValueOffset, e.Length);
        using SnappyStream decompressor = new(streamSegment, CompressionMode.Decompress, true);
        int totalRead = 0;
        int read;
        do
        {
            int before = buffer.WriterIndex;
            //We don't know the uncompressed length
            await buffer.WriteBytesAsync(decompressor, (int)e.Length * 4, cancellation);
            read = buffer.WriterIndex - before;
            totalRead += read;
        }
        while (read != 0);
        return totalRead;
    }


    private void EnsureCompressedStream(int minLength)
    {
        if (_compressedData == null)
            _compressedData = new MemoryStream(minLength);
        else
            _compressedData.SetLength(0);

    }

    public async ValueTask<int> ReadEntryValue(IByteBuffer buffer, Entry e, CancellationToken cancellation = default)
    {
        if (buffer is null) throw new ArgumentNullException(nameof(buffer));
        if (buffer.Capacity < e.Length) throw new ArgumentException($"Buffer must be at least {e.Length} long.", nameof(buffer));
        if (e.ValueOffset + e.Length > StreamLength) throw new EraFormatException($"Entry has a length ({e.Length}) and offset ({e.Offset}) that would read beyond the length of the stream.");

        _stream.Position = e.ValueOffset;
        await buffer.WriteBytesAsync(_stream, (int)e.Length, cancellation);
        return (int)e.Length;
    }

    public Task Flush(CancellationToken cancellation = default)
    {
        return _stream.FlushAsync(cancellation);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _stream?.Dispose();
                _blockIndex?.Dispose();
                _compressedData?.Dispose();
            }
            _disposedValue = true;
        }
    }
    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
    internal long Seek(long offset, SeekOrigin origin)
    {
        return _stream.Seek(offset, origin);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckStreamBounds(long offset)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset), "Cannot be a negative number.");
        if (offset > StreamLength - 8)
            throw new ArgumentOutOfRangeException(nameof(offset), "Cannot read beyond the length of the stream.");
    }

    private sealed class BlockIndex : IDisposable
    {
        private bool _disposedValue;
        private readonly long _start;
        private readonly long _count;
        private readonly long _length;
        private readonly ArrayPoolList<byte> _index;

        public long Start => _start;
        public long Count => _count;

        private BlockIndex(Span<byte> index, long start, long count, long length)
        {
            try
            {
                _index = new ArrayPoolList<byte>(index.Length);
                index.CopyTo(_index.AsSpan(0, index.Length));
            }
            catch
            {
                _index?.Dispose();
                throw;
            }
            _start = start;
            _count = count;
            _length = length;
        }

        public long BlockOffset(long blockNumber)
        {
            if (blockNumber > Start + Count || blockNumber < Start)
                throw new ArgumentOutOfRangeException(nameof(blockNumber), $"Block {blockNumber} is outside the bounds of this index.");

            int indexOffset = (int)(blockNumber - _start) * 8;
            int blockIndexOffset = 8 + indexOffset;
            long relativeOffset = BitConverter.ToInt64(_index.AsSpan(blockIndexOffset, 8));

            int indexLength = 16 + 8 * (int)_count;
            return _length - indexLength + blockIndexOffset + 8 + relativeOffset;
        }

        public static async Task<BlockIndex> InitializeIndex(Stream stream, CancellationToken cancellation)
        {
            using ArrayPoolList<byte> pooledBytes = new(8);
            Memory<byte> bytes = pooledBytes.AsMemory(0, 8);

            stream.Position = stream.Length - 8;
            await stream.ReadAsync(bytes, cancellation);
            long c = BitConverter.ToInt64(bytes.Span);

            int indexLength = 16 + 8 * (int)c;

            if (indexLength < 16 || indexLength > EraWriter.MaxEra1Size * 8 + 16)
                throw new EraFormatException("Index is in an invalid format.");

            using ArrayPoolList<byte> blockIndex = new(indexLength);
            blockIndex.AddRange(bytes.Span);

            long startIndex = stream.Length - indexLength;
            stream.Position = startIndex;
            await stream.ReadAsync(blockIndex.AsMemory(0, indexLength), cancellation);
            long s = BitConverter.ToInt64(blockIndex.AsSpan(0, 8));
            return new(blockIndex.AsSpan(0, indexLength), s, c, stream.Length);
        }
        private void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _index?.Dispose();
                }
                _disposedValue = true;
            }
        }
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
