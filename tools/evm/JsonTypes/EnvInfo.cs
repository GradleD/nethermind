using System.Runtime.InteropServices.JavaScript;
using Ethereum.Test.Base;
using Evm.T8NTool;
using Nethermind.Consensus.Ethash;
using Nethermind.Core;
using Nethermind.Core.Crypto;
using Nethermind.Core.Specs;
using Nethermind.Core.Test.Builders;
using Nethermind.Int256;
using Nethermind.Specs.Forks;

namespace Evm.JsonTypes
{
    public class EnvInfo
    {
        public Address? CurrentCoinbase { get; set; }
        public long CurrentGasLimit { get; set; }
        public ulong CurrentTimestamp { get; set; }
        public long CurrentNumber { get; set; }

        public Withdrawal[]? Withdrawals { get; set; }

        public byte[]? CurrentRandom { get; set; }
        public ulong ParentTimestamp { get; set; }
        public UInt256? ParentDifficulty { get; set; }
        public UInt256? CurrentBaseFee { get; set; }
        public UInt256? CurrentDifficulty { get; set; }
        public Hash256? ParentUncleHash { get; set; }
        public Hash256? ParentBeaconBlockRoot { get; set; }
        public UInt256? ParentBaseFee { get; set; }
        public long ParentGasUsed { get; set; }
        public long ParentGasLimit { get; set; }
        public ulong? ParentExcessBlobGas { get; set; }
        public ulong? CurrentExcessBlobGas { get; set; }
        public ulong? ParentBlobGasUsed { get; set; }
        public Dictionary<string, Hash256> BlockHashes { get; set; } = [];
        public Ommer[] Ommers { get; set; } = [];

        public Hash256? GetCurrentRandomHash256()
        {
            if (CurrentRandom == null) return null;

            if (CurrentRandom.Length < Hash256.Size)
            {
                var currentRandomWithLeadingZeros = new byte[Hash256.Size];
                Array.Copy(CurrentRandom, 0, currentRandomWithLeadingZeros, Hash256.Size - CurrentRandom.Length,
                    CurrentRandom.Length);
                CurrentRandom = currentRandomWithLeadingZeros;
            }

            return new Hash256(CurrentRandom);
        }

        public void ApplyChecks(ISpecProvider specProvider, IReleaseSpec spec)
        {
            ApplyLondonChecks(spec);
            ApplyShanghaiChecks(spec);
            ApplyCancunChecks(spec);
            ApplyMergeChecks(specProvider);
        }

        private void ApplyLondonChecks(IReleaseSpec spec)
        {
            if (spec is not London) return;
            if (CurrentBaseFee != null) return;

            if (!ParentBaseFee.HasValue || CurrentNumber == 0)
            {
                throw new T8NException("EIP-1559 config but missing 'parentBaseFee' in env section", ExitCodes.ErrorConfig);
            }

            var parent = Build.A.BlockHeader.WithNumber(CurrentNumber - 1).WithBaseFee(ParentBaseFee.Value)
                .WithGasUsed(ParentGasUsed).WithGasLimit(ParentGasLimit).TestObject;
            CurrentBaseFee = BaseFeeCalculator.Calculate(parent, spec);
        }

        private void ApplyShanghaiChecks(IReleaseSpec spec)
        {
            if (spec is not Shanghai) return;
            if (Withdrawals == null)
            {
                throw new T8NException("Shanghai config but missing 'withdrawals' in env section", ExitCodes.ErrorConfig);
            }
        }

        private void ApplyCancunChecks(IReleaseSpec spec)
        {
            if (spec is not Cancun)
            {
                ParentBeaconBlockRoot = null;
                return;
            }

            if (ParentBeaconBlockRoot == null)
            {
                throw new T8NException("post-cancun env requires parentBeaconBlockRoot to be set", ExitCodes.ErrorConfig);
            }
        }

        private void ApplyMergeChecks(ISpecProvider specProvider)
        {
            if (specProvider.TerminalTotalDifficulty?.IsZero ?? false)
            {
                if (CurrentRandom == null) throw new T8NException("post-merge requires currentRandom to be defined in env", ExitCodes.ErrorConfig);
                if (CurrentDifficulty?.IsZero ?? false) throw new T8NException("post-merge difficulty must be zero (or omitted) in env", ExitCodes.ErrorConfig);
                return;
            }
            if (CurrentDifficulty != null) return;
            if (!ParentDifficulty.HasValue)
            {
                throw new T8NException(
                    "currentDifficulty was not provided, and cannot be calculated due to missing parentDifficulty", ExitCodes.ErrorConfig);
            }

            if (CurrentNumber == 0)
            {
                throw new T8NException("currentDifficulty needs to be provided for block number 0", ExitCodes.ErrorConfig);
            }

            if (CurrentTimestamp <= ParentTimestamp)
            {
                throw new T8NException($"currentDifficulty cannot be calculated -- currentTime ({CurrentTimestamp}) needs to be after parent time ({ParentTimestamp})", ExitCodes.ErrorConfig);
            }

            EthashDifficultyCalculator difficultyCalculator = new(specProvider);

            CurrentDifficulty = difficultyCalculator.Calculate(ParentDifficulty.Value, ParentTimestamp, CurrentTimestamp, CurrentNumber, ParentUncleHash is not null);
        }
    }
}
