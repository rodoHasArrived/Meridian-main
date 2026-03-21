# Further Simplification Opportunities

**Date:** 2026-02-20
**Last Updated:** 2026-03-16
**Status:** In progress — several items completed since initial audit
**Context:** Identified during code review simplification work (PRs #1302, #1308)

This document captures simplification opportunities discovered during two rounds of code review and cleanup. Each section includes rationale, affected files, estimated impact, and suggested approach. Items are updated as work is completed.

For already-completed cleanup work, see [`CLEANUP_OPPORTUNITIES.md`](../archived/CLEANUP_OPPORTUNITIES.md) and [`CLEANUP_SUMMARY.md`](../archived/CLEANUP_SUMMARY.md).

## Progress Overview (as of 2026-03-16)

| # | Category | Status |
|---|----------|--------|
| 1 | Thin WPF service wrappers | 🔄 Partially done |
| 2 | Manual double-checked locking → `Lazy<T>` | ✅ Completed |
| 3 | Endpoint boilerplate helpers | 🔄 Partially done |
| 4 | ConfigStore/BackfillCoordinator wrappers | ⏳ Pending |
| 5 | Orphaned ServiceBase abstractions | ⏳ Pending |
| 6 | `Task.Run` wrapping async I/O | ✅ Completed |
| 7 | Remaining bare catch blocks | ✅ Completed |
| 8 | FormatBytes/date format duplication | 🔄 Partially done |
| 9 | Dead code and empty stubs | 🔄 Partially done |
| 10 | Stale UWP references in source | ✅ Completed |
| 11 | Endpoint file organization | ⏳ Pending (scope changed) |
| 12 | Duplicate model definitions | ⏳ Pending |

---

## Table of Contents

- [1. Thin WPF Service Wrappers](#1-thin-wpf-service-wrappers) 🔄
- [2. Manual Double-Checked Locking (Singleton Pattern)](#2-manual-double-checked-locking-singleton-pattern) ✅
- [3. Repetitive Endpoint Boilerplate](#3-repetitive-endpoint-boilerplate) 🔄
- [4. Duplicate ConfigStore and BackfillCoordinator Wrappers](#4-duplicate-configstore-and-backfillcoordinator-wrappers) ⏳
- [5. Orphaned ServiceBase Abstractions](#5-orphaned-servicebase-abstractions) ⏳
- [6. Task.Run Wrapping Async I/O](#6-taskrun-wrapping-async-io) ✅
- [7. Remaining Bare Catch Blocks](#7-remaining-bare-catch-blocks) ✅
- [8. FormatBytes and Date Format Duplication](#8-formatbytes-and-date-format-duplication) 🔄
- [9. Dead Code and Empty Stubs](#9-dead-code-and-empty-stubs) 🔄
- [10. Remaining Stale UWP References in Source](#10-remaining-stale-uwp-references-in-source) ✅
- [11. Endpoint File Organization](#11-endpoint-file-organization) ⏳
- [12. Duplicate Model Definitions Across Layers](#12-duplicate-model-definitions-across-layers) ⏳
- [Summary Matrix](#summary-matrix)

---

## 1. Thin WPF Service Wrappers

**Status:** 🔄 Partially done

**What was already done (PR #1308):** Removed duplicate `BackfillApiService` in WPF (identical to `Ui.Services` version) and empty `AdvancedAnalyticsService` wrapper with zero overrides.

**Update (2026-03-16):** `WpfDataQualityService` and `WpfAnalysisExportService` were never introduced into the codebase. `ExportPresetService` has been reduced to 23 lines and `AdminMaintenanceService` to 17 lines. `StorageService` (65 lines) and `SchemaService` (124 lines) remain as simplification candidates.

**What remains:** A few WPF services still follow the thin-wrapper pattern with zero or minimal business logic.

### Candidates

| Service | File | Lines | Issue |
|---------|------|-------|-------|
| `StorageService` | `src/Meridian.Wpf/Services/StorageService.cs` | ~65 | Thin wrapper over `StorageServiceBase`; one added method is pure API mapping |
| `SchemaService` | `src/Meridian.Wpf/Services/SchemaService.cs` | ~124 | Minimal override of `SchemaServiceBase`; only adds file path resolution |

### Suggested Approach

- For `StorageService`, inject file path configuration into the base class constructor instead of overriding.
- For `SchemaService`, move file path logic to constructor argument and remove the subclass.
- **Estimated removal:** ~150-200 lines across 2 files.
- **Risk:** Low — behavioral parity maintained since wrappers add no logic.

---

## 2. Manual Double-Checked Locking (Singleton Pattern)

**Status:** ✅ Completed

**What was already done (PR #1302):** Replaced manual double-checked locking in `HttpClientFactoryProvider` with `Lazy<T>`.

**Update (2026-03-16):** All 43 identified services across `Ui.Services` and `Wpf` have been converted. Every singleton now uses the idiomatic pattern:

```csharp
private static readonly Lazy<SomeService> _instance = new(() => new SomeService());
public static SomeService Instance => _instance.Value;
```

The `_lock` field and outer null check have been removed from all services, eliminating ~350-430 lines and the associated threading subtleties. No further action required.

---

## 3. Repetitive Endpoint Boilerplate

**Status:** 🔄 Partially done

**What was already done (PR #1308):** Extracted repetitive try-catch in `DataQualityEndpoints` into `HandleSync`/`HandleAsync` helpers and `ParseDateOrToday`, reducing ~300 lines.

**Update (2026-03-16):** `EndpointHelpers.cs` has been created in `Ui.Shared/Endpoints/` (125 lines) with `HandleSync<TService>`, `HandleAsync<TService>`, and `FormatErrorResult` helpers. However, the migration of existing endpoint files to use these helpers is incomplete — the files listed below still use inline try-catch patterns.

### Example Pattern (still repeated across files)

```csharp
if (service is null)
    return Results.Json(new { error = "Service unavailable" }, jsonOptions);
try
{
    var result = await service.DoSomethingAsync(ct);
    return Results.Json(result, jsonOptions);
}
catch (Exception ex)
{
    return Results.Problem($"Failed: {ex.Message}");
}
```

### Affected Files

| File | Location | Lines (current) | Approx. Repeated Blocks |
|------|----------|-----------------|------------------------|
| `StorageQualityEndpoints.cs` | `Ui.Shared/Endpoints/` | 344 | 16 inline try-catch blocks |
| `StorageEndpoints.cs` | `Ui.Shared/Endpoints/` | 694 | 15+ endpoints |
| `ProviderEndpoints.cs` | `Ui.Shared/Endpoints/` | 471 | 12+ endpoints |
| `SymbolEndpoints.cs` | `Ui.Shared/Endpoints/` | 488 | 12+ endpoints |
| `AdminEndpoints.cs` | `Ui.Shared/Endpoints/` | 359 | 8+ endpoints |
| `DiagnosticsEndpoints.cs` | `Ui.Shared/Endpoints/` | 362 | 10+ endpoints |

### Suggested Approach

Apply `EndpointHelpers.HandleSync` / `HandleAsync` to the inline blocks above. The infrastructure is already in place — this is a mechanical migration.

- **Estimated reduction:** ~400-600 lines across 6+ files.
- **Risk:** Low — purely structural; no behavioral change.

---

## 4. Duplicate ConfigStore and BackfillCoordinator Wrappers

**Status:** ⏳ Pending

**What was already done (PR #1302):** Simplified `ConfigStore` wrapper in `Ui.Shared` by removing verbose XML docs. Fixed `BackfillCoordinator.CreateService()` double-calling `CreateProviders()`.

**What remains:** The `Ui.Shared` versions of `ConfigStore` and `BackfillCoordinator` are still thin wrappers over their `Application/Http/` counterparts.

| Wrapper | Location | Lines | Core | Lines |
|---------|----------|-------|------|-------|
| `ConfigStore` | `Ui.Shared/Services/ConfigStore.cs` | ~61 | `Application/Http/ConfigStore.cs` | ~187 |
| `BackfillCoordinator` | `Ui.Shared/Services/BackfillCoordinator.cs` | ~380 | `Application/Http/BackfillCoordinator.cs` | ~308 |

### Suggested Approach

- For `ConfigStore`: The wrapper only provides a web-specific default path. Inject the path resolver via the core class's existing `DefaultPathResolver` static delegate, then remove the wrapper entirely.
- For `BackfillCoordinator`: The wrapper adds preview functionality. Move preview methods into the core class (gated by an optional parameter) to eliminate the wrapper layer.
- **Estimated reduction:** ~250 lines.
- **Risk:** Medium — requires verifying that endpoint registrations correctly resolve the core classes after wrapper removal.

---

## 5. Orphaned ServiceBase Abstractions

**Status:** ⏳ Pending

**Context:** `*ServiceBase` classes in `Ui.Services` were created to share code between WPF and UWP desktop clients. With UWP removed (Phase 6), each base class now has exactly one implementation.

**Update (2026-03-16):** `AdvancedAnalyticsServiceBase` (336 lines) is confirmed orphaned — the WPF subclass was removed in PR #1308 and no replacement was created. The other candidates below remain unchanged.

### Candidates for Merging

| Base Class | Location | Implementation | Status |
|------------|----------|---------------|--------|
| `StorageServiceBase` | `Ui.Services/Services/` | `Wpf/Services/StorageService.cs` | Single impl — merge candidate |
| `AdminMaintenanceServiceBase` | `Ui.Services/Services/` | `Wpf/Services/AdminMaintenanceService.cs` | Single impl — merge candidate |
| `ExportPresetServiceBase` | `Ui.Services/Services/` | `Wpf/Services/ExportPresetService.cs` | Single impl — merge candidate |
| `AdvancedAnalyticsServiceBase` | `Ui.Services/Services/` | None (orphaned — 336 lines) | No implementation — **remove** |
| `SchemaServiceBase` | `Ui.Services/Services/` | `Ui.Services/Services/SchemaService.cs` | Same project — merge |
| `ConnectionServiceBase` | `Ui.Services/Services/` | `Wpf/Services/ConnectionService.cs` | Has platform-specific timer code — evaluate |
| `NavigationServiceBase` | `Ui.Services/Services/` | `Wpf/Services/NavigationService.cs` | Has WPF Frame abstraction — evaluate |
| `ThemeServiceBase` | `Ui.Services/Services/` | `Wpf/Services/ThemeService.cs` | Has WPF ResourceDictionary code — keep separate |

### Suggested Approach

- **Immediate:** Remove `AdvancedAnalyticsServiceBase.cs` (336 lines) — confirmed orphaned with no subclass.
- **Quick wins:** Merge `StorageServiceBase`, `AdminMaintenanceServiceBase`, and `ExportPresetServiceBase` into their single implementations.
- **Evaluate:** `ConnectionServiceBase` and `NavigationServiceBase` contain platform abstractions (timer types, frame navigation) that justify the separation — keep unless a second desktop platform is ruled out permanently.
- **Estimated reduction:** ~500-700 lines (removing/merging 3-4 base classes, plus the orphaned base).
- **Risk:** Medium — requires updating all consumers to reference the concrete class instead of the base.

---

## 6. Task.Run Wrapping Async I/O

**Status:** ✅ Completed

**Project guideline (CLAUDE.md):** "NEVER use `Task.Run` for I/O-bound operations (wastes thread pool)."

**Update (2026-03-16):** All identified instances have been resolved. `WebSocketProviderBase.cs` (which contained the WebSocket receive-loop wrapping) was removed from the codebase as part of a broader resilience refactor. The `EventPipeline`, `StatusHttpServer`, `StatusWriter`, and storage service fire-and-forget cases no longer wrap async I/O in `Task.Run`. No further action required.

---

## 7. Remaining Bare Catch Blocks

**Status:** ✅ Completed

**What was already done (PR #1302):** Narrowed bare catches in `ConfigStore` to specific exception types.

**Update (2026-03-16):** All three remaining files have been updated:

- `Wpf/Views/SettingsPage.xaml.cs` — catch now uses `when (ex is HttpRequestException or TimeoutException or TaskCanceledException)`.
- `Wpf/Views/AnalysisExportWizardPage.xaml.cs` — catch now uses `when (ex is IOException or UnauthorizedAccessException or ArgumentException or DriveNotFoundException)`.
- `Infrastructure/Providers/Streaming/InteractiveBrokers/EnhancedIBConnectionManager.IBApi.cs` — narrowed to typed exception filter.

No further action required.

---

## 8. FormatBytes and Date Format Duplication

**Status:** 🔄 Partially done

### FormatBytes Wrappers

**Update (2026-03-16):** The private `FormatBytes` one-liner wrappers in WPF view code-behind files have been removed. Call sites now invoke `FormatHelpers.FormatBytes()` directly. ✅

### Date Format Strings

**Update (2026-03-16):** `FormatHelpers.IsoDateFormat` constant (`"yyyy-MM-dd"`) has been added to `FormatHelpers.cs`. However, ~32 uses of the literal `"yyyy-MM-dd"` remain in `Ui.Services/Services/`. These should be migrated to use the constant.

```csharp
// In FormatHelpers.cs — already added
public const string IsoDateFormat = "yyyy-MM-dd";
```

**Remaining work:** Replace the ~32 literal occurrences of `"yyyy-MM-dd"` in `Ui.Services/Services/` with `FormatHelpers.IsoDateFormat`.

- **Estimated changes:** ~32 single-line substitutions.
- **Risk:** Very low.

---

## 9. Dead Code and Empty Stubs

**Status:** 🔄 Partially done

**Update (2026-03-16):**

| Item | File | Lines | Status |
|------|------|-------|--------|
| ~~Empty `StubEndpoints`~~ | ~~`Ui.Shared/Endpoints/StubEndpoints.cs`~~ | ~~16~~ | ✅ Removed |
| Dead config stubs | `Wpf/Services/ConfigService.cs` | ~445-468 | ⚠️ Verify — `InitializeAsync` returns `Task.CompletedTask`; confirm no callers rely on out-of-band initialization |
| Orphaned `AdvancedAnalyticsServiceBase` | `Ui.Services/Services/AdvancedAnalyticsServiceBase.cs` | ~336 | ⏳ Pending — no WPF subclass exists after PR #1308 removed `AdvancedAnalyticsService` |

### Remaining work

- Remove `AdvancedAnalyticsServiceBase.cs` (336 lines) — confirmed orphaned (see Section 5).
- Review `ConfigService.cs` generic stubs (`GetConfigAsync<T>()` / `SaveConfigAsync()`) to verify they are unreachable before removal.

- **Estimated removal:** ~340-370 lines.
- **Risk:** Low — dead code removal with no behavioral impact.

---

## 10. Remaining Stale UWP References in Source

**Status:** ✅ Completed

**What was already done:** R1-R9 cleanup (see [`CLEANUP_OPPORTUNITIES.md`](../archived/CLEANUP_OPPORTUNITIES.md)), plus PR #1302 updated 13+ stale UWP references in comments and project metadata.

**Update (2026-03-16):** All identified stale UWP references in source code have been removed. Verified clean across:
`PipelinePolicyConstants.cs`, `Contracts.csproj`, `SharedResiliencePolicies.cs`, `StatusHttpServer.cs`,
`HostAdapters.cs`, `StorageServiceBase.cs`, `ExportPresetServiceBase.cs`, `BackfillApiService.cs`,
`CredentialService.cs`. No further action required.

---

## 11. Endpoint File Organization

**Status:** ⏳ Pending (scope changed)

### Current State

39 endpoint files in `Ui.Shared/Endpoints/` (up from 33 at initial audit — new endpoints added for `LeanEndpoints`, `MessagingEndpoints`, `ReplayEndpoints`, `ProviderExtendedEndpoints`, `SamplingEndpoints`, `AnalyticsEndpoints`). Sizes still range widely: 125 to 694 lines.

`StubEndpoints.cs` (16 lines) has been removed. ✅

The three small consolidation candidates (`QualityDropsEndpoints.cs`, `AlignmentEndpoints.cs`, `IndexEndpoints.cs`) no longer exist as separate files — their content was either merged or the endpoints were replaced by new dedicated files.

### Remaining large files (split candidates)

| File | Lines | Suggestion |
|------|-------|------------|
| `StorageEndpoints.cs` | 694 | Split into stats + catalog endpoints |
| `LeanEndpoints.cs` | 605 | Split into integration + export endpoints |
| `ProviderEndpoints.cs` | 471 | Split into config + status endpoints |
| `SymbolEndpoints.cs` | 488 | Consider splitting if growth continues |

### Suggested Approach

Split the four largest files (each >450 lines) on domain boundaries. This is a low-risk organizational change.

- **Net result:** ~39 → ~43-45 files with more consistent sizing (~200-350 lines each).
- **Risk:** Low — purely organizational.

---

## 12. Duplicate Model Definitions Across Layers

**Status:** ⏳ Pending

Model classes are scattered across multiple projects with potential overlap:

| Category | Location | Files | Overlap Risk |
|----------|----------|-------|--------------|
| Domain models | `Domain/Models/` | 3 | `AggregateBar.cs` still present alongside `Contracts/Domain/Models/AggregateBarPayload.cs` |
| Contract models | `Contracts/Domain/Models/` | 21 | Authoritative layer — others should reference |
| API models | `Contracts/Api/` | 10 | May duplicate UI service models |
| UI service models | `Ui.Services/Services/*Models.cs` | 5 | `StorageModels`, `AdminMaintenanceModels`, etc. |
| WPF models | `Wpf/Models/` | 2 | `StorageDisplayModels` (29 lines) vs `StorageModels` (110 lines) in Ui.Services |
| Storage models | `Storage/Maintenance/ArchiveMaintenanceModels.cs` | 1 | May overlap with UI `AdminMaintenanceModels` |

### Suggested Approach

- Audit `Domain/Models/AggregateBar.cs` vs `Contracts/Domain/Models/AggregateBarPayload.cs` for true duplication.
- Compare `Ui.Services/Services/StorageModels.cs` (110 lines) with `Wpf/Models/StorageDisplayModels.cs` (29 lines) for consolidation.
- Compare `AdminMaintenanceModels.cs` (Ui.Services) with `ArchiveMaintenanceModels.cs` (Storage).
- **Risk:** Medium — model changes affect serialization and API contracts.

---

## Summary Matrix

| # | Category | Est. Lines Removed | Risk | Priority | Status | Depends On |
|---|----------|-------------------|------|----------|--------|------------|
| 1 | Thin WPF service wrappers | ~150-200 | Low | Medium | 🔄 Partial | — |
| 2 | Manual double-checked locking → `Lazy<T>` | ~~350-430~~ | Low | — | ✅ Done | — |
| 3 | Endpoint boilerplate helpers | 400-600 | Low | Medium | 🔄 Partial | — |
| 4 | ConfigStore/BackfillCoordinator wrappers | ~250 | Medium | Low | ⏳ Pending | — |
| 5 | Orphaned ServiceBase abstractions | ~500-700 | Medium | Medium | ⏳ Pending | #1 |
| 6 | `Task.Run` wrapping async I/O | ~~50~~ | Low-Med | — | ✅ Done | — |
| 7 | Remaining bare catch blocks | ~~30~~ | Low | — | ✅ Done | — |
| 8 | FormatBytes/date format duplication | ~32 | Very Low | Low | 🔄 Partial | — |
| 9 | Dead code and empty stubs | ~340-370 | Low | High | 🔄 Partial | — |
| 10 | Stale UWP references in source | ~~20~~ | None | — | ✅ Done | — |
| 11 | Endpoint file organization | ~0 (split) | Low | Low | ⏳ Pending | #3 |
| 12 | Duplicate model definitions | TBD | Medium | Low | ⏳ Pending | Audit needed |

**Completed since initial audit:** Items 2, 6, 7, 10 fully done; Items 1, 3, 8, 9 partially done.

**Remaining estimated removable/simplifiable code:** ~1,600-2,200 lines

### Recommended Execution Order

1. **Immediate (High priority, Low risk):** Item 9 (remove `AdvancedAnalyticsServiceBase`, confirm ConfigService stubs) — dead code removal confirmed safe.
2. **Quick mechanical (Low risk):** Item 8 (migrate ~32 `"yyyy-MM-dd"` literals to `FormatHelpers.IsoDateFormat`).
3. **Structural (Medium priority, Low risk):** Item 3 — apply existing `EndpointHelpers` to the 6 endpoint files still using inline patterns.
4. **Wrapper cleanup (Medium priority, Low risk):** Items 1 and 5 — remove thin WPF wrappers and orphaned base classes together.
5. **Architecture evaluation (Low priority, Medium risk):** Items 4, 11, 12 — wrapper elimination, large-file splits, model consolidation.

---

## Cross-References

- **Completed cleanup:** [`docs/archived/CLEANUP_OPPORTUNITIES.md`](../archived/CLEANUP_OPPORTUNITIES.md)
- **Completed cleanup summary:** [`docs/archived/CLEANUP_SUMMARY.md`](../archived/CLEANUP_SUMMARY.md)
- **Architecture refactor plan:** [`docs/development/refactor-map.md`](../development/refactor-map.md)
- **Improvement tracking:** [`docs/status/IMPROVEMENTS.md`](../status/IMPROVEMENTS.md) (items C1, C2, C3 are related)
- **Project roadmap:** [`docs/status/ROADMAP.md`](../status/ROADMAP.md)
