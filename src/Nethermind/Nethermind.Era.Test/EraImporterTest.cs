// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Blockchain;
using Nethermind.Consensus.Validators;
using Nethermind.Core.Extensions;
using Nethermind.Core.Test.Builders;
using NSubstitute;
using System.IO.Abstractions;
using Autofac;
using Nethermind.Core;
using Nethermind.Era1.Exceptions;

namespace Nethermind.Era1.Test;
public class EraImporterTest
{
    [Test]
    public void ImportAsArchiveSync_DirectoryContainsNoEraFiles_ThrowEraImportException()
    {
        using IContainer testContext = EraTestModule.BuildContainerBuilder().Build();

        TmpDirectory tmpDirectory = testContext.Resolve<TmpDirectory>();
        IEraImporter sut = testContext.Resolve<IEraImporter>();

        Assert.That(() => sut.ImportAsArchiveSync(tmpDirectory.DirectoryPath, null, CancellationToken.None), Throws.TypeOf<EraImportException>());
    }

    [Test]
    public async Task ImportAsArchiveSync_DirectoryContainsWrongEraFiles_ThrowEraImportException()
    {
        using IContainer testContext = EraTestModule.BuildContainerBuilderWithBlockTreeOfLength(10).Build();
        TmpDirectory tempDirectory = new TmpDirectory();

        IFileSystem fileSystem = testContext.Resolve<IFileSystem>();
        fileSystem.Directory.CreateDirectory(tempDirectory.DirectoryPath);
        string badFilePath = Path.Join(tempDirectory.DirectoryPath, "abc-00000-00000000.era1");
        FileSystemStream stream = fileSystem.File.Create(badFilePath);
        await stream.WriteAsync(new byte[]{0, 0});
        stream.Close();

        IEraImporter sut = testContext.Resolve<IEraImporter>();
        Assert.That(() => sut.ImportAsArchiveSync(tempDirectory.DirectoryPath, null, CancellationToken.None), Throws.TypeOf<EraFormatException>());
    }

    [Test]
    public async Task ImportAsArchiveSync_BlockCannotBeValidated_ThrowEraImportException()
    {
        await using IContainer testCtx = await CreateExportedEraTests();
        IBlockTree sourceBlocktree = testCtx.Resolve<IBlockTree>();

        IBlockTree blockTree = Build.A.BlockTree().TestObject;
        blockTree.SuggestBlock(sourceBlocktree.FindBlock(0)!, BlockTreeSuggestOptions.None);

        await using IContainer targetCtx = EraTestModule.BuildContainerBuilder()
            .AddSingleton<IBlockTree>(blockTree)
            .Build();

        targetCtx.Resolve<IBlockValidator>().ValidateSuggestedBlock(Arg.Any<Block>(), out Arg.Any<string?>()).Returns(false);

        IEraImporter sut = targetCtx.Resolve<IEraImporter>();
        Assert.That(() => sut.ImportAsArchiveSync(testCtx.Resolve<TmpDirectory>().DirectoryPath, null, CancellationToken.None), Throws.TypeOf<EraImportException>());
    }

    [Test]
    public async Task VerifyEraFiles_VerifyAccumulatorsWithExpected_DoesNotThrow()
    {
        const int ChainLength = 128;
        await using IContainer fromCtx = await CreateExportedEraTests(ChainLength);

        TmpDirectory tmpDirectory = fromCtx.Resolve<TmpDirectory>();
        string destinationPath = tmpDirectory.DirectoryPath;

        await using IContainer toCtx = EraTestModule.BuildContainerBuilder()
            .Build();

        IEraImporter sut = toCtx.Resolve<IEraImporter>();
        await sut.Import(destinationPath, 0, long.MaxValue, Path.Join(destinationPath, EraExporter.AccumulatorFileName), default);
    }

    [Test]
    public async Task VerifyEraFiles_VerifyAccumulatorsWithUnexpected_ThrowEraVerificationException()
    {
        using IContainer outputCtx = await CreateExportedEraTests(64);
        IFileSystem fileSystem = outputCtx.Resolve<IFileSystem>();
        string destinationPath = outputCtx.Resolve<TmpDirectory>().DirectoryPath;

        string accumulatorPath = Path.Combine(destinationPath, EraExporter.AccumulatorFileName);
        var accumulators = outputCtx.Resolve<IFileSystem>().File.ReadAllLines(accumulatorPath).Select(s => Bytes.FromHexString(s)).ToArray();
        accumulators[accumulators.Length - 1] = new byte[32];
        await fileSystem.File.WriteAllLinesAsync(accumulatorPath, accumulators.Select(acc => acc.ToHexString()));

        using IContainer inCtx = EraTestModule.BuildContainerBuilder()
            .Build();

        IEraImporter sut = inCtx.Resolve<IEraImporter>();
        Func<Task> importTask = () => sut.Import(destinationPath, 0, long.MaxValue,
            Path.Join(destinationPath, EraExporter.AccumulatorFileName), default);

        Assert.That(importTask, Throws.TypeOf<EraVerificationException>());
    }

    private async Task<IContainer> CreateExportedEraTests(int chainLength = 512)
    {
        IContainer testCtx = EraTestModule.BuildContainerBuilderWithBlockTreeOfLength(chainLength).Build();
        await testCtx.Resolve<IEraExporter>().Export(testCtx.Resolve<TmpDirectory>().DirectoryPath, 0, chainLength-1);
        return testCtx;
    }
}
