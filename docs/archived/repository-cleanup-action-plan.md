# Repository Cleanup Action Plan

**Version:** 1.2
**Last Updated:** 2026-02-16
**Status:** Phases 1-6 Complete
**Related:** ROADMAP.md Phase 6, Phase 8

This document provides a detailed, actionable plan for cleaning up the Meridian repository, removing technical debt, and establishing sustainable organization patterns.

---

## Table of Contents

- [Overview](#overview)
- [Cleanup Priorities](#cleanup-priorities)
- [Phase 1: Immediate Wins (Zero Risk)](#phase-1-immediate-wins-zero-risk)
- [Phase 2: Interface Consolidation](#phase-2-interface-consolidation)
- [Phase 3: Service Deduplication](#phase-3-service-deduplication)
- [Phase 4: Large File Decomposition](#phase-4-large-file-decomposition)
- [Phase 5: Documentation Consolidation](#phase-5-documentation-consolidation)
- [Phase 6: Build and CI Optimization](#phase-6-build-and-ci-optimization)
- [Verification Procedures](#verification-procedures)
- [Rollback Plans](#rollback-plans)

---

## Overview

### Current State

| Metric | Current | After Cleanup | Improvement |
|--------|---------|---------------|-------------|
| **Duplicate Code** | ~3,000 lines | ~500 lines | 83% reduction |
| **Unused Files** | ~260 KB | 0 KB | 100% removed |
| **Duplicate Interfaces** | 9 duplicates | 0 remaining | 100% consolidated (9/9) |
| **Files >2,000 LOC** | 4 files | 0 files | 100% decomposed |
| **Documentation Organization** | Scattered | Organized | Navigable |
| **Orphaned Tests** | ~15 files | 0 files | 100% aligned |

### Goals

1. **Reduce maintenance burden** — Eliminate duplicate code requiring parallel updates
2. **Improve navigability** — Clear structure makes code easy to find
3. **Prevent future debt** — Established patterns prevent recurrence
4. **Maintain stability** — All changes must pass existing tests
5. **Document decisions** — Clear rationale for all structural changes

---

## Cleanup Priorities

### Priority Matrix

```
                High Impact
                    │
    ┌───────────────┼───────────────┐
    │               │               │
    │   P1 - DO     │   P2 - PLAN   │
    │   IMMEDIATELY │   & EXECUTE   │
Low │───────────────┼───────────────┤ High
Risk│               │               │ Risk
    │   P3 - WHEN   │   P4 - AVOID  │
    │   CONVENIENT  │   OR DEFER    │
    │               │               │
    └───────────────┼───────────────┘
                    │
                Low Impact
```

### Categorization

| Priority | Risk | Impact | Action | Example |
|----------|------|--------|--------|---------|
| **P1** | Low | High | Do immediately | Delete unused files |
| **P2** | Medium | High | Plan carefully and execute | Interface consolidation |
| **P3** | Low | Low | Do when convenient | Rename variables for clarity |
| **P4** | High | Low | Avoid or defer | Rewrite stable code for style |

---

## Phase 1: Immediate Wins (Zero Risk)

These items have no downstream impact and can be deleted/cleaned immediately.

### 1.1 Delete Unused Files

| File/Folder | Size | References | Action | Command |
|-------------|------|------------|--------|---------|
| `src/Meridian.Infrastructure/Utilities/SymbolNormalizer.cs` | ~80 LOC | 0 | ✅ Already deleted (PR #1028) | — |
| `src/Meridian.Uwp/Examples/` | ~260 KB | 0 | ✅ Deleted (UWP project removed) | — |
| `build-output.log` (if tracked) | Various | 0 | Remove from git | `git rm build-output.log` |
| Orphaned test files | Various | 0 | Audit and delete | See Section 1.2 |

**Verification:**
```bash
# Ensure no references exist
grep -r "SymbolNormalizer" src/ tests/
grep -r "Examples/" src/ tests/
git grep "build-output.log"

# Build must still succeed
dotnet build -c Release

# All tests must pass
dotnet test
```

### 1.2 Audit Orphaned Test Files

**Procedure:**

1. List all test files:
   ```bash
   find tests/ -name "*.cs" -type f | sort > test-files.txt
   ```

2. For each test file, verify corresponding source file exists:
   ```bash
   # Example: ConfigServiceTests.cs should correspond to ConfigService.cs
   find src/ -name "ConfigService.cs"
   ```

3. If source file doesn't exist, check if test is:
   - **Integration test** — May not have direct source file (keep)
   - **Orphaned test** — Source was deleted but test remains (delete)

4. Delete confirmed orphaned tests:
   ```bash
   git rm tests/path/to/OrphanedTests.cs
   ```

### 1.3 Clean Build Artifacts

**Check `.gitignore` covers:**

```gitignore
# Build results
[Dd]ebug/
[Rr]elease/
x64/
x86/
[Bb]in/
[Oo]bj/
[Ll]og/

# Visual Studio
.vs/
*.user
*.suo
*.userprefs

# Build logs
*.log
build-output.log

# Node
node_modules/
npm-debug.log

# Python
__pycache__/
.venv/
*.pyc
```

**Scan for tracked artifacts:**
```bash
git ls-files | grep -E "(bin/|obj/|\.log$|node_modules|__pycache__)"
```

**Remove if found:**
```bash
git rm --cached [file]
git commit -m "chore: Remove tracked build artifacts"
```

---

## Phase 2: Interface Consolidation

Consolidate duplicate interface definitions into canonical locations.

### 2.1 Mapping: Duplicate → Canonical

| Interface | Canonical Location | Duplicates to Delete |
|-----------|-------------------|---------------------|
| `IConfigService` | `Ui.Services/Contracts/` | ✅ Consolidated (UWP removed) |
| `IThemeService` | `Ui.Services/Contracts/` | ✅ Already consolidated (PR #1028) |
| `INotificationService` | `Ui.Services/Contracts/` | ✅ Consolidated (UWP removed) |
| `ILoggingService` | `Ui.Services/Contracts/` | ✅ Already consolidated (PR #1028) |
| `IMessagingService` | `Ui.Services/Contracts/` | ✅ Already consolidated (PR #1028) |
| `IKeyboardShortcutService` | `Ui.Services/Contracts/` | ✅ Consolidated (UWP removed) |
| `IBackgroundTaskSchedulerService` | `Ui.Services/Contracts/` | ✅ Already consolidated (PR #1028) |
| `IPendingOperationsQueueService` | `Ui.Services/Contracts/` | ✅ Already consolidated (PR #1028) |
| `IOfflineTrackingPersistenceService` | `Ui.Services/Contracts/` | ✅ Already consolidated (PR #1028) |

**Status:** 9 of 9 completed. UWP project has been fully removed from the codebase.

### 2.2 Procedure for Each Interface

**Example: `IConfigService`**

#### Step 1: Verify Canonical Interface

```bash
cat src/Meridian.Ui.Services/Contracts/IConfigService.cs
```

Ensure it has the most complete method signatures (127 lines in canonical vs 15-line stubs).

#### Step 2: Find All Implementations

```bash
grep -r "IConfigService" src/ --include="*.cs" | grep "class.*:"
```

Expected results:
- `Ui.Services/Services/ConfigService.cs` — Implementation
- `Wpf/Services/ConfigService.cs` — WPF implementation
- `Uwp/Services/ConfigService.cs` — UWP implementation

#### Step 3: Update `using` Directives

In WPF and UWP implementations, update:

```csharp
// OLD
using Meridian.Wpf.Services;

// NEW
using Meridian.Ui.Services.Contracts;
```

#### Step 4: Delete Duplicate Interface Files

```bash
git rm src/Meridian.Wpf/Services/IConfigService.cs
git rm src/Meridian.Uwp/Contracts/IConfigService.cs
```

#### Step 5: Build and Test

```bash
# Full solution build
dotnet build -c Release

# Run affected tests
dotnet test tests/Meridian.Wpf.Tests/ --filter "ConfigService"
dotnet test tests/Meridian.Ui.Tests/ --filter "ConfigService"
```

#### Step 6: Commit

```bash
git commit -m "refactor: Consolidate IConfigService to canonical location

- Delete duplicate interface definitions from Wpf and Uwp
- Update using directives to reference Ui.Services.Contracts
- All tests pass, no functional changes

Ref: Phase 2 Interface Consolidation (repository-cleanup-action-plan.md)"
```

### 2.3 Remaining Interfaces

Repeat procedure for:
- [ ] `IConfigService`
- [ ] `INotificationService`
- [ ] `IKeyboardShortcutService`

**Estimated effort:** 1 hour per interface × 3 = 3 hours total

---

## Phase 3: Service Deduplication

Extract shared logic from WPF/UWP service implementations into `Ui.Services`.

### 3.1 Service Deduplication Strategy

| Service Category | Approach | Effort | Example |
|------------------|----------|--------|---------|
| **Identical services** (<5% diff) | Move to `Ui.Services` directly | Low | `FormValidationService` ✅ Done |
| **Singleton pattern diff** | Extract base class with singleton variant | Medium | `ThemeService`, `ConfigService` |
| **Minor platform diffs** | Strategy pattern with platform adapter | Medium | `NavigationService`, `LoggingService` |
| **Complex services** | Extract orchestration, keep platform specifics | High | `AdminMaintenanceService` |

### 3.2 Example: Extract Shared Service

**Target:** `FormValidationService` (already completed in PR #1028 — use as template)

#### Before (Duplicated):
```
Wpf/Services/FormValidationService.cs      (~200 LOC)
Uwp/Services/FormValidationService.cs      (~200 LOC)
```

#### After (Consolidated):
```
Ui.Services/Services/FormValidationService.cs  (~200 LOC, canonical)
Wpf/Services/ — References shared service
Uwp/Services/ — References shared service
```

#### Procedure:

1. **Compare implementations:**
   ```bash
   diff src/Meridian.Wpf/Services/FormValidationService.cs \
        src/Meridian.Uwp/Services/FormValidationService.cs
   ```

2. **If <5% difference:**
   - Move most complete version to `Ui.Services/Services/`
   - Delete both WPF and UWP versions
   - Update references in both projects

3. **If 5-20% difference:**
   - Extract shared logic to base class in `Ui.Services`
   - Keep platform-specific overrides in WPF/UWP

4. **If >20% difference:**
   - Extract common orchestration to shared service
   - Use dependency injection for platform-specific adapters

### 3.3 Candidate Services for Phase 3

| Service | WPF LOC | UWP LOC | Difference | Priority | Strategy |
|---------|---------|---------|------------|----------|----------|
| `BrushRegistry` | 150 | 150 | <5% | P1 | Direct move |
| `ExportPresetService` | 200 | 200 | <5% | P1 | Direct move |
| `InfoBarService` | 120 | 120 | <5% | P1 | Direct move |
| `TooltipService` | 80 | 80 | <5% | P1 | Direct move |
| `ThemeService` | 180 | 180 | ~10% (singleton) | P2 | Base class |
| `ConfigService` | 350 | 350 | ~10% (singleton) | P2 | Base class |
| `NavigationService` | 250 | 250 | ~15% (platform nav) | P2 | Strategy |
| `LoggingService` | 180 | 180 | ~12% (log targets) | P2 | Strategy |

**Estimated effort:**
- P1 (4 services × 1 hour) = 4 hours
- P2 (4 services × 3 hours) = 12 hours
- **Total:** ~16 hours

---

## Phase 4: Large File Decomposition

Break apart files >2,000 LOC into focused, maintainable modules.

### 4.1 Target Files

| File | Current LOC | Target | Strategy | Status |
|------|-------------|--------|----------|--------|
| `UiServer.cs` | ~~3,030~~ → **191** | <500 per file | Extract endpoints to Endpoints/ folder | ✅ **COMPLETED** (2026-02-12) |
| `HtmlTemplateGenerator.cs` (Ui.Shared) | 2,536 (3 partials) | <500 per file | Already decomposed: main (671), Styles (867), Scripts (998) | ✅ **Already well-structured** — no further action needed |
| `PortableDataPackager.cs` | ~~2,042~~ → **5 partials** | <400 per file | Split Scripts.cs into Sql.cs + Import.cs | ✅ **COMPLETED** (2026-02-16) |
| `AnalysisExportService.cs` | ~~1,657~~ → **6 partials** | <400 per file | Split Formats.cs into Parquet/Xlsx/Arrow | ✅ **COMPLETED** (2026-02-16) |

**PortableDataPackager.cs Achievement**: Split `Scripts.cs` (822 LOC) into 3 focused files: `Scripts.cs` (docs/loaders, ~273 LOC), `Scripts.Sql.cs` (PostgreSQL/ClickHouse/DuckDB, ~173 LOC), `Scripts.Import.cs` (Python/R/Spark, ~230 LOC).

**AnalysisExportService.cs Achievement**: Split `Formats.cs` (1,070 LOC) into 4 focused files: `Formats.cs` (CSV/JSONL/Lean/SQL, ~275 LOC), `Formats.Parquet.cs` (~234 LOC), `Formats.Xlsx.cs` (~290 LOC), `Formats.Arrow.cs` (~234 LOC).

**UiServer.cs Achievement**: Reduced from 3,030 to 191 lines (93.7% reduction, -2,839 lines) by delegating to 30+ extracted endpoint modules in `Ui.Shared/Endpoints/`. Removed 5 legacy Configure*Routes() methods and all inline endpoint definitions.

### 4.2 Example: Decompose `UiServer.cs`

#### Current Structure (3,030 LOC):
```csharp
public class UiServer
{
    // Server configuration (100 LOC)
    public void Configure() { }
    
    // Health endpoints (200 LOC)
    private void MapHealthEndpoints() { }
    
    // Config endpoints (300 LOC)
    private void MapConfigEndpoints() { }
    
    // Storage endpoints (400 LOC)
    private void MapStorageEndpoints() { }
    
    // ... 10+ more endpoint groups
    
    // HTML rendering (500 LOC)
    private string RenderDashboard() { }
    
    // Authentication (200 LOC)
    private bool ValidateApiKey() { }
}
```

#### Target Structure (<500 LOC per file):

```
Application/Http/
├── UiServer.cs                           (~200 LOC - config only)
├── Endpoints/
│   ├── HealthEndpoints.cs                (~150 LOC)
│   ├── ConfigEndpoints.cs                (~300 LOC)
│   ├── StorageEndpoints.cs               (~400 LOC)
│   ├── BackfillEndpoints.cs              (~250 LOC)
│   └── [other endpoint groups]
├── Rendering/
│   └── HtmlRenderer.cs                   (~300 LOC)
└── Auth/
    └── ApiKeyMiddleware.cs               (~150 LOC)
```

#### Procedure:

1. **Create new endpoint files:**
   ```bash
   mkdir -p src/Meridian.Application/Http/Endpoints
   touch src/Meridian.Application/Http/Endpoints/HealthEndpoints.cs
   ```

2. **Extract endpoint mapping methods:**
   ```csharp
   // In HealthEndpoints.cs
   public static class HealthEndpoints
   {
       public static void MapHealthEndpoints(this WebApplication app)
       {
           // Move MapHealthEndpoints() logic here
       }
   }
   ```

3. **Update UiServer.cs to use new endpoints:**
   ```csharp
   public class UiServer
   {
       public void Configure(WebApplication app)
       {
           app.MapHealthEndpoints();
           app.MapConfigEndpoints();
           // ...
       }
   }
   ```

4. **Build and test:**
   ```bash
   dotnet build src/Meridian.Application/
   dotnet test tests/Meridian.Tests/Integration/EndpointTests/
   ```

5. **Commit incrementally:**
   ```bash
   git add src/Meridian.Application/Http/Endpoints/HealthEndpoints.cs
   git commit -m "refactor: Extract health endpoints from UiServer

   - Create dedicated HealthEndpoints class
   - Move MapHealthEndpoints logic
   - Update UiServer to use extracted endpoints
   - No functional changes, all tests pass"
   ```

**Repeat for each endpoint group.**

### 4.3 Estimated Effort

| File | Extraction Tasks | Effort per Task | Total |
|------|------------------|-----------------|-------|
| `UiServer.cs` | 12 endpoint groups | 30 min | 6 hours |
| `HtmlTemplates.cs` | 8 rendering methods | 45 min | 6 hours |
| `PortableDataPackager.cs` | 3 components | 2 hours | 6 hours |
| `AnalysisExportService.cs` | 4 format writers | 1.5 hours | 6 hours |
| **Total** | — | — | **24 hours** |

---

## Phase 5: Documentation Consolidation

Organize scattered documentation into clear, navigable structure.

### 5.1 Create Master Documentation Index

**File:** `docs/README.md`

```markdown
# Meridian Documentation

## Quick Links
- [Getting Started](../getting-started/README.md)
- [Architecture Overview](../architecture/overview.md)
- [API Reference](../reference/api-reference.md)
- [Roadmap](../status/ROADMAP.md)

## For Developers
- [Repository Organization Guide](./repository-organization-guide.md)
- [Provider Implementation Guide](./provider-implementation.md)
- [Testing Guide](development/testing-guide.md)
- [Architecture Decision Records](adr/)

## For Operators
- [Deployment Guide](operations/deployment-guide.md)
- [Operator Runbook](operations/operator-runbook.md)
- [Monitoring Setup](operations/monitoring-setup.md)

## For Users
- [Setup Guides](providers/)
- [User Guide](getting-started/user-guide.md)
- [FAQ](HELP.md)

## Status & Planning
- [Project Roadmap](status/ROADMAP.md)
- [TODO Tracking](status/TODO.md)
- [Changelog](status/CHANGELOG.md)
- [Production Status](status/production-status.md)

## Archives
- [Historical Documentation](archived/INDEX.md)
- [Completed Audits](audits/)
```

**Effort:** 2 hours

### 5.2 Create Archived Documentation Index

**File:** `docs/archived/INDEX.md`

```markdown
# Archived Documentation

This folder contains historical documentation that has been superseded by newer guides or is no longer actively maintained.

## Why Archive?
- Implementation completed
- Approach changed
- Information incorporated into other docs
- Historical reference only

## Archived Documents

| Document | Date Archived | Reason | Current Reference |
|----------|---------------|--------|-------------------|
| `DUPLICATE_CODE_ANALYSIS.md` | 2026-02-12 | Analysis complete, most items resolved | See ROADMAP.md Phase 6 |
| `REPOSITORY_REORGANIZATION_PLAN.md` | 2026-02-12 | Superseded by repository-organization-guide.md | [Repository Organization Guide](../development/repository-organization-guide.md) |
| `consolidation.md` | 2026-02-12 | Work completed | — |
| `desktop-ui-alternatives-evaluation.md` | 2026-02-12 | Decision made (WPF primary) | See architecture/ui-redesign.md |
| `uwp-development-roadmap.md` | 2026-02-12 | UWP deprecated in favor of WPF | — |
| `2026-02_PR_SUMMARY.md` | 2026-02-12 | Historical PR summary | See CHANGELOG.md |
| ... | ... | ... | ... |
```

**Effort:** 3 hours

### 5.3 Consolidate Improvement Tracking

**Action:** Merge `IMPROVEMENTS.md` and `STRUCTURAL_IMPROVEMENTS.md` into single document

**Target:** `docs/status/IMPROVEMENTS_CONSOLIDATED.md`

**Structure:**
```markdown
# Project Improvements Tracking

## High-Impact Improvements (from IMPROVEMENTS.md)
| ID | Title | Status | Priority | Phase |
|----|-------|--------|----------|-------|
| ... | ... | ... | ... | ... |

## Structural Improvements (from STRUCTURAL_IMPROVEMENTS.md)
| ID | Title | Status | Priority | Phase |
|----|-------|--------|----------|-------|
| ... | ... | ... | ... | ... |

## Completed Improvements
[List of completed items with completion dates]
```

**Effort:** 4 hours

---

## Phase 6: Build and CI Optimization

Optimize build times and CI/CD workflows.

### 6.1 NuGet Caching Audit

**Check current caching:**
```bash
grep -A 5 "cache" .github/workflows/*.yml
```

**Ensure all workflows use consistent caching:**
```yaml
- name: Cache NuGet packages
  uses: actions/cache@v4
  with:
    path: ~/.nuget/packages
    key: ${{ runner.os }}-nuget-${{ hashFiles('**/packages.lock.json') }}
    restore-keys: |
      ${{ runner.os }}-nuget-
```

**Estimated savings:** 2-3 minutes per workflow run

### 6.2 Workflow Consolidation

**Review 17 workflows for redundancy:**

```bash
cd .github/workflows
ls -lh *.yml | awk '{print $9, $5}'
```

**Potential consolidation:**
- Can `pr-checks.yml` and `code-quality.yml` merge?
- Are `desktop-builds.yml` and `dotnet-desktop.yml` redundant?
- Can `nightly.yml` reuse `test-matrix.yml`?

**Effort:** 8 hours analysis + implementation

### 6.3 Test Parallelization

**Audit test project dependencies:**
```bash
dotnet test --list-tests | wc -l  # Count tests
dotnet test --help | grep parallel
```

**Enable parallel test execution:**
```xml
<!-- In Directory.Build.props or test projects -->
<PropertyGroup>
  <ParallelizeTestCollections>true</ParallelizeTestCollections>
  <MaxParallelThreads>4</MaxParallelThreads>
</PropertyGroup>
```

**Estimated savings:** 20-30% reduction in test time

---

## Verification Procedures

### Pre-Cleanup Baseline

Before making any changes, establish baseline:

```bash
# Record current state
dotnet build -c Release 2>&1 | tee baseline-build.log
dotnet test --logger "console;verbosity=detailed" 2>&1 | tee baseline-tests.log

# Count metrics
find src/ -name "*.cs" | wc -l > baseline-file-count.txt
find src/ -name "*.cs" -exec wc -l {} + | tail -1 > baseline-loc.txt

# Save to branch
git checkout -b cleanup-baseline
git add baseline-*.{log,txt}
git commit -m "chore: Establish baseline metrics before cleanup"
```

### Post-Change Verification

After each cleanup phase:

```bash
# 1. Build must succeed
dotnet build -c Release || { echo "Build failed!"; exit 1; }

# 2. All tests must pass
dotnet test || { echo "Tests failed!"; exit 1; }

# 3. Test count must not decrease (unless deleting orphaned tests)
BASELINE_TESTS=$(grep -c "Passed!" baseline-tests.log)
CURRENT_TESTS=$(dotnet test --logger "console;verbosity=minimal" | grep -c "Passed!")
if [ $CURRENT_TESTS -lt $BASELINE_TESTS ]; then
    echo "Warning: Test count decreased from $BASELINE_TESTS to $CURRENT_TESTS"
fi

# 4. Code coverage must not regress
dotnet test --collect:"XPlat Code Coverage" --results-directory ./coverage
# Compare with baseline coverage
```

### Integration Testing

Run end-to-end scenarios:

```bash
# Test scenario 1: Start server and check health
dotnet run --project src/Meridian -- --ui --http-port 8080 &
sleep 5
curl http://localhost:8080/healthz
kill %1

# Test scenario 2: Run backfill
dotnet run --project src/Meridian -- --dry-run

# Test scenario 3: Desktop app launches (Windows only)
if [[ "$OS" == "Windows_NT" ]]; then
    dotnet run --project src/Meridian.Wpf &
    sleep 10
    kill %1
fi
```

---

## Rollback Plans

### Git Safety Net

Always work in feature branches:

```bash
# Start cleanup work
git checkout -b cleanup/phase-1-immediate-wins
git push -u origin cleanup/phase-1-immediate-wins

# Make changes
...

# If something goes wrong
git reset --hard origin/main
```

### Incremental Commits

Commit after each file/component:

```bash
# Good: Small, revertible commit
git add src/Meridian.Ui.Services/Contracts/IConfigService.cs
git commit -m "refactor: Consolidate IConfigService interface"

# Bad: Giant commit
git add .
git commit -m "refactor: Clean up everything"
```

### Automated Rollback Check

Add rollback verification to CI:

```yaml
- name: Verify rollback safety
  run: |
    # Test that reverting last commit still builds
    git revert HEAD --no-commit
    dotnet build || { echo "Rollback would break build!"; exit 1; }
    git revert --abort
```

---

## Execution Tracking

Use this checklist to track progress:

### Phase 1: Immediate Wins
- [x] Delete UWP Examples folder ✅ (Already completed before this session)
- [x] Remove tracked build artifacts ✅ (None found - .gitignore comprehensive)
- [x] Audit and delete orphaned test files ✅ (Checked - all test files valid)
- [x] Clean up .gitignore ✅ (Already comprehensive)

### Phase 2: Interface Consolidation
- [x] `IConfigService` ✅ (Consolidated — UWP removed)
- [x] `INotificationService` ✅ (Consolidated — UWP removed)
- [x] `IKeyboardShortcutService` ✅ (Consolidated — UWP removed)
- [x] All 9 interfaces consolidated ✅ (6 via PR #1028, 3 resolved by UWP removal)

### Phase 3: Service Deduplication
- [ ] `BrushRegistry` (Platform-specific: WPF/UWP use different brush types)
- [ ] `ExportPresetService` (Platform-specific: Different storage APIs)
- [ ] `InfoBarService` (Platform-specific)
- [ ] `TooltipService` (Platform-specific)
- [ ] `ThemeService` (Platform-specific)
- [ ] `ConfigService` (Platform-specific)
- [ ] `NavigationService` (Platform-specific)
- [ ] `LoggingService` (Platform-specific)

*Note: With UWP removed, Phase 3 service deduplication is no longer needed. WPF is the sole desktop platform.*

### Phase 4: Large File Decomposition
- [x] `UiServer.cs` ✅ **COMPLETED** (3,030 → 191 LOC, 93.7% reduction, 2026-02-12)
- [x] `HtmlTemplateGenerator.cs` ✅ Already well-structured (3 focused partials: main/Styles/Scripts)
- [x] `PortableDataPackager.cs` ✅ **COMPLETED** — Split Scripts.cs into Scripts.cs + Scripts.Sql.cs + Scripts.Import.cs (2026-02-16)
- [x] `AnalysisExportService.cs` ✅ **COMPLETED** — Split Formats.cs into Formats.cs + Formats.Parquet.cs + Formats.Xlsx.cs + Formats.Arrow.cs (2026-02-16)

### Phase 5: Documentation Consolidation
- [x] Create `docs/README.md` ✅ (Already exists)
- [x] Create `docs/archived/INDEX.md` ✅ (Already exists)
- [x] Consolidate improvement tracking ✅ (IMPROVEMENTS.md already references archived docs as superseded)
- [x] Audit and fix broken UWP links ✅ Fixed stale UWP references in docs/HELP.md (3 occurrences), docs/integrations/language-strategy.md (2 occurrences), tests/coverlet.runsettings, and Directory.Build.props (2026-02-16)

### Phase 6: Build Optimization
- [x] Audit NuGet caching ✅ All .NET workflows already use `setup-dotnet-cache` composite action (2026-02-16)
- [x] Workflow consolidation review ✅ `test-matrix.yml` intentionally diverges from `reusable-dotnet-build.yml` (documented); separate C#/F# test runs with per-language args require distinct steps (2026-02-16)
- [x] Enable test parallelization ✅ Added `xunit.runner.json` with `parallelizeTestCollections: true` and shared via `tests/Directory.Build.props` (2026-02-16)

---

## Success Metrics

| Metric | Before | Target | Measurement |
|--------|--------|--------|-------------|
| **Duplicate Code Lines** | ~3,000 | <500 | `grep -r "class.*Service" src/ \| wc -l` |
| **Files >1,000 LOC** | 7 | <3 | `find src/ -name "*.cs" -exec wc -l {} + \| awk '$1 > 1000'` |
| **Duplicate Interfaces** | 9 | 0 | Manual audit |
| **Orphaned Tests** | ~15 | 0 | `find tests/ -name "*Tests.cs"` vs source files |
| **Build Time** | ~45s | <35s | CI workflow duration |
| **Test Time** | ~120s | <90s | `dotnet test` duration |
| **Documentation Organization** | 3.5/5 | 5/5 | Manual assessment |

---

*Track progress in GitHub issues with label `cleanup` and milestone `Repository Cleanup`.*
