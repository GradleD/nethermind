// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using Nethermind.Core.Specs;
using Nethermind.Evm.EOF;

namespace Nethermind.Evm
{
    public static class CodeDepositHandler
    {
        private const byte InvalidStartingCodeByte = 0xEF;
        public static long CalculateCost(int byteCodeLength, IReleaseSpec spec)
        {
            if (spec.LimitCodeSize && byteCodeLength > spec.MaxCodeSize)
                return long.MaxValue;

            return GasCostOf.CodeDeposit * byteCodeLength;
        }


        public static bool CodeIsInvalid(IReleaseSpec spec, ReadOnlyMemory<byte> code, int fromVersion)
            => !CodeIsValid(spec, code, fromVersion);
        public static bool CodeIsValid(IReleaseSpec spec, ReadOnlyMemory<byte> code, int fromVersion)
        {
            bool valid = true;
            if (spec.IsEofEnabled)
            {
                //fromVersion = (execType is ExecutionType.Create1 or ExecutionType.Create2) ? fromVersion : 0; //// hmmmm
                valid = IsValidWithEofRules(code.Span, fromVersion);
            }
            else if (spec.IsEip3541Enabled)
            {
                valid = IsValidWithLegacyRules(code.Span);
            }

            return valid;
        }

        public static bool IsValidWithLegacyRules(ReadOnlySpan<byte> code)
        {
            return code is not [InvalidStartingCodeByte, ..]; ;
        }

        public static bool IsValidWithEofRules(ReadOnlySpan<byte> code, int fromVersion, EvmObjectFormat.ValidationStrategy strategy = EvmObjectFormat.ValidationStrategy.Validate)
        {
            bool isCodeEof = EvmObjectFormat.IsEof(code, out int codeVersion);
            bool valid = code.Length >= 1
                  && codeVersion >= fromVersion
                  && (isCodeEof
                        ? EvmObjectFormat.IsValidEof(code, strategy, out _)
                        : (fromVersion > 0 ? false : IsValidWithLegacyRules(code)));
            return valid;
        }
    }
}
