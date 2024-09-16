// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Int256;

namespace Nethermind.Facade.Eth.RpcTransaction;

/// <summary>
/// Base class for all output Nethermind RPC Transactions.
/// Several fields are non-spec compliant so are marked as optional
/// </summary>
/// <remarks>
/// Input:
/// <para>JSON -> <see cref="RpcGenericTransaction"></see> (derived by <c>System.Text.JSON</c>)</para>
/// <para><see cref="RpcGenericTransaction"/> -> <see cref="Transaction"/> (<see cref="RpcGenericTransaction.Converter"/> with a registry of [<see cref="TxType"/> => <see cref="IToTransaction{T}"/>)</para>
/// Output:
/// <para><see cref="Transaction"/> -> <see cref="RpcNethermindTransaction"/> (<see cref="TransactionConverter"/> with a registry of [<see cref="TxType"/> => <see cref="IFromTransaction{T}"/>)</para>
/// <para><see cref="RpcNethermindTransaction"/> -> JSON (derived by <c>System.Text.JSON</c>.)</para>
/// </remarks>
public abstract class RpcNethermindTransaction
{
    public TxType Type { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public Hash256? Hash { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long? TransactionIndex { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public Hash256? BlockHash { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public long? BlockNumber { get; set; }

    public RpcNethermindTransaction(Transaction transaction, int? txIndex = null, Hash256? blockHash = null, long? blockNumber = null)
    {
        Hash = transaction.Hash;
        TransactionIndex = txIndex;
        BlockHash = blockHash;
        BlockNumber = blockNumber;
    }

    public class JsonConverter : JsonConverter<RpcNethermindTransaction>
    {
        private readonly Type[] _transactionTypes = new Type[Transaction.MaxTxType + 1];

        public JsonConverter RegisterTransactionType(TxType type, Type @class)
        {
            _transactionTypes[(byte)type] = @class;
            return this;
        }

        public override RpcNethermindTransaction? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var jsonDocument = JsonDocument.ParseValue(ref reader);

            TxType discriminator = default;
            if (jsonDocument.RootElement.TryGetProperty("type", out JsonElement typeProperty))
            {
                discriminator = (TxType?)typeProperty.Deserialize(typeof(TxType), options) ?? default;
            }

            Type concreteTxType = _transactionTypes[(byte)discriminator];

            return (RpcNethermindTransaction?)jsonDocument.Deserialize(concreteTxType, options);
        }

        public override void Write(Utf8JsonWriter writer, RpcNethermindTransaction value, JsonSerializerOptions options)
        {
            JsonSerializer.Serialize(writer, value, value.GetType(), options);
        }
    }

    public class TransactionConverter : IFromTransaction<RpcNethermindTransaction>
    {
        private readonly IFromTransaction<RpcNethermindTransaction>?[] _converters = new IFromTransaction<RpcNethermindTransaction>?[Transaction.MaxTxType + 1];

        public TransactionConverter RegisterConverter(TxType txType, IFromTransaction<RpcNethermindTransaction> converter)
        {
            _converters[(byte)txType] = converter;
            return this;
        }

        public RpcNethermindTransaction FromTransaction(Transaction tx, TransactionConverterExtraData extraData)
        {
            var converter = _converters[(byte)tx.Type] ?? throw new ArgumentException("No converter for transaction type");
            return converter.FromTransaction(tx);
        }
    }

    #region Refactoring transition code
    public static readonly TransactionConverter GlobalConverter = new TransactionConverter()
        .RegisterConverter(TxType.Legacy, new RpcLegacyTransaction.Converter())
        .RegisterConverter(TxType.AccessList, new RpcAccessListTransaction.Converter())
        .RegisterConverter(TxType.EIP1559, new RpcEIP1559Transaction.Converter())
        .RegisterConverter(TxType.Blob, new RpcBlobTransaction.Converter());
    // TODO: Add Optimism:
    // `.RegisterConverter(TxType.DepositTx, new RpcOptimismTransaction.Converter())`

    public static RpcNethermindTransaction FromTransaction(Transaction transaction, Hash256? blockHash = null, long? blockNumber = null, int? txIndex = null, UInt256? baseFee = null)
    {
        var extraData = new TransactionConverterExtraData
        {
            TxIndex = txIndex,
            BlockHash = blockHash,
            BlockNumber = blockNumber,
            BaseFee = baseFee
        };
        return GlobalConverter.FromTransaction(transaction, extraData);
    }
    #endregion
}
