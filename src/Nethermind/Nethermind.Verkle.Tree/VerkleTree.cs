// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Diagnostics;
using Nethermind.Core.Collections;
using Nethermind.Core.Extensions;
using Nethermind.Db;
using Nethermind.Verkle.Curve;
using Nethermind.Verkle.Fields.FrEElement;
using Nethermind.Verkle.Tree.Nodes;
using Nethermind.Verkle.Tree.Proofs;
using Nethermind.Verkle.Tree.Sync;
using Nethermind.Verkle.Tree.Utils;
using Nethermind.Verkle.Tree.VerkleDb;
using Committer = Nethermind.Verkle.Tree.Utils.Committer;
using LeafUpdateDelta = Nethermind.Verkle.Tree.Utils.LeafUpdateDelta;

namespace Nethermind.Verkle.Tree;

public partial class VerkleTree: IVerkleTree
{
    private static byte[] RootKey = Array.Empty<byte>();
    private VerkleMemoryDb _treeCache;
    public readonly IVerkleStore _verkleStateStore;

    private Pedersen _stateRoot;

    public Pedersen StateRoot
    {
        get
        {
            if (_isDirty) throw new InvalidOperationException("trying to get root hash of not committed tree");
            return _stateRoot;
        }
        set
        {
            MoveToStateRoot(value);
        }
    }

    private bool _isDirty;

    private readonly SpanDictionary<byte, LeafUpdateDelta> _leafUpdateCache;

    public VerkleTree(IDbProvider dbProvider)
    {
        _treeCache = new VerkleMemoryDb();
        _verkleStateStore = new VerkleStateStore(dbProvider);
        _leafUpdateCache = new SpanDictionary<byte, LeafUpdateDelta>(Bytes.SpanEqualityComparer);
        _stateRoot = _verkleStateStore.RootHash;
        ProofBranchPolynomialCache = new Dictionary<byte[], FrE[]>(Bytes.EqualityComparer);
        ProofStemPolynomialCache = new Dictionary<byte[], SuffixPoly>(Bytes.EqualityComparer);
    }

    protected VerkleTree(IVerkleStore verkleStateStore)
    {
        _treeCache = new VerkleMemoryDb();
        _verkleStateStore = verkleStateStore;
        _leafUpdateCache = new SpanDictionary<byte, LeafUpdateDelta>(Bytes.SpanEqualityComparer);
        _stateRoot = _verkleStateStore.RootHash;
        ProofBranchPolynomialCache = new Dictionary<byte[], FrE[]>(Bytes.EqualityComparer);
        ProofStemPolynomialCache = new Dictionary<byte[], SuffixPoly>(Bytes.EqualityComparer);
    }

    public bool MoveToStateRoot(Pedersen stateRoot)
    {
        try
        {
            if (GetStateRoot().Equals(stateRoot)) return true;
            _verkleStateStore.MoveToStateRoot(stateRoot);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private Pedersen GetStateRoot()
    {
        byte[] stateRoot = GetInternalNode(Array.Empty<byte>())?.InternalCommitment.Point.ToBytes().ToArray() ??
                           throw new InvalidOperationException();
        return new Pedersen(stateRoot);
    }

    public byte[]? Get(Pedersen key)
    {
        return _treeCache.GetLeaf(key.Bytes, out byte[]? value)
            ? value
            : _verkleStateStore.GetLeaf(key.Bytes);
    }

    public void Set(Pedersen key, byte[]? value)
    {
        _treeCache.SetLeaf(key.BytesAsSpan, value);
    }

    public void Insert(Pedersen key, byte[] value)
    {
        _isDirty = true;
#if DEBUG
        if (value.Length != 32) throw new ArgumentException("value must be 32 bytes", nameof(value));
#endif
        ReadOnlySpan<byte> stem = key.StemAsSpan;
        bool present = _leafUpdateCache.TryGetValue(stem, out LeafUpdateDelta leafUpdateDelta);
        if(!present) leafUpdateDelta = new LeafUpdateDelta();
        leafUpdateDelta.UpdateDelta(UpdateLeafAndGetDelta(key, value), key.SuffixByte);
        _leafUpdateCache[stem] = leafUpdateDelta;
    }

    public void InsertStemBatch(ReadOnlySpan<byte> stem, IEnumerable<(byte, byte[])> leafIndexValueMap)
    {
        _isDirty = true;
#if DEBUG
        if (stem.Length != 31) throw new ArgumentException("stem must be 31 bytes", nameof(stem));
        Span<byte> keyD = new byte[32];
        IEnumerable<(byte, byte[])> indexValueMap = leafIndexValueMap as (byte, byte[])[] ?? leafIndexValueMap.ToArray();
        foreach ((byte, byte[]) keyVal in indexValueMap)
        {
            stem.CopyTo(keyD);
            keyD[31] = keyVal.Item1;
            Console.WriteLine("KA: " + EnumerableExtensions.ToString(keyD.ToArray()));
            Console.WriteLine("V: " + EnumerableExtensions.ToString(keyVal.Item2));
        }
#endif
        bool present = _leafUpdateCache.TryGetValue(stem, out LeafUpdateDelta leafUpdateDelta);
        if(!present) leafUpdateDelta = new LeafUpdateDelta();

        Span<byte> key = new byte[32];
        stem.CopyTo(key);
        foreach ((byte index, byte[] value) in leafIndexValueMap)
        {
            key[31] = index;
            leafUpdateDelta.UpdateDelta(UpdateLeafAndGetDelta(new Pedersen(key.ToArray()), value), key[31]);
        }

        _leafUpdateCache[stem.ToArray()] = leafUpdateDelta;
    }

    public void InsertStemBatch(ReadOnlySpan<byte> stem, IEnumerable<LeafInSubTree> leafIndexValueMap)
    {
        _isDirty = true;
#if DEBUG
        if (stem.Length != 31) throw new ArgumentException("stem must be 31 bytes", nameof(stem));
        Span<byte> keyD = new byte[32];
        IEnumerable<(byte, byte[])> indexValueMap = leafIndexValueMap as (byte, byte[])[] ?? leafIndexValueMap.ToArray();
        foreach ((byte, byte[]) keyVal in indexValueMap)
        {
            stem.CopyTo(keyD);
            keyD[31] = keyVal.Item1;
            Console.WriteLine("KA: " + EnumerableExtensions.ToString(keyD.ToArray()));
            Console.WriteLine("V: " + EnumerableExtensions.ToString(keyVal.Item2));
        }
#endif
        bool present = _leafUpdateCache.TryGetValue(stem, out LeafUpdateDelta leafUpdateDelta);
        if(!present) leafUpdateDelta = new LeafUpdateDelta();

        Span<byte> key = new byte[32];
        stem.CopyTo(key);
        foreach (LeafInSubTree leaf in leafIndexValueMap)
        {
            key[31] = leaf.SuffixByte;
            leafUpdateDelta.UpdateDelta(UpdateLeafAndGetDelta(new Pedersen(key.ToArray()), leaf.Leaf), key[31]);
        }

        _leafUpdateCache[stem.ToArray()] = leafUpdateDelta;
    }

    private Banderwagon UpdateLeafAndGetDelta(Pedersen key, byte[] value)
    {
        byte[]? oldValue = Get(key);
        Banderwagon leafDeltaCommitment = GetLeafDelta(oldValue, value, key.SuffixByte);
        Set(key, value);
        return leafDeltaCommitment;
    }

    private static Banderwagon GetLeafDelta(byte[]? oldValue, byte[] newValue, byte index)
    {

#if DEBUG
        if (oldValue is not null && oldValue.Length != 32) throw new ArgumentException("oldValue must be null or 32 bytes", nameof(oldValue));
        if (newValue.Length != 32) throw new ArgumentException("newValue must be 32 bytes", nameof(newValue));
#endif

        (FrE newValLow, FrE newValHigh) = VerkleUtils.BreakValueInLowHigh(newValue);
        (FrE oldValLow, FrE oldValHigh) = VerkleUtils.BreakValueInLowHigh(oldValue);

        int posMod128 = index % 128;
        int lowIndex = 2 * posMod128;
        int highIndex = lowIndex + 1;

        Banderwagon deltaLow = Committer.ScalarMul(newValLow - oldValLow, lowIndex);
        Banderwagon deltaHigh = Committer.ScalarMul(newValHigh - oldValHigh, highIndex);
        return deltaLow + deltaHigh;
    }

    public static Banderwagon GetLeafDelta(byte[] newValue, byte index)
    {

#if DEBUG
        if (newValue.Length != 32) throw new ArgumentException("newValue must be 32 bytes", nameof(newValue));
#endif

        (FrE newValLow, FrE newValHigh) = VerkleUtils.BreakValueInLowHigh(newValue);

        int posMod128 = index % 128;
        int lowIndex = 2 * posMod128;
        int highIndex = lowIndex + 1;

        Banderwagon deltaLow = Committer.ScalarMul(newValLow, lowIndex);
        Banderwagon deltaHigh = Committer.ScalarMul(newValHigh, highIndex);
        return deltaLow + deltaHigh;
    }

    public void Commit()
    {
        foreach (KeyValuePair<byte[], LeafUpdateDelta> leafDelta in _leafUpdateCache)
        {
            UpdateTreeCommitments(leafDelta.Key, leafDelta.Value);
        }
        _leafUpdateCache.Clear();
        _isDirty = false;
        _stateRoot = GetStateRoot();
    }

    public void CommitTree(long blockNumber)
    {
        _verkleStateStore.Flush(blockNumber, _treeCache);
        _treeCache = new VerkleMemoryDb();
        _stateRoot = _verkleStateStore.RootHash;
    }

    private void UpdateTreeCommitments(Span<byte> stem, LeafUpdateDelta leafUpdateDelta)
    {
        // calculate this by update the leafs and calculating the delta - simple enough
        TraverseContext context = new TraverseContext(stem, leafUpdateDelta);
        Banderwagon rootDelta = TraverseBranch(context);
        UpdateRootNode(rootDelta);
    }

    private void UpdateRootNode(Banderwagon rootDelta)
    {
        InternalNode root = GetInternalNode(RootKey) ?? throw new InvalidOperationException("root should be present");
        InternalNode newRoot = root.Clone();
        newRoot.InternalCommitment.AddPoint(rootDelta);
        SetInternalNode(RootKey, newRoot);
    }

    private InternalNode? GetInternalNode(byte[] nodeKey)
    {
        return _treeCache.GetInternalNode(nodeKey, out InternalNode? value)
            ? value
            : _verkleStateStore.GetInternalNode(nodeKey);
    }

    private void SetInternalNode(byte[] nodeKey, InternalNode node)
    {
        _treeCache.SetInternalNode(nodeKey, node);
    }

    public Banderwagon TraverseBranch(TraverseContext traverseContext)
    {
        byte childIndex = traverseContext.Stem[traverseContext.CurrentIndex];
        byte[] absolutePath = traverseContext.Stem[..(traverseContext.CurrentIndex + 1)].ToArray();

        InternalNode? child = GetInternalNode(absolutePath);
        if (child is null)
        {
            // 1. create new suffix node
            // 2. update the C1 or C2 - we already know the leafDelta - traverseContext.LeafUpdateDelta
            // 3. update ExtensionCommitment
            // 4. get the delta for commitment - ExtensionCommitment - 0;
            InternalNode stem = new InternalNode(VerkleNodeType.StemNode, traverseContext.Stem.ToArray());
            FrE deltaFr = stem.UpdateCommitment(traverseContext.LeafUpdateDelta);
            FrE deltaHash = deltaFr + stem.InitCommitmentHash!.Value;

            // 1. Add internal.stem node
            // 2. return delta from ExtensionCommitment
            SetInternalNode(absolutePath, stem);
            return Committer.ScalarMul(deltaHash, childIndex);
        }

        if (child.IsBranchNode)
        {
            traverseContext.CurrentIndex += 1;
            Banderwagon branchDeltaHash = TraverseBranch(traverseContext);
            traverseContext.CurrentIndex -= 1;
            FrE deltaHash = child.UpdateCommitment(branchDeltaHash);
            SetInternalNode(absolutePath, child);
            return Committer.ScalarMul(deltaHash, childIndex);
        }

        traverseContext.CurrentIndex += 1;
        (Banderwagon stemDeltaHash, bool changeStemToBranch) = TraverseStem(child, traverseContext);
        traverseContext.CurrentIndex -= 1;
        if (changeStemToBranch)
        {
            InternalNode newChild = new InternalNode(VerkleNodeType.BranchNode);
            newChild.InternalCommitment.AddPoint(child.InternalCommitment.Point);
            // since this is a new child, this would be just the parentDeltaHash.PointToField
            // now since there was a node before and that value is deleted - we need to subtract
            // that from the delta as well
            FrE deltaHash = newChild.UpdateCommitment(stemDeltaHash);
            SetInternalNode(absolutePath, newChild);
            return Committer.ScalarMul(deltaHash, childIndex);
        }
        // in case of stem, no need to update the child commitment - because this commitment is the suffix commitment
        // pass on the update to upper level
        return stemDeltaHash;
    }

    private (Banderwagon, bool) TraverseStem(InternalNode node, TraverseContext traverseContext)
    {
        Debug.Assert(node.IsStem);

        (List<byte> sharedPath, byte? pathDiffIndexOld, byte? pathDiffIndexNew) =
            VerkleUtils.GetPathDifference(node.Stem!, traverseContext.Stem.ToArray());

        if (sharedPath.Count != 31)
        {
            int relativePathLength = sharedPath.Count - traverseContext.CurrentIndex;
            // byte[] relativeSharedPath = sharedPath.ToArray()[traverseContext.CurrentIndex..].ToArray();
            byte oldLeafIndex = pathDiffIndexOld ?? throw new ArgumentException();
            byte newLeafIndex = pathDiffIndexNew ?? throw new ArgumentException();
            // node share a path but not the complete stem.

            // the internal node will be denoted by their sharedPath
            // 1. create SuffixNode for the traverseContext.Key - get the delta of the commitment
            // 2. set this suffix as child node of the BranchNode - get the commitment point
            // 3. set the existing suffix as the child - get the commitment point
            // 4. update the internal node with the two commitment points
            InternalNode newStem = new InternalNode(VerkleNodeType.StemNode, traverseContext.Stem.ToArray());
            FrE deltaFrNewStem = newStem.UpdateCommitment(traverseContext.LeafUpdateDelta);
            FrE deltaHashNewStem = deltaFrNewStem + newStem.InitCommitmentHash!.Value;

            // creating the stem node for the new suffix node
            byte[] stemKey = new byte[sharedPath.Count + 1];
            sharedPath.CopyTo(stemKey);
            stemKey[^1] = newLeafIndex;
            SetInternalNode(stemKey, newStem);
            Banderwagon newSuffixCommitmentDelta = Committer.ScalarMul(deltaHashNewStem, newLeafIndex);

            stemKey = new byte[sharedPath.Count + 1];
            sharedPath.CopyTo(stemKey);
            stemKey[^1] = oldLeafIndex;
            SetInternalNode(stemKey, node);

            Banderwagon oldSuffixCommitmentDelta =
                Committer.ScalarMul(node.InternalCommitment.PointAsField, oldLeafIndex);

            Banderwagon deltaCommitment = oldSuffixCommitmentDelta + newSuffixCommitmentDelta;

            Banderwagon internalCommitment = FillSpaceWithBranchNodes(sharedPath.ToArray(), relativePathLength, deltaCommitment);

            return (internalCommitment - node.InternalCommitment.Point, true);
        }

        InternalNode updatedStemNode = node.Clone();
        FrE deltaFr = updatedStemNode.UpdateCommitment(traverseContext.LeafUpdateDelta);
        SetInternalNode(traverseContext.Stem[..traverseContext.CurrentIndex].ToArray(), updatedStemNode);
        return (Committer.ScalarMul(deltaFr, traverseContext.Stem[traverseContext.CurrentIndex - 1]), false);
    }

    private Banderwagon FillSpaceWithBranchNodes(byte[] path, int length, Banderwagon deltaPoint)
    {
        for (int i = 0; i < length; i++)
        {
            InternalNode newInternalNode = new(VerkleNodeType.BranchNode);
            FrE upwardsDelta = newInternalNode.UpdateCommitment(deltaPoint);
            SetInternalNode(path[..^i], newInternalNode);
            deltaPoint = Committer.ScalarMul(upwardsDelta, path[path.Length - i - 1]);
        }

        return deltaPoint;
    }

    public void Reset()
    {
        _leafUpdateCache.Clear();
        ProofBranchPolynomialCache.Clear();
        ProofStemPolynomialCache.Clear();
        _treeCache = new VerkleMemoryDb();
        _verkleStateStore.Reset();
    }

    public ref struct TraverseContext
    {
        public LeafUpdateDelta LeafUpdateDelta { get; }
        public Span<byte> Stem { get; }
        public int CurrentIndex { get; set; }

        public TraverseContext(Span<byte> stem, LeafUpdateDelta delta)
        {
            Stem = stem;
            CurrentIndex = 0;
            LeafUpdateDelta = delta;
        }
    }
}
