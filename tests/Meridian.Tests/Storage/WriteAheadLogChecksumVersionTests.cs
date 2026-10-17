using System.Globalization;
using FluentAssertions;
using Meridian.Storage.Archival;
using Meridian.Tests.Infrastructure;

namespace Meridian.Tests.Storage;

public sealed class WriteAheadLogChecksumVersionTests : TempDirectoryAsyncTestBase
{
    private static readonly DateTime Timestamp =
        new DateTime(2026, 10, 7, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234567);

    [Fact]
    public void V2_MatchesPinnedFramedPackBitsDigest()
    {
        // Independently generated from little-endian fields and a scalar Python PackBits
        // encoder, then hashed through libblake3 after checking its official empty vector.
        const string payload = "{\"symbol\":\"SPY\",\"price\":450.25}";
        var frame = WalChecksumReference.Frame(42, Timestamp, "Trade", payload);
        Convert.ToHexStringLower(frame).Should().Be(
            "2a00000000000000872ed0646f24df48050000001f00000054726164657b2273796d626f6c223a22535059222c227072696365223a3435302e32357d");
        Convert.ToHexStringLower(WalChecksumReference.PackBits(frame)).Should().Be(
            "002afa0008872ed0646f24df4805fe00001ffe002354726164657b2273796d626f6c223a22535059222c227072696365223a3435302e32357d");
        WalChecksum.Compute(42, Timestamp, "Trade", payload, version: 2)
            .Should().Be("01f48f4acb2b8e3e6cca2cc0b1693badf8811f384f09fa449c1b0c9646abe5fa");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    public void ComputeChecksum_RejectsUnsupportedVersions(int version)
    {
        var compute = () => WalChecksum.Compute(1, Timestamp, "Trade", "{}", version);
        compute.Should().Throw<InvalidDataException>();
    }

    [Theory]
    [InlineData(0, "af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262")]
    [InlineData(1, "2d3adedff11b61f14c886e35afa036736dcd87a74d27b5c1510225d0f592e213")]
    [InlineData(63, "e9bc37a594daad83be9470df7f7b3798297c3d834ce80ba85d6e207627b7db7b")]
    [InlineData(64, "4eed7141ea4a5cd4b788606bd23f46e212af9cacebacdc7d1f4c6dc7f2511b98")]
    [InlineData(65, "de1e5fa0be70df6d2be8fffd0e99ceaa8eb6e8c93a63f2d8d1c30ecb6b263dee")]
    [InlineData(1023, "10108970eeda3eb932baac1428c7a2163b0e924c9a9e25b35bba72b28f70bd11")]
    [InlineData(1024, "42214739f095a406f3fc83deb889744ac00df831c10daa55189b5d121c855af7")]
    [InlineData(1025, "d00278ae47eb27b34faecf67b4fe263f82d5412916c1ffd97c8cb7fb814b8444")]
    [InlineData(4096, "015094013f57a5277b59d8475c0501042c0b642e531b0a1c8f58d2163229e969")]
    public void Blake3Native_MatchesOfficialUnkeyed256BitVectors(int length, string expected)
    {
        // The official BLAKE3 test-vector input is byte[i] = i % 251.
        var input = Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray();
        Span<byte> digest = stackalloc byte[32];
        Blake3.Hasher.Hash(input, digest);
        Convert.ToHexStringLower(digest).Should().Be(expected);
    }

    [Fact]
    public void PackBits_MatchesScalarOracleAndRoundtripsEveryBoundaryPattern()
    {
        var patterns = new List<byte[]> { Array.Empty<byte>() };
        var random = new Random(3109);
        foreach (var length in new[] { 1, 2, 3, 15, 16, 17, 31, 32, 33, 63, 64, 65, 127, 128, 129, 130, 255, 256, 257, 384, 1023, 1024, 1025, 4096 })
        {
            patterns.Add(Enumerable.Repeat((byte)0, length).ToArray());
            patterns.Add(Enumerable.Repeat((byte)255, length).ToArray());
            patterns.Add(Enumerable.Range(0, length).Select(i => (byte)(i % 251)).ToArray());
            var bytes = new byte[length];
            random.NextBytes(bytes);
            patterns.Add(bytes);
        }

        foreach (var offset in new[] { 0, 1, 15, 16, 17, 30, 31, 32, 33, 62, 63, 64, 65, 126, 127, 128, 129, 253, 254, 255 })
            foreach (var run in new[] { 3, 127, 128, 129, 130, 256, 257 })
            {
                // Triples just before and after vector/literal boundaries catch scanner omissions;
                // 129/130-byte runs require canonical one-byte/two-byte final packets.
                var prefix = Enumerable.Range(0, offset).Select(i => (byte)(i % 251));
                patterns.Add(prefix.Concat(Enumerable.Repeat((byte)252, run))
                    .Concat(new byte[] { 7, 8, 9, 10 }).ToArray());
            }

        foreach (var source in patterns)
        {
            var destination = new byte[source.Length + (source.Length + 127) / 128];
            var written = WalChecksum.EncodePackBits(source, destination);
            written.Should().BeInRange(0, destination.Length);
            var encoded = destination.AsSpan(0, written).ToArray();
            encoded.Should().Equal(WalChecksumReference.PackBits(source));
            DecodePackBits(encoded).Should().Equal(source);
        }
    }

    [Fact]
    public void V2_LengthFramingDistinguishesAmbiguousV1FieldSeparators()
    {
        var first = WalChecksum.Compute(1, Timestamp, "Trade|tail", "payload", version: 2);
        var second = WalChecksum.Compute(1, Timestamp, "Trade", "tail|payload", version: 2);
        first.Should().NotBe(second, "type and payload boundaries are independently encoded");
        first.Should().Be(WalChecksumReference.Compute(1, Timestamp, "Trade|tail", "payload", version: 2));
        second.Should().Be(WalChecksumReference.Compute(1, Timestamp, "Trade", "tail|payload", version: 2));
    }

    [Fact]
    public async Task Recovery_MixedVersionsResumesSequenceAndWritesV2CommitMarkers()
    {
        await WriteFixture("01-legacy.wal", 1, Record(1, "Trade", "{\"text\":\"legacy|payload\"}", 1));
        await WriteFixture("02-modern.wal", 2, Record(2, "Trade", "{\"text\":\"modern\"}", 2));

        await using (var wal = new WriteAheadLog(TestDataRoot, Options()))
        {
            await wal.InitializeAsync();
            wal.LastRecoveryEventCount.Should().Be(2);
            var appended = await wal.AppendAsync("new payload", "Trade");
            appended.Sequence.Should().Be(3, "both versions contribute to the recovered sequence");
            appended.Checksum.Should().Be(WalChecksumReference.Compute(
                appended.Sequence, appended.Timestamp, appended.RecordType, appended.Payload, version: 2));
            await wal.CommitAsync(2);
            var remaining = await ReadUncommitted(wal);
            remaining.Select(r => r.Sequence).Should().Equal(3);
            wal.CorruptedRecordCount.Should().Be(0);
        }

        var newFiles = Directory.GetFiles(TestDataRoot, "wal_*.wal");
        newFiles.Should().ContainSingle();
        var lines = await File.ReadAllLinesAsync(newFiles[0]);
        lines[0].Should().StartWith("MDCWAL02|2|");
        var commitFields = lines.Single(line => line.Contains("|COMMIT|", StringComparison.Ordinal)).Split('|', 5);
        var commitTimestamp = DateTime.Parse(commitFields[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        commitFields[3].Should().Be(WalChecksumReference.Compute(
            long.Parse(commitFields[0], CultureInfo.InvariantCulture), commitTimestamp,
            commitFields[2], commitFields[4], version: 2));

        await using var restarted = new WriteAheadLog(TestDataRoot, Options());
        await restarted.InitializeAsync();
        var replay = await ReadUncommitted(restarted);
        replay.Select(r => r.Sequence).Should().Equal(3);
        (await restarted.AppendAsync("after restart", "Trade")).Sequence.Should().Be(5,
            "the v2 commit marker also consumes a sequence number");
    }

    [Fact]
    public async Task Rotation_WritesOnlyV2HeadersAndRecoverableRecords()
    {
        await using (var wal = new WriteAheadLog(TestDataRoot, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Halt,
            MaxWalFileSizeBytes = 1
        }))
        {
            await wal.InitializeAsync();
            for (var i = 0; i < 3; i++)
            {
                await wal.AppendAsync(new { Value = i }, "Trade");
            }
            await wal.FlushAsync();
        }

        var files = Directory.GetFiles(TestDataRoot, "*.wal");
        files.Should().HaveCountGreaterThan(1);
        foreach (var file in files)
        {
            (await File.ReadAllLinesAsync(file))[0].Should().StartWith("MDCWAL02|2|");
        }
        await using var restarted = new WriteAheadLog(TestDataRoot, Options());
        await restarted.InitializeAsync();
        restarted.LastRecoveryEventCount.Should().Be(3);
        restarted.CorruptedRecordCount.Should().Be(0);
    }

    [Fact]
    public async Task Repair_MixedVersionsKeepsOriginalHeadersAndChecksums()
    {
        var legacy = Record(1, "Trade", "{\"text\":\"legacy\"}", 1);
        var modern = Record(2, "Trade", "{\"text\":\"modern\"}", 2);
        var legacyPath = await WriteFixture("01-legacy.wal", 1, legacy, legacy + "changed");
        var modernPath = await WriteFixture("02-modern.wal", 2, modern, modern + "changed");
        await WriteFixture("03-last-recovered.wal", 2, Record(3, "Trade", "{}", 2));

        await using (var wal = new WriteAheadLog(TestDataRoot, Options(WalCorruptionMode.Skip)))
        {
            await wal.InitializeAsync();
            var result = await wal.RepairAsync();
            result.ValidRecords.Should().Be(3);
            result.CorruptedRecords.Should().Be(2);
            result.RepairedFiles.Should().Be(2);
        }

        (await File.ReadAllLinesAsync(legacyPath)).Should().Equal(Header(1), legacy);
        (await File.ReadAllLinesAsync(modernPath)).Should().Equal(Header(2), modern);
        await using var restarted = new WriteAheadLog(TestDataRoot, Options());
        await restarted.InitializeAsync();
        restarted.LastRecoveryEventCount.Should().Be(3);
        restarted.CorruptedRecordCount.Should().Be(0);
    }

    [Theory]
    [InlineData("MDCWAL01|2|2026-10-07T12:34:56.1234567Z")]
    [InlineData("MDCWAL02|1|2026-10-07T12:34:56.1234567Z")]
    [InlineData("MDCWAL02|3|2026-10-07T12:34:56.1234567Z")]
    [InlineData("MDCWAL03|3|2026-10-07T12:34:56.1234567Z")]
    [InlineData("MDCWAL01")]
    [InlineData("MDCWAL02")]
    [InlineData("MDCWAL02|x|2026-10-07T12:34:56.1234567Z")]
    [InlineData("MDCWAL02|2")]
    [InlineData("MDCWAL02|2|not-a-timestamp")]
    [InlineData("MDCWAL02|2|2026-10-07T12:34:56.1234567Z|unexpected")]
    public async Task Recovery_RecognizedUnsupportedOrMalformedHeadersHaltEveryCorruptionMode(string header)
    {
        var path = Path.Combine(TestDataRoot, "retained.wal");
        await File.WriteAllLinesAsync(path, new[] { header, Record(1, "Trade", "{}", 1) });
        var original = await File.ReadAllBytesAsync(path);
        foreach (var mode in new[] { WalCorruptionMode.Skip, WalCorruptionMode.Alert, WalCorruptionMode.Halt })
        {
            await using var wal = new WriteAheadLog(TestDataRoot, Options(mode));
            var initialize = async () => await wal.InitializeAsync();
            await initialize.Should().ThrowAsync<InvalidDataException>(
                "an unsupported segment cannot safely supply recovery sequence or commit state");
            (await File.ReadAllBytesAsync(path)).Should().Equal(original);
        }
    }

    [Fact]
    public async Task Repair_UnsupportedVersionPreservesEntireFile()
    {
        var path = Path.Combine(TestDataRoot, "unsupported.wal");
        await File.WriteAllLinesAsync(path, new[]
        {
            "MDCWAL03|3|2026-10-07T12:34:56.1234567Z", Record(1, "Trade", "{}", 1), "damaged"
        });
        var original = await File.ReadAllBytesAsync(path);
        await using var wal = new WriteAheadLog(TestDataRoot, Options(WalCorruptionMode.Skip));
        var repair = await wal.RepairAsync();
        repair.RepairedFiles.Should().Be(0);
        (await File.ReadAllBytesAsync(path)).Should().Equal(original);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Truncate_UnsupportedVersionCannotBeDeleted(bool usesSegmentMetadata)
    {
        await using var wal = new WriteAheadLog(TestDataRoot, new WalOptions
        {
            SyncMode = WalSyncMode.NoSync,
            CorruptionMode = WalCorruptionMode.Skip,
            ArchiveAfterTruncate = false
        });
        await wal.InitializeAsync();
        var name = usesSegmentMetadata ? "wal_20000101_000000_000000000000.wal" : "foreign.wal";
        var path = Path.Combine(TestDataRoot, name);
        await File.WriteAllLinesAsync(path, new[] { "MDCWAL03|3|2026-10-07T12:34:56.1234567Z", "retained data" });
        var original = await File.ReadAllBytesAsync(path);
        try
        {
            await wal.TruncateAsync(long.MaxValue);
        }
        catch (InvalidDataException)
        {
            // A scan may reject the unsupported format before reaching the deletion gate.
        }
        (await File.ReadAllBytesAsync(path)).Should().Equal(original,
            "neither metadata bounds nor an unreadable record scan justify deleting an unknown format");
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    public async Task Recovery_HeaderCannotRelabelTheChecksumAlgorithm(int headerVersion, int checksumVersion)
    {
        await WriteFixture("relabeled.wal", headerVersion, Record(1, "Trade", "{}", checksumVersion));
        await using var wal = new WriteAheadLog(TestDataRoot, Options());
        var initialize = async () => await wal.InitializeAsync();
        await initialize.Should().ThrowAsync<InvalidDataException>();
    }

    internal static string Header(int version) =>
        $"MDCWAL{version:D2}|{version}|2026-10-07T12:34:56.1234567Z";

    private static string Record(long sequence, string recordType, string payload, int version)
    {
        var checksum = WalChecksumReference.Compute(sequence, Timestamp, recordType, payload, version);
        return FormattableString.Invariant($"{sequence}|{Timestamp:O}|{recordType}|{checksum}|{payload}");
    }

    private async Task<string> WriteFixture(string name, int version, params string[] records)
    {
        var path = Path.Combine(TestDataRoot, name);
        await File.WriteAllLinesAsync(path, new[] { Header(version) }.Concat(records));
        return path;
    }

    private static WalOptions Options(WalCorruptionMode mode = WalCorruptionMode.Halt) => new()
    {
        SyncMode = WalSyncMode.NoSync,
        CorruptionMode = mode
    };

    private static async Task<List<WalRecord>> ReadUncommitted(WriteAheadLog wal)
    {
        var records = new List<WalRecord>();
        await foreach (var record in wal.GetUncommittedRecordsAsync())
        {
            records.Add(record);
        }
        return records;
    }

    private static byte[] DecodePackBits(byte[] encoded)
    {
        using var decoded = new MemoryStream();
        var position = 0;
        while (position < encoded.Length)
        {
            var token = encoded[position++];
            token.Should().NotBe(128, "the canonical encoder never emits the PackBits no-op token");
            if (token <= 127)
            {
                var count = token + 1;
                decoded.Write(encoded, position, count);
                position += count;
            }
            else
            {
                var value = encoded[position++];
                for (var i = 0; i < 257 - token; i++)
                {
                    decoded.WriteByte(value);
                }
            }
        }
        return decoded.ToArray();
    }
}
