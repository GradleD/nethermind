// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Nethermind.Core;
using Nethermind.Core.Eip2930;
using Nethermind.Core.Test.Builders;
using Nethermind.Facade.Eth.RpcTransaction;
using Nethermind.Int256;
using Nethermind.Serialization.Json;
using NUnit.Framework;

namespace Nethermind.JsonRpc.Test.Modules.RpcTransaction;

public class RpcAccessListTransactionTests
{
    private readonly EthereumJsonSerializer _serializer = new();

    private static TransactionBuilder<Transaction> Build => Core.Test.Builders.Build.A.Transaction.WithType(TxType.AccessList);

    public static readonly Transaction[] Transactions =
    [
        Build.TestObject,

        Build.WithNonce(UInt256.Zero).TestObject,
        Build.WithNonce((UInt256) 123).TestObject,
        Build.WithNonce(UInt256.MaxValue).TestObject,

        Build.WithTo(null).TestObject,
        Build.WithTo(TestItem.AddressA).TestObject,
        Build.WithTo(TestItem.AddressB).TestObject,
        Build.WithTo(TestItem.AddressC).TestObject,
        Build.WithTo(TestItem.AddressD).TestObject,
        Build.WithTo(TestItem.AddressE).TestObject,
        Build.WithTo(TestItem.AddressF).TestObject,

        Build.WithGasLimit(0).TestObject,
        Build.WithGasLimit(123).TestObject,
        Build.WithGasLimit(long.MaxValue).TestObject,

        Build.WithValue(UInt256.Zero).TestObject,
        Build.WithValue((UInt256) 123).TestObject,
        Build.WithValue(UInt256.MaxValue).TestObject,

        Build.WithData(TestItem.RandomDataA).TestObject,
        Build.WithData(TestItem.RandomDataB).TestObject,
        Build.WithData(TestItem.RandomDataC).TestObject,
        Build.WithData(TestItem.RandomDataD).TestObject,

        Build.WithGasPrice(UInt256.Zero).TestObject,
        Build.WithGasPrice((UInt256) 123).TestObject,
        Build.WithGasPrice(UInt256.MaxValue).TestObject,

        Build.WithChainId(null).TestObject,
        Build.WithChainId(BlockchainIds.Mainnet).TestObject,
        Build.WithChainId(BlockchainIds.Sepolia).TestObject,
        Build.WithChainId(0).TestObject,
        Build.WithChainId(ulong.MaxValue).TestObject,

        Build.WithAccessList(new AccessList.Builder()
            .AddAddress(TestItem.AddressA)
            .AddStorage(1)
            .AddStorage(2)
            .AddStorage(3)
            .Build()).TestObject,

        Build.WithAccessList(new AccessList.Builder()
            .AddAddress(TestItem.AddressA)
            .AddStorage(1)
            .AddStorage(2)
            .AddAddress(TestItem.AddressB)
            .AddStorage(3)
            .Build()).TestObject,

        Build.WithSignature(TestItem.RandomSignatureA).TestObject,
        Build.WithSignature(TestItem.RandomSignatureB).TestObject,
    ];

    [SetUp]
    public void SetUp()
    {
        RpcAccessListTransaction.DefaultChainId = BlockchainIds.Mainnet;
    }

    [TestCaseSource(nameof(Transactions))]
    public void Always_satisfies_schema(Transaction transaction)
    {
        var rpcTx = RpcAccessListTransaction.FromTransaction(transaction);
        string serialized = _serializer.Serialize(rpcTx);
        using var jsonDocument = JsonDocument.Parse(serialized);
        var json = jsonDocument.RootElement;

        json.GetProperty("type").GetString().Should().MatchRegex("^0x1$");
        json.GetProperty("nonce").GetString().Should().MatchRegex("^0x([1-9a-f]+[0-9a-f]*|0)$");
        json.GetProperty("to").GetString()?.Should().MatchRegex("^0x[0-9a-fA-F]{40}$");
        json.GetProperty("gas").GetString().Should().MatchRegex("^0x([1-9a-f]+[0-9a-f]*|0)$");
        json.GetProperty("value").GetString().Should().MatchRegex("^0x([1-9a-f]+[0-9a-f]*|0)$");
        json.GetProperty("input").GetString().Should().MatchRegex("^0x[0-9a-f]*$");
        json.GetProperty("gasPrice").GetString().Should().MatchRegex("^0x([1-9a-f]+[0-9a-f]*|0)$");
        // Suprising inconsistency in `FluentAssertions` where `AllSatisfy` fails on empty collections.
        // This requires wrapping the assertion in a condition.
        // See: https://github.com/fluentassertions/fluentassertions/discussions/2143#discussioncomment-9677309
        var accessList = json.GetProperty("accessList").EnumerateArray();
        if (accessList.Any())
        {
            accessList.Should().AllSatisfy(item =>
            {
                item.GetProperty("address").GetString().Should().MatchRegex("^0x[0-9a-fA-F]{40}$");
                item.GetProperty("storageKeys").EnumerateArray().Should().AllSatisfy(key =>
                    key.GetString().Should().MatchRegex("^0x[0-9a-f]{64}$")
                );
            });
        }
        json.GetProperty("chainId").GetString().Should().MatchRegex("^0x([1-9a-f]+[0-9a-f]*|0)$");
        var yParity = json.GetProperty("yParity").GetString();
        yParity.Should().MatchRegex("^0x([1-9a-f]+[0-9a-f]*|0)$");
        if (json.TryGetProperty("v", out var v))
        {
            v.GetString().Should().Be(yParity);
        }
        json.GetProperty("r").GetString().Should().MatchRegex("^0x([1-9a-f]+[0-9a-f]*|0)$");
        json.GetProperty("s").GetString().Should().MatchRegex("^0x([1-9a-f]+[0-9a-f]*|0)$");
    }
}
