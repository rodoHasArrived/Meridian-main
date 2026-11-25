---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-UI-SERVICES
path: src/Meridian.Ui.Services
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-11-17
---

# src/Meridian.Ui.Services

The existing accounting agreement workspace/client carries optional structured legal declarations,
dates-only schedule previews and explicit external-review attestations through the native host API.
It keeps exact decimal strings, submitted hashes and reviewer/operator roles distinct. Obligation
monitoring uses the canonical posted schedule and remains advisory rather than a local calculation
or approval. See [the intercompany procedure](../../docs/operators/intercompany-entity-controls.md).

The default shared configuration service re-reads existing JSON under the common host write lease
and applies the same whole-document save guard as desktop settings before atomic publication.
Missing files allow validated first-run creation; malformed or empty existing files are never
replaced by fallback defaults. Loaded drafts retain only a source fingerprint, so a stale draft
cannot erase a concurrent host change. Plaintext credentials, host tenancy and live activation,
provider ceilings, audit ownership and stored-data semantics use the central guard.

Backfill provider metadata consumes the same Core tier catalog as the HTTP editor and adapter
constructors. Shared option validation enforces vendor ceilings; tier IDs and Twelve Data/OpenFIGI
options survive DTO roundtrips. Nested extension data also retains validation profiles/audit, failover
rules/audit/timing and symbol mappings. Common provider-option edits preserve existing provider-specific
host fields from the locked read. Offline metadata does not imply a connected or credentialed runtime.

Intercompany accounting profiles link to the canonical legal registry through exact approved
GUID/version/payload-hash pins. Legal terms and independent legal approval remain in that registry;
profiles own operational templates, party-book/account mappings and bounded validity only. Native
legal line dimensions and accounting-profile dimensions remain separate through storage, review,
source evidence and both workstations. See [the implementation plan](../../docs/engineering/plans/intercompany-entity-controls.md).

`Services/Accounting/WorkstationEntityConsolidationApiClient` consumes the shared entity timeline,
period list and consolidation preview/draft routes without calculating finance locally. Responses
must retain the requested organization/entity/as-of or consolidation scope. Its companion
`WorkstationManualJournalApiClient` lets Accounting Configure review the same retained server
drafts through existing workbench and lifecycle routes; the authenticated host resolves actor and
tenant authority. Refused, stale and unavailable responses never produce local success.

`Services/Accounting/WorkstationFiscalYearCloseApiClient` reads the governed fiscal-year close plan
for one book and year. It refuses a plan for another book or year, and a "ready" plan without a
hash or with blockers; workstations present the server's sections and never recompute balances.

The shared entity/consolidation API client supplies authorized organization/entity/book/period
choices, entity timeline and consolidation preview/draft operations to WPF. Requests preserve host
scope and server-calculated capabilities; no workstation client derives financial truth locally.
See [intercompany operations](../../docs/operators/intercompany-entity-controls.md).

`WorkstationIntercompanyAgreementApiClient` and `IntercompanyAgreementWorkspace` consume the shared
tenant-scoped registry and advisory journal-line routes. The workspace retains unsaved/conflicting
input, server capabilities, exact approved versions, evidence and review history without duplicating
agreement authority in the desktop client. Source-line responses verify agreement/book/date identity
and retain exact amount strings. See [intercompany agreements](../../docs/domain/intercompany-agreements.md).

`IntercompanyManagementFeeWorkspace` is the desktop state for fixed management fees over the client's fee
preview and draft calls; the agreement workspace carries the fixed-fee basis. See
[governed fixed management fees](../../docs/domain/intercompany-agreements.md#governed-fixed-management-fees).

`IntercompanyRateFixingWorkspace` is the desktop state for reviewed fixings over the client's
list, propose and decide calls; the agreement workspace carries the fixing lag and accrual slices
name their pinned fixing. See [floating rates and reviewed fixings](../../docs/domain/intercompany-agreements.md#floating-rates-and-reviewed-fixings).

`IntercompanyBalanceConfirmationWorkspace` is the desktop state for reciprocal balance confirmation
over the client's exact-decimal confirmation reads and actions; a refused action keeps its request
identity only for an identical retry. See [reciprocal balance confirmation](../../docs/domain/intercompany-agreements-and-entity-lifecycle.md#reciprocal-balance-confirmation).

The interest accrual workspace shows each slice's compounded interest for rule v3 previews. See
[periodic compounding](../../docs/domain/intercompany-agreements.md#periodic-compounding).

`IntercompanyCounterpartQueueWorkspace` is the desktop state for counterpart delivery records and
booking breaks over the same `/api/ledger/intercompany/proposals` and `/breaks` routes as the
browser queue. Retry, explicit linking and FX repricing send a reasoned request whose identity is
reused for an identical retry; the server decides permission, target-book authority and every
amount. Responses for another book, proposal, link target or repricing request are refused, a
break outage reads as unknown rather than empty, and amounts render from exact decimals.
The source book is chosen from `/api/ledger/books`, the route the browser uses, and the existing
counterpart book from that list's books owned by the proposal's counterparty entity.

`IntercompanyInterestAccrualWorkspace` is the desktop state for requesting a governed loan-interest
accrual over `/api/ledger/intercompany/accruals/preview` and `/drafts`, the routes the browser uses.
It sends the source book, the two accounting profiles and the half-open window only; the rate,
principal and amount are the server's. A draft is bound to the evidence digest of the preview that
was shown, any change to the selection withdraws that preview, and a response for another window,
book or computation is refused. It never approves or posts. The book is chosen from
`/api/ledger/books`; its approved LoanInterest profiles come from the book-scoped
`/api/ledger/intercompany-accounting-arrangements` read, and the principal choices are the Loan
profiles of the same legal agreement. The lists only fill the selection; the server still
authorizes every identifier, and a profile read for a book no longer chosen is ignored.

`IntercompanyAccrualScheduleWorkspace` is the desktop state for opt-in, reviewed accrual schedules
over `/api/ledger/intercompany/accrual-schedules*`, the routes the browser uses. Choosing a book
lists its schedules and, for ledger managers, the LoanInterest profiles and same-agreement Loan
principal profiles to propose one with a reason. A selected schedule is approved, rejected or
disabled (reject and disable need a reason) against its exact version; due periods are previewed
as of today's UTC date and an active schedule can be run now. Schedules only draft for ordinary
review; nothing posts, and the server refuses approval by the proposer. A request identity is
reused only for an identical retry, read-only operators get no mutation commands, and responses
for another book, schedule, date or request are refused.

`WorkstationOnboardingApiClient` and `OnboardingShadowWorkspace` bring shadow-mode onboarding
(W11-SHADOW-001) to the desktop over the browser's onboarding routes. The panel covers:

- designation and withdrawal
- opening-balance import, with file content sent base64-encoded
- the combined opening report and uncovered kinds
- explanations for opening breaks and period differences
- the parallel-run certificate and promotion

Onboarding refusals surface the server's `{ "error": ... }` sentence. Responses for another workspace,
certificate or book are refused with 502. Every mutation carries the workspace version. A 409 reloads the
server copy and keeps the operator's inputs. Late responses from an earlier selection are discarded.

Shared API response options accept exact decimal strings as well as legacy numeric tokens, so
typed fixed-income reads preserve nullable and nested financial values. API request serialization
continues to emit numbers; compact storage, pretty-print and retained calculation serializers are
unchanged. The endpoint precision regression exercises the real shared client against both wire forms.

`DataSamplingService.EstimateSampleSizeAsync` calls `GET /api/sampling/estimate?symbol=...` once per requested symbol and combines the server's population and recommended size; it previously POSTed a body the route does not accept.

## Purpose

Owned credential connection discovery excludes incomplete ownership and duplicate connection IDs.
Status reads accept an explicit connection ID so credential-management selection does not borrow
provider-wide state. Account and environment metadata remain attached to each connection.

`SetupWizardService.SaveCredentialsAsync` accepts a save only when the service acknowledges the requested
provider's canonical ID with a Configured or Verified state.
`SettingsConfigurationService.SaveProviderCredentialsAsync` returns the persisted state; `Partial` means
the fields were saved while required fields are still missing, so callers report it as incomplete rather
than as a failed save.
Credential-management save, remove and verification operations use the shared authenticated API
client. Saves send canonical field names; mutations require a matching provider and an acknowledged
result state. Verification requires a successful, dated server result. Optional connection IDs route
to retained scoped ownership. Fixed failure messages do not echo secrets or server response bodies.
A 401 or 403 refusal throws `CredentialServiceRefusedException`, a subclass of the existing
`InvalidOperationException`, whose message states that credential changes need a signed-in account
with a tenant assignment and ManageCredentials. Nothing falls back to a local or environment store.
Each `ProviderCredentialStatus` carries the vault field schema the service reported for that provider,
or null when the service reported none; editors must use those names rather than the local catalog's.
Status reads also include managed providers the service reports but the local market-data catalog
omits (QuickBooks, Plaid, IB Flex). Verification treats a successful `NotRequired` result as ready.
Removal accepts `Missing`, or `NotRequired` with an explicit `NotRequired` credential source;
a missing or default source cannot confirm a `NotRequired` result.
`providerWideOnly: true` reads provider-wide records only, without the tenant's connection credentials.
`OAuthRefreshService` refreshes tokens through the host's `ICredentialService`; the shared
`CredentialService` is a no-op test and fallback base that never yields a token. Provider ids come
from the host's `OAuthTokenResourcePrefix` (WPF: `Meridian.OAuth.<provider>`), and `.refresh`
companion entries are skipped. The WPF host starts the service after desktop activation and disposes
it on exit. `TimeSeriesAlignmentService` reports an HTTP 501 as `IsServiceUnavailable` so callers can
present alignment as unavailable rather than as a failed run.
Provider IDs use the shared ProviderSdk identity map for request paths, mutation acknowledgements,
status joins and catalog deduplication. Aliases resolve to one canonical status row and retain the
service field schema; ambiguous duplicate service rows remain unavailable. Status projections retain
verification state and timestamp so callers can distinguish configured from verified credentials.

`DesktopOperatorPreferenceSync` keeps the desktop theme, density and keyboard shortcut overrides in
step with the operator's host preferences (`/api/workstation/operator/preferences`, the set the browser workstation syncs).
`DesktopOperatorPreferenceMapping` maps the shared vocabulary to `AppTheme` and `ShellDensityMode`:
`default` and `spacious` both show as Standard, and a desktop Standard save keeps a stored
`spacious`. On start the host values are applied and nothing is saved, because the desktop does not
persist its theme and its startup theme is a default rather than a choice. An operator change on the
desktop re-reads the host set and saves it with only appearance and density replaced, so the time
zone, page-descriptions setting and browser shortcut overrides are kept. Desktop shortcut overrides
travel as `DesktopKeyBindings`: a host set (an empty set included) replaces this desktop's at start;
when the host has none yet, this desktop's own overrides are saved up to seed it; and an operator
rebind, toggle or reset saves only that set. `ToHostShortcuts`, `ToDesktopShortcuts` and
`SameShortcuts` map and compare the two forms. Notification choices travel as `Notifications`: a host
set replaces the desktop's `NotificationSettings` at start (unset values take the defaults; the sound
choice stays desktop-only), and a desktop change saves only that set (`ToNotificationSettings`,
`ToHostNotifications`).

`NotificationServiceBase` judges quiet hours with `QuietHours.IsActive` against its `TimeProvider`
and the shared `DisplayTimeZone`, so the window is wall-clock time in the operator's display zone; a
window ending earlier than it starts runs past midnight, the end minute is not quiet, and equal times
never hold anything. `IsQuietHoursNow` exposes the state, `UpdateSettings` raises `SettingsChanged`,
and `NotificationSettings.Copy()` builds a change without editing the settings in use. A host that cannot be read at
start leaves the desktop local for the run; a failed save keeps the local change and the next change
retries. The display time zone is resolved with this machine's zone data at start and again whenever
the desktop saves, and applied to the shared `DisplayTimeZone` (UTC when unset or unknown here).
`OperatorPreferencesApiClient` is the HTTP client and `IDesktopPreferenceSurface` the seam the WPF
shell implements.

`FormatHelpers.FormatDisplayTimestamp` (with `DateTime`, nullable and `FormatDisplayTimeRange`
overloads) formats an instant in `DisplayTimeZone.Current`. In UTC the label is exactly the
`FormatUtcTimestamp` form ("2026-07-05 12:00 UTC"); in any other zone the local time carries its
offset for that instant ("2026-07-05 13:00 UTC+01:00"), so a zone that is at +00:00 for part of the
year is never labelled UTC. `TryParseDisplayTimestamp` reads either form back to UTC. Display only:
stored, audit and ledger values are unchanged. `DisplayTimestampTests` set the process-wide zone and
run in a serial test collection.

`DesktopShellPreferences` holds the desktop shell density and the operator's keyboard shortcut
overrides (`ShortcutOverrides`, keyed by action id, with keys and modifiers stored as WPF enum
names so this contract stays free of WPF types). `SettingsConfigurationService` persists both in
`%LOCALAPPDATA%\Meridian\desktop-shell-preferences.json` through a temp-file-then-move write:
`SetShortcutOverrides` replaces the whole set (empty restores defaults) and preserves density,
density changes preserve overrides, files without overrides still load, and null or blank entries
in a hand-edited file are skipped. Validating overrides against registered shortcuts belongs to the
WPF `KeyboardShortcutService`.

`SettingsConfigurationService.GetProviderCredentialStatusesAsync` reads the authenticated service's
credential states. Missing, ambiguous or refused responses remain unavailable, even when environment
variables contain keys. The synchronous environment method remains a legacy diagnostic helper and
is no longer used by the WPF settings, credential management or add-provider status surfaces.

Setup wizard credential saves use the authenticated canonical credential API through the shared
session/CSRF client. They no longer write secrets to application configuration, process environment,
or user environment. An optional retained connection ID selects scoped persistence. Missing or refused
API acknowledgements fail the save with fixed error text; there is no local plaintext fallback.

Batch exports reject duplicate queue entries, skip cancelled attempts, and serialize atomic job-store writes with observable failures. Initial creation and manual or scheduled requeues persist before publishing to workers. Cancellation and removal persist before signalling an execution token or releasing job ownership; failed writes restore the previous status and keep queued work retryable. Successful cancellations and removals remain effective after restart; completion and failure notifications follow durable history and execution cleanup so subscribers can queue a repeat or retry. Raw, JSONL, and CSV are supported; Parquet is rejected until a physical writer exists. JSONL accounting uses the actual decompressed artifact path. CSV discovers all columns, quotes every cell, and fails with rejected row numbers before replacing an artifact. Backfill checkpoint mutations serialize per job and reclaim locks after the last holder or waiter leaves, including cancellation and persistence failures. Activity-feed persistence coalesces pending snapshots while preserving waiter completion and shutdown draining.

UI services contains workstation endpoints, UI projections, and operator workflow service support.

`Services/ColorPalette.cs` supplies the Design System 4.3.0 runtime colors used by WPF brush registries,
ScottPlot, and QuantScript. Core ARGB values mirror `Meridian.Wpf/Styles/ThemeTokens.xaml`: actual
traces use graphite, comparisons neutral ink, positive/negative retain green/red, and information
uses interaction blue. Semantic washes match the corresponding 20% XAML brushes. The palette is
static and keeps light card/data backgrounds paired with their ink in either window theme; this
update does not introduce dynamic chart theming. `ColorPaletteTests` checks C#/XAML parity and
existing notification, progress, latency, and stream-status mapping boundaries.


Activity-feed startup and legacy migration retain the file generation they opened while allowing
the ordered persistence worker to publish an atomic replacement. Snapshot reads share deletion
on Windows and use the shared atomic writer's open-file replacement support.
The reader still observes its complete original generation; subsequent loads see the replacement.

Fixture network delays retain their 50–149 ms simulation range and use a configurable time provider.
The shared singleton uses system time. Tests control timer completion directly and verify both
pre-cancelled and pending requests; caller cancellation is honored without waiting for simulated time.

Provider health caches and history use ProviderSdk canonical family identities so accepted
configuration and telemetry aliases address the same health row.


`Services/Comparisons/PeriodComparisonProjection.cs` formats any `PeriodComparisonDto` for the
desktop exactly as the browser comparison panel does: two-place amounts with negatives in
parentheses, whole counts, the server's percent (never recomputed), "Not comparable · prior zero",
and the Portfolio materiality threshold. `PeriodComparisonApiClient` reads the portfolio cash ladder
comparison over `UiApiRoutes.PortfolioCashLadderComparison`.

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

UI logging accepts named message placeholders with the existing string tuple properties, for
example `LogError("Failed to load {Symbol}", exception, ("Symbol", symbol))`. The shared formatter
retains property fields and renders the message for desktop Activity Log consumers. Unknown
placeholders remain visible, property values are not parsed as templates, and messages without
properties retain their literal text. `LoggingServiceBaseTests` covers rendering and exception retention.

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
Data completeness denominators, calendar holiday cells, backfill recommendations, and analysis
exports use the Platform-owned operational trading calendar through `IOperationalTradingCalendar`.
The calendar visualization reuses its completeness service's calendar instance, including injected
policy, so holiday shading and missing-day counts cannot select different calendars. The legacy
`TradingCalendarService` is a forwarding adapter and contains no independent holiday rules.

Use this module when changing workstation endpoint behavior, operator workflow read models,
readiness projections, or UI-service orchestration consumed by browser and WPF clients.
Accounting reconciliation casework endpoints are shared workstation behavior, not client-specific UI logic. Preserve compatibility wrappers for legacy review/resolve calls while routing assign, lifecycle, taxonomy, comments, sign-off, reopen, audit, and bulk triage through shared contracts. Statement break read models are projected into shared `StatementBreakDto` records so the break queue can seed statement-originated cases without depending on infrastructure records.
Statement-run API projection ownership lives in `src/Meridian.Ui.Shared` so browser, WPF, and host-served workstation composition resolve the same source-backed adapter. The former `Meridian.Ui.Services.Services.Reconciliation.ReconciliationApiService` compatibility wrapper had no consumers and was removed; the shared implementation delegates broker/custodian CSV intake and persistence to the Financial Operations-owned statement-run workflow, then returns shared `StatementRunDto` projections with linked breaks and durable casework for browser and WPF operators. Statement-run list rows also derive review-required status, completion posture, and open exception counts from retained workflow breaks so reconciliation queues do not hide unresolved statement exceptions. Queue-status rows aggregate retained statement breaks by fund-account scope and carry source-backed account identifiers, blocker reasons, and break evidence links for shared operational dashboards. Statement-originated break rows inherit retained case owner, suggested action, SLA due/warning/breach posture, escalation label/reason, and statement-run evidence route so shared break queues seed assigned operator work instead of unowned placeholders.
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

`LiveDataService` reads the server's `TradesResponse`, `QuotesResponse` and `OrderBookResponse`
envelopes, with URLs built from `UiApiRoutes`. `GetRecentQuotesAsync` returns at most one quote,
because the server keeps only the current quote.

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
| `W12-ENTITY-001` | Effective entity lifecycle and audited book-control migration |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-UI-SERVICES -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

### Intercompany journal drafts, counterpart picking and confirmation close policy

`IntercompanyCounterpartQueueWorkspace` loads `TargetJournalChoices` from the counterpart-candidate read when the
target book changes and exposes `OpenTargetJournalCommand` through a host-supplied `JournalOpener`.
`IntercompanyBalanceConfirmationWorkspace.ClosePolicy` shows the book's confirmation close policy and proposes,
approves, rejects or disables it over `/api/ledger/intercompany/confirmation-close-policies`; an unreadable policy is
reported, never shown as none. The matching `WorkstationIntercompanyAgreementApiClient` partials check that each
response belongs to the requested proposal, book and request.

## Validation

```bash
dotnet test tests/Meridian.Ui.Tests/Meridian.Ui.Tests.csproj --filter "FullyQualifiedName~LoggingServiceBaseTests" --logger "console;verbosity=normal"
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
