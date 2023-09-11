// SPDX-FileCopyrightText: 2023 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Config;
using Nethermind.Consensus;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Specs;
using Nethermind.Int256;

namespace Nethermind.Facade.Proxy.Models.MultiCall;

public class BlockOverride
{
    public Keccak PrevRandao { get; set; } = Keccak.Zero;
    public ulong? Number { get; set; } 
    public ulong? Time { get; set; } 
    public ulong? GasLimit { get; set; } 
    public Address? FeeRecipient { get; set; }
    public UInt256? BaseFeePerGas { get; set; }

    public BlockHeader GetBlockHeader(BlockHeader parent, IBlocksConfig cfg)
    {
        ulong newTime = Time ?? parent.Timestamp + cfg.SecondsPerSlot;

        long newGasLimit = GasLimit switch
        {
            null => parent.GasLimit,
            <= long.MaxValue => (long)GasLimit,
            _ => throw new OverflowException($"GasLimit value is too large, max value {ulong.MaxValue}")
        };

        long newBlockNumber = Number switch
        {
            null => parent.Number + 1,
            <= long.MaxValue => (long)Number,
            _ => throw new OverflowException($"Block Number value is too large, max value {ulong.MaxValue}")
        };

        Address newFeeRecipientAddress = FeeRecipient != null ? FeeRecipient : parent.Beneficiary;

        var result = new BlockHeader(
            parent.Hash,
            Keccak.OfAnEmptySequenceRlp,
            newFeeRecipientAddress,
            UInt256.Zero,
            newBlockNumber,
            newGasLimit,
            newTime,
            Array.Empty<byte>());

        result.MixHash = PrevRandao;
        result.BaseFeePerGas = BaseFeePerGas != null ? BaseFeePerGas.Value : parent.BaseFeePerGas;

        UInt256 difficulty = ConstantDifficulty.One.Calculate(result, parent);
        result.Difficulty = difficulty;
        result.TotalDifficulty = parent.TotalDifficulty + difficulty;
        return result;
    }
}
