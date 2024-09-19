// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Nethermind.Core;
using Nethermind.Core.Collections;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Int256;
using Nethermind.Logging;
using Nethermind.State.Tracing;
using Nethermind.Trie.Pruning;

namespace Nethermind.State
{
    using Nethermind.Core.Cpu;
    /// <summary>
    /// Manages persistent storage allowing for snapshotting and restoring
    /// Persists data to ITrieStore
    /// </summary>
    internal sealed class PersistentStorageProvider : PartialStorageProviderBase
    {
        private readonly ITrieStore _trieStore;
        private readonly StateProvider _stateProvider;
        private readonly ILogManager? _logManager;
        internal readonly IStorageTreeFactory _storageTreeFactory;
        private readonly Dictionary<AddressAsKey, StorageTree> _storages = new();
        private readonly HashSet<AddressAsKey> _toUpdateRoots = new();

        /// <summary>
        /// EIP-1283
        /// </summary>
        private readonly Dictionary<StorageCell, byte[]> _originalValues = new();

        private readonly HashSet<StorageCell> _committedThisRound = new();
        private readonly Dictionary<AddressAsKey, SelfDestructDictionary<UInt256, byte[]>> _blockReadCache = new(4_096);
        private readonly Dictionary<AddressAsKey, SelfDestructDictionary<UInt256, byte[]>> _blockWriteCache = new(4_096);
        private readonly ConcurrentDictionary<StorageCell, byte[]>? _preBlockCache;
        private readonly Func<StorageCell, byte[]> _loadFromTree;

        /// <summary>
        /// Manages persistent storage allowing for snapshotting and restoring
        /// Persists data to ITrieStore
        /// </summary>
        public PersistentStorageProvider(ITrieStore trieStore,
            StateProvider stateProvider,
            ILogManager logManager,
            IStorageTreeFactory? storageTreeFactory,
            ConcurrentDictionary<StorageCell, byte[]>? preBlockCache,
            bool populatePreBlockCache) : base(logManager)
        {
            _trieStore = trieStore ?? throw new ArgumentNullException(nameof(trieStore));
            _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
            _logManager = logManager ?? throw new ArgumentNullException(nameof(logManager));
            _storageTreeFactory = storageTreeFactory ?? new StorageTreeFactory();
            _preBlockCache = preBlockCache;
            _populatePreBlockCache = populatePreBlockCache;
            _loadFromTree = LoadFromTreeStorage;
        }

        public Hash256 StateRoot { get; set; } = null!;
        private readonly bool _populatePreBlockCache;

        /// <summary>
        /// Reset the storage state
        /// </summary>
        public override void Reset()
        {
            base.Reset();
            _blockReadCache.Clear();
            _blockWriteCache.Clear();
            _storages.Clear();
            _originalValues.Clear();
            _committedThisRound.Clear();
            _toUpdateRoots.Clear();
        }

        /// <summary>
        /// Get the current value at the specified location
        /// </summary>
        /// <param name="storageCell">Storage location</param>
        /// <returns>Value at location</returns>
        protected override ReadOnlySpan<byte> GetCurrentValue(in StorageCell storageCell) =>
            TryGetCachedValue(storageCell, out byte[]? bytes) ? bytes! : LoadFromTree(storageCell);

        /// <summary>
        /// Return the original persistent storage value from the storage cell
        /// </summary>
        /// <param name="storageCell"></param>
        /// <returns></returns>
        public byte[] GetOriginal(in StorageCell storageCell)
        {
            if (!_originalValues.TryGetValue(storageCell, out var value))
            {
                throw new InvalidOperationException("Get original should only be called after get within the same caching round");
            }

            if (_transactionChangesSnapshots.TryPeek(out int snapshot))
            {
                if (_intraBlockCache.TryGetValue(storageCell, out StackList<int> stack))
                {
                    if (stack.TryGetSearchedItem(snapshot, out int lastChangeIndexBeforeOriginalSnapshot))
                    {
                        return _changes[lastChangeIndexBeforeOriginalSnapshot].Value;
                    }
                }
            }

            return value;
        }

        private HashSet<AddressAsKey>? _tempToUpdateRoots;
        /// <summary>
        /// Called by Commit
        /// Used for persistent storage specific logic
        /// </summary>
        /// <param name="tracer">Storage tracer</param>
        protected override void CommitCore(IStorageTracer tracer)
        {
            if (_logger.IsTrace) _logger.Trace("Committing storage changes");

            int currentPosition = _changes.Count - 1;
            if (currentPosition < 0)
            {
                return;
            }
            if (_changes[currentPosition].IsNull)
            {
                throw new InvalidOperationException($"Change at current position {currentPosition} was null when committing {nameof(PartialStorageProviderBase)}");
            }

            HashSet<AddressAsKey> toUpdateRoots = (_tempToUpdateRoots ??= new());

            bool isTracing = tracer.IsTracingStorage;
            Dictionary<StorageCell, ChangeTrace>? trace = null;
            if (isTracing)
            {
                trace = new Dictionary<StorageCell, ChangeTrace>();
            }

            for (int i = 0; i <= currentPosition; i++)
            {
                Change change = _changes[currentPosition - i];
                if (!isTracing && change!.ChangeType == ChangeType.JustCache)
                {
                    continue;
                }

                if (_committedThisRound.Contains(change!.StorageCell))
                {
                    if (isTracing && change.ChangeType == ChangeType.JustCache)
                    {
                        trace![change.StorageCell] = new ChangeTrace(change.Value, trace[change.StorageCell].After);
                    }

                    continue;
                }

                if (isTracing && change.ChangeType == ChangeType.JustCache)
                {
                    tracer!.ReportStorageRead(change.StorageCell);
                }

                _committedThisRound.Add(change.StorageCell);

                if (change.ChangeType == ChangeType.Destroy)
                {
                    continue;
                }

                int forAssertion = _intraBlockCache[change.StorageCell].Pop();
                if (forAssertion != currentPosition - i)
                {
                    throw new InvalidOperationException($"Expected checked value {forAssertion} to be equal to {currentPosition} - {i}");
                }

                switch (change.ChangeType)
                {
                    case ChangeType.Destroy:
                        break;
                    case ChangeType.JustCache:
                        break;
                    case ChangeType.Update:
                        if (_logger.IsTrace)
                        {
                            _logger.Trace($"  Update {change.StorageCell.Address}_{change.StorageCell.Index} V = {change.Value.ToHexString(true)}");
                        }

                        SaveToTree(toUpdateRoots, change);

                        if (isTracing)
                        {
                            trace![change.StorageCell] = new ChangeTrace(change.Value);
                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            foreach (AddressAsKey address in toUpdateRoots)
            {
                // since the accounts could be empty accounts that are removing (EIP-158)
                if (_stateProvider.AccountExists(address))
                {
                    _toUpdateRoots.Add(address);
                }
                else
                {
                    _toUpdateRoots.Remove(address);
                    _storages.Remove(address);
                }
            }
            toUpdateRoots.Clear();

            base.CommitCore(tracer);
            _originalValues.Clear();
            _committedThisRound.Clear();

            if (isTracing)
            {
                ReportChanges(tracer!, trace!);
            }
        }

        protected override void CommitStorageRoots()
        {
            if (_toUpdateRoots.Count == 0)
            {
                return;
            }

            var totalCount = 0;
            var i = 0;
            var keys = _blockWriteCache.Keys.ToArray();
            Parallel.For(0, keys.Length, RuntimeInformation.ParallelOptionsLogicalCores, _ =>
            {
                var address = keys[Interlocked.Increment(ref i) - 1];
                if (!_storages.TryGetValue(address, out StorageTree tree))
                {
                    tree = GetOrCreateStorageLocked(address);
                }

                var dict = _blockWriteCache[address];
                var count = 0;
                foreach (var kvp in dict)
                {
                    tree.Set(kvp.Key, kvp.Value);
                    count++;
                }

                Interlocked.Add(ref totalCount, count);
            });

            _logger.Info($"Storage Write: {totalCount} values");
            Db.Metrics.StorageTreeWrites += totalCount;

            // Is overhead of parallel foreach worth it?
            if (_toUpdateRoots.Count <= 4)
            {
                UpdateRootHashesSingleThread();
            }
            else
            {
                UpdateRootHashesMultiThread();
            }

            void UpdateRootHashesSingleThread()
            {
                foreach (KeyValuePair<AddressAsKey, StorageTree> kvp in _storages)
                {
                    if (!_toUpdateRoots.Contains(kvp.Key))
                    {
                        // Wasn't updated don't recalculate
                        continue;
                    }

                    StorageTree storageTree = kvp.Value;
                    storageTree.UpdateRootHash(canBeParallel: true);
                    _stateProvider.UpdateStorageRoot(address: kvp.Key, storageTree.RootHash);
                }
            }

            void UpdateRootHashesMultiThread()
            {
                var storages = _toUpdateRoots.ToArray();
                var i = 0;
                // We can recalculate the roots in parallel as they are all independent tries
                Parallel.For(0, storages.Length, RuntimeInformation.ParallelOptionsLogicalCores, _ =>
                {
                    AddressAsKey address = storages[Interlocked.Increment(ref i) - 1];

                    StorageTree storageTree = _storages[address];
                    storageTree.UpdateRootHash(canBeParallel: false);
                });

                // Update the storage roots in the main thread non in parallel
                foreach (KeyValuePair<AddressAsKey, StorageTree> kvp in _storages)
                {
                    if (!_toUpdateRoots.Contains(kvp.Key))
                    {
                        continue;
                    }

                    // Update the storage root for the Account
                    _stateProvider.UpdateStorageRoot(address: kvp.Key, kvp.Value.RootHash);
                }

            }
        }

        private void SaveToTree(HashSet<AddressAsKey> toUpdateRoots, Change change)
        {
            if (_originalValues.TryGetValue(change.StorageCell, out byte[] initialValue) &&
                initialValue.AsSpan().SequenceEqual(change.Value))
            {
                // no need to update the tree if the value is the same
                return;
            }

            toUpdateRoots.Add(change.StorageCell.Address);

            ref SelfDestructDictionary<UInt256, byte[]>? dict = ref CollectionsMarshal.GetValueRefOrAddDefault(_blockWriteCache, change.StorageCell.Address, out bool exists);
            if (!exists)
            {
                dict = new SelfDestructDictionary<UInt256, byte[]>(StorageTree.EmptyBytes);
            }

            dict[change.StorageCell.Index] = change.Value;
        }

        /// <summary>
        /// Commit persistent storage trees
        /// </summary>
        /// <param name="blockNumber">Current block number</param>
        public void CommitTrees(long blockNumber)
        {
            foreach (KeyValuePair<AddressAsKey, StorageTree> storage in _storages)
            {
                if (!_toUpdateRoots.Contains(storage.Key))
                {
                    continue;
                }
                storage.Value.Commit(blockNumber);
            }

            _toUpdateRoots.Clear();
            // only needed here as there is no control over cached storage size otherwise
            _storages.Clear();
        }

        private StorageTree GetOrCreateStorageLocked(Address address)
        {
            ref StorageTree? value = ref Unsafe.NullRef<StorageTree>();
            lock (_storages)
            {
                value = ref CollectionsMarshal.GetValueRefOrAddDefault(_storages, address, out bool exists);
                if (!exists)
                {
                    value = _storageTreeFactory.Create(address, _trieStore.GetTrieStore(address.ToAccountPath), _stateProvider.GetStorageRoot(address), StateRoot, _logManager);
                }
            }

            return value;
        }

        private StorageTree GetOrCreateStorage(Address address)
        {
            ref StorageTree? value = ref CollectionsMarshal.GetValueRefOrAddDefault(_storages, address, out bool exists);
            if (!exists)
            {
                value = _storageTreeFactory.Create(address, _trieStore.GetTrieStore(address.ToAccountPath), _stateProvider.GetStorageRoot(address), StateRoot, _logManager);
            }

            return value;
        }

        public void WarmUp(in StorageCell storageCell, bool isEmpty)
        {
            if (isEmpty)
            {
                if (_preBlockCache is not null)
                {
                    _preBlockCache[storageCell] = [];
                }
            }
            else
            {
                LoadFromTree(in storageCell);
            }
        }

        private ReadOnlySpan<byte> LoadFromTree(in StorageCell storageCell)
        {
            if (_blockWriteCache.TryGetValue(storageCell.Address, out SelfDestructDictionary<UInt256, byte[]>? writeDict) &&
                writeDict.TryGetValue(storageCell.Index, out var value))
            {
                Db.Metrics.IncrementStorageTreeCache();
                if (!storageCell.IsHash) PushToRegistryOnly(storageCell, value);
                return value;
            }

            ref SelfDestructDictionary<UInt256, byte[]>? dict = ref CollectionsMarshal.GetValueRefOrAddDefault(_blockReadCache, storageCell.Address, out bool exists);
            if (!exists)
            {
                dict = new SelfDestructDictionary<UInt256, byte[]>(StorageTree.EmptyBytes);
            }

            ref byte[]? valueRef = ref dict.GetValueRefOrAddDefault(storageCell.Index, out exists);
            if (!exists)
            {
                valueRef = !_populatePreBlockCache ?
                    LoadFromTreeReadPreWarmCache(in storageCell) :
                    LoadFromTreePopulatePrewarmCache(in storageCell);
            }
            else
            {
                Db.Metrics.IncrementStorageTreeCache();
            }

            if (!storageCell.IsHash) PushToRegistryOnly(storageCell, valueRef);
            return valueRef;
        }

        private byte[] LoadFromTreePopulatePrewarmCache(in StorageCell storageCell)
        {
            long priorReads = Db.Metrics.ThreadLocalStorageTreeReads;

            byte[] value = _preBlockCache is not null
                ? _preBlockCache.GetOrAdd(storageCell, _loadFromTree)
                : _loadFromTree(storageCell);

            if (Db.Metrics.ThreadLocalStorageTreeReads == priorReads)
            {
                // Read from Concurrent Cache
                Db.Metrics.IncrementStorageTreeCache();
            }
            return value;
        }

        private byte[] LoadFromTreeReadPreWarmCache(in StorageCell storageCell)
        {
            if (_preBlockCache?.TryGetValue(storageCell, out byte[] value) ?? false)
            {
                Db.Metrics.IncrementStorageTreeCache();
            }
            else
            {
                value = _loadFromTree(storageCell);
            }
            return value;
        }

        private byte[] LoadFromTreeStorage(StorageCell storageCell)
        {
            StorageTree tree = GetOrCreateStorage(storageCell.Address);
            Db.Metrics.IncrementStorageTreeReads();
            return !storageCell.IsHash ? tree.Get(storageCell.Index) : tree.GetArray(storageCell.Hash.Bytes);
        }

        private void PushToRegistryOnly(in StorageCell cell, byte[] value)
        {
            StackList<int> stack = SetupRegistry(cell);
            _originalValues[cell] = value;
            stack.Push(_changes.Count);
            _changes.Add(new Change(ChangeType.JustCache, cell, value));
        }

        private static void ReportChanges(IStorageTracer tracer, Dictionary<StorageCell, ChangeTrace> trace)
        {
            foreach ((StorageCell address, ChangeTrace change) in trace)
            {
                byte[] before = change.Before;
                byte[] after = change.After;

                if (!Bytes.AreEqual(before, after))
                {
                    tracer.ReportStorageChange(address, before, after);
                }
            }
        }

        private Hash256 RecalculateRootHash(Address address)
        {
            StorageTree storageTree = GetOrCreateStorage(address);
            storageTree.UpdateRootHash();
            return storageTree.RootHash;
        }

        /// <summary>
        /// Clear all storage at specified address
        /// </summary>
        /// <param name="address">Contract address</param>
        public override void ClearStorage(Address address)
        {
            base.ClearStorage(address);

            ref SelfDestructDictionary<UInt256, byte[]>? dict = ref CollectionsMarshal.GetValueRefOrAddDefault(_blockWriteCache, address, out bool exists);
            if (!exists)
            {
                dict = new SelfDestructDictionary<UInt256, byte[]>(StorageTree.EmptyBytes);
            }

            dict.SelfDestruct();

            // here it is important to make sure that we will not reuse the same tree when the contract is revived
            // by means of CREATE 2 - notice that the cached trie may carry information about items that were not
            // touched in this block, hence were not zeroed above
            // TODO: how does it work with pruning?
            _toUpdateRoots.Remove(address);
            _storages[address] = new StorageTree(_trieStore.GetTrieStore(address.ToAccountPath), Keccak.EmptyTreeHash, _logManager);
        }

        private class StorageTreeFactory : IStorageTreeFactory
        {
            public StorageTree Create(Address address, IScopedTrieStore trieStore, Hash256 storageRoot, Hash256 stateRoot, ILogManager? logManager)
                => new(trieStore, storageRoot, logManager);
        }

        private class SelfDestructDictionary<TKey, TValue>(TValue destructedValue) : IEnumerable<KeyValuePair<TKey, TValue>>
            where TKey : notnull
        {
            private bool _selfDestruct;
            private readonly Dictionary<TKey, TValue> _dictionary = new();

            public void SelfDestruct()
            {
                _selfDestruct = true;
                _dictionary.Clear();
            }

            public ref TValue? GetValueRefOrAddDefault(TKey storageCellIndex, out bool exists)
            {
                ref TValue value = ref CollectionsMarshal.GetValueRefOrAddDefault(_dictionary, storageCellIndex, out exists);
                if (!exists && _selfDestruct)
                {
                    value = destructedValue;
                    exists = true;
                }
                return ref value;
            }

            public bool TryGetValue(TKey key, out TValue value)
                => _dictionary.TryGetValue(key, out value);

            public TValue? this[TKey key]
            {
                set => _dictionary[key] = value;
            }

            public Dictionary<TKey, TValue>.Enumerator GetEnumerator()
                => _dictionary.GetEnumerator();

            IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
                => GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator()
                => GetEnumerator();
        }
    }
}
