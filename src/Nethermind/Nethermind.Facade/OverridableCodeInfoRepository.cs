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
    private readonly Dictionary<Address, CodeInfo> _codeOverwrites = new();

    public CodeInfo GetCachedCodeInfo(IWorldState worldState, Address codeSource, IReleaseSpec vmSpec) =>
        _codeOverwrites.TryGetValue(codeSource, out CodeInfo result)
            ? result
            : codeInfoRepository.GetCachedCodeInfo(worldState, codeSource, vmSpec);
    public CodeInfo GetCachedCodeInfo(IWorldState worldState, Address codeSource, IReleaseSpec vmSpec, out Address? delegationAddress)
    {
        delegationAddress = null;
        return _codeOverwrites.TryGetValue(codeSource, out CodeInfo result)
            ? result
            : codeInfoRepository.GetCachedCodeInfo(worldState, codeSource, vmSpec);
    }

    public CodeInfo GetOrAdd(ValueHash256 codeHash, ReadOnlySpan<byte> initCode) => codeInfoRepository.GetOrAdd(codeHash, initCode);

    public void InsertCode(IWorldState state, ReadOnlyMemory<byte> code, Address codeOwner, IReleaseSpec spec) =>
        codeInfoRepository.InsertCode(state, code, codeOwner, spec);

    public void SetCodeOverwrite(
        IWorldState worldState,
        IReleaseSpec vmSpec,
        Address key,
        CodeInfo value,
        Address? redirectAddress = null)
    {
        if (redirectAddress is not null)
        {
            _codeOverwrites[redirectAddress] = GetCachedCodeInfo(worldState, key, vmSpec);
        }

        _codeOverwrites[key] = value;
    }

    public void ClearOverwrites()
    {
        _codeOverwrites.Clear();
    }

    public int InsertFromAuthorizations(IWorldState worldState, AuthorizationTuple?[] authorizations, ISet<Address> accessedAddresses, IReleaseSpec spec)
    {
        return codeInfoRepository.InsertFromAuthorizations(worldState, authorizations, accessedAddresses, spec);
    }

    public bool IsDelegation(IWorldState worldState, Address address, [NotNullWhen(true)] out Address? delegatedAddress)
    {
        return codeInfoRepository.IsDelegation(worldState, address, out delegatedAddress);
    }

    public ValueHash256 GetCodeHash(IWorldState worldState, Address address)
    {
        return codeInfoRepository.GetCodeHash(worldState, address);
    }
}
