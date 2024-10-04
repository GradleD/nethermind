// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using Nethermind.Int256;

namespace Nethermind.Core.Specs;

public class ReleaseSpecDecorator(IReleaseSpec spec) : IReleaseSpec
{
    public virtual bool IsEip1559Enabled => spec.IsEip1559Enabled;
    public virtual long Eip1559TransitionBlock => spec.Eip1559TransitionBlock;
    public virtual UInt256 ForkBaseFee => spec.ForkBaseFee;
    public virtual UInt256 BaseFeeMaxChangeDenominator => spec.BaseFeeMaxChangeDenominator;
    public virtual long ElasticityMultiplier => spec.ElasticityMultiplier;
    public virtual bool IsEip658Enabled => spec.IsEip658Enabled;
    public virtual string Name => spec.Name;
    public virtual long MaximumExtraDataSize => spec.MaximumExtraDataSize;
    public virtual long MaxCodeSize => spec.MaxCodeSize;
    public virtual long MinGasLimit => spec.MinGasLimit;
    public virtual long GasLimitBoundDivisor => spec.GasLimitBoundDivisor;
    public virtual UInt256 BlockReward => spec.BlockReward;
    public virtual long DifficultyBombDelay => spec.DifficultyBombDelay;
    public virtual long DifficultyBoundDivisor => spec.DifficultyBoundDivisor;
    public virtual long? FixedDifficulty => spec.FixedDifficulty;
    public virtual int MaximumUncleCount => spec.MaximumUncleCount;
    public virtual bool IsTimeAdjustmentPostOlympic => spec.IsTimeAdjustmentPostOlympic;
    public virtual bool IsEip2Enabled => spec.IsEip2Enabled;
    public virtual bool IsEip7Enabled => spec.IsEip7Enabled;
    public virtual bool IsEip100Enabled => spec.IsEip100Enabled;
    public virtual bool IsEip140Enabled => spec.IsEip140Enabled;
    public virtual bool IsEip150Enabled => spec.IsEip150Enabled;
    public virtual bool IsEip155Enabled => spec.IsEip155Enabled;
    public virtual bool IsEip158Enabled => spec.IsEip158Enabled;
    public virtual bool IsEip160Enabled => spec.IsEip160Enabled;
    public virtual bool IsEip170Enabled => spec.IsEip170Enabled;
    public virtual bool IsEip196Enabled => spec.IsEip196Enabled;
    public virtual bool IsEip197Enabled => spec.IsEip197Enabled;
    public virtual bool IsEip198Enabled => spec.IsEip198Enabled;
    public virtual bool IsEip211Enabled => spec.IsEip211Enabled;
    public virtual bool IsEip214Enabled => spec.IsEip214Enabled;
    public virtual bool IsEip649Enabled => spec.IsEip649Enabled;
    public virtual bool IsEip145Enabled => spec.IsEip145Enabled;
    public virtual bool IsEip1014Enabled => spec.IsEip1014Enabled;
    public virtual bool IsEip1052Enabled => spec.IsEip1052Enabled;
    public virtual bool IsEip1283Enabled => spec.IsEip1283Enabled;
    public virtual bool IsEip1234Enabled => spec.IsEip1234Enabled;
    public virtual bool IsEip1344Enabled => spec.IsEip1344Enabled;
    public virtual bool IsEip2028Enabled => spec.IsEip2028Enabled;
    public virtual bool IsEip152Enabled => spec.IsEip152Enabled;
    public virtual bool IsEip1108Enabled => spec.IsEip1108Enabled;
    public virtual bool IsEip1884Enabled => spec.IsEip1884Enabled;
    public virtual bool IsEip2200Enabled => spec.IsEip2200Enabled;
    public virtual bool IsEip2537Enabled => spec.IsEip2537Enabled;
    public virtual bool IsEip2565Enabled => spec.IsEip2565Enabled;
    public virtual bool IsEip2929Enabled => spec.IsEip2929Enabled;
    public virtual bool IsEip2930Enabled => spec.IsEip2930Enabled;
    public virtual bool IsEip3198Enabled => spec.IsEip3198Enabled;
    public virtual bool IsEip3529Enabled => spec.IsEip3529Enabled;
    public virtual bool IsEip3541Enabled => spec.IsEip3541Enabled;
    public virtual bool IsEip3607Enabled => spec.IsEip3607Enabled;
    public virtual bool IsEip3651Enabled => spec.IsEip3651Enabled;
    public virtual bool IsEip1153Enabled => spec.IsEip1153Enabled;
    public virtual bool IsEip3855Enabled => spec.IsEip3855Enabled;
    public virtual bool IsEip5656Enabled => spec.IsEip5656Enabled;
    public virtual bool IsEip3860Enabled => spec.IsEip3860Enabled;
    public virtual bool IsEip4895Enabled => spec.IsEip4895Enabled;
    public virtual bool IsEip4844Enabled => spec.IsEip4844Enabled;
    public virtual bool IsEip4788Enabled => spec.IsEip4788Enabled;
    public virtual Address? Eip4788ContractAddress => spec.Eip4788ContractAddress;
    public bool IsEip6110Enabled => spec.IsEip6110Enabled;
    public Address DepositContractAddress => spec.DepositContractAddress;
    public bool IsEip7002Enabled => spec.IsEip7002Enabled;
    public Address Eip7002ContractAddress => spec.Eip7002ContractAddress;
    public virtual bool IsEip2935Enabled => spec.IsEip2935Enabled;
    public virtual bool IsEip7709Enabled => spec.IsEip7709Enabled;
    public virtual Address Eip2935ContractAddress => spec.Eip2935ContractAddress;
    public virtual bool IsEip6780Enabled => spec.IsEip6780Enabled;
    public virtual bool IsRip7212Enabled => spec.IsRip7212Enabled;
    public virtual bool IsOpGraniteEnabled => spec.IsOpGraniteEnabled;
    public virtual bool IsOntakeEnabled => spec.IsOntakeEnabled;
    public virtual ulong WithdrawalTimestamp => spec.WithdrawalTimestamp;
    public virtual ulong Eip4844TransitionTimestamp => spec.Eip4844TransitionTimestamp;
    public virtual bool IsEip158IgnoredAccount(Address address) => spec.IsEip158IgnoredAccount(address);
    public bool IsEip4844FeeCollectorEnabled => spec.IsEip4844FeeCollectorEnabled;

    public virtual long MaxInitCodeSize => spec.MaxInitCodeSize;
    public virtual bool ValidateChainId => spec.ValidateChainId;
    public virtual bool ClearEmptyAccountWhenTouched => spec.ClearEmptyAccountWhenTouched;
    // VM
    public virtual bool LimitCodeSize => spec.LimitCodeSize;
    public virtual bool UseHotAndColdStorage => spec.UseHotAndColdStorage;
    public virtual bool UseTxAccessLists => spec.UseTxAccessLists;
    public virtual bool AddCoinbaseToTxAccessList => spec.AddCoinbaseToTxAccessList;
    public virtual bool ModExpEnabled => spec.ModExpEnabled;
    public virtual bool Bn128Enabled => spec.Bn128Enabled;
    public virtual bool BlakeEnabled => spec.BlakeEnabled;
    public virtual bool Bls381Enabled => spec.Bls381Enabled;
    public virtual bool ChargeForTopLevelCreate => spec.ChargeForTopLevelCreate;
    public virtual bool FailOnOutOfGasCodeDeposit => spec.FailOnOutOfGasCodeDeposit;
    public virtual bool UseShanghaiDDosProtection => spec.UseShanghaiDDosProtection;
    public virtual bool UseExpDDosProtection => spec.UseExpDDosProtection;
    public virtual bool UseLargeStateDDosProtection => spec.UseLargeStateDDosProtection;
    public virtual bool ReturnDataOpcodesEnabled => spec.ReturnDataOpcodesEnabled;
    public virtual bool ChainIdOpcodeEnabled => spec.ChainIdOpcodeEnabled;
    public virtual bool Create2OpcodeEnabled => spec.Create2OpcodeEnabled;
    public virtual bool DelegateCallEnabled => spec.DelegateCallEnabled;
    public virtual bool StaticCallEnabled => spec.StaticCallEnabled;
    public virtual bool ShiftOpcodesEnabled => spec.ShiftOpcodesEnabled;
    public virtual bool RevertOpcodeEnabled => spec.RevertOpcodeEnabled;
    public virtual bool ExtCodeHashOpcodeEnabled => spec.ExtCodeHashOpcodeEnabled;
    public virtual bool SelfBalanceOpcodeEnabled => spec.SelfBalanceOpcodeEnabled;
    public virtual bool UseConstantinopleNetGasMetering => spec.UseConstantinopleNetGasMetering;
    public virtual bool UseIstanbulNetGasMetering => spec.UseIstanbulNetGasMetering;
    public virtual bool UseNetGasMetering => spec.UseNetGasMetering;
    public virtual bool UseNetGasMeteringWithAStipendFix => spec.UseNetGasMeteringWithAStipendFix;
    public virtual bool Use63Over64Rule => spec.Use63Over64Rule;
    public virtual bool BaseFeeEnabled => spec.BaseFeeEnabled;
    // EVM Related
    public virtual bool IncludePush0Instruction => spec.IncludePush0Instruction;
    public virtual bool TransientStorageEnabled => spec.TransientStorageEnabled;
    public virtual bool WithdrawalsEnabled => spec.WithdrawalsEnabled;
    public virtual bool SelfdestructOnlyOnSameTransaction => spec.SelfdestructOnlyOnSameTransaction;
    public virtual bool IsBeaconBlockRootAvailable => spec.IsBeaconBlockRootAvailable;
    public virtual bool IsBlockHashInStateAvailable => spec.IsBlockHashInStateAvailable;
    public virtual bool MCopyIncluded => spec.MCopyIncluded;
    public virtual bool BlobBaseFeeEnabled => spec.BlobBaseFeeEnabled;
    public virtual Address? FeeCollector => spec.FeeCollector;
    public virtual UInt256? Eip1559BaseFeeMinValue => spec.Eip1559BaseFeeMinValue;
    public virtual bool ValidateReceipts => spec.ValidateReceipts;
}
