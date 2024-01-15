// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using Nethermind.Core;
using Nethermind.Db;
using Nethermind.Verkle.Tree.Serializers;
using Nethermind.Verkle.Tree.TreeNodes;

namespace Nethermind.Verkle.Tree.VerkleDb;

public class VerkleKeyValueDb : IVerkleDb, IVerkleKeyValueDb
{
    public VerkleKeyValueDb(IDbProvider dbProvider)
    {
        LeafDb = dbProvider.LeafDb;
        InternalNodeDb = dbProvider.InternalNodesDb;
    }

    public VerkleKeyValueDb(IDb internalNodeDb, IDb leafDb)
    {
        LeafDb = leafDb;
        InternalNodeDb = internalNodeDb;
    }

    public bool GetLeaf(ReadOnlySpan<byte> key, out byte[]? value)
    {
        value = GetLeaf(key);
        return value is not null;
    }

    public bool GetInternalNode(ReadOnlySpan<byte> key, out InternalNode? value)
    {
        value = GetInternalNode(key);
        return value is not null;
    }

    public void SetLeaf(ReadOnlySpan<byte> leafKey, byte[] leafValue)
    {
        LeafDb[leafKey] = leafValue;
    }

    public void SetInternalNode(ReadOnlySpan<byte> internalNodeKey, InternalNode internalNodeValue)
    {
        SetInternalNode(internalNodeKey, internalNodeValue, InternalNodeDb);
    }

    public void RemoveLeaf(ReadOnlySpan<byte> leafKey)
    {
        LeafDb.Remove(leafKey);
    }

    public void RemoveInternalNode(ReadOnlySpan<byte> internalNodeKey)
    {
        InternalNodeDb.Remove(internalNodeKey);
    }


    public void BatchLeafInsert(IEnumerable<KeyValuePair<byte[], byte[]?>> keyLeaf)
    {
        using IWriteBatch batch = LeafDb.StartWriteBatch();
        foreach ((var key, var value) in keyLeaf) batch[key] = value;
    }

    public void BatchInternalNodeInsert(IEnumerable<KeyValuePair<byte[], InternalNode?>> internalNode)
    {
        using IWriteBatch batch = InternalNodeDb.StartWriteBatch();
        foreach ((var key, InternalNode? value) in internalNode) SetInternalNode(key, value, batch);
    }

    public IDb LeafDb { get; }
    public IDb InternalNodeDb { get; }

    public byte[]? GetLeaf(ReadOnlySpan<byte> key)
    {
        return LeafDb.Get(key);
    }

    public InternalNode? GetInternalNode(ReadOnlySpan<byte> key)
    {
        var value = InternalNodeDb[key];
        return value is null ? null : InternalNodeSerializer.Instance.Decode(value);
    }

    private static void SetInternalNode(ReadOnlySpan<byte> internalNodeKey, InternalNode? internalNodeValue,
        IWriteOnlyKeyValueStore db)
    {
        if (internalNodeValue != null)
            db[internalNodeKey] = InternalNodeSerializer.Instance.Encode(internalNodeValue).Bytes;
    }
}
