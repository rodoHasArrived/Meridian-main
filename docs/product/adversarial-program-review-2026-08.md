# Adversarial Program Review — Meridian (2026-08)

**Status:** independent review input; not a governance or roadmap-status document
**Owner:** review author (independent adversarial pass)
**Reviewed:** 2026-08-11 at commit `01ad9aeb`
**Scope:** whole-program review of Meridian's high-level functionality, focused on end-user value
**Method:** source-evidence audit of the wired code paths. This checkout has no .NET SDK, so **no
finding below was confirmed at runtime** — every claim is anchored to `file:line` and is a
static-evidence claim. Where a runtime check would change the conclusion, that is stated inline.
This pass deliberately re-tests the [2026-07-21](adversarial-program-review-2026-07.md) and
[2026-07-26](../../archive/docs/assessments/adversarial-program-review-2026-07-26.md) reviews before
adding new findings, so remediated items are credited rather than re-litigated.

## Headline

The two prior reviews found, in order, "built but not wired" and "a broken first mile and an
unsupported last mile." Sixteen days on, **the first mile is genuinely fixed** — the workstation
bundle is committed, the demo seeds six subsystems, paper fills cost money, and two institutional
statement formats landed. The theme has moved again:

> **Meridian is now a credible single-operator workbench that cannot yet be operated as a system.**
> The product a user drives is real and increasingly honest. What is missing is everything *around*
> the user: it emits no telemetry (the OpenTelemetry layer has zero callers), it has no published or
> versioned API contract (1 of ~1,168 routes is versioned, and no route serves the OpenAPI document
> the code can already render), it cannot safely host two people editing the same record (no ETag /
> If-Match / row-version anywhere in the codebase), and 56 production stores are whole-file JSON
> guarded by an in-process semaphore, which caps the product at one host.

The prior reviews asked "is the number real?" That question is now mostly answered. The unasked
question is "**can anyone run, integrate with, or staff this thing?**" — and today the answer is no.

## Scorecard: what moved since 2026-07-26

| 07-26 finding | Status at `01ad9aeb` | Evidence |
| --- | --- | --- |
| Fresh-clone quickstart fails — `wwwroot/` not checked in | **Fixed.** The workstation bundle is tracked (112 files) and `.gitignore` now names it the canonical tracked bundle | `git ls-files src/Meridian.Ui/wwwroot/`; `.gitignore:276` |
| Demo seeds only 2 of 7 nav areas | **Largely fixed.** Seeds reconciliation breaks, a strategy run, fund accounts, position snapshots, journal drafts, report packs, and durable market history | `src/Meridian.Ui.Shared/Services/DemoWorkspaceSeeder.cs:63-122` |
| Paper fills instant, complete, cost-free | **Fixed on cost.** `PaperTradingCostModel` applies commission, fees, and slippage to every paper fill | `src/Meridian.Execution/Adapters/PaperTradingGateway.cs:22,444-458` |
| No institutional statement formats (camt.053, BAI2) | **Fixed.** Both connectors now exist alongside CSV/OFX/IB-Flex/Alpaca | `.../Connectors/Camt/Camt053StatementConnector.cs`, `.../Connectors/Bai2/Bai2StatementConnector.cs` |
| RBAC non-uniform across routes | **Partial, and now measured.** A mechanical sweep enumerates every mutating route and fails on new unguarded ones; 40 routes guarded, baseline ratcheted 152 → 112 | `tests/.../EndpointAuthorizationCoverageTests.cs`; commit `0f9e40d8` |
| Tenancy reads fail open | **Open.** A tenantless caller still gets no predicate, so every row passes | `src/Meridian.Contracts/Tenancy/TenantReadPredicate.cs:33` |
| Journal ledger has no hash chain | **Open.** No hash reference of any kind in the Postgres journal store | `src/Meridian.Storage/Ledger/PostgresLedgerJournalStore.cs` |
| Money-path stores silently fall back to in-memory | **Open.** Banking, money-market, and direct-lending all take an unannounced in-memory `else` branch | `src/Meridian.Application/Composition/Features/StorageFeatureRegistration.cs:539-563,576-578` |
| Reconciliation never sees the ledger side | **Open by design.** Ledger-transaction population is intentionally empty and fails closed to breaks | `src/Meridian.Application/Reconciliation/RetainedInternalReconciliationPopulationProvider.cs` |

The remediation stream is real and is hitting the things prior reviews named. The pattern worth
noting: **every fixed row was a bounded, in-product defect; every still-open row is a systemic or
architectural posture.** The team is very good at closing the first kind and has not yet started on
the second.

## New findings

### N1. The observability layer emits nothing (high — blocks operating the product)

`OpenTelemetrySetup` is a ~470-line tracing and metrics layer with OTLP exporters, sampling config,
and dev/prod profiles. Eleven production call sites create spans through `MarketDataTracing`
(backfill fetch, storage, WAL recovery, pipeline processing).

**`AddOpenTelemetryTracing` and `Initialize` have zero callers anywhere in `src/`.** No
`TracerProvider` or `MeterProvider` is ever registered, so every `Activity` those eleven sites create
has no listener and is dropped. The instrumentation cost is paid; nothing is ever exported.

- `src/Meridian.Platform/Tracing/OpenTelemetrySetup.cs:33,116` — the two entrypoints, uncalled
- `src/Meridian.Infrastructure/Adapters/Core/Backfill/BackfillWorkerService.cs:558,609,633` — spans created into the void

There is also no `AddHealthChecks`/`MapHealthChecks` registration; `/health` and `/healthz` are
hand-rolled handlers (`src/Meridian.Application/Composition/HostAdapters.cs:60,71`).

**User impact:** when a backfill stalls, a reconciliation run hangs, or a report pack times out, there
is no trace, no metric, and no dependency-aware health signal to diagnose it with. Nobody can operate
this in production, and nobody can answer "why was it slow yesterday."
**Improvement:** call `AddOpenTelemetryTracing` from host composition, export the pipeline/backfill/
storage meters that already exist, and back `/health` with real dependency probes. **Value: high.
Effort: S** — the layer is already written; this is a wiring fix, the same shape as the 07-21 review's
highest-ROI items.

### N2. No optimistic concurrency — two operators silently overwrite each other (high)

The codebase contains **zero** occurrences of `ETag`, `If-Match`, `RowVersion`, `ConcurrencyToken`, or
`xmin`. Version-checked writes exist in exactly two subsystems:

- `src/Meridian.Reporting/ReportingGovernanceService.cs:112,137` (`expectedVersion`)
- `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.Casework.cs:999`

Everywhere else — journal entries and drafts, fund structure, capital accounts, close checklists,
report packs outside the governance path, statement mapping profiles — a write is a blind overwrite.

**User impact:** this is a product sold on approvals, evidence, and four-eyes control. Two accountants
with the same close checklist open, or a preparer and a reviewer on the same journal draft, produce a
silent lost update with **no conflict, no warning, and an audit trail that records both edits as
successful**. That is a worse failure than a wrong number, because the evidence chain says it was fine.
**Improvement:** add a version column and `If-Match`/`expectedVersion` enforcement to every mutable
money-path aggregate, returning 409 with the current state so the UI can offer a merge. The pattern
is already proven in `ReportingGovernanceService` — generalize it. **Value: high. Effort: M.**

### N3. No published or versioned API contract (high — blocks the integration wedge)

Meridian maps roughly **1,168 routes across 107 endpoint files**. Exactly **one** is versioned:
`app.MapGroup("/api/v1/risk")` (`src/Meridian.Ui.Shared/Endpoints/RiskEndpoints.cs:80`). Everything
else is unversioned `/api/<family>`.

`ApiDocumentationService` can already render an OpenAPI spec and Swagger UI
(`src/Meridian.Platform/ApiDocumentation/ApiDocumentationService.cs:49-51`) and is DI-registered
(`DiagnosticsFeatureRegistration.cs:81`) — but **no route serves `/api/openapi.json`**, and there is
no `AddOpenApi`/`MapOpenApi` call in the repository.

There is a strong *internal* contract: 857 route constants in `src/Meridian.Contracts/Api/UiApiRoutes.cs`
mirrored into `src/Meridian.Ui/dashboard/src/lib/ui-api-routes.generated.ts`. That keeps the two
first-party clients honest; it does nothing for anyone outside the repo.

**User impact:** the stated wedge is a control tower that "sits above spreadsheets, custodians,
brokers, administrators, portfolio systems, general ledgers, banks, and document stores." Every one of
those integrations is someone else's system calling Meridian or Meridian calling out on a stable
contract. Today a customer's BI tool, GL connector, or auditor's extract script has no documented,
discoverable, or version-stable surface to bind to — and any refactor silently breaks them.
**Improvement:** serve the OpenAPI document the code already generates, freeze a `/api/v1` prefix for
the routes external parties need first (ledger reads, report-pack exports, reconciliation status), and
publish a deprecation policy. **Value: high. Effort: M.**

### N4. Whole-file JSON persistence caps the product at one host (high)

**56 production classes** are file-backed JSON/JSONL stores. The reconciliation break queue — core to
the product's wedge — is representative: mutations serialize on an in-process `SemaphoreSlim` and
rewrite the whole file atomically.

- `src/Meridian.Strategies/Services/FileReconciliationBreakQueueRepository.cs:25,48,97` — `SemaphoreSlim(1,1)`, `AtomicFileWriter.WriteAsync`
- Other examples: `FileStatementMappingProfileStore`, `FileGovernanceReportPackRepository`, `FileManualJournalEntryDraftStore`, `RolePermissionProfileStore`, `FileOperationalCaseHistoryStore`

Three consequences follow directly:

1. **Single-process only.** The semaphore is in-process; a second host instance or a maintenance CLI
   writing concurrently corrupts state. This forecloses horizontal scale, rolling restarts, and any
   HA posture.
2. **O(n) write amplification.** Every break comment rewrites the entire break file. A queue with tens
   of thousands of breaks — a normal month for a mid-size fund administrator — degrades on each edit.
3. **No cross-store transaction.** A workflow touching breaks, journals, and report packs cannot commit
   atomically, so a crash mid-workflow leaves inconsistent state that the audit trail will not flag.

This is also the layer the demo and evaluation path runs on, so an evaluator's smooth experience does
not predict production behavior at volume.
**Improvement:** move the money-path and casework aggregates behind the Postgres stores that already
exist for ledger/banking/security-master, and keep file stores for genuinely single-writer local state.
**Value: high. Effort: L.**

### N5. Bulk operations are missing where the work is highest-volume (medium)

Across 353 `.tsx` files, only **8** reference multi-select or bulk-action patterns; 28 reference
keyboard handling. The command palette is 72 lines
(`src/Meridian.Ui/dashboard/src/app-shell.command-palette.ts`).

**User impact:** reconciliation and close work is inherently batch-shaped — "accept these 200 sub-cent
FX breaks," "assign this custodian's 80 breaks to Priya," "re-run these 12 report packs." A
one-at-a-time UI turns a 10-minute task into an afternoon, and it is the single most common reason ops
teams keep the spreadsheet they were told to give up. This is the highest ratio of user-value to
engineering effort in this review.
**Improvement:** add selection state + a bulk-action bar to the break queue, close checklist, and
journal-draft grids, backed by batch endpoints that record one approval event per batch.
**Value: high. Effort: M.**

### N6. Maintainability hazards have not improved (medium)

The 07-21 review flagged 4,000–7,400-line files. At this commit the largest are:

| File | Lines |
| --- | --- |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.tsx` | 7,397 |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.view-model.ts` | 7,147 |
| `src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts` | 6,673 |
| `src/Meridian.Ui/dashboard/src/screens/accounting-screen.tsx` | 6,126 |
| `src/Meridian.Wpf/ViewModels/Accounting/AccountingConfigureViewModel.cs` | 5,357 |

The built workstation is 4.2 MB, with a 465 KB entry chunk and a 431 KB accounting chunk. A god-file
ratchet exists in CI, which stops regression but has not driven reduction.

## Still-open structural items (unchanged, restated with current evidence)

- **Tenancy reads fail open.** `TenantReadPredicate.ShouldFilter` returns `false` for a tenantless
  caller, and the documented semantic is "no predicate at all, so every row passes"
  (`src/Meridian.Contracts/Tenancy/TenantReadPredicate.cs:26-34`). Defensible for one-company
  deployments; disqualifying for any shared one.
- **112 mutating routes still process a permissionless caller**, per the repo's own frozen baseline
  (`tests/.../EndpointAuthorizationCoverageTests.cs:53-167`), including
  `POST /api/auth/accounts/{username}/password-reset`, `POST /api/auth/accounts/{username}/disable`,
  and the whole `POST /api/fund-structure/*` family. *Caveat: this baseline was captured by a run this
  checkout cannot reproduce, and at least one listed route — `POST /api/execution/orders/submit` — has
  a permission check as its first statement (`ExecutionEndpoints.cs:131`). The baseline should be
  re-derived and pruned; the count may be pessimistic.*
- **No hash chain on the authoritative journal**, so the ledger that the whole "prove the number"
  promise rests on is not tamper-evident.
- **Money-path stores fall back to in-memory without announcement**, so ledger/banking/MMF state
  evaporates on restart in any deployment that has not set the connection-string environment variables.
- **Reconciliation still cannot see the ledger side.** Ledger-transaction population is deliberately
  empty and fails closed, so **every transaction line on an imported statement becomes a break**. The
  code's reasoning is sound (a wrong projection is worse than none), but the user-visible result is that
  cash and transaction reconciliation — the reason to buy this — does not yet function end to end.

## Prioritized improvement list (by end-user value uplift)

| # | Improvement | Why it is high-value to the end user | Effort |
| --- | --- | --- | --- |
| 1 | **Agree the journal→transaction projection and populate the ledger side of reconciliation** | Today every transaction line is a break; this is the wedge and it does not close | L |
| 2 | **Bulk selection + batch actions on break queue, close checklist, journal drafts** | Highest value-to-effort ratio in the review; it is why teams keep the spreadsheet | M |
| 3 | **Wire OpenTelemetry; back `/health` with dependency probes** | The product becomes operable and diagnosable; the layer is already written | S |
| 4 | **Optimistic concurrency on every money-path aggregate** | Stops silent lost updates that the audit trail records as success | M |
| 5 | **Serve OpenAPI; freeze a `/api/v1` surface for ledger, exports, reconciliation status** | Unlocks the integration story the wedge depends on | M |
| 6 | **Move casework and money-path aggregates off whole-file JSON** | Removes the single-host ceiling and the O(n) write cliff at real volumes | L |
| 7 | **Fail-closed tenancy, hash-chained journal, finish the RBAC ratchet, announce in-memory fallback** | Makes the governance brand real rather than asserted | L |
| 8 | **Re-derive the unguarded-route baseline; prune false entries** | The ratchet is the right mechanism; it needs an accurate starting number | S |

Items 3, 5, and 8 are small and unblock disproportionate value. Item 2 is the one a user would notice
within an hour of real work.

## What is genuinely strong (so fixes do not regress it)

The remediation discipline is real and fast — every prior-review row that was a bounded defect got
closed inside three weeks. The mechanical authorization sweep is a genuinely good governance
primitive: it makes a whole class of regression impossible by construction rather than by review. The
shared route-constant contract keeps two clients honest without hand-sync. The reconciliation
population provider's decision to fail closed rather than fabricate matches, and to *document why in
the source*, is exactly the judgement this product needs. Ingestion breadth now covers the
institutional formats. Paper fills cost money. The demo path works from a clean clone. And the browser
workstation remains a mature, accessible, deeply-built surface — its problem has never been the UI.
