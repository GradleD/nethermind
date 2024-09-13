// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Db;
using Nethermind.Logging;
using Nethermind.Trie;
using Nethermind.Trie.Pruning;

namespace Nethermind.State;

public class ReadOnlyWorldStateProvider : IWorldStateProvider
{
    private readonly IStateReader _stateReader;
    public ITrieStore TrieStore { get; set; }

    public ReadOnlyWorldStateProvider(
        IDbProvider dbProvider,
        IReadOnlyTrieStore readOnlyTrieStore,
        ILogManager logManager)
    {
        IReadOnlyDbProvider readOnlyDbProvider = dbProvider.AsReadOnly(false);
        IDb codeDb = readOnlyDbProvider.GetDb<IDb>(DbNames.Code).AsReadOnly(true);

        _stateReader = new StateReader(readOnlyTrieStore, codeDb, logManager);
        TrieStore = readOnlyTrieStore;
    }

    public virtual IWorldState GetGlobalWorldState(BlockHeader header) => throw new InvalidOperationException("world state is not supported");

    public virtual IStateReader GetGlobalStateReader()
    {
        return _stateReader;
    }

    public virtual IWorldState GetWorldState() => throw new InvalidOperationException("world state is not supported");

    public bool HasStateForRoot(Hash256 stateRoot)
    {
        return _stateReader.HasStateForRoot(stateRoot);
    }

    public void RunTreeVisitor(ITreeVisitor treeVisitor, Hash256 stateRoot, VisitingOptions? visitingOptions = null)
    {
        _stateReader.RunTreeVisitor(treeVisitor, stateRoot, visitingOptions);
    }
}