// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using DotNetty.Buffers;
using DotNetty.Common.Utilities;
using Nethermind.Core;
using Nethermind.Core.Buffers;
using Nethermind.Core.Extensions;
using Nethermind.Int256;
using Nethermind.Serialization.Rlp;
using Snappier;

namespace Nethermind.Era1;
internal class EraReader : IAsyncEnumerable<(Block, TxReceipt[], UInt256)>, IDisposable
{
    private bool _disposedValue;
    private long _currentBlockNumber;
    private ReceiptMessageDecoder _receiptDecoder = new();
    private E2Store _store;

    public long CurrentBlockNumber => _currentBlockNumber;

    private EraReader(E2Store e2)
    {
        _store = e2;
        _currentBlockNumber = e2.Metadata.Start;
    }
    public async IAsyncEnumerator<(Block, TxReceipt[], UInt256)> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        (Block? b, TxReceipt[]? r, UInt256? td) = await Next(cancellationToken);
        while (b != null && r != null && td != null)
        {
            yield return (b, r, td.Value);
            (b, r, td) = await Next(cancellationToken);
        }
    }
    internal static Task<EraReader> Create(string file, CancellationToken token = default)
    {
        if (string.IsNullOrEmpty(file)) throw new ArgumentException("Cannot be null or empty.", nameof(file));
        return Create(File.OpenRead(file), token);
    }
    internal static async Task<EraReader> Create(Stream stream, CancellationToken token = default)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (!stream.CanRead) throw new ArgumentException("Provided stream is not readable.");

        E2Store e2 = new E2Store(stream);
        await e2.SetMetaData(token);
        EraReader e = new EraReader(e2);

        return e;
    }
    // Format: <network>-<epoch>-<hexroot>.era1
    public static IEnumerable<string> GetAllEraFiles(string directoryPath, string network)
    {
        var entries = Directory.GetFiles(directoryPath, "*.era1");
        if (!entries.Any())
            yield break;

        uint next = 0;

        foreach (string file in entries)
        {
            string[] parts = Path.GetFileName(file).Split(new char[] { '-' });
            if (parts.Length != 3 || parts[0] != network)
            {
                continue;
            }
            uint epoch;
            if (!uint.TryParse(parts[1], out epoch))
                throw new EraException($"Invalid era1 filename: {Path.GetFileName(file)}");
            else if (epoch != next)
                throw new EraException($"Epoch {epoch} is missing.");

            next++;
            yield return (file);
        }
    }
    public async Task<byte[]> ReadAccumulator(CancellationToken cancellation = default)
    {
        _store.Seek(-32 - 8 * 4 - _store.Metadata.Count * 8, SeekOrigin.End);
        Entry accumulator = await _store.ReadEntryCurrentPosition(cancellation);
        CheckType(accumulator, EntryTypes.Accumulator);
        IByteBuffer buffer = PooledByteBufferAllocator.Default.Buffer(32);
        try
        {
            int read = await _store.ReadEntryValue(buffer, accumulator, cancellation);
            byte[] bytes = new byte[read];
            for (int i = 0; i < read; i++)
            {
                bytes[i] = buffer.GetByte(buffer.ArrayOffset + i);
            }
            return bytes;
        }
        finally
        {
            buffer.Release();
        }
    }
    public Task<(Block, TxReceipt[], UInt256)> GetBlockByNumber(long number, CancellationToken cancellation = default)
    {
        if (number < _store.Metadata.Start)
            throw new ArgumentOutOfRangeException(nameof(number), $"Cannot be less than the first block number {_store.Metadata.Start}.");
        if (number > _store.Metadata.End)
            throw new ArgumentOutOfRangeException(nameof(number), $"Cannot be more than the last block number {_store.Metadata.End}.");
        return ReadBlockAndReceipts(number, cancellation);
    }
    private async Task<(Block?, TxReceipt[]?, UInt256?)> Next(CancellationToken cancellationToken)
    {
        if (_store.Metadata.Start + _store.Metadata.Count <= _currentBlockNumber)
        {
            //TODO test enumerate more than once
            Reset();
            return (null, null, null);
        }
        (Block? b, TxReceipt[]? r, UInt256? td) = await ReadBlockAndReceipts(_currentBlockNumber, cancellationToken);
        _currentBlockNumber++;
        return (b, r, td);
    }

    private async Task<(Block, TxReceipt[], UInt256)> ReadBlockAndReceipts(long blockNumber, CancellationToken cancellationToken)
    {
        long blockOffset = await SeekToBlock(blockNumber, cancellationToken);
        IByteBuffer buffer = PooledByteBufferAllocator.Default.Buffer(1024 * 1024);

        try
        {
            Entry e = await _store.ReadEntryAt(blockOffset, cancellationToken);
            CheckType(e, EntryTypes.CompressedHeader);
            BlockHeader header = await DecompressAndDecode<BlockHeader>(buffer, e);
            
            e = await _store.ReadEntryCurrentPosition(cancellationToken);
            CheckType(e, EntryTypes.CompressedBody);

            BlockBody body = await DecompressAndDecode<BlockBody>(buffer, e);

            e = await _store.ReadEntryCurrentPosition(cancellationToken);
            CheckType(e, EntryTypes.CompressedReceipts);

            int read = await Decompress(buffer, e);
            TxReceipt[] receipts = DecodeReceipts(buffer, read);

            e = await _store.ReadEntryCurrentPosition(cancellationToken);
            CheckType(e, EntryTypes.TotalDifficulty);
            read = await _store.ReadEntryValue(buffer, e, cancellationToken);

            UInt256 currentTotalDiffulty = new UInt256(buffer.ReadAllBytesAsSpan());

            Block block = new Block(header, body);

            return (block, receipts, currentTotalDiffulty);
        }
        finally
        {
            buffer.Release();
        };
    }

    private TxReceipt[] DecodeReceipts(IByteBuffer buf, int count)
    {
        return _receiptDecoder.DecodeArray(new NettyRlpStream(buf));
    }

    private async Task<long> SeekToBlock(long blockNumber, CancellationToken token = default)
    {
        //Last 8 bytes is the count, so we skip them
        long startOfIndex = _store.Metadata.Length - 8 - _store.Metadata.Count * 8;
        long indexOffset = (blockNumber - _store.Metadata.Start) * 8;
        long blockIndexOffset = startOfIndex + indexOffset;

        long blockOffset = await _store.ReadValueAt(blockIndexOffset, token);
        return _store.Seek(blockOffset, SeekOrigin.Current);
    }

    private void Reset()
    {
        _currentBlockNumber = 0;
    }

    private static void CheckType(Entry e, ushort expected)
    {
        if (e.Type != expected) throw new EraException($"Expected an entry of type {expected}, but got {e.Type}.");
    }
    private Task<int> Decompress(IByteBuffer buffer, Entry e, CancellationToken cancellation = default) => _store.ReadEntryValueAsSnappy(buffer, e, cancellation);

    private async Task<T> DecompressAndDecode<T>(IByteBuffer buffer, Entry e, CancellationToken cancellation = default) where T : class
    {
        //TODO handle read more than buffer length
        buffer.EnsureWritable((int)e.Length * 2, true);
        await _store.ReadEntryValueAsSnappy(buffer, e, cancellation);
        return Rlp.Decode<T>(new NettyRlpStream(buffer));
    }

    private static bool IsValidFilename(string file)
    {
        if (!Path.GetExtension(file).Equals(".era1", StringComparison.OrdinalIgnoreCase))
            return false;
        string[] parts = file.Split(new char[] { '-' });
        uint epoch;
        if (parts.Length != 3 || !uint.TryParse(parts[1], out epoch))
            return false;
        return true;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _store?.Dispose();
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
