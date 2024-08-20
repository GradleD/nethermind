// SPDX-FileCopyrightText: 2022 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Nethermind.Core.Crypto;
using Nethermind.Core.Extensions;
using NUnit.Framework;

namespace Nethermind.Core.Test
{
    [TestFixture]
    public class KeccakCacheTests
    {
        [Test]
        public void Multiple()
        {
            const int spins = 10;

            var random = new Random(13);
            var bytes = new byte[31]; // misaligned length
            random.NextBytes(bytes);

            ValueHash256 expected = ValueKeccak.Compute(bytes);

            for (int i = 0; i < spins; i++)
            {
                ValueHash256 actual = KeccakCache.Compute(bytes);
                actual.Equals(expected).Should().BeTrue();
            }
        }

        [Test]
        public void Empty()
        {
            ReadOnlySpan<byte> span = ReadOnlySpan<byte>.Empty;
            KeccakCache.Compute(span).Should().Be(ValueKeccak.Compute(span));
        }

        [Test]
        public void Very_long()
        {
            ReadOnlySpan<byte> span = new byte[192];
            KeccakCache.Compute(span).Should().Be(ValueKeccak.Compute(span));
        }

        [Test]
        [Explicit("Used to create collisions")]
        public void Print_collisions()
        {
            var random = new Random(13);
            Span<byte> span = stackalloc byte[32];

            random.NextBytes(span);
            var bucket = KeccakCache.GetBucket(span);

            Console.WriteLine(span.ToHexString());

            var found = 1;

            while (found < 4)
            {
                random.NextBytes(span);
                if (KeccakCache.GetBucket(span) == bucket)
                {
                    Console.WriteLine(span.ToHexString());
                    found++;
                }
            }
        }

        [Test]
        public void Collision()
        {
            var colliding = new[]
            {
                "50f78269ea2ddd2d6ab4338fd5c7909c229561e565f6b04b9447b0bd73585687",
                "de75b3e495a58811469fb21345c7c1f84db0a3e1a3bf628c5689b53520af94de",
                "82be999650f45409208eacb42f357695bca746f58fb35c0a4a4d09d5a2ac066a",
                "f71034d862639845003bdc2d0d30ed1f8bd24c77573026fe9b838f33e72dcc6d",
            };

            var collisions = colliding.Length;
            var array = colliding.Select(c => Bytes.FromHexString(c)).ToArray();
            var values = array.Select(a => ValueKeccak.Compute(a)).ToArray();

            var bucket = KeccakCache.GetBucket(array[0]);

            for (int i = 1; i < collisions; i++)
            {
                var input = array[i];
                bucket.Should().Be(KeccakCache.GetBucket(input));
                KeccakCache.Compute(input).Should().Be(values[i]);
            }

            Parallel.ForEach(array, (a, state, index) =>
            {
                ValueHash256 v = values[index];

                for (int i = 0; i < 100_000; i++)
                {
                    KeccakCache.Compute(a).Should().Be(v);
                }
            });
        }

        [Test]
        public void Spin_through_all()
        {
            Span<byte> span = stackalloc byte[4];
            for (int i = 0; i < KeccakCache.Count; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(span, i);
                KeccakCache.Compute(span).Should().Be(ValueKeccak.Compute(span));
            }
        }
    }
}