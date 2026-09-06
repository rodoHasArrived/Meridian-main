---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-UI-SERVICES
path: src/Meridian.Ui.Services
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-09-05
---

# src/Meridian.Ui.Services

## Purpose

`SettingsConfigurationService.GetProviderCredentialStatusesAsync` reads the authenticated service's
credential states. Missing, ambiguous or refused responses remain unavailable, even when environment
variables contain keys. The synchronous environment method remains a legacy diagnostic helper and
is no longer used by the WPF settings, credential management or add-provider status surfaces.

Setup wizard credential saves use the authenticated canonical credential API through the shared
session/CSRF client. They no longer write secrets to application configuration, process environment,
or user environment. An optional retained connection ID selects scoped persistence. Missing or refused
API acknowledgements fail the save with fixed error text; there is no local plaintext fallback.

UI services contains workstation endpoints, UI projections, and operator workflow service support.


`Services/Accounting/AccountingProjectionQueryService.cs` exposes shared accounting close projections for desktop and browser surfaces: trial balance, dimension-scoped roll-forward, source-linked audit rows, and close-state evidence gates.

`Services/Accounting/WorkstationAccountingCloseApiClient.cs` implements the
`IWorkstationAccountingCloseApiClient` marker over `IAccountingCloseManagementService`.
The WPF Accounting Close feature resolves this HTTP client for retained plans, configuration,
sign-offs, evidence review, late adjustments, locks, and reopens. Requests reach the governed
server close endpoints, where authenticated middleware resolves tenant, company, actor, and
controller authority. Hard-lock requests carry the selected close scope and workflow version;
the backend re-evaluates shared readiness before mutation. An unavailable or refused response
does not produce local success, and a plan for another workflow is rejected.

## Layer responsibility

This module owns backend read-model and workflow surfaces for operator UI clients. Keep endpoint
contracts aligned with shared UI models and compatibility gates.

## Key folders and files

- Endpoint and projection services - workstation-facing API composition.
- Workflow service support - operator readiness, routing, and read-model aggregation.
- Project metadata - service dependencies shared by browser and desktop surfaces.

## Important workflows

Reporting status projections translate application report manifests into operator read models with template family, approval status, retry attempt, artifact, audit, and section lineage counts. Keep these projections aligned with browser and WPF Reporting surfaces so both shells show the same generated-run state.
Preview and estimate services must fail closed when the API cannot return source-backed numbers.
Analysis export projections also fail closed when an artifact omits its physical format or when the
returned CSV, Parquet, XLSX, or Arrow format differs from the requested format; compatibility names
such as `excel` and `feather` normalize only to `xlsx` and `arrow`, never to a substitute file.
Do not return synthetic successful counts, latency values, sample sizes, or alignment metrics from
UI-service fallbacks; callers should see an unavailable/error state instead of plausible-looking
operational data.
Scheduled archive-maintenance state is exposed through stable snapshots so operator edits can run
concurrently with scheduler ticks. Timer callbacks are cancellation-aware and contain background
exceptions at the service boundary instead of allowing a maintenance tick to terminate the host.

Use this module when changing workstation endpoint behavior, operator workflow read models,
readiness projections, or UI-service orchestration consumed by browser and WPF clients.
Accounting reconciliation casework endpoints are shared workstation behavior, not client-specific UI logic. Preserve compatibility wrappers for legacy review/resolve calls while routing assign, lifecycle, taxonomy, comments, sign-off, reopen, audit, and bulk triage through shared contracts. Statement break read models are projected into shared `StatementBreakDto` records so the break queue can seed statement-originated cases without depending on infrastructure records.
Statement-run API projection ownership lives in `src/Meridian.Ui.Shared` so browser, WPF, and host-served workstation composition resolve the same source-backed adapter. This module keeps a compatibility wrapper for the legacy `Meridian.Ui.Services.Services.Reconciliation.ReconciliationApiService` type while the shared implementation delegates broker/custodian CSV intake and persistence to the Financial Operations-owned statement-run workflow, then returns shared `StatementRunDto` projections with linked breaks and durable casework for browser and WPF operators. Statement-run list rows also derive review-required status, completion posture, and open exception counts from retained workflow breaks so reconciliation queues do not hide unresolved statement exceptions. Queue-status rows aggregate retained statement breaks by fund-account scope and carry source-backed account identifiers, blocker reasons, and break evidence links for shared operational dashboards. Statement-originated break rows inherit retained case owner, suggested action, SLA due/warning/breach posture, escalation label/reason, and statement-run evidence route so shared break queues seed assigned operator work instead of unowned placeholders.
OMS/EMS integration API handlers are registered from `Services/Integrations/` and implement the shared `IOmsIntegrationApiHandler` contract for idempotent ingestion, replay-safe deduplication, adapter diagnostics, Excel pull/push conflict resolution, request-signing validation, key-rotation hooks, and audit logging.
Settings configuration keeps the retained built-in profile id `research` for compatibility, but
renders it as `Strategy` so browser and WPF settings surfaces do not reintroduce `Research` as a
root workspace label.
Setup wizard presets follow the same compatibility pattern: the retained `researcher` preset id
remains available for callers, while the visible preset name is `Strategy Analyst`.
Command palette workspace entries emit canonical page tags for visible roots; `Open Strategy
Workspace` uses `StrategyShell`, with legacy words such as `research` retained only as search
keywords.
Security Master command-palette entries use the Accounting workspace label while retaining
reference-data search terms, matching the shared close, reconciliation, and report evidence flows.
Data and Accounting command-palette keywords avoid legacy `Data Operations` and `Governance`
workspace vocabulary; use `data`, `quality`, `accounting`, `controls`, and workflow-specific terms
for discovery.
Persisted workspace categories use canonical `Strategy`, `Trading`, `Data`, `Accounting`, and
`Custom` enum members. The older `Research`, `DataOperations`, and `Governance` members remain
only as numeric compatibility aliases for previously saved workspace state.

## Diagrams

See `DIA-BROWSER-WORKSTATION` and `DIA-PAPER-SESSION-REPLAY` in
`docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-UI-SERVICES -->
| Roadmap item | Title |
| --- | --- |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W2-PROMO-001` | Paper promotion evidence and operator acceptance |
| `W3-CONT-001` | Research to paper continuity |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W4-RPT-001` | Governed report pack readiness |
| `W5-ACCT-001` | Accounting records and operational evidence |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-UI-SERVICES -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~MapWorkstationEndpoints" --logger "console;verbosity=normal"
dotnet test tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj --filter "FullyQualifiedName~WorkstationAccountingCloseApiClientTests" --logger "console;verbosity=normal"
```

`WorkstationAccountingCloseApiClientTests` covers transport and server refusal handling;
`tests/Meridian.Wpf.Tests/Features/Accounting/AccountingCloseHttpRecoveryTests.cs` exercises
actual feature registration and selected-workflow recovery after missing, foreign, stale,
unavailable, or blocked close evidence. These are focused acceptance scenarios; W10-SEAM-001
remains in progress until the required hosted integration evidence is complete.

## Change rules

Keep endpoint contracts aligned with `src/Meridian.Ui.Shared` and `src/Meridian.Contracts`. Prefer
shared projections over browser-only or WPF-only product logic.

## Related docs

- `src/Meridian.Ui.Shared/README.md`
- `docs/status/contract-compatibility-matrix.md`
- `docs/reference/oms-ems-integration.md`
- `docs/source/generated/source-module-index.md`

- Reconciliation API service projections expose statement runs, open breaks, account queue status, case summaries, and calibration KPIs for break trend, auto-match rate, T+0 closure rate, and alert thresholds shared by desktop and browser operators.
