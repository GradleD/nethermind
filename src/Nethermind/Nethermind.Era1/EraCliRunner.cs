// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.IO.Abstractions;
using Microsoft.ClearScript.JavaScript;
using Nethermind.Blockchain;
using Nethermind.Config;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.Logging;

namespace Nethermind.Era1;

public class EraCliRunner(
    IEraConfig eraConfig,
    IBlockTree blockTree,
    IEraImporter eraImporter,
    IEraExporter eraExporter,
    IProcessExitSource processExitSource,
    ILogManager logManager)
{
    private readonly ILogger _logger = logManager.GetClassLogger<EraCliRunner>();

    public async Task Run(CancellationToken token)
    {
        if (!string.IsNullOrEmpty(eraConfig.ImportDirectory))
        {
            await Import();
        }
        else if (!string.IsNullOrEmpty(eraConfig.ExportDirectory))
        {
            await Export(token);
        }
    }

    private async Task Export(CancellationToken cancellation) {
        var start = eraConfig.Start;
        var end = eraConfig.End;
        if (end == 0)
        {
            end = blockTree.Head?.Number ?? 0;
        }

        try
        {
            await eraExporter.Export(eraConfig.ExportDirectory!, start, end, createAccumulator: true, cancellation: cancellation);
        }
        catch (Exception e) when (e is TaskCanceledException or OperationCanceledException)
        {
            _logger.Warn($"A running export job was cancelled.");
        }
        catch (Exception e) when (e is EraException)
        {
            _logger.Error($"The export failed with the message: {e.Message}");
        }
        catch (Exception e)
        {
            _logger.Error("Import error", e);
            throw;
        }
    }

    private async Task Import() {
        try
        {
            await eraImporter.ImportAsArchiveSync(eraConfig.ImportDirectory!, eraConfig.TrustedAccumulatorFile, processExitSource.Token);
        }
        catch (Exception e) when (e is TaskCanceledException or OperationCanceledException)
        {
            _logger.Warn($"A running import job was cancelled.");
        }
        catch (Exception e) when (e is EraException or EraImportException)
        {
            _logger.Error($"The import failed with the message: {e.Message}");
        }
        catch (Exception e)
        {
            _logger.Error("Import error", e);
            throw;
        }
    }
}
