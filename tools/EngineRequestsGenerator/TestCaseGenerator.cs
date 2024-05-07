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
    private const int _blockGasConsumptionTarget = 30_000_000;

    private string _chainSpecPath;
    private ChainSpec _chainSpec;
    private ChainSpecBasedSpecProvider _chainSpecBasedSpecProvider;
    private EthereumJsonSerializer _serializer = new();
    private TestCase _testCase;
    private readonly string _outputPath;
    private TaskCompletionSource<bool>? _taskCompletionSource;
    private Task WaitForProcessingBlock => _taskCompletionSource?.Task ?? Task.CompletedTask;

    public TestCaseGenerator(
        string chainSpecPath,
        TestCase testCase,
        string outputPath)
    {
        _maxNumberOfWithdrawalsPerBlock = 16;
        _numberOfWithdrawals = 1600;
        _chainSpecPath = chainSpecPath;
        _testCase = testCase;
        _outputPath = outputPath;

        _numberOfBlocksToProduce = _testCase switch
        {
            TestCase.Warmup => 1000,
            TestCase.Transfers => 2,
            TestCase.TxDataZero => 2,
            _ => 3
        };
    }
    public async Task Generate()
    {
        bool generateSingleFile = false;
        if (generateSingleFile)
        {
            await GenerateTestCase(_blockGasConsumptionTarget);
            Console.WriteLine($"generated testcase {_blockGasConsumptionTarget}");
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
        _txsPerBlock = _testCase switch
        {
            TestCase.Transfers => (int)blockGasConsumptionTarget / (int)GasCostOf.Transaction,
            _ => 1
        };

        // chain initialization
        StringBuilder stringBuilder = new();
        ChainSpecLoader chainSpecLoader = new(_serializer);
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

            switch (_testCase)
            {
                case TestCase.Transfers:
                case TestCase.Warmup:
                case TestCase.TxDataZero:
                case TestCase.SHA2From32Bytes:
                    SubmitTxs(chain.TxPool, privateKeys, previousBlock.Withdrawals, _testCase, blockGasConsumptionTarget);
                    break;
                // cases with contract deployment:
                case TestCase.Keccak256From1Byte:
                case TestCase.Keccak256From8Bytes:
                case TestCase.Keccak256From32Bytes:
                    if (i < 2)
                    {
                        // in iteration 0 there is only withdrawal,
                        // in iteration 1 there is only contract deployment
                        SubmitTxs(chain.TxPool, privateKeys, previousBlock.Withdrawals, _testCase, blockGasConsumptionTarget);
                    }
                    else
                    {
                        // starting from in iteration 2, there are contract calls
                        CallContract(chain, privateKeys[previousBlock.Withdrawals.FirstOrDefault().ValidatorIndex - 1], blockGasConsumptionTarget);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(_testCase), _testCase, null);
            }

            chain.PayloadPreparationService!.StartPreparingPayload(previousBlock.Header, payloadAttributes);
            Block block = chain.PayloadPreparationService!.GetPayload(payloadAttributes.GetPayloadId(previousBlock.Header)).Result!.CurrentBestBlock!;

            Console.WriteLine($"testcase {blockGasConsumptionTarget} gasUsed: {block.GasUsed}");


            ExecutionPayloadV3 executionPayload = new(block);
            string executionPayloadString = _serializer.Serialize(executionPayload);
            string blobsString = _serializer.Serialize(Array.Empty<byte[]>());
            string parentBeaconBlockRootString = _serializer.Serialize(previousBlock.Hash);

            WriteJsonRpcRequest(stringBuilder, "engine_newPayloadV3", executionPayloadString, blobsString, parentBeaconBlockRootString);

            ForkchoiceStateV1 forkchoiceState = new(block.Hash, Keccak.Zero, Keccak.Zero);
            WriteJsonRpcRequest(stringBuilder, "engine_forkchoiceUpdatedV3", _serializer.Serialize(forkchoiceState));

            if (block.Number < _numberOfBlocksToProduce)
            {
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
        }

        if (!Directory.Exists(_outputPath))
            Directory.CreateDirectory(_outputPath);
        await File.WriteAllTextAsync($"{_outputPath}/{_testCase}_{blockGasConsumptionTarget/1_000_000}M.txt", stringBuilder.ToString());
    }

    private void OnEmptyProcessingQueue(object? sender, EventArgs e)
    {
        _taskCompletionSource?.SetResult(true);
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
                    .WithData(PrepareKeccak256Code(1))
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
                    .WithData(PrepareKeccak256Code(8))
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
                    .WithData(PrepareKeccak256Code(32))
                    .WithGasLimit(blockGasConsumptionTarget)
                    .SignedAndResolved(privateKey)
                    .TestObject;
            // case TestCase.SHA2From32Bytes:
            //     return Build.A.Transaction
            //         .WithNonce((UInt256)nonce)
            //         .WithType(TxType.EIP1559)
            //         .WithMaxFeePerGas(1.GWei())
            //         .WithMaxPriorityFeePerGas(1.GWei())
            //         .WithTo(null)
            //         .WithChainId(BlockchainIds.Holesky)
            //         .WithData(PrepareKeccak256Code(32))
            //         .WithGasLimit(blockGasConsumptionTarget)
            //         .SignedAndResolved(privateKey)
            //         .TestObject;
            default:
                throw new ArgumentOutOfRangeException(nameof(testCase), testCase, null);
        }
    }

    private void CallContract(EngineModuleTests.MergeTestBlockchain chain, PrivateKey privateKey, long blockGasConsumptionTarget)
    {
        Transaction tx = Build.A.Transaction
            .WithNonce(UInt256.Zero)
            .WithType(TxType.EIP1559)
            .WithMaxFeePerGas(1.GWei())
            .WithMaxPriorityFeePerGas(1.GWei())
            .WithTo(new Address("0x7dd5df5a938ecb3acafaa0e026b235d100f71bbf"))
            .WithChainId(BlockchainIds.Holesky)
            .WithGasLimit(blockGasConsumptionTarget)
            .SignedAndResolved(privateKey)
            .TestObject;;
        chain.TxPool.SubmitTx(tx, TxHandlingOptions.None).Should().Be(AcceptTxResult.Accepted);
    }

    private byte[] PrepareKeccak256Code(int bytesToComputeKeccak)
    {
        List<byte> oneIteration = new();
        // long costOfOneIteration = 0;

        oneIteration.Add((byte)Instruction.PUSH0);
        // costOfOneIteration += GasCostOf.Base;
        oneIteration.Add((byte)Instruction.MSTORE);
        // costOfOneIteration += GasCostOf.Memory;
        oneIteration.Add((byte)Instruction.PUSH1);
        oneIteration.Add((byte)bytesToComputeKeccak);
        // costOfOneIteration += GasCostOf.VeryLow;
        oneIteration.Add((byte)Instruction.PUSH0);
        // costOfOneIteration += GasCostOf.Base;
        oneIteration.Add((byte)Instruction.KECCAK256);
        // costOfOneIteration += GasCostOf.Call;

        List<byte> codeToDeploy = new();
        // long cost = 0;

        codeToDeploy.Add((byte)Instruction.CALLER);     // first, preitaration item - put on stack callers address
        // cost += GasCostOf.Base;
        codeToDeploy.Add((byte)Instruction.JUMPDEST);   // second item - jump destination (on offset 1)
        // cost += GasCostOf.JumpDest;

        for (int i = 0; i < 4095; i++)
        {
            codeToDeploy.AddRange(oneIteration);
            // cost += costOfOneIteration;
        }

        codeToDeploy.Add((byte)Instruction.PUSH1);
        codeToDeploy.Add((byte)1);
        // cost += GasCostOf.VeryLow;
        codeToDeploy.Add((byte)Instruction.JUMP);
        // cost += GasCostOf.Mid;

        List<byte> initCode = GenerateInitCode(codeToDeploy);
        List<byte> byteCode = GenerateCodeToDeployContract(initCode);
        return byteCode.ToArray();
    }

    private List<byte> GenerateCodeToDeployContract(List<byte> initCode)
    {
        List<byte> byteCode = new();

        for (long i = 0; i < initCode.Count; i += 32)
        {
            List<byte> currentWord = i == 0
                ? initCode.Slice(0, initCode.Count % 32)
                : initCode.Slice((int)i - 32 + initCode.Count % 32, 32);
            byteCode.Add((byte)(Instruction.PUSH1 + (byte)currentWord.Count - 1));
            byteCode.AddRange(currentWord);

            // push memory offset - i
            byte[] memoryOffset = i.ToBigEndianByteArrayWithoutLeadingZeros();
            if (memoryOffset is [0])
            {
                byteCode.Add((byte)Instruction.PUSH0);
            }
            else
            {
                byteCode.Add((byte)(Instruction.PUSH1 + (byte)memoryOffset.Length - 1));
                byteCode.AddRange(memoryOffset);
            }

            // save in memory
            byteCode.Add((byte)Instruction.MSTORE);
        }

        // push size of init code to read from memory
        byte[] sizeOfInitCode = initCode.Count.ToByteArray().WithoutLeadingZeros().ToArray();
        byteCode.Add((byte)(Instruction.PUSH1 + (byte)sizeOfInitCode.Length - 1));
        byteCode.AddRange(sizeOfInitCode);

        // offset in memory
        byteCode.Add((byte)(Instruction.PUSH1));
        byteCode.AddRange(new[] { (byte)(32 - (initCode.Count % 32)) });

        // 0 wei to send
        byteCode.Add((byte)Instruction.PUSH0);

        byteCode.Add((byte)Instruction.CREATE);

        Console.WriteLine($"size of prepared code: {byteCode.Count}");

        return byteCode;
    }

    private List<byte> GenerateInitCode(List<byte> codeToDeploy)
    {
        List<byte> initCode = new();

        for (long i = 0; i < codeToDeploy.Count; i += 32)
        {
            List<byte> currentWord = i == 0
                ? codeToDeploy.Slice(0, codeToDeploy.Count % 32)
                : codeToDeploy.Slice((int)i - 32 + codeToDeploy.Count % 32, 32);

            initCode.Add((byte)(Instruction.PUSH1 + (byte)currentWord.Count - 1));
            initCode.AddRange(currentWord);

            // push memory offset - i
            byte[] memoryOffset = i.ToBigEndianByteArrayWithoutLeadingZeros();
            if (memoryOffset is [0])
            {
                initCode.Add((byte)Instruction.PUSH0);
            }
            else
            {
                initCode.Add((byte)(Instruction.PUSH1 + (byte)memoryOffset.Length - 1));
                initCode.AddRange(memoryOffset);
            }

            // save in memory
            initCode.Add((byte)Instruction.MSTORE);
        }

        // push size of memory read
        byte[] sizeOfCodeToDeploy = codeToDeploy.Count.ToByteArray().WithoutLeadingZeros().ToArray();
        initCode.Add((byte)(Instruction.PUSH1 + (byte)sizeOfCodeToDeploy.Length - 1));
        initCode.AddRange(sizeOfCodeToDeploy);

        // push memory offset
        initCode.Add((byte)(Instruction.PUSH1));
        initCode.AddRange(new[] { (byte)(32 - (codeToDeploy.Count % 32)) });

        // add return opcode
        initCode.Add((byte)(Instruction.RETURN));

        return initCode;
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