// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Core.Crypto;
using Nethermind.Core;
using Nethermind.Int256;
using System.Text.Json.Serialization;
using Nethermind.Facade.Eth.RpcTransaction;

namespace Nethermind.Optimism.Rpc;

public class RpcOptimismTransaction : RpcNethermindTransaction
{
    public TxType Type { get; set; }

    public Hash256 SourceHash { get; set; }

    public Address From { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public Address? To { get; set; }

    public UInt256? Mint { get; set; }

    public UInt256 Value { get; set; }

    public long Gas { get; set; }

    public bool? IsSystemTx { get; set; }

    public byte[] Input { get; set; }

    #region Nethermind specific fields
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UInt256? DepositReceiptVersion { get; set; }
    #endregion

    public RpcOptimismTransaction(Transaction transaction) : base(transaction)
    {
        Type = transaction.Type;
        SourceHash = transaction.SourceHash ?? Hash256.Zero;
        From = transaction.SenderAddress ?? Address.SystemUser;
        To = transaction.To;
        Mint = transaction.Mint;
        Value = transaction.Value;
        Gas = transaction.GasLimit;
        IsSystemTx = transaction.IsOPSystemTransaction ? true : null;
        Input = transaction.Data?.ToArray() ?? [];

        // TODO: Handle extra data
        // DepositReceiptVersion = receipt?.DepositReceiptVersion;

        // NOTE: According to https://github.com/ethereum-optimism/op-geth/blob/8af19cf20261c0b62f98cc27da3a268f542822ee/core/types/deposit_tx.go#L79 `nonce == 0`
        // Nonce = receipt?.DepositNonce;
    }

    public static readonly ITransactionConverter<RpcOptimismTransaction> Converter = new ConverterImpl();

    private class ConverterImpl : ITransactionConverter<RpcOptimismTransaction>
    {
        public RpcOptimismTransaction FromTransaction(Transaction transaction) => new(transaction);
    }
}
