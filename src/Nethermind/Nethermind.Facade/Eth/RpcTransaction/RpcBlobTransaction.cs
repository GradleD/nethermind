// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Text.Json.Serialization;
using Nethermind.Core;
using Nethermind.Int256;

namespace Nethermind.Facade.Eth.RpcTransaction;

public class RpcBlobTransaction : RpcEIP1559Transaction
{
    public UInt256 MaxFeePerBlobGas { get; set; }

    // TODO: Each item should be a 32 byte array
    // Currently we don't enforce this (hashes can have any length)
    public byte[][] BlobVersionedHashes { get; set; }

    [JsonConstructor]
    public RpcBlobTransaction() { }

    public RpcBlobTransaction(Transaction transaction) : base(transaction)
    {
        MaxFeePerBlobGas = transaction.MaxFeePerBlobGas ?? 0;
        BlobVersionedHashes = transaction.BlobVersionedHashes ?? [];
    }

    public new static readonly IRpcTransactionConverter Converter = new ConverterImpl();

    private class ConverterImpl : IRpcTransactionConverter
    {
        public IRpcTransaction FromTransaction(Transaction transaction, TxReceipt receipt) => new RpcBlobTransaction(transaction);
    }
}
