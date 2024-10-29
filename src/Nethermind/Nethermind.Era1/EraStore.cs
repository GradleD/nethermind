// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.IO.Abstractions;
using System.Runtime.CompilerServices;
using Nethermind.Consensus.Validators;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Core.Specs;
using Nethermind.Era1.Exceptions;
using NonBlocking;

namespace Nethermind.Era1;
public class EraStore : IEraStore
{
    private readonly char[] _eraSeparator = ['-'];

    private readonly IFileSystem _fileSystem;
    private readonly ISpecProvider _specProvider;
    private readonly IBlockValidator _blockValidator;
    private readonly ISet<ValueHash256>? _trustedAccumulators;

    private readonly Dictionary<long, string> _epochs;
    private readonly ConcurrentDictionary<long, bool> _verifiedEpochs = new();

    /// <summary>
    /// Simple mechanism to limit opened file. Each epoch is sharded to _maxOpenFile. Whenever a reader is to be used
    /// it is removed from _openedReader unless there are no opened reader yet. When the reader is returned, the
    /// reader try to put itself back in _openedReader. If failed it try to remove and dispose current reader,
    /// otherwise, it try to dispose itself.
    /// Note: EraReader itself is concurrent safe, so there is probably a better way to do this.
    /// </summary>
    private readonly int _maxOpenFile;
    private readonly ConcurrentDictionary<int, (long, EraReader)> _openedReader = new();

    private bool _disposed = false;
    private readonly int _maxEraFile;

    private int BiggestEpoch { get; set; }
    private int SmallestEpoch { get; set; } = int.MaxValue;

    private long? _firstBlock = null;
    public long FirstBlock
    {
        get
        {
            if (_firstBlock == null)
            {
                using EraRenter _ = RentReader(SmallestEpoch, out EraReader smallestEraReader);
                _firstBlock = smallestEraReader.StartBlock;
            }
            return _firstBlock.Value;
        }
    }

    private long? _lastBlock = null;

    public long LastBlock
    {
        get
        {
            if (_lastBlock == null)
            {
                using EraRenter _ = RentReader(BiggestEpoch, out EraReader biggestEraReader);
                _lastBlock = biggestEraReader.LastBlock;
            }
            return _lastBlock.Value;
        }
    }

    public EraStore(
        ISpecProvider specProvider,
        IBlockValidator blockValidator,
        IFileSystem fileSystem,
        string networkName,
        int maxEraSize,
        ISet<ValueHash256>? trustedAcccumulators,
        string directory
    ) {
        _specProvider = specProvider;
        _blockValidator = blockValidator;
        _fileSystem = fileSystem;
        _trustedAccumulators = trustedAcccumulators;
        _maxEraFile = maxEraSize;
        _maxOpenFile = Environment.ProcessorCount * 2;

        var eraFiles = EraPathUtils.GetAllEraFiles(directory, networkName, fileSystem).ToArray();
        _epochs = new();
        foreach (var file in eraFiles)
        {
            string[] parts = Path.GetFileName(file).Split(_eraSeparator);
            int epoch;
            if (parts.Length != 3 || !int.TryParse(parts[1], out epoch) || epoch < 0)
                throw new ArgumentException($"Malformed Era1 file '{file}'.", nameof(eraFiles));
            _epochs[epoch] = file;
            if (epoch > BiggestEpoch)
                BiggestEpoch = epoch;
            if (epoch < SmallestEpoch)
                SmallestEpoch = epoch;
        }
    }

    private long GetEpochNumber(long blockNumber)
    {
        // This seems to be the geth way of encoding blocks.
        long epochOffset = (blockNumber - FirstBlock) / _maxEraFile;
        return SmallestEpoch + epochOffset;
    }

    public bool HasEpoch(long epoch) => _epochs.ContainsKey(epoch);

    public EraReader GetReader(long epoch)
    {
        GuardMissingEpoch(epoch);
        return new EraReader(new E2StoreReader(_epochs[epoch]));
    }

    public async Task<Block?> FindBlock(long blockNumber, bool ensureValidated = true, CancellationToken cancellation = default)
    {
        ThrowIfNegative(blockNumber);

        long partOfEpoch = GetEpochNumber(blockNumber);
        if (!_epochs.ContainsKey(partOfEpoch))
            return null;

        using EraRenter _r = RentReader(partOfEpoch, out EraReader reader);
        if (ensureValidated) await EnsureEpochVerified(partOfEpoch, reader, cancellation);
        (Block b, _) = await reader.GetBlockByNumber(blockNumber, cancellation);
        return b;
    }

    private async ValueTask EnsureEpochVerified(long epoch, EraReader reader, CancellationToken cancellation)
    {
        if (!(_verifiedEpochs.TryGetValue(epoch, out bool verified) && verified))
        {
            var eraAccumulator = await reader.VerifyContent(_specProvider, _blockValidator, cancellation);
            if (_trustedAccumulators != null && !_trustedAccumulators.Contains(eraAccumulator))
            {
                throw new EraVerificationException( $"Unable to verify epoch {epoch}. Accumulator {eraAccumulator} not trusted");
            }

            _verifiedEpochs.TryAdd(epoch, true);
        }
    }

    public long NextEraStart(long blockNumber)
    {
        long epoch = GetEpochNumber(blockNumber);
        using EraRenter _ = RentReader(epoch, out EraReader reader);
        return reader.LastBlock + 1;
    }

    public async Task<(Block?, TxReceipt[]?)> FindBlockAndReceipts(long number, bool ensureValidated = true, CancellationToken cancellation = default)
    {
        ThrowIfNegative(number);

        long partOfEpoch = GetEpochNumber(number);
        if (!_epochs.ContainsKey(partOfEpoch))
            return (null, null);

        using EraRenter _ = RentReader(partOfEpoch, out EraReader reader);
        if (ensureValidated) await EnsureEpochVerified(partOfEpoch, reader, cancellation);
        (Block b, TxReceipt[] r) = await reader.GetBlockByNumber(number, cancellation);
        return (b, r);
    }

    private EraRenter RentReader(long epoch, out EraReader reader)
    {
        GuardMissingEpoch(epoch);

        int shardIdx = (int)(epoch % _maxOpenFile);
        if (_openedReader.TryRemove(shardIdx, out (long, EraReader) openedReader))
        {
            if (openedReader.Item1 == epoch)
            {
                reader = openedReader.Item2;
                return new EraRenter(this, reader, epoch);
            }

            if (!_openedReader.TryAdd(shardIdx, openedReader))
            {
                openedReader.Item2.Dispose();
            }
        }

        reader = GetReader(epoch);
        return new EraRenter(this, reader, epoch);
    }

    private void ReturnReader(long epoch, EraReader reader)
    {
        int shardIdx = (int)(epoch % _maxOpenFile);

        if (_openedReader.TryAdd(shardIdx, (epoch, reader))) return;

        // Something opened another reader of the same shard.

        // We try to remove and dispose the current one first.
        if (_openedReader.TryRemove(shardIdx, out (long, EraReader) existingReader))
        {
            existingReader.Item2.Dispose();
        }

        if (_openedReader.TryAdd(shardIdx, (epoch, reader))) return;

        // Still failed, so we just dispose ourself
        reader.Dispose();
    }

    public async Task CreateAccumulatorFile(string accumulatorPath, CancellationToken cancellationToken)
    {
        _fileSystem.File.Delete(accumulatorPath);
        using StreamWriter stream = new StreamWriter(_fileSystem.File.Create(accumulatorPath), System.Text.Encoding.UTF8);
        bool first = true;

        foreach (var kv in _epochs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (EraReader reader = GetReader(kv.Key))
            {
                string root = (reader.ReadAccumulator()).BytesAsSpan.ToHexString(true);
                if (!first)
                    root = Environment.NewLine + root;
                else
                    first = false;
                await stream.WriteAsync(root);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ThrowIfNegative(long number)
    {
        if (number < 0)
            throw new ArgumentOutOfRangeException(nameof(number), number, "Cannot be negative.");
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void GuardMissingEpoch(long epoch)
    {
        if (!HasEpoch(epoch))
            throw new ArgumentOutOfRangeException($"Epoch not available.", epoch, nameof(epoch));
    }

    private readonly struct EraRenter(EraStore store, EraReader reader, long epoch) : IDisposable
    {
        public void Dispose()
        {
            store.ReturnReader(epoch, reader);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (KeyValuePair<int, (long, EraReader)> kv in _openedReader)
        {
            kv.Value.Item2.Dispose();
        }
    }
}
