// SPDX-FileCopyrightText: 2024 Demerzel Solutions Limited
// SPDX-License-Identifier: LGPL-3.0-only

using System;
using System.Buffers.Binary;
using System.Linq;
using System.Threading.Tasks;
using Nethermind.Core.Extensions;
using Nethermind.Logging;

namespace Nethermind.Optimism.Rpc;

public abstract class OptimismProtocolVersion : IEquatable<OptimismProtocolVersion>, IComparable<OptimismProtocolVersion>
{
    public const int ByteLength = 32;

    public class ParseException(string message) : Exception(message);

    private OptimismProtocolVersion() { }

    public static OptimismProtocolVersion Read(ReadOnlySpan<byte> span)
    {
        if (span.Length < ByteLength) throw new ParseException($"Expected at least {ByteLength} bytes");

        var version = span[0];
        return version switch
        {
            0 => V0.Read(span),
            _ => throw new ParseException($"Unsupported version: {version}")
        };
    }

    public abstract void Write(Span<byte> span);

    public abstract int CompareTo(OptimismProtocolVersion? other);

    public abstract bool Equals(OptimismProtocolVersion? other);

    public override bool Equals(object? obj) => obj is OptimismProtocolVersion other && Equals(other);
    public abstract override int GetHashCode();

    public static bool operator >(OptimismProtocolVersion left, OptimismProtocolVersion right) => left.CompareTo(right) > 0;
    public static bool operator <(OptimismProtocolVersion left, OptimismProtocolVersion right) => left.CompareTo(right) < 0;
    public static bool operator >=(OptimismProtocolVersion left, OptimismProtocolVersion right) => left.CompareTo(right) >= 0;
    public static bool operator <=(OptimismProtocolVersion left, OptimismProtocolVersion right) => left.CompareTo(right) <= 0;
    public static bool operator ==(OptimismProtocolVersion left, OptimismProtocolVersion right) => left.Equals(right);
    public static bool operator !=(OptimismProtocolVersion left, OptimismProtocolVersion right) => !left.Equals(right);

    public abstract override string ToString();

    public sealed class V0 : OptimismProtocolVersion
    {
        public byte[] Build { get; }
        public UInt32 Major { get; }
        public UInt32 Minor { get; }
        public UInt32 Patch { get; }
        public UInt32 PreRelease { get; }

        public V0(ReadOnlySpan<byte> build, UInt32 major, UInt32 minor, UInt32 patch, UInt32 preRelease)
        {
            Build = build.ToArray();
            Major = major;
            Minor = minor;
            Patch = patch;
            PreRelease = preRelease;
        }

        public V0(string version)
        {
            var parts = version.Split('.').Select(UInt32.Parse).ToList();
            if (parts.Count != 4) throw new ParseException($"Invalid version format: {version}");

            Build = new byte[8];
            Major = parts[0];
            Minor = parts[1];
            Patch = parts[2];
            PreRelease = parts[3];
        }

        public new static V0 Read(ReadOnlySpan<byte> span)
        {
            var version = span[0];
            if (version != 0) throw new ParseException($"Expected version 0, got {version}");

            var reserved = span.TakeAndMove(7);
            if (!reserved.IsZero()) throw new ParseException("Expected reserved bytes to be zero");

            var build = span.TakeAndMove(8);
            var major = BinaryPrimitives.ReadUInt32BigEndian(span.TakeAndMove(4));
            var minor = BinaryPrimitives.ReadUInt32BigEndian(span.TakeAndMove(4));
            var patch = BinaryPrimitives.ReadUInt32BigEndian(span.TakeAndMove(4));
            var preRelease = BinaryPrimitives.ReadUInt32BigEndian(span.TakeAndMove(4));

            return new V0(build, major, minor, patch, preRelease);
        }

        public override void Write(Span<byte> span)
        {
            span[0] = 0;

            span.TakeAndMove(7);

            Build.CopyTo(span.TakeAndMove(8));
            BinaryPrimitives.WriteUInt32BigEndian(span.TakeAndMove(4), Major);
            BinaryPrimitives.WriteUInt32BigEndian(span.TakeAndMove(4), Minor);
            BinaryPrimitives.WriteUInt32BigEndian(span.TakeAndMove(4), Patch);
            BinaryPrimitives.WriteUInt32BigEndian(span.TakeAndMove(4), PreRelease);
        }

        public override int CompareTo(OptimismProtocolVersion? other)
        {
            if (ReferenceEquals(this, other)) return 0;

            if (other is null) return 1;

            if (other is not V0 otherVersion) throw new ArgumentException("Object is not a valid OptimismProtocolVersion.V0", nameof(other));

            var majorComparison = Major.CompareTo(otherVersion.Major);
            if (majorComparison != 0) return majorComparison;

            var minorComparison = Minor.CompareTo(otherVersion.Minor);
            if (minorComparison != 0) return minorComparison;

            var patchComparison = Patch.CompareTo(otherVersion.Patch);
            if (patchComparison != 0) return patchComparison;

            return (PreRelease, otherVersion.PreRelease) switch
            {
                (0, 0) => 0,
                (0, _) => 1,
                (_, 0) => -1,
                _ => PreRelease.CompareTo(otherVersion.PreRelease)
            };
        }

        public override bool Equals(OptimismProtocolVersion? other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is null) return false;
            if (other is not V0 otherVersion) return false;

            return Build.SequenceEqual(otherVersion.Build)
                   && Major == otherVersion.Major
                   && Minor == otherVersion.Minor
                   && Patch == otherVersion.Patch
                   && PreRelease == otherVersion.PreRelease;
        }

        public override bool Equals(object? obj) => ReferenceEquals(this, obj) || obj is V0 other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Build, Major, Minor, Patch, PreRelease);

        public override string ToString() => $"v{Major}.{Minor}.{Patch}{(PreRelease == 0 ? "" : $"-{PreRelease}")}";
    }
}

public sealed class OptimismSuperchainSignal(
    OptimismProtocolVersion recommended,
    OptimismProtocolVersion required)
{
    public OptimismProtocolVersion Recommended { get; } = recommended;
    public OptimismProtocolVersion Required { get; } = required;
}

public interface IOptimismSuperchainSignalHandler
{
    OptimismProtocolVersion CurrentVersion { get; }
    Task OnBehindRecommended(OptimismProtocolVersion recommended);
    Task OnBehindRequired(OptimismProtocolVersion required);
}

public sealed class LoggingOptimismSuperchainSignalHandler(
    OptimismProtocolVersion currentVersion,
    ILogManager logManager
) : IOptimismSuperchainSignalHandler
{
    private readonly ILogger _logger = logManager.GetClassLogger();
    public OptimismProtocolVersion CurrentVersion { get; init; } = currentVersion;

    public Task OnBehindRecommended(OptimismProtocolVersion recommended)
    {
        _logger.Warn($"Current version {CurrentVersion} is behind recommended version {recommended}");
        return Task.CompletedTask;
    }

    public Task OnBehindRequired(OptimismProtocolVersion required)
    {
        _logger.Error($"Current version {CurrentVersion} is behind required version {required}");
        return Task.CompletedTask;
    }
}
