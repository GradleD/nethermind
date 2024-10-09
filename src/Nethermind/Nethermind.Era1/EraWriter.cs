// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using DotNetty.Buffers;
using Nethermind.Core;
using Nethermind.Core.Collections;
using Nethermind.Core.Crypto;
using Nethermind.Core.Specs;
using Nethermind.Int256;
using Nethermind.Serialization.Rlp;

namespace Nethermind.Era1;
public class EraWriter : IDisposable
{
    public const int MaxEra1Size = 8192;

    private long _startNumber;
    private bool _firstBlock = true;
    private long _totalWritten;
    private readonly ArrayPoolList<long> _entryIndexes;

    private readonly HeaderDecoder _headerDecoder = new();
    private readonly BlockBodyDecoder _blockBodyDecoder = new();
    private readonly ReceiptMessageDecoder _receiptDecoder = new();
    private readonly IByteBufferAllocator _byteBufferAllocator;

    private readonly E2StoreStream _e2StoreStream;
    private readonly AccumulatorCalculator _accumulatorCalculator;
    private readonly ISpecProvider _specProvider;
    private bool _disposedValue;
    private bool _finalized;

    public static EraWriter Create(string path, ISpecProvider specProvider, IByteBufferAllocator? bufferAllocator = null)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException($"'{nameof(path)}' cannot be null or whitespace.", nameof(path));
        return Create(new FileStream(path, FileMode.Create), specProvider, bufferAllocator);
    }
    public static EraWriter Create(Stream stream, ISpecProvider specProvider, IByteBufferAllocator? bufferAllocator = null)
    {
        if (specProvider is null) throw new ArgumentNullException(nameof(specProvider));

        EraWriter b = new(E2StoreStream.ForWrite(stream), specProvider, bufferAllocator);
        return b;
    }

    private EraWriter(E2StoreStream e2StoreStream, ISpecProvider specProvider, IByteBufferAllocator? bufferAllocator)
    {
        _e2StoreStream = e2StoreStream;
        _accumulatorCalculator = new();
        _specProvider = specProvider;
        _byteBufferAllocator = bufferAllocator ?? PooledByteBufferAllocator.Default;
        _entryIndexes = new(MaxEra1Size);
    }

    public Task<bool> Add(Block block, TxReceipt[] receipts, in CancellationToken cancellation = default)
    {
        return Add(block, receipts, block.TotalDifficulty ?? block.Difficulty, cancellation);
    }
    public Task<bool> Add(Block block, TxReceipt[] receipts, in UInt256 totalDifficulty, in CancellationToken cancellation = default)
    {
        if (block.Header == null)
            throw new ArgumentException("The block must have a header.", nameof(block));
        if (block.Hash == null)
            throw new ArgumentException("The block must have a hash.", nameof(block));

        int headerLength = _headerDecoder.GetLength(block.Header, RlpBehaviors.None);
        int bodyLength = _blockBodyDecoder.GetLength(block.Body, RlpBehaviors.None);
        RlpBehaviors behaviors = _specProvider.GetSpec(block.Header).IsEip658Enabled ? RlpBehaviors.Eip658Receipts : RlpBehaviors.None;
        int receiptsLength = _receiptDecoder.GetLength(receipts, behaviors);

        IByteBuffer byteBuffer = _byteBufferAllocator.Buffer(headerLength + bodyLength + receiptsLength);
        try
        {
            byteBuffer.EnsureWritable(headerLength + bodyLength + receiptsLength);

            RlpStream rlpStream = new NettyRlpStream(byteBuffer);
            rlpStream.Encode(block.Header);
            Memory<byte> headerBytes = byteBuffer.ReadAllBytesAsMemory();

            rlpStream.Encode(block.Body);
            Memory<byte> bodyBytes = byteBuffer.ReadAllBytesAsMemory();

            //Geth implementation has a byte array representing both TxPostState and StatusCode, and serializes whatever is set
            rlpStream.Encode(receipts, behaviors);
            Memory<byte> receiptBytes = byteBuffer.ReadAllBytesAsMemory();

            return Add(block.Hash, headerBytes, bodyBytes, receiptBytes, block.Number, block.Difficulty, totalDifficulty, cancellation);
        }
        finally
        {
            byteBuffer.Release();
        }
    }
    /// <summary>
    /// Write RLP encoded data to the underlying stream.
    /// </summary>
    /// <param name="blockHash"></param>
    /// <param name="blockHeader"></param>
    /// <param name="blockBody"></param>
    /// <param name="receiptsArray"></param>
    /// <param name="blockNumber"></param>
    /// <param name="blockDifficulty"></param>
    /// <param name="totalDifficulty"></param>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="EraException"></exception>
    public async Task<bool> Add(
        Hash256 blockHash,
        Memory<byte> blockHeader,
        Memory<byte> blockBody,
        Memory<byte> receiptsArray,
        long blockNumber,
        UInt256 blockDifficulty,
        UInt256 totalDifficulty,
        CancellationToken cancellation = default)
    {
        if (blockHash is null) throw new ArgumentNullException(nameof(blockHash));
        if (blockHeader.Length == 0) throw new ArgumentException("Rlp encoded data cannot be empty.", nameof(blockHeader));
        if (blockBody.Length == 0) throw new ArgumentException("Rlp encoded data cannot be empty.", nameof(blockBody));
        if (receiptsArray.Length == 0) throw new ArgumentException("Rlp encoded data cannot be empty.", nameof(receiptsArray));
        if (totalDifficulty < blockDifficulty)
            throw new ArgumentOutOfRangeException(nameof(totalDifficulty), $"Cannot be less than the block difficulty.");
        if (_finalized)
            throw new EraException($"Finalized() has been called on this {nameof(EraWriter)}, and no more blocks can be added. ");

        if (_firstBlock)
        {
            _startNumber = blockNumber;
            _totalWritten += await WriteVersion();
            _firstBlock = false;
        }

        if (_entryIndexes.Count >= MaxEra1Size)
            return false;

        _entryIndexes.Add(_totalWritten);
        _accumulatorCalculator.Add(blockHash, totalDifficulty);
        _totalWritten += await _e2StoreStream.WriteEntryAsSnappy(EntryTypes.CompressedHeader, blockHeader, cancellation);

        _totalWritten += await _e2StoreStream.WriteEntryAsSnappy(EntryTypes.CompressedBody, blockBody, cancellation);

        _totalWritten += await _e2StoreStream.WriteEntryAsSnappy(EntryTypes.CompressedReceipts, receiptsArray, cancellation);

        _totalWritten += await _e2StoreStream.WriteEntry(EntryTypes.TotalDifficulty, totalDifficulty.ToLittleEndian(), cancellation);

        return true;
    }

    public async Task<byte[]> Finalize(CancellationToken cancellation = default)
    {
        if (_firstBlock)
            throw new EraException("Finalize was called, but no blocks have been added yet.");

        byte[] root = _accumulatorCalculator.ComputeRoot().ToArray();
        _totalWritten += await _e2StoreStream.WriteEntry(EntryTypes.Accumulator, root, cancellation);

        long blockIndexPosition = _totalWritten;

        //Index is 64 bits segments in the format => start | index | index | ... | count
        //16 bytes is for the start and count plus every entry
        byte[] blockIndex = new byte[16 + _entryIndexes.Count * 8];
        WriteUInt64(blockIndex, 0, _startNumber);

        //era1:= Version | block-tuple ... | other-entries ... | Accumulator | BlockIndex
        //block-index := starting-number | index | index | index... | count

        //All positions are relative to the end position in the index
        for (int i = 0; i < _entryIndexes.Count; i++)
        {
            //Skip 8 bytes for the start value
            WriteUInt64(blockIndex, 8 + i * 8, _entryIndexes[i] - blockIndexPosition);
        }

        WriteUInt64(blockIndex, 8 + _entryIndexes.Count * 8, _entryIndexes.Count);

        await _e2StoreStream.WriteEntry(EntryTypes.BlockIndex, blockIndex, cancellation);
        await _e2StoreStream.Flush(cancellation);

        _entryIndexes.Clear();
        _accumulatorCalculator.Clear();
        _finalized = true;
        return root;
    }
    private static bool WriteUInt64(Span<byte> destination, int off, long value)
    {
        return BitConverter.TryWriteBytes(destination.Slice(off, 8), value) == false ? throw new EraException("Failed to write UInt64 to output.") : true;
    }

    private Task<int> WriteVersion()
    {
        return _e2StoreStream.WriteEntry(EntryTypes.Version, Array.Empty<byte>());
    }

    private class EntryIndexInfo
    {
        public long Index { get; }
        public EntryIndexInfo(long index)
        {
            Index = index;
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _e2StoreStream?.Dispose();
                _accumulatorCalculator?.Dispose();
                _entryIndexes?.Dispose();
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

    public static string Filename(string network, long epoch, Hash256 root)
    {
        if (string.IsNullOrEmpty(network)) throw new ArgumentException($"'{nameof(network)}' cannot be null or empty.", nameof(network));
        if (root is null) throw new ArgumentNullException(nameof(root));
        if (epoch < 0) throw new ArgumentOutOfRangeException(nameof(epoch), "Cannot be a negative number.");

        return $"{network}-{epoch.ToString("D5")}-{root.ToString(true)[2..10]}.era1";
    }
}
