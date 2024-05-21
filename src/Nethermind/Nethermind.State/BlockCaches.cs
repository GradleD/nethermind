// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Nethermind.Core;

namespace Nethermind.State;

public class BlockCaches
{
    public NonBlocking.ConcurrentDictionary<StorageCell, byte[]> StorageCache { get; } = new(Environment.ProcessorCount, 4096);
    public NonBlocking.ConcurrentDictionary<AddressAsKey, Account> StateCache { get; } = new(Environment.ProcessorCount, 4096);
}

