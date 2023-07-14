// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Nethermind.Core;
using Nethermind.Core.Caching;
using Nethermind.Core.Collections;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Core.Resettables;
using Nethermind.Logging;
using Nethermind.State.Tracing;
using Nethermind.Trie.Pruning;

namespace Nethermind.State
{
    /// <summary>
    /// Manages persistent storage allowing for snapshotting and restoring
    /// Persists data to ITrieStore
    /// </summary>
    internal class PersistentStorageProvider : PartialStorageProviderBase
    {
        private readonly ITrieStore _trieStore;
        private readonly StateProvider _stateProvider;
        private readonly ILogManager? _logManager;
        private readonly ResettableDictionary<Address, StorageTree> _storages = new();
        private readonly Dictionary<Address, int> _accessedStorages = new();
        /// <summary>
        /// EIP-1283
        /// </summary>
        private readonly ResettableDictionary<StorageCell, byte[]> _originalValues = new();
        private readonly ResettableHashSet<StorageCell> _committedThisRound = new();

        // state root aware caching
        private const int SingeCacheEntryRoughEstimate =
            32 + // StorageCell.Index
            20 + MemorySizes.SmallObjectOverhead + MemorySizes.ArrayOverhead + // Storage.Access
            32 + MemorySizes.ArrayOverhead + // value + MemorySizes.ArrayOverhead
            MemorySizes.SmallObjectOverhead + MemorySizes.RefSize +
            MemorySizes.RefSize + // LinkedListNode, plus its refs
            MemorySizes.SmallObjectOverhead; // for dictionary

        private const int BlockAccessDecay = 10;
        private readonly LruCache<StorageCell, byte[]>? _cellCache;

        public PersistentStorageProvider(ITrieStore? trieStore, StateProvider? stateProvider, ILogManager? logManager,
            int cellCacheSize)
            : base(logManager)
        {
            _trieStore = trieStore ?? throw new ArgumentNullException(nameof(trieStore));
            _stateProvider = stateProvider ?? throw new ArgumentNullException(nameof(stateProvider));
            _logManager = logManager ?? throw new ArgumentNullException(nameof(logManager));
            _cellCache = cellCacheSize > 0
                ? new LruCache<StorageCell, byte[]>(cellCacheSize, "Storage Cell Cache")
                : null;
        }

        /// <summary>
        /// Reset the storage state
        /// </summary>
        public override void Reset()
        {
            base.Reset();
            _storages.Reset();
            _cellCache?.Clear();
            _accessedStorages.Clear();
            _originalValues.Clear();
            _committedThisRound.Clear();
        }

        /// <summary>
        /// Get the current value at the specified location
        /// </summary>
        /// <param name="storageCell">Storage location</param>
        /// <returns>Value at location</returns>
        protected override byte[] GetCurrentValue(in StorageCell storageCell)
        {
            _accessedStorages[storageCell.Address] = BlockAccessDecay;
            return TryGetCachedValue(storageCell, out byte[]? bytes) ? bytes! : LoadFromTree(storageCell);
        }

        /// <summary>
        /// Return the original persistent storage value from the storage cell
        /// </summary>
        /// <param name="storageCell"></param>
        /// <returns></returns>
        public byte[] GetOriginal(in StorageCell storageCell)
        {
            if (!_originalValues.ContainsKey(storageCell))
            {
                throw new InvalidOperationException("Get original should only be called after get within the same caching round");
            }

            if (_transactionChangesSnapshots.TryPeek(out int snapshot))
            {
                if (_intraBlockCache.TryGetValue(storageCell, out StackList<int> stack))
                {
                    if (stack.TryGetSearchedItem(snapshot, out int lastChangeIndexBeforeOriginalSnapshot))
                    {
                        return _changes[lastChangeIndexBeforeOriginalSnapshot]!.Value;
                    }
                }
            }

            return _originalValues[storageCell];
        }


        /// <summary>
        /// Called by Commit
        /// Used for persistent storage specific logic
        /// </summary>
        /// <param name="tracer">Storage tracer</param>
        protected override void CommitCore(IStorageTracer tracer)
        {
            if (_logger.IsTrace) _logger.Trace("Committing storage changes");

            if (_changes[_currentPosition] is null)
            {
                throw new InvalidOperationException($"Change at current position {_currentPosition} was null when commiting {nameof(PartialStorageProviderBase)}");
            }

            if (_changes[_currentPosition + 1] is not null)
            {
                throw new InvalidOperationException($"Change after current position ({_currentPosition} + 1) was not null when commiting {nameof(PartialStorageProviderBase)}");
            }

            HashSet<Address> toUpdateRoots = new();

            bool isTracing = tracer.IsTracingStorage;
            Dictionary<StorageCell, ChangeTrace>? trace = null;
            if (isTracing)
            {
                trace = new Dictionary<StorageCell, ChangeTrace>();
            }

            for (int i = 0; i <= _currentPosition; i++)
            {
                Change change = _changes[_currentPosition - i];
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
                if (forAssertion != _currentPosition - i)
                {
                    throw new InvalidOperationException($"Expected checked value {forAssertion} to be equal to {_currentPosition} - {i}");
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

                        StorageTree tree = GetOrCreateStorage(change.StorageCell.Address);
                        Db.Metrics.StorageTreeWrites++;
                        toUpdateRoots.Add(change.StorageCell.Address);
                        // set in the tree
                        tree.Set(change.StorageCell.Index, change.Value);
                        // and in the cache
                        _cellCache?.Set(change.StorageCell, change.Value);
                        if (isTracing)
                        {
                            trace![change.StorageCell] = new ChangeTrace(change.Value);
                        }

                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            // TODO: it seems that we are unnecessarily recalculating root hashes all the time in storage?
            foreach (Address address in toUpdateRoots)
            {
                // since the accounts could be empty accounts that are removing (EIP-158)
                if (_stateProvider.AccountExists(address))
                {
                    Keccak root = RecalculateRootHash(address);

                    // _logger.Warn($"Recalculating storage root {address}->{root} ({toUpdateRoots.Count})");
                    _stateProvider.UpdateStorageRoot(address, root);
                }
            }

            base.CommitCore(tracer);
            _originalValues.Reset();
            _committedThisRound.Reset();

            if (isTracing)
            {
                ReportChanges(tracer!, trace!);
            }
        }

        /// <summary>
        /// Commit persistent storage trees
        /// </summary>
        /// <param name="blockNumber">Current block number</param>
        public void CommitTrees(long blockNumber)
        {
            foreach (KeyValuePair<Address, StorageTree> storage in _storages)
            {
                storage.Value.Commit(blockNumber);
            }

            // TODO: maybe I could update storage roots only now?

            foreach (KeyValuePair<Address, StorageTree> storage in _storages)
            {
                ref int blockAccessDecay = ref CollectionsMarshal.GetValueRefOrNullRef(_accessedStorages, storage.Key);
                if (Unsafe.IsNullRef(ref blockAccessDecay))
                {
                    ThrowInvalidOperation();
                }

                int decay = (--blockAccessDecay);
                if (decay <= 0)
                {
                    // Storage hasn't been accessed in BlockAccessDecay blocks, so we can remove it from the cache
                    _accessedStorages.Remove(storage.Key);
                    _storages.Remove(storage.Key);
                }
            }

            [DoesNotReturn]
            [StackTraceHidden]
            static void ThrowInvalidOperation()
            {
                throw new InvalidOperationException("Access decay was null");
            }
        }

        private StorageTree GetOrCreateStorage(Address address)
        {
            _accessedStorages[address] = BlockAccessDecay;
            if (!_storages.TryGetValue(address, out StorageTree storageTree))
            {
                storageTree = new(_trieStore, _stateProvider.GetStorageRoot(address), _logManager);
                _storages[address] = storageTree;
                return storageTree;
            }

            return storageTree;
        }

        private byte[] LoadFromTree(in StorageCell storageCell)
        {
            if (!TryLoadFromCache(storageCell, out byte[]? value))
            {
                StorageTree tree = GetOrCreateStorage(storageCell.Address);

                Db.Metrics.StorageTreeReads++;
                value = tree.Get(storageCell.Index);
                // cache write-through
                _cellCache?.Set(storageCell, value);
            }
            else
            {
                _accessedStorages[storageCell.Address] = BlockAccessDecay;
            }

            PushToRegistryOnly(storageCell, value);
            return value;
        }

        /// <summary>
        /// Tries to retrieve the storage cell from an LRU cache.
        /// </summary>
        private bool TryLoadFromCache(in StorageCell storageCell, out byte[]? value)
        {
            if (_cellCache != null && _cellCache.TryGet(storageCell, out value))
            {
                Db.Metrics.StorageTreeCacheReads++;
                return true;
            }

            value = default;
            return false;
        }

        private void PushToRegistryOnly(in StorageCell cell, byte[] value)
        {
            SetupRegistry(cell);
            IncrementChangePosition();
            _intraBlockCache[cell].Push(_currentPosition);
            _originalValues[cell] = value;
            _changes[_currentPosition] = new Change(ChangeType.JustCache, cell, value);
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

        private Keccak RecalculateRootHash(Address address)
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

            // here it is important to make sure that we will not reuse the same tree when the contract is revived
            // by means of CREATE 2 - notice that the cached trie may carry information about items that were not
            // touched in this block, hence were not zeroed above
            // TODO: how does it work with pruning?
            _storages[address] = new StorageTree(_trieStore, Keccak.EmptyTreeHash, _logManager);
            // Mark it as unaccessed
            _accessedStorages[address] = 0;
            // TODO: big penalty on clearing storage, potentially, could benefit from less a-bomb cleanup
            // for a given address only
            _cellCache?.Clear();
        }
    }
}
