using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Security.Cryptography;
using System.Text;

namespace Meridian.Storage.Archival;

/// <summary>
/// Versioned WAL digests. Version 1 retains the original SHA-256 text representation;
/// version 2 hashes a lossless, canonical PackBits encoding with BLAKE3-256.
/// </summary>
internal static class WalChecksum
{
    internal const int LegacyVersion = 1;
    internal const int CurrentVersion = 2;
    private const int StackBufferSize = 4608;

    internal static string Compute(long sequence, DateTime timestamp, string recordType, string payload,
        int version = CurrentVersion)
    {
        Span<byte> digest = stackalloc byte[32];
        ComputeCore(sequence, timestamp, recordType, payload, digest, version);
        return Convert.ToHexStringLower(digest);
    }

    // Both encoders initialize every byte included in the digest. Unused capacity is never read.
    [SkipLocalsInit]
    internal static void ComputeCore(long sequence, DateTime timestamp, string recordType, string payload,
        Span<byte> destination, int version = CurrentVersion)
    {
        if (version == LegacyVersion)
        {
            ComputeLegacyCore(sequence, timestamp, recordType, payload, destination);
            return;
        }
        if (version != CurrentVersion)
            throw new InvalidDataException($"Unsupported WAL checksum version {version}.");

        var typeLength = Encoding.UTF8.GetByteCount(recordType);
        var payloadLength = Encoding.UTF8.GetByteCount(payload);
        var length = checked(24 + typeLength + payloadLength);
        var packedCapacity = checked(length + (length + 127) / 128);
        byte[]? rentedRecord = null;
        byte[]? rentedPacked = null;
        Span<byte> record = length <= StackBufferSize
            ? stackalloc byte[StackBufferSize]
            : (rentedRecord = ArrayPool<byte>.Shared.Rent(length));
        Span<byte> packed = packedCapacity <= StackBufferSize
            ? stackalloc byte[StackBufferSize]
            : (rentedPacked = ArrayPool<byte>.Shared.Rent(packedCapacity));
        try
        {
            // Length framing distinguishes field boundaries, including embedded pipe characters.
            BinaryPrimitives.WriteInt64LittleEndian(record, sequence);
            BinaryPrimitives.WriteInt64LittleEndian(record[8..], timestamp.ToBinary());
            BinaryPrimitives.WriteInt32LittleEndian(record[16..], typeLength);
            BinaryPrimitives.WriteInt32LittleEndian(record[20..], payloadLength);
            Encoding.UTF8.GetBytes(recordType, record[24..]);
            Encoding.UTF8.GetBytes(payload, record[(24 + typeLength)..]);
            var written = EncodePackBits(record[..length], packed);
            // Exactly 32 bytes uses the stateless native one-shot API, without heap hash state.
            Blake3.Hasher.Hash(packed[..written], destination[..32]);
        }
        finally
        {
            if (rentedPacked is not null)
                ArrayPool<byte>.Shared.Return(rentedPacked);
            if (rentedRecord is not null)
                ArrayPool<byte>.Shared.Return(rentedRecord);
        }
    }

    /// <summary>
    /// Canonical PackBits: literals use tokens 0..127, runs use tokens 129..255.
    /// Detect runs of at least three bytes, split runs/literals at 128 bytes, and encode
    /// a one-byte run remainder as a separate literal. Token 128 is never emitted.
    /// </summary>
    internal static int EncodePackBits(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var read = 0;
        var written = 0;
        while (read < source.Length)
        {
            var nextRun = FindRun(source[read..]);
            var literalLength = nextRun < 0 ? source.Length - read : nextRun;
            while (literalLength > 0)
            {
                var count = Math.Min(128, literalLength);
                destination[written++] = (byte)(count - 1);
                source.Slice(read, count).CopyTo(destination[written..]);
                written += count;
                read += count;
                literalLength -= count;
            }
            if (nextRun < 0)
                break;

            var value = source[read];
            var different = source[(read + 3)..].IndexOfAnyExcept(value);
            var runLength = different < 0 ? source.Length - read : different + 3;
            read += runLength;
            while (runLength > 0)
            {
                var count = Math.Min(128, runLength);
                destination[written++] = count == 1 ? (byte)0 : (byte)(257 - count);
                destination[written++] = value;
                runLength -= count;
            }
        }
        return written;
    }

    private static int FindRun(ReadOnlySpan<byte> source)
    {
        var offset = 0;
        ref var start = ref MemoryMarshal.GetReference(source);
        if (Vector256.IsHardwareAccelerated)
        {
            for (; offset <= source.Length - 34; offset += 32)
            {
                var first = Vector256.LoadUnsafe(ref start, (nuint)offset);
                var second = Vector256.LoadUnsafe(ref start, (nuint)(offset + 1));
                var third = Vector256.LoadUnsafe(ref start, (nuint)(offset + 2));
                var mask = (Vector256.Equals(first, second) & Vector256.Equals(first, third))
                    .ExtractMostSignificantBits();
                if (mask != 0)
                    return offset + BitOperations.TrailingZeroCount(mask);
            }
        }
        else if (Vector128.IsHardwareAccelerated)
        {
            for (; offset <= source.Length - 18; offset += 16)
            {
                var first = Vector128.LoadUnsafe(ref start, (nuint)offset);
                var second = Vector128.LoadUnsafe(ref start, (nuint)(offset + 1));
                var third = Vector128.LoadUnsafe(ref start, (nuint)(offset + 2));
                var mask = (Vector128.Equals(first, second) & Vector128.Equals(first, third))
                    .ExtractMostSignificantBits();
                if (mask != 0)
                    return offset + BitOperations.TrailingZeroCount(mask);
            }
        }
        for (; offset < source.Length - 2; offset++)
        {
            if (source[offset] == source[offset + 1] && source[offset] == source[offset + 2])
                return offset;
        }
        return -1;
    }

    [SkipLocalsInit]
    private static void ComputeLegacyCore(long sequence, DateTime timestamp, string recordType, string payload,
        Span<byte> destination)
    {
        var length = checked(20 + 33 + 3 + Encoding.UTF8.GetByteCount(recordType) + Encoding.UTF8.GetByteCount(payload));
        byte[]? rented = null;
        Span<byte> buffer = length <= StackBufferSize
            ? stackalloc byte[StackBufferSize]
            : (rented = ArrayPool<byte>.Shared.Rent(length));
        try
        {
            if (!Utf8Formatter.TryFormat(sequence, buffer, out var written))
                throw new InvalidOperationException("Failed to format WAL sequence.");
            buffer[written++] = (byte)'|';
            if (!Utf8Formatter.TryFormat(timestamp, buffer[written..], out var timestampWritten, 'O'))
                throw new InvalidOperationException("Failed to format WAL timestamp.");
            written += timestampWritten;
            buffer[written++] = (byte)'|';
            written += Encoding.UTF8.GetBytes(recordType, buffer[written..]);
            buffer[written++] = (byte)'|';
            written += Encoding.UTF8.GetBytes(payload, buffer[written..]);
            if (!SHA256.TryHashData(buffer[..written], destination, out _))
                throw new InvalidOperationException("Failed to compute WAL checksum.");
        }
        finally
        {
            if (rented is not null)
                ArrayPool<byte>.Shared.Return(rented);
        }
    }
}
