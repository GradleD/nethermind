// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using DotNetty.Buffers;
using Nethermind.Core.Collections;
using Nethermind.Serialization.Rlp;
using Snappier;
namespace Nethermind.Era1;

internal class E2StoreStream : IDisposable
{
    internal const int HeaderSize = 8;

    private readonly Stream _stream;
    private bool _disposedValue;
    private IByteBufferAllocator _bufferAllocator;
    private MemoryStream? _compressedData;

    public long StreamLength => _stream.Length;

    public long Position => _stream.Position;

    public static E2StoreStream ForWrite(Stream stream)
    {
        if (!stream.CanWrite)
            throw new ArgumentException("Stream must be writeable.", nameof(stream));
        return new(stream);
    }
    public static Task<E2StoreStream> ForRead(Stream stream, IByteBufferAllocator? bufferAllocator, CancellationToken cancellation)
    {
        if (!stream.CanRead)
            throw new ArgumentException("Stream must be readable.", nameof(stream));
        E2StoreStream storeStream = new(stream, bufferAllocator);
        return Task.FromResult(storeStream);
    }
    internal E2StoreStream(Stream stream, IByteBufferAllocator? bufferAllocator = null)
    {
        _stream = stream;
        _bufferAllocator = bufferAllocator ?? PooledByteBufferAllocator.Default;
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

        ReadOnlyMemory<byte> headerMemory = headerBuffer.AsReadOnlyMemory()[..HeaderSize];
        await _stream.WriteAsync(headerMemory, cancellation);
        if (length > 0)
        {
            await _stream.WriteAsync(bytes, cancellation);
        }

        return length + HeaderSize;
    }

    private void EnsureCompressedStream(int minLength)
    {
        if (_compressedData == null)
            _compressedData = new MemoryStream(minLength);
        else
            _compressedData.SetLength(0);
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
}