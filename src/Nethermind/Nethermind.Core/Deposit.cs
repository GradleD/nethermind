// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Core.Extensions;
using System.Text;

namespace Nethermind.Core;

/// <summary>
/// Represents a Deposit that has been validated at the consensus layer.
/// </summary>
public class Deposit
{
    public byte[]? PubKey { get; set; }
    public byte[]? WithdrawalCredentials { get; set; }
    public ulong Amount { get; set; }
    public byte[]? Signature { get; set; }
    public ulong Index { get; set; }
    public override string ToString() => ToString(string.Empty);

    public string ToString(string indentation) => new StringBuilder($"{indentation}{nameof(Deposit)} {{")
        .Append($"{nameof(Index)}: {Index}, ")
        .Append($"{nameof(WithdrawalCredentials)}: {WithdrawalCredentials?.ToHexString()}, ")
        .Append($"{nameof(Amount)}: {Amount}, ")
        .Append($"{nameof(Signature)}: {Signature?.ToHexString()}, ")
        .Append($"{nameof(PubKey)}: {PubKey?.ToHexString()}}}")
        .ToString();
}
