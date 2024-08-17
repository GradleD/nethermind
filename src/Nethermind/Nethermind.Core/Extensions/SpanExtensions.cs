// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Buffers;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Nethermind.Core.Collections;
using Nethermind.Core.Crypto;

namespace Nethermind.Core.Extensions
{
    public static class SpanExtensions
    {
        // Ensure that hashes are different for every run of the node and every node, so if are any hash collisions on
        // one node they will not be the same on another node or across a restart so hash collision cannot be used to degrade
        // the performance of the network as a whole.
        private static readonly uint s_instanceRandom = (uint)System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue);

        public static string ToHexString(this in ReadOnlySpan<byte> span, bool withZeroX)
        {
            return ToHexString(span, withZeroX, false, false);
        }

        public static string ToHexString(this in Span<byte> span, bool withZeroX)
        {
            return ToHexViaLookup(span, withZeroX, false, false);
        }

        public static string ToHexString(this in ReadOnlySpan<byte> span, bool withZeroX, bool noLeadingZeros)
        {
            return ToHexViaLookup(span, withZeroX, noLeadingZeros, false);
        }

        public static string ToHexString(this in ReadOnlySpan<byte> span)
        {
            return ToHexString(span, false, false, false);
        }

        public static string ToHexString(this in Span<byte> span)
        {
            return ToHexViaLookup(span, false, false, false);
        }

        public static string ToHexString(this in ReadOnlySpan<byte> span, bool withZeroX, bool noLeadingZeros, bool withEip55Checksum)
        {
            return ToHexViaLookup(span, withZeroX, noLeadingZeros, withEip55Checksum);
        }

        public static string ToHexString(this in Span<byte> span, bool withZeroX, bool noLeadingZeros, bool withEip55Checksum)
        {
            return ToHexViaLookup(span, withZeroX, noLeadingZeros, withEip55Checksum);
        }

        [DebuggerStepThrough]
        private unsafe static string ToHexViaLookup(ReadOnlySpan<byte> bytes, bool withZeroX, bool skipLeadingZeros, bool withEip55Checksum)
        {
            if (withEip55Checksum)
            {
                return ToHexStringWithEip55Checksum(bytes, withZeroX, skipLeadingZeros);
            }
            if (bytes.Length == 0) return "";

            int leadingZeros = skipLeadingZeros ? Bytes.CountLeadingNibbleZeros(bytes) : 0;
            int length = bytes.Length * 2 + (withZeroX ? 2 : 0) - leadingZeros;

            if (skipLeadingZeros && length == (withZeroX ? 2 : 0))
            {
                return withZeroX ? "0x0" : "0";
            }

            fixed (byte* input = &Unsafe.Add(ref MemoryMarshal.GetReference(bytes), leadingZeros / 2))
            {
                var createParams = new StringParams(input, bytes.Length, leadingZeros, withZeroX);
                return string.Create(length, createParams, static (chars, state) =>
                {

                    Bytes.OutputBytesToCharHex(ref state.Input, state.InputLength, ref MemoryMarshal.GetReference(chars), state.WithZeroX, state.LeadingZeros);
                });
            }
        }

        unsafe readonly struct StringParams(byte* input, int inputLength, int leadingZeros, bool withZeroX)
        {
            private readonly byte* _input = input;
            public readonly int InputLength = inputLength;
            public readonly int LeadingZeros = leadingZeros;
            public readonly bool WithZeroX = withZeroX;

            public readonly ref byte Input => ref Unsafe.AsRef<byte>(_input);
        }

        private static string ToHexStringWithEip55Checksum(ReadOnlySpan<byte> bytes, bool withZeroX, bool skipLeadingZeros)
        {
            string hashHex = Keccak.Compute(bytes.ToHexString(false)).ToString(false);

            int leadingZeros = skipLeadingZeros ? Bytes.CountLeadingNibbleZeros(bytes) : 0;
            int length = bytes.Length * 2 + (withZeroX ? 2 : 0) - leadingZeros;
            if (leadingZeros >= 2)
            {
                bytes = bytes[(leadingZeros / 2)..];
            }
            char[] charArray = ArrayPool<char>.Shared.Rent(length);

            Span<char> chars = charArray.AsSpan(0, length);

            if (withZeroX)
            {
                // In reverse, bounds check on [1] will elide bounds check on [0]
                chars[1] = 'x';
                chars[0] = '0';
                // Trim off the two chars from the span
                chars = chars[2..];
            }

            uint[] lookup32 = Bytes.Lookup32;

            bool odd = (leadingZeros & 1) != 0;
            int oddity = odd ? 2 : 0;
            if (odd)
            {
                // Odd number of hex chars, handle the first
                // seperately so loop can work in pairs
                uint val = lookup32[bytes[0]];
                char char2 = (char)(val >> 16);
                chars[0] = char.IsLetter(char2) && hashHex[1] > '7'
                            ? char.ToUpper(char2)
                            : char2;

                // Trim off the first byte and char from the spans
                chars = chars[1..];
                bytes = bytes[1..];
            }

            for (int i = 0; i < chars.Length; i += 2)
            {
                uint val = lookup32[bytes[i / 2]];
                char char1 = (char)val;
                char char2 = (char)(val >> 16);

                chars[i] = char.IsLetter(char1) && hashHex[i + oddity] > '7'
                            ? char.ToUpper(char1)
                            : char1;
                chars[i + 1] = char.IsLetter(char2) && hashHex[i + 1 + oddity] > '7'
                            ? char.ToUpper(char2)
                            : char2;
            }

            string result = new string(charArray.AsSpan(0, length));
            ArrayPool<char>.Shared.Return(charArray);

            return result;
        }

        public static ReadOnlySpan<byte> TakeAndMove(this ref ReadOnlySpan<byte> span, int length)
        {
            ReadOnlySpan<byte> s = span[..length];
            span = span[length..];
            return s;
        }

        public static bool IsNullOrEmpty<T>(this in Span<T> span) => span.Length == 0;
        public static bool IsNull<T>(this in Span<T> span) => Unsafe.IsNullRef(ref MemoryMarshal.GetReference(span));
        public static bool IsNullOrEmpty<T>(this in ReadOnlySpan<T> span) => span.Length == 0;
        public static bool IsNull<T>(this in ReadOnlySpan<T> span) => Unsafe.IsNullRef(ref MemoryMarshal.GetReference(span));

        public static ArrayPoolList<T> ToPooledList<T>(this in ReadOnlySpan<T> span)
        {
            ArrayPoolList<T> newList = new ArrayPoolList<T>(span.Length);
            newList.AddRange(span);
            return newList;
        }

        [SkipLocalsInit]
        public static int FastHash(this ReadOnlySpan<byte> input)
        {
            var length = input.Length;
            if (length == 0) return 0;

            ref var b = ref MemoryMarshal.GetReference(input);

            // Start with instance random, length and first as seed
            uint hash = (s_instanceRandom + (uint)length) ^ b;

            // This is done below, without branches
            // if ((length & 1) == 1)
            // {
            //     hash = b;
            //     b = ref Unsafe.Add(ref b, 1);
            //     length -= 1;
            // }

            uint bit = (uint)length & sizeof(byte);
            b = ref Unsafe.Add(ref b, bit);
            length -= (int)bit;

            if ((length & sizeof(ushort)) != 0)
            {
                hash = BitOperations.Crc32C(hash, Unsafe.ReadUnaligned<ushort>(ref b));
                b = ref Unsafe.Add(ref b, sizeof(ushort));
                length -= sizeof(ushort);
            }
            if ((length & sizeof(uint)) != 0)
            {
                hash = BitOperations.Crc32C(hash, Unsafe.ReadUnaligned<uint>(ref b));
                b = ref Unsafe.Add(ref b, sizeof(uint));
                length -= sizeof(uint);
            }

            while (length >= sizeof(ulong) * 3)
            {
                // Crc32C is 3 cycle latency, 1 cycle throughput. However is complex to fully
                // break the dependency chain when most of our hashes are on <= 32 byte data.
                hash = BitOperations.Crc32C(hash, Unsafe.ReadUnaligned<ulong>(ref b));
                hash = BitOperations.Crc32C(hash, Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref b, sizeof(ulong))));
                hash = BitOperations.Crc32C(hash, Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref b, sizeof(ulong) * 2)));
                b = ref Unsafe.Add(ref b, sizeof(ulong) * 3);
                length -= sizeof(ulong) * 3;
            }

            while (length > 0)
            {
                hash = BitOperations.Crc32C(hash, Unsafe.ReadUnaligned<ulong>(ref b));
                b = ref Unsafe.Add(ref b, sizeof(ulong));
                length -= sizeof(ulong);
            }

            return (int)hash;
        }
    }
}
