// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Specs;
using Nethermind.Core.Test.Builders;
using Nethermind.Int256;
using NSubstitute;

namespace Nethermind.Era1.Test;
internal class EraReaderTests
{
    [Test]
    public async Task ReadAccumulator_DoesNotThrow()
    {
        using TmpFile tmpFile = new TmpFile();
        EraWriter builder = EraWriter.Create(tmpFile.FilePath, Substitute.For<ISpecProvider>());
        Block block0 = Build.A.Block.WithNumber(0).WithTotalDifficulty(BlockHeaderBuilder.DefaultDifficulty).TestObject;
        Block block1 = Build.A.Block.WithNumber(1).WithTotalDifficulty(BlockHeaderBuilder.DefaultDifficulty).TestObject;
        Block block2 = Build.A.Block.WithNumber(2).WithTotalDifficulty(BlockHeaderBuilder.DefaultDifficulty).TestObject;
        await builder.Add(block0, Array.Empty<TxReceipt>());
        await builder.Add(block1, Array.Empty<TxReceipt>());
        await builder.Add(block2, Array.Empty<TxReceipt>());
        await builder.Finalize();
        await builder.Finalize();

        using EraReader sut = new EraReader(tmpFile.FilePath);
        Assert.That(() => sut.ReadAccumulator(), Throws.Nothing);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task GetBlockByNumber_DifferentNumber_ReturnsBlockWithCorrectNumber(int number)
    {
        using TmpFile tmpFile = new TmpFile();
        EraWriter builder = EraWriter.Create(tmpFile.FilePath, Substitute.For<ISpecProvider>());
        Block block0 = Build.A.Block.WithNumber(0).WithTotalDifficulty(BlockHeaderBuilder.DefaultDifficulty).TestObject;
        Block block1 = Build.A.Block.WithNumber(1).WithTotalDifficulty(BlockHeaderBuilder.DefaultDifficulty).TestObject;
        Block block2 = Build.A.Block.WithNumber(2).WithTotalDifficulty(BlockHeaderBuilder.DefaultDifficulty).TestObject;
        await builder.Add(block0, Array.Empty<TxReceipt>());
        await builder.Add(block1, Array.Empty<TxReceipt>());
        await builder.Add(block2, Array.Empty<TxReceipt>());
        await builder.Finalize();

        using EraReader sut = new EraReader(tmpFile.FilePath);
        (Block result, _, _) = await sut.GetBlockByNumber(number);
        Assert.That(result.Number, Is.EqualTo(number));
    }

    [Test]
    public async Task GetAsyncEnumerator_EnumerateAll_ReadsAllAddedBlocks()
    {
        using TmpFile tmpFile = new TmpFile();
        EraWriter builder = EraWriter.Create(tmpFile.FilePath, Substitute.For<ISpecProvider>());
        Block block0 = Build.A.Block.WithNumber(0).WithTotalDifficulty(BlockHeaderBuilder.DefaultDifficulty).TestObject;
        Block block1 = Build.A.Block.WithNumber(1).WithTotalDifficulty(BlockHeaderBuilder.DefaultDifficulty).TestObject;
        Block block2 = Build.A.Block.WithNumber(2).WithTotalDifficulty(BlockHeaderBuilder.DefaultDifficulty).TestObject;
        await builder.Add(block0, Array.Empty<TxReceipt>());
        await builder.Add(block1, Array.Empty<TxReceipt>());
        await builder.Add(block2, Array.Empty<TxReceipt>());
        await builder.Finalize();

        using EraReader sut = new EraReader(tmpFile.FilePath);
        IAsyncEnumerator<(Block, TxReceipt[], UInt256)> enumerator = sut.GetAsyncEnumerator();
        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        (Block block, _, UInt256 td) = enumerator.Current;
        block.Header.TotalDifficulty = td;
        block.Should().BeEquivalentTo(block0);

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        (block, _, td) = enumerator.Current;
        block.Header.TotalDifficulty = td;
        block.Should().BeEquivalentTo(block1);

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        (block, _, td) = enumerator.Current;
        block.Header.TotalDifficulty = td;
        block.Should().BeEquivalentTo(block2);
    }

    [Test]
    public async Task GetAsyncEnumerator_EnumerateAll_ReadsAllAddedReceipts()
    {
        using TmpFile tmpFile = new TmpFile();
        EraWriter builder = EraWriter.Create(tmpFile.FilePath, Substitute.For<ISpecProvider>());
        Block block0 = Build.A.Block.WithTotalDifficulty(BlockHeaderBuilder.DefaultDifficulty).TestObject;
        TxReceipt[] receipt0 = new[] { Build.A.Receipt.WithTxType(TxType.EIP1559).TestObject };
        TxReceipt[] receipt1 = new[] { Build.A.Receipt.WithTxType(TxType.EIP1559).TestObject };
        TxReceipt[] receipt2 = new[] { Build.A.Receipt.WithTxType(TxType.EIP1559).TestObject };
        await builder.Add(block0, receipt0);
        await builder.Add(block0, receipt1);
        await builder.Add(block0, receipt2);
        await builder.Finalize();
        using EraReader sut = new EraReader(tmpFile.FilePath);

        IAsyncEnumerator<(Block, TxReceipt[], UInt256)> enumerator = sut.GetAsyncEnumerator();
        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        (_, TxReceipt[] receipts, _) = enumerator.Current;
        receipts.Should().BeEquivalentTo(receipt0);

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        (_, receipts, _) = enumerator.Current;
        receipts.Should().BeEquivalentTo(receipt1);

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        (_, receipts, _) = enumerator.Current;
        receipts.Should().BeEquivalentTo(receipt2);
    }

    [Test]
    public async Task GetAsyncEnumerator_EnumerateAll_EnumeratesCorrectAmountOfBlocks()
    {
        using TmpFile tmpFile = new TmpFile();
        EraWriter builder = EraWriter.Create(tmpFile.FilePath, Substitute.For<ISpecProvider>());
        Block block0 = Build.A.Block.WithNumber(0).WithTotalDifficulty(BlockHeaderBuilder.DefaultDifficulty).TestObject;
        Block block1 = Build.A.Block.WithNumber(1).WithTotalDifficulty(BlockHeaderBuilder.DefaultDifficulty).TestObject;
        Block block2 = Build.A.Block.WithNumber(2).WithTotalDifficulty(BlockHeaderBuilder.DefaultDifficulty).TestObject;
        await builder.Add(block0, Array.Empty<TxReceipt>());
        await builder.Add(block1, Array.Empty<TxReceipt>());
        await builder.Add(block2, Array.Empty<TxReceipt>());
        await builder.Finalize();

        using EraReader sut = new EraReader(tmpFile.FilePath);
        int result = 0;
        await foreach (var item in sut)
        {
            result++;
        }

        Assert.That(result, Is.EqualTo(3));
    }

    [Test]
    public async Task GetAsyncEnumerator_EnumerateAllInReverse_BlocksAreReturnedInReverseOrder()
    {
        using TmpFile tmpFile = new TmpFile();
        EraWriter builder = EraWriter.Create(tmpFile.FilePath, Substitute.For<ISpecProvider>());
        Block block0 = Build.A.Block.WithNumber(0).WithDifficulty(0).TestObject;
        Block block1 = Build.A.Block.WithNumber(1).WithDifficulty(0).TestObject;
        Block block2 = Build.A.Block.WithNumber(2).WithDifficulty(0).TestObject;
        Block[] expectedOrder = new[] { block2, block1, block0 };
        await builder.Add(block0, Array.Empty<TxReceipt>());
        await builder.Add(block1, Array.Empty<TxReceipt>());
        await builder.Add(block2, Array.Empty<TxReceipt>());
        await builder.Finalize();

        using EraReader sut = new EraReader(tmpFile.FilePath);

        var enumerator = sut.GetAsyncEnumerator();
        for (int i = 2; i < 0; i--)
        {
            await enumerator.MoveNextAsync();
            (Block b, _, _) = enumerator.Current;
            Assert.That(b.Number, Is.EqualTo(i));
        }
    }

    [Test]
    public async Task VerifyAccumulator_CreateBlocks_AccumulatorMatches()
    {
        using AccumulatorCalculator calculator = new();
        using TmpFile tmpFile = new TmpFile();
        EraWriter builder = EraWriter.Create(tmpFile.FilePath, Substitute.For<ISpecProvider>());
        Block block0 = Build.A.Block.WithNumber(0).WithTotalDifficulty(BlockHeaderBuilder.DefaultDifficulty).TestObject;
        Block block1 = Build.A.Block.WithNumber(1).WithTotalDifficulty(block0.Difficulty + block0.TotalDifficulty).TestObject;
        Block block2 = Build.A.Block.WithNumber(2).WithTotalDifficulty(block1.Difficulty + block1.TotalDifficulty).TestObject;
        calculator.Add(block0.Hash!, block0.TotalDifficulty!.Value);
        calculator.Add(block1.Hash!, block1.TotalDifficulty!.Value);
        calculator.Add(block2.Hash!, block2.TotalDifficulty!.Value);
        await builder.Add(block0, Array.Empty<TxReceipt>());
        await builder.Add(block1, Array.Empty<TxReceipt>());
        await builder.Add(block2, Array.Empty<TxReceipt>());
        await builder.Finalize();

        using EraReader sut = new EraReader(tmpFile.FilePath);
        bool result = await sut.VerifyAccumulator(calculator.ComputeRoot(), Substitute.For<ISpecProvider>());

        Assert.That(result, Is.True);
    }
    [Test]
    public async Task VerifyAccumulator_FirstVerifyThenEnumerateAll_AllBlocksEnumerated()
    {
        using AccumulatorCalculator calculator = new();
        using TmpFile tmpFile = new TmpFile();
        EraWriter builder = EraWriter.Create(tmpFile.FilePath, Substitute.For<ISpecProvider>());
        Block block0 = Build.A.Block.WithNumber(0).WithTotalDifficulty(BlockHeaderBuilder.DefaultDifficulty).TestObject;
        Block block1 = Build.A.Block.WithNumber(1).WithTotalDifficulty(block0.Difficulty + block0.TotalDifficulty).TestObject;
        Block block2 = Build.A.Block.WithNumber(2).WithTotalDifficulty(block1.Difficulty + block1.TotalDifficulty).TestObject;
        calculator.Add(block0.Hash!, block0.TotalDifficulty!.Value);
        calculator.Add(block1.Hash!, block1.TotalDifficulty!.Value);
        calculator.Add(block2.Hash!, block2.TotalDifficulty!.Value);
        await builder.Add(block0, Array.Empty<TxReceipt>());
        await builder.Add(block1, Array.Empty<TxReceipt>());
        await builder.Add(block2, Array.Empty<TxReceipt>());
        await builder.Finalize();
        int count = 0;
        using EraReader sut = new EraReader(tmpFile.FilePath);

        await sut.VerifyAccumulator(calculator.ComputeRoot(), Substitute.For<ISpecProvider>());
        await foreach (var item in sut)
        {
            count++;
        }

        Assert.That(count, Is.EqualTo(3));
    }

    [Test]
    public async Task ReadAccumulator_CalculateWithAccumulatorCalculator_AccumulatorMatches()
    {
        using AccumulatorCalculator calculator = new();
        using TmpFile tmpFile = new TmpFile();
        EraWriter builder = EraWriter.Create(tmpFile.FilePath, Substitute.For<ISpecProvider>());
        Block block0 = Build.A.Block.WithNumber(0).WithTotalDifficulty(BlockHeaderBuilder.DefaultDifficulty).TestObject;
        Block block1 = Build.A.Block.WithNumber(1).WithTotalDifficulty(BlockHeaderBuilder.DefaultDifficulty).TestObject;
        Block block2 = Build.A.Block.WithNumber(2).WithTotalDifficulty(BlockHeaderBuilder.DefaultDifficulty).TestObject;
        calculator.Add(block0.Hash!, BlockHeaderBuilder.DefaultDifficulty);
        calculator.Add(block1.Hash!, BlockHeaderBuilder.DefaultDifficulty);
        calculator.Add(block2.Hash!, BlockHeaderBuilder.DefaultDifficulty);
        await builder.Add(block0, Array.Empty<TxReceipt>());
        await builder.Add(block1, Array.Empty<TxReceipt>());
        await builder.Add(block2, Array.Empty<TxReceipt>());
        await builder.Finalize();

        using EraReader sut = new EraReader(tmpFile.FilePath);
        var result = sut.ReadAccumulator();

        Assert.That(result, Is.EqualTo(calculator.ComputeRoot()));
    }
}
