// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Text.Json.Serialization;
using Ethereum.Test.Base;
using Ethereum.Test.Base.T8NUtils;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Specs;
using Nethermind.Evm;
using Nethermind.Evm.Tracing;
using Nethermind.Int256;
using Nethermind.Serialization.Rlp;
using Nethermind.State;
using Nethermind.State.Proofs;

namespace Evm.T8NTool;

public class T8nResult
{
    public Hash256? TxRoot { get; set; }
    public Hash256? ReceiptsRoot { get; set; }
    public Hash256? WithdrawalsRoot { get; set; }
    public Hash256? LogsHash { get; set; }
    public Bloom? LogsBloom { get; set; }
    public TxReceipt[]? Receipts { get; set; }
    public RejectedTx[]? Rejected { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public UInt256? CurrentDifficulty { get; set; }
    public UInt256? GasUsed { get; set; }
    public UInt256? CurrentBaseFee { get; set; }
    public UInt256? CurrentExcessBlobGas { get; set; }
    public UInt256? BlobGasUsed { get; set; }
    public Dictionary<Address, AccountState> Accounts { get; set; }
    public byte[] TransactionsRlp { get; set; }

    private static readonly ReceiptMessageDecoder _receiptMessageDecoder = new();

    public static T8nResult ConstructT8NResult(WorldState stateProvider,
        Block block,
        T8nTestCase test,
        StorageTxTracer storageTracer,
        BlockReceiptsTracer blockReceiptsTracer,
        ISpecProvider specProvider,
        BlockHeader header,
        TransactionExecutionReport txReport)
    {
        T8nResult t8NResult = new();

        IReceiptSpec receiptSpec = specProvider.GetSpec(header);

        Hash256 txRoot = TxTrie.CalculateRoot(txReport.SuccessfulTransactions.ToArray());
        Hash256 receiptsRoot = ReceiptTrie<TxReceipt>.CalculateRoot(receiptSpec,
            txReport.SuccessfulTransactionReceipts.ToArray(), _receiptMessageDecoder);

        var logEntries = txReport.SuccessfulTransactionReceipts
            .SelectMany(receipt => receipt.Logs ?? Enumerable.Empty<LogEntry>())
            .ToArray();
        var bloom = new Bloom(logEntries);

        var gasUsed = blockReceiptsTracer.TxReceipts.Count == 0 ? 0 : (ulong)blockReceiptsTracer.LastReceipt.GasUsedTotal;

        ulong? blobGasUsed = test.Fork.IsEip4844Enabled
            ? BlobGasCalculator.CalculateBlobGas(txReport.ValidTransactions.ToArray())
            : null;

        t8NResult.TxRoot = txRoot;
        t8NResult.ReceiptsRoot = receiptsRoot;
        t8NResult.LogsBloom = bloom;
        t8NResult.LogsHash = Keccak.Compute(Rlp.OfEmptySequence.Bytes);
        t8NResult.Receipts = txReport.SuccessfulTransactionReceipts.ToArray();
        t8NResult.Rejected = txReport.RejectedTransactionReceipts.Count == 0
            ? null
            : txReport.RejectedTransactionReceipts.ToArray();
        t8NResult.CurrentDifficulty = test.CurrentDifficulty;
        t8NResult.GasUsed = new UInt256(gasUsed);
        t8NResult.CurrentBaseFee = test.CurrentBaseFee;
        t8NResult.WithdrawalsRoot = block.WithdrawalsRoot;
        t8NResult.CurrentExcessBlobGas = header.ExcessBlobGas;
        t8NResult.BlobGasUsed = blobGasUsed;

        var accounts = test.Pre.Keys.ToDictionary(address => address,
            address => GetAccountState(address, stateProvider, storageTracer.Storages));
        foreach (var ommer in test.Ommers)
        {
            accounts.Add(ommer.Address, GetAccountState(ommer.Address, stateProvider, storageTracer.Storages));
        }

        if (header.Beneficiary != null)
        {
            accounts.Add(header.Beneficiary, GetAccountState(header.Beneficiary, stateProvider, storageTracer.Storages));
        }

        t8NResult.Accounts = accounts.Where(account => !account.Value.IsEmptyAccount()).ToDictionary();
        t8NResult.TransactionsRlp = Rlp.Encode(txReport.SuccessfulTransactions.ToArray()).Bytes;

        return t8NResult;
    }

    private static AccountState GetAccountState(Address address, WorldState stateProvider, Dictionary<Address, Dictionary<UInt256, byte[]>> storages)
    {
        var account = stateProvider.GetAccount(address);
        var code = stateProvider.GetCode(address);
        var accountState = new AccountState
        {
            Nonce = account.Nonce,
            Balance = account.Balance,
            Code = code.Length == 0 ? null : code
        };

        if (storages.TryGetValue(address, out var storage))
        {
            accountState.Storage = storage;
        }

        return accountState;
    }
}
