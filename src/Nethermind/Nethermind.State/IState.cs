// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Int256;
using Nethermind.Trie.Pruning;
using EvmWord = System.Runtime.Intrinsics.Vector256<byte>;

namespace Nethermind.State;

public interface IState : IReadOnlyState
{
    void Set(Address address, in AccountStruct account, bool isNewHint = false);

    void SetStorage(in StorageCell cell, EvmWord value);

    /// <summary>
    /// Informs the state about the potential of this sell being set.
    /// Might be used for prefetching purposes of the commitment.
    /// </summary>
    void StorageMightBeSet(in StorageCell cell);

    /// <summary>
    /// Commits the changes.
    /// </summary>
    void Commit(long blockNumber);

    /// <summary>
    /// Resets all the changes.
    /// </summary>
    void Reset();
}

public interface IReadOnlyState : IDisposable
{
    bool TryGet(Address address, out AccountStruct account);

    /// <summary>
    /// Gets storage by the cell.
    /// </summary>
    EvmWord GetStorageAt(in StorageCell cell);

    /// <summary>
    /// Gets storage by the index that has already been hashed.
    /// </summary>
    EvmWord GetStorageAt(Address address, in ValueHash256 hash);

    Hash256 StateRoot { get; }
}

/// <summary>
/// The factory allowing to get a state at the given keccak.
/// </summary>
public interface IStateFactory : IAsyncDisposable
{
    IState Get(Hash256 stateRoot);

    IReadOnlyState GetReadOnly(Hash256? stateRoot);

    event EventHandler<ReorgBoundaryReached>? ReorgBoundaryReached;

    bool HasRoot(Hash256 stateRoot);

    public bool TryGet(Hash256 stateRoot, Address address, out AccountStruct account);

    public EvmWord GetStorage(Hash256 stateRoot, in Address address, in UInt256 index);
}

public interface IStateOwner
{
    IState State { get; }
}