// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Facade.Eth.RpcTransaction;
using Nethermind.Serialization.Json;
using System.Text.Json.Serialization;

namespace Nethermind.Taiko.Rpc;

public sealed class PreBuiltTxList(LegacyTransactionForRpc[] transactions, ulong estimatedGasUsed, ulong bytesLength)
{
    public LegacyTransactionForRpc[] TxList { get; } = transactions;

    [JsonConverter(typeof(LongRawJsonConverter))]
    public ulong EstimatedGasUsed { get; } = estimatedGasUsed;

    [JsonConverter(typeof(LongRawJsonConverter))]
    public ulong BytesLength { get; } = bytesLength;
}
