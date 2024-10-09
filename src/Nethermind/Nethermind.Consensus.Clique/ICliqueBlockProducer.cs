// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Collections.Generic;
using Nethermind.Core;
using Nethermind.Core.Crypto;

namespace Nethermind.Consensus.Clique
{
    public interface ICliqueBlockProducerRunner : IBlockProducerRunner
    {
        void CastVote(Address signer, bool vote);
        void UncastVote(Address signer);
        void ProduceOnTopOf(Hash256 hash);
        IReadOnlyDictionary<Address, bool> GetProposals();
    }
}
