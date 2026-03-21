using System.Security.Cryptography;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

namespace Meridian.Benchmarks;

/// <summary>
/// Benchmarks for WAL checksum computation strategies.
///
/// <para>
/// The BOTTLENECK_REPORT (#2) identified the original WAL checksum path as a P0 bottleneck:
/// <code>
///   var data = $"{sequence}|{timestamp:O}|{recordType}|{payload}";
///   var bytes = Encoding.UTF8.GetBytes(data);     // duplicates entire payload in memory
///   var hash  = SHA256.HashData(bytes);
///   return Convert.ToHexString(hash).ToLowerInvariant();  // two string allocs
/// </code>
/// For a 1 KB payload this creates ~3 KB of temporary heap objects per record.
/// </para>
///
/// <para>
/// The production <c>WriteAheadLog.ComputeChecksum</c> has since been updated to use
/// <see cref="IncrementalHash"/> with <c>stackalloc</c> output and <see cref="System.Buffers.ArrayPool{T}"/>
/// for large payloads.  These benchmarks retain the legacy string-concat approach as a historical
/// baseline so the improvement remains measurable over time.
/// </para>
///
/// <para>
/// These benchmarks measure three approaches:
/// <list type="bullet">
///   <item><see cref="Checksum_StringConcat_Legacy"/> — original string-concat approach (historical baseline)</item>
///   <item><see cref="Checksum_IncrementalHash"/> — hash fields individually with IncrementalHash to
///         avoid the full concatenated-string allocation</item>
///   <item><see cref="Checksum_IncrementalHash_StackAlloc"/> — IncrementalHash with stackalloc output
///         buffer (eliminates the 32-byte hash array allocation; closest to the current production path)</item>
/// </list>
/// </para>
///
/// <para>
/// Run with: <c>dotnet run -c Release -- --filter "*WalChecksum*" --memory</c>
/// </para>
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class WalChecksumBenchmarks
{
    private long _sequence;
    private DateTimeOffset _timestamp;
    private const string RecordType = "Trade";
    // Simulated JSON payload at three realistic sizes.
    private string _smallPayload = null!;   // ~256 bytes
    private string _mediumPayload = null!;  // ~1 KB (typical trade event)
    private string _largePayload = null!;   // ~4 KB (L2 snapshot)

    [Params("small", "medium", "large")]
    public string PayloadSize { get; set; } = "medium";

    [GlobalSetup]
    public void Setup()
    {
        _sequence = 123456789L;
        _timestamp = new DateTimeOffset(2024, 1, 15, 14, 30, 0, TimeSpan.Zero);

        // Realistic JSON payloads at each size class
        _smallPayload = new string('x', 256);
        _mediumPayload = new string('x', 1024);
        _largePayload = new string('x', 4096);
    }

    private string CurrentPayload => PayloadSize switch
    {
        "small" => _smallPayload,
        "large" => _largePayload,
        _ => _mediumPayload
    };

    /// <summary>
    /// Historical string-concat approach that was used in WriteAheadLog.ComputeChecksum
    /// before the IncrementalHash refactor.  Kept as a measurement baseline.
    /// Allocations per call (1 KB payload):
    ///   1. Interpolated data string  (~1 KB)
    ///   2. byte[] from GetBytes      (~1 KB)
    ///   3. byte[32] from SHA256      (32 B)
    ///   4. hex string from HexString (64 B)
    ///   5. lowercase string          (64 B)
    /// </summary>
    [Benchmark(Baseline = true)]
    public string Checksum_StringConcat_Legacy()
    {
        var payload = CurrentPayload;
        var data = $"{_sequence}|{_timestamp:O}|{RecordType}|{payload}";
        var bytes = Encoding.UTF8.GetBytes(data);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Hash fields individually with <see cref="IncrementalHash"/> to avoid building
    /// the full concatenated string.  Each field is encoded directly into a small
    /// stack-allocated buffer (or rented array for the payload).
    /// Allocations per call: only the payload byte[] (no full-data concatenation).
    /// </summary>
    [Benchmark]
    public string Checksum_IncrementalHash()
    {
        var payload = CurrentPayload;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        // Separator bytes reused across fields
        ReadOnlySpan<byte> pipe = "|"u8;

        // Sequence number — encode to a small stack buffer
        Span<byte> seqBuffer = stackalloc byte[32];
        if (Encoding.UTF8.TryGetBytes(_sequence.ToString(), seqBuffer, out var seqWritten))
            hash.AppendData(seqBuffer[..seqWritten]);
        else
            hash.AppendData(Encoding.UTF8.GetBytes(_sequence.ToString()));

        hash.AppendData(pipe);

        // Timestamp — ISO 8601 is always ≤ 35 characters
        Span<byte> tsBuffer = stackalloc byte[40];
        var tsStr = _timestamp.ToString("O");
        if (Encoding.UTF8.TryGetBytes(tsStr, tsBuffer, out var tsWritten))
            hash.AppendData(tsBuffer[..tsWritten]);
        else
            hash.AppendData(Encoding.UTF8.GetBytes(tsStr));

        hash.AppendData(pipe);

        // Record type — short string, fits on stack
        Span<byte> typeBuffer = stackalloc byte[64];
        if (Encoding.UTF8.TryGetBytes(RecordType, typeBuffer, out var typeWritten))
            hash.AppendData(typeBuffer[..typeWritten]);
        else
            hash.AppendData(Encoding.UTF8.GetBytes(RecordType));

        hash.AppendData(pipe);

        // Payload — unavoidably heap-allocated because size is unknown at compile time
        hash.AppendData(Encoding.UTF8.GetBytes(payload));

        var hashBytes = hash.GetHashAndReset();
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    /// <summary>
    /// <see cref="Checksum_IncrementalHash"/> with a stackalloc output buffer to avoid the
    /// 32-byte <c>byte[]</c> allocation from <see cref="IncrementalHash.GetHashAndReset()"/>.
    /// Uses <see cref="IncrementalHash.TryGetHashAndReset"/> which writes into a caller-supplied span.
    /// </summary>
    [Benchmark]
    public string Checksum_IncrementalHash_StackAlloc()
    {
        var payload = CurrentPayload;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        ReadOnlySpan<byte> pipe = "|"u8;

        Span<byte> seqBuffer = stackalloc byte[32];
        if (Encoding.UTF8.TryGetBytes(_sequence.ToString(), seqBuffer, out var seqWritten))
            hash.AppendData(seqBuffer[..seqWritten]);
        else
            hash.AppendData(Encoding.UTF8.GetBytes(_sequence.ToString()));

        hash.AppendData(pipe);

        Span<byte> tsBuffer = stackalloc byte[40];
        var tsStr = _timestamp.ToString("O");
        if (Encoding.UTF8.TryGetBytes(tsStr, tsBuffer, out var tsWritten))
            hash.AppendData(tsBuffer[..tsWritten]);
        else
            hash.AppendData(Encoding.UTF8.GetBytes(tsStr));

        hash.AppendData(pipe);

        Span<byte> typeBuffer = stackalloc byte[64];
        if (Encoding.UTF8.TryGetBytes(RecordType, typeBuffer, out var typeWritten))
            hash.AppendData(typeBuffer[..typeWritten]);
        else
            hash.AppendData(Encoding.UTF8.GetBytes(RecordType));

        hash.AppendData(pipe);

        hash.AppendData(Encoding.UTF8.GetBytes(payload));

        // Write hash directly into a stack buffer — no heap allocation for the 32-byte output
        Span<byte> hashOutput = stackalloc byte[32];
        hash.TryGetHashAndReset(hashOutput, out _);
        return Convert.ToHexString(hashOutput).ToLowerInvariant();
    }
}
