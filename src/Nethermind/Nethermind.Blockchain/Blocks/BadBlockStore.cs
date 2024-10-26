// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Db;
using Nethermind.Serialization.Rlp;

namespace Nethermind.Blockchain.Blocks;

public class BadBlockStore(IDb blockDb, long? maxSize = null) : IBadBlockStore
{
    private readonly BlockDecoder _blockDecoder = new();

    public void Insert(Block block, WriteFlags writeFlags = WriteFlags.None)
    {
        if (block.Hash is null)
        {
            throw new InvalidOperationException("An attempt to store a block with a null hash.");
        }

        // if we carry Rlp from the network message all the way here we could avoid encoding back to RLP here
        // Although cpu is the main bottleneck since NettyRlpStream uses pooled memory which avoid unnecessary allocations..
        using NettyRlpStream newRlp = _blockDecoder.EncodeToNewNettyStream(block);

        blockDb.Set(block.Number, block.Hash, newRlp.AsSpan(), writeFlags);

        if (maxSize is not null)
        {
            TruncateToMaxSize();
        }
    }

    public IEnumerable<Block> GetAll()
    {
        return blockDb.GetAllValues(true).Select(bytes => _blockDecoder.Decode(ByteArrayExtensions.AsRlpStream((byte[]?)bytes)));
    }

    private void TruncateToMaxSize()
    {
        int toDelete = (int)(blockDb.GatherMetric().Size - maxSize!);
        if (toDelete > 0)
        {
            foreach (var blockToDelete in GetAll().Take(toDelete))
            {
                Delete(blockToDelete.Number, blockToDelete.Hash);
            }
        }
    }

    private void Delete(long blockNumber, Hash256 blockHash)
    {
        blockDb.Delete(blockNumber, blockHash);
        blockDb.Remove(blockHash.Bytes);
    }
}
