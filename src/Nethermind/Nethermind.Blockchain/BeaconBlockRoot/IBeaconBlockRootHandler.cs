// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.State;

namespace Nethermind.Blockchain.BeaconBlockRoot;
public interface IBeaconBlockRootHandler
{
    void StoreBeaconRoot(Block block, IReleaseSpec spec, IWorldState worldState);
}
