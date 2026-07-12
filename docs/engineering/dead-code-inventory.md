# Dead-Code Inventory

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-06-20

This inventory records conservative dead-code cleanup evidence for Meridian. It replaces the old
status snapshot route at `docs/status/dead-code-inventory.md` (historical copy retained at
`archive/docs/status/dead-code-inventory.md`), and it is intentionally scoped to cleanup triage
rather than public API redesign.

## Scan Scope

The 2026-06-20 pass checked tracked source code for explicit removal markers and common dead-code
signals without editing `src/**`.

```powershell
rg -n "\[Obsolete" src -g '!**/bin/**' -g '!**/obj/**'
rg -n "#if\s+(false|LEGACY|DEPRECATED)|TODO\s*:?\s*remove|FIXME\s*:?\s*remove|HACK\s*:?\s*remove|throw new NotImplementedException|NotImplementedException\(" src -g '!**/bin/**' -g '!**/obj/**'
rg -n "^\s*//\s*(public|private|protected|internal|var |if \(|foreach \(|for \(|while \(|return |await |using |[A-Za-z0-9_]+\()" src -g '*.cs' -g '!**/bin/**' -g '!**/obj/**'
```

## Current Findings

| Area | Finding | Cleanup decision |
| --- | --- | --- |
| `src/Meridian.Application/Services/AutoConfigurationService.cs` | `UseCase.Research` is obsolete. | Retain. It is a compatibility alias for `Strategy`. |
| `src/Meridian.Ui.Services/Services/WorkspaceModels.cs` | `WorkspaceCategory.Research`, `WorkspaceCategory.DataOperations`, and `WorkspaceCategory.Governance` are obsolete. | Retain. Numeric enum values and aliases are persisted workspace-state compatibility surfaces. |
| `src/Meridian.Infrastructure/Adapters/Templates/TemplateBrokerageGateway.cs` | `TemplateBrokerageGateway` is obsolete. | Retain. It is a deterministic provider scaffold and copy target with test coverage. |
| `src/Meridian.Backtesting/MeridianNativeBacktestStudioEngine.cs` | `resultTask.Result` is present after `IsCompletedSuccessfully`. | No cleanup in this pass. The value read is guarded and needs behavioral review before changing async flow. |
| `src/Meridian.Infrastructure/Adapters/Robinhood/RobinhoodBrokerageGateway.cs` | Audit-style `.Result` search also matches response `.Results` properties. | No cleanup. These are collection properties, not sync-over-async calls. |
| Comment-like code scan | Matches are explanatory comments in `WorkstationEndpoints.cs`, `SchemaServiceBase.cs`, and `ParquetStorageSink.cs`. | No cleanup. They are not commented-out code tombstones. |

No `#if false`, `#if LEGACY_*`, `#if DEPRECATED_*`, `TODO remove`, `FIXME remove`,
`HACK remove`, or `NotImplementedException` candidates were found under `src/**` in this pass.

## Removal Rules

- Remove only private members or unreachable branches after checking references, `nameof`, string
  literals, DI registration, reflection attributes, source generation, XAML bindings, and tests.
- Do not remove public contracts, persisted enum aliases, provider discovery attributes, ADR
  attributes, JSON serialization contexts, or route aliases as cleanup.
- Treat generated files, interop files, and archive-migration stubs as owner-controlled surfaces.
- Split any candidate that changes a public symbol, route tag, enum value, or provider contract into
  a separate reviewed refactor.

## Next Safe Cleanup Batches

- Re-run this inventory before each cleanup batch and compare findings instead of relying on stale
  status snapshots.
- Use the nearest module README and `docs/source/data/source-modules.yml` before any source edit.
- Prefer one cleanup category per change: obsolete-member migration, WPF code-behind noise,
  commented-out code, log hygiene, or docs routing.
- Validate source removals with the narrowest module test or build command listed in the source
  registry.
