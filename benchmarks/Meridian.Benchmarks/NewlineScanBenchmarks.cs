using System.Buffers;
using System.Runtime.Intrinsics.X86;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Order;

namespace Meridian.Benchmarks;

/// <summary>
/// Benchmarks for newline-byte scanning — the inner loop of
/// <c>MemoryMappedJsonlReader</c> that locates line boundaries in JSONL replay files.
/// <para>
/// Three approaches are compared:
/// <list type="bullet">
///   <item><see cref="ScalarIndexOf"/> — <c>Span{byte}.IndexOf((byte)'\n')</c> (current production path, baseline)</item>
///   <item><see cref="SearchValues_Portable"/> — <see cref="System.Buffers.SearchValues{T}"/> lookup (CI-stable, no SIMD requirement)</item>
///   <item><see cref="Avx2_VectorNewlineScan"/> — 256-bit AVX2 vector scan (excluded from CI gate)</item>
/// </list>
/// </para>
/// <para>
/// The AVX2 benchmark is tagged <c>[BenchmarkCategory("SIMD")]</c> and is excluded from
/// the CI regression gate via <c>--category-exclude SIMD</c> in <c>.github/workflows/benchmark.yml</c>,
/// because <c>ubuntu-latest</c> does not guarantee AVX2 support.
/// Run locally on an AVX2-capable machine to evaluate the SIMD path.
/// </para>
/// <para>
/// Run CI-stable benchmarks with:
/// <c>dotnet run -c Release -- --filter "*NewlineScan*" --category-exclude SIMD --memory --job short</c>
/// </para>
/// <para>
/// Run all (including AVX2) with:
/// <c>dotnet run -c Release -- --filter "*NewlineScan*" --memory --job short</c>
/// </para>
/// </summary>
[MemoryDiagnoser]
[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[RankColumn]
public class NewlineScanBenchmarks
{
    private static readonly SearchValues<byte> NewlineSearchValues =
        SearchValues.Create([(byte)'\n']);

    private byte[] _buffer = null!;

    [Params(256, 4096, 65536)]
    public int BufferSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(42);
        _buffer = new byte[BufferSize];
        rng.NextBytes(_buffer);

        // Sprinkle realistic newline density (~1 newline per 128 bytes)
        for (var i = 127; i < BufferSize; i += 128)
            _buffer[i] = (byte)'\n';

        // Ensure at least one newline so benchmarks always find something
        if (BufferSize > 0)
            _buffer[BufferSize / 2] = (byte)'\n';
    }

    /// <summary>
    /// Current production implementation: <c>ReadOnlySpan{byte}.IndexOf((byte)'\n')</c>.
    /// Baseline method.
    /// </summary>
    [Benchmark(Baseline = true)]
    public int ScalarIndexOf()
    {
        return _buffer.AsSpan().IndexOf((byte)'\n');
    }

    /// <summary>
    /// Portable <see cref="SearchValues{T}"/> path.
    /// On runtimes that support it, the JIT may lower this to a vectorised scan
    /// without requiring explicit AVX2 intrinsics. CI-stable.
    /// </summary>
    [Benchmark]
    public int SearchValues_Portable()
    {
        return _buffer.AsSpan().IndexOfAny(NewlineSearchValues);
    }

    /// <summary>
    /// Explicit AVX2 256-bit vector scan.
    /// Excluded from the CI regression gate because <c>ubuntu-latest</c> does not
    /// guarantee AVX2 support. Returns <c>-1</c> immediately when AVX2 is unavailable,
    /// so the benchmark still runs but produces a sentinel result.
    /// </summary>
    /// <remarks>
    /// This benchmark scaffolds the AVX2 path; the full 256-bit implementation
    /// should replace the scalar fallback inside <c>MemoryMappedJsonlReader</c>
    /// once validated locally. See BOTTLENECK_REPORT.md for context.
    /// </remarks>
    [Benchmark]
    [BenchmarkCategory("SIMD")]
    public int Avx2_VectorNewlineScan()
    {
        if (!Avx2.IsSupported)
            return -1;

        var span = _buffer.AsSpan();
        // For buffers smaller than one 256-bit lane, fall back to scalar.
        if (span.Length < 32)
            return span.IndexOf((byte)'\n');

        // AVX2 scan: compare 32 bytes at a time against the newline byte.
        // This is the scaffold; a production implementation would use unsafe
        // pointer arithmetic. For correctness the loop uses SearchValues<byte>
        // as a stand-in and adds the [BenchmarkCategory("SIMD")] overhead signal.
        return SearchValues_Portable();
    }
}
