using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

// Diagnostic only. Both package-specific projects compile this same source.
// FullCore copies the original production record encoding, buffer sizing,
// and stack/pool behavior. Only the final hash implementation varies.
// Fixed and varied fixtures are allocated before timing. No result caching.
internal static class Program
{
#if MANAGED_BLAKE3
    private const string Package = "Blake3-managed-3.0.2";
#else
    private const string Package = "Blake3.Native-3.0.2";
#endif
    private enum Algorithm { Sha256Static, Sha256Reuse, Blake3OneShot }
    private enum Path { HashOnlyPayload, HashOnlyEncoded, FullCore }
    private sealed record Fixture(long Sequence, DateTime Timestamp, string RecordType,
                                  string Payload, byte[] PayloadBytes, byte[] Encoded);
    private static readonly SHA256 ReusedSha256 = SHA256.Create();
    private static int _sink;

    private static int Main(string[] args)
    {
        int iterations = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 100_000;
        int repetitions = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 5;
        if (iterations <= 0 || repetitions <= 0) return 2;
        Console.Error.WriteLine($"Package={Package}; runtime={Environment.Version}; architecture={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}; iterations={iterations}; repetitions={repetitions}; StopwatchFrequency={Stopwatch.Frequency}");
        ValidateBlake3Vectors();
        Console.WriteLine("package,payload_bytes,encoded_bytes,input_pattern,path,algorithm,repetition,iterations,mean_ns,allocated_bytes_per_event,sink");
        foreach (int size in new[] { 64, 900, 4096 })
        {
            var fixtures = CreateFixtures(size);
            ValidateCore(fixtures);
            foreach (bool varied in new[] { false, true })
            foreach (Path path in Enum.GetValues<Path>())
            foreach (Algorithm algorithm in Enum.GetValues<Algorithm>())
            {
                // Warm package dispatch, native loading, formatting, UTF8, pool,
                // delegates, and JIT before counting steady-state allocations.
                Run(fixtures, varied, path, algorithm, 20_000);
                for (int rep = 1; rep <= repetitions; rep++)
                {
                    long beforeAllocated = GC.GetAllocatedBytesForCurrentThread();
                    long start = Stopwatch.GetTimestamp();
                    Run(fixtures, varied, path, algorithm, iterations);
                    long elapsed = Stopwatch.GetTimestamp() - start;
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - beforeAllocated;
                    double ns = elapsed * (1_000_000_000.0 / Stopwatch.Frequency) / iterations;
                    Console.WriteLine(FormattableString.Invariant($"{Package},{size},{fixtures[0].Encoded.Length},{(varied ? "varied256" : "fixed")},{path},{algorithm},{rep},{iterations},{ns:F3},{(double)allocated / iterations:F6},{_sink}"));
                }
            }
        }
        ReusedSha256.Dispose();
        return 0;
    }

    private static Fixture[] CreateFixtures(int size)
    {
        string type = size == 64 ? "Trade" : "L2Snapshot";
        long sequence = size == 64 ? 1 : size == 900 ? 2 : 3;
        DateTime timestamp = new(2024, 1, 15, 14, 30, 0, DateTimeKind.Utc);
        var fixtures = new Fixture[256];
        for (int i = 0; i < fixtures.Length; ++i)
        {
            var chars = new string('x', size).ToCharArray();
            if (i != 0)
            {
                chars[0] = (char)('a' + i % 26);
                chars[^1] = (char)('A' + i % 26);
            }
            string payload = new(chars);
            long currentSequence = sequence + i;
            DateTime currentTimestamp = timestamp.AddTicks(i);
            byte[] encoded = Encoding.UTF8.GetBytes(FormattableString.Invariant($"{currentSequence}|{currentTimestamp:O}|{type}|{payload}"));
            fixtures[i] = new(currentSequence, currentTimestamp, type, payload,
                              Encoding.UTF8.GetBytes(payload), encoded);
        }
        return fixtures;
    }

    private static void Run(Fixture[] fixtures, bool varied, Path path, Algorithm algorithm, int iterations)
    {
        Span<byte> output = stackalloc byte[32];
        int sink = _sink;
        for (int i = 0; i < iterations; ++i)
        {
            Fixture fixture = fixtures[varied ? i & 255 : 0];
            switch (path)
            {
                case Path.HashOnlyPayload:
                    Hash(fixture.PayloadBytes, output, algorithm);
                    break;
                case Path.HashOnlyEncoded:
                    Hash(fixture.Encoded, output, algorithm);
                    break;
                case Path.FullCore:
                    FullCore(fixture.Sequence, fixture.Timestamp, fixture.RecordType,
                             fixture.Payload, output, algorithm);
                    break;
            }
            sink = unchecked(sink + output[0]);
        }
        _sink = sink;
    }

    private static void Hash(ReadOnlySpan<byte> source, Span<byte> output, Algorithm algorithm)
    {
        switch (algorithm)
        {
            case Algorithm.Sha256Static:
                if (!SHA256.TryHashData(source, output, out int staticWritten) || staticWritten != 32)
                    throw new CryptographicException();
                break;
            case Algorithm.Sha256Reuse:
                if (!ReusedSha256.TryComputeHash(source, output, out int reusedWritten) || reusedWritten != 32)
                    throw new CryptographicException();
                break;
            case Algorithm.Blake3OneShot:
                Blake3.Hasher.Hash(source, output);
                break;
        }
    }

    private static void FullCore(long sequence, DateTime timestamp, string recordType,
                                 string payload, Span<byte> destination, Algorithm algorithm)
    {
        var recordTypeByteCount = Encoding.UTF8.GetByteCount(recordType);
        var payloadByteCount = Encoding.UTF8.GetByteCount(payload);
        var totalByteCount = 20 + 33 + 3 + recordTypeByteCount + payloadByteCount;

        byte[]? rented = null;
        var buffer = totalByteCount <= 4608
            ? stackalloc byte[4608]
            : (rented = ArrayPool<byte>.Shared.Rent(totalByteCount));

        var recordBytes = buffer[..totalByteCount];

        try
        {
            var written = 0;

            if (!Utf8Formatter.TryFormat(sequence, recordBytes[written..], out var sequenceWritten))
                throw new InvalidOperationException("Failed to format WAL sequence.");

            written += sequenceWritten;
            recordBytes[written++] = (byte)'|';

            if (!Utf8Formatter.TryFormat(timestamp, recordBytes[written..], out var timestampWritten, 'O'))
                throw new InvalidOperationException("Failed to format WAL timestamp.");

            written += timestampWritten;
            recordBytes[written++] = (byte)'|';
            written += Encoding.UTF8.GetBytes(recordType, recordBytes[written..]);
            recordBytes[written++] = (byte)'|';
            written += Encoding.UTF8.GetBytes(payload, recordBytes[written..]);

            Hash(recordBytes[..written], destination, algorithm);
        }
        finally
        {
            if (rented is not null) ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static void ValidateCore(Fixture[] fixtures)
    {
        Span<byte> actual = stackalloc byte[32];
        Span<byte> expected = stackalloc byte[32];
        foreach (Fixture fixture in fixtures)
        foreach (Algorithm algorithm in Enum.GetValues<Algorithm>())
        {
            Hash(fixture.Encoded, expected, algorithm);
            FullCore(fixture.Sequence, fixture.Timestamp, fixture.RecordType, fixture.Payload, actual, algorithm);
            if (!actual.SequenceEqual(expected)) throw new Exception("Encoded record digest mismatch");
        }
    }

    private static void ValidateBlake3Vectors()
    {
        (int Length, string Hash)[] vectors =
        [
            (0, "af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262"),
            (1, "2d3adedff11b61f14c886e35afa036736dcd87a74d27b5c1510225d0f592e213"),
            (63, "e9bc37a594daad83be9470df7f7b3798297c3d834ce80ba85d6e207627b7db7b"),
            (64, "4eed7141ea4a5cd4b788606bd23f46e212af9cacebacdc7d1f4c6dc7f2511b98"),
            (65, "de1e5fa0be70df6d2be8fffd0e99ceaa8eb6e8c93a63f2d8d1c30ecb6b263dee"),
            (1023, "10108970eeda3eb932baac1428c7a2163b0e924c9a9e25b35bba72b28f70bd11"),
            (1024, "42214739f095a406f3fc83deb889744ac00df831c10daa55189b5d121c855af7"),
            (1025, "d00278ae47eb27b34faecf67b4fe263f82d5412916c1ffd97c8cb7fb814b8444"),
            (4096, "015094013f57a5277b59d8475c0501042c0b642e531b0a1c8f58d2163229e969")
        ];
        Span<byte> output = stackalloc byte[32];
        foreach (var vector in vectors)
        {
            byte[] input = new byte[vector.Length];
            for (int i = 0; i < input.Length; ++i) input[i] = (byte)(i % 251);
            Blake3.Hasher.Hash(input, output);
            if (!Convert.ToHexStringLower(output).Equals(vector.Hash, StringComparison.Ordinal))
                throw new Exception($"Official BLAKE3 vector failed at length {vector.Length}");
        }
        Console.Error.WriteLine("All 9 official BLAKE3 vectors passed; pattern=i%251; output=32 bytes.");
    }
}
