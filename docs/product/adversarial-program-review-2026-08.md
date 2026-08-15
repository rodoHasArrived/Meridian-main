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

> **Meridian is now a credible single-operator workbench that is not yet a multi-operator system.**
> The product a user drives is real and increasingly honest. What is missing is everything *around*
> the user: it cannot safely host two people editing the same record (no ETag / If-Match / row-version
> anywhere in the codebase), 56 production stores are whole-file JSON guarded by an in-process
> semaphore — which caps the product at one host — it has no published or versioned API contract (1 of
> ~1,168 routes is versioned, and no route serves the OpenAPI document the code can already render),
> and the surfaces where operators do the highest-volume work have no bulk actions.

The prior reviews asked "is the number real?" That question is now mostly answered. The unasked
question is "**can a team — rather than a person — run this, integrate with it, and work in it at
volume?**" Today the answer is no, and none of the four reasons is a hard problem.

One correction to the 07-26 review's "monitoring is decorative" line, which this pass tested and found
overstated: `/metrics` serves a real Prometheus registry and `/health` is dependency-aware. The
genuine gap is narrower and is recorded as N1.

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

### N1. Distributed tracing is dead code — every span is dropped (medium)

*Scoped claim. Metrics and health are genuinely wired and are not part of this finding:*
`/metrics` serves the `prometheus-net` `DefaultRegistry` plus ten legacy hand-written series
(`src/Meridian.Application/Http/Endpoints/StatusEndpointHandlers.cs:319-343`), and `/health` returns a
dependency-aware response covering provider connectivity and storage health, with a 503 path
(`src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:28`). Counters and health work.

**Tracing does not.** `OpenTelemetrySetup` is a ~470-line layer with OTLP exporters, sampling config,
and dev/prod profiles, and eleven production call sites create spans through `MarketDataTracing`
(backfill fetch, storage, WAL recovery, pipeline processing). But **`AddOpenTelemetryTracing` and
`Initialize` have zero callers anywhere in `src/`** — no `TracerProvider` is ever registered, so every
`Activity` those eleven sites create has no listener and is discarded.

- `src/Meridian.Platform/Tracing/OpenTelemetrySetup.cs:33,116` — the two entrypoints, uncalled
- `src/Meridian.Infrastructure/Adapters/Core/Backfill/BackfillWorkerService.cs:558,609,633` — spans created into the void

**User impact:** counters tell an operator *that* backfill throughput dropped; nothing lets them follow
one slow backfill across fetch → validate → store to find *where*. For multi-stage pipelines — which is
most of what Meridian does — aggregate metrics without spans is the hard half of diagnosis missing.
The instrumentation cost is already being paid for no return.
**Improvement:** call `AddOpenTelemetryTracing` from host composition and point it at the OTLP endpoint
the config already models. **Value: medium. Effort: S** — the layer is written; this is a wiring fix.

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
| 3 | **Register the TracerProvider so the eleven existing span sites emit** | Turns aggregate counters into per-request diagnosis across pipeline stages; the layer is already written | S |
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
institutional formats. Paper fills cost money. The demo path works from a clean clone. The Prometheus
registry and dependency-aware health endpoint are real working monitoring, not the decoration an
earlier review took them for. And the browser workstation remains a mature, accessible, deeply-built
surface — its problem has never been the UI.

## Relationship to the existing tracker

The `PRD-000`…`PRD-019` production-readiness issues already name several items restated here —
`PRD-001` (fail-closed authorization and tenancy), `PRD-009` (durability under concurrency),
`PRD-015` (backup, restore, and DR), `PRD-019` (probe/scrape auth). Those rows stay authoritative;
this review adds evidence to them rather than opening a competing lane.

Findings not covered by an existing row were opened as issues:

| Finding | Issue |
| --- | --- |
| N2 — optimistic concurrency / silent lost updates | [#2694](https://github.com/rodoHasArrived/Meridian-main/issues/2694) |
| N3 — no published or versioned API contract | [#2695](https://github.com/rodoHasArrived/Meridian-main/issues/2695) |
| N1 — tracing registered nowhere | [#2696](https://github.com/rodoHasArrived/Meridian-main/issues/2696) |
| N4 — whole-file JSON persistence ceiling | [#2697](https://github.com/rodoHasArrived/Meridian-main/issues/2697) |

Two findings went to existing rows instead of new issues: N5 (bulk actions) as evidence on
`W10-RECON-002` [#2639](https://github.com/rodoHasArrived/Meridian-main/issues/2639), and the
baseline-accuracy question on `W9-GOV-008`
[#2633](https://github.com/rodoHasArrived/Meridian-main/issues/2633).

## Brainstorm — where the next unit of effort buys the most user value

*Speculative. Nothing below is evidence; it is idea generation prompted by the findings above, and
should be treated as working input rather than a proposal.*

**1. Break triage autopilot.** Breaks arrive in families — one bad FX rate makes 200 breaks, one
missed corporate action makes 40. Cluster by signature (same security, same delta shape, same
custodian, same day), then let the operator dispose of the cluster with one governed decision that
records the rule it applied. Over time the accepted rules become proposals. This turns the highest
volume work into the fastest work and produces training data for `W10-RECON-004`. Pairs naturally
with `W10-RECON-002`. **Value: very high. Effort: M.**

**2. Make "prove the number" a literal gesture.** Every figure on every screen becomes
right-clickable: source document, transform, journal line, approval, and the report it landed in —
one drawer, one path, from anywhere. `W10-PROV-001` proposes this for ledger amounts; the
generalization is what would make the brand claim demonstrable in a sales call rather than argued.
**Value: very high. Effort: M** on top of `W10-PROV-001`.

**3. Close as a burndown with a critical path.** Instead of a checklist, one number — "9 blockers,
critical path runs through the custodian statement that hasn't arrived" — with the dependency graph
behind it. Close is a scheduling problem and the product currently presents it as a list. This is
the screen a controller would keep open all day, which makes it the natural home screen.
**Value: high. Effort: M.**

**4. Governed Excel round-trip.** Ops teams will not stop using Excel; the winning move is to make
the loop safe rather than to fight it. Export a working sheet with row identities and a checksum,
let them edit it in Excel, re-import with a diff preview showing exactly which cells changed and
which need approval. This converts the product's biggest competitor into a supported input surface,
and it reuses the statement-import machinery that already exists. **Value: high. Effort: M.**

**5. A daily "what changed and what's at risk" digest.** One email per morning: new breaks by
materiality, marks going stale, approvals waiting on you, close blockers that moved. Meridian
currently requires someone to come looking; this makes it push. Cheap to build on the read models
that already exist, and it is how the product stays in a customer's routine between closes.
**Value: high. Effort: S.**

**6. Declarative connector packs as a customer-extensible surface.** The `IStatementConnector` seam
is clean and the CSV/OFX mapping profiles are already declarative. Publishing that as a documented
pack format — with a validator and a test harness — lets a customer's own analyst onboard their
custodian's format without a Meridian release. This is the highest-leverage answer to "we can't
ingest most institutional formats," because it stops requiring the vendor to write every one.
Depends on the API contract work in [#2695](https://github.com/rodoHasArrived/Meridian-main/issues/2695).
**Value: high. Effort: M–L.**

**7. Package one bounded product rather than the platform.** The 07-21 review's focus argument, put
commercially: "bank and custody reconciliation for fund administrators" is a product a buyer can
evaluate in an afternoon and a team can support. The platform underneath can stay broad; what ships,
gets documented, gets a demo script, and gets a support envelope should be one slice. Every finding
in this review is cheaper to fix inside one bounded slice than across seven workspaces.
**Value: strategic. Effort: ongoing.**
