// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Blockchain;
using Nethermind.Core.Specs;
using Nethermind.Evm.TransactionProcessing;
using Nethermind.Logging;
using Nethermind.State;

namespace Nethermind.Consensus.Processing;

public class ReadOnlyTxProcessingEnvFactory(
    IWorldStateManager worldStateManager,
    IReadOnlyBlockTree readOnlyBlockTree,
    ISpecProvider? specProvider,
    ILogManager? logManager,
    IWorldState? worldStateToWarmUp = null) : IReadOnlyTxProcessingEnvFactory
{
    public ReadOnlyTxProcessingEnvFactory(
        IWorldStateManager worldStateManager,
        IBlockTree blockTree,
        ISpecProvider? specProvider,
        ILogManager? logManager,
        IWorldState? worldStateToWarmUp = null)
        : this(worldStateManager, blockTree.AsReadOnly(), specProvider, logManager, worldStateToWarmUp)
    {
    }

    public IReadOnlyTxProcessorSource Create() => new ReadOnlyTxProcessingEnv(worldStateManager, readOnlyBlockTree, specProvider, logManager, worldStateToWarmUp);
}
