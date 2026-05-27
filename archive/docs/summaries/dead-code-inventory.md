# Dead Code Inventory

**Last Updated:** 2026-05-20

## Scope

Static repository scan (type/route reference checks) to identify dead or likely-dead code paths.

## Findings

### 1) Adapter template scaffolds are non-production and currently unreferenced

- **Files:**  
  - `src/Meridian.Infrastructure/Adapters/Templates/TemplateBrokerageGateway.cs`  
  - `src/Meridian.Infrastructure/Adapters/Templates/BrokerAdapterTemplate.cs`
- **Evidence:**  
  - `TemplateBrokerageGateway` is explicitly marked `[Obsolete]` as a copy-target scaffold and warns it must not be used in production (`TemplateBrokerageGateway.cs:34-36`).  
  - Repository references to `TemplateBrokerageGateway` and `BrokerAdapterTemplate` are limited to their own files and docs (no runtime registrations/call sites).
- **Assessment:** Not active runtime code; effectively dead for production execution.
- **Recommendation:** Keep as scaffolding, but move to a clearly isolated scaffold location or exclude from production assemblies if possible.

### 2) Legacy brokerage-sync compatibility projection endpoints appear unused in-repo

- **Files:**  
  - `src/Meridian.Ui.Shared/Endpoints/FundAccountEndpoints.cs`  
  - `src/Meridian.Ui.Shared/Services/BrokeragePortfolioSyncService.cs`  
  - `src/Meridian.Contracts/Workstation/BrokerageSyncDtos.cs`
- **Evidence:**  
  - Legacy endpoints still exposed:
    - `/{accountId}/brokerage-sync/positions` (`FundAccountEndpoints.cs:285-299`)  
    - `/{accountId}/brokerage-sync/activity` (`FundAccountEndpoints.cs:301-318`)  
  - Endpoint block is explicitly marked compatibility-only with `#pragma warning disable CS0618` (`FundAccountEndpoints.cs:284-320`).  
  - DTOs returned by these routes are marked `[Obsolete]` (`BrokerageSyncDtos.cs:198-270`).  
  - Service methods primarily serving these routes:
    - `GetPositionsAsync` (`BrokeragePortfolioSyncService.cs:290-296`)  
    - `GetActivityAsync` (`BrokeragePortfolioSyncService.cs:298-299`)
  - No in-repo references were found in the active dashboard frontend or tests for these two route paths.
- **Assessment:** High likelihood of dead internal usage; may still serve external consumers.
- **Recommendation:** Confirm API consumer telemetry, then deprecate/remove endpoints and obsolete DTOs in a versioned contract cleanup.

### 3) Non-IBAPI event members are intentionally unimplemented compatibility surface

- **File:** `src/Meridian.Infrastructure/Adapters/InteractiveBrokers/EnhancedIBConnectionManager.cs`
- **Evidence:**  
  - Non-IBAPI build path suppresses `CS0067` for interface-required events (`EnhancedIBConnectionManager.cs:49-61`).  
  - Members exist to keep interface compatibility and throw platform-not-supported for behavior (`EnhancedIBConnectionManager.cs:66-103`).
- **Assessment:** Not dead by accident; intentional compile-path compatibility code.
- **Recommendation:** Keep as-is unless IBAPI build strategy changes.

### 4) Multiple event hooks are declared but never raised (dormant event surface)

- **Files:**  
  - `src/Meridian.Ui.Services/Services/EventReplayService.cs`  
  - `src/Meridian.Ui.Services/Services/TimeSeriesAlignmentService.cs`  
  - `src/Meridian.Ui.Services/Services/LeanIntegrationService.cs`  
  - `src/Meridian.Application/Config/Credentials/CredentialTestingService.cs`  
  - `src/Meridian.Application/Scheduling/BackfillScheduleManager.cs`  
  - `src/Meridian.Infrastructure/Adapters/Core/CompositeHistoricalDataProvider.cs`  
  - `src/Meridian.Infrastructure/Adapters/Core/Backfill/BackfillRequestQueue.cs`
- **Evidence:**  
  - Explicit `CS0067` suppression comments indicate dormant events:
    - `EventReplayService.EventReplayed`, `EventReplayService.ProgressChanged` (`EventReplayService.cs:32-39`)  
    - `TimeSeriesAlignmentService.ProgressChanged` (`TimeSeriesAlignmentService.cs:27-29`)  
    - `LeanIntegrationService.BacktestStatusChanged` (`LeanIntegrationService.cs:27-29`)  
    - `CredentialTestingService.OnTokenRefreshed` (`CredentialTestingService.cs:28-30`)  
    - `BackfillScheduleManager.ScheduleDue` (`BackfillScheduleManager.cs:40-42`)  
    - `CompositeHistoricalDataProvider.OnProgressUpdate` (`CompositeHistoricalDataProvider.cs:41-43`)  
    - `BackfillRequestQueue.OnRequestReady` (`BackfillRequestQueue.cs:39-41`)
  - Additional invoke checks show these members have no corresponding `?.Invoke(...)` call sites in their defining files.
- **Assessment:** High-confidence dead members (declared extension points, currently inert).
- **Recommendation:** Either remove these events or file follow-up tasks to implement emission and subscribers.

### 5) Event replay WPF surface is mock-driven while replay service appears unwired

- **Files:**  
  - `src/Meridian.Wpf/ViewModels/EventReplayViewModel.cs`  
  - `src/Meridian.Ui.Services/Services/EventReplayService.cs`
- **Evidence:**  
  - `EventReplayViewModel.Initialize()` seeds hard-coded sample sessions instead of calling replay APIs (`EventReplayViewModel.cs:85-87`).  
  - Source-wide symbol search in `src/` finds `EventReplayService` references only inside `EventReplayService.cs` itself (no production call sites).
- **Assessment:** High likelihood that `EventReplayService` is currently orphaned in production code (retained by tests only).
- **Recommendation:** Either wire `EventReplayViewModel` to `EventReplayService` or archive/remove the service to reduce maintenance overhead.

## Summary

Most dead-code signals are **intentional scaffolding/compatibility surfaces**, not accidental orphan logic.  
The strongest cleanup candidates are:
- legacy brokerage-sync compatibility endpoint + obsolete DTO path (pending external consumer confirmation),
- dormant event declarations with no emitters,
- and the likely-unwired `EventReplayService` path in retained WPF workflow code.
