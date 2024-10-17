// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Autofac;
using Nethermind.Core;
using Nethermind.Core.Container;
using Nethermind.Init.Steps;
using Nethermind.Runner.Ethereum;

namespace Nethermind.Runner.Modules;

public class RunnerModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        base.Load(builder);

        builder
            .AddSingleton<EthereumRunner>()
            .AddSingleton<EthereumStepsManager>()
            .AddSingleton<IEthereumStepsLoader, EthereumStepsLoader>()
            .AddIStepsFromAssembly(typeof(IStep).Assembly)
            .AddIStepsFromAssembly(GetType().Assembly);
    }
}
