using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using K4os.Compression.LZ4;

// Component diagnostic only; never linked into Meridian.
// The mode byte and original-length field make raw/compressed hash inputs disjoint.
// Normal-init control explicitly clears the buffer inside SkipLocalsInit code;
// the original unmodified compiler-zeroed core is retained in the earlier profile.
internal static class Program
{
    enum Layout { TextV1, BinaryV2 }
    enum Codec { Raw, Lz4Always, RawThrough512 }
    enum Digest { Sha256Reuse, IncrementalReuse }
    sealed record Fixture(long Sequence, DateTime Timestamp, string Type, string Payload);
    static readonly SHA256 Sha = SHA256.Create();
    static readonly IncrementalHash Incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    static int _sink;
    static int Main(string[] args)
    {
        int iterations = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 10_000;
        int reps = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 3;
        Console.Error.WriteLine($"K4os.LZ4=1.3.8; codec={LZ4Codec.Version}; runtime={Environment.Version}; tiered={Environment.GetEnvironmentVariable("DOTNET_TieredCompilation")}; iterations={iterations}; reps={reps}");
        ValidateCompression();
        Console.WriteLine("fixture,payload_bytes,input_pattern,layout,skip_locals_init,codec,digest,repetition,iterations,median_candidate_mean_ns,allocated_bytes_per_event");
        foreach (string kind in new[] { "ascii", "json" })
        foreach (int size in new[] { 64, 900, 4096 })
        {
            Fixture[] fixtures = Fixtures(size, kind);
            foreach (bool varied in new[] { false, true })
            foreach (Layout layout in Enum.GetValues<Layout>())
            foreach (bool skipInit in new[] { false, true })
            foreach (Codec codec in Enum.GetValues<Codec>())
            foreach (Digest digest in Enum.GetValues<Digest>())
            {
                Run(fixtures, varied, layout, skipInit, codec, digest, 10_000);
                for (int rep = 1; rep <= reps; ++rep)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    long start = Stopwatch.GetTimestamp();
                    Run(fixtures, varied, layout, skipInit, codec, digest, iterations);
                    long elapsed = Stopwatch.GetTimestamp() - start;
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    double nanos = (double)elapsed * 1_000_000_000 / Stopwatch.Frequency / iterations;
                    Console.WriteLine(FormattableString.Invariant($"{kind},{size},{(varied ? "varied256" : "fixed")},{layout},{skipInit},{codec},{digest},{rep},{iterations},{nanos:F3},{(double)allocated / iterations:F6}"));
                }
            }
        }
        Sha.Dispose(); Incremental.Dispose();
        return 0;
    }

    static Fixture[] Fixtures(int size, string kind)
    {
        var fixtures = new Fixture[256];
        DateTime timestamp = new(2024, 1, 15, 14, 30, 0, DateTimeKind.Utc);
        for (int i = 0; i < fixtures.Length; ++i)
        {
            string payload;
            if (kind == "ascii")
            {
                var chars = new string('x', size).ToCharArray();
                if (i != 0) { chars[0] = (char)('a' + i % 26); chars[^1] = (char)('A' + i % 26); }
                payload = new(chars);
            }
            else
            {
                var builder = new StringBuilder();
                if (size == 64)
                    builder.Append("{\"symbol\":\"AAPL\",\"price\":174.53,\"size\":100");
                else
                {
                    builder.Append("{\"symbol\":\"AAPL\",\"timestamp\":\"2024-01-15T14:30:00Z\",\"bids\":[");
                    int level = 0;
                    while (builder.Length + 50 < size)
                    {
                        if (level != 0) builder.Append(',');
                        builder.Append(FormattableString.Invariant($"[{174.53m + (level % 10) * 0.01m},{100 + i + level}]"));
                        ++level;
                    }
                    builder.Append(']');
                }
                builder.Append(",\"pad\":\"");
                builder.Append('x', size - builder.Length - 2);
                builder.Append("\"}"); payload = builder.ToString();
            }
            fixtures[i] = new(i + (size == 64 ? 1 : size == 900 ? 2 : 3), timestamp.AddTicks(i),
                              size == 64 ? "Trade" : "L2Snapshot", payload);
        }
        return fixtures;
    }

    static void Run(Fixture[] fixtures, bool varied, Layout layout, bool skipInit, Codec codec, Digest digest, int count)
    {
        Span<byte> output = stackalloc byte[32];
        int sink = _sink;
        for (int i = 0; i < count; ++i)
        {
            Fixture f = fixtures[varied ? i & 255 : 0];
            Core(f.Sequence, f.Timestamp, f.Type, f.Payload, output, layout, skipInit, codec, digest);
            sink = unchecked(sink + output[0]);
        }
        _sink = sink;
    }

    [SkipLocalsInit]
    static void Core(long sequence, DateTime timestamp, string type, string payload,
                     Span<byte> destination, Layout layout, bool skipInit, Codec codec, Digest digest)
    {
        int typeLength = Encoding.UTF8.GetByteCount(type);
        int payloadLength = Encoding.UTF8.GetByteCount(payload);
        int maximum = (layout == Layout.TextV1 ? 56 : 24) + typeLength + payloadLength;
        byte[]? rented = null;
        Span<byte> buffer = maximum <= 4608 ? stackalloc byte[4608] : (rented = ArrayPool<byte>.Shared.Rent(maximum));
        if (!skipInit && rented is null) buffer.Clear();
        try
        {
            int written;
            if (layout == Layout.BinaryV2)
            {
                BinaryPrimitives.WriteInt64LittleEndian(buffer, sequence);
                BinaryPrimitives.WriteInt64LittleEndian(buffer[8..], timestamp.ToBinary());
                BinaryPrimitives.WriteInt32LittleEndian(buffer[16..], typeLength);
                BinaryPrimitives.WriteInt32LittleEndian(buffer[20..], payloadLength);
                written = 24;
                written += Encoding.UTF8.GetBytes(type, buffer[written..]);
                written += Encoding.UTF8.GetBytes(payload, buffer[written..]);
            }
            else
            {
                Utf8Formatter.TryFormat(sequence, buffer, out written);
                buffer[written++] = (byte)'|';
                Utf8Formatter.TryFormat(timestamp, buffer[written..], out int tsWritten, 'O');
                written += tsWritten; buffer[written++] = (byte)'|';
                written += Encoding.UTF8.GetBytes(type, buffer[written..]);
                buffer[written++] = (byte)'|';
                written += Encoding.UTF8.GetBytes(payload, buffer[written..]);
            }
            Hash(buffer[..written], destination, codec, digest);
        }
        finally { if (rented is not null) ArrayPool<byte>.Shared.Return(rented); }
    }

    [SkipLocalsInit]
    static void Hash(ReadOnlySpan<byte> input, Span<byte> output, Codec codec, Digest digest)
    {
        // Prefix binds mode and original length so compression is injective
        // across both compressed and uncompressed branches of the candidate.
        bool compress = codec == Codec.Lz4Always || codec == Codec.RawThrough512 && input.Length > 512;
        int capacity = (compress ? LZ4Codec.MaximumOutputSize(input.Length) : input.Length) + 5;
        byte[]? rented = null;
        Span<byte> bytes = capacity <= 4800 ? stackalloc byte[4800] : (rented = ArrayPool<byte>.Shared.Rent(capacity));
        try
        {
            bytes[0] = compress ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteInt32LittleEndian(bytes[1..], input.Length);
            int written;
            if (compress)
            {
                written = LZ4Codec.Encode(input, bytes[5..], LZ4Level.L00_FAST);
                if (written < 0) throw new Exception("LZ4 output too small");
            }
            else { input.CopyTo(bytes[5..]); written = input.Length; }
            ReadOnlySpan<byte> source = bytes[..(written + 5)];
            if (digest == Digest.Sha256Reuse)
            {
                if (!Sha.TryComputeHash(source, output, out int n) || n != 32) throw new CryptographicException();
            }
            else
            {
                Incremental.AppendData(source);
                if (!Incremental.TryGetHashAndReset(output, out int n) || n != 32) throw new CryptographicException();
            }
        }
        finally { if (rented is not null) ArrayPool<byte>.Shared.Return(rented); }
    }

    static void ValidateCompression()
    {
        int mismatches = 0, checks = 0;
        bool previous = LZ4Codec.Enforce32;
        try
        {
            foreach (string kind in new[] { "ascii", "json" })
            foreach (int size in new[] { 64, 900, 4096, 70000 })
            {
                foreach (Fixture fixture in Fixtures(size, kind))
                {
                    byte[] input = Encoding.UTF8.GetBytes(FormattableString.Invariant($"{fixture.Sequence}|{fixture.Timestamp:O}|{fixture.Type}|{fixture.Payload}"));
                    var output64 = new byte[LZ4Codec.MaximumOutputSize(input.Length)];
                    var output32 = new byte[output64.Length];
                    LZ4Codec.Enforce32 = false; int n64 = LZ4Codec.Encode(input, output64);
                    LZ4Codec.Enforce32 = true; int n32 = LZ4Codec.Encode(input, output32);
                    if (n64 != n32 || !output64.AsSpan(0, n64).SequenceEqual(output32.AsSpan(0, n32))) ++mismatches;
                    var decoded = new byte[input.Length];
                    if (LZ4Codec.Decode(output32.AsSpan(0, n32), decoded) != input.Length || !decoded.AsSpan().SequenceEqual(input))
                        throw new Exception("LZ4 lossless roundtrip failed");
                    ++checks;
                }
            }
        }
        finally { LZ4Codec.Enforce32 = previous; }
        Console.Error.WriteLine($"LZ4 lossless roundtrips={checks}; x32/x64 compressed-byte mismatches={mismatches}; Enforce32 restored={previous}");
    }
}
