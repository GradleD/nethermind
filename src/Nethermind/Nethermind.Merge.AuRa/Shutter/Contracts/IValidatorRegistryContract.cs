// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System.Threading.Tasks;
using Nethermind.Core;
using Nethermind.Int256;
using Nethermind.TxPool;

namespace Nethermind.Merge.AuRa.Shutter.Contracts;

public interface IValidatorRegistryContract
{
    /// <summary>
    /// Check if validator is registered.
    /// </summary>
    /// <param name="message"></param>
    /// <param name="signature"></param>
    bool IsRegistered(BlockHeader blockHeader, ulong validatorIndex, byte[] validatorPubKey);

    /// <summary>
    /// Returns the number of previous updates to the registry.
    /// </summary>
    /// <param name="blockHeader"></param>
    UInt256 GetNumUpdates(BlockHeader blockHeader);

    /// <summary>
    /// Retrieves the ith update to the registry.
    /// </summary>
    /// <param name="blockHeader"></param>
    /// <param name="i"></param>
    Update GetUpdate(BlockHeader blockHeader, in UInt256 i);

    struct Update
    {
        public byte[] Message;
        public byte[] Signature;
    }
}