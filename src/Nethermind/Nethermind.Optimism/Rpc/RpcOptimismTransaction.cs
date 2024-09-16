// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Core.Crypto;
using Nethermind.Core;
using Nethermind.Int256;
using System.Text.Json.Serialization;
using Nethermind.Facade.Eth.RpcTransaction;
using Nethermind.Facade.Eth;

namespace Nethermind.Optimism.Rpc;

/// <Remarks>
/// Defined in https://github.com/ethereum-optimism/op-geth/blob/8af19cf20261c0b62f98cc27da3a268f542822ee/core/types/deposit_tx.go#L29-L46
/// </Remarks>
public class RpcOptimismTransaction : RpcNethermindTransaction
{
    public TxType Type { get; set; }

    public Hash256 SourceHash { get; set; }

    public Address From { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public Address? To { get; set; }

    public UInt256? Mint { get; set; }

    public UInt256 Value { get; set; }

    public ulong Gas { get; set; }

    public bool IsSystemTx { get; set; }

    public byte[] Input { get; set; }

    #region Nethermind specific fields
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UInt256? DepositReceiptVersion { get; set; }
    #endregion

    public RpcOptimismTransaction(Transaction transaction, int? txIndex = null, Hash256? blockHash = null, long? blockNumber = null, OptimismTxReceipt? receipt = null)
        : base(transaction, txIndex, blockHash, blockNumber)
    {
        // NOTE: According to https://github.com/ethereum-optimism/op-geth/blob/8af19cf20261c0b62f98cc27da3a268f542822ee/core/types/deposit_tx.go#L79 `nonce == 0`
        // Nonce = receipt?.DepositNonce;

        Type = transaction.Type;
        SourceHash = transaction.SourceHash ?? Hash256.Zero;
        From = transaction.SenderAddress ?? Address.SystemUser;
        To = transaction.To;
        Mint = transaction.Mint;
        Value = transaction.Value;
        // TODO: Unsafe cast
        Gas = (ulong)transaction.GasLimit;
        IsSystemTx = transaction.IsOPSystemTransaction;
        Input = transaction.Data?.ToArray() ?? [];

        DepositReceiptVersion = receipt?.DepositReceiptVersion;
    }

    public class Converter : IToTransaction<RpcOptimismTransaction>, IFromTransaction<RpcOptimismTransaction>
    {
        public RpcOptimismTransaction FromTransaction(Transaction tx, TransactionConverterExtraData extraData)
            => new(tx, txIndex: extraData.TxIndex, blockHash: extraData.BlockHash, blockNumber: extraData.BlockNumber, receipt: extraData.Receipt as OptimismTxReceipt);

        public Transaction ToTransaction(RpcOptimismTransaction rpcTx)
        {
            return new Transaction()
            {
                Type = rpcTx.Type,
                SourceHash = rpcTx.SourceHash,
                SenderAddress = rpcTx.From,
                To = rpcTx.To,
                Mint = rpcTx.Mint ?? 0,
                Value = rpcTx.Value,
                GasPrice = rpcTx.Gas,
                // TODO: Unsafe cast
                GasLimit = (long)rpcTx.Gas,
                IsOPSystemTransaction = rpcTx.IsSystemTx,
                Data = rpcTx.Input
            };
        }

        public Transaction ToTransactionWithDefaults(RpcOptimismTransaction rpcTx)
        {
            var tx = ToTransaction(rpcTx);
            return tx;
        }
    }
}
