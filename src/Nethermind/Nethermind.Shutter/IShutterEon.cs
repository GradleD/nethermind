// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Crypto;
using Nethermind.Core;

namespace Nethermind.Shutter;

public interface IShutterEon
{
    public Info? GetCurrentEonInfo();

    public void Update(BlockHeader header);

    public readonly struct Info
    {
        public ulong Eon { get; init; }
        public Bls.P2 Key { get; init; }
        public ulong Threshold { get; init; }
        public Address[] Addresses { get; init; }
    }
}