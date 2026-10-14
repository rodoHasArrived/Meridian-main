using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Meridian.Storage.Archival;
using Meridian.Tests.Infrastructure;

namespace Meridian.Tests.Storage;

/// <summary>
/// Compatibility and corruption checks for both retained WAL checksum formats.
/// Reference encoders are independent of the optimized production path.
/// </summary>
public sealed class WriteAheadLogChecksumTests : TempDirectoryAsyncTestBase
{
    private static readonly DateTime Timestamp =
        new DateTime(2026, 10, 7, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234567);

    [Fact]
    public void ComputeChecksum_MatchesRetainedV1Digest()
    {
        WalChecksum.Compute(42, Timestamp, "Trade", "{\"symbol\":\"SPY\",\"price\":450.25}", version: 1)
            .Should().Be("d17d8a97cc9f2ad12fdbd3e142e82fc67521aee3b4715468a010a0734ad1d079");
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(long.MaxValue)]
    public void ComputeChecksum_PreservesSequenceAndRoundtripTimestampFormatting(long sequence)
    {
        foreach (var timestamp in new[]
        {
            Timestamp,
            DateTime.SpecifyKind(Timestamp, DateTimeKind.Unspecified),
            DateTime.SpecifyKind(Timestamp, DateTimeKind.Local),
            DateTime.MinValue,
            DateTime.MaxValue
        })
            foreach (var version in new[] { 1, 2 })
            {
                WalChecksum.Compute(sequence, timestamp, "Trade", "{}", version)
                    .Should().Be(WalChecksumReference.Compute(sequence, timestamp, "Trade", "{}", version),
                        "sequence {0} and timestamp {1:O} must match the selected checksum format",
                        sequence, timestamp);
            }
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    public void V2_ChecksumSurvivesTimestampTextRoundtrip(DateTimeKind kind)
    {
        var timestamp = DateTime.SpecifyKind(Timestamp, kind);
        var recovered = DateTime.Parse(timestamp.ToString("O", CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        WalChecksum.Compute(42, timestamp, "Trade", "{}", version: 2)
            .Should().Be(WalChecksum.Compute(42, recovered, "Trade", "{}", version: 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(130)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    [InlineData(511)]
    [InlineData(512)]
    [InlineData(1023)]
    [InlineData(1024)]
    [InlineData(4095)]
    [InlineData(4096)]
    [InlineData(4546)]
    [InlineData(4547)]
    [InlineData(4548)]
    [InlineData(8192)]
    [InlineData(65536)]
    public void ComputeChecksum_MatchesReferenceAcrossPayloadAndBufferBoundaries(int length)
    {
        foreach (var payload in new[]
        {
            new string('x', length),
            new string('\u20ac', length) + "\U0001f4c8|\u6771\u4eac",
            string.Concat(Enumerable.Range(0, length).Select(i => (char)('!' + i % 90)))
        })
            foreach (var version in new[] { 1, 2 })
            {
                WalChecksum.Compute(42, Timestamp, "Trade", payload, version)
                    .Should().Be(WalChecksumReference.Compute(42, Timestamp, "Trade", payload, version));
            }
    }

    [Fact]
    public void ComputeChecksum_PreservesUtf8ReplacementFallback()
    {
        foreach (var text in new[] { "\ud800", "\udc00", "\ud800x\udc00", "\ud800\udc00", "x\ud800\ud800y" })
            foreach (var version in new[] { 1, 2 })
            {
                WalChecksum.Compute(42, Timestamp, text, "prefix|" + text, version)
                    .Should().Be(WalChecksumReference.Compute(42, Timestamp, text, "prefix|" + text, version));
            }
    }

    [Fact]
    public void ComputeChecksum_LargeRecordTypesPreserveFieldBoundaries()
    {
        foreach (var length in new[] { 512, 4096, 8192 })
        {
            var recordType = new string('\u53d6', length);
            foreach (var payload in new[] { string.Empty, "|payload" })
                foreach (var version in new[] { 1, 2 })
                {
                    WalChecksum.Compute(42, Timestamp, recordType, payload, version)
                        .Should().Be(WalChecksumReference.Compute(42, Timestamp, recordType, payload, version));
                }
        }
    }

    [Theory]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    public void ComputeChecksum_IsIndependentOfCurrentCulture(string cultureName)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            foreach (var version in new[] { 1, 2 })
            {
                WalChecksum.Compute(long.MinValue, Timestamp, "Trade", "1234.56", version)
                    .Should().Be(WalChecksumReference.Compute(long.MinValue, Timestamp, "Trade", "1234.56", version));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void ComputeChecksum_ConcurrentCallsKeepBuffersAndHashStateIsolated()
    {
        Parallel.For(0, 96, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
        {
            // Alternate pooled and small buffers to catch stale tails and shared hash state.
            var payload = new string(i % 2 == 0 ? '\u20ac' : 'x', i % 3 == 0 ? 8192 : 65) + i;
            var recordType = "Trade" + i;
            foreach (var version in new[] { 1, 2 })
            {
                WalChecksum.Compute(i, Timestamp.AddTicks(i), recordType, payload, version)
                    .Should().Be(WalChecksumReference.Compute(i, Timestamp.AddTicks(i), recordType, payload, version));
            }
        });
    }

    [Fact]
    public async Task AppendAndRecovery_PreserveReferenceChecksumsAndPayloadPipes()
    {
        var expected = new List<WalRecord>();
        await using (var seed = new WriteAheadLog(TestDataRoot, Options()))
        {
            await seed.InitializeAsync();
            foreach (var text in new[] { new string('x', 8192), "small|payload", "\u6771\u4eac|\U0001f4c8" })
            {
                var record = await seed.AppendAsync(new { Text = text }, "\u53d6\u5f15");
                record.Checksum.Should().Be(WalChecksumReference.Compute(
                    record.Sequence, record.Timestamp, record.RecordType, record.Payload, version: 2));
                expected.Add(record);
            }
            await seed.FlushAsync();
        }

        await using var recovered = new WriteAheadLog(TestDataRoot, Options());
        await recovered.InitializeAsync();
        recovered.CorruptedRecordCount.Should().Be(0);
        var records = new List<WalRecord>();
        await foreach (var record in recovered.GetUncommittedRecordsAsync())
        {
            records.Add(record);
        }
        records.Should().BeEquivalentTo(expected, options => options.WithStrictOrdering());
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(1, 3)]
    [InlineData(1, 4)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(2, 3)]
    [InlineData(2, 4)]
    public async Task Recovery_RejectsEveryChangedChecksummedField(int version, int field)
    {
        const string payload = "{\"symbol\":\"SPY\",\"price\":450.25}";
        var checksum = WalChecksumReference.Compute(42, Timestamp, "Trade", payload, version);
        var fields = new[] { "42", Timestamp.ToString("O", CultureInfo.InvariantCulture), "Trade", checksum, payload };
        fields[field] = field switch
        {
            0 => "43",
            1 => Timestamp.AddTicks(1).ToString("O", CultureInfo.InvariantCulture),
            2 => "Quote",
            3 => checksum.ToUpperInvariant(),
            _ => "{\"symbol\":\"SPY\",\"price\":450.26}"
        };
        await File.WriteAllLinesAsync(Path.Combine(TestDataRoot, "retained-v1.wal"),
            new[] { WriteAheadLogChecksumVersionTests.Header(version), string.Join('|', fields) });

        await using var wal = new WriteAheadLog(TestDataRoot, Options());
        var initialize = async () => await wal.InitializeAsync();
        await initialize.Should().ThrowAsync<InvalidDataException>(
            "changing field {0} must invalidate the retained checksum", field);
    }

    private static WalOptions Options() => new()
    {
        SyncMode = WalSyncMode.NoSync,
        CorruptionMode = WalCorruptionMode.Halt
    };

}

/// <summary>Simple allocating reference used only by integrity tests.</summary>
internal static class WalChecksumReference
{
    internal static string Compute(long sequence, DateTime timestamp, string recordType, string payload, int version)
    {
        if (version == 1)
        {
            var retainedText = string.Concat(
                sequence.ToString(CultureInfo.InvariantCulture), "|",
                timestamp.ToString("O", CultureInfo.InvariantCulture), "|", recordType, "|", payload);
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(retainedText)));
        }

        var frame = Frame(sequence, timestamp, recordType, payload);
        var encoded = PackBits(frame);
        Span<byte> digest = stackalloc byte[32];
        Blake3.Hasher.Hash(encoded, digest);
        return Convert.ToHexStringLower(digest);
    }

    internal static byte[] Frame(long sequence, DateTime timestamp, string recordType, string payload)
    {
        var typeBytes = Encoding.UTF8.GetBytes(recordType);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var frame = new byte[24 + typeBytes.Length + payloadBytes.Length];
        BinaryPrimitives.WriteInt64LittleEndian(frame, sequence);
        BinaryPrimitives.WriteInt64LittleEndian(frame.AsSpan(8), timestamp.ToBinary());
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(16), typeBytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(20), payloadBytes.Length);
        typeBytes.CopyTo(frame, 24);
        payloadBytes.CopyTo(frame, 24 + typeBytes.Length);
        return frame;
    }

    internal static byte[] PackBits(byte[] source)
    {
        using var encoded = new MemoryStream();
        var position = 0;
        while (position < source.Length)
        {
            var runLength = RunLength(source, position);
            if (runLength >= 3)
            {
                while (runLength > 0)
                {
                    var count = Math.Min(runLength, 128);
                    encoded.WriteByte(count == 1 ? (byte)0 : (byte)(257 - count));
                    encoded.WriteByte(source[position]);
                    position += count;
                    runLength -= count;
                }
            }
            else
            {
                var start = position++;
                while (position < source.Length && position - start < 128 && RunLength(source, position) < 3)
                {
                    position++;
                }
                encoded.WriteByte((byte)(position - start - 1));
                encoded.Write(source, start, position - start);
            }
        }
        return encoded.ToArray();
    }

    private static int RunLength(byte[] source, int position)
    {
        var end = position + 1;
        while (end < source.Length && source[end] == source[position])
        {
            end++;
        }
        return end - position;
    }
}
