// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Specs;
using Nethermind.Evm;
using Nethermind.Evm.CodeAnalysis;
using Nethermind.State;

namespace Nethermind.Facade;

public class OverridableCodeInfoRepository(ICodeInfoRepository codeInfoRepository) : ICodeInfoRepository
{
    private readonly Dictionary<Address, ICodeInfo> _codeOverwrites = new();

    public ICodeInfo GetCachedCodeInfo(IWorldState worldState, Address codeSource, IReleaseSpec vmSpec, out Address? delegationAddress)
    {
        delegationAddress = null;
        return _codeOverwrites.TryGetValue(codeSource, out ICodeInfo result)
            ? result
            : codeInfoRepository.GetCachedCodeInfo(worldState, codeSource, vmSpec);
    }

    public void InsertCode(IWorldState state, ReadOnlyMemory<byte> code, Address codeOwner, IReleaseSpec spec) =>
        codeInfoRepository.InsertCode(state, code, codeOwner, spec);

    public void SetCodeOverwrite(
        IWorldState worldState,
        IReleaseSpec vmSpec,
        Address key,
        ICodeInfo value,
        Address? redirectAddress = null)
    {
        if (redirectAddress is not null)
        {
            _codeOverwrites[redirectAddress] = this.GetCachedCodeInfo(worldState, key, vmSpec);
        }

        _codeOverwrites[key] = value;
    }

    public void SetDelegation(IWorldState state, Address codeSource, Address authority, IReleaseSpec spec) =>
        codeInfoRepository.SetDelegation(state, codeSource, authority, spec);

    public bool TryGetDelegation(IReadOnlyStateProvider worldState, Address address, IReleaseSpec vmSpec, [NotNullWhen(true)] out Address? delegatedAddress) =>
        codeInfoRepository.TryGetDelegation(worldState, address, vmSpec, out delegatedAddress);

    public ValueHash256 GetExecutableCodeHash(IWorldState worldState, Address address, IReleaseSpec spec) =>
        codeInfoRepository.GetExecutableCodeHash(worldState, address, spec);
}
