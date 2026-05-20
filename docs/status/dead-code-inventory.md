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

## Summary

Most dead-code signals are **intentional scaffolding/compatibility surfaces**, not accidental orphan logic.  
The strongest cleanup candidate is the **legacy brokerage-sync compatibility endpoint + obsolete DTO path**, pending external consumer confirmation.
