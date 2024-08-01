// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Threading;
using System.Threading.Tasks;
using Nethermind.Blockchain.Era1;
using Nethermind.Era1;

namespace Nethermind.Blockchain;

public interface IEraExporter
{
    string NetworkName { get; }

    event EventHandler<ExportProgressArgs> ExportProgress;

    Task Export(string destinationPath, long start, long end, int size = EraWriter.MaxEra1Size, CancellationToken cancellation = default);
}
