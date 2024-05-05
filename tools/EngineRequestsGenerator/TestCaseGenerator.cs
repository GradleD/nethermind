﻿// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Text;
using FluentAssertions;
using Nethermind.Blockchain;
using Nethermind.Consensus.Producers;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Core.Test.Builders;
using Nethermind.Crypto;
using Nethermind.Evm;
using Nethermind.Int256;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.Data;
using Nethermind.Merge.Plugin.Test;
using Nethermind.Serialization.Json;
using Nethermind.Specs.ChainSpecStyle;
using Nethermind.TxPool;

namespace EngineRequestsGenerator;

public class TestCaseGenerator
{
    private int _numberOfBlocksToProduce;
    private int _maxNumberOfWithdrawalsPerBlock;
    private int _numberOfWithdrawals;
    private int _txsPerBlock;
    private const int blockGasConsumptionTarget = 30_000_000;

    private string _chainSpecPath;
    private ChainSpec _chainSpec;
    private ChainSpecBasedSpecProvider _chainSpecBasedSpecProvider;
    private TestCase _testCase;
    private readonly string _outputPath;
    private TaskCompletionSource<bool>? _taskCompletionSource;
    private Task WaitForProcessingBlock => _taskCompletionSource?.Task ?? Task.CompletedTask;

    public TestCaseGenerator(
        string chainSpecPath,
        TestCase testCase,
        string outputPath)
    {
        _numberOfBlocksToProduce = 2;

        _maxNumberOfWithdrawalsPerBlock = 16;
        _numberOfWithdrawals = 1600;
        _chainSpecPath = chainSpecPath;
        _testCase = testCase;
        _outputPath = outputPath;
    }
    public async Task Generate()
    {
        bool generateSingleFile = false;
        if (generateSingleFile)
        {
            await GenerateTestCase(blockGasConsumptionTarget);
            Console.WriteLine($"generated testcase {blockGasConsumptionTarget}");
        }
        else
        {
            await GenerateTestCases();
        }
    }


    private async Task GenerateTestCases()
    {
        foreach (long blockGasConsumptionTarget in BlockGasVariants.Variants)
        {
            await GenerateTestCase(blockGasConsumptionTarget);
            Console.WriteLine($"generated testcase {blockGasConsumptionTarget}");
        }
    }

    private async Task GenerateTestCase(long blockGasConsumptionTarget)
    {
        // _txsPerBlock = blockGasConsumptionTarget / 854_000;
        // _txsPerBlock = blockGasConsumptionTarget / 970_000;
        // _txsPerBlock = blockGasConsumptionTarget / 987_000;

        _txsPerBlock = _testCase switch
        {
            TestCase.Transfers => (int)blockGasConsumptionTarget / (int)GasCostOf.Transaction,
            _ => 1
        };

        // chain initialization
        StringBuilder stringBuilder = new();
        EthereumJsonSerializer serializer = new();

        ChainSpecLoader chainSpecLoader = new(serializer);
        _chainSpec = chainSpecLoader.LoadEmbeddedOrFromFile(_chainSpecPath, LimboLogs.Instance.GetClassLogger());
        _chainSpecBasedSpecProvider = new(_chainSpec);

        EngineModuleTests.MergeTestBlockchain chain = await new EngineModuleTests.MergeTestBlockchain().Build(true, _chainSpecBasedSpecProvider);

        GenesisLoader genesisLoader = new(_chainSpec, _chainSpecBasedSpecProvider, chain.State, chain.TxProcessor);
        Block genesisBlock = genesisLoader.Load();

        chain.BlockTree.SuggestBlock(genesisBlock);


        // prepare private keys - up to 16_777_216 (2^24)
        int numberOfKeysToGenerate = _maxNumberOfWithdrawalsPerBlock * _numberOfBlocksToProduce;
        PrivateKey[] privateKeys = PreparePrivateKeys(numberOfKeysToGenerate).ToArray();


        // producing blocks and printing engine requests
        Block previousBlock = genesisBlock;
        for (int i = 0; i < _numberOfBlocksToProduce; i++)
        {
            PayloadAttributes payloadAttributes = new()
            {
                Timestamp = previousBlock.Timestamp + 1,
                ParentBeaconBlockRoot = previousBlock.Hash,
                PrevRandao = previousBlock.Hash ?? Keccak.Zero,
                SuggestedFeeRecipient = Address.Zero,
                Withdrawals = GetBlockWithdrawals(i, privateKeys).ToArray()
            };

            SubmitTxs(chain.TxPool, privateKeys, previousBlock.Withdrawals, _testCase, blockGasConsumptionTarget);

            chain.PayloadPreparationService!.StartPreparingPayload(previousBlock.Header, payloadAttributes);
            Block block = chain.PayloadPreparationService!.GetPayload(payloadAttributes.GetPayloadId(previousBlock.Header)).Result!.CurrentBestBlock!;

            Console.WriteLine($"testcase {blockGasConsumptionTarget} gasUsed: {block.GasUsed}");


            ExecutionPayloadV3 executionPayload = new(block);
            string executionPayloadString = serializer.Serialize(executionPayload);
            string blobsString = serializer.Serialize(Array.Empty<byte[]>());
            string parentBeaconBlockRootString = serializer.Serialize(previousBlock.Hash);

            WriteJsonRpcRequest(stringBuilder, "engine_newPayloadV3", executionPayloadString, blobsString, parentBeaconBlockRootString);

            ForkchoiceStateV1 forkchoiceState = new(block.Hash, Keccak.Zero, Keccak.Zero);
            WriteJsonRpcRequest(stringBuilder, "engine_forkchoiceUpdatedV3", serializer.Serialize(forkchoiceState));

            _taskCompletionSource = new TaskCompletionSource<bool>();
            chain.BlockProcessingQueue.ProcessingQueueEmpty += OnEmptyProcessingQueue;
            chain.BlockTree.SuggestBlock(block);

            if (!WaitForProcessingBlock.IsCompleted)
            {
                await WaitForProcessingBlock;
                chain.BlockProcessingQueue.ProcessingQueueEmpty -= OnEmptyProcessingQueue;
            }

            previousBlock = block;
        }

        if (!Directory.Exists(_outputPath))
            Directory.CreateDirectory(_outputPath);
        await File.WriteAllTextAsync($"{_outputPath}/{_testCase}_{blockGasConsumptionTarget/1_000_000}M.txt", stringBuilder.ToString());
    }

    private void OnEmptyProcessingQueue(object? sender, EventArgs e)
    {
        // if (!WaitForProcessingBlock.IsCompleted)
        // {
            _taskCompletionSource?.SetResult(true);
        // }
    }

    private void SubmitTxs(ITxPool txPool, PrivateKey[] privateKeys, Withdrawal[] previousBlockWithdrawals, TestCase testCase, long blockGasConsumptionTarget)
    {
        int txsPerAddress = _txsPerBlock / _maxNumberOfWithdrawalsPerBlock;
        int txsLeft = _txsPerBlock % _maxNumberOfWithdrawalsPerBlock;

        foreach (Withdrawal previousBlockWithdrawal in previousBlockWithdrawals)
        {
            int additionalTx = (int)previousBlockWithdrawal.ValidatorIndex % _maxNumberOfWithdrawalsPerBlock < txsLeft
                ? 1
                : 0;
            for (int i = 0; i < txsPerAddress + additionalTx; i++)
            {
                Transaction tx = GetTx(privateKeys[previousBlockWithdrawal.ValidatorIndex - 1], i, testCase, blockGasConsumptionTarget);
                txPool.SubmitTx(tx, TxHandlingOptions.None).Should().Be(AcceptTxResult.Accepted);

                // tx = Build.A.Transaction
                //     .WithNonce((UInt256)i)
                //     .WithType(TxType.EIP1559)
                //     .WithMaxFeePerGas(1.GWei())
                //     .WithMaxPriorityFeePerGas(1.GWei())
                //     .WithTo(privateKeys[previousBlockWithdrawal.ValidatorIndex - 1].Address)
                //     .WithChainId(BlockchainIds.Holesky)
                //     .WithGasLimit(blockGasConsumptionTarget)
                //     .SignedAndResolved(privateKeys[previousBlockWithdrawal.ValidatorIndex - 2])
                //     .TestObject;;
                // txPool.SubmitTx(tx, TxHandlingOptions.None).Should().Be(AcceptTxResult.Accepted);
            }
        }
    }

    private Transaction GetTx(PrivateKey privateKey, int nonce, TestCase testCase, long blockGasConsumptionTarget)
    {
        switch (testCase)
        {
            case TestCase.Warmup:
            case TestCase.Transfers:
                return Build.A.Transaction
                    .WithNonce((UInt256)nonce)
                    .WithType(TxType.EIP1559)
                    .WithMaxFeePerGas(1.GWei())
                    .WithMaxPriorityFeePerGas(1.GWei())
                    .WithTo(TestItem.AddressB)
                    .WithChainId(BlockchainIds.Holesky)
                    .SignedAndResolved(privateKey)
                    .TestObject;
            case TestCase.TxDataZero:
                long numberOfBytes = (blockGasConsumptionTarget - GasCostOf.Transaction) / GasCostOf.TxDataZero;
                byte[] data = new byte[numberOfBytes];
                return Build.A.Transaction
                    .WithNonce((UInt256)nonce)
                    .WithType(TxType.EIP1559)
                    .WithMaxFeePerGas(1.GWei())
                    .WithMaxPriorityFeePerGas(1.GWei())
                    .WithTo(TestItem.AddressB)
                    .WithChainId(BlockchainIds.Holesky)
                    .WithData(data)
                    .WithGasLimit(_chainSpec.Genesis.GasLimit)
                    .SignedAndResolved(privateKey)
                    .TestObject;
            case TestCase.Keccak256From1Byte:
                return Build.A.Transaction
                    .WithNonce((UInt256)nonce)
                    .WithType(TxType.EIP1559)
                    .WithMaxFeePerGas(1.GWei())
                    .WithMaxPriorityFeePerGas(1.GWei())
                    .WithTo(null)
                    .WithChainId(BlockchainIds.Holesky)
                    .WithData(PrepareKeccak256Code(blockGasConsumptionTarget, 1))
                    .WithGasLimit(blockGasConsumptionTarget)
                    .SignedAndResolved(privateKey)
                    .TestObject;
            case TestCase.Keccak256From8Bytes:
                return Build.A.Transaction
                    .WithNonce((UInt256)nonce)
                    .WithType(TxType.EIP1559)
                    .WithMaxFeePerGas(1.GWei())
                    .WithMaxPriorityFeePerGas(1.GWei())
                    .WithTo(null)
                    .WithChainId(BlockchainIds.Holesky)
                    .WithData(PrepareKeccak256Code(blockGasConsumptionTarget, 8))
                    .WithGasLimit(blockGasConsumptionTarget)
                    .SignedAndResolved(privateKey)
                    .TestObject;
            case TestCase.Keccak256From32Bytes:
                return Build.A.Transaction
                    .WithNonce((UInt256)nonce)
                    .WithType(TxType.EIP1559)
                    .WithMaxFeePerGas(1.GWei())
                    .WithMaxPriorityFeePerGas(1.GWei())
                    .WithTo(null)
                    .WithChainId(BlockchainIds.Holesky)
                    .WithData(PrepareKeccak256Code(blockGasConsumptionTarget, 32))
                    .WithGasLimit(blockGasConsumptionTarget)
                    .SignedAndResolved(privateKey)
                    .TestObject;
            case TestCase.SHA2From32Bytes:
                return Build.A.Transaction
                    .WithNonce((UInt256)nonce)
                    .WithType(TxType.EIP1559)
                    .WithMaxFeePerGas(1.GWei())
                    .WithMaxPriorityFeePerGas(1.GWei())
                    .WithTo(null)
                    .WithChainId(BlockchainIds.Holesky)
                    .WithData(PrepareKeccak256Code(blockGasConsumptionTarget, 32))
                    .WithGasLimit(blockGasConsumptionTarget)
                    .SignedAndResolved(privateKey)
                    .TestObject;
            default:
                throw new ArgumentOutOfRangeException(nameof(testCase), testCase, null);
        }
    }

    // private static byte[] PrepareKeccak256CodeInIntVersion(int blockGasConsumptionTarget)
    // {
    //     List<byte> byteCode = new();
    //
    //     // int example = 1;
    //     // byte[] byteExample = example.ToByteArray();
    //     // UInt256 length = (UInt256)byteExample.Length;
    //
    //     long gasLeft = blockGasConsumptionTarget - GasCostOf.Transaction;
    //     // long gasCost = GasCostOf.Sha3 + GasCostOf.Sha3Word * EvmPooledMemory.Div32Ceiling(in length);
    //     // long iterations = (blockGasConsumptionTarget - GasCostOf.Transaction) / gasCost;
    //
    //     int i = 0;
    //     long dataCost = 0;
    //     // while(gasLeft > 0)
    //     for (int j = 0; j < 3500; j++)
    //     {
    //         List<byte> iterationCode = new();
    //         var data = i++.ToByteArray();
    //         // int zeroData = data.AsSpan().CountZeros();
    //         UInt256 length = (UInt256)data.Length;
    //
    //         long gasCost = 0;
    //         // GasCostOf.Sha3 + GasCostOf.Sha3Word * EvmPooledMemory.Div32Ceiling(in length) + zeroData * GasCostOf.TxDataZero + (data.Length - zeroData) * GasCostOf.TxDataNonZeroEip2028;
    //         // push value as source to compute hash
    //         iterationCode.Add((byte)(Instruction.PUSH1 + (byte)data.Length - 1));
    //         gasCost += GasCostOf.VeryLow;
    //         iterationCode.AddRange(data);
    //
    //         // gasCost += zeroData * GasCostOf.TxDataZero + (data.Length - zeroData) * GasCostOf.TxDataNonZeroEip2028;
    //         // push memory position - 0
    //         iterationCode.Add((byte)(Instruction.PUSH1));
    //         gasCost += GasCostOf.VeryLow;
    //         iterationCode.AddRange(new[] { Byte.MinValue });
    //         // gasCost += GasCostOf.TxDataZero;
    //         // save in memory
    //         iterationCode.Add((byte)Instruction.MSTORE);
    //         gasCost += GasCostOf.Memory;
    //
    //         // push byte size to read from memory - 4
    //         iterationCode.Add((byte)(Instruction.PUSH1));
    //         gasCost += GasCostOf.VeryLow;
    //         iterationCode.AddRange(new[] { (byte)4 });
    //         // gasCost += GasCostOf.TxDataNonZeroEip2028;
    //         // push byte offset in memory - 0
    //         iterationCode.Add((byte)(Instruction.PUSH1));
    //         gasCost += GasCostOf.VeryLow;
    //         iterationCode.AddRange(new[] { Byte.MinValue });
    //         // gasCost += GasCostOf.TxDataZero;
    //         // compute keccak
    //         iterationCode.Add((byte)Instruction.KECCAK256);
    //         gasCost += GasCostOf.Sha3 + GasCostOf.Sha3Word * EvmPooledMemory.Div32Ceiling(in length);
    //
    //         //remove from stack
    //         iterationCode.Add((byte)(Instruction.POP));
    //         gasCost += GasCostOf.Base;
    //
    //         byteCode.AddRange(iterationCode);
    //
    //         int zeroData = iterationCode.ToArray().AsSpan().CountZeros();
    //
    //         gasCost += zeroData * GasCostOf.TxDataZero + (iterationCode.Count - zeroData) * GasCostOf.TxDataNonZeroEip2028;
    //
    //         dataCost += zeroData * GasCostOf.TxDataZero + (iterationCode.Count - zeroData) * GasCostOf.TxDataNonZeroEip2028;
    //
    //         gasLeft -= gasCost;
    //
    //         // now keccak of given data is in memory
    //     }
    //
    //     return byteCode.ToArray();
    // }
    //
    // private static byte[] PrepareKeccak256CodeInByteVersion(int blockGasConsumptionTarget)
    // {
    //     List<byte> byteCode = new();
    //
    //     // int example = 1;
    //     // byte[] byteExample = example.ToByteArray();
    //     // UInt256 length = (UInt256)byteExample.Length;
    //
    //     long gasLeft = blockGasConsumptionTarget - GasCostOf.Transaction;
    //     // long gasCost = GasCostOf.Sha3 + GasCostOf.Sha3Word * EvmPooledMemory.Div32Ceiling(in length);
    //     // long iterations = (blockGasConsumptionTarget - GasCostOf.Transaction) / gasCost;
    //
    //     byte i = 0;
    //     long dataCost = 0;
    //     for (int j = 0; j < 4455; j++)
    //     {
    //         List<byte> iterationCode = new();
    //         var data = i++;
    //         // int zeroData = data.AsSpan().CountZeros();
    //
    //         long gasCost = 0;
    //         // GasCostOf.Sha3 + GasCostOf.Sha3Word * EvmPooledMemory.Div32Ceiling(in length) + zeroData * GasCostOf.TxDataZero + (data.Length - zeroData) * GasCostOf.TxDataNonZeroEip2028;
    //         // push value as source to compute hash
    //         iterationCode.Add((byte)(Instruction.PUSH1));
    //         gasCost += GasCostOf.VeryLow;
    //         iterationCode.Add(data);
    //
    //         // gasCost += zeroData * GasCostOf.TxDataZero + (data.Length - zeroData) * GasCostOf.TxDataNonZeroEip2028;
    //         // push memory position - 0
    //         iterationCode.Add((byte)(Instruction.PUSH1));
    //         gasCost += GasCostOf.VeryLow;
    //         iterationCode.AddRange(new[] { Byte.MinValue });
    //         // gasCost += GasCostOf.TxDataZero;
    //         // save in memory
    //         iterationCode.Add((byte)Instruction.MSTORE);
    //         gasCost += GasCostOf.Memory;
    //
    //         // push byte size to read from memory - 1
    //         iterationCode.Add((byte)(Instruction.PUSH1));
    //         gasCost += GasCostOf.VeryLow;
    //         iterationCode.AddRange(new[] { (byte)1 });
    //         // gasCost += GasCostOf.TxDataNonZeroEip2028;
    //         // push byte offset in memory - 0
    //         iterationCode.Add((byte)(Instruction.PUSH1));
    //         gasCost += GasCostOf.VeryLow;
    //         iterationCode.AddRange(new[] { Byte.MinValue });
    //         // gasCost += GasCostOf.TxDataZero;
    //         // compute keccak
    //         iterationCode.Add((byte)Instruction.KECCAK256);
    //         gasCost += GasCostOf.Sha3 + GasCostOf.Sha3Word * EvmPooledMemory.Div32Ceiling(1);
    //
    //         //remove from stack
    //         iterationCode.Add((byte)(Instruction.POP));
    //         gasCost += GasCostOf.Base;
    //
    //         byteCode.AddRange(iterationCode);
    //
    //         int zeroData = iterationCode.ToArray().AsSpan().CountZeros();
    //
    //         gasCost += zeroData * GasCostOf.TxDataZero + (iterationCode.Count - zeroData) * GasCostOf.TxDataNonZeroEip2028;
    //
    //         dataCost += zeroData * GasCostOf.TxDataZero + (iterationCode.Count - zeroData) * GasCostOf.TxDataNonZeroEip2028;
    //
    //         gasLeft -= gasCost;
    //
    //         // now keccak of given data is in memory
    //     }
    //
    //     return byteCode.ToArray();
    // }

    private byte[] PrepareKeccak256Code(long blockGasConsumptionTarget, int bytesToComputeKeccak)
    {
        List<byte> byteCode = new();

        long gasLeft = blockGasConsumptionTarget - GasCostOf.Transaction;
        // long gasCost = GasCostOf.Sha3 + GasCostOf.Sha3Word * EvmPooledMemory.Div32Ceiling(in length);
        // long iterations = (blockGasConsumptionTarget - GasCostOf.Transaction) / gasCost;

        long gasCost = 0;
        long dataCost = 0;

        List<byte> preIterationCode = new();

        preIterationCode.Add((byte)(Instruction.PUSH1));
        preIterationCode.Add(0x00);
        gasCost += GasCostOf.VeryLow;

        List<byte> iterationCode = new();
        long gasCostPerIteration = 0;

        // push memory position - 0
        iterationCode.Add((byte)(Instruction.PUSH1));
        gasCostPerIteration += GasCostOf.VeryLow;
        iterationCode.AddRange(new[] { Byte.MinValue });
        // save in memory
        iterationCode.Add((byte)Instruction.MSTORE);
        gasCostPerIteration += GasCostOf.Memory;

        // push byte size to read from memory - bytesToComputeKeccak
        iterationCode.Add((byte)(Instruction.PUSH1));
        gasCostPerIteration += GasCostOf.VeryLow;
        iterationCode.AddRange(new[] { (byte)bytesToComputeKeccak });
        // push byte offset in memory - 0
        iterationCode.Add((byte)(Instruction.PUSH1));
        gasCostPerIteration += GasCostOf.VeryLow;
        iterationCode.AddRange(new[] { Byte.MinValue });
        // compute keccak
        iterationCode.Add((byte)Instruction.KECCAK256);
        gasCostPerIteration += GasCostOf.Sha3 + GasCostOf.Sha3Word * EvmPooledMemory.Div32Ceiling(1);

        // now keccak of given data is on top of stack


        // int zeroData = iterationCode.ToArray().AsSpan().CountZeros();
        //
        // dataCost += zeroData * GasCostOf.TxDataZero + (iterationCode.Count - zeroData) * GasCostOf.TxDataNonZeroEip2028;
        // gasCost += dataCost;


        gasLeft -= gasCost;

        long iterations = (_chainSpecBasedSpecProvider.GenesisSpec.MaxInitCodeSize - preIterationCode.Count) / iterationCode.Count;

        byteCode.AddRange(preIterationCode);

        for (int i = 0; i < iterations; i++)
        {
            byteCode.AddRange(iterationCode);
            gasCost += gasCostPerIteration;
        }

        int zeroData = byteCode.ToArray().AsSpan().CountZeros();
        dataCost += zeroData * GasCostOf.TxDataZero + (byteCode.Count - zeroData) * GasCostOf.TxDataNonZeroEip2028;

        gasCost += dataCost;

        return byteCode.ToArray();
    }

    private IEnumerable<Withdrawal> GetBlockWithdrawals(int alreadyProducedBlocks, PrivateKey[] privateKeys)
    {
        if (alreadyProducedBlocks * _maxNumberOfWithdrawalsPerBlock >= _numberOfWithdrawals) yield break;

        for (int i = 0; i < _maxNumberOfWithdrawalsPerBlock; i++)
        {
            int currentPrivateKeyIndex = alreadyProducedBlocks * _maxNumberOfWithdrawalsPerBlock + i;

            yield return new Withdrawal
            {
                Address = privateKeys[currentPrivateKeyIndex].Address,
                AmountInGwei = 1_000_000_000_000, // 1000 eth
                ValidatorIndex = (ulong)(currentPrivateKeyIndex + 1),
                Index = (ulong)(i % 16 + 1)
            };
        }
    }

    private IEnumerable<PrivateKey> PreparePrivateKeys(int numberOfKeysToGenerate)
    {
        int numberOfKeys = 0;
        for (byte i = 1; i > 0; i++)
        {
            for (byte j = 1; j > 0; j++)
            {
                for (byte k = 1; k > 0; k++)
                {
                    if (numberOfKeys++ >= numberOfKeysToGenerate)
                    {
                        yield break;
                    }

                    byte[] bytes = new byte[32];
                    bytes[29] = i;
                    bytes[30] = j;
                    bytes[31] = k;
                    yield return new PrivateKey(bytes);
                }
            }
        }
    }

    // private IEnumerable<Withdrawal> PrepareWithdrawals(PrivateKey[] privateKeys)
    // {
    //     for (int i = 0; i < _numberOfWithdrawals; i++)
    //     {
    //         yield return new Withdrawal
    //         {
    //             Address = privateKeys[i].Address,
    //             AmountInGwei = 1_000_000_000_000, // 1000 eth
    //             ValidatorIndex = (ulong)(i + 1),
    //             Index = (ulong)(i % 16 + 1)
    //         };
    //     }
    // }

    private void WriteJsonRpcRequest(StringBuilder stringBuilder, string methodName, params  string[]? parameters)
    {
        stringBuilder.Append($"{{\"jsonrpc\":\"2.0\",\"method\":\"{methodName}\",");

        if (parameters is not null)
        {
            stringBuilder.Append($"\"params\":[");
            for(int i = 0; i < parameters.Length; i++)
            {
                stringBuilder.Append(parameters[i]);
                if (i + 1 < parameters.Length) stringBuilder.Append(",");
            }
            stringBuilder.Append($"],");
        }

        stringBuilder.Append("\"id\":67}");
        stringBuilder.AppendLine();
    }

}
