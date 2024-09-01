// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Core.Test.Builders;
using Nethermind.Evm;
using Nethermind.Facade.Eth;
using Nethermind.Specs;
using Nethermind.Specs.Forks;
using Nethermind.Specs.Test;
using Nethermind.State;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Nethermind.JsonRpc.Test.Modules.Eth;

public partial class EthRpcModuleTests
{
    [Test]
    public async Task Eth_estimateGas_web3_should_return_insufficient_balance_error()
    {
        using Context ctx = await Context.Create();
        Address someAccount = new("0x0001020304050607080910111213141516171819");
        ctx.Test.ReadOnlyState.AccountExists(someAccount).Should().BeFalse();
        TransactionForRpc transaction = ctx.Test.JsonSerializer.Deserialize<TransactionForRpc>(
            "{\"from\":\"0x0001020304050607080910111213141516171819\",\"gasPrice\":\"0x100000\", \"data\": \"0x70a082310000000000000000000000006c1f09f6271fbe133db38db9c9280307f5d22160\", \"to\": \"0x0d8775f648430679a709e98d2b0cb6250d2887ef\", \"value\": 500}");
        string serialized =
            await ctx.Test.TestEthRpc("eth_estimateGas", ctx.Test.JsonSerializer.Serialize(transaction));
        Assert.That(
            serialized, Is.EqualTo("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32000,\"message\":\"insufficient funds for transfer: address 0x0001020304050607080910111213141516171819\"},\"id\":67}"));
        ctx.Test.ReadOnlyState.AccountExists(someAccount).Should().BeFalse();
    }


    [Test]
    public async Task Eth_estimateGas_web3_sample_not_enough_gas_system_account()
    {
        using Context ctx = await Context.Create();
        ctx.Test.ReadOnlyState.AccountExists(Address.SystemUser).Should().BeFalse();
        TransactionForRpc transaction = ctx.Test.JsonSerializer.Deserialize<TransactionForRpc>(
            "{\"gasPrice\":\"0x100000\", \"data\": \"0x70a082310000000000000000000000006c1f09f6271fbe133db38db9c9280307f5d22160\", \"to\": \"0x0d8775f648430679a709e98d2b0cb6250d2887ef\"}");
        string serialized =
            await ctx.Test.TestEthRpc("eth_estimateGas", ctx.Test.JsonSerializer.Serialize(transaction));
        Assert.That(serialized, Is.EqualTo("{\"jsonrpc\":\"2.0\",\"result\":\"0x53b8\",\"id\":67}"));
        ctx.Test.ReadOnlyState.AccountExists(Address.SystemUser).Should().BeFalse();
    }

    [Test]
    public async Task Eth_estimateGas_web3_sample_not_enough_gas_other_account()
    {
        using Context ctx = await Context.Create();
        Address someAccount = new("0x0001020304050607080910111213141516171819");
        ctx.Test.ReadOnlyState.AccountExists(someAccount).Should().BeFalse();
        TransactionForRpc transaction = ctx.Test.JsonSerializer.Deserialize<TransactionForRpc>(
            "{\"from\":\"0x0001020304050607080910111213141516171819\",\"gasPrice\":\"0x100000\", \"data\": \"0x70a082310000000000000000000000006c1f09f6271fbe133db38db9c9280307f5d22160\", \"to\": \"0x0d8775f648430679a709e98d2b0cb6250d2887ef\"}");
        string serialized =
            await ctx.Test.TestEthRpc("eth_estimateGas", ctx.Test.JsonSerializer.Serialize(transaction));
        Assert.That(serialized, Is.EqualTo("{\"jsonrpc\":\"2.0\",\"result\":\"0x53b8\",\"id\":67}"));
        ctx.Test.ReadOnlyState.AccountExists(someAccount).Should().BeFalse();
    }

    [Test]
    public async Task Eth_estimateGas_web3_above_block_gas_limit()
    {
        using Context ctx = await Context.Create();
        Address someAccount = new("0x0001020304050607080910111213141516171819");
        ctx.Test.ReadOnlyState.AccountExists(someAccount).Should().BeFalse();
        TransactionForRpc transaction = ctx.Test.JsonSerializer.Deserialize<TransactionForRpc>(
            "{\"from\":\"0x0001020304050607080910111213141516171819\",\"gas\":\"0x100000000\",\"gasPrice\":\"0x100000\", \"data\": \"0x70a082310000000000000000000000006c1f09f6271fbe133db38db9c9280307f5d22160\", \"to\": \"0x0d8775f648430679a709e98d2b0cb6250d2887ef\"}");
        string serialized =
            await ctx.Test.TestEthRpc("eth_estimateGas", ctx.Test.JsonSerializer.Serialize(transaction));
        Assert.That(serialized, Is.EqualTo("{\"jsonrpc\":\"2.0\",\"result\":\"0x53b8\",\"id\":67}"));
        ctx.Test.ReadOnlyState.AccountExists(someAccount).Should().BeFalse();
    }

    [TestCase(false, 2)]
    [TestCase(true, 2)]
    [TestCase(true, 17)]
    public async Task Eth_create_access_list_calculates_proper_gas(bool optimize, long loads)
    {
        var test = await TestRpcBlockchain.ForTest(SealEngineType.NethDev)
            .Build(new TestSpecProvider(Berlin.Instance));

        (byte[] code, AccessListItemForRpc[] accessList) = GetTestAccessList(loads);

        TransactionForRpc transaction =
            test.JsonSerializer.Deserialize<TransactionForRpc>(
                $"{{\"type\":\"0x1\", \"data\": \"{code.ToHexString(true)}\"}}");
        string serializedCreateAccessList = await test.TestEthRpc("eth_createAccessList",
            test.JsonSerializer.Serialize(transaction), "0x0", optimize.ToString().ToLower());

        transaction.AccessList = test.JsonSerializer.Deserialize<AccessListItemForRpc[]>(JToken.Parse(serializedCreateAccessList).SelectToken("result.accessList")!.ToString());
        string serializedEstimateGas =
            await test.TestEthRpc("eth_estimateGas", test.JsonSerializer.Serialize(transaction), "0x0");

        var gasUsedEstimateGas = JToken.Parse(serializedEstimateGas).Value<string>("result");
        var gasUsedCreateAccessList =
            JToken.Parse(serializedCreateAccessList).SelectToken("result.gasUsed")?.Value<string>();

        var gasUsedAccessList = (long)Bytes.FromHexString(gasUsedCreateAccessList!).ToUInt256();
        var gasUsedEstimate = (long)Bytes.FromHexString(gasUsedEstimateGas!).ToUInt256();
        Assert.That(gasUsedEstimate, Is.EqualTo(gasUsedAccessList).Within(1.5).Percent);
    }

    [TestCase(true, 0xeee7, 0xf71b)]
    [TestCase(false, 0xeee7, 0xee83)]
    public async Task Eth_estimate_gas_with_accessList(bool senderAccessList, long gasPriceWithoutAccessList,
        long gasPriceWithAccessList)
    {
        var test = await TestRpcBlockchain.ForTest(SealEngineType.NethDev).WithConfig(new JsonRpcConfig() { EstimateErrorMargin = 0 })
            .Build(new TestSpecProvider(Berlin.Instance));

        (byte[] code, AccessListItemForRpc[] accessList) = GetTestAccessList(2, senderAccessList);

        TransactionForRpc transaction =
            test.JsonSerializer.Deserialize<TransactionForRpc>(
                $"{{\"type\":\"0x1\", \"data\": \"{code.ToHexString(true)}\"}}");
        string serialized = await test.TestEthRpc("eth_estimateGas", test.JsonSerializer.Serialize(transaction), "0x0");
        Assert.That(
            serialized, Is.EqualTo($"{{\"jsonrpc\":\"2.0\",\"result\":\"{gasPriceWithoutAccessList.ToHexString(true)}\",\"id\":67}}"));

        transaction.AccessList = accessList;
        serialized = await test.TestEthRpc("eth_estimateGas", test.JsonSerializer.Serialize(transaction), "0x0");
        Assert.That(
            serialized, Is.EqualTo($"{{\"jsonrpc\":\"2.0\",\"result\":\"{gasPriceWithAccessList.ToHexString(true)}\",\"id\":67}}"));
    }

    [Test]
    public async Task Eth_estimate_gas_is_lower_with_optimized_access_list()
    {
        var test = await TestRpcBlockchain.ForTest(SealEngineType.NethDev)
            .Build(new TestSpecProvider(Berlin.Instance));

        (byte[] code, AccessListItemForRpc[] accessList) = GetTestAccessList(2, true);
        (byte[] _, AccessListItemForRpc[] optimizedAccessList) = GetTestAccessList(2, false);

        TransactionForRpc transaction =
            test.JsonSerializer.Deserialize<TransactionForRpc>(
                $"{{\"type\":\"0x1\", \"data\": \"{code.ToHexString(true)}\"}}");
        transaction.AccessList = accessList;
        string serialized = await test.TestEthRpc("eth_estimateGas", test.JsonSerializer.Serialize(transaction), "0x0");
        long estimateGas = Convert.ToInt64(JToken.Parse(serialized).Value<string>("result"), 16);

        transaction.AccessList = optimizedAccessList;
        serialized = await test.TestEthRpc("eth_estimateGas", test.JsonSerializer.Serialize(transaction), "0x0");
        long optimizedEstimateGas = Convert.ToInt64(JToken.Parse(serialized).Value<string>("result"), 16);

        optimizedEstimateGas.Should().BeLessThan(estimateGas);
    }

    [Test]
    public async Task Estimate_gas_without_gas_pricing()
    {
        using Context ctx = await Context.Create();
        TransactionForRpc transaction = ctx.Test.JsonSerializer.Deserialize<TransactionForRpc>(
            "{\"from\": \"0x0d8775f648430679a709e98d2b0cb6250d2887ef\", \"to\": \"0x0d8775f648430679a709e98d2b0cb6250d2887ef\"}");
        string serialized = await ctx.Test.TestEthRpc("eth_estimateGas", ctx.Test.JsonSerializer.Serialize(transaction));
        Assert.That(serialized, Is.EqualTo("{\"jsonrpc\":\"2.0\",\"result\":\"0x5208\",\"id\":67}"));
    }

    [Test]
    public async Task Estimate_gas_with_gas_pricing()
    {
        using Context ctx = await Context.Create();
        TransactionForRpc transaction = ctx.Test.JsonSerializer.Deserialize<TransactionForRpc>(
            "{\"from\": \"0x32e4e4c7c5d1cea5db5f9202a9e4d99e56c91a24\", \"to\": \"0x32e4e4c7c5d1cea5db5f9202a9e4d99e56c91a24\", \"gasPrice\": \"0x10\"}");
        string serialized = await ctx.Test.TestEthRpc("eth_estimateGas", ctx.Test.JsonSerializer.Serialize(transaction));
        Assert.That(serialized, Is.EqualTo("{\"jsonrpc\":\"2.0\",\"result\":\"0x5208\",\"id\":67}"));
    }

    [Test]
    public async Task Estimate_gas_without_gas_pricing_after_1559_legacy()
    {
        using Context ctx = await Context.CreateWithLondonEnabled();
        TransactionForRpc transaction = ctx.Test.JsonSerializer.Deserialize<TransactionForRpc>(
            "{\"from\": \"0x32e4e4c7c5d1cea5db5f9202a9e4d99e56c91a24\", \"to\": \"0x32e4e4c7c5d1cea5db5f9202a9e4d99e56c91a24\", \"gasPrice\": \"0x10\"}");
        string serialized = await ctx.Test.TestEthRpc("eth_estimateGas", ctx.Test.JsonSerializer.Serialize(transaction));
        Assert.That(serialized, Is.EqualTo("{\"jsonrpc\":\"2.0\",\"result\":\"0x5208\",\"id\":67}"));
    }

    [Test]
    public async Task Estimate_gas_without_gas_pricing_after_1559_new_type_of_transaction()
    {
        using Context ctx = await Context.CreateWithLondonEnabled();
        TransactionForRpc transaction = ctx.Test.JsonSerializer.Deserialize<TransactionForRpc>(
            "{\"from\": \"0x32e4e4c7c5d1cea5db5f9202a9e4d99e56c91a24\", \"to\": \"0x32e4e4c7c5d1cea5db5f9202a9e4d99e56c91a24\", \"type\": \"0x2\"}");
        string serialized = await ctx.Test.TestEthRpc("eth_estimateGas", ctx.Test.JsonSerializer.Serialize(transaction));
        Assert.That(serialized, Is.EqualTo("{\"jsonrpc\":\"2.0\",\"result\":\"0x5208\",\"id\":67}"));
        byte[] code = Prepare.EvmCode
            .Op(Instruction.BASEFEE)
            .PushData(0)
            .Op(Instruction.SSTORE)
            .Done;
    }

    [Test]
    public async Task Estimate_gas_with_base_fee_opcode()
    {
        using Context ctx = await Context.CreateWithLondonEnabled();

        byte[] code = Prepare.EvmCode
            .Op(Instruction.BASEFEE)
            .PushData(0)
            .Op(Instruction.MSTORE)
            .PushData("0x20")
            .PushData("0x0")
            .Op(Instruction.RETURN)
            .Done;

        string dataStr = code.ToHexString();
        TransactionForRpc transaction = ctx.Test.JsonSerializer.Deserialize<TransactionForRpc>(
            $"{{\"from\": \"0x32e4e4c7c5d1cea5db5f9202a9e4d99e56c91a24\", \"type\": \"0x2\", \"data\": \"{dataStr}\"}}");
        string serialized = await ctx.Test.TestEthRpc("eth_estimateGas", ctx.Test.JsonSerializer.Serialize(transaction));
        Assert.That(
            serialized, Is.EqualTo("{\"jsonrpc\":\"2.0\",\"result\":\"0xe891\",\"id\":67}"));
    }

    [Test]
    public async Task Estimate_gas_with_revert()
    {
        using Context ctx = await Context.CreateWithLondonEnabled();

        byte[] code = Prepare.EvmCode
            .PushData(0)
            .PushData(0)
            .Op(Instruction.REVERT)
            .Done;

        string dataStr = code.ToHexString();
        TransactionForRpc transaction = ctx.Test.JsonSerializer.Deserialize<TransactionForRpc>(
            $"{{\"from\": \"0x32e4e4c7c5d1cea5db5f9202a9e4d99e56c91a24\", \"type\": \"0x2\", \"data\": \"{dataStr}\"}}");
        string serialized = await ctx.Test.TestEthRpc("eth_estimateGas", ctx.Test.JsonSerializer.Serialize(transaction));
        Assert.That(
            serialized, Is.EqualTo("{\"jsonrpc\":\"2.0\",\"error\":{\"code\":-32015,\"message\":\"revert\"},\"id\":67}"));
    }

    [Test]
    public async Task should_estimate_transaction_with_deployed_code_when_eip3607_enabled()
    {
        OverridableReleaseSpec releaseSpec = new(London.Instance) { Eip1559TransitionBlock = 1, IsEip3607Enabled = true };
        TestSpecProvider specProvider = new(releaseSpec) { AllowTestChainOverride = false };
        using Context ctx = await Context.Create(specProvider);

        Transaction tx = Build.A.Transaction.SignedAndResolved(TestItem.PrivateKeyA).TestObject;
        TransactionForRpc transaction = new(Keccak.Zero, 1L, 1, tx);
        ctx.Test.State.InsertCode(TestItem.AddressA, "H"u8.ToArray(), London.Instance);
        transaction.To = TestItem.AddressB;

        string serialized =
            await ctx.Test.TestEthRpc("eth_estimateGas", ctx.Test.JsonSerializer.Serialize(transaction), "latest");
        Assert.That(serialized, Is.EqualTo("{\"jsonrpc\":\"2.0\",\"result\":\"0x5208\",\"id\":67}"));
    }

    [TestCase(
        "Nonce override doesn't cause failure",
        """{"from":"0x7f554713be84160fdf0178cc8df86f5aabd33397","to":"0xb7705ae4c6f81b66cdb323c65f4e8133690fc099","value":"0x0"}""",
        """{"0xb7705ae4c6f81b66cdb323c65f4e8133690fc099":{"nonce":"0x123"}}""",
        """{"jsonrpc":"2.0","result":"TODO","id":67}"""
    )]
    [TestCase(
        "Uses account balance from state override",
        """{"from":"0x7f554713be84160fdf0178cc8df86f5aabd33397","to":"0xbe5c953dd0ddb0ce033a98f36c981f1b74d3b33f","value":"0x100"}""",
        """{"0x7f554713be84160fdf0178cc8df86f5aabd33397":{"balance":"0x100"}}""",
        """{"jsonrpc":"2.0","result":"TODO","id":67}"""
    )]
    [TestCase(
        "Executes code from state override",
        """{"from":"0xb7705ae4c6f81b66cdb323c65f4e8133690fc099","to":"0xc200000000000000000000000000000000000000","input":"0xf8b2cb4f000000000000000000000000b7705ae4c6f81b66cdb323c65f4e8133690fc099"}""",
        """{"0xc200000000000000000000000000000000000000":{"code":"0x608060405234801561001057600080fd5b506004361061002b5760003560e01c8063f8b2cb4f14610030575b600080fd5b61004a600480360381019061004591906100e4565b610060565b604051610057919061012a565b60405180910390f35b60008173ffffffffffffffffffffffffffffffffffffffff16319050919050565b600080fd5b600073ffffffffffffffffffffffffffffffffffffffff82169050919050565b60006100b182610086565b9050919050565b6100c1816100a6565b81146100cc57600080fd5b50565b6000813590506100de816100b8565b92915050565b6000602082840312156100fa576100f9610081565b5b6000610108848285016100cf565b91505092915050565b6000819050919050565b61012481610111565b82525050565b600060208201905061013f600083018461011b565b9291505056fea2646970667358221220172c443a163d8a43e018c339d1b749c312c94b6de22835953d960985daf228c764736f6c63430008120033"}}""",
        """{"jsonrpc":"2.0","result":"TODO","id":67}"""
    )]
    [TestCase(
        "Executes precompile using overriden address",
        """{"from":"0x7f554713be84160fdf0178cc8df86f5aabd33397","to":"0x0000000000000000000000000000000000123456","input":"0xB6E16D27AC5AB427A7F68900AC5559CE272DC6C37C82B3E052246C82244C50E4000000000000000000000000000000000000000000000000000000000000001C7B8B1991EB44757BC688016D27940DF8FB971D7C87F77A6BC4E938E3202C44037E9267B0AEAA82FA765361918F2D8ABD9CDD86E64AA6F2B81D3C4E0B69A7B055"}""",
        """{"0x0000000000000000000000000000000000000001":{"movePrecompileToAddress":"0x0000000000000000000000000000000000123456", "code": "0x"}}""",
        """{"jsonrpc":"2.0","result":"0xbb8","id":67}"""
    )]
    public async Task Estimate_gas_with_state_override(string name, string transaction, string stateOverride, string expectedResult)
    {
        using Context ctx = await Context.Create();

        string serialized = await ctx.Test.TestEthRpc("eth_estimateGas", transaction, "latest", stateOverride);

        Assert.That(serialized, Is.EqualTo(expectedResult));
    }

    [TestCase(
        """{"from":"0x7f554713be84160fdf0178cc8df86f5aabd33397","to":"0xbe5c953dd0ddb0ce033a98f36c981f1b74d3b33f","value":"0x1"}""",
        """{"0x7f554713be84160fdf0178cc8df86f5aabd33397":{"balance":"0x123", "nonce": "0x123"}}"""
    )]
    [TestCase(
        """{"from":"0x7f554713be84160fdf0178cc8df86f5aabd33397","to":"0xc200000000000000000000000000000000000000","input":"0xf8b2cb4f000000000000000000000000b7705ae4c6f81b66cdb323c65f4e8133690fc099"}""",
        """{"0xc200000000000000000000000000000000000000":{"code":"0x608060405234801561001057600080fd5b506004361061002b5760003560e01c8063f8b2cb4f14610030575b600080fd5b61004a600480360381019061004591906100e4565b610060565b604051610057919061012a565b60405180910390f35b60008173ffffffffffffffffffffffffffffffffffffffff16319050919050565b600080fd5b600073ffffffffffffffffffffffffffffffffffffffff82169050919050565b60006100b182610086565b9050919050565b6100c1816100a6565b81146100cc57600080fd5b50565b6000813590506100de816100b8565b92915050565b6000602082840312156100fa576100f9610081565b5b6000610108848285016100cf565b91505092915050565b6000819050919050565b61012481610111565b82525050565b600060208201905061013f600083018461011b565b9291505056fea2646970667358221220172c443a163d8a43e018c339d1b749c312c94b6de22835953d960985daf228c764736f6c63430008120033"}}"""
    )]
    public async Task Estimate_gas_with_state_override_does_not_affect_other_calls(string transaction, string stateOverride)
    {
        using Context ctx = await Context.Create();

        var resultOverrideBefore = await ctx.Test.TestEthRpc("eth_estimateGas", transaction, "latest", stateOverride);

        var resultNoOverride = await ctx.Test.TestEthRpc("eth_estimateGas", transaction, "latest");

        var resultOverrideAfter = await ctx.Test.TestEthRpc("eth_estimateGas", transaction, "latest", stateOverride);

        Assert.Multiple(() =>
        {
            Assert.That(resultOverrideBefore, Is.EqualTo(resultOverrideAfter), resultOverrideBefore.Replace("\"", "\\\""));
            Assert.That(resultNoOverride, Is.Not.EqualTo(resultOverrideAfter), resultNoOverride.Replace("\"", "\\\""));
        });
    }
}
