# Ingestion Correctness Fixes — Scope

**Audience:** Implementing developer
**Source of truth:** Verified current-state spec (line numbers from current `main`)
**Scope:** Three ingestion correctness fixes — (1) `PersistentDedupLedger` fire-and-forget durability, (2) `IngestionJobService` non-atomic persistence, (3) EDGAR provider stub catalog.

---

## Fix 1 — `PersistentDedupLedger` fire-and-forget durability

**File:** `/home/user/Meridian-main/src/Meridian.Application/Pipeline/PersistentDedupLedger.cs`

### Severity & rationale

**Severity: High (silent data-correctness defect).**

`IsDuplicateAsync` returns `false` — telling the pipeline "this event is new, emit it downstream" — *before* the dedup record is durable on disk. The write path:

- Line 187: in-memory cache updated first (`_cache[key] = nowTicks;`).
- Line 195: `await _writer!.WriteLineAsync(ledgerLine.AsMemory(), ct)` — writes only into the 4 KB `StreamWriter` buffer. The writer is created with `AutoFlush = false` (line 215).
- Line 202: `return false;` — reports "not duplicate" while the line is still buffered.

The buffer is only drained by `FlushAsync` (line 609), `CompactAsync` (line 635), or `DisposeAsync` (line 689) — none of which is on the hot path; `FlushAsync` is invoked only by external schedulers. **On a crash between emit and the next external flush, the downstream event was already emitted but its dedup record never reached disk.** On restart, `InitializeAsync` (lines 111–161) reloads `dedup_ledger.jsonl` line-by-line and rebuilds `_cache` without that key, so the same event is **reprocessed as new → duplicate emission downstream.** This breaks the at-most-once guarantee the dedup ledger exists to provide.

The cache-before-write ordering (line 187) is itself correct and must stay — concurrent in-flight calls must observe the entry immediately. The only correctness defect is that the disk line is not durable before the method reports the event as new.

### Exact change-set

Add a strict-durability mode gated by a constructor flag, defaulting to current (throughput-optimized) behavior. Two coordinated edits.

**Edit 1 — add field (around lines 25–26) and constructor parameter (lines 58–73):**

```csharp
private readonly bool _flushOnWrite;
```

```csharp
public PersistentDedupLedger(
    string ledgerDirectory,
    TimeSpan? entryTtl = null,
    int maxInMemoryEntries = 500_000,
    bool flushOnWrite = false)   // NEW — default false preserves current throughput behavior
{
    // ...existing assignments...
    _flushOnWrite = flushOnWrite;
    // ...
}
```

**Edit 2 — flush inside the existing `_writeLock` in `IsDuplicateAsync` (lines 191–200), so durability is achieved before the lock releases and before `return false`:**

Current (lines 190–201):

```csharp
// Persist to disk (fire-and-forget the write, but serialize access)
await _writeLock.WaitAsync(ct).ConfigureAwait(false);
try
{
    await EnsureWriterInitializedAsync(ct).ConfigureAwait(false);
    await _writer!.WriteLineAsync(ledgerLine.AsMemory(), ct).ConfigureAwait(false);
}
finally
{
    _writeLock.Release();
}
```

Proposed:

```csharp
// Persist to disk; under strict durability, flush before reporting the event as new.
await _writeLock.WaitAsync(ct).ConfigureAwait(false);
try
{
    await EnsureWriterInitializedAsync(ct).ConfigureAwait(false);
    await _writer!.WriteLineAsync(ledgerLine.AsMemory(), ct).ConfigureAwait(false);
    if (_flushOnWrite)
    {
        await _writer.FlushAsync(ct).ConfigureAwait(false);   // durable before IsDuplicateAsync returns false
    }
}
finally
{
    _writeLock.Release();
}
```

Also update the stale line-190 comment (`fire-and-forget the write`) to reflect the new behavior.

**Optional crash-durability hardening (separate, larger):** `StreamWriter.FlushAsync` flushes to the OS, not to the physical disk. For full crash-durability, upgrade `EnsureWriterInitializedAsync` (lines 205–217) to retain the `FileStream` reference (`fs`) as a field and, when `_flushOnWrite` is set, call `await fs.FlushAsync(ct)` after the writer flush. **The minimal version flushes the `StreamWriter` only** and closes the application-crash window (process-kill / unhandled-exception path); only OS/power loss would still be at risk. Recommend shipping minimal first; treat the `FileStream.FlushAsync` upgrade as a follow-up if power-loss durability is required.

### Options / DI wiring

**DI construction site (the only one in product code):**
`/home/user/Meridian-main/src/Meridian.Application/Composition/Features/PipelineFeatureRegistration.cs:162-168`

Current:

```csharp
services.AddSingleton<PersistentDedupLedger>(sp =>
{
    var storageOptions = sp.GetRequiredService<StorageOptions>();          // line 164
    var ledger = new PersistentDedupLedger(Path.Combine(storageOptions.RootPath, "_dedup")); // line 165
    ledger.InitializeAsync().GetAwaiter().GetResult();
    return ledger;
});
```

To wire strict durability, source the flag from `StorageOptions` (already resolved on line 164):

```csharp
var ledger = new PersistentDedupLedger(
    Path.Combine(storageOptions.RootPath, "_dedup"),
    flushOnWrite: storageOptions.DedupFlushOnWrite);   // new option, defaulted false
```

- Add a `bool DedupFlushOnWrite` property to `StorageOptions` (confirm its definition before adding; it is the existing options type already injected here).
- **Default `false`** → no behavior or performance change unless an operator opts in.
- **No new DI registration** — the singleton factory already has `sp` / `storageOptions` in scope.

**Backward compatibility:** the constructor parameter is optional and defaulted, so the DI site, all consumers, and all ~9 test/benchmark construction sites compile unchanged.

### Blast radius

- **DI:** single construction site (`PipelineFeatureRegistration.cs:162-168`); compiles unchanged because the new param is optional. Flag wiring is opt-in.
- **Consumers:** `EventPipeline` (`EventPipeline.cs:53,141,195,250`) holds it as `PersistentDedupLedger?` and calls `IsDuplicateAsync`; the signature is unchanged → no impact.
- **Tests/benchmarks (all positional, directory-only):** `PersistentDedupLedgerTests.cs:51,54,76,94`, `WalEventPipelineTests.cs:334`, `AllocationBudgetIntegrationTests.cs:85,116,145,171`, `DeduplicationKeyBenchmarks.cs:61` — optional param ⇒ all compile unchanged.
- **Performance:** when `flushOnWrite=false` (default), **zero change**. When `true`, every *new* (non-duplicate) event triggers a `FlushAsync` under `_writeLock` → a per-write OS flush on the hot path. This serializes writes and adds syscall + I/O latency proportional to new-event rate; it trades throughput for durability. This is exactly why it is gated behind a flag defaulting to `false` — high-throughput ingestion keeps current behavior; deployments that need at-most-once across crashes opt in. (Duplicate hits short-circuit earlier in `IsDuplicateAsync` and never reach the write path, so the flush cost applies only to genuinely new events.)

### Test plan

**Project/folder:** `/home/user/Meridian-main/tests/Meridian.Tests/Application/Pipeline/` — namespace `Meridian.Tests.Application.Pipeline`. Extend `PersistentDedupLedgerTests.cs` (FluentAssertions cohort, `IAsyncLifetime`, dispose-and-reconstruct restart idiom). Add `using FluentAssertions;` per-file (`using Xunit;` is a global using).

**Critical crash-simulation note:** `DisposeAsync` (line 689) **already flushes the writer**, so the dedup line reaches disk on a clean dispose **regardless of `flushOnWrite`**. A clean dispose-and-reconstruct round-trip therefore passes both before and after this fix and proves nothing about durability-before-return. To actually exercise `flushOnWrite`, the test must simulate a **hard crash that bypasses dispose** — abandon the instance **without calling `DisposeAsync`/`FlushAsync`** (let it be garbage-collected; do not dispose), or drive the writes through a separate child process that is killed mid-run. The assertions below are written against the bypass-dispose approach.

1. **`IsDuplicateAsync_FlushOnWrite_PersistsBeforeReturnWithoutDisposeOrFlush`** — construct `new PersistentDedupLedger(dir, flushOnWrite: true)`, `InitializeAsync()`, call `IsDuplicateAsync(evt)` (returns `false`). **Without calling `DisposeAsync` or `FlushAsync`** (deliberately abandon the instance — do not dispose, since dispose would flush and mask the defect), read the on-disk file directly and assert the dedup line is already present: `(await File.ReadAllLinesAsync(Path.Combine(dir, "dedup_ledger.jsonl"))).Should().ContainSingle().Which.Should().Contain("\"k\":", "the dedup line must be durable before IsDuplicateAsync reports the event as new — without any explicit flush or dispose");`. Optionally then construct a second instance against the same `dir`, `InitializeAsync()`, and assert `(await secondLedger.IsDuplicateAsync(evt)).Should().BeTrue()`. The load-bearing assertion is reading the file **without** flush/dispose — that is what distinguishes pre-fix (file empty) from post-fix (line present).

2. **`IsDuplicateAsync_DefaultFlushOff_DoesNotFlushPerWrite`** (regression-guard for default behavior) — construct with the default constructor (no flag); after a single `IsDuplicateAsync(evt)` and **before** any `FlushAsync`/`DisposeAsync`, read the on-disk file directly and assert it is still empty or absent (the in-memory `_cache` still reports duplicates in-process, but disk is buffered). This documents that the throughput default is intentionally unchanged and contrasts directly with test 1: same bypass-dispose technique, opposite expected disk state. Use the existing flush-then-reload test as an additional contrast case.

3. **Concurrent-correctness sanity (`flushOnWrite: true`)** — fire N concurrent `IsDuplicateAsync` calls for the same key and assert exactly one returns `false` (the cache-before-write ordering at line 187 still de-dups concurrent callers), and the on-disk file contains exactly one line after dispose.

---

## Fix 2 — `IngestionJobService` non-atomic persistence

**File:** `/home/user/Meridian-main/src/Meridian.Application/Pipeline/IngestionJobService.cs`

### Severity & rationale

**Severity: High (job-state loss on crash).**

`PersistJobAsync` (lines 389–406) writes job state with `File.WriteAllTextAsync(filePath, json, ct)` at **line 396**, which opens the destination, **truncates it in place**, then streams JSON. A crash mid-write leaves a truncated/partial `job_{id}.json` on disk.

The load path compounds this. `LoadJobsAsync` (lines 65–93) reads (line 78) and deserializes (line 79); a malformed/truncated file throws and is caught at lines 86–89 (`_log.Warning(...)`) and **silently dropped**. So a mid-write crash does not merely delay a job update — combined with truncate-in-place, it **permanently loses the job's persisted state** on next load.

Keying/format facts: `GetJobFilePath(string jobId)` (lines 386–387) = `Path.Combine(_persistenceDir, $"job_{jobId}.json")`; `job.JobId` is a GUID string (`IngestionJob.cs:24`); loader globs `job_*.json` (line 70). Serialization is **reflection-based** `_jsonOptions` (lines 27–31), **not** source-generated → no `JsonContext`/source-gen change required.

### Exact change-set

Replace the non-atomic write with `AtomicFileWriter.WriteAsync`, mirroring `BackfillStatusStore` (`/home/user/Meridian-main/src/Meridian.Storage/Backfill/BackfillStatusStore.cs:33-37`), which already does the serialize-then-atomic-write pattern.

**Edit 1 — add using (after line 7 usings):**

```csharp
using Meridian.Storage.Archival;
```

**Edit 2 — replace line 396 inside `PersistJobAsync` (the rest of the method, including `_persistLock` and the catch, stays):**

Current:

```csharp
await File.WriteAllTextAsync(filePath, json, ct).ConfigureAwait(false);   // line 396 — non-atomic, truncates-then-writes in place
```

Proposed:

```csharp
await AtomicFileWriter.WriteAsync(filePath, json, ct).ConfigureAwait(false);   // atomic: temp + fsync + rename
```

**AtomicFileWriter API (verified):** `/home/user/Meridian-main/src/Meridian.Storage/Archival/AtomicFileWriter.cs`, `public static async Task WriteAsync(string destinationPath, string content, CancellationToken ct = default)` (lines 65–106). It writes to a temp file `.{file}.{guid}.tmp` (line 453), fsyncs the temp, `File.Move(..., overwrite: true)` for an atomic rename (line 92), then fsyncs the directory (POSIX dir-fd fsync; NTFS no-op). A reader therefore sees either the complete old file or the complete new file — never a truncated one.

**Optional hardening (separate from atomicity):** in `LoadJobsAsync`, on deserialize/IO failure attempt a `.bak`/`.tmp` sidecar rather than silently dropping. Not required — the atomic write removes the truncated-file failure mode entirely, making the silent-skip catch at lines 86–89 effectively unreachable for partial-write corruption.

### Options / DI wiring

**None required.** The change is internal to `PersistJobAsync`; the constructor signature is unchanged.

- `IngestionJobService` is constructed at `/home/user/Meridian-main/src/Meridian.Application/Composition/Features/BackfillFeatureRegistration.cs:45-52` and exposed as `IEtlIngestionJobCoordinator` at `EtlFeatureRegistration.cs:50` — **no DI change.**
- **Project reference already present:** `Meridian.Application → Meridian.Storage` (`Meridian.Application.csproj:41`). `AtomicFileWriter.WriteAsync(string, string, ct)` is already used in ~10 Application files (e.g. `ConfigStore.cs:109`, `StatusWriter.cs:76`, `SchedulingService.cs:425`). **No new reference, no new package.**

### Blast radius

- **Internal-only** change to `PersistJobAsync`; all callers unaffected (`CreateJobAsync:123`, `TransitionAsync:190`, `UpdateCheckpointAsync:226`, `UpdateSymbolProgressAsync:291`) — same signature, same `_persistLock`.
- **On-disk filename/format unchanged** (`job_{id}.json`, same JSON). `LoadJobsAsync` glob `job_*.json` (line 70) still matches; `DeleteJobAsync` (line 369) uses the same `GetJobFilePath`. `AtomicFileWriter` temp files are leading-dot hidden names (`.job_*.json.{guid}.tmp`), do **not** match the `job_*.json` glob, and are removed on success → **no migration, no stray loads.**
- **Performance:** `AtomicFileWriter.WriteAsync` adds a temp-write + fsync(s) + rename per persist versus a single truncate-write. Job persists are low-frequency (create/transition/checkpoint/symbol-progress), not a hot ingestion loop, so the added durability cost is negligible and acceptable unconditionally — **no flag needed.**
- **Tests unaffected:** `IngestionJobServiceTests.cs`, `IngestionJobServiceCoordinationTests.cs`, `EtlJobOrchestratorTests.cs`, `IngestionJobTests.cs`.

### Test plan

**Project/folder:** `/home/user/Meridian-main/tests/Meridian.Tests/Application/Pipeline/` — namespace `Meridian.Tests.Application.Pipeline`. Extend `IngestionJobServiceTests.cs` (already has `IDisposable` temp-dir lifecycle and the reload-into-new-instance idiom: `LoadJobsAsync_RestoresPersistedJobs`). Add `using FluentAssertions;` per-file.

1. **`PersistJobAsync_WritesAtomically_NoTempFilesRemainAndJobFileValid`** — create + transition a job, assert `File.Exists(Path.Combine(_tempDir, $"job_{job.JobId}.json")).Should().BeTrue()`, assert the persisted content parses (`var act = () => JsonDocument.Parse(File.ReadAllText(path)); act.Should().NotThrow();`), and assert **no leftover temp files**: `Directory.GetFiles(_tempDir, ".job_*.tmp").Should().BeEmpty()` (matches the `.job_{id}.json.{guid}.tmp` shape) and the loader glob still resolves exactly one file (`Directory.GetFiles(_tempDir, "job_*.json").Should().ContainSingle()` — the leading-dot temp name does not match this glob).

2. **`LoadJobsAsync_TruncatedJobFile_SkipsCorruptAndLoadsValid`** (corrupt-file-skip behavior, mirrors `BackfillStatusStoreTests` corrupt-file tolerance) — persist two valid jobs through `_service`, then **after disposing/closing** the service handle, directly truncate one `job_{id}.json` on disk to a partial JSON fragment (`File.WriteAllText(path, "{\"JobId\":\"...\",\"Sta")`). Construct `using var service2 = new IngestionJobService(_tempDir); await service2.LoadJobsAsync();` and assert the valid job round-trips (`service2.GetJob(validId).Should().NotBeNull()`) while the corrupt one is skipped (`service2.GetJob(corruptId).Should().BeNull()`). This documents the load-side resilience and demonstrates that, post-fix, the atomic write prevents this corrupt state from arising on the write path.

3. **`PersistThenReload_RoundTripsState`** (atomic-write-survives, restart idiom) — extend/confirm `LoadJobsAsync_RestoresPersistedJobs`: create + transition + checkpoint + symbol-progress on `_service`, then `using var service2 = new IngestionJobService(_tempDir); await service2.LoadJobsAsync();` and assert `service2.GetJob(id)` round-trips state and symbols via `.Should().BeEquivalentTo(...)`. The atomic rename guarantees the reload reads a complete file.

---

## Fix 3 — EDGAR provider stubs (catalog only)

This is a **catalog/triage deliverable**, not a behavior change. Most "empty" returns in the EDGAR files are **legitimate guard clauses** (empty/invalid input, non-success HTTP, absent JSON nodes) and must **not** be touched. The genuine work is concentrated in one file, and is **narrower than a first pass suggests** — exchange and state-of-incorporation mapping already exist.

### Severity & rationale

**Severity: Low–Medium (data-enrichment shallowness, no correctness break).** No crash or data loss — ingested securities are valid but under-described. The cost is downstream reference-data quality (missing identifiers/fields, coarse asset classification), which affects matching/reporting completeness rather than integrity.

### Catalog

**Genuine gaps — `/home/user/Meridian-main/src/Meridian.Infrastructure/Adapters/Edgar/EdgarSecurityMasterIngestProvider.cs`:**

- **`MapToCreateRequest` (lines 215–274) — already richer than "Ticker + CIK only".** It emits a `Ticker` + `Cik` identifier **and** already maps exchange (`["exchangeCode"] = submission?.Exchanges?.FirstOrDefault()`, line 246), `stateOfIncorporation` (line 248), `fiscalYearEnd`, `sic`, `sicDescription`, and `category` into `CommonTerms`. It does hardcode `currency=USD` / `country=US` (lines 244–249 region). **Genuine remaining gap:** it does **not** surface `formerNames` or `addresses`, and does not derive richer security identifiers from those. **It does NOT need exchange added — exchange is already mapped.** Scope the enrichment to `formerNames`/`addresses` (and any other fields EDGAR actually exposes). Note: CUSIP / ISIN / FIGI are **not present in EDGAR submissions at all** and cannot be sourced here — do not promise them.
- **`MapAssetClass` (lines 280–305):** coarse SIC-range heuristic that **defaults everything unmatched to `"Equity"`** (line 303). **Should** distinguish ETFs / notes / preferred instead of collapsing to Equity. This is the higher-value genuine gap.
- **`EdgarSubmission` model (lines 324–358):** already includes `Exchanges` (line 345) and `StateOfIncorporation` (line 351). It omits `formerNames`, `addresses`, and full filing history → those are the fields to widen to back the `formerNames`/`addresses` mapping above.
- **Not stubs (leave as-is):** `Array.Empty<CreateSecurityRequest>()` (line 85) and `yield break` (lines 174, 183) in `FetchAllAsync` / `ParseCompanyTickers` are correct guards on null JSON / parse failure.

**No genuine gaps — `/home/user/Meridian-main/src/Meridian.Infrastructure/Adapters/Edgar/EdgarReferenceDataProvider.cs`:**

- Substantially implemented (real fetch + parse for tickers, submissions, companyfacts, bulk ZIPs, archive documents). The `Array.Empty<...>()` / `null` returns at lines 239, 300, 306, 337, 402, 409, 413, 462, 593, 653, 693, 720, 787, 816, 848 are **all guard clauses** — correct empty results, not stubs. **Do not "fix" these.**
- Lower-priority, deliberate throttles (not stubs): `FetchSecurityDataAsync` (lines 246–295) caps candidate filings at 25 (line 255) and documents at 8 (line 810).

**Net actionable scope:** `formerNames`/`addresses` mapping in `MapToCreateRequest` (exchange + state-of-incorporation are already done), asset-class granularity in `MapAssetClass`, and the corresponding `formerNames`/`addresses` field coverage in the `EdgarSubmission` model — all in `EdgarSecurityMasterIngestProvider.cs`.

### Options / DI wiring

**None.** Pure provider-internal enrichment; no options, no DI changes.

### Blast radius

- Confined to `EdgarSecurityMasterIngestProvider.cs`. Output shape changes are *additive* (more `CommonTerms` keys, finer asset classes) → richer `CreateSecurityRequest` payloads. Verify downstream consumers of `CreateSecurityRequest` tolerate the new asset-class values (e.g. `ETF`, `Note`, `Preferred`) before broadening `MapAssetClass`.
- **Requires upstream data:** richer mapping is only meaningful where `EdgarSubmission` actually carries the fields; widening the model (lines 324–358) for `formerNames`/`addresses` is a prerequisite for those `MapToCreateRequest` improvements. Scope strictly to what EDGAR exposes (exchange MIC — already mapped; `formerNames`; `addresses`; state-of-incorporation — already mapped) rather than promising identifier types (CUSIP/ISIN/FIGI) EDGAR does not provide.
- No performance concern.

### Test plan

**Project/folder:** EDGAR adapter tests belong under `/home/user/Meridian-main/tests/Meridian.Tests/Infrastructure/Providers/` (provider golden-path home; ledger-break fixtures already live there). Use the existing fixture convention — committed JSON payloads — and feed a stub HTTP/JSON layer.

- **`MapToCreateRequest_WithFormerNames_EmitsFormerNamesInCommonTerms`** — given an `EdgarSubmission` fixture carrying `formerNames`, assert the resulting `CreateSecurityRequest` carries those former names. **Important assertion mechanics (verified against the code):** exchange and similar metadata are written to **`CommonTerms`** (a serialized JSON dict), **not** to `Identifiers`. `SecurityIdentifierDto` has **no `Type` member** — its discriminator is `Kind` of enum type `SecurityIdentifierKind` (`Meridian.Contracts/SecurityMaster/SecurityIdentifiers.cs:44-45`), and `"MIC"`/`"formerNames"` are not valid `Kind` values. So assert against the `CommonTerms` JSON keys, e.g. parse `result.CommonTerms` and assert the `formerNames` key is present; for the already-mapped exchange, assert `CommonTerms` contains the `exchangeCode` key (do **not** assert `result.Identifiers.Should().Contain(i => i.Type == "MIC")` — that does not compile and tests the wrong collection).
- **`MapAssetClass_EtfSicRange_ReturnsEtfNotEquity`** — `[Theory]` over representative SIC codes asserting ETF/note/preferred classification instead of the `"Equity"` default (line 303). Keep one case asserting the unmatched default still falls back to `"Equity"` for genuine unknowns.
- **Regression guard:** a test confirming that null/empty `companyTickers` JSON still yields `Array.Empty<CreateSecurityRequest>()` (line 85) and that parse failures still `yield break` — locking the legitimate guards so the enrichment work does not accidentally convert a correct empty result into an error path.

This fix is **deferrable** and should be split into its own work item; it shares no code with Fixes 1–2.

---

## Recommended sequencing

**Do Fix 2 (atomic persistence) first.**
It is the highest value-to-risk ratio: it closes a real **job-state-loss** failure mode with a **one-line swap** (`File.WriteAllTextAsync` → `AtomicFileWriter.WriteAsync`) plus a using. No options, no DI, no constructor change, no on-disk format change, no caller impact, and the project reference and pattern (`BackfillStatusStore`) already exist. It is **independently shippable** and a **safe quick win** — **Effort S, Risk Low**. Land it on its own so it can merge fast.

**Do Fix 1 (dedup durability) second, with design care.**
It addresses an equally serious correctness gap (duplicate downstream emission after crash), but it carries a **real correctness/perf tradeoff that must be called out explicitly:**

- **Perf:** `flushOnWrite=true` forces a per-new-event `FlushAsync` under `_writeLock`, serializing the hot path and adding syscall/I/O latency proportional to new-event rate. This is **why it must stay gated** behind a flag defaulting to `false` — never make strict flushing unconditional.
- **Durability ceiling:** the minimal `StreamWriter.FlushAsync` closes the application-crash window but **not** OS/power-loss (data still in the OS page cache). If power-loss durability is a requirement, the `FileStream.FlushAsync` upgrade in `EnsureWriterInitializedAsync` is needed — design and review that separately; do not silently imply full crash-durability from the minimal change.
- **Backward compat:** the optional/defaulted constructor param keeps all ~9 construction sites and DI compiling unchanged; the behavior change is **opt-in only**, so it is **independently shippable** with zero default-path risk.
- **Test care:** the durability test must bypass `DisposeAsync` (which already flushes), or it proves nothing — see Fix 1 test plan.

**Effort M, Risk Medium** (correctness easy; the perf gating, the durability-ceiling decision, and the crash-simulation test technique need explicit sign-off, plus the new `StorageOptions.DedupFlushOnWrite` option). Ship the flag + minimal flush first; treat `FileStream.FlushAsync` as a follow-up.

**Do Fix 3 (EDGAR enrichment) last, as a separate work item.**
Lowest severity (data shallowness, no correctness/loss), and **smaller than first scoped** — exchange and state-of-incorporation are already mapped, so the genuine gap is `formerNames`/`addresses` + asset-class granularity. It is partly **data-constrained** (CUSIP/ISIN/FIGI are not in EDGAR at all). It is **not a quick win** and should not block Fixes 1–2. **Effort M (formerNames/addresses + asset-class) , Risk Medium** (additive output changes can surprise downstream consumers of the new asset-class values; scope strictly to what EDGAR actually exposes).

**Independence:** all three fixes are non-overlapping (different files, no shared code) and can be developed and merged in parallel PRs. The recommended *order of attention* is **Fix 2 → Fix 1 → Fix 3**, prioritizing the safe high-value quick win, then the gated correctness/perf-sensitive change, then the deferrable enrichment.

---

**Validation command (Fixes 1 & 2):**
`dotnet test tests/Meridian.Tests -c Release --filter "FullyQualifiedName~Pipeline" /p:EnableWindowsTargeting=true`
**Fix 3:** `dotnet test tests/Meridian.Tests -c Release --filter "FullyQualifiedName~Edgar" /p:EnableWindowsTargeting=true`

Relevant absolute paths:
- `/home/user/Meridian-main/src/Meridian.Application/Pipeline/PersistentDedupLedger.cs`
- `/home/user/Meridian-main/src/Meridian.Application/Pipeline/IngestionJobService.cs`
- `/home/user/Meridian-main/src/Meridian.Application/Composition/Features/PipelineFeatureRegistration.cs`
- `/home/user/Meridian-main/src/Meridian.Storage/Archival/AtomicFileWriter.cs`
- `/home/user/Meridian-main/src/Meridian.Storage/Backfill/BackfillStatusStore.cs`
- `/home/user/Meridian-main/src/Meridian.Infrastructure/Adapters/Edgar/EdgarSecurityMasterIngestProvider.cs`
- `/home/user/Meridian-main/src/Meridian.Infrastructure/Adapters/Edgar/EdgarReferenceDataProvider.cs`
- `/home/user/Meridian-main/src/Meridian.Contracts/SecurityMaster/SecurityIdentifiers.cs`
- `/home/user/Meridian-main/tests/Meridian.Tests/Application/Pipeline/PersistentDedupLedgerTests.cs`
- `/home/user/Meridian-main/tests/Meridian.Tests/Application/Pipeline/IngestionJobServiceTests.cs`
