# Windows Desktop Historical Provider Configurability Assessment

## Meridian — Structure Review & Improvement Plan

**Date:** 2026-02-13
**Last Reviewed:** 2026-03-19
**Status:** Reference assessment with follow-up; infrastructure complete, UI workflow in progress
**Author:** Architecture Review

---

> Use this file for the provider-configurability rationale and phased implementation shape. For broader desktop delivery status, pair it with `desktop-platform-improvements-implementation-guide.md` and `../status/ROADMAP.md`.

## 1) Current structure (what is good, what is limiting)

### Strengths already present

- **Provider abstraction is solid** via `IHistoricalDataProvider`, `BaseHistoricalDataProvider`, and `CompositeHistoricalDataProvider`, which allows fallback composition and health-aware behavior.
- **Factory centralization exists** through `ProviderFactory.CreateBackfillProviders()`, so provider creation logic is in one place and ordered by provider priority.
- **Multiple provider implementations are already modular** (`Alpaca`, `Polygon`, `Tiingo`, `Finnhub`, `Stooq`, `Yahoo`, `AlphaVantage`, `NasdaqDataLink`, plus IB/StockSharp integration paths), enabling extension without rewriting core orchestration.

### Gaps for Windows desktop configurability

- **WPF Backfill page is mostly demo/static behavior today** and does not persist per-provider operational settings (priority, enable/disable, provider-specific throttles) through a user-friendly workflow.
- **Desktop config DTOs previously lacked a typed per-provider backfill options tree**, making it difficult to build robust UI editing with validation.
- **First-run config template lacked explicit provider settings**, so users cannot immediately inspect/edit fallback order from the Windows app without manual JSON editing.
- **No explicit desktop-level mutation API for provider options** in `ConfigService`, which makes upcoming settings UI difficult to implement cleanly.

---

## 2) Changes introduced in this patch

### New shared configuration DTOs for provider-level backfill settings

Added:
- `BackfillConfigDto.Providers`
- `BackfillProvidersConfigDto`
- `BackfillProviderOptionsDto`

These establish a typed contract for provider-level options:
- `Enabled`
- `Priority`
- `RateLimitPerMinute`
- `RateLimitPerHour`

### Desktop configuration service extensions

Added `ConfigService` methods:
- `GetBackfillProvidersConfigAsync()`
- `SetBackfillProviderOptionsAsync(string providerId, BackfillProviderOptionsDto options)`

These create a clean seam for WPF settings pages and avoid ad-hoc JSON mutation logic in UI code-behind.

### First-run default configuration alignment

Updated default WPF config template to include a full `Backfill.Providers` block with sensible initial priorities and rate-limit hints.

This gives new Windows users an immediately editable baseline for fallback behavior.

---

## 3) Recommended next steps to make providers configurable + extendable in Windows

### Phase 1 — Settings UI (high impact, low risk)

1. Add a **Backfill Provider Settings** panel in WPF:
   - Provider enabled toggle
   - Priority numeric editor
   - Rate limit fields (minute/hour)
   - Reset-to-default action
2. Bind panel to `ConfigService.GetBackfillProvidersConfigAsync()` and `SetBackfillProviderOptionsAsync(...)`.
3. Add inline validation (priority uniqueness optional warning, minimum rate-limit values).

### Phase 2 — Runtime transparency & operator confidence

1. Add a **fallback chain preview** (sorted by effective priority) in the Windows UI.
2. Pull provider health/rate-limit status from existing APIs and display alongside configured values.
3. Show “effective configuration source” badges (default vs user override vs env/secret-driven credential availability).

### Phase 3 — Extensibility model for future providers

1. Introduce a provider metadata descriptor endpoint (editable capabilities schema) to drive dynamic UI generation for provider-specific options.
2. Use a `Dictionary<string, JsonElement>` extension bag for provider-unique options while keeping common fields typed.
3. Add feature flags for new provider onboarding in Windows UI without shipping full custom pages each time.

### Phase 4 — Safety & quality controls

1. Add config validation rules in `ConfigService.ValidateConfigAsync()`:
   - non-negative priorities
   - conflicting fallback order warnings
   - disabled-all-provider hard error
2. Add an optional “dry-run backfill plan” endpoint that returns selected provider sequence per symbol.
3. Add audit trail for provider config edits (timestamp, user, delta) in desktop logs.

---

## 4) Expected functional outcomes

- Non-developer Windows users can tune fallback behavior without editing raw JSON.
- Provider onboarding cost is reduced due to typed + extensible config primitives.
- Production resilience improves by making fallback order visible, explainable, and testable.
- Support burden drops through better defaults and runtime diagnostics in desktop UI.

---

## 5) Implementation notes for maintainers

- Keep provider-specific secrets in existing credential resolvers and secure stores; do not duplicate secrets into desktop config files.
- Preserve provider-independent orchestration in `CompositeHistoricalDataProvider`; treat desktop changes as **configuration orchestration**, not provider logic forks.
- Prefer additive config fields with safe defaults to preserve backward compatibility.



## 6) Detailed future implementation blueprint

### 6.1 Proposed desktop domain model for provider configuration

Use a layered shape in WPF view models:

- `BackfillProviderSettingsViewModel`
  - `ProviderId`
  - `DisplayName`
  - `Enabled`
  - `Priority`
  - `RateLimitPerMinute`
  - `RateLimitPerHour`
  - `IsCredentialConfigured` (derived/readonly)
  - `ValidationErrors`
- `BackfillProviderSettingsCollectionViewModel`
  - `ObservableCollection<BackfillProviderSettingsViewModel>`
  - commands: `Load`, `Save`, `ResetDefaults`, `AutoOrderByRecommendation`

This gives deterministic binding and minimizes direct JSON interaction in UI layers.

### 6.2 API/contract additions for runtime explainability

Add a diagnostics endpoint (or extend existing one) that returns **effective provider selection context**:

```json
{
  "providers": [
    {
      "providerId": "alpaca",
      "configuredPriority": 5,
      "effectivePriority": 5,
      "enabled": true,
      "credentialAvailable": true,
      "health": "Healthy",
      "reasonIfSkipped": null
    }
  ]
}
```

The UI should show this beside editable config so operators can distinguish:
- configured intent
- runtime reality
- why fallback changed at execution time

### 6.3 Backfill execution safety checks

Before a backfill run starts, perform a preflight validator:

1. at least one enabled provider with credentials available
2. no invalid rate limits (<= 0)
3. from/to dates valid and bounded
4. symbol list non-empty after normalization
5. dry-run provider chain generated for at least one symbol

If preflight fails, return actionable errors with fields mapped to desktop controls.

### 6.4 Migration strategy (backward compatible)

1. **Read old config** (`Backfill.Provider`) as primary provider hint.
2. If `Backfill.Providers` is missing, auto-initialize defaults in memory.
3. Write back migrated structure only after first successful Save from settings UI.
4. Keep supporting old fields for at least one major release.

### 6.5 Telemetry and operability plan

Capture these metrics per provider in desktop and service logs:

- selection count
- fallback count
- skipped-due-to-credentials count
- skipped-due-to-health count
- average bars/request
- average request latency
- throttling wait duration

These should feed both the **Provider Health page** and a long-term trend view.

### 6.6 Acceptance criteria for completion

- User can reorder providers and persist priorities through WPF UI.
- User can enable/disable providers without editing JSON manually.
- UI displays runtime effective chain and mismatch reasons.
- Preflight blocks impossible runs with clear remediation text.
- Config validation detects invalid priorities/rate limits.
- End-to-end integration test covers: config edit -> save -> backfill uses updated order.

---

## 7) Implementation Follow-Up (2026-03-19)

**Status:** Proposals documented and infrastructure in place; UI implementation in progress.

### Implementation Status by Phase

| Phase | Proposal | Status | Evidence |
|-------|----------|--------|----------|
| **Phase 1** | Typed DTOs for provider config | ✅ Done | `BackfillProviderOptionsDto`, `BackfillProvidersConfigDto` in `Meridian.Contracts` |
| **Phase 2** | Desktop config service methods | ✅ Done | `GetBackfillProvidersConfigAsync()`, `SetBackfillProviderOptionsAsync()` in `ConfigService` |
| **Phase 3** | WPF UI binding framework | ✅ Done | `BackfillProviderSettingsViewModel`, `ObservableCollection<BackfillProviderSettingsViewModel>` |
| **Phase 4** | Backfill page UI controls | 🔄 Partial | Provider list, priority reordering partially implemented; full settings workflow deferred to Phase 11 |
| **Phase 5** | Runtime diagnostics endpoint | 🔄 Partial | Provider health dashboard exists (`/api/providers/dashboard`); effective selection context pending |
| **Phase 6** | Preflight validator | ⚠️ Open | Validator patterns exist; comprehensive backfill preflight not yet integrated |
| **Phase 7** | Backward-compatible migration | ⚠️ Open | Old config fields supported; automatic migration on first save not yet implemented |
| **Phase 8** | Telemetry & operability | 🔄 Partial | Provider health metrics captured; long-term trend view roadmapped for Phase 14+ |

### Key Achievements

1. ✅ **Type-safe config layer** — Eliminated error-prone JSON string manipulation
2. ✅ **WPF service bindings** — `ConfigService` methods decouple UI from direct config mutation
3. ✅ **Provider health visibility** — `/api/providers/dashboard` surfaces runtime provider state to UI

### Remaining Gaps

- Full WPF UI for reordering/enabling providers (tracked in Phase 11)
- Backward-compatible config migration script
- Comprehensive preflight validation before backfill execution
- Long-term provider telemetry trends

**Verdict:** Foundation is solid; UI completion expected in Phase 11. Core infrastructure enables rapid UI buildout without architectural changes.

---

**Assessment Date:** 2026-02-13
**Last Reviewed:** 2026-03-19
