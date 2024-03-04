// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Nethermind.Consensus.Validators;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Core.Specs;
using Nethermind.Crypto;
using Nethermind.Db;
using Nethermind.Int256;
using Nethermind.Evm;
using Nethermind.Evm.Tracing;
using Nethermind.Evm.TransactionProcessing;
using Nethermind.Logging;
using Nethermind.Paprika;
using Nethermind.Specs;
using Nethermind.Specs.Forks;
using Nethermind.Specs.Test;
using Nethermind.State;
using NUnit.Framework;

namespace Ethereum.Test.Base
{
    public abstract class GeneralStateTestBase
    {
        private static ILogger _logger = new(new ConsoleAsyncLogger(LogLevel.Info));
        private static ILogManager _logManager = LimboLogs.Instance;
        private static UInt256 _defaultBaseFeeForStateTest = 0xA;

        [SetUp]
        public void Setup()
        {
        }

        protected void Setup(ILogManager logManager)
        {
            _logManager = logManager ?? LimboLogs.Instance;
            _logger = _logManager.GetClassLogger();
        }

        protected EthereumTestResult RunTest(GeneralStateTest test)
        {
            return RunTest(test, NullTxTracer.Instance).Result;
        }

        protected async Task<EthereumTestResult> RunTest(GeneralStateTest test, ITxTracer txTracer)
        {
            TestContext.Write($"Running {test.Name} at {DateTime.UtcNow:HH:mm:ss.ffffff}");
            Assert.IsNull(test.LoadFailure, "test data loading failure");

            await using PaprikaStateFactory stateDb = new();
            IDb codeDb = new MemDb();

            ISpecProvider specProvider = new CustomSpecProvider(
                ((ForkActivation)0, Frontier.Instance), // TODO: this thing took a lot of time to find after it was removed!, genesis block is always initialized with Frontier
                ((ForkActivation)1, test.Fork));

            if (specProvider.GenesisSpec != Frontier.Instance)
            {
                Assert.Fail("Expected genesis spec to be Frontier for blockchain tests");
            }

            WorldState stateProvider = new(stateDb, codeDb, _logManager);
            IBlockhashProvider blockhashProvider = new TestBlockhashProvider();
            IVirtualMachine virtualMachine = new VirtualMachine(
                blockhashProvider,
                specProvider,
                _logManager);

            TransactionProcessor transactionProcessor = new(
                specProvider,
                stateProvider,
                virtualMachine,
                _logManager);

            InitializeTestState(test, stateProvider, specProvider);

            BlockHeader header = new(
                test.PreviousHash,
                Keccak.OfAnEmptySequenceRlp,
                test.CurrentCoinbase,
                test.CurrentDifficulty,
                test.CurrentNumber,
                test.CurrentGasLimit,
                test.CurrentTimestamp,
                Array.Empty<byte>());
            header.BaseFeePerGas = test.Fork.IsEip1559Enabled ? test.CurrentBaseFee ?? _defaultBaseFeeForStateTest : UInt256.Zero;
            header.StateRoot = test.PostHash;
            header.Hash = header.CalculateHash();
            header.IsPostMerge = test.CurrentRandom is not null;
            header.MixHash = test.CurrentRandom;
            header.WithdrawalsRoot = test.CurrentWithdrawalsRoot;
            header.ParentBeaconBlockRoot = test.CurrentBeaconRoot;
            header.ExcessBlobGas = 0;

            Stopwatch stopwatch = Stopwatch.StartNew();
            TxValidator? txValidator = new((MainnetSpecProvider.Instance.ChainId));
            IReleaseSpec? spec = specProvider.GetSpec((ForkActivation)test.CurrentNumber);
            if (test.Transaction.ChainId == null)
                test.Transaction.ChainId = MainnetSpecProvider.Instance.ChainId;
            if (test.ParentBlobGasUsed is not null && test.ParentExcessBlobGas is not null)
            {
                BlockHeader parent = new(
                    parentHash: Keccak.Zero,
                    unclesHash: Keccak.OfAnEmptySequenceRlp,
                    beneficiary: test.CurrentCoinbase,
                    difficulty: test.CurrentDifficulty,
                    number: test.CurrentNumber - 1,
                    gasLimit: test.CurrentGasLimit,
                    timestamp: test.CurrentTimestamp,
                    extraData: Array.Empty<byte>()
                )
                {
                    BlobGasUsed = (ulong)test.ParentBlobGasUsed,
                    ExcessBlobGas = (ulong)test.ParentExcessBlobGas,
                };
                header.ExcessBlobGas = BlobGasCalculator.CalculateExcessBlobGas(parent, spec);
            }
            bool isValid = txValidator.IsWellFormed(test.Transaction, spec);
            if (isValid)
                transactionProcessor.Execute(test.Transaction, new BlockExecutionContext(header), txTracer);
            stopwatch.Stop();

            stateProvider.Commit(specProvider.GenesisSpec);
            stateProvider.CommitTree(1);

            // '@winsvega added a 0-wei reward to the miner , so we had to add that into the state test execution phase. He needed it for retesteth.'
            if (!stateProvider.AccountExists(test.CurrentCoinbase))
            {
                stateProvider.CreateAccount(test.CurrentCoinbase, 0);
            }
            stateProvider.Commit(specProvider.GetSpec((ForkActivation)1));

            // TODO
            // stateProvider.RecalculateStateRoot();

            List<string> differences = RunAssertions(test, stateProvider);
            EthereumTestResult testResult = new(test.Name, test.ForkName, differences.Count == 0);
            testResult.TimeInMs = stopwatch.Elapsed.TotalMilliseconds;
            testResult.StateRoot = stateProvider.StateRoot;

            //            Assert.Zero(differences.Count, "differences");
            return testResult;
        }

        private static void InitializeTestState(GeneralStateTest test, WorldState stateProvider, ISpecProvider specProvider)
        {
            foreach (KeyValuePair<Address, AccountState> accountState in test.Pre)
            {
                foreach (KeyValuePair<UInt256, byte[]> storageItem in accountState.Value.Storage)
                {
                    stateProvider.Set(new StorageCell(accountState.Key, storageItem.Key),
                        storageItem.Value.ToEvmWord());
                }

                stateProvider.CreateAccount(accountState.Key, accountState.Value.Balance);
                stateProvider.InsertCode(accountState.Key, accountState.Value.Code, specProvider.GenesisSpec);
                stateProvider.SetNonce(accountState.Key, accountState.Value.Nonce);
            }

            stateProvider.Commit(specProvider.GenesisSpec);
            stateProvider.CommitTree(0);
            stateProvider.Reset();
        }

        private List<string> RunAssertions(GeneralStateTest test, IWorldState stateProvider)
        {
            List<string> differences = new();
            if (test.PostHash != stateProvider.StateRoot)
            {
                differences.Add($"STATE ROOT exp: {test.PostHash}, actual: {stateProvider.StateRoot}");
            }

            foreach (string difference in differences)
            {
                _logger.Info(difference);
            }

            return differences;
        }
    }
}
