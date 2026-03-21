# Meridian Codebase Code Audit Report
**Date:** 2026-03-18
**Audit Type:** Code Convention & Architecture Compliance
**Scope:** Full repository (773 source files, 3,374 files scanned)

---

## Executive Summary

The Meridian codebase audit identified **1,183 total findings** across code conventions and architecture compliance:
- **Code Conventions:** 832 issues (2 critical, 693 warnings, 137 info)
- **Architecture Compliance:** 351 issues (86 critical, 265 warnings)

### Critical Issues Requiring Immediate Attention: 88

The most critical issues are architectural violations that violate ADR contracts and create data durability risks. These must be addressed before shipping.

---

## Code Convention Audit Results

**Tool:** `ai-repo-updater.py audit-code`
**Total Findings:** 832

### Summary by Category

| Category | Count | Severity | Fix Hint |
|----------|-------|----------|----------|
| Missing `CancellationToken` on async methods | 686 | ⚠️ Warning | Add `CancellationToken ct = default` parameter |
| Unsealed public classes | 137 | ℹ️ Info | Add `sealed` modifier unless designed for inheritance |
| Raw `HttpClient` instantiation | 7 | ⚠️ Warning | Inject `IHttpClientFactory` and call `CreateClient()` |
| Blocking async with `.Result`/`.Wait()` | 2 | 🔴 **Critical** | Use `await` instead (deadlock risk) |

### Critical Issues: Blocking Async (2)

**File:** `src/Meridian.Mcp/Tools/ConventionTools.cs`

These two instances of `.Result` or `.Wait()` on async code can cause thread pool deadlocks. These must be refactored to use `await` instead.

**Prevention:** Per CLAUDE.md — NEVER block async with `.Result` or `.Wait()` (causes deadlocks)

**Verification Command:**
```bash
python3 build/scripts/ai-repo-updater.py audit-code | grep "blocking-async"
```

---

### Warning Issues: Missing CancellationToken (686)

**Files Affected:**
- `src/Meridian.McpServer/Tools/SymbolTools.cs` (line 67, 115)
- `src/Meridian.McpServer/Tools/BackfillTools.cs` (line 39)
- `src/Meridian.McpServer/Tools/StorageTools.cs` (line 87)
- `src/Meridian.Infrastructure/Resilience/WebSocketConnectionManager.cs` (lines 120, 333, 525, 533)
- `src/Meridian.Infrastructure/Resilience/WebSocketResiliencePolicy.cs` (lines 261, 339)
- And 676 more async methods across the codebase...

**Impact:** Async methods lack cancellation support, preventing graceful shutdown and timeout handling.

**Prevention:** Per CLAUDE.md — ALWAYS use `CancellationToken` on async methods

**Verification Command:**
```bash
python3 build/scripts/ai-repo-updater.py audit-code | grep "missing-cancellation-token" | wc -l
```

---

### Warning Issues: Raw HttpClient (7)

**Files Affected:**
- `src/Meridian.Mcp/Tools/ConventionTools.cs`
- Other infrastructure adapter files

**Impact:** Direct `new HttpClient()` instantiation causes socket exhaustion and DNS cache issues.

**Prevention:** Inject `IHttpClientFactory` and call `.CreateClient()` instead.

---

### Info Issues: Unsealed Classes (137)

**Files Affected:**
- `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/EnhancedIBConnectionManager.IBApi.cs` (line 13)
- `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBApiLimits.cs` (line 486)
- `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/EnhancedIBConnectionManager.cs` (line 20)
- And 134 more public classes...

**Impact:** Low-priority code quality. Classes should be `sealed` unless explicitly designed for inheritance (improves performance and signals intent).

---

## Architecture Compliance Audit Results

**Tool:** `ai-architecture-check.py check`
**Total Findings:** 351 (86 critical, 265 warnings)

### Summary by Violation Type

| Violation | Count | Severity | Impact |
|-----------|-------|----------|--------|
| Direct FileStream writes (ADR-007) | 35 | 🔴 **Critical** | Data durability risk: partial writes on crash |
| Forbidden dependency directions (ProviderSdk refs) | 8 | 🔴 **Critical** | Architectural layering violation |
| Missing [ImplementsAdr] attributes | 2 | 🔴 **Critical** | ADR contract traceability lost |
| Raw Channel.Create* calls (ADR-013) | 2 | 🔴 **Critical** | Backpressure & bounded channel policy violated |
| Missing [DataSource] attributes | 14 | ⚠️ Warning | Provider registry discovery failure |
| Other ADR violations | 5 | ⚠️ Warning | Various ADR contract gaps |

---

### Critical: Direct FileStream Writes in Storage Path (35 issues)

**Violation:** ADR-007 (WAL & Event Pipeline Durability) violation

**Files Affected:**
- `src/Meridian.Storage/Packaging/PortableDataPackager.Creation.cs` (5 issues at lines 353, 409, 467, 574, 593)
- `src/Meridian.Storage/Packaging/PortableDataPackager.Validation.cs` (5 issues at lines 21, 33, 62, 92, 180)
- `src/Meridian.Storage/Archival/WriteAheadLog.cs` (7 issues)
- `src/Meridian.Storage/Archival/SchemaVersionManager.cs` (2 issues at lines 253, 288)
- `src/Meridian.Storage/Store/JsonlMarketDataStore.cs` (1 issue at line 76)
- `src/Meridian.Storage/Services/DataQualityService.cs` (1 issue at line 437)
- `src/Meridian.Storage/Services/FileMaintenanceService.cs` (4 issues)
- `src/Meridian.Storage/Services/StorageCatalogService.cs` (2 issues)
- And 3 more files...

**Problem:**
```csharp
// ❌ WRONG: Direct File.Create bypasses AtomicFileWriter
await using var packageStream = File.Create(packagePath);
```

Direct file system writes create **data durability risks**:
- Partial JSONL records written to disk if process crashes
- No atomic write guarantees
- Recovery impossible if file is incomplete

**Fix:**
```csharp
// ✓ CORRECT: Route through AtomicFileWriter
await atomicWriter.WriteAsync(data, packagePath, ct);
```

**Prevention:** All storage writes must go through `AtomicFileWriter` to guarantee atomicity and prevent partial data on crash.

**Reference:** `CLAUDE.storage.md` — AtomicFileWriter contract

---

### Critical: Forbidden Dependency Directions (8 issues)

**Violation:** ProviderSdk → Contracts-only layering rule

**Files Affected:**
- `src/Meridian.ProviderSdk/IMarketDataClient.cs` (lines 3, 4)
  - `using Meridian.Infrastructure.Adapters.Core;`
  - `using Meridian.Infrastructure.Contracts;`
- `src/Meridian.ProviderSdk/IProviderMetadata.cs` (lines 1, 2)
- `src/Meridian.ProviderSdk/IOptionsChainProvider.cs` (lines 3, 4)
- `src/Meridian.ProviderSdk/DataSourceAttribute.cs` (line 1)
- `src/Meridian.ProviderSdk/IProviderModule.cs` (line 1)

**Problem:** ProviderSdk should only reference `Meridian.Contracts` to maintain clean layering for SDK consumers. References to `Infrastructure.Adapters.Core` and `Infrastructure.DataSources` create circular dependencies.

**Fix:** Remove forbidden `using` directives; move shared types to `Contracts` project if needed.

---

### Critical: Missing [ImplementsAdr] Attributes (2 issues)

**Violation:** ADR traceability contract not maintained

**Files Affected:**
- `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBSimulationClient.cs` (line 18)
- `src/Meridian.Infrastructure/Adapters/Core/ISymbolSearchProvider.cs` (line 83)

**Fix:** Add attributes:
```csharp
[ImplementsAdr("ADR-001", "Core provider contract")]
[ImplementsAdr("ADR-004", "Async patterns with CancellationToken")]
public class IBSimulationClient : IMarketDataClient { ... }
```

**Reference:** ADR-005 — Attribute-based discovery

---

### Critical: Raw Channel.Create* Calls (2 issues)

**Violation:** ADR-013 (Bounded channel pipeline policy)

**Files Affected:**
- `src/Meridian.Execution/OrderManagementSystem.cs` (line 31)
  - `_executionChannel = Channel.CreateBounded<ExecutionReport>(...)`
- `src/Meridian.Core/Pipeline/EventPipelinePolicy.cs` (line 111)
  - `return Channel.CreateBounded<T>(ToBoundedOptions(...))`

**Problem:** Raw `Channel.Create*` calls bypass `EventPipelinePolicy`, losing consistent backpressure configuration across the system.

**Fix:**
```csharp
// ✓ CORRECT: Use EventPipelinePolicy for consistent backpressure
_executionChannel = EventPipelinePolicy.Default.CreateChannel<ExecutionReport>();
```

**Reference:** ADR-013 — Bounded channel pipeline policy

---

### Warning: Missing [DataSource] Attributes (14 issues)

**Files Affected:**
- `src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpSymbolSearchProvider.cs` (line 36)
- `src/Meridian.Infrastructure/Adapters/StockSharp/StockSharpHistoricalDataProvider.cs` (line 39)
- `src/Meridian.Infrastructure/Adapters/Failover/FailoverAwareMarketDataClient.cs` (line 23)
- `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBHistoricalDataProvider.cs` (line 30)
- `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/IBSimulationClient.cs` (line 18)
- `src/Meridian.Infrastructure/Adapters/TwelveData/TwelveDataHistoricalDataProvider.cs` (line 1)
- `src/Meridian.Infrastructure/Adapters/Core/WebSocketProviderBase.cs` (line 34)
- `src/Meridian.Infrastructure/Adapters/Core/ISymbolSearchProvider.cs` (line 83)
- `src/Meridian.Infrastructure/Adapters/Core/CompositeHistoricalDataProvider.cs` (line 20)
- `src/Meridian.Infrastructure/Adapters/Core/BaseHistoricalDataProvider.cs` (line 37)
- And 4 more files...

**Problem:** Missing `[DataSource("provider-name")]` attribute prevents automatic provider registry discovery.

**Fix:**
```csharp
[DataSource("stooq")]
public class StooqHistoricalDataProvider : IHistoricalDataProvider { ... }
```

**Reference:** ADR-005 — Attribute-based discovery

---

## Known AI Errors Cross-Reference

The audit results align with 2 known documented AI mistakes:

1. **AI-20260205-nu1008-central-package-management**
   Area: build/NuGet/CPM
   Status: Fixed
   Relevance: PackageReference Version violations would show in build, not code audit

2. **AI-20260207-storage-namespace-circular-dependency**
   Area: build/Infrastructure
   Status: Fixed
   Relevance: Storage circular deps resolved; FileStream violations are separate durability concern

---

## Recommendations & Priority

### Immediate (Before Next Release)

1. **Fix 2 blocking async calls** (deadlock risk)
   - `src/Meridian.Mcp/Tools/ConventionTools.cs`
   - Estimated effort: 1-2 hours
   - Verification: `dotnet build` + `make ai-audit-code`

2. **Fix 8 ProviderSdk dependency violations** (architectural)
   - Remove forbidden `using` directives from ProviderSdk
   - Estimated effort: 2-3 hours
   - Verification: `python3 build/scripts/ai-architecture-check.py check-deps`

3. **Fix 35 FileStream writes in storage** (data durability)
   - Route through `AtomicFileWriter`
   - Estimated effort: 8-12 hours
   - Verification: `python3 build/scripts/ai-architecture-check.py check-sinks`

4. **Add 2 missing [ImplementsAdr] attributes** (traceability)
   - `IBSimulationClient.cs`, `ISymbolSearchProvider.cs`
   - Estimated effort: 1 hour
   - Verification: `python3 build/scripts/ai-architecture-check.py check-adrs`

5. **Fix 2 raw Channel.Create* calls** (pipeline policy)
   - Use `EventPipelinePolicy.Default.CreateChannel<T>()`
   - Estimated effort: 1 hour
   - Verification: `python3 build/scripts/ai-architecture-check.py check-channels`

**Total Immediate Effort:** ~15-20 hours

### Short-term (This Sprint)

6. **Add 14 missing [DataSource] attributes** (provider registry)
   - Estimated effort: 2-3 hours
   - Verification: `python3 build/scripts/ai-architecture-check.py check-adrs`

7. **Add CancellationToken to 686 async methods** (graceful shutdown)
   - Large-scale refactoring
   - Estimated effort: 30-40 hours
   - Can be done incrementally (start with hot paths: providers, storage, pipeline)

### Long-term (Code Quality)

8. **Add `sealed` modifier to 137 unsealed classes** (performance, clarity)
   - Low priority; can be incremental
   - Estimated effort: 5-10 hours
   - Only required if class is not a base class

9. **Replace 7 raw HttpClient instantiations** (reliability)
   - Estimated effort: 2 hours
   - Verification: `python3 build/scripts/ai-repo-updater.py audit-code | grep "raw-httpclient"`

---

## Verification Commands

Run these commands to verify fixes as you address issues:

```bash
# Full audit (code + architecture)
make ai-audit

# Code conventions only
make ai-audit-code

# Architecture only
python3 build/scripts/ai-architecture-check.py check

# Specific checks
python3 build/scripts/ai-architecture-check.py check-sinks      # FileStream
python3 build/scripts/ai-architecture-check.py check-adrs       # [ImplementsAdr]
python3 build/scripts/ai-architecture-check.py check-deps       # Dependency violations
python3 build/scripts/ai-architecture-check.py check-channels   # Channel.Create*

# Build + test verification
make ai-verify
```

---

## References

- **CLAUDE.md:** Project conventions and critical rules
- **docs/adr/:** Architecture Decision Records (ADR-001…ADR-014)
- **docs/ai/ai-known-errors.md:** Known AI-caused mistakes
- **docs/ai/claude/CLAUDE.storage.md:** Storage architecture and AtomicFileWriter contract
- **docs/ai/claude/CLAUDE.repo-updater.md:** Audit tool documentation

---

## Audit Artifacts

Raw audit outputs saved for reference:
- `audit-code-results.json` — Full code convention findings (JSON format)
- `audit-architecture-results.txt` — Full architecture findings (text format)

---

*Audit completed: 2026-03-18 21:51:40 UTC*
