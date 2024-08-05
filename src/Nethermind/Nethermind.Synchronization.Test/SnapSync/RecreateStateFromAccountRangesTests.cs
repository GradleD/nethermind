// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Core.Test.Builders;
using Nethermind.Db;
using Nethermind.Int256;
using Nethermind.Logging;
using Nethermind.Paprika;
using Nethermind.State;
using Nethermind.State.Proofs;
using Nethermind.State.Snap;
using Nethermind.Synchronization.SnapSync;
using Nethermind.Trie;
using Nethermind.Trie.Pruning;
using NUnit.Framework;

namespace Nethermind.Synchronization.Test.SnapSync
{
    [TestFixture]
    public class RecreateStateFromAccountRangesTests
    {
        private StateTree _inputTree;
        private StateTree _inputTree_7;
        private StateTree _inputTree_9;

        [OneTimeSetUp]
        public void Setup()
        {
            _inputTree = TestItem.Tree.GetStateTree(maxCount: 6);
            _inputTree_7 = TestItem.Tree.GetStateTree(maxCount: 7);
            _inputTree_9 = TestItem.Tree.GetStateTree(maxCount: 9);
        }

        private byte[][] CreateProofForPath(ReadOnlySpan<byte> path, StateTree tree = null)
        {
            AccountProofCollector accountProofCollector = new(path);
            if (tree is null)
            {
                tree = _inputTree;
            }
            tree.Accept(accountProofCollector, tree.RootHash);
            return accountProofCollector.BuildResult().Proof;
        }

        //[Test]
        public void Test01()
        {
            Hash256 rootHash = _inputTree.RootHash;   // "0x8c81279168edc449089449bc0f2136fc72c9645642845755633cf259cd97988b"

            byte[][] firstProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[0].Path.Bytes);
            byte[][] lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[5].Path.Bytes);

            MemDb db = new();
            TrieStore fullStore = new(db, LimboLogs.Instance);
            IScopedTrieStore store = fullStore.GetTrieStore(null);
            StateTree tree = new(store, LimboLogs.Instance);

            IList<TrieNode> nodes = new List<TrieNode>();
            TreePath emptyPath = TreePath.Empty;

            for (int i = 0; i < (firstProof!).Length; i++)
            {
                byte[] nodeBytes = (firstProof!)[i];
                var node = new TrieNode(NodeType.Unknown, nodeBytes);
                node.ResolveKey(store, ref emptyPath, i == 0);

                nodes.Add(node);
                if (i < (firstProof!).Length - 1)
                {
                    //IBatch batch = store.GetOrStartNewBatch();
                    //batch[node.Keccak!.Bytes] = nodeBytes;
                    //db.Set(node.Keccak!, nodeBytes);
                }
            }

            for (int i = 0; i < (lastProof!).Length; i++)
            {
                byte[] nodeBytes = (lastProof!)[i];
                var node = new TrieNode(NodeType.Unknown, nodeBytes);
                node.ResolveKey(store, ref emptyPath, i == 0);

                nodes.Add(node);
                if (i < (lastProof!).Length - 1)
                {
                    //IBatch batch = store.GetOrStartNewBatch();
                    //batch[node.Keccak!.Bytes] = nodeBytes;
                    //db.Set(node.Keccak!, nodeBytes);
                }
            }

            tree.RootRef = nodes[0];

            tree.Set(TestItem.Tree.AccountsWithPaths[0].Path, TestItem.Tree.AccountsWithPaths[0].Account);
            tree.Set(TestItem.Tree.AccountsWithPaths[1].Path, TestItem.Tree.AccountsWithPaths[1].Account);
            tree.Set(TestItem.Tree.AccountsWithPaths[2].Path, TestItem.Tree.AccountsWithPaths[2].Account);
            tree.Set(TestItem.Tree.AccountsWithPaths[3].Path, TestItem.Tree.AccountsWithPaths[3].Account);
            tree.Set(TestItem.Tree.AccountsWithPaths[4].Path, TestItem.Tree.AccountsWithPaths[4].Account);
            tree.Set(TestItem.Tree.AccountsWithPaths[5].Path, TestItem.Tree.AccountsWithPaths[5].Account);

            tree.Commit(0);

            Assert.That(tree.RootHash, Is.EqualTo(_inputTree.RootHash));
            Assert.That(db.Keys.Count, Is.EqualTo(6));  // we don't persist proof nodes (boundary nodes)
            Assert.IsFalse(db.KeyExists(rootHash)); // the root node is a part of the proof nodes
        }

        [Test]
        public void RecreateAccountStateFromOneRangeWithNonExistenceProof()
        {
            Hash256 rootHash = _inputTree.RootHash;   // "0x8c81279168edc449089449bc0f2136fc72c9645642845755633cf259cd97988b"

            byte[][] firstProof = CreateProofForPath(Keccak.Zero.Bytes);
            byte[][] lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[5].Path.Bytes);

            MemDb db = new();
            IDbProvider dbProvider = new DbProvider();
            dbProvider.RegisterDb(DbNames.State, db);
            IStateFactory stateFactory = new PaprikaStateFactory();
            ProgressTracker progressTracker = new(null, dbProvider.StateDb, stateFactory, LimboLogs.Instance);
            SnapProvider snapProvider = new(progressTracker, dbProvider.StateDb, new NodeStorage(db), LimboLogs.Instance);

            AddRangeResult result = snapProvider.AddAccountRange(1, rootHash, Keccak.Zero, TestItem.Tree.AccountsWithPaths[0..6], firstProof!.Concat(lastProof!).ToArray());

            Assert.That(result, Is.EqualTo(AddRangeResult.OK));
            //Assert.That(db.Keys.Count, Is.EqualTo(10));  // we persist proof nodes (boundary nodes) via stitching
            //Assert.IsFalse(db.KeyExists(rootHash));
        }

        [Test]
        public void RecreateAccountStateFromOneRangeWithExistenceProof()
        {
            Hash256 rootHash = _inputTree.RootHash;   // "0x8c81279168edc449089449bc0f2136fc72c9645642845755633cf259cd97988b"

            byte[][] firstProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[0].Path.Bytes);
            byte[][] lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[5].Path.Bytes);

            MemDb db = new();
            IDbProvider dbProvider = new DbProvider();
            dbProvider.RegisterDb(DbNames.State, db);
            IStateFactory stateFactory = new PaprikaStateFactory();
            ProgressTracker progressTracker = new(null, dbProvider.StateDb, stateFactory, LimboLogs.Instance);
            SnapProvider snapProvider = new(progressTracker, dbProvider.StateDb, new NodeStorage(db), LimboLogs.Instance);

            var result = snapProvider.AddAccountRange(1, rootHash, TestItem.Tree.AccountsWithPaths[0].Path, TestItem.Tree.AccountsWithPaths[0..6], firstProof!.Concat(lastProof!).ToArray());

            IRawState rawState = progressTracker.GetSyncState();
            rawState.Finalize(200);

            var state = stateFactory.Get(rootHash);
            foreach (var item in TestItem.Tree.AccountsWithPaths[0..6])
            {
                Account a = state.Get(item.Path);
                Assert.That((item.Account.IsTotallyEmpty && a is null) || (!item.Account.IsTotallyEmpty && a is not null), Is.True);
                Assert.That(a?.Balance ?? 0, Is.EqualTo(item.Account.Balance));
            }

            Assert.That(result, Is.EqualTo(AddRangeResult.OK));
            //Assert.That(db.Keys.Count, Is.EqualTo(10));  // we persist proof nodes (boundary nodes) via stitching
            //Assert.IsFalse(db.KeyExists(rootHash));

            stateFactory.DisposeAsync();
        }

        [Test]
        public void RecreateAccountStateFromOneRangeWithoutProof()
        {
            //Hash256 rootHash = _inputTree.RootHash;   // "0x8c81279168edc449089449bc0f2136fc72c9645642845755633cf259cd97988b"
            Hash256 rootHash = _inputTree_9.RootHash;

            MemDb db = new();
            DbProvider dbProvider = new();
            dbProvider.RegisterDb(DbNames.State, db);
            IStateFactory stateFactory = new PaprikaStateFactory();
            ProgressTracker progressTracker = new(null, dbProvider.GetDb<IDb>(DbNames.State), stateFactory, LimboLogs.Instance);
            SnapProvider snapProvider = CreateSnapProvider(progressTracker, dbProvider);

            var result = snapProvider.AddAccountRange(1, rootHash, TestItem.Tree.AccountsWithPaths[0].Path, TestItem.Tree.AccountsWithPaths);

            Assert.That(result, Is.EqualTo(AddRangeResult.OK));

            IRawState rawState = progressTracker.GetSyncState();
            rawState.Finalize(200);

            var state = stateFactory.Get(rootHash);
            AssertAllAccounts(state, TestItem.Tree.AccountsWithPaths.Length);

            //Assert.That(db.Keys.Count, Is.EqualTo(10));  // we don't have the proofs so we persist all nodes
            //Assert.IsFalse(db.KeyExists(rootHash)); // the root node is NOT a part of the proof nodes
        }

        [Test]
        public void RecreateStorageStateFromOneRangeWithoutProof_File()
        {
            var accounts = new List<PathWithAccount>();
            var proofs = new List<byte[]>();

            Hash256 rootHash;
            Hash256 staringHash;
            Hash256 calculatedHash;

            using (var sr = new StreamReader(@"C:\Temp\case_0x0000000000000000000000000000000000000000000000000000000000000000_0x000850acb57ae8470f5b1b712acad557dca7e9988dd960c67e867093ae19dc77.txt"))
            {
                string line = "";

                staringHash = new Hash256(sr.ReadLine());
                rootHash = new Hash256(sr.ReadLine());
                calculatedHash = new Hash256(sr.ReadLine());

                while ((line = sr.ReadLine()) != null)
                {
                    if (string.IsNullOrEmpty(line))
                        break;
                    var parts = line.Split('|');

                    Account account = new Account(UInt256.Parse(parts[1]), UInt256.Parse(parts[2]), new Hash256(parts[3]), new Hash256(parts[4]));

                    accounts.Add(new PathWithAccount(new Hash256(parts[0]), account));
                }

                while ((line = sr.ReadLine()) != null)
                {
                    if (string.IsNullOrEmpty(line))
                        break;

                    var proof = Bytes.FromHexString(line);

                    proofs.Add(proof);
                }
            }

            TrieStore trieStore = new TrieStore(new MemDb(), NullLogManager.Instance);
            StateTree trie = new StateTree(trieStore.GetTrieStore(null), NullLogManager.Instance);

            for (int i = 0; i < accounts.Count; i++)
            {
                trie.Set(accounts[i].Path, accounts[i].Account);
            }
            trie.UpdateRootHash();

            MemDb db = new();
            IDbProvider dbProvider = new DbProvider();
            dbProvider.RegisterDb(DbNames.State, db);
            IStateFactory stateFactory = new PaprikaStateFactory();
            ProgressTracker progressTracker = new(null, dbProvider.StateDb, stateFactory, LimboLogs.Instance);
            SnapProvider snapProvider = new(progressTracker, dbProvider.StateDb, new NodeStorage(db), LimboLogs.Instance);

            var result = snapProvider.AddAccountRange(1, rootHash, staringHash, accounts, proofs);

            Assert.That(result, Is.EqualTo(AddRangeResult.OK));

            IRawState rawState = progressTracker.GetSyncState();
            rawState.Finalize(1);
            IReadOnlyState state = stateFactory.GetReadOnly(rawState.StateRoot);
            //AssertAllStorageSlots(state, 6);
        }

        [Test]
        public void RecreateAccountStateFromMultipleRange()
        {
            Hash256 rootHash = _inputTree.RootHash;   // "0x8c81279168edc449089449bc0f2136fc72c9645642845755633cf259cd97988b"

            // output state
            MemDb db = new();
            IDbProvider dbProvider = new DbProvider();
            dbProvider.RegisterDb(DbNames.State, db);
            IStateFactory stateFactory = new PaprikaStateFactory();
            ProgressTracker progressTracker = new(null, dbProvider.StateDb, stateFactory, LimboLogs.Instance);
            SnapProvider snapProvider = new(progressTracker, dbProvider.StateDb, new NodeStorage(db), LimboLogs.Instance);

            byte[][] firstProof = CreateProofForPath(Keccak.Zero.Bytes);
            byte[][] lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[1].Path.Bytes);
            //IRawState rawState = progressTracker.GetSyncState();

            var result1 = snapProvider.AddAccountRange(1, rootHash, Keccak.Zero, TestItem.Tree.AccountsWithPaths[0..2], firstProof!.Concat(lastProof!).ToArray());

            Assert.That(result1, Is.EqualTo(AddRangeResult.OK));
            //Assert.That(db.Keys.Count, Is.EqualTo(2));

            firstProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[2].Path.Bytes);
            lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[3].Path.Bytes);

            var result2 = snapProvider.AddAccountRange(1, rootHash, TestItem.Tree.AccountsWithPaths[2].Path, TestItem.Tree.AccountsWithPaths[2..4], firstProof!.Concat(lastProof!).ToArray());
            Assert.That(result2, Is.EqualTo(AddRangeResult.OK));

            //Assert.That(db.Keys.Count, Is.EqualTo(5));  // we don't persist proof nodes (boundary nodes)

            firstProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[4].Path.Bytes);
            lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[5].Path.Bytes);

            var result3 = snapProvider.AddAccountRange(1, rootHash, TestItem.Tree.AccountsWithPaths[4].Path, TestItem.Tree.AccountsWithPaths[4..6], firstProof!.Concat(lastProof!).ToArray());

            Assert.That(result3, Is.EqualTo(AddRangeResult.OK));
            //Assert.That(db.Keys.Count, Is.EqualTo(10));  // we persist proof nodes (boundary nodes) via stitching
            //Assert.IsFalse(db.KeyExists(rootHash));

            IRawState rawState = progressTracker.GetNewRawState();
            rawState.Finalize(1);

            var state = stateFactory.Get(rootHash);
            foreach (var item in TestItem.Tree.AccountsWithPaths[0..6])
            {
                Account a = state.Get(item.Path);
                Assert.That((item.Account.IsTotallyEmpty && a is null) || (!item.Account.IsTotallyEmpty && a is not null), Is.True);
                Assert.That(a?.Balance ?? 0, Is.EqualTo(item.Account.Balance));
            }
        }

        [Test]
        public void RecreateAccountStateFromMultipleRange_Single()
        {
            Hash256 rootHash = _inputTree_9.RootHash;   // "0x8c81279168edc449089449bc0f2136fc72c9645642845755633cf259cd97988b"

            // output state
            MemDb db = new();
            IDbProvider dbProvider = new DbProvider();
            dbProvider.RegisterDb(DbNames.State, db);
            IStateFactory stateFactory = new PaprikaStateFactory();
            ProgressTracker progressTracker = new(null, dbProvider.StateDb, stateFactory, LimboLogs.Instance);
            SnapProvider snapProvider = new(progressTracker, dbProvider.StateDb, new NodeStorage(db), LimboLogs.Instance);

            for (int i = 0; i < 9; i++)
            {
                byte[][] firstProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[i].Path.Bytes, _inputTree_9);
                var result = snapProvider.AddAccountRange(1, rootHash, TestItem.Tree.AccountsWithPaths[i].Path, TestItem.Tree.AccountsWithPaths[i..(i+1)], firstProof);
                Assert.That(result, Is.EqualTo(AddRangeResult.OK));
            }

            IRawState rawState = progressTracker.GetNewRawState();
            rawState.Finalize(1);

            var state = stateFactory.Get(rootHash);
            AssertAllAccounts(state, 9);
        }

        private struct Range
        {
            public int start;
            public int end;
        };

        [Test]
        public void RecreateAccountStateFromMultipleRange_MT()
        {
            Hash256 rootHash = _inputTree.RootHash;   // "0x8c81279168edc449089449bc0f2136fc72c9645642845755633cf259cd97988b"

            // output state
            MemDb db = new();
            IDbProvider dbProvider = new DbProvider();
            dbProvider.RegisterDb(DbNames.State, db);
            IStateFactory stateFactory = new PaprikaStateFactory();
            ProgressTracker progressTracker = new(null, dbProvider.StateDb, stateFactory, LimboLogs.Instance);
            SnapProvider snapProvider = new(progressTracker, dbProvider.StateDb, new NodeStorage(db), LimboLogs.Instance);

            var addAccountRange = (Object o) =>
            {
                Range r = (Range)o;
                byte[][] firstProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[r.start].Path.Bytes);
                byte[][] lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[r.end].Path.Bytes);

                var result = snapProvider.AddAccountRange(1, rootHash, TestItem.Tree.AccountsWithPaths[r.start].Path,
                    TestItem.Tree.AccountsWithPaths[r.start..(r.end + 1)], firstProof!.Concat(lastProof!).ToArray());

                Assert.That(result, Is.EqualTo(AddRangeResult.OK));
            };

            var t1 = new Task(addAccountRange, new Range() {start = 0, end = 1});
            var t2 = new Task(addAccountRange, new Range() { start = 2, end = 3 });
            var t3 = new Task(addAccountRange, new Range() { start = 4, end = 5 });

            t1.Start();
            t2.Start();
            t3.Start();

            Task.WaitAll(new[] { t1, t2, t3 });

            IRawState rawState = progressTracker.GetNewRawState();
            rawState.Finalize(1);

            var state = stateFactory.Get(rootHash);
            AssertAllAccounts(state, 6);
        }

        [Test]
        public void RecreateAccountStateFromMultipleRange_PivotChange_High()
        {
            Hash256 rootHash = _inputTree.RootHash;   // "0x8c81279168edc449089449bc0f2136fc72c9645642845755633cf259cd97988b"
            Hash256 newRootHash = _inputTree_7.RootHash;

            // output state
            MemDb db = new();
            IDbProvider dbProvider = new DbProvider();
            dbProvider.RegisterDb(DbNames.State, db);
            IStateFactory stateFactory = new PaprikaStateFactory();
            ProgressTracker progressTracker = new(null, dbProvider.StateDb, stateFactory, LimboLogs.Instance);
            SnapProvider snapProvider = new(progressTracker, dbProvider.StateDb, new NodeStorage(db), LimboLogs.Instance);

            byte[][] firstProof = CreateProofForPath(Keccak.Zero.Bytes);
            byte[][] lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[1].Path.Bytes);

            var result1 = snapProvider.AddAccountRange(1, rootHash, Keccak.Zero, TestItem.Tree.AccountsWithPaths[0..2], firstProof!.Concat(lastProof!).ToArray());

            Assert.That(result1, Is.EqualTo(AddRangeResult.OK));

            firstProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[2].Path.Bytes, _inputTree_7);
            lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[3].Path.Bytes, _inputTree_7);

            var result2 = snapProvider.AddAccountRange(1, newRootHash, TestItem.Tree.AccountsWithPaths[2].Path, TestItem.Tree.AccountsWithPaths[2..4], firstProof!.Concat(lastProof!).ToArray());
            Assert.That(result2, Is.EqualTo(AddRangeResult.OK));

            firstProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[4].Path.Bytes, _inputTree_7);
            lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[6].Path.Bytes, _inputTree_7);

            var result3 = snapProvider.AddAccountRange(1, newRootHash, TestItem.Tree.AccountsWithPaths[4].Path, TestItem.Tree.AccountsWithPaths[4..7], firstProof!.Concat(lastProof!).ToArray());

            Assert.That(result3, Is.EqualTo(AddRangeResult.OK));

            IRawState rawState = progressTracker.GetSyncState();
            rawState.Finalize(1);

            var state = stateFactory.Get(newRootHash);
            AssertAllAccounts(state, 7);
        }

        [Test]
        public void RecreateAccountStateFromMultipleRange_PivotChange_Low()
        {
            Hash256 rootHash = _inputTree.RootHash;   // "0x8c81279168edc449089449bc0f2136fc72c9645642845755633cf259cd97988b"
            StateTree newPivotTree = TestItem.Tree.GetStateTree(maxCount: 6);
            newPivotTree.Set(TestItem.Tree.AccountAddress0, TestItem.Tree.Account10);
            newPivotTree.Commit(1);
            Hash256 rootHashNew = newPivotTree.RootHash;

            // output state
            MemDb db = new();
            IDbProvider dbProvider = new DbProvider();
            dbProvider.RegisterDb(DbNames.State, db);
            IStateFactory stateFactory = new PaprikaStateFactory();
            ProgressTracker progressTracker = new(null, dbProvider.StateDb, stateFactory, LimboLogs.Instance);
            SnapProvider snapProvider = new(progressTracker, dbProvider.StateDb, new NodeStorage(db), LimboLogs.Instance);

            byte[][] firstProof = CreateProofForPath(Keccak.Zero.Bytes);
            byte[][] lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[1].Path.Bytes);

            var result1 = snapProvider.AddAccountRange(1, rootHash, Keccak.Zero, TestItem.Tree.AccountsWithPaths[0..2], firstProof!.Concat(lastProof!).ToArray());

            Assert.That(result1, Is.EqualTo(AddRangeResult.OK));

            firstProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[2].Path.Bytes, newPivotTree);
            lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[3].Path.Bytes, newPivotTree);

            var result2 = snapProvider.AddAccountRange(1, rootHashNew, TestItem.Tree.AccountsWithPaths[2].Path, TestItem.Tree.AccountsWithPaths[2..4], firstProof!.Concat(lastProof!).ToArray());
            Assert.That(result2, Is.EqualTo(AddRangeResult.OK));

            firstProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[4].Path.Bytes, newPivotTree);
            lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[5].Path.Bytes, newPivotTree);

            var result3 = snapProvider.AddAccountRange(1, rootHashNew, TestItem.Tree.AccountsWithPaths[4].Path, TestItem.Tree.AccountsWithPaths[4..6], firstProof!.Concat(lastProof!).ToArray());

            Assert.That(result3, Is.EqualTo(AddRangeResult.OK));

            //re-add changed account to simulate healing
            firstProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[0].Path.Bytes, newPivotTree);
            var resultHeal = snapProvider.AddAccountRange(1, rootHashNew, Keccak.Zero, new[] {new PathWithAccount(TestItem.Tree.AccountAddress0, TestItem.Tree.Account10)}, firstProof);

            Assert.That(resultHeal, Is.EqualTo(AddRangeResult.OK));

            IRawState rawState = progressTracker.GetSyncState();
            rawState.Finalize(1);

            var state = stateFactory.Get(rootHashNew);

            foreach (var item in TestItem.Tree.AccountsWithPaths[1..6])
            {
                Account a = state.Get(item.Path);
                Assert.That((item.Account.IsTotallyEmpty && a is null) || (!item.Account.IsTotallyEmpty && a is not null), Is.True);
                Assert.That(a?.Balance ?? 0, Is.EqualTo(item.Account.Balance));
            }
        }

        [Test]
        public void RecreateAccountStateFromMultipleRange_InReverseOrder()
        {
            Hash256 rootHash = _inputTree.RootHash;   // "0x8c81279168edc449089449bc0f2136fc72c9645642845755633cf259cd97988b"

            // output state
            MemDb db = new();
            IDbProvider dbProvider = new DbProvider();
            dbProvider.RegisterDb(DbNames.State, db);
            IStateFactory stateFactory = new PaprikaStateFactory();
            ProgressTracker progressTracker = new(null, dbProvider.StateDb, stateFactory, LimboLogs.Instance);
            SnapProvider snapProvider = new(progressTracker, dbProvider.StateDb, new NodeStorage(db), LimboLogs.Instance);

            byte[][] firstProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[4].Path.Bytes);
            byte[][] lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[5].Path.Bytes);
            var result3 = snapProvider.AddAccountRange(1, rootHash, TestItem.Tree.AccountsWithPaths[4].Path, TestItem.Tree.AccountsWithPaths[4..6], firstProof!.Concat(lastProof!).ToArray());

            //Assert.That(db.Keys.Count, Is.EqualTo(4));

            firstProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[2].Path.Bytes);
            lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[3].Path.Bytes);
            var result2 = snapProvider.AddAccountRange(1, rootHash, TestItem.Tree.AccountsWithPaths[2].Path, TestItem.Tree.AccountsWithPaths[2..4], firstProof!.Concat(lastProof!).ToArray());

            //Assert.That(db.Keys.Count, Is.EqualTo(6));  // we don't persist proof nodes (boundary nodes)

            firstProof = CreateProofForPath(Keccak.Zero.Bytes);
            lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[1].Path.Bytes);
            var result1 = snapProvider.AddAccountRange(1, rootHash, Keccak.Zero, TestItem.Tree.AccountsWithPaths[0..2], firstProof!.Concat(lastProof!).ToArray());

            Assert.That(result1, Is.EqualTo(AddRangeResult.OK));
            Assert.That(result2, Is.EqualTo(AddRangeResult.OK));
            Assert.That(result3, Is.EqualTo(AddRangeResult.OK));
            //Assert.That(db.Keys.Count, Is.EqualTo(10));  // we persist proof nodes (boundary nodes) via stitching
            //Assert.IsFalse(db.KeyExists(rootHash));

            IRawState rawState = progressTracker.GetSyncState();
            rawState.Finalize(200);

            var state = stateFactory.Get(rootHash);
            AssertAllAccounts(state, 6);
        }

        [Test]
        public void RecreateAccountStateFromMultipleRange_OutOfOrder()
        {
            Hash256 rootHash = _inputTree.RootHash;   // "0x8c81279168edc449089449bc0f2136fc72c9645642845755633cf259cd97988b"

            // output state
            MemDb db = new();
            IDbProvider dbProvider = new DbProvider();
            dbProvider.RegisterDb(DbNames.State, db);
            IStateFactory stateFactory = new PaprikaStateFactory();
            ProgressTracker progressTracker = new(null, dbProvider.StateDb, stateFactory, LimboLogs.Instance);
            SnapProvider snapProvider = new(progressTracker, dbProvider.StateDb, new NodeStorage(db), LimboLogs.Instance);

            byte[][] firstProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[4].Path.Bytes);
            byte[][] lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[5].Path.Bytes);
            var result3 = snapProvider.AddAccountRange(1, rootHash, TestItem.Tree.AccountsWithPaths[4].Path, TestItem.Tree.AccountsWithPaths[4..6], firstProof!.Concat(lastProof!).ToArray());

            //Assert.That(db.Keys.Count, Is.EqualTo(4));

            firstProof = CreateProofForPath(Keccak.Zero.Bytes);
            lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[1].Path.Bytes);
            var result1 = snapProvider.AddAccountRange(1, rootHash, Keccak.Zero, TestItem.Tree.AccountsWithPaths[0..2], firstProof!.Concat(lastProof!).ToArray());

            //Assert.That(db.Keys.Count, Is.EqualTo(6));  // we don't persist proof nodes (boundary nodes)

            firstProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[2].Path.Bytes);
            lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[3].Path.Bytes);
            var result2 = snapProvider.AddAccountRange(1, rootHash, TestItem.Tree.AccountsWithPaths[2].Path, TestItem.Tree.AccountsWithPaths[2..4], firstProof!.Concat(lastProof!).ToArray());

            Assert.That(result1, Is.EqualTo(AddRangeResult.OK));
            Assert.That(result2, Is.EqualTo(AddRangeResult.OK));
            Assert.That(result3, Is.EqualTo(AddRangeResult.OK));
            //Assert.That(db.Keys.Count, Is.EqualTo(10));  // we persist proof nodes (boundary nodes) via stitching
            //Assert.IsFalse(db.KeyExists(rootHash));

            IRawState rawState = progressTracker.GetSyncState();
            rawState.Finalize(200);

            var state = stateFactory.Get(rootHash);
            AssertAllAccounts(state, 6);
        }

        [Test]
        public void RecreateAccountStateFromMultipleOverlappingRange()
        {
            Hash256 rootHash = _inputTree.RootHash;   // "0x8c81279168edc449089449bc0f2136fc72c9645642845755633cf259cd97988b"

            // output state
            MemDb db = new();
            IDbProvider dbProvider = new DbProvider();
            dbProvider.RegisterDb(DbNames.State, db);
            IStateFactory stateFactory = new PaprikaStateFactory();
            ProgressTracker progressTracker = new(null, dbProvider.StateDb, stateFactory, LimboLogs.Instance);
            SnapProvider snapProvider = new(progressTracker, dbProvider.StateDb, new NodeStorage(db), LimboLogs.Instance);

            byte[][] firstProof = CreateProofForPath(Keccak.Zero.Bytes);
            byte[][] lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[2].Path.Bytes);

            var result1 = snapProvider.AddAccountRange(1, rootHash, Keccak.Zero, TestItem.Tree.AccountsWithPaths[0..3], firstProof!.Concat(lastProof!).ToArray());

            //Assert.That(db.Keys.Count, Is.EqualTo(3));

            firstProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[2].Path.Bytes);
            lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[3].Path.Bytes);

            var result2 = snapProvider.AddAccountRange(1, rootHash, TestItem.Tree.AccountsWithPaths[2].Path, TestItem.Tree.AccountsWithPaths[2..4], firstProof!.Concat(lastProof!).ToArray());

            firstProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[3].Path.Bytes);
            lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[4].Path.Bytes);

            var result3 = snapProvider.AddAccountRange(1, rootHash, TestItem.Tree.AccountsWithPaths[3].Path, TestItem.Tree.AccountsWithPaths[3..5], firstProof!.Concat(lastProof!).ToArray());

            //Assert.That(db.Keys.Count, Is.EqualTo(6));  // we don't persist proof nodes (boundary nodes)

            firstProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[4].Path.Bytes);
            lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[5].Path.Bytes);

            var result4 = snapProvider.AddAccountRange(1, rootHash, TestItem.Tree.AccountsWithPaths[4].Path, TestItem.Tree.AccountsWithPaths[4..6], firstProof!.Concat(lastProof!).ToArray());

            Assert.That(result1, Is.EqualTo(AddRangeResult.OK));
            Assert.That(result2, Is.EqualTo(AddRangeResult.OK));
            Assert.That(result3, Is.EqualTo(AddRangeResult.OK));
            Assert.That(result4, Is.EqualTo(AddRangeResult.OK));
            //Assert.That(db.Keys.Count, Is.EqualTo(10));  // we persist proof nodes (boundary nodes) via stitching
            //Assert.IsFalse(db.KeyExists(rootHash));
        }

        [Test]
        public void CorrectlyDetermineHasMoreChildren()
        {
            Hash256 rootHash = _inputTree.RootHash;   // "0x8c81279168edc449089449bc0f2136fc72c9645642845755633cf259cd97988b"

            // output state
            MemDb db = new();
            IDbProvider dbProvider = new DbProvider();
            dbProvider.RegisterDb(DbNames.State, db);
            IStateFactory stateFactory = new PaprikaStateFactory();
            ProgressTracker progressTracker = new(null, dbProvider.StateDb, stateFactory, LimboLogs.Instance);
            SnapProvider snapProvider = new(progressTracker, dbProvider.StateDb, new NodeStorage(db), LimboLogs.Instance);

            byte[][] firstProof = CreateProofForPath(Keccak.Zero.Bytes);
            byte[][] lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[1].Path.Bytes);
            byte[][] proofs = firstProof.Concat(lastProof).ToArray();

            StateTree newTree = new(new TrieStore(new MemDb(), LimboLogs.Instance), LimboLogs.Instance);

            PathWithAccount[] receiptAccounts = TestItem.Tree.AccountsWithPaths[0..2];

            bool HasMoreChildren(ValueHash256 limitHash)
            {
                (AddRangeResult _, bool moreChildrenToRight, IList<PathWithAccount> _, IList<ValueHash256> _) =
                    SnapProviderHelper.AddAccountRange(progressTracker.GetSyncState(), 1, rootHash, Keccak.Zero, limitHash.ToCommitment(), receiptAccounts, proofs);
                return moreChildrenToRight;
            }

            HasMoreChildren(TestItem.Tree.AccountsWithPaths[1].Path).Should().BeFalse();
            HasMoreChildren(TestItem.Tree.AccountsWithPaths[2].Path).Should().BeFalse();
            HasMoreChildren(TestItem.Tree.AccountsWithPaths[3].Path).Should().BeTrue();
            HasMoreChildren(TestItem.Tree.AccountsWithPaths[4].Path).Should().BeTrue();

            UInt256 between2and3 = new UInt256(TestItem.Tree.AccountsWithPaths[1].Path.Bytes, true);
            between2and3 += 5;

            HasMoreChildren(new Hash256(between2and3.ToBigEndian())).Should().BeFalse();

            between2and3 = new UInt256(TestItem.Tree.AccountsWithPaths[2].Path.Bytes, true);
            between2and3 -= 1;

            HasMoreChildren(new Hash256(between2and3.ToBigEndian())).Should().BeFalse();
        }

        [Test]
        public void CorrectlyDetermineMaxKeccakExist()
        {
            StateTree tree = new StateTree(new TrieStore(new MemDb(), LimboLogs.Instance), LimboLogs.Instance);

            PathWithAccount ac1 = new PathWithAccount(Keccak.Zero, Build.An.Account.WithBalance(1).TestObject);
            PathWithAccount ac2 = new PathWithAccount(Keccak.Compute("anything"), Build.An.Account.WithBalance(2).TestObject);
            PathWithAccount ac3 = new PathWithAccount(Keccak.MaxValue, Build.An.Account.WithBalance(2).TestObject);

            tree.Set(ac1.Path, ac1.Account);
            tree.Set(ac2.Path, ac2.Account);
            tree.Set(ac3.Path, ac3.Account);
            tree.Commit(0);

            Hash256 rootHash = tree.RootHash;   // "0x8c81279168edc449089449bc0f2136fc72c9645642845755633cf259cd97988b"

            // output state
            MemDb db = new();
            IDbProvider dbProvider = new DbProvider();
            dbProvider.RegisterDb(DbNames.State, db);
            IStateFactory stateFactory = new PaprikaStateFactory();
            ProgressTracker progressTracker = new(null, dbProvider.StateDb, stateFactory, LimboLogs.Instance);
            SnapProvider snapProvider = new(progressTracker, dbProvider.StateDb, new NodeStorage(db), LimboLogs.Instance);

            byte[][] firstProof = CreateProofForPath(ac1.Path.Bytes, tree);
            byte[][] lastProof = CreateProofForPath(ac2.Path.Bytes, tree);
            byte[][] proofs = firstProof.Concat(lastProof).ToArray();

            StateTree newTree = new(new TrieStore(new MemDb(), LimboLogs.Instance), LimboLogs.Instance);

            PathWithAccount[] receiptAccounts = { ac1, ac2 };

            //bool HasMoreChildren(ValueHash256 limitHash)
            //{
            //    (AddRangeResult _, bool moreChildrenToRight, IList<PathWithAccount> _, IList<ValueHash256> _) =
            //        SnapProviderHelper.AddAccountRange(newTree, 0, rootHash, Keccak.Zero, limitHash.ToCommitment(), receiptAccounts, proofs);
            //    return moreChildrenToRight;
            //}

            //HasMoreChildren(ac1.Path).Should().BeFalse();
            //HasMoreChildren(ac2.Path).Should().BeFalse();

            //UInt256 between2and3 = new UInt256(ac2.Path.Bytes, true);
            //between2and3 += 5;

            //HasMoreChildren(new Hash256(between2and3.ToBigEndian())).Should().BeFalse();

            //// The special case
            //HasMoreChildren(Keccak.MaxValue).Should().BeTrue();
        }

        [Test]
        public void MissingAccountFromRange()
        {
            Hash256 rootHash = _inputTree.RootHash;   // "0x8c81279168edc449089449bc0f2136fc72c9645642845755633cf259cd97988b"

            // output state
            MemDb db = new();
            IDbProvider dbProvider = new DbProvider();
            dbProvider.RegisterDb(DbNames.State, db);
            IStateFactory stateFactory = new PaprikaStateFactory();
            ProgressTracker progressTracker = new(null, dbProvider.StateDb, stateFactory, LimboLogs.Instance);
            SnapProvider snapProvider = new(progressTracker, dbProvider.StateDb, new NodeStorage(db), LimboLogs.Instance);

            byte[][] firstProof = CreateProofForPath(Keccak.Zero.Bytes);
            byte[][] lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[1].Path.Bytes);

            var result1 = snapProvider.AddAccountRange(1, rootHash, Keccak.Zero, TestItem.Tree.AccountsWithPaths[0..2], firstProof!.Concat(lastProof!).ToArray());

            //Assert.That(db.Keys.Count, Is.EqualTo(2));

            firstProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[2].Path.Bytes);
            lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[3].Path.Bytes);

            // missing TestItem.Tree.AccountsWithHashes[2]
            var result2 = snapProvider.AddAccountRange(1, rootHash, TestItem.Tree.AccountsWithPaths[2].Path, TestItem.Tree.AccountsWithPaths[3..4], firstProof!.Concat(lastProof!).ToArray());

            //Assert.That(db.Keys.Count, Is.EqualTo(2));

            firstProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[4].Path.Bytes);
            lastProof = CreateProofForPath(TestItem.Tree.AccountsWithPaths[5].Path.Bytes);

            var result3 = snapProvider.AddAccountRange(1, rootHash, TestItem.Tree.AccountsWithPaths[4].Path, TestItem.Tree.AccountsWithPaths[4..6], firstProof!.Concat(lastProof!).ToArray());

            Assert.That(result1, Is.EqualTo(AddRangeResult.OK));
            Assert.That(result2, Is.EqualTo(AddRangeResult.DifferentRootHash));
            Assert.That(result3, Is.EqualTo(AddRangeResult.OK));
            //Assert.That(db.Keys.Count, Is.EqualTo(6));
            //Assert.IsFalse(db.KeyExists(rootHash));
        }

        private SnapProvider CreateSnapProvider(ProgressTracker progressTracker, IDbProvider dbProvider)
        {
            try
            {
                IDb _ = dbProvider.CodeDb;
            }
            catch (ArgumentException)
            {
                dbProvider.RegisterDb(DbNames.Code, new MemDb());
            }
            return new(progressTracker, dbProvider.CodeDb, new NodeStorage(dbProvider.StateDb), LimboLogs.Instance);
        }

        private static void AssertAllAccounts(IReadOnlyState state, int upTo)
        {
            foreach (var item in TestItem.Tree.AccountsWithPaths[0..upTo])
            {
                Account a = state.Get(item.Path);
                Assert.That((item.Account.IsTotallyEmpty && a is null) || (!item.Account.IsTotallyEmpty && a is not null), Is.True);
                Assert.That(a?.Balance ?? 0, Is.EqualTo(item.Account.Balance));
            }
        }
    }
}
