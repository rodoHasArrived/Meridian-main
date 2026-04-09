# Assembly-Level Performance Roadmap

**Version:** 1.0
**Last Updated:** 2026-04-08
**Audience:** Core contributors, performance engineers
**Source Evaluation:** [`docs/evaluations/assembly-performance-opportunities.md`](../evaluations/assembly-performance-opportunities.md)

---

This document translates the seven improvement candidates identified in the assembly-performance
evaluation into a **viability-assessed, phase-ordered implementation roadmap**. Each candidate is
scored against the Meridian codebase's actual risk profile, and all code locations are pinned to the
production files they touch.

This roadmap corresponds to Meridian's optional advanced-performance track. It should normally follow the core operator-readiness path unless profiling shows a concrete performance blocker affecting an active Wave 1-6 deliverable.

---

## Viability Assessment Summary

| # | Candidate | Source Files | Estimated ROI | Risk | Viability | Phase |
| --- | --------- | ------------ | ------------- | ---- | --------- | ----- |
| 1a | JSONL newline scan — SIMD (`FindNextNewline`) | `MemoryMappedJsonlReader.cs` L237-275 | High | Low | ✅ Viable | B |
| 1b | JSONL newline scan — defer UTF-16 decode | `MemoryMappedJsonlReader.cs` L237-275 | High | Medium | ✅ Viable | A |
| 2 | UTF-8 sequence extraction (byte path) | `DataQualityScoringService.cs` L252-265 | Medium-High | Low | ✅ Viable | B |
| 3a | `LatencyHistogram` binary-search bucket | `LatencyHistogram.cs` L255-265 | Medium | Low | ✅ Viable | A |
| 3b | `LatencyHistogram` circular sample buffer | `LatencyHistogram.cs` L166-168 | Medium | Low | ✅ Viable | A |
| 4a | `EventBuffer.Drain` ring-buffer variant | `EventBuffer.cs` L151-167 | Medium | Medium | ✅ Viable | B |
| 4b | `DrainBySymbol` symbol-interning + single alloc | `EventBuffer.cs` L228-257 | Medium | Low | ✅ Viable | A |
| 5 | Anomaly rolling stats (SIMD accumulate) | `AnomalyDetector.cs` L61-85 | Low-Medium | High | ⚠️ Profile-First | C |

---

## Detailed Viability Assessments

### 1a — JSONL Newline Scan (SIMD)

**Files:** `src/Meridian.Storage/Replay/MemoryMappedJsonlReader.cs` (lines 237-275)

**Viability:** ✅ Viable — Low Risk

The existing benchmark harness (`benchmarks/Meridian.Benchmarks/`) already includes
`WalChecksumBenchmarks`, confirming the team can integrate new `BenchmarkDotNet` cases quickly.
The `.NET 8+ SearchValues<byte>` portable path (`MemoryExtensions.IndexOf`) is a JIT-selected
SIMD path and requires no `#if` guards. The AVX2 manual path (`Vector256<byte>` + `Avx2.MoveMask`)
is additive and protected by `Avx2.IsSupported`. The fallback scalar path is unchanged. Risk is
low because the optimization is isolated to a private helper method.

**Prerequisites:** Representative 100 MB+ replay file for benchmarking.

---

### 1b — Defer UTF-16 Materialization

**Files:** `src/Meridian.Storage/Replay/MemoryMappedJsonlReader.cs` (lines 237-275)

**Viability:** ✅ Viable — Medium Risk

`Encoding.UTF8.GetString` allocates a new `string` per line. Moving to `ReadOnlyMemory<byte>`
slices requires updating the type passed to `DeserializeLines`, which is an internal caller
boundary. The change is medium risk because it alters the interface between the scanner and the
deserializer. The existing `MemoryMappedJsonlReaderTests` must be updated to validate byte-slice
paths. Deferring UTF-16 decode can be developed independently of 1a.

**Prerequisites:** Confirm `DeserializeLines` callers use the result only within the current
`ArrayPool` lease window.

---

### 2 — UTF-8 Sequence Extraction

**Files:** `src/Meridian.Storage/Services/DataQualityScoringService.cs` (lines 252-265)

**Viability:** ✅ Viable — Low Risk

The replacement is a private static helper `TryExtractSequenceUtf8(ReadOnlySpan<byte>, out long)`.
The production evaluation document provides a complete, overflow-safe implementation sketch
(see Section B of the source document). The switch from `File.ReadLinesAsync` to `FileStream` +
`ArrayPool<byte>` buffer is a localized change in `ComputeSequenceScoreAsync` only. The existing
`DataQualityScoringServiceTests` provide a correctness baseline.

**Prerequisites:** None. Fully self-contained change.

---

### 3a — `LatencyHistogram` Binary-Search Bucket

**Files:** `src/Meridian.Application/Monitoring/DataQuality/LatencyHistogram.cs` (lines 255-265)

**Viability:** ✅ Viable — Low Risk

A drop-in replacement for the 8-iteration linear scan. `Array.BinarySearch` or the manual binary
search sketch in the evaluation (Section C) both work. For the current default of 8 boundaries
the benefit is ~3–7 comparisons saved per event, which is modest in isolation but accumulates
under high-volume ingest because `Record()` executes under a `lock`. No external API changes.

**Prerequisites:** None. Add a benchmark case to confirm improvement.

---

### 3b — `LatencyHistogram` Circular Sample Buffer

**Files:** `src/Meridian.Application/Monitoring/DataQuality/LatencyHistogram.cs` (lines 166-168)

**Viability:** ✅ Viable — Low Risk

Replacing `List<LatencySample>` and `RemoveAt(0)` with a fixed-size `LatencySample[]` ring
eliminates O(n) front-shift work under lock on every overflow call. The percentile calculation
that reads `_samples` must be updated to iterate the ring using `(_sampleHead - _sampleCount + i) % MaxSamples`.
This is an internal-only change. Existing tests on percentile output serve as a correctness gate.

**Prerequisites:** Confirm `MaxSamples` default value so the ring can be statically sized.

---

### 4a — `EventBuffer.Drain` Ring-Buffer Variant

**Files:** `src/Meridian.Application/Pipeline/EventBuffer.cs` (lines 151-167)

**Viability:** ✅ Viable — Medium Risk

`GetRange + RemoveRange` allocates and copies on every partial drain. A ring-buffer backed by a
power-of-two array eliminates both. The risk is medium because `EventBuffer<T>` is a shared
infrastructure type used by multiple sinks. The ring-buffer and existing `List<T>` variant could
coexist via constructor injection, allowing callers to opt in. `EventBufferTests` cover
correctness and must be extended to cover the ring path.

**Prerequisites:** Audit all callers of `Drain(int maxCount)` to confirm they can accept a
`Memory<T>` slice rather than a new `List<T>`. Consider a feature flag or new subtype.

---

### 4b — `DrainBySymbol` Symbol-Interning

**Files:** `src/Meridian.Application/Pipeline/EventBuffer.cs` (lines 228-257)

**Viability:** ✅ Viable — Low Risk

`SymbolTable` already exists in `src/Meridian.Core/Performance/SymbolTable.cs`. The
evaluation sketch (Section D) replaces two `List<MarketEvent>` allocations and one `AddRange`
copy per call with an in-place partition using integer symbol IDs. This is purely internal to
`MarketEventBuffer`. `EventBufferTests` must be extended for the new in-place path.

**Prerequisites:** Confirm `SymbolTable` is already referenced by `EventBuffer`'s assembly.

---

### 5 — Anomaly Rolling Statistics (SIMD Accumulate)

**Files:** `src/Meridian.Application/Monitoring/DataQuality/AnomalyDetector.cs` (lines 61-85)

**Viability:** ⚠️ Profile-First — High Risk

The evaluation requires profiling to confirm `AnomalyDetector` is CPU-bound before investing in
`Vector<double>` horizontal-add patterns. In most Meridian deployments the monitoring thread is
background-priority and the anomaly window is per-symbol, making this path unlikely to be hot
enough to justify SIMD complexity. If profiling shows otherwise, the `Vector<double>` approach
in the evaluation is a valid next step.

**Decision gate:** Do not implement Phase C until BenchmarkDotNet or sampling profiler data
confirms `RecordTrade` contributes > 5% of CPU time in a representative run.

---

## Implementation Phases

### Phase A: Algorithmic Improvements (Low Risk)

**Estimated effort:** Medium (3–5 days total)
**Suggested branch:** `perf/phase-a-algorithmic`
**Prerequisites:** None
**Exit criteria:** All existing tests pass; BenchmarkDotNet results included in PR.

#### P-A-1: `LatencyHistogram` — Binary-Search Bucket + Circular Sample Buffer (items 3a, 3b)

- [ ] Add benchmark case `LatencyHistogramBenchmarks` to `benchmarks/Meridian.Benchmarks/`
      measuring `Record()` throughput with 8, 16, and 32 boundaries.
- [ ] Replace linear bucket scan (lines 255-265 of `LatencyHistogram.cs`) with the `FindBucket`
      binary search (`Array.BinarySearch` or manual as in evaluation Section C).
- [ ] Replace `List<LatencySample>` with a fixed-size `LatencySample[]` ring; update
      `_sampleHead` / `_sampleCount` fields; update percentile read path.
- [ ] Run benchmarks; confirm ≥ 10% throughput improvement under lock on 8-bucket config.
- [ ] Run `dotnet test tests/Meridian.Tests` and confirm all monitoring tests pass.

#### P-A-2: Defer UTF-16 Decode in `MemoryMappedJsonlReader` (item 1b)

- [ ] Add benchmark case `NewlineScanBenchmarks` measuring line-scan throughput on 1 MB / 64 MB
      byte arrays.
- [ ] Introduce `ReadOnlyMemory<byte>` slice path: replace per-line `Encoding.UTF8.GetString`
      with a byte slice held in the pooled `ArrayPool` buffer; decode only after line passes
      downstream filter.
- [ ] Update `DeserializeLines` (or its callers) to accept `ReadOnlyMemory<byte>` instead of
      `string` (internal boundary).
- [ ] Run benchmarks; confirm allocation reduction (GC-allocated bytes per operation).
- [ ] Run `dotnet test tests/Meridian.Tests` and confirm all replay and reader tests pass.

#### P-A-3: `DrainBySymbol` In-Place Partition + Symbol Interning (item 4b)

- [ ] Verify `SymbolTable` (`src/Meridian.Core/Performance/SymbolTable.cs`) is
      accessible from `EventBuffer`'s assembly.
- [ ] Add in-place partition method `DrainBySymbolId(int symbolId)` to `MarketEventBuffer`
      using the evaluation Section D sketch.
- [ ] Update callers of `DrainBySymbol(string)` to resolve via `SymbolTable.GetOrAdd` before
      calling the new int-keyed overload; preserve the old overload as a thin wrapper.
- [ ] Add benchmark case for `DrainBySymbol` current vs in-place partition.
- [ ] Extend `EventBufferTests` with in-place partition correctness tests.
- [ ] Run `dotnet test tests/Meridian.Tests` to confirm no regression.

---

### Phase B: Hot-Path Byte-Level Rewrites (Medium Risk)

**Estimated effort:** High (5–8 days total)
**Suggested branch:** `perf/phase-b-byte-paths`
**Prerequisites:** Phase A merged; representative 100 MB+ replay file available for benchmarking.
**Exit criteria:** All existing tests pass; BenchmarkDotNet results included in PR; GC
allocation diagnostic shows reduction.

#### P-B-1: SIMD Newline Scanner for `MemoryMappedJsonlReader` (item 1a)

- [ ] Add `NewlineScanBenchmarks` to the benchmark project (if not already added in Phase A)
      covering 1 KB / 64 KB / 4 MB chunks with varying newline density.
- [ ] Implement `static class NewlineScanner` with the feature-gated dispatch pattern from
      evaluation Section E: `SearchValues` portable path (primary), `AVX2` path (x64), scalar
      fallback.
- [ ] Replace the inner `for (int i = 0; i < chunkSize; i++)` loop in
      `ReadFileMemoryMappedAsync` (lines 237-269) with calls to `NewlineScanner.Find`.
- [ ] Gate the AVX2 path with `Avx2.IsSupported`; always compile scalar fallback.
- [ ] Write unit tests calling `FindNextNewlineScalar` directly to confirm boundary and
      wrap-around correctness.
- [ ] Run benchmarks; confirm ≥ 1.5× speedup on 4 MB chunks.
- [ ] Run `dotnet test tests/Meridian.Tests` and confirm all replay tests pass.

#### P-B-2: UTF-8 Byte Path for Sequence Scoring (item 2)

- [ ] Implement `TryExtractSequenceUtf8(ReadOnlySpan<byte> line, out long sequence)` as a
      private static method in `DataQualityScoringService` using evaluation Section B sketch.
- [ ] Replace `File.ReadLinesAsync` in `ComputeSequenceScoreAsync` with a `FileStream` +
      `ArrayPool<byte>` buffered reader that produces `ReadOnlySpan<byte>` slices.
- [ ] Keep the 10 000-line cap; apply it to bytes-processed rather than string-line count.
- [ ] Extend `DataQualityScoringServiceTests` to cover the byte-path extraction on synthetic
      JSONL with known sequence values.
- [ ] Add benchmark case `SequenceScoringBenchmarks`: current `string` path vs UTF-8 byte path
      across 1 K / 10 K lines.
- [ ] Run benchmarks; confirm ≥ 40% allocation reduction.
- [ ] Run `dotnet test tests/Meridian.Tests` to confirm no regression.

#### P-B-3: `EventBuffer.Drain` Ring-Buffer Variant (item 4a)

- [ ] Audit all callers of `Drain(int maxCount)` to determine if they can consume a
      `Memory<T>` slice or if a new `List<T>` is required by external contracts.
- [ ] Implement `RingEventBuffer<T>` (or ring-buffer mode flag on `EventBuffer<T>`) backed by
      a power-of-two array with head/tail indices.
- [ ] Ensure `Drain(int maxCount)` on the ring variant is O(1) amortized with at most two
      `Array.Copy` calls (wrap-around case).
- [ ] Provide an opt-in constructor parameter so existing callers are unaffected.
- [ ] Extend `EventBufferTests` to cover ring-buffer drain under concurrent add/drain.
- [ ] Add benchmark case: `GetRange+RemoveRange` vs ring drain across 100 / 1 000 / 10 000
      element buffers with partial drain ratios of 10%, 50%, 90%.
- [ ] Run benchmarks; confirm allocation reduction and throughput improvement.
- [ ] Run `dotnet test tests/Meridian.Tests` to confirm no regression.

---

### Phase C: Profile-Gated SIMD Kernel (High Risk — Profile First)

**Estimated effort:** High (3–5 days if profiling confirms hotness)
**Suggested branch:** `perf/phase-c-anomaly-simd`
**Prerequisites:** Phases A and B merged; profiling session confirming `RecordTrade` > 5% CPU.
**Exit criteria:** Profiling data attached to PR; `Vector<double>` path produces identical
results as scalar path under forced-scalar mode.

#### P-C-1: Anomaly Detector Rolling Stats Vectorization (item 5)

- [ ] **Gate:** Run BenchmarkDotNet or attach a CPU sampling profile from a 30-minute live
      collection session. Proceed only if `AnomalyDetector.ProcessTrade` or
      `SymbolStatistics.RecordTrade` exceeds 5% of total CPU time in the profile.
- [ ] If gate passes: restructure `SymbolStatistics` rolling-window array to struct-of-arrays
      layout for cache-line density.
- [ ] Replace scalar `_sumOfPrices += price; _sumOfSquares += price * price;` accumulation
      with `Vector<double>` horizontal-add over incoming price batches.
- [ ] Keep the scalar path and select via a compile-time constant or runtime feature flag for
      unit test determinism.
- [ ] Run BenchmarkDotNet before/after; attach results to PR.
- [ ] Run `dotnet test tests/Meridian.Tests` and confirm anomaly detection tests pass.

---

## Benchmark Harness Requirements

Extend `benchmarks/Meridian.Benchmarks/` with the following new benchmark files:

| File | Covers |
| ---- | ------ |
| `NewlineScanBenchmarks.cs` | Items 1a, 1b — scalar vs `SearchValues` vs AVX2 newline scan |
| `SequenceScoringBenchmarks.cs` | Item 2 — `string`-based vs UTF-8 byte path |
| `LatencyHistogramBenchmarks.cs` | Items 3a, 3b — linear vs binary bucket; list vs ring samples |
| `EventBufferDrainBenchmarks.cs` | Items 4a, 4b — partial drain and `DrainBySymbol` comparison |

All new benchmark classes must:

- Annotate with `[MemoryDiagnoser]` to capture GC allocation metrics.
- Parametrize over representative data sizes (small / medium / large).
- Run on at least one x64 machine with AVX2 and one without (SSE2 only) if using SIMD paths.

---

## Validation Checklist (per PR)

Before merging any optimized path:

- [ ] All existing tests pass with the optimized path active (`dotnet test` green).
- [ ] Scalar fallback path tested independently by forcing the non-SIMD code path.
- [ ] BenchmarkDotNet results attached to PR description showing measured improvement.
- [ ] `[MemoryDiagnoser]` confirms equal or lower GC-allocated bytes per operation.
- [ ] No new `Version` attributes added to `<PackageReference>` items (CPM policy).
- [ ] New intrinsic helpers marked `private static` and guarded by feature-detection.
- [ ] ADR compliance check: `[ImplementsAdr]` attributes where applicable.
- [ ] XML doc comments on all new public or internal API surfaces.

---

## Risk Register

| Risk | Likelihood | Impact | Mitigation |
| ---- | ---------- | ------ | ---------- |
| SIMD path produces wrong results on non-AVX2 hardware | Low | High | Always compile and test scalar fallback; CI must include non-AVX2 run |
| `DeserializeLines` callers hold byte slices beyond pool lease window | Medium | High | Audit all callers before Phase A-2; add lease-scope comments |
| `EventBuffer` ring-buffer mode breaks a sink's partial-drain invariant | Medium | Medium | Opt-in constructor; keep original `List<T>` path as default |
| Anomaly SIMD incorrectly skips profiling gate | High | Low | PR template requires profiling data attachment for Phase C |
| Performance regression from ring-buffer wrap-around edge case | Low | Medium | Cover wrap-around in `EventBufferTests` with concurrent add/drain |

---

## Open Questions

| # | Question | Owner | Impact |
| --- | -------- | ----- | ------ |
| 1 | Does `DeserializeLines` need `string` semantics, or can it accept `ReadOnlyMemory<byte>`? | Storage team | Unblocks Phase A-2 |
| 2 | Is `SymbolTable` already in scope for `EventBuffer`'s assembly? | Core team | Unblocks Phase A-3 |
| 3 | What is the `MaxSamples` default in `LatencyHistogram`? Static or config? | Monitoring team | Unblocks Phase A-1 ring sizing |
| 4 | Is a 100 MB+ replay file available for benchmark runs in CI? | DevOps | Unblocks Phase B benchmarks |
| 5 | Should Phase B-3 ring buffer be opt-in (constructor flag) or replace default? | Core team | Design decision for Phase B-3 |

---

## Related Documents

- **Source evaluation:** [`docs/evaluations/assembly-performance-opportunities.md`](../evaluations/assembly-performance-opportunities.md)
- **Main roadmap:** [`docs/status/ROADMAP.md`](../status/ROADMAP.md) — optional advanced-performance track
- **Benchmark project:** [`benchmarks/Meridian.Benchmarks/`](https://github.com/rodoHasArrived/Meridian/blob/main/benchmarks/Meridian.Benchmarks)
- **`SymbolTable`:** `src/Meridian.Core/Performance/SymbolTable.cs`
- **`MemoryMappedJsonlReader`:** `src/Meridian.Storage/Replay/MemoryMappedJsonlReader.cs`
- **`DataQualityScoringService`:** `src/Meridian.Storage/Services/DataQualityScoringService.cs`
- **`LatencyHistogram`:** `src/Meridian.Application/Monitoring/DataQuality/LatencyHistogram.cs`
- **`EventBuffer`:** `src/Meridian.Application/Pipeline/EventBuffer.cs`
- **`AnomalyDetector`:** `src/Meridian.Application/Monitoring/DataQuality/AnomalyDetector.cs`

---

_Last Updated: 2026-04-08_
