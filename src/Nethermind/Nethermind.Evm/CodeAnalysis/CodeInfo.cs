// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Threading;
using Nethermind.Evm.CodeAnalysis.IL;
using System.Runtime.CompilerServices;
using Nethermind.Evm.Precompiles;
using Nethermind.Evm.Tracing;
using Nethermind.Core.Crypto;
using Nethermind.Evm.Config;
using Nethermind.Logging;
using ILMode = int;

namespace Nethermind.Evm.CodeAnalysis
{
    public class CodeInfo : IThreadPoolWorkItem
    {
        public Hash256 CodeHash { get; init; }
        public ReadOnlyMemory<byte> MachineCode { get; }
        public IPrecompile? Precompile { get; set; }


        // IL-EVM
        private int _callCount;

        public async void NoticeExecution(IVMConfig vmConfig, ILogger logger)
        {
            // IL-EVM info already created
            if (_callCount > Math.Max(vmConfig.JittingThreshold, vmConfig.PatternMatchingThreshold))
                return;

            Interlocked.Increment(ref _callCount);
            // use Interlocked just in case of concurrent execution to run it only once
            ILMode mode =
                 vmConfig.IsPatternMatchingEnabled && _callCount == vmConfig.PatternMatchingThreshold
                    ? IlInfo.ILMode.PAT_MODE
                    : vmConfig.IsJitEnabled && _callCount == vmConfig.JittingThreshold
                        ? IlInfo.ILMode.JIT_MODE
                        : IlInfo.ILMode.NO_ILVM;

            if (mode == IlInfo.ILMode.NO_ILVM)
                return;

            await IlAnalyzer.StartAnalysis(this, mode, logger, vmConfig).ConfigureAwait(false);
        }
        private readonly JumpDestinationAnalyzer _analyzer;
        private static readonly JumpDestinationAnalyzer _emptyAnalyzer = new(Array.Empty<byte>());
        public static CodeInfo Empty { get; } = new CodeInfo(Array.Empty<byte>(), Keccak.OfAnEmptyString);

        public CodeInfo(byte[] code, Hash256 codeHash = null)
        {
            CodeHash = codeHash ?? Keccak.Compute(code.AsSpan());
            MachineCode = code;
            _analyzer = code.Length == 0 ? _emptyAnalyzer : new JumpDestinationAnalyzer(code);
        }

        public CodeInfo(ReadOnlyMemory<byte> code, Hash256 codeHash = null)
        {
            CodeHash = codeHash ?? Keccak.Compute(code.Span);
            MachineCode = code;
            _analyzer = code.Length == 0 ? _emptyAnalyzer : new JumpDestinationAnalyzer(code);
        }

        public bool IsPrecompile => Precompile is not null;
        public bool IsEmpty => ReferenceEquals(_analyzer, _emptyAnalyzer) && !IsPrecompile;

        /// <summary>
        /// Gets information whether this code info has IL-EVM optimizations ready.
        /// </summary>
        internal IlInfo? IlInfo { get; set; } = IlInfo.Empty;

        public CodeInfo(IPrecompile precompile)
        {
            Precompile = precompile;
            MachineCode = Array.Empty<byte>();
            _analyzer = _emptyAnalyzer;
            CodeHash = Keccak.OfAnEmptyString;
        }

        public bool ValidateJump(int destination)
        {
            return _analyzer.ValidateJump(destination);
        }

        void IThreadPoolWorkItem.Execute()
        {
            _analyzer.Execute();
        }

        public void AnalyseInBackgroundIfRequired()
        {
            if (!ReferenceEquals(_analyzer, _emptyAnalyzer) && _analyzer.RequiresAnalysis)
            {
                ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
            }
        }
    }
}
