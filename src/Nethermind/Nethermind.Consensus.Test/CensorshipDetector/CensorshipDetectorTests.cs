// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Consensus.Processing;
using Nethermind.TxPool;
using Nethermind.Logging;
using NSubstitute;
using NUnit.Framework;
using Nethermind.Blockchain;
using Nethermind.Core;
using System.Collections.Generic;
using Nethermind.Core.Specs;
using Nethermind.Consensus.Comparers;
using Nethermind.Specs;
using Nethermind.Consensus.Validators;
using Nethermind.Crypto;
using Nethermind.State;
using Nethermind.Trie.Pruning;
using Nethermind.Db;
using FluentAssertions;
using Nethermind.Core.Test.Builders;
using Nethermind.Core.Extensions;
using Nethermind.Specs.Forks;
using Nethermind.Core.Crypto;
using System.Linq;
using Nethermind.Consensus.Processing.CensorshipDetector;

namespace Nethermind.Consensus.Test;

[TestFixture]
public class CensorshipDetectorTests
{
    private ILogManager _logManager;
    private WorldState _stateProvider;
    private IBlockProcessor _blockProcessor;
    private ISpecProvider _specProvider;
    private IEthereumEcdsa _ethereumEcdsa;
    private IComparer<Transaction> _comparer;
    private TxPool.TxPool _txPool;
    private CensorshipDetector _censorshipDetector;

    [SetUp]
    public void Setup()
    {
        _logManager = LimboLogs.Instance;
        TrieStore trieStore = new(new MemDb(), _logManager);
        MemDb codeDb = new();
        _stateProvider = new WorldState(trieStore, codeDb, _logManager);
        _blockProcessor = Substitute.For<IBlockProcessor>();
    }

    [TearDown]
    public void TearDown()
    {
        _txPool.Dispose();
        _censorshipDetector.Dispose();
    }

    // Address Censorship is given to be false here since censorship is not being detected for any address.
    [Test]
    public void Censorship_when_address_censorship_is_false_and_high_paying_tx_censorship_is_true_for_all_blocks_in_main_cache()
    {
        Processing.CensorshipDetector.Metrics.PoolCensorshipDetectionUniqueAddressesCount = 0;
        _txPool = CreatePool();
        _censorshipDetector = new(_txPool, _comparer, _blockProcessor, _logManager, new CensorshipDetectorConfig() { });

        Transaction tx1 = SubmitTxToPool(1, TestItem.PrivateKeyA, TestItem.AddressA);
        Transaction tx2 = SubmitTxToPool(2, TestItem.PrivateKeyB, TestItem.AddressA);
        Transaction tx3 = SubmitTxToPool(3, TestItem.PrivateKeyC, TestItem.AddressA);
        Transaction tx4 = SubmitTxToPool(4, TestItem.PrivateKeyD, TestItem.AddressA);
        Transaction tx5 = SubmitTxToPool(5, TestItem.PrivateKeyE, TestItem.AddressA);

        Block block1 = Build.A.Block.WithNumber(1).WithTransactions([tx4]).WithParentHash(TestItem.KeccakA).TestObject;
        Hash256 blockHash1 = block1.Hash!;
        BlockProcessingWorkflow(block1);

        Block block2 = Build.A.Block.WithNumber(2).WithTransactions([tx3]).WithParentHash(blockHash1).TestObject;
        Hash256 blockHash2 = block2.Hash!;
        BlockProcessingWorkflow(block2);

        Block block3 = Build.A.Block.WithNumber(3).WithTransactions([tx2]).WithParentHash(blockHash2).TestObject;
        Hash256 blockHash3 = block3.Hash!;
        BlockProcessingWorkflow(block3);

        Block block4 = Build.A.Block.WithNumber(4).WithTransactions([tx1]).WithParentHash(blockHash3).TestObject;
        BlockProcessingWorkflow(block4);

        Assert.That(() => _censorshipDetector.GetCensoredBlocks().Contains(new BlockNumberHash(block4)), Is.EqualTo(true).After(10, 1));
    }

    // Address Censorship is given to be false here since censorship is not being detected for any address.
    [Test]
    public void No_censorship_when_address_censorship_is_false_and_high_paying_tx_censorship_is_false_for_some_blocks_in_main_cache()
    {
        Processing.CensorshipDetector.Metrics.PoolCensorshipDetectionUniqueAddressesCount = 0;
        _txPool = CreatePool();
        _censorshipDetector = new(_txPool, _comparer, _blockProcessor, _logManager, new CensorshipDetectorConfig() { });

        Transaction tx1 = SubmitTxToPool(1, TestItem.PrivateKeyA, TestItem.AddressA);
        Transaction tx2 = SubmitTxToPool(2, TestItem.PrivateKeyB, TestItem.AddressA);
        Transaction tx3 = SubmitTxToPool(3, TestItem.PrivateKeyC, TestItem.AddressA);
        Transaction tx4 = SubmitTxToPool(4, TestItem.PrivateKeyD, TestItem.AddressA);
        Transaction tx5 = SubmitTxToPool(5, TestItem.PrivateKeyE, TestItem.AddressA);

        Block block1 = Build.A.Block.WithNumber(1).WithTransactions([tx4]).WithParentHash(TestItem.KeccakA).TestObject;
        Hash256 blockHash1 = block1.Hash!;
        BlockProcessingWorkflow(block1);

        Block block2 = Build.A.Block.WithNumber(2).WithTransactions([tx3, tx5]).WithParentHash(blockHash1).TestObject;
        Hash256 blockHash2 = block2.Hash!;
        BlockProcessingWorkflow(block2);

        Block block3 = Build.A.Block.WithNumber(3).WithTransactions([tx2]).WithParentHash(blockHash2).TestObject;
        Hash256 blockHash3 = block3.Hash!;
        BlockProcessingWorkflow(block3);

        Block block4 = Build.A.Block.WithNumber(4).WithTransactions([tx1]).WithParentHash(blockHash3).TestObject;
        BlockProcessingWorkflow(block4);

        Assert.That(() => _censorshipDetector.GetCensoredBlocks().Contains(new BlockNumberHash(block4)), Is.EqualTo(false).After(10, 1));
    }

    [Test]
    public void Unique_addresses_count_is_calculated_correctly()
    {
        Processing.CensorshipDetector.Metrics.PoolCensorshipDetectionUniqueAddressesCount = 0;
        _txPool = CreatePool();
        _censorshipDetector = new(
        _txPool,
        _comparer,
        _blockProcessor,
        _logManager,
        new CensorshipDetectorConfig()
        {
            AddressesForCensorshipDetection = [
            TestItem.AddressA.ToString(),
            TestItem.AddressB.ToString(),
            TestItem.AddressC.ToString()]
        });

        Transaction tx = SubmitTxToPool(1, TestItem.PrivateKeyE, TestItem.AddressD);
        Assert.That(() => Processing.CensorshipDetector.Metrics.PoolCensorshipDetectionUniqueAddressesCount, Is.EqualTo(0).After(10, 1));

        Transaction tx1 = SubmitTxToPool(1, TestItem.PrivateKeyA, TestItem.AddressA);
        Assert.That(() => Processing.CensorshipDetector.Metrics.PoolCensorshipDetectionUniqueAddressesCount, Is.EqualTo(1).After(10, 1));

        Transaction tx2 = SubmitTxToPool(2, TestItem.PrivateKeyB, TestItem.AddressB);
        Assert.That(() => Processing.CensorshipDetector.Metrics.PoolCensorshipDetectionUniqueAddressesCount, Is.EqualTo(2).After(10, 1));

        Transaction tx3 = SubmitTxToPool(3, TestItem.PrivateKeyC, TestItem.AddressC);
        Assert.That(() => Processing.CensorshipDetector.Metrics.PoolCensorshipDetectionUniqueAddressesCount, Is.EqualTo(3).After(10, 1));

        Transaction tx4 = SubmitTxToPool(4, TestItem.PrivateKeyD, TestItem.AddressC);

        Assert.That(() => Processing.CensorshipDetector.Metrics.PoolCensorshipDetectionUniqueAddressesCount, Is.EqualTo(3).After(10, 1));
        _txPool.RemoveTransaction(tx1.Hash);
        Assert.That(() => Processing.CensorshipDetector.Metrics.PoolCensorshipDetectionUniqueAddressesCount, Is.EqualTo(2).After(10, 1));
        _txPool.RemoveTransaction(tx2.Hash);
        Assert.That(() => Processing.CensorshipDetector.Metrics.PoolCensorshipDetectionUniqueAddressesCount, Is.EqualTo(1).After(10, 1));
        _txPool.RemoveTransaction(tx3.Hash);
        Assert.That(() => Processing.CensorshipDetector.Metrics.PoolCensorshipDetectionUniqueAddressesCount, Is.EqualTo(1).After(10, 1));
        _txPool.RemoveTransaction(tx4.Hash);
        Assert.That(() => Processing.CensorshipDetector.Metrics.PoolCensorshipDetectionUniqueAddressesCount, Is.EqualTo(0).After(10, 1));
    }

    // High-Paying Tx Censorship is given to be false here.
    [Test]
    public void Censorship_when_high_paying_tx_censorship_is_false_and_address_censorship_is_true_for_all_blocks_in_main_cache()
    {
        Processing.CensorshipDetector.Metrics.PoolCensorshipDetectionUniqueAddressesCount = 0;
        _txPool = CreatePool();
        _censorshipDetector = new(
        _txPool,
        _comparer,
        _blockProcessor,
        _logManager,
        new CensorshipDetectorConfig()
        {
            AddressesForCensorshipDetection = [
            TestItem.AddressA.ToString(),
            TestItem.AddressB.ToString(),
            TestItem.AddressC.ToString(),
            TestItem.AddressD.ToString(),
            TestItem.AddressE.ToString(),
            TestItem.AddressF.ToString()]
        });

        Transaction tx1 = SubmitTxToPool(1, TestItem.PrivateKeyA, TestItem.AddressA);
        Transaction tx2 = SubmitTxToPool(2, TestItem.PrivateKeyB, TestItem.AddressB);
        Transaction tx3 = SubmitTxToPool(3, TestItem.PrivateKeyC, TestItem.AddressC);
        Transaction tx4 = SubmitTxToPool(4, TestItem.PrivateKeyD, TestItem.AddressD);
        Transaction tx5 = SubmitTxToPool(5, TestItem.PrivateKeyE, TestItem.AddressE);
        Transaction tx6 = SubmitTxToPool(6, TestItem.PrivateKeyF, TestItem.AddressF);

        Block block1 = Build.A.Block.WithNumber(1).WithTransactions([tx6]).WithParentHash(TestItem.KeccakA).TestObject;
        Hash256 blockHash1 = block1.Hash!;
        BlockProcessingWorkflow(block1);

        Block block2 = Build.A.Block.WithNumber(2).WithTransactions([tx5]).WithParentHash(blockHash1).TestObject;
        Hash256 blockHash2 = block2.Hash!;
        BlockProcessingWorkflow(block2);

        Block block3 = Build.A.Block.WithNumber(3).WithTransactions([tx4]).WithParentHash(blockHash2).TestObject;
        Hash256 blockHash3 = block3.Hash!;
        BlockProcessingWorkflow(block3);

        Block block4 = Build.A.Block.WithNumber(4).WithTransactions([tx3]).WithParentHash(blockHash3).TestObject;
        BlockProcessingWorkflow(block4);

        Assert.That(() => _censorshipDetector.GetCensoredBlocks().Contains(new BlockNumberHash(block4)), Is.EqualTo(true).After(10, 1));
    }

    // High-Paying Tx Censorship is given to be false here.
    [Test]
    public void No_censorship_when_high_paying_tx_censorship_is_false_and_address_censorship_is_false_for_some_blocks_in_main_cache()
    {
        Processing.CensorshipDetector.Metrics.PoolCensorshipDetectionUniqueAddressesCount = 0;
        _txPool = CreatePool();
        _censorshipDetector = new(
        _txPool,
        _comparer,
        _blockProcessor,
        _logManager,
        new CensorshipDetectorConfig()
        {
            AddressesForCensorshipDetection = [
            TestItem.AddressA.ToString(),
            TestItem.AddressB.ToString(),
            TestItem.AddressC.ToString(),
            TestItem.AddressD.ToString(),
            TestItem.AddressE.ToString()]
        });

        Transaction tx1 = SubmitTxToPool(1, TestItem.PrivateKeyA, TestItem.AddressA);
        Transaction tx2 = SubmitTxToPool(2, TestItem.PrivateKeyB, TestItem.AddressB);
        Transaction tx3 = SubmitTxToPool(3, TestItem.PrivateKeyC, TestItem.AddressC);
        Transaction tx4 = SubmitTxToPool(4, TestItem.PrivateKeyD, TestItem.AddressD);
        Transaction tx5 = SubmitTxToPool(5, TestItem.PrivateKeyE, TestItem.AddressE);

        Block block1 = Build.A.Block.WithNumber(1).WithTransactions([tx3, tx4, tx5]).WithParentHash(TestItem.KeccakA).TestObject;
        Hash256 blockHash1 = block1.Hash!;
        BlockProcessingWorkflow(block1);

        Transaction tx6 = SubmitTxToPool(6, TestItem.PrivateKeyC, TestItem.AddressC);
        Transaction tx7 = SubmitTxToPool(7, TestItem.PrivateKeyD, TestItem.AddressD);
        Transaction tx8 = SubmitTxToPool(8, TestItem.PrivateKeyE, TestItem.AddressE);

        Block block2 = Build.A.Block.WithNumber(2).WithTransactions([tx7, tx8]).WithParentHash(blockHash1).TestObject;
        Hash256 blockHash2 = block2.Hash!;
        BlockProcessingWorkflow(block2);

        Transaction tx9 = SubmitTxToPool(9, TestItem.PrivateKeyD, TestItem.AddressD);
        Transaction tx10 = SubmitTxToPool(10, TestItem.PrivateKeyE, TestItem.AddressE);

        Block block3 = Build.A.Block.WithNumber(3).WithTransactions([tx6, tx9, tx10]).WithParentHash(blockHash2).TestObject;
        Hash256 blockHash3 = block3.Hash!;
        BlockProcessingWorkflow(block3);

        Block block4 = Build.A.Block.WithNumber(4).WithTransactions([tx1, tx2]).WithParentHash(blockHash3).TestObject;
        BlockProcessingWorkflow(block4);

        Assert.That(() => _censorshipDetector.GetCensoredBlocks().Contains(new BlockNumberHash(block4)), Is.EqualTo(false).After(10, 1));
    }

    private TxPool.TxPool CreatePool(bool eip1559Enabled = true)
    {
        if (eip1559Enabled)
        {
            _specProvider = Substitute.For<ISpecProvider>();
            _specProvider.GetSpec(Arg.Any<BlockHeader>()).Returns(London.Instance);
        }
        else
        {
            _specProvider = MainnetSpecProvider.Instance;
        }

        IBlockTree blockTree = Substitute.For<IBlockTree>();
        blockTree.FindBestSuggestedHeader().Returns(Build.A.BlockHeader.WithNumber(1_000_000).TestObject);
        _ethereumEcdsa = new EthereumEcdsa(_specProvider.ChainId, _logManager);
        _comparer = new TransactionComparerProvider(_specProvider, blockTree).GetDefaultComparer();

        return new(
            _ethereumEcdsa,
            new BlobTxStorage(),
            new ChainHeadInfoProvider(_specProvider, blockTree, _stateProvider),
            new TxPoolConfig(),
            new TxValidator(_specProvider.ChainId),
            _logManager,
            _comparer);
    }

    private void BlockProcessingWorkflow(Block block)
    {
        _blockProcessor.BlockProcessing += Raise.EventWith(new BlockEventArgs(block));
        Assert.That(() => _censorshipDetector.BlockPotentiallyCensored(block.Number, block.Hash), Is.EqualTo(true).After(10, 1));

        foreach (Transaction tx in block.Transactions)
        {
            _txPool.RemoveTransaction(tx.Hash);
        }
    }

    private Transaction SubmitTxToPool(int maxPriorityFeePerGas, PrivateKey privateKey, Address address)
    {
        Transaction tx = Build.A.Transaction.
                        WithType(TxType.EIP1559).
                        WithMaxFeePerGas(10.Wei()).
                        WithMaxPriorityFeePerGas(maxPriorityFeePerGas.Wei()).
                        WithTo(address).
                        SignedAndResolved(_ethereumEcdsa, privateKey).
                        TestObject;
        _stateProvider.CreateAccount(tx.SenderAddress, 1_000_000.Wei());
        AcceptTxResult result = _txPool.SubmitTx(tx, TxHandlingOptions.PersistentBroadcast);
        result.Should().Be(AcceptTxResult.Accepted);
        return tx;
    }
}
