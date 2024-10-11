// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using Nethermind.Evm.Precompiles;
using Nethermind.Evm.Precompiles.Bls;

namespace Nethermind.Precompiles.Benchmark;

public class BlsPairingCheckBenchmark : PrecompileBenchmarkBase
{
    protected override IEnumerable<IPrecompile> Precompiles => new[]
    {
        PairingCheckPrecompile.Instance
    };

    protected override string InputsDirectory => "blspairingcheck";
}
