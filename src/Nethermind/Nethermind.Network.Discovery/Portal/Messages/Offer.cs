// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Serialization.Ssz;

namespace Nethermind.Network.Discovery.Portal.Messages;

[SszSerializable]
public class Offer
{
    [SszList(64000)] // TODO: Check limit
    public byte[][] ContentKeys { get; set; } = Array.Empty<byte[]>();
}
