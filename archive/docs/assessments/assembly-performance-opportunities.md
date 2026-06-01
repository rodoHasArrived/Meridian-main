# Assembly-Level Performance Opportunities

**Version:** 2.0
**Last Updated:** 2026-03-17
**Audience:** Core contributors, performance engineers

---

This document identifies where hand-written assembly (or .NET hardware intrinsics, which compile to
SIMD instructions) could materially improve performance in Meridian, and corrects
several inaccuracies from the initial draft by anchoring every candidate to verified production code.

## Executive Summary

Assembly-level work is most valuable in this repository for **byte-level parsing/scanning hot paths**
and **algorithmic improvements in numeric kernels**. It provides weak returns for orchestration
logic, async I/O waiting, or framework-heavy paths. Several areas that appeared to be candidates have
already been optimized by the runtime or by existing code patterns.

Highest-potential candidates in priority order:

1. JSONL newline scanning in memory-mapped replay (`MemoryMappedJsonlReader`).
2. UTF-8 sequence-number extraction in data-quality scoring (`DataQualityScoringService`).
3. Latency histogram bucket selection (`LatencyHistogram`).
4. Partial drain and per-symbol drain allocation in `EventBuffer` / `MarketEventBuffer`.
5. Anomaly-detection rolling statistics (`AnomalyDetector.SymbolStatistics`).

Areas already sufficiently optimized are documented at the end to prevent redundant effort.

---

## Candidate Areas

## 1) Memory-mapped JSONL reader: newline scanning and UTF-8 string allocation

### Why this is a hot candidate

`MemoryMappedJsonlReader.ReadFileMemoryMappedAsync` scans 4 MB chunks with a scalar byte loop
and materializes a new managed `string` for every line it finds. For a 1 GB uncompressed replay
file this means hundreds of millions of byte comparisons and millions of string allocations.

### Exact hot path (lines 237-275 of `MemoryMappedJsonlReader.cs`)

```csharp
for (int i = 0; i < chunkSize; i++)
{
    if (buffer[i] == '\n')                                          // scalar scan, one byte at a time
    {
        var lineLength = i - lineStart;
        var lineText = Encoding.UTF8.GetString(buffer, lineStart, lineLength).TrimEnd('\r'); // allocates per line
        // ...
        pendingLines.Add(lineText);
    }
}
// partial line at chunk boundary:
lineBuilder.Append(Encoding.UTF8.GetString(buffer, lineStart, remainingLength));            // allocates again
```

Two independent improvements are possible independently of each other:

**1a. Vectorize the delimiter scan.**
Replace the scalar `buffer[i] == '\n'` loop with a SIMD scan. On AVX2 hardware this processes
32 bytes per iteration instead of 1, reducing the scan cost by ~30x.

**1b. Defer UTF-16 materialization.**
`Encoding.UTF8.GetString` allocates a new `string` per line even when most lines are discarded
by downstream filters. Hold lines as `ReadOnlyMemory<byte>` slices into the pooled `ArrayPool`
buffer and decode only after a line has passed validation.

### Assembly/SIMD specifics

- Use `Vector256<byte>` compare-equal to `'\n'` with `Avx2.MoveMask` to find newlines in 32-byte
  batches. When AVX2 is absent fall back to `Vector128<byte>` (SSE2) then scalar.
- .NET 8+ portable fallback: `MemoryExtensions.IndexOfAnyValues<byte>` with a pre-created
  `SearchValues<byte>` for `{'\n', '\r'}` compiles to an optimized SIMD path on every platform.

### Expected impact

- Strong upside for large uncompressed replay files where parsing is CPU-bound.
- Delimiter scan speedups of 1.5x-4x for the scan portion are typical.
- Eliminating per-line `GetString` allocations reduces GC pressure and improves
  cache density through the deserialization step.

---

## 2) Data-quality sequence extraction: UTF-16 string scan to UTF-8 byte scan

### Why this is a hot candidate

`DataQualityScoringService.ComputeSequenceScoreAsync` reads each JSONL file line-by-line with
`File.ReadLinesAsync` (which allocates a UTF-16 `string` per line) and then searches each
string for the `"Sequence"` key using `string.IndexOf` with `OrdinalIgnoreCase`, followed by
two character-by-character loops using `char.IsDigit`. The method caps processing at 10 000 lines,
but those 10 000 iterations are branch-heavy and run on every scored file.

### Exact hot path (lines 252-265 of `DataQualityScoringService.cs`)

```csharp
var seqStart = line.IndexOf("\"Sequence\":", StringComparison.OrdinalIgnoreCase);
if (seqStart < 0)
    seqStart = line.IndexOf("\"sequence\":", StringComparison.OrdinalIgnoreCase); // second scan if first misses
if (seqStart >= 0)
{
    var numStart = seqStart + 11;
    while (numStart < line.Length && !char.IsDigit(line[numStart]) && line[numStart] != '-')
        numStart++;                 // char-by-char skip of whitespace/non-digit

    var numEnd = numStart;
    while (numEnd < line.Length && (char.IsDigit(line[numEnd]) || line[numEnd] == '-'))
        numEnd++;                   // char-by-char digit-run extraction

    if (long.TryParse(line.AsSpan(numStart, numEnd - numStart), out var seq))
        // ... gap/order checks
}
```

### Assembly/SIMD opportunity

Replace the UTF-16 path end-to-end with a UTF-8 byte reader:

1. Switch from `File.ReadLinesAsync` to `File.OpenRead` with a `StreamReader` that exposes
   `ReadOnlySpan<byte>` via `PipeReader`, or use a `FileStream` with an `ArrayPool<byte>` buffer.
2. Scan for the `"Sequence":` key with `ReadOnlySpan<byte>.IndexOf` against a `u8` literal -
   the .NET runtime already vectorizes this with SSE2/AVX2 on x64.
3. Parse digits directly from the UTF-8 byte span instead of `char.IsDigit` over UTF-16.
4. Eliminate the two-pass `OrdinalIgnoreCase` scan: the JSONL format uses consistent casing
   per provider, so a single lowercase-first probe is sufficient.

### Expected impact

- Moderate-to-high benefit when scoring many large files during batch quality reports.
- Especially valuable when the scheduler triggers full-library rescoring overnight.
- Eliminates the allocation of 10 000 `string` objects per scored file.

---

## 3) Latency histogram bucket selection: linear scan to algorithmic improvement

### Why this is a candidate

`LatencyHistogram.PerSymbolTracker.Record(double latencyMs)` is called on every market event when
latency monitoring is active. Inside a `lock`, it performs a linear scan over the bucket-boundary
array to classify the sample. With the default 8 boundaries this is a short loop, but it executes
under lock on every event, making the loop body itself a contention multiplier.

### Exact hot path (lines 255-274 of `LatencyHistogram.cs`)

```csharp
lock (_lock)
{
    _count++;
    _sum += latencyMs;
    _sumSquares += latencyMs * latencyMs;
    _min = Math.Min(_min, latencyMs);
    _max = Math.Max(_max, latencyMs);

    var bucketIndex = 0;
    for (int i = 0; i < _config.BucketBoundaries.Length; i++)   // linear scan every event
    {
        if (latencyMs <= _config.BucketBoundaries[i])
            break;
        bucketIndex++;
    }
    _bucketCounts[bucketIndex]++;

    _samples.Add(new LatencySample(DateTimeOffset.UtcNow, latencyMs));  // allocation per event
    while (_samples.Count > _config.MaxSamples)
        _samples.RemoveAt(0);                                   // O(n) front-remove under lock
}
```

There are three independent issues here:

**3a. Replace the linear bucket scan with binary search.**
`Array.BinarySearch` or a manual binary search over the sorted `BucketBoundaries` reduces
O(n) to O(log n). For 8 buckets this is marginal in absolute time, but it removes 3-7 redundant
comparisons per event and is a zero-risk algorithmic change.

**3b. Remove the front-of-list `RemoveAt(0)` inside the lock.**
`List<T>.RemoveAt(0)` shifts all remaining sample elements by one on every overflow call.
A fixed-size circular `LatencySample[]` with a head index eliminates both the shift and the
implicit lock-time proportional to sample count.

**3c. Optional SIMD bucket scan for larger boundary sets.**
If `BucketBoundaries` is ever expanded beyond ~16 entries, a SIMD approach using
`Vector256<double>` comparison to find the first boundary greater than `latencyMs` becomes
worthwhile. For 8 boundaries binary search is already faster.

### Expected impact

- Moderate direct latency reduction per event in high-volume ingest.
- Eliminates two hidden O(n) operations that run under the lock on every event.
- GC pressure reduction from replacing `List<LatencySample>` overflow removal.

---

## 4) `EventBuffer` partial drain and `DrainBySymbol` allocation

### Why this is a candidate

`EventBuffer<T>` already has an excellent zero-copy full drain via `DrainAll()`, which swaps
two pre-allocated `List<T>` references under a single lock - no element copies and no
allocations. However, two secondary paths have significant overhead:

**Path A - `Drain(int maxCount)` (lines 151-167 of `EventBuffer.cs`)**

```csharp
var count = Math.Min(maxCount, _active.Count);
var result = _active.GetRange(0, count);   // allocates a new List<T> with count elements
_active.RemoveRange(0, count);             // shifts _active.Count - count elements to the front
_count = _active.Count;
```

`GetRange` allocates a new `List<T>` on every call. `RemoveRange(0, count)` is O(remaining)
because it shifts all tail elements to the left. Under sustained partial-drain workloads
(e.g., per-symbol sink flushing) these accumulate as GC pressure and CPU copies.

**Path B - `MarketEventBuffer.DrainBySymbol` (lines 228-257 of `EventBuffer.cs`)**

```csharp
lock (_lock)
{
    var matching = new List<MarketEvent>();   // allocation per call
    var remaining = new List<MarketEvent>(); // allocation per call

    foreach (var evt in _active)
    {
        if (evt.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))  // string compare per event
            matching.Add(evt);
        else
            remaining.Add(evt);
    }

    _active.Clear();
    _active.AddRange(remaining);  // copies O(remaining) items back
    _count = _active.Count;
    return matching;
}
```

`DrainBySymbol` allocates two new lists, copies every event into one of them, then rebuilds
`_active` from the non-matching list. When draining a single symbol from a buffer holding many
symbols this results in O(n) copy work and two GC-tracked list allocations per call.

### Assembly/SIMD opportunity

**For `Drain(int maxCount)`**: introduce a ring-buffer variant backed by a power-of-two array
with head/tail indices. The front-shift copy disappears entirely; drain is two `Array.Copy`
calls at most (wrap-around case). The result list can reuse a thread-local or pooled backing
array passed in by the caller.

**For `DrainBySymbol`**: pre-intern symbols using `SymbolTable` (already in
`src/Meridian.Core/Performance/SymbolTable.cs`) so comparison becomes `int == int`.
Replace the two-list reconstruction pattern with an in-place partition using `List<T>.RemoveAll`
or a single-copy write into a pooled array - eliminating one allocation and one full-list copy.

### Expected impact

- Moderate improvement for sink flush paths that use partial drains.
- High improvement when `DrainBySymbol` is called at per-tick frequency on multi-symbol buffers.
- Zero-allocation path reduces steady-state GC pressure during live collection.

---

## 5) Anomaly-detection rolling statistics: vectorizable numeric kernel

### Why this is a candidate

`AnomalyDetector` maintains a `SymbolStatistics` object per symbol and calls
`stats.RecordTrade(timestamp, price, volume, config)` on every processed trade event (marked
`[MethodImpl(MethodImplOptions.AggressiveInlining)]`). The statistics object maintains a rolling
window of recent prices and volumes to compute mean and standard deviation used for spike
detection.

### Where in code

- `AnomalyDetector.ProcessTrade` (lines 61-85 of `AnomalyDetector.cs`): dispatches to
  `SymbolStatistics.RecordTrade` on the hot path.
- `SymbolStatistics.RecordTrade` (internal class): accumulates `_sumOfPrices`, `_sumOfSquares`,
  and a circular price window to compute mean and standard deviation.

### Assembly/SIMD opportunity

If profiling confirms that `SymbolStatistics.RecordTrade` contributes meaningfully to CPU time:

- Accumulate `_sumOfPrices` and `_sumOfSquares` using `Vector<double>` horizontal-add across
  a batch of incoming prices instead of a per-item scalar loop.
- Convert the rolling-window price array to a struct-of-arrays layout for better cache line
  utilization during the mean/stddev compute step.
- Use `Avx.HorizontalAdd` or `AdvSimd.AddAcross` (ARM) to reduce partial sums.

**Prerequisite**: this path is only profitable if `AnomalyDetector` is on the ingest hot path,
not in a background monitoring thread. Profile before investing.

### Expected impact

- Low-to-moderate; depends on monitoring frequency and symbol count.
- Greatest benefit in configurations with many actively monitored symbols and tight spike thresholds.

---

## 6) WAL checksum computation: already optimized (no further action)

`WriteAheadLog.AppendAsync` computes a per-record checksum using `IncrementalHash` with
`stackalloc` buffers for small fields and `ArrayPool<byte>` for payloads. The existing
`WalChecksumBenchmarks` (in `benchmarks/`) already documents an ~60-70% improvement over
the legacy string-concatenation approach.

The `StorageChecksumService` SHA-256 methods (`ComputeChecksumAsync`, `ComputeChecksum`) use
`SHA256.Create().ComputeHashAsync` and `SHA256.HashData`, which the .NET runtime routes to the
hardware SHA-NI instruction set on x86-64 CPUs that support it. No additional intrinsics work
is warranted.

---

## 7) `EventPipeline.TryPublish` and consume loop: already well optimized

`EventPipeline.TryPublish` (line 402) carries `[MethodImpl(AggressiveInlining)]`,
uses `Interlocked.Increment` for lock-free publish counting, employs a bitmask
(`count & ReaderCountSampleMask`) instead of modulo for sampling, and passes events
by `in` reference. The consume loop drains the bounded channel into a pre-allocated batch
buffer. There is no vectorizable kernel in this path; hardware-level improvements here
would come from reducing contention or lock granularity, not from SIMD.

---

## Prioritization

| Priority | Candidate | Estimated ROI | Risk |
| -------- | --------- | ------------- | ---- |
| 1 | JSONL newline scan (SIMD) | High | Low - fallback path trivial |
| 2 | JSONL newline scan (defer UTF-16 decode) | High | Medium - caller API change |
| 3 | Sequence extraction (UTF-8 byte path) | Medium-High | Low - isolated method |
| 4 | `LatencyHistogram` binary search + circular samples | Medium | Low - pure algorithmic |
| 5 | `EventBuffer.Drain` ring-buffer variant | Medium | Medium - data structure change |
| 6 | `DrainBySymbol` symbol-interning + single alloc | Medium | Low - localized change |
| 7 | Anomaly rolling stats (SIMD accumulate) | Low-Medium | High - profile first |

---

## Practical Guidance

- Prefer **.NET hardware intrinsics** (`System.Runtime.Intrinsics`) over raw inline assembly for
  portability and maintainability.
- Gate fast paths with runtime CPU feature detection (`Avx2.IsSupported`, `AdvSimd.IsSupported`)
  and always keep a scalar fallback.
- Benchmark with realistic files and symbol mixes - use the existing `BenchmarkDotNet` harness
  in `benchmarks/Meridian.Benchmarks/` - before and after each change.
- Adopt the profile-first rule: only optimize code confirmed hot by sampling/tracing. Most I/O
  paths in this repository are I/O-bound, not CPU-bound; measure before assuming otherwise.

## When _not_ to use assembly or intrinsics here

Do not use assembly for:

- Business-rule orchestration and provider glue code.
- Async I/O control flow and retry policies.
- Most logging/configuration paths.
- Cryptographic hashing (SHA-256 is already hardware-accelerated by the runtime).

For these areas, higher-level design changes will outperform intrinsics effort.

---

## Implementation Sketches

The examples below are partial but buildable sketches intended to show structure and integration
points. They are not drop-in production code; each requires correctness testing and benchmarking.

### Correctness-first principles

- Every SIMD path must have a scalar fallback that produces identical results.
- New paths must pass the existing test suite without modification.
- Intrinsics fallback correctness should be explicitly tested by forcing the scalar path via a
  compile-time constant or environment variable.

### A) Vectorized newline scan for `MemoryMappedJsonlReader`

Replace the scalar `buffer[i] == '\n'` loop (lines 237-269) with a tiered SIMD dispatch.

```csharp
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

/// <summary>
/// Finds the index of the first '\n' byte in <paramref name="data"/> at or after
/// <paramref name="start"/>. Returns -1 if no newline is found.
/// </summary>
private static int FindNextNewline(ReadOnlySpan<byte> data, int start)
{
    if ((uint)start >= (uint)data.Length)
        return -1;

    // .NET 8+: SearchValues path - JIT selects optimal SIMD width automatically.
    // This is the preferred portable path; only drop to manual intrinsics if
    // profiling shows SearchValues is not fast enough for the target platform.
    int idx = data[start..].IndexOf((byte)'\n');
    return idx >= 0 ? start + idx : -1;
}

/// <summary>
/// AVX2 path for x86-64 CPUs that support 256-bit SIMD.
/// Only needed if SearchValues overhead proves measurable in profiling.
/// </summary>
private static int FindNextNewlineAvx2(ReadOnlySpan<byte> data, int start)
{
    var needle = Vector256.Create((byte)'\n');
    ref byte r0 = ref MemoryMarshal.GetReference(data);
    int i = start;
    int last = data.Length - Vector256<byte>.Count;

    while (i <= last)
    {
        var vec = Vector256.LoadUnsafe(ref Unsafe.Add(ref r0, i));
        uint mask = (uint)Avx2.MoveMask(Avx2.CompareEqual(vec, needle));
        if (mask != 0)
            return i + BitOperations.TrailingZeroCount(mask);
        i += Vector256<byte>.Count;
    }

    // Scalar tail
    for (; i < data.Length; i++)
        if (Unsafe.Add(ref r0, i) == (byte)'\n') return i;

    return -1;
}
```

Integration strategy for `ReadFileMemoryMappedAsync`:

1. Keep the existing `List<string>` and batch flow unchanged.
2. Replace only the inner `for` loop's `buffer[i] == '\n'` test with a call to
   `FindNextNewline(buffer.AsSpan(0, chunkSize), lineStart)`, advancing `lineStart`
   and `i` to the returned position on each hit.
3. Benchmark on a representative 100 MB+ replay file.
4. As a follow-up, pass `ReadOnlyMemory<byte>` slices (instead of decoded `string`) to
   `DeserializeLines` to defer the `Encoding.UTF8.GetString` call into the deserializer.

### B) UTF-8 sequence extraction for `ComputeSequenceScoreAsync`

Replace the current `IndexOf` + `char.IsDigit` path (lines 252-265) with a byte-level parser.
This avoids the double `OrdinalIgnoreCase` scan and operates on UTF-8 bytes throughout.

```csharp
/// <summary>
/// Extracts the sequence number from a raw UTF-8 JSONL line without allocating.
/// Returns false if no "Sequence" field is found or the value is unparseable.
/// </summary>
private static bool TryExtractSequenceUtf8(ReadOnlySpan<byte> line, out long sequence)
{
    sequence = 0;

    // Use UTF-8 literals (C# 11 u8 suffix) - zero allocation, compile-time constant.
    ReadOnlySpan<byte> keyLower = "\"sequence\":"u8;
    ReadOnlySpan<byte> keyUpper = "\"Sequence\":"u8;

    int p = line.IndexOf(keyLower);
    int keyLen = keyLower.Length;
    if (p < 0)
    {
        p = line.IndexOf(keyUpper);
        keyLen = keyUpper.Length;
        if (p < 0) return false;
    }

    int i = p + keyLen;

    // Skip optional whitespace
    while (i < line.Length && (line[i] == (byte)' ' || line[i] == (byte)'\t'))
        i++;

    bool neg = i < line.Length && line[i] == (byte)'-';
    if (neg) i++;

    long value = 0;
    int digits = 0;
    while (i < line.Length)
    {
        uint d = (uint)(line[i] - (byte)'0');
        if (d > 9) break;
        if (value > (long.MaxValue - (long)d) / 10)
            return false; // overflow guard
        value = value * 10 + (long)d;
        i++;
        digits++;
    }

    if (digits == 0) return false;
    sequence = neg ? -value : value;
    return true;
}
```

To use this without allocating per-line byte arrays, feed the file through a `PipeReader` or
`FileStream` with an `ArrayPool<byte>` read buffer and pass slices to `TryExtractSequenceUtf8`
directly, avoiding `File.ReadLinesAsync` entirely for the scoring path.

### C) Binary search bucket classification for `LatencyHistogram`

Drop-in algorithmic replacement for the linear scan (lines 255-265). No SIMD required; binary
search is already ~3x faster than linear for 8 buckets and scales to larger bucket counts.

```csharp
// Replace the existing linear scan:
//   var bucketIndex = 0;
//   for (int i = 0; i < _config.BucketBoundaries.Length; i++) { ... }
//
// With:
var bucketIndex = FindBucket(_config.BucketBoundaries, latencyMs);

[MethodImpl(MethodImplOptions.AggressiveInlining)]
private static int FindBucket(double[] boundaries, double value)
{
    // boundaries is sorted ascending; return the index of the first entry >= value,
    // or boundaries.Length if value exceeds all boundaries (overflow bucket).
    int lo = 0, hi = boundaries.Length;
    while (lo < hi)
    {
        int mid = (lo + hi) >>> 1;
        if (value <= boundaries[mid])
            hi = mid;
        else
            lo = mid + 1;
    }
    return lo;
}
```

Replace the `_samples` overflow removal with a fixed-size circular buffer:

```csharp
// Instead of:
//   _samples.Add(new LatencySample(...));
//   while (_samples.Count > _config.MaxSamples) _samples.RemoveAt(0);
//
// Use a pre-allocated ring:
private readonly LatencySample[] _sampleRing = new LatencySample[MaxSamples];
private int _sampleHead;
private int _sampleCount;

_sampleRing[_sampleHead % MaxSamples] = new LatencySample(DateTimeOffset.UtcNow, latencyMs);
_sampleHead++;
_sampleCount = Math.Min(_sampleCount + 1, MaxSamples);
```

Percentile calculation then iterates `_sampleRing[(_sampleHead - _sampleCount + i) % MaxSamples]`
instead of `_samples[i]`, which is slightly more complex but eliminates all
`RemoveAt(0)` O(n) copies.

### D) `DrainBySymbol` with pre-interned symbols and single allocation

This change uses `SymbolTable` (already in `src/Meridian.Core/Performance/`) to
replace string equality with integer equality on the hot path.

```csharp
// In MarketEventBuffer or a caller that populates it:
private readonly SymbolTable _symbolTable = new();

// When adding events, store the symbol ID alongside or replace the string lookup with:
//   int symbolId = _symbolTable.GetOrAdd(evt.Symbol);

// DrainBySymbol with integer comparison:
public IReadOnlyList<MarketEvent> DrainBySymbolId(int symbolId)
{
    lock (_lock)
    {
        if (_active.Count == 0)
            return Array.Empty<MarketEvent>();

        // In-place partition: move non-matching events to the front,
        // collect matching events without a second list allocation.
        var matching = new List<MarketEvent>(_active.Count / 4); // rough capacity guess
        int writeIdx = 0;

        for (int i = 0; i < _active.Count; i++)
        {
            var evt = _active[i];
            if (_symbolTable.GetOrAdd(evt.Symbol) == symbolId)
                matching.Add(evt);
            else
                _active[writeIdx++] = evt;
        }

        // Trim the tail in-place without a second list
        if (writeIdx < _active.Count)
            _active.RemoveRange(writeIdx, _active.Count - writeIdx);

        _count = _active.Count;
        return matching.Count > 0 ? matching : Array.Empty<MarketEvent>();
    }
}
```

This eliminates one `List<MarketEvent>` allocation and one full-buffer `AddRange` copy per call
compared to the current implementation, while preserving event ordering.

### E) Feature-gated dispatch pattern for newline scanner

Use a single entry point with a static readonly dispatch field set once at startup:

```csharp
internal static class NewlineScanner
{
    // Delegate is resolved once; no branch on every call after JIT.
    private static readonly Func<ReadOnlySpan<byte>, int, int> _impl =
        Avx2.IsSupported ? FindNextNewlineAvx2 :
        Sse2.IsSupported ? FindNextNewlineSse2 :
        FindNextNewlineScalar;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Find(ReadOnlySpan<byte> data, int start) => _impl(data, start);
}
```

This keeps maintenance manageable and allows deterministic unit tests per hardware path by
calling `FindNextNewlineScalar` directly.

### F) Benchmark harness additions

Extend the existing `BenchmarkDotNet` projects in `benchmarks/Meridian.Benchmarks/`
with:

- `FindNextNewline`: scalar vs `SearchValues` vs SSE2 vs AVX2 across 1 KB / 64 KB / 4 MB chunks.
- Sequence parse: current `string`-based path vs UTF-8 byte path across 1 K / 10 K lines.
- Bucket selection: linear scan vs binary search for 8, 16, and 32 boundaries.
- Buffer drain: partial `Drain(maxCount)` with `GetRange+RemoveRange` vs ring buffer.
- `DrainBySymbol`: current two-list approach vs in-place partition + integer equality.

Suggested benchmark dimensions:

- File profiles: small/medium/large JSONL chunks.
- Line lengths: short ticker events vs long depth-of-book updates.
- CPU targets: x64 AVX2, x64 SSE2-only, ARM64 (use `AdvSimd` path instead of AVX2).
- Data characteristics: newline density, invalid-JSON rate, and sequence-missing frequency.

### G) Validation checklist before merging any optimized path

1. **Correctness parity**: all existing tests pass with the optimized path active.
2. **Fallback safety**: force the scalar path via a compile switch and confirm identical results.
3. **Perf evidence**: `BenchmarkDotNet` results show a measurable win on at least two
   representative datasets; include results in the PR description.
4. **No regression in allocations**: `BenchmarkDotNet` memory diagnostics show equal or lower
   GC-allocated bytes per operation.
5. **Operational confidence**: replay/quality/report end-to-end timing improves measurably
   on a representative production-sized dataset.
