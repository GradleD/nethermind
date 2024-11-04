// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Abi;
using Nethermind.Core;
using Nethermind.Core.Specs;
using Nethermind.Evm.TransactionProcessing;
using Nethermind.Merge.AuRa.Contracts;
using Nethermind.Specs.ChainSpecStyle;

namespace Nethermind.Merge.AuRa.Withdrawals;

public class WithdrawalContractFactory : IWithdrawalContractFactory
{
    private readonly IAbiEncoder _abiEncoder;
    private readonly Address _contractAddress;
    private readonly ISpecProvider _specProvider;

    public WithdrawalContractFactory(AuRaParameters parameters, IAbiEncoder abiEncoder, ISpecProvider specProvider)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        _abiEncoder = abiEncoder ?? throw new ArgumentNullException(nameof(abiEncoder));
        _contractAddress = parameters.WithdrawalContractAddress;
        _specProvider = specProvider ?? throw new ArgumentNullException(nameof(specProvider));
    }

    public IWithdrawalContract Create(ITransactionProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);

        return new WithdrawalContract(processor, _abiEncoder, _contractAddress, _specProvider);
    }
}
