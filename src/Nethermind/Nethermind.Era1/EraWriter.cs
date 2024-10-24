// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Buffers.Binary;
using DotNetty.Buffers;
using Nethermind.Core;
using Nethermind.Core.Collections;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
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

    private readonly E2StoreWriter _e2StoreWriter;
    private readonly AccumulatorCalculator _accumulatorCalculator;
    private readonly ISpecProvider _specProvider;
    private bool _disposedValue;
    private bool _finalized;

    public EraWriter(string path, ISpecProvider specProvider)
        : this(new E2StoreWriter(new FileStream(path, FileMode.Create)), specProvider)
    {
    }

    public EraWriter(Stream outputStream, ISpecProvider specProvider)
        : this(new E2StoreWriter(outputStream), specProvider)
    {
    }

    private EraWriter(E2StoreWriter e2StoreWriter, ISpecProvider specProvider)
    {
        _e2StoreWriter = e2StoreWriter;
        _accumulatorCalculator = new();
        _specProvider = specProvider;
        _entryIndexes = new(MaxEra1Size);
    }

    public async Task Add(Block block, TxReceipt[] receipts, CancellationToken cancellation = default)
    {
        if (_finalized)
            throw new EraException($"Finalized() has been called on this {nameof(EraWriter)}, and no more blocks can be added. ");

        if (block.Header == null)
            throw new ArgumentException("The block must have a header.", nameof(block));
        if (block.Hash == null)
            throw new ArgumentException("The block must have a hash.", nameof(block));

        if (_entryIndexes.Count >= MaxEra1Size)
            throw new ArgumentException($"Era file should not contain more than {MaxEra1Size} blocks");

        if (_firstBlock)
        {
            _startNumber = block.Number;
            _totalWritten += await WriteVersion();
            _firstBlock = false;
        }

        if (block.TotalDifficulty < block.Difficulty)
            throw new ArgumentOutOfRangeException(nameof(block.TotalDifficulty), $"Cannot be less than the block difficulty.");

        _entryIndexes.Add(_totalWritten);
        _accumulatorCalculator.Add(block.Hash, block.TotalDifficulty!.Value);

        RlpBehaviors behaviors = _specProvider.GetSpec(block.Header).IsEip658Enabled ? RlpBehaviors.Eip658Receipts : RlpBehaviors.None;

        using (NettyRlpStream headerBytes = _headerDecoder.EncodeToNewNettyStream(block.Header, behaviors))
        {
            _totalWritten += await _e2StoreWriter.WriteEntryAsSnappy(EntryTypes.CompressedHeader, headerBytes.AsMemory(), cancellation);
        }

        using (NettyRlpStream bodyBytes = _blockBodyDecoder.EncodeToNewNettyStream(block.Body, behaviors))
        {
            _totalWritten += await _e2StoreWriter.WriteEntryAsSnappy(EntryTypes.CompressedBody, bodyBytes.AsMemory(), cancellation);
        }

        using (NettyRlpStream receiptBytes = _receiptDecoder.EncodeToNewNettyStream(receipts, behaviors))
        {
            _totalWritten += await _e2StoreWriter.WriteEntryAsSnappy(EntryTypes.CompressedReceipts, receiptBytes.AsMemory(), cancellation);
        }

        _totalWritten += await _e2StoreWriter.WriteEntry(EntryTypes.TotalDifficulty, block.TotalDifficulty!.Value.ToLittleEndian(), cancellation);
    }

    public async Task<byte[]> Finalize(CancellationToken cancellation = default)
    {
        if (_firstBlock)
            throw new EraException("Finalize was called, but no blocks have been added yet.");

        byte[] root = _accumulatorCalculator.ComputeRoot().ToByteArray();
        _totalWritten += await _e2StoreWriter.WriteEntry(EntryTypes.Accumulator, root, cancellation);

        long blockIndexPosition = _totalWritten;

        //Index is 64 bits segments in the format => start | index | index | ... | count
        //16 bytes is for the start and count plus every entry
        byte[] blockIndex = new byte[16 + _entryIndexes.Count * 8];
        WriteInt64(blockIndex, 0, _startNumber);

        //era1:= Version | block-tuple ... | other-entries ... | Accumulator | BlockIndex
        //block-index := starting-number | index | index | index... | count

        //All positions are relative to the end position in the index
        for (int i = 0; i < _entryIndexes.Count; i++)
        {
            //Skip 8 bytes for the start value
            WriteInt64(blockIndex, 8 + i * 8, _entryIndexes[i] - blockIndexPosition);
        }

        WriteInt64(blockIndex, 8 + _entryIndexes.Count * 8, _entryIndexes.Count);

        await _e2StoreWriter.WriteEntry(EntryTypes.BlockIndex, blockIndex, cancellation);
        await _e2StoreWriter.Flush(cancellation);

        _entryIndexes.Clear();
        _accumulatorCalculator.Clear();
        _finalized = true;
        return root;
    }

    private static void WriteInt64(Span<byte> destination, int off, long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(off, 8), value);
    }

    private Task<int> WriteVersion()
    {
        return _e2StoreWriter.WriteEntry(EntryTypes.Version, Array.Empty<byte>());
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
                _e2StoreWriter?.Dispose();
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
