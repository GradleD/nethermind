// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only
using System;
using Nethermind.Core.Extensions;

namespace Nethermind.Core.ConsensusRequests;

/// <summary>
/// Represents a Deposit that has been validated at the consensus layer.
/// </summary>
public class ConsolidationRequest : ConsensusRequest
{
    public ConsolidationRequest()
    {
        Type = ConsensusRequestsType.ConsolidationRequest;
    }
    public Address? SourceAddress
    {
        get { return SourceAddressField; }
        set { SourceAddressField = value; }
    }

    public Memory<byte>? SourcePubkey
    {
        get { return PubKeyField; }
        set { PubKeyField = value; }
    }

    public byte[]? TargetPubkey
    {
        get { return WithdrawalCredentialsField; }
        set { WithdrawalCredentialsField = value; }
    }

    public override string ToString() => ToString(string.Empty);

    public string ToString(string indentation) => @$"{indentation}{nameof(ConsolidationRequest)}
            {{ {nameof(SourceAddress)}: {SourceAddress},
            {nameof(SourcePubkey)}: {SourcePubkey?.Span.ToHexString()},
            {nameof(TargetPubkey)}: {TargetPubkey?.ToHexString()},
            }}";


}
