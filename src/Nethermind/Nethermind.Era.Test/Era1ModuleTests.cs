// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Buffers.Binary;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using FluentAssertions;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Era1;
using Nethermind.Blockchain.Receipts;
using Nethermind.Consensus.Processing;
using Nethermind.Consensus.Validators;
using Nethermind.Core;
using Nethermind.Core.Extensions;
using Nethermind.Core.Specs;
using Nethermind.Core.Test.Blockchain;
using Nethermind.Core.Test.Builders;
using Nethermind.Evm.Tracing;
using Nethermind.Int256;
using Nethermind.JsonRpc.Modules;
using Nethermind.Specs;
using Nethermind.Specs.ChainSpecStyle;
using NSubstitute;
using NUnit.Framework.Constraints;

namespace Nethermind.Era1.Test;
public class Era1ModuleTests
{
    [Test]
    public async Task ExportAndImportTwoBlocksAndReceipts()
    {
        using MemoryStream stream = new();
        using EraWriter builder = EraWriter.Create(stream, Substitute.For<ISpecProvider>());
        Block block0 = Build.A.Block
            .WithNumber(0)
            .WithTotalDifficulty(BlockHeaderBuilder.DefaultDifficulty)
            .WithTransactions(Build.A.Transaction.SignedAndResolved(TestItem.PrivateKeyA)
                                                 .WithSenderAddress(null)
                                                 .To(TestItem.GetRandomAddress()).TestObject)
            .TestObject;
        Block block1 = Build.A.Block
            .WithNumber(1)
            .WithTotalDifficulty(BlockHeaderBuilder.DefaultDifficulty)
            .WithTransactions(Build.A.Transaction.SignedAndResolved(TestItem.PrivateKeyB)
                                                 .WithSenderAddress(null)
                                                 .To(TestItem.GetRandomAddress()).TestObject).TestObject;
        TxReceipt receipt0 = Build.A.Receipt
            .WithAllFieldsFilled
            .TestObject;
        TxReceipt receipt1 = Build.A.Receipt
            .WithAllFieldsFilled
            .TestObject;

        await builder.Add(block0, new[] { receipt0 });
        await builder.Add(block1, new[] { receipt1 });
        await builder.Finalize();

        using EraReader reader = await EraReader.Create(stream);

        IAsyncEnumerator<(Block, TxReceipt[], UInt256)> enumerator = reader.GetAsyncEnumerator();
        await enumerator.MoveNextAsync();
        (Block importedBlock0, TxReceipt[] ImportedReceipts0, UInt256 td0) = enumerator.Current;
        importedBlock0.Header.TotalDifficulty = td0;
        await enumerator.MoveNextAsync();
        (Block importedBlock1, TxReceipt[] ImportedReceipts1, UInt256 td1) = enumerator.Current;
        importedBlock1.Header.TotalDifficulty = td1;
        await enumerator.DisposeAsync();

        importedBlock0.Should().BeEquivalentTo(block0);
        importedBlock1.Should().BeEquivalentTo(block1);

        ImportedReceipts0.Should().BeEquivalentTo(ImportedReceipts0);
        ImportedReceipts1.Should().BeEquivalentTo(ImportedReceipts1);

        Assert.That(td0, Is.EqualTo(BlockHeaderBuilder.DefaultDifficulty));
        Assert.That(td1, Is.EqualTo(BlockHeaderBuilder.DefaultDifficulty));
    }

    [TestCase("holesky")]
    [TestCase("mainnet")]
    public async Task ImportAndExportGethFiles(string network)
    {
        var eraFiles = EraPathUtils.GetAllEraFiles($"testdata/{network}", network);

        Assert.That(eraFiles.Count(), Is.GreaterThan(0));

        var specProvider = new ChainSpecBasedSpecProvider(new ChainSpec
        {
            SealEngineType = SealEngineType.BeaconChain,
            Parameters = new ChainParameters()
        });

        foreach (var era in eraFiles)
        {
            var readFromFile = new List<(Block b, TxReceipt[] r, UInt256 td)>();

            using var eraEnumerator = await EraReader.Create(era, new FileSystem(), default);
            using var destination = new MemoryStream();
            using var builder = EraWriter.Create(destination, specProvider);
            await foreach ((Block b, TxReceipt[] r, UInt256 td) in eraEnumerator)
            {
                await builder.Add(b, r, td);
                readFromFile.Add((b, r, td));
            }
            await builder.Finalize();

            using EraReader exportedToImported = await EraReader.Create(destination);
            int i = 0;
            await foreach ((Block b, TxReceipt[] r, UInt256 td) in exportedToImported)
            {
                Assert.That(i, Is.LessThan(readFromFile.Count()), "Exceeded the block count read from the file.");
                b.ToString(Block.Format.Full).Should().BeEquivalentTo(readFromFile[i].b.ToString(Block.Format.Full));
                r.Should().BeEquivalentTo(readFromFile[i].r);
                Assert.That(td, Is.EqualTo(readFromFile[i].td));
                i++;
            }
        }
    }

    [TestCase("geth")]
    public async Task VerifyAccumulatorsOnFiles(string dir)
    {
        var eraStore = new EraStore(dir, "mainnet", new FileSystem());

        ISpecProvider specProvider = Substitute.For<ISpecProvider>();
        IReceiptSpec receiptSpec = Substitute.For<IReleaseSpec>();

        receiptSpec.IsEip658Enabled.Returns(true);
        specProvider.GetSpec(Arg.Any<ForkActivation>()).Returns(receiptSpec);

        await eraStore.VerifyAll(specProvider, default);
    }

    [Test]
    public async Task CreateEraAndVerifyAccumulators()
    {
        TestBlockchain testBlockchain = await BasicTestBlockchain.Create();
        testBlockchain.State.AddToBalance(TestItem.AddressA, 10.Ether(), testBlockchain.SpecProvider.GenesisSpec);
        testBlockchain.State.RecalculateStateRoot();

        using MemoryStream stream = new();

        Block genesis = testBlockchain.BlockFinder.FindBlock(0)!;

        int numOfBlocks = 12;
        int numOfTx = 2;
        UInt256 nonce = 0;

        List<Block> blocks = [genesis];

        for (int i = 0; i < numOfBlocks; i++)
        {
            Transaction[] transactions = new Transaction[numOfTx];
            for (int y = 0; y < numOfTx; y++)
            {
                transactions[y] = Build.A.Transaction.WithTo(TestItem.GetRandomAddress())
                                                     .WithNonce(nonce)
                                                     .WithValue(TestContext.CurrentContext.Random.NextUInt(10))
                                                     .SignedAndResolved(TestItem.PrivateKeyA)
                                                     .TestObject;
                nonce++;
            }
            blocks.Add(Build.A.Block.WithUncles(Build.A.Block.TestObject)
                                    .WithBaseFeePerGas(1)
                                    .WithTotalDifficulty(blocks[i].TotalDifficulty + blocks[i].Difficulty)
                                    .WithTransactions(transactions)
                                    .WithParent(blocks[i]).TestObject);
        }

        testBlockchain.BlockProcessor.Process(genesis.StateRoot!, blocks, ProcessingOptions.NoValidation, new BlockReceiptsTracer());

        using EraWriter builder = EraWriter.Create(stream, Substitute.For<ISpecProvider>());

        foreach (var block in blocks)
        {
            await builder.Add(block, testBlockchain.ReceiptStorage.Get(block));
        }

        await builder.Finalize();

        using EraReader eraReader = await EraReader.Create(stream);

        var eraAccumulator = await eraReader.ReadAccumulator();

        Assert.That(await eraReader.VerifyAccumulator(eraAccumulator, Substitute.For<ISpecProvider>()), Is.True);
    }

    [Test]
    public async Task TestEraBuilderCreatesCorrectIndex()
    {
        BasicTestBlockchain testBlockchain = await BasicTestBlockchain.Create();
        using MemoryStream stream = new();
        using EraWriter builder = EraWriter.Create(stream, Substitute.For<ISpecProvider>());

        Block genesis = testBlockchain.BlockFinder.FindBlock(0)!;
        TxReceipt[] genesisReceipts = testBlockchain.ReceiptStorage.Get(genesis);

        await builder.Add(genesis, genesisReceipts);

        testBlockchain.BlockProcessor.BlockProcessed += (sender, blockArgs) =>
        {
            builder.Add(blockArgs.Block, blockArgs.TxReceipts).Wait();
        };

        int numOfBlocks = 12;
        await testBlockchain.BuildSomeBlocks(numOfBlocks);

        await builder.Finalize();

        byte[] buffer = new byte[1024];

        EraMetadata metadata = await EraMetadata.CreateEraMetadata(stream, default);
        Assert.That(metadata.Start, Is.EqualTo(0));
        Assert.That(metadata.Count, Is.EqualTo(numOfBlocks + 1));

        for (int i = 0; i < metadata.Count; i++)
        {
            long blockOffset = metadata.BlockOffset(i);
            stream.Seek(blockOffset, SeekOrigin.Begin);

            stream.Read(buffer, 0, 2);

            ushort entryType = BinaryPrimitives.ReadUInt16LittleEndian(buffer);

            //We expect to find a compressed header in this position
            Assert.That(entryType, Is.EqualTo(EntryTypes.CompressedHeader));
        }
    }

    [Test]
    public async Task TestBigBlocksExportImportHistory()
    {
        TestBlockchain testBlockchain = await BasicTestBlockchain.Create();
        testBlockchain.State.AddToBalance(TestItem.AddressA, 10.Ether(), testBlockchain.SpecProvider.GenesisSpec);
        testBlockchain.State.RecalculateStateRoot();

        using MemoryStream stream = new();
        using EraWriter builder = EraWriter.Create(stream, Substitute.For<ISpecProvider>());

        Block genesis = testBlockchain.BlockFinder.FindBlock(0)!;

        int numOfBlocks = 16;
        int numOfTx = 1000;
        UInt256 nonce = 0;
        var blocks = new List<Block>
        {
            genesis
        };
        for (int i = 0; i < numOfBlocks; i++)
        {
            Transaction[] transactions = new Transaction[numOfTx];
            for (int y = 0; y < numOfTx; y++)
            {
                transactions[y] = Build.A.Transaction.WithTo(TestItem.GetRandomAddress())
                                                     .WithNonce(nonce)
                                                     .WithValue(1)
                                                     .SignedAndResolved(TestItem.PrivateKeyA)
                                                     .TestObject;
                nonce++;
            }
            blocks.Add(Build.A.Block.WithUncles(Build.A.Block.TestObject)
                                    .WithBaseFeePerGas(1)
                                    .WithWithdrawals(100)
                                    .WithTotalDifficulty(1000000L + blocks[i].Difficulty)
                                    .WithTransactions(transactions)
                                    .WithParent(blocks[i])
                                    .WithGasLimit(30_000_000).TestObject);
        }

        testBlockchain.BlockProcessor.Process(genesis.StateRoot!, blocks, ProcessingOptions.NoValidation, new BlockReceiptsTracer());

        foreach (var block in blocks)
        {
            foreach (var item in block.Transactions)
                item.SenderAddress = null;
            await builder.Add(block, testBlockchain.ReceiptStorage.Get(block));
        }

        await builder.Finalize();

        using EraReader iterator = await EraReader.Create(stream);

        await using var enu = iterator.GetAsyncEnumerator();
        for (int i = 0; i < numOfBlocks; i++)
        {
            Assert.That(await enu.MoveNextAsync(), Is.True, $"Expected block {i} from the iterator, but it returned false.");
            (Block b, TxReceipt[] r, UInt256 td) = enu.Current;
            b.Header.TotalDifficulty = td;

            Block expectedBlock = blocks[i] ?? throw new ArgumentException("Could not find required block?");

            //ignore this for comparison
            expectedBlock.Header.MaybeParent = null;

            TxReceipt[] expectedReceipts = testBlockchain.ReceiptStorage.Get(expectedBlock);

            b.Should().BeEquivalentTo(expectedBlock);

            Assert.That(r.Length, Is.EqualTo(expectedReceipts.Length), "Incorrect amount of receipts.");

            for (int y = 0; y < expectedReceipts.Length; y++)
            {
                Assert.That(r[y].TxType, Is.EqualTo(expectedReceipts[y].TxType));
                Assert.That(r[y].PostTransactionState, Is.EqualTo(expectedReceipts[y].PostTransactionState));
                Assert.That(r[y].GasUsedTotal, Is.EqualTo(expectedReceipts[y].GasUsedTotal));
                Assert.That(r[y].Bloom, Is.EqualTo(expectedReceipts[y].Bloom));
                Assert.That(r[y].Logs, Is.EquivalentTo(expectedReceipts[y].Logs!));
                if (expectedReceipts[y].Error == null)
                    Assert.That(r[y].Error, new OrConstraint(Is.Null, Is.Empty));
                else
                    Assert.That(r[y].Error, Is.EqualTo(expectedReceipts[y].Error));
            }
        }
    }

    [Test]
    public async Task EraExportAndImport()
    {
        const int ChainLength = 10000;
        var fileSystem = new MockFileSystem();
        BlockTree exportTree = Build.A.BlockTree().OfChainLength(ChainLength).TestObject;
        IReceiptStorage receiptStorage = Substitute.For<IReceiptStorage>();
        ISpecProvider specProvider = Substitute.For<ISpecProvider>();
        IBlockValidator blockValidator = Substitute.For<IBlockValidator>();
        blockValidator.ValidateSuggestedBlock(Arg.Any<Block>(), out _).Returns(true);
        EraExporter exporter = new(fileSystem, exportTree, receiptStorage, specProvider, "abc");
        await exporter.Export("test", 0, ChainLength - 1);

        BlockTree importTree = Build.A.BlockTree()
            .WithBlocks(exportTree.FindBlock(0, BlockTreeLookupOptions.None)!).TestObject;
        int bestSuggestedNumber = 0;
        importTree.NewBestSuggestedBlock += (sender, args) => bestSuggestedNumber++;
        EraImporter importer = new(
            fileSystem,
            importTree,
            blockValidator,
            receiptStorage,
            specProvider,
            "abc");
        await importer.ImportAsArchiveSync("test", CancellationToken.None);

        Assert.That(importTree.BestSuggestedHeader, Is.Not.Null);
        Assert.That(importTree.BestSuggestedHeader!.Hash, Is.EqualTo(exportTree.HeadHash));

        Assert.That(bestSuggestedNumber, Is.EqualTo(ChainLength - 1));
    }

    [Test]
    public async Task EraExportAndImportWithValidation()
    {
        const int ChainLength = EraWriter.MaxEra1Size * 3;
        var fileSystem = new MockFileSystem();
        BlockTree exportTree = Build.A.BlockTree().OfChainLength(ChainLength).TestObject;
        IReceiptStorage receiptStorage = Substitute.For<IReceiptStorage>();
        ISpecProvider specProvider = MainnetSpecProvider.Instance;
        IBlockValidator blockValidator = Substitute.For<IBlockValidator>();
        EraExporter exporter = new(fileSystem, exportTree, receiptStorage, specProvider, "abc");
        await exporter.Export("test", 0, ChainLength - 1);

        BlockTree importTree = Build.A.BlockTree()
            .WithBlocks(exportTree.FindBlock(0, BlockTreeLookupOptions.None)!).TestObject;

        int bestSuggestedNumber = 0;
        importTree.NewBestSuggestedBlock += (sender, args) => bestSuggestedNumber++;

        EraImporter importer = new(
            fileSystem,
            importTree,
            blockValidator,
            receiptStorage,
            specProvider,
            "abc");
        await importer.Import("test", 0, exportTree.Head!.Number, Path.Combine("test", "accumulators.txt"));

        Assert.That(importTree.BestSuggestedHeader, Is.Not.Null);
        Assert.That(importTree.BestSuggestedHeader!.Hash, Is.EqualTo(exportTree.HeadHash));

        Assert.That(importTree.BestKnownNumber, Is.EqualTo(exportTree.BestKnownNumber));
    }

    [Test]
    public async Task EraExportAndImportNormal()
    {
        const int ChainLength = EraWriter.MaxEra1Size * 5;
        var fileSystem = new MockFileSystem();
        BlockTree exportTree = Build.A.BlockTree().OfChainLength(ChainLength).TestObject;
        IReceiptStorage receiptStorage = Substitute.For<IReceiptStorage>();
        ISpecProvider specProvider = MainnetSpecProvider.Instance;
        IBlockValidator blockValidator = Substitute.For<IBlockValidator>();
        EraExporter exporter = new(fileSystem, exportTree, receiptStorage, specProvider, "abc");
        await exporter.Export("test", 0, ChainLength - 1);

        BlockTree importTree = Build.A.BlockTree()
            .WithBlocks(exportTree.FindBlock(0, BlockTreeLookupOptions.None)!).TestObject;

        EraImporter importer = new(
            fileSystem,
            importTree,
            blockValidator,
            receiptStorage,
            specProvider,
            "abc");

        await importer.Import("test", 0, exportTree.Head!.Number, Path.Combine("test", "accumulators.txt"));

        Assert.That(importTree.LowestInsertedHeader, Is.Not.Null);
        Assert.That(importTree.BestSuggestedHeader!.Hash, Is.EqualTo(exportTree.HeadHash));
    }
}
