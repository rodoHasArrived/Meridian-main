# Cleanup Opportunities Audit (Deep Scan, WPF-Only Direction)

_Date: 2026-02-10_
_Last Updated: 2026-02-20_
_Status: UWP removal complete — residual configuration and documentation references remain_

This audit provides a **thorough, file-specific cleanup plan** with WPF as the only supported desktop platform.
The focus is to make each cleanup item directly actionable by naming **where** to change and **how** to validate.

**Update (2026-02-20):** Major milestone — UWP project has been fully deleted from the source tree and removed from the solution. Service migration is complete. This update reflects the new post-UWP state and shifts focus to residual reference cleanup, structural refactors, and documentation alignment.

**Update (2026-02-12):** This document has been updated to reflect completed work including UiServer endpoint extraction, H1-H3 repository hygiene cleanup, and root documentation relocation. See completion status markers throughout.

## Quick Status Summary

| Category | Status | Details |
|----------|--------|---------|
| **Repository Hygiene (H1-H3)** | ✅ Complete | Artifacts removed, .gitignore enhanced, root docs normalized |
| **UiServer Endpoint Extraction (S1)** | ✅ Complete | 3030 LOC → 260 LOC, 32 endpoint modules |
| **UWP Platform Removal (P1-P4)** | ✅ Complete | Project deleted, solution cleaned, tests removed |
| **UWP Service Migration (P6)** | ✅ Complete | UWP services removed; WPF is sole desktop client |
| **Residual UWP References (P5-R)** | ✅ Complete | All 9 items (R1-R9) cleaned up |
| **HtmlTemplates Split (S2)** | ✅ Partially Complete | Split into 3 partial class files (2,533 LOC total) |
| **Storage Services Split (S3)** | ✅ Complete | PortableDataPackager split into 5 files; AnalysisExportService split into 6 files |
| **Architecture Debt (A1-A2)** | ✅ Complete | DataGapRepair DI resolved; SubscriptionManager renamed to SubscriptionOrchestrator |

**Key Achievements Since Last Update:**
- UWP project (`src/Meridian.Uwp/`) fully deleted
- UWP removed from `Meridian.sln`
- UWP integration tests removed
- UWP workflow references removed from `.github/workflows/desktop-builds.yml`
- `SubscriptionManager` renamed to `SubscriptionOrchestrator` in Application layer
- DataGapRepair DI TODO resolved (no remaining TODO markers)
- HtmlTemplates split into `HtmlTemplateGenerator.cs` (670 LOC) + `.Scripts.cs` (997 LOC) + `.Styles.cs` (866 LOC)
- PortableDataPackager split into 5 partial class files (2,098 LOC total)
- AnalysisExportService split into 6 partial class files (1,683 LOC total)

**Remaining Work:**
- ~~Consider archiving `docs/development/uwp-to-wpf-migration.md`~~ Done - moved to `../../docs/`
- Consider moving `HtmlTemplateGenerator` static CSS/JS to versioned `wwwroot/` files
- Regenerate repository structure docs and diagrams to reflect post-UWP state

## 1) Scan methods and evidence sources

### Commands used

1. `rg --files` for broad inventory.
2. `rg -n "TODO|FIXME|HACK" src --glob '!**/bin/**' --glob '!**/obj/**'` for technical-debt markers.
3. `rg -n "Meridian\.Uwp|Uwp|UWP" ...` across solution, workflows, tests, docs for platform coupling.
4. `git ls-files | xargs -I{} du -k "{}" | sort -nr` to spot large tracked artifacts.
5. Python line-count scan to identify monolith files.
6. Python similarity scan for UWP/WPF same-name services.
7. (2026-02-20) Full re-audit of repository state post-UWP deletion.

### What this enables

- Verification that UWP removal is structurally complete.
- Identification of residual references that create confusion or broken documentation.
- Cleaner review scope for remaining large classes/files.

---

## 2) Immediate repository hygiene — ✅ COMPLETED

**Status:** All hygiene tasks (H1, H2, H3) completed. See `docs/audits/CLEANUP_SUMMARY.md` for detailed completion report.

### H1. Remove accidental artifact file — ✅ DONE

- **File:** `...`
- **Issue:** Tracked scratch output (`Line 319: 65`) appears non-source and non-doc.
- **Status:** ✅ Removed in commit 77179ec
- **Verification:** File no longer tracked; `.gitignore` patterns added to prevent future scratch artifacts.

### H2. Untrack local build logs — ✅ DONE

- **File:** `build-output.log`
- **Issue:** Machine-generated log in version control increases history churn.
- **Status:** ✅ Removed in commit 77179ec (93,549 bytes deleted)
- **Improvements:** Comprehensive `.gitignore` patterns added with inline documentation for build logs, temporary files, and artifacts.
- **Verification:** No log files tracked; `*.log` pattern prevents future accidental commits.

### H3. Normalize root documentation — ✅ DONE

- **Files:** `PR_SUMMARY.md`, `UI_IMPROVEMENTS_SUMMARY.md`, `VISUAL_CODE_EXAMPLES.md`
- **Issue:** Root-level narrative docs dilute discoverability of canonical docs structure.
- **Status:** ✅ All files moved to `../../docs/` with date prefixes (2026-02_*)
- **Verification:** Root directory contains only intentional top-level docs (`README.md`, `LICENSE`, `CLAUDE.md`, etc.).

---

## 3) WPF-only platform migration — ✅ STRUCTURALLY COMPLETE

UWP has been fully removed from the codebase. The project directory, solution references, integration tests, and workflow jobs have all been cleaned. What remains are **residual documentation and configuration references** that point to the now-deleted UWP project.

### P1. Solution and project graph cleanup — ✅ DONE

- **Status:** ✅ **COMPLETED**
- **Evidence:** `grep -i "uwp" Meridian.sln` returns no matches.
- **Verification:** `dotnet sln Meridian.sln list` shows no UWP project. Solution builds cleanly.

### P2. CI/CD workflow cleanup — ✅ MOSTLY DONE

- **Status:** ✅ **COMPLETED** for workflow files
- **Evidence:** `grep -rn "Meridian\.Uwp\|UWP_PROJECT\|uwp-generate-assets" .github/workflows` returns no matches.
- **Note:** `desktop-builds.yml` correctly includes a clarification comment: `# NOTE: UWP/WinUI 3 application has been removed. WPF is the sole desktop client.`

### P3. Source tree cleanup — ✅ DONE

- **Status:** ✅ **COMPLETED**
- **Evidence:** `src/Meridian.Uwp/` directory no longer exists.
- **Verification:** `find src -type d -name "*Uwp*"` returns no results.

### P4. Tests and coverage cleanup — ✅ DONE

- **Status:** ✅ **COMPLETED**
- **Evidence:** `tests/Meridian.Tests/Integration/UwpCoreIntegrationTests.cs` no longer exists.
- **Verification:** No UWP-specific test files remain in the test suite.

### P5-R. Residual UWP reference cleanup — ✅ COMPLETE

All stale UWP references in configuration files, documentation, and code comments have been cleaned up.

#### R1. `.github/labeler.yml` — ✅ DONE

- **Fix:** Replaced `src/Meridian.Uwp/**/*` with `src/Meridian.Wpf/**/*` in the `ui` label.

#### R2. `.github/pull_request_template_desktop.md` — ✅ DONE

- **Fix:** Removed `src/Meridian.Uwp/**` path and UWP build checklist items (`make build-uwp`, `make uwp-xaml-diagnose`).

#### R3. `.github/QUICKSTART.md` — ✅ DONE

- **Fix:** Removed `desktop-app.yml | UWP app builds` workflow entry.

#### R4. `docs/operations/operator-runbook.md` — ✅ DONE

- **Fix:** Replaced "UWP Desktop Application" section with "WPF Desktop Application" pointing to correct project path.

#### R5. `docs/status/production-status.md` — ✅ DONE

- **Fix:** Removed UWP Desktop App row; updated WPF description to sole desktop client. Updated diagram heading to WPF-only.

#### R6. `docs/status/IMPROVEMENTS.md` C7 and F1 items — ✅ DONE

- **Fix:** Updated C7 from OPEN to COMPLETED (UWP removed, no duplicate services). Updated F1 from PARTIAL to COMPLETED (UWP navigation no longer applies). Updated all summary tables and progress ratios.

#### R7. `docs/ai/copilot/instructions.md` — ✅ DONE

- **Fix:** Removed `Meridian.Uwp/` from directory tree; updated desktop apps description to WPF-only.

#### R8. `docs/operations/msix-packaging.md` — ✅ DONE

- **Fix:** Updated `Meridian.Uwp.csproj` reference to `Meridian.Wpf.csproj`.

#### R9. WPF code comments referencing UWP — ✅ DONE

- **Fix:** Updated `AppConfig.cs` header from "UWP-Specific" to "WPF-Specific"; updated `RetentionAssuranceService.cs` doc comment from "UWP-specific" to "WPF-specific".

### P6. Service migration — ✅ COMPLETE

- **Status:** ✅ **COMPLETED** — UWP project deleted; all services now exist only in WPF and/or shared projects.
- **Evidence:** `find src/Meridian.Uwp -name "*.cs" 2>/dev/null` returns no results (directory does not exist).
- **Current state:** 51 service files in `src/Meridian.Wpf/Services/`. No duplicate UWP counterparts remain.
- **Shared service layer:** `src/Meridian.Ui.Services/Services/` contains platform-agnostic base classes and implementations.

---

## 4) High-impact structural refactors (non-platform) — ✅ MOSTLY COMPLETE

### S1. Decompose `UiServer` endpoint monolith — ✅ DONE

- **File:** `src/Meridian/UiServer.cs` (was ~3030 LOC, now **260 LOC** — 91.4% reduction).
- **Status:** ✅ **COMPLETED** in commit 540f5bd
- **Achievement:** All endpoint logic extracted to 32 dedicated endpoint modules in `src/Meridian.Ui.Shared/Endpoints/`:
  - `AdminEndpoints`, `AlignmentEndpoints`, `AnalyticsEndpoints`, `ApiKeyMiddleware`
  - `BackfillEndpoints`, `BackfillScheduleEndpoints`, `ConfigEndpoints`, `CronEndpoints`
  - `DiagnosticsEndpoints`, `ExportEndpoints`, `FailoverEndpoints`, `HealthEndpoints`
  - `IBEndpoints`, `IndexEndpoints`, `LeanEndpoints`, `LiveDataEndpoints`
  - `MaintenanceScheduleEndpoints`, `MessagingEndpoints`, `PathValidation`
  - `ProviderEndpoints`, `ProviderExtendedEndpoints`, `QualityDropsEndpoints`
  - `ReplayEndpoints`, `SamplingEndpoints`, `StatusEndpoints`, `StorageEndpoints`
  - `StorageQualityEndpoints`, `StubEndpoints`, `SubscriptionEndpoints`
  - `SymbolEndpoints`, `SymbolMappingEndpoints`, `UiEndpoints`
- **Validation:** Endpoint behavior parity maintained; all endpoints functional via modular pattern.

### S2. Break apart HTML template monolith — ✅ PARTIALLY DONE

- **Original file:** `HtmlTemplates.cs` (2511 LOC monolith)
- **Current state:** Renamed and split into 3 partial class files:
  - `src/Meridian.Ui.Shared/HtmlTemplateGenerator.cs` — **670 LOC** (core template logic)
  - `src/Meridian.Ui.Shared/HtmlTemplateGenerator.Scripts.cs` — **997 LOC** (JavaScript generation)
  - `src/Meridian.Ui.Shared/HtmlTemplateGenerator.Styles.cs` — **866 LOC** (CSS generation)
  - **Total:** 2,533 LOC across 3 files
- **Status:** ✅ **SPLIT COMPLETED** — monolith broken into logical partitions
- **Remaining opportunity:** Static CSS/JS could still be moved to versioned files under `wwwroot/` for better cacheability and separation of concerns, but the file is no longer a single monolith.
- **Validation:** Page renders unchanged; template generation functionally equivalent.

### S3. Split storage workflow mega-services — ✅ DONE

- **PortableDataPackager** — split into 5 partial class files (2,098 LOC total):
  - `PortableDataPackager.cs` — 411 LOC (core orchestration)
  - `PortableDataPackager.Creation.cs` — 602 LOC (package creation)
  - `PortableDataPackager.Scripts.cs` — 273 LOC (script generation)
  - `PortableDataPackager.Scripts.Import.cs` — 395 LOC (import scripts)
  - `PortableDataPackager.Scripts.Sql.cs` — 173 LOC (SQL scripts)
  - `PortableDataPackager.Validation.cs` — 244 LOC (validation logic)
- **AnalysisExportService** — split into 6 partial class files (1,683 LOC total):
  - `AnalysisExportService.cs` — 240 LOC (core)
  - `AnalysisExportService.Formats.cs` — 274 LOC (format routing)
  - `AnalysisExportService.Formats.Arrow.cs` — 234 LOC (Arrow export)
  - `AnalysisExportService.Formats.Parquet.cs` — 234 LOC (Parquet export)
  - `AnalysisExportService.Formats.Xlsx.cs` — 354 LOC (Excel export)
  - `AnalysisExportService.IO.cs` — 347 LOC (I/O operations)
- **StorageCatalogService** — 1,148 LOC (single file, but clear single responsibility)
- **Status:** ✅ **COMPLETED** — all mega-services properly decomposed
- **Validation:** Existing tests continue to pass.

---

## 5) Architecture debt cleanup — ✅ COMPLETED

### A1. Resolve DI boundary TODO in gap repair — ✅ DONE

- **File:** `src/Meridian.Infrastructure/Providers/Historical/GapAnalysis/DataGapRepair.cs`
- **Previous issue:** `TODO: Implement via dependency injection - Infrastructure cannot reference Storage`
- **Status:** ✅ **COMPLETED** — no TODO markers remain in the file.
- **Verification:** `grep -rn "TODO.*dependency injection\|TODO.*Infrastructure cannot reference" src/Meridian.Infrastructure/Providers/Historical/` returns no matches.

### A2. Clarify `SubscriptionManager` role boundaries — ✅ DONE

- **Previous issue:** Same class name (`SubscriptionManager`) in both Application and Infrastructure layers.
- **Status:** ✅ **COMPLETED** — Application layer class renamed to `SubscriptionOrchestrator`.
- **Current state:**
  - `src/Meridian.Application/Subscriptions/SubscriptionOrchestrator.cs` — high-level orchestration
  - `src/Meridian.Infrastructure/Shared/SubscriptionManager.cs` — low-level subscription lifecycle
- **Verification:** No naming collision between layers. Each class name reflects its architectural role.

---

## 6) Generated docs and diagram churn control

### G1. Keep generated artifacts but isolate review noise

- **Likely noisy areas:** `docs/generated/*`, `docs/diagrams/*`, `docs/uml/*`.
- **Action:**
  1. Keep generation deterministic.
  2. Prefer dedicated docs-generation PRs.
  3. Use PR labels or commit conventions separating code vs generated refresh.
- **Validation:** functional PRs stay focused; generated asset updates are intentional and easy to review.

---

## 7) Execution roadmap (recommended PR sequence) — UPDATED

**Progress as of 2026-02-20:**

1. **PR-1 Hygiene:** ✅ **COMPLETED** — removed scratch artifacts, untracked `build-output.log`, relocated root summary docs to `../../docs/`. See `docs/audits/CLEANUP_SUMMARY.md`.

2. **PR-2 Platform references (solution/CI):** ✅ **COMPLETED** — UWP removed from solution file and workflow jobs.

3. **PR-3 Service migration:** ✅ **COMPLETED** — UWP project fully deleted; WPF is sole desktop client. All services consolidated in WPF and shared projects.

4. **PR-4 UWP deletion:** ✅ **COMPLETED** — `src/Meridian.Uwp/` deleted. UWP integration tests removed.

5. **PR-5 Structural refactors:** ✅ **COMPLETED**
   - ✅ UiServer endpoint extraction (91.4% reduction, 260 LOC remaining)
   - ✅ HtmlTemplates split into 3 partial class files (670 + 997 + 866 LOC)
   - ✅ PortableDataPackager split into 5 partial class files
   - ✅ AnalysisExportService split into 6 partial class files

6. **PR-6 Architecture debt:** ✅ **COMPLETED**
   - ✅ A1: DataGapRepair DI TODO resolved (no remaining markers)
   - ✅ A2: SubscriptionManager renamed to SubscriptionOrchestrator (naming collision resolved)

7. **PR-7 Residual reference cleanup:** ✅ **COMPLETED** — All 9 items (R1-R9) cleaned up

8. **PR-8 Generated docs refresh:** ⚠️ **NOT STARTED** — regenerate inventories/diagrams after structural changes.

## 8) Definition of done — UPDATED

**Current Status (2026-02-20):**

### ✅ Completed:
- ✅ Root directory is free of accidental artifacts and local logs
- ✅ Root documentation normalized (historical docs in `../../docs/`)
- ✅ UiServer endpoint extraction complete (91.4% reduction to 260 LOC)
- ✅ Repository hygiene (H1, H2, H3) fully addressed
- ✅ UWP project deleted from source tree
- ✅ UWP removed from solution file
- ✅ UWP workflow jobs removed from CI
- ✅ UWP integration tests removed
- ✅ UWP/WPF service deduplication complete (UWP project deleted)
- ✅ HtmlTemplates split into 3 partial class files
- ✅ Storage mega-services decomposed (PortableDataPackager: 5 files, AnalysisExportService: 6 files)
- ✅ DataGapRepair DI boundary issue resolved
- ✅ SubscriptionManager naming collision resolved (renamed to SubscriptionOrchestrator)

### ⚠️ Remaining:
- ⚠️ Generated docs not yet refreshed to reflect post-UWP repository structure
- ⚠️ HtmlTemplateGenerator still embeds CSS/JS inline (2,533 LOC total across 3 files) — could benefit from moving static assets to `wwwroot/`

### ✅ Recently Completed:
- ✅ All 9 residual UWP reference items (R1-R9) cleaned up
- ✅ `docs/status/IMPROVEMENTS.md` C7 and F1 marked COMPLETED

**Next Steps:**
1. Regenerate repository structure docs and diagrams
2. Consider moving HtmlTemplateGenerator static CSS/JS to versioned `wwwroot/` files
3. Review further simplification opportunities in [`FURTHER_SIMPLIFICATION_OPPORTUNITIES.md`](../audits/FURTHER_SIMPLIFICATION_OPPORTUNITIES.md)
