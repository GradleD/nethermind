// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Consensus.Validators;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using Nethermind.Core.Specs;
using Nethermind.Crypto;
using Nethermind.Logging;
using Nethermind.TxPool;

namespace Nethermind.Taiko;

public class TaikoBlockValidator(
    ITxValidator txValidator,
    IHeaderValidator headerValidator,
    IUnclesValidator unclesValidator,
    ISpecProvider specProvider,
    IEthereumEcdsa ecdsa,
    ILogManager logManager) : BlockValidator(txValidator, headerValidator, unclesValidator, specProvider, logManager)
{
    private static readonly byte[] AnchorSelector = Keccak.Compute("anchor(bytes32,bytes32,uint64,uint32)").Bytes[0..4].ToArray();
    private static readonly byte[] AnchorV2Selector = Keccak.Compute("anchorV2(uint64,bytes32,uint32,(uint8,uint8,uint32,uint64,uint32))").Bytes[0..4].ToArray();

    private static readonly Address GoldenTouchAccount = new("0x0000777735367b36bC9B61C50022d9D0700dB4Ec");

    private readonly Address TaikoL2Address = TaikoAddressHelper.GetTaikoL2ContractAddress(specProvider);

    private const long AnchorGasLimit = 250_000;

    private readonly IEthereumEcdsa _ecdsa = ecdsa;

    protected override bool ValidateEip4844Fields(Block block, IReleaseSpec spec, out string? error)
    {
        // for some reason they don't validate these fields in taiko-geth
        error = null;
        return true;
    }

    protected override bool ValidateTransactions(Block block, IReleaseSpec spec, out string? errorMessage)
    {
        if (block.IsGenesis)
        {
            errorMessage = null;
            return true;
        }

        if (block.TxRoot == Keccak.Zero)
        {
            if (block.Transactions.Length is 0)
            {
                errorMessage = "Missing required Anchor Transaction.";
                return false;
            }

            if (!ValidateAnchorTransaction(block.Transactions[0], block, out errorMessage))
                return false;
        }

        // TaikoPlugin initializes the TxValidator with a Always.Valid validator
        return base.ValidateTransactions(block, spec, out errorMessage);
    }

    private bool ValidateAnchorTransaction(Transaction tx, Block block, out string? errorMessage)
    {
        if (tx.Type != TxType.EIP1559)
        {
            errorMessage = "Anchor Transaction must be of type EIP1559.";
            return false;
        }

        if (tx.To != TaikoL2Address)
        {
            errorMessage = "Anchor Transaction must target taiko L2 address.";
            return false;
        }

        if (tx.Data is null || (!Bytes.AreEqual(tx.Data.Value.Span[0..4], AnchorSelector) && !Bytes.AreEqual(tx.Data.Value.Span[0..4], AnchorV2Selector)))
        {
            errorMessage = "Anchor Transaction must have the correct selector.";
            return false;
        }

        if (!tx.Value.IsZero)
        {
            errorMessage = "Anchor Transaction must have 0 value.";
            return false;
        }

        if (tx.GasLimit != AnchorGasLimit)
        {
            errorMessage = "Anchor Transaction must have the correct gas limit.";
            return false;
        }

        if (tx.MaxFeePerGas != block.BaseFeePerGas)
        {
            errorMessage = "Anchor Transaction must have the correct max fee per gas.";
            return false;
        }

        tx.SenderAddress = _ecdsa.RecoverAddress(tx)
            ?? throw new InvalidOperationException("Couldn't recover sender address for Anchor Transaction");
        if (!tx.SenderAddress!.Equals(GoldenTouchAccount))
        {
            errorMessage = "Anchor Transaction must be sent by the Golden Touch account.";
            return false;
        }

        errorMessage = null;
        return true;
    }
}
