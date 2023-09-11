// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Int256;

namespace Nethermind.Facade.Proxy.Models.MultiCall;

public class MultiCallBlockResult
{
    public ulong Number { get; set; }
    public Keccak Hash { get; set; }
    public ulong Timestamp { get; set; }
    public ulong GasLimit { get; set; }
    public ulong GasUsed { get; set; }
    public Address FeeRecipient { get; set; }
    public UInt256 BaseFeePerGas { get; set; }
    public MultiCallCallResult[] Calls { get; set; }
    public UInt256 PrevRandao { get; set; }

}
