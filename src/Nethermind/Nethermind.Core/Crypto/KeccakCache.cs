// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace Nethermind.Core.Crypto;

/// <summary>
/// This is a minimalistic one-way set associative cache for Keccak values.
///
/// It allocates only 8MB of memory to store 64k of entries.
/// No misaligned reads, requires a single CAS to lock.
/// Also, uses copying on the stack to get the entry, have it copied and release the lock ASAP.
/// </summary>
public static unsafe class KeccakCache
{
    /// <summary>
    /// This counts make the cache consume 8MB of continues memory
    /// </summary>
    private const int Count = BucketMask + 1;
    private const int BucketMask = 0x0000_FFFF;
    private const uint HashMask = 0xFFFF_0000;

    private static readonly Entry* Memory;

    static KeccakCache()
    {
        const UIntPtr size = Count * Entry.Size;

        // Aligned, so that no torn reads if fields of Entry are properly aligned.
        Memory = (Entry*)NativeMemory.AlignedAlloc(size, Entry.Size);
        NativeMemory.Clear(Memory, size);
    }

    [SkipLocalsInit]
    public static ValueHash256 Compute(ReadOnlySpan<byte> input)
    {
        if (input.Length > Entry.MaxPayloadLength)
        {
            return ValueKeccak.Compute(input);
        }

        var fast = FastHash(input);
        var index = fast & BucketMask;

        Debug.Assert(index is > 0 and < Count);

        uint hashAndLength = (fast & HashMask) | (ushort)input.Length;

        ref Entry e = ref Unsafe.Add(ref Unsafe.AsRef<Entry>(Memory), index);

        // Read aligned, volatile, won't be torn, check with computed
        if (Volatile.Read(ref e.HashAndLength) == hashAndLength)
        {
            // There's a possibility of a hit, try lock.
            if (Interlocked.CompareExchange(ref e.Lock, Entry.Locked, Entry.Unlocked) == Entry.Unlocked)
            {
                if (e.HashAndLength != hashAndLength)
                {
                    // The value has been changed between reading and taking a lock.
                    // Release the lock and compute, Use Volatile.Write to release?
                    Interlocked.Exchange(ref e.Lock, Entry.Unlocked);
                    goto Compute;
                }

                // Lock taken, copy to local
                Entry copy = e;

                // Release the lock, potentially Volatile.Write??
                Interlocked.Exchange(ref e.Lock, Entry.Unlocked);

                // Lengths are equal, the input length can be used without any additional operation.
                if (MemoryMarshal.CreateReadOnlySpan(ref copy.Payload, input.Length).SequenceEqual(input))
                {
                    return copy.Value;
                }
            }
        }

    Compute:
        var hash = ValueKeccak.Compute(input);

        // Try lock and memoize
        if (Interlocked.CompareExchange(ref e.Lock, Entry.Locked, Entry.Unlocked) == Entry.Unlocked)
        {
            e.HashAndLength = hashAndLength;
            e.Value = hash;

            input.CopyTo(MemoryMarshal.CreateSpan(ref e.Payload, input.Length));

            // Release the lock, potentially Volatile.Write??
            Interlocked.Exchange(ref e.Lock, Entry.Unlocked);
        }

        return hash;
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint FastHash(ReadOnlySpan<byte> input)
    {
        uint hash = 13;
        var length = input.Length;

        ref var b = ref MemoryMarshal.GetReference(input);
        if ((length & 1) == 1)
        {
            hash = b;
            b = ref Unsafe.Add(ref b, 1);
            length -= 1;
        }
        if ((length & 2) == 2)
        {
            hash = BitOperations.Crc32C(hash, Unsafe.ReadUnaligned<ushort>(ref b));
            b = ref Unsafe.Add(ref b, 2);
            length -= 2;
        }
        if ((length & 4) == 4)
        {
            hash = BitOperations.Crc32C(hash, Unsafe.ReadUnaligned<uint>(ref b));
            b = ref Unsafe.Add(ref b, 4);
            length -= 4;
        }

        while (length > 0)
        {
            hash = BitOperations.Crc32C(hash, Unsafe.ReadUnaligned<ulong>(ref b));
            b = ref Unsafe.Add(ref b, 8);
            length -= 8;
        }

        return hash;
    }

    /// <summary>
    /// An entry to cache keccak
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = Size)]
    private struct Entry
    {
        public const int Unlocked = 0;
        public const int Locked = 1;

        /// <summary>
        /// Should work for both ARM and x64 and be aligned.
        /// </summary>
        public const int Size = 128;

        private const int PayloadStart = 8;
        private const int ValueStart = Size - ValueHash256.MemorySize;
        public const int MaxPayloadLength = ValueStart - PayloadStart;

        [FieldOffset(0)]
        public int Lock;

        /// <summary>
        /// The mix of hash and length allows for a fast comparison.
        /// </summary>
        [FieldOffset(4)]
        public uint HashAndLength;

        [FieldOffset(PayloadStart)]
        public byte Payload;

        /// <summary>
        /// The actual value
        /// </summary>
        [FieldOffset(ValueStart)]
        public ValueHash256 Value;
    }
}
