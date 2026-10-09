using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Meridian.Storage.Archival;

internal static class Program
{
    sealed record Fixture(long Sequence, DateTime Timestamp, string Type, string Payload);
    static int _sink;
    static void Main(string[] args)
    {
        int iterations = args.Length > 0 ? int.Parse(args[0]) : 50_000;
        int reps = args.Length > 1 ? int.Parse(args[1]) : 3;
        ValidatePackBits();
        Console.WriteLine("fixture,payload_bytes,input_pattern,repetition,iterations,mean_ns,allocated_bytes_per_event,sink");
        foreach (string kind in new[] { "ascii", "json" })
        foreach (int size in new[] { 64, 900, 4096 })
        {
            Fixture[] fixtures = Fixtures(size, kind);
            ValidateCore(fixtures);
            foreach (bool varied in new[] { false, true })
            {
                Run(fixtures, varied, 50_000);
                for (int rep = 1; rep <= reps; ++rep)
                {
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    long start = Stopwatch.GetTimestamp();
                    Run(fixtures, varied, iterations);
                    long elapsed = Stopwatch.GetTimestamp() - start;
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    double nanos = elapsed * (1_000_000_000.0 / Stopwatch.Frequency) / iterations;
                    Console.WriteLine(FormattableString.Invariant($"{kind},{size},{(varied ? "varied256" : "fixed")},{rep},{iterations},{nanos:F3},{(double)allocated / iterations:F6},{_sink}"));
                }
            }
        }
        Console.Error.WriteLine("1536 independently framed/oracle-packed digest fixtures passed.");
    }
    static void Run(Fixture[] fixtures, bool varied, int count)
    {
        Span<byte> output = stackalloc byte[32];
        int sink = _sink;
        for (int i = 0; i < count; ++i)
        {
            Fixture f = fixtures[varied ? i & 255 : 0];
            WalChecksum.ComputeCore(f.Sequence, f.Timestamp, f.Type, f.Payload, output);
            sink = unchecked(sink + output[0]);
        }
        _sink = sink;
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
                if (size == 64) builder.Append("{\"symbol\":\"AAPL\",\"price\":174.53,\"size\":100");
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
                builder.Append(",\"pad\":\""); builder.Append('x', size - builder.Length - 2);
                builder.Append("\"}"); payload = builder.ToString();
            }
            fixtures[i] = new(i + (size == 64 ? 1 : size == 900 ? 2 : 3), timestamp.AddTicks(i),
                              size == 64 ? "Trade" : "L2Snapshot", payload);
        }
        return fixtures;
    }

    // Independent scalar canonical encoder with a simple look-ahead at each byte.
    static byte[] OraclePack(ReadOnlySpan<byte> source)
    {
        var result = new List<byte>();
        int position = 0;
        while (position < source.Length)
        {
            int runEnd = position + 1;
            while (runEnd < source.Length && source[runEnd] == source[position]) ++runEnd;
            int run = runEnd - position;
            if (run >= 3)
            {
                while (run > 0)
                {
                    int count = Math.Min(128, run);
                    result.Add(count == 1 ? (byte)0 : (byte)(257 - count));
                    result.Add(source[position]); run -= count;
                }
                position = runEnd;
            }
            else
            {
                int literalStart = position++;
                while (position < source.Length && position - literalStart < 128)
                {
                    if (position + 2 < source.Length && source[position] == source[position + 1] && source[position] == source[position + 2]) break;
                    ++position;
                }
                result.Add((byte)(position - literalStart - 1));
                for (int i = literalStart; i < position; ++i) result.Add(source[i]);
            }
        }
        return result.ToArray();
    }

    static void ValidatePackBits()
    {
        var random = new Random(713);
        int checks = 0;
        foreach (int length in new[] { 0, 1, 2, 3, 17, 18, 31, 32, 33, 34, 63, 64, 127, 128, 129, 130, 255, 256, 257, 1023, 1024, 1025, 4096, 5000 })
        for (int pattern = 0; pattern < 6; ++pattern)
        {
            var input = new byte[length];
            random.NextBytes(input);
            if (pattern == 0) Array.Fill(input, (byte)42);
            if (pattern == 1) for (int i = 0; i < length; ++i) input[i] = (byte)(i % 2);
            if (pattern == 2) for (int i = 0; i < length; ++i) input[i] = (byte)(i / 3);
            if (pattern == 3) for (int i = 0; i < length; ++i) input[i] = (byte)(i / 128);
            if (pattern == 4) for (int i = 0; i < length; ++i) input[i] = (byte)(i / 129);
            var output = new byte[length + (length + 127) / 128];
            int written = WalChecksum.EncodePackBits(input, output);
            if (!output.AsSpan(0, written).SequenceEqual(OraclePack(input))) throw new Exception($"PackBits oracle mismatch length={length} pattern={pattern}");
            ++checks;
        }
        Console.Error.WriteLine($"Independent scalar PackBits oracle passed {checks} adversarial block/run boundary fixtures.");
        Span<byte> digest = stackalloc byte[32];
        Blake3.Hasher.Hash([], digest);
        if (Convert.ToHexStringLower(digest) != "af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262") throw new Exception("Official BLAKE3 empty vector mismatch");
    }

    static void ValidateCore(Fixture[] fixtures)
    {
        Span<byte> actual = stackalloc byte[32];
        Span<byte> expected = stackalloc byte[32];
        foreach (Fixture fixture in fixtures)
        {
            byte[] type = Encoding.UTF8.GetBytes(fixture.Type);
            byte[] payload = Encoding.UTF8.GetBytes(fixture.Payload);
            byte[] record = new byte[24 + type.Length + payload.Length];
            BinaryPrimitives.WriteInt64LittleEndian(record, fixture.Sequence);
            BinaryPrimitives.WriteInt64LittleEndian(record.AsSpan(8), fixture.Timestamp.ToBinary());
            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(16), type.Length);
            BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(20), payload.Length);
            type.CopyTo(record, 24); payload.CopyTo(record, 24 + type.Length);
            Blake3.Hasher.Hash(OraclePack(record), expected);
            WalChecksum.ComputeCore(fixture.Sequence, fixture.Timestamp, fixture.Type, fixture.Payload, actual);
            if (!actual.SequenceEqual(expected)) throw new Exception("Production digest differs from independent canonical oracle");
        }
    }
}
