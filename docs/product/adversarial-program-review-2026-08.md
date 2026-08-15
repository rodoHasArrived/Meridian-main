# Adversarial Program Review — Meridian (2026-08)

**Status:** independent review input; not a governance or roadmap-status document
**Owner:** review author (independent adversarial pass)
**Reviewed:** 2026-08-11 at commit `01ad9aeb`; corrected 2026-08-15 after review of PR #2698
**Scope:** whole-program review of Meridian's high-level functionality, focused on end-user value
**Method:** source-evidence audit of the wired code paths. This checkout has no .NET SDK, so **no
finding below was confirmed at runtime** — every claim is anchored to `file:line` and is a
static-evidence claim. Where a runtime check would change the conclusion, that is stated inline.
This pass deliberately re-tests the [2026-07-21](adversarial-program-review-2026-07.md) and
[2026-07-26](../../archive/docs/assessments/adversarial-program-review-2026-07-26.md) reviews before
adding new findings, so remediated items are credited rather than re-litigated.

> **Corrected 2026-08-15 after automated review of PR #2698.** Seven claims in the first draft were
> checked against source and found wrong or overstated; all seven are corrected below, and the
> sections they touched are rewritten rather than annotated. The errors shared two causes worth
> recording, because they are the failure modes of this review method:
>
> 1. **Truncated searches read as exhaustive.** The claim "version-checked writes exist in exactly two
>    subsystems" came from a `grep … | head -10`. The real count is 366 occurrences across dozens of
>    files, including a dedicated `SecurityMasterConcurrencyException`.
> 2. **Absence of one idiom read as absence of the capability.** The claim "no route serves the OpenAPI
>    document" came from searching for `AddOpenApi`/`MapOpenApi`/`openapi.json` — the .NET 9+ built-in
>    API. Meridian uses Swashbuckle, and serves `/swagger/v1/swagger.json`.
>
> A negative finding in a codebase this size needs a positive search for the thing that would refute it.
> Where a corrected finding survives at reduced scope, that is stated; where it does not survive, it is
> struck. The individual `file:line` anchors were captured at `01ad9aeb` and may have drifted.

## Headline

The two prior reviews found, in order, "built but not wired" and "a broken first mile and an
unsupported last mile." Sixteen days on, **the first mile is genuinely fixed** — the workstation
bundle is committed, the demo seeds six subsystems, paper fills cost money, and two institutional
statement formats landed. The theme has moved again:

> **Meridian's remaining gap is mostly the last mile of things already built.** After correction,
> this review found very little that is missing outright. It found capability that is present but
> unreachable, uneven, or unversioned: bulk reconciliation casework implemented end-to-end and called
> by no screen; a tracing layer with eleven instrumented sites and no registered provider; concurrency
> control implemented per-service but with no uniform HTTP-level contract and at least one aggregate
> with no guard at all; a served Swagger document over a route surface where 1 of ~1,168 routes is
> versioned.

That is a materially more favourable read than the first draft, and the correction is the finding:
**this codebase is consistently better than a survey of it suggests**, which is the same trap the
2026-07-21 review named as "built but not wired" and which this review fell into from the other side.
An adverse pass that greps for absence will systematically under-credit a codebase where the
capability exists under a different name, in a different layer, or behind one unwired call site.

Two corrections to prior reviews, both of which this pass tested and found overstated:

- The 07-26 review's "monitoring is decorative": `/metrics` serves a real Prometheus registry and
  `/health` is dependency-aware. Only tracing is unwired (N1).
- The 07-21 and 07-26 reviews' "silent in-memory fallback": the host now reports
  `PERSISTENCE: NONE — every money-path store is in-memory and loses data on restart` at readiness, and
  forces a non-real provenance label. The durability limitation is real; the silence is not.

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

### N2. Concurrency control is real but uneven, and has no HTTP-level contract (medium)

**Corrected.** The first draft claimed version-checked writes existed in "exactly two subsystems."
That was wrong — it came from a truncated search. `expectedVersion` appears **366 times** across
`src/`, including `PostgresLedgerJournalStore`, `PostgresDirectLendingStateStore`,
`PostgresSecurityMasterEventStore`, `OperationsContinuityWorkflowService`, and
`ScopedAccessAssignmentStore`, and there is a dedicated `SecurityMasterConcurrencyException`. The
money paths named in the first draft as unguarded are largely guarded.

What survives is narrower and still worth fixing:

**Coverage is uneven.** Spot-checking mutable aggregates for any version reference:

| Aggregate | Version references |
| --- | ---: |
| `InMemoryFundStructureService` | 24 |
| `FileGovernanceReportPackRepository` | 11 |
| `FileStatementMappingProfileStore` | 6 |
| `FileManualJournalEntryDraftStore` | **0** |

Manual journal drafts — a preparer/reviewer surface by definition — appear to have no guard.
A per-aggregate inventory would establish whether that is the only gap; this review did not do one,
and the corrected claim should not be generalized beyond it.

**There is no HTTP-level concurrency contract.** `ETag`, `If-Match`, and `RowVersion` genuinely do not
appear anywhere in `src/`. Concurrency is enforced per-service by callers passing `expectedVersion`,
which means it is correct where a caller remembered and absent where one did not, and a browser or
WPF client has no uniform way to detect a conflict and offer a merge.

**Improvement:** inventory mutable aggregates for guard coverage and close the gaps (journal drafts
first); then surface the version that already exists as an `ETag` and accept `If-Match`, returning 409
with current state. This is mostly exposing an existing mechanism at the transport layer, not building
one. **Value: medium. Effort: M.**

### N3. The API surface is served and documented, but unversioned (medium)

**Corrected.** The first draft claimed no route serves an OpenAPI document. That was wrong: the host
registers a v1 Swagger document via `AddSwaggerGen` (`src/Meridian/UiServer.cs:428-442`) and serves it
through `UseSwagger()`/`UseSwaggerUI()` at `/swagger/v1/swagger.json`
(`UiServer.cs:566-573`); `docs/reference/api-reference.md:116` advertises `/swagger`. The search that
produced the error looked for `AddOpenApi`/`MapOpenApi`/`openapi.json` — the .NET 9+ built-in idiom —
and missed Swashbuckle. A discoverable contract exists.

What survives is the versioning half:

Meridian maps roughly **1,168 routes across 107 endpoint files**. Exactly **one** is versioned:
`app.MapGroup("/api/v1/risk")` (`src/Meridian.Ui.Shared/Endpoints/RiskEndpoints.cs:80`). Everything
else is unversioned `/api/<family>`. The Swagger document is titled "v1" but describes a route surface
that carries no version in its paths, so the document's version and the routes' stability are
unrelated.

There is also a second, unused renderer — `ApiDocumentationService`
(`src/Meridian.Platform/ApiDocumentation/ApiDocumentationService.cs:49-51`), DI-registered at
`DiagnosticsFeatureRegistration.cs:81` — which generates its own spec and Swagger UI page and is
reachable from no route. Two documentation paths where one is served is a smaller version of the same
built-but-unwired pattern.

There is a strong *internal* contract: 857 route constants in `src/Meridian.Contracts/Api/UiApiRoutes.cs`
mirrored into `src/Meridian.Ui/dashboard/src/lib/ui-api-routes.generated.ts`. That keeps the two
first-party clients honest; it does nothing for anyone outside the repo.

**User impact:** an integrator can discover the surface, which is the larger half of the problem and
is solved. What they cannot get is a stability promise. A customer's BI tool, GL connector, or
auditor's extract script binds to paths that carry no version, so any refactor breaks them with no
deprecation window and no signal — and the team has no way to make a breaking change deliberately.
**Improvement:** decide which routes are external (ledger reads, report-pack exports, reconciliation
status are the plausible first set), move them under `/api/v1/*` with the current paths kept as
redirects, mark the boundary in `UiApiRoutes.cs`, and publish a compatibility policy. Separately,
delete or serve the unused `ApiDocumentationService`. **Value: medium. Effort: M.**

### N4. File-backed stores scale by rewrite, and their locking is inconsistent (medium)

**Corrected.** The first draft claimed the representative store was protected only by an in-process
semaphore and that a second process "necessarily corrupts" it. That was wrong: every mutation also
acquires a cross-process lease — a `FileStream` opened with `FileShare.None`
(`FileReconciliationBreakQueueRepository.Persistence.cs:248-267`) — and then reloads state before
writing (`FileReconciliationBreakQueueRepository.cs:226-231,524-529`). That is a genuine
multi-process guard, and reading `SemaphoreSlim` without reading the persistence partial is what
produced the error.

**56 production classes** are file-backed JSON/JSONL stores. Two concerns survive, both narrower:

1. **O(n) write amplification.** Every mutation re-serializes the whole collection. A break queue
   holding tens of thousands of breaks — an ordinary month for a mid-size administrator —
   re-serializes all of them on each edit, so cost grows with the data an engaged customer
   accumulates.
2. **No cross-store transaction.** A workflow touching breaks, journals, and report packs commits to
   three files independently; a crash between them leaves inconsistent state the audit trail cannot
   flag.

**The locking is also not uniform.** The break queue's lease is the good case; whether the other 55
stores have an equivalent is unverified, and that inconsistency is the actionable part. A store with
no lease has the multi-process hazard the first draft wrongly attributed to the whole layer.

**Improvement:** audit the file stores for lease coverage and standardize on the break queue's pattern;
separately, move the highest-growth collections behind the Postgres stores that already exist.
**Value: medium. Effort: M** for the lease audit, **L** for migration.

### N5. Bulk reconciliation casework is built end-to-end and reachable from no screen (high)

**Corrected in scope, and the correction makes this cheaper, not smaller.** The first draft treated
bulk actions as missing and recommended building batch endpoints. The backend already exists:

- `src/Meridian.Contracts/Api/UiApiRoutes.cs:891-894` — bulk dry-run, execute, status, and result routes
- `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs:2158-2209` — all four mapped
- `src/Meridian.Ui/dashboard/src/lib/api.ts:3209-3217` — browser client functions

The repo's own W10 assessment already records this precisely: bulk casework is "implemented end to end
and unwired… contracts with an idempotency key and a bounded case count, a repository implementation
with dry-run and retained receipts, a mapped endpoint, and browser client functions that no screen
calls" (`w10-depth-slate-2026-07.md:255-259`).

The UI-side measurement stands: across 353 `.tsx` files only **8** reference multi-select or
bulk-action patterns.

**User impact:** unchanged and still the highest value-to-effort item in this review. Reconciliation
and close work is batch-shaped — "accept these 200 sub-cent FX breaks", "assign this custodian's 80
breaks to one owner" — and a one-at-a-time UI is the most common reason ops teams keep the spreadsheet
they were told to replace. What changed is the cost: this is selection state and a bulk-action bar
wired to retained, idempotent, dry-run-capable rails that already exist and are already tested.
**Improvement:** wire the break-queue grid to the existing bulk routes, then extend the same selection
pattern to the close checklist and journal drafts. Keep the existing one-receipt-per-batch semantics so
a bulk sweep stays distinguishable from N reviewed decisions in the audit trail.
**Value: high. Effort: S–M.**

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
- **The 112-route unguarded baseline measures the wrong thing and should not be read as security
  posture.** *Corrected:* the first draft cited `POST /api/auth/accounts/{username}/password-reset`
  and `.../disable` as routes a permissionless caller can execute. Both are guarded — they call
  `ResolveManageUsersActor` and return 401/403 (`AuthEndpoints.cs:267-275,327-335`). They appear in
  the baseline because the sweep posts `{}`, the handler rejects the body/route username mismatch with
  a **400** before reaching the permission check, and the test counts any non-401/403 as unguarded.
  The same artifact explains `POST /api/execution/orders/submit`, whose first statement is a
  permission check (`ExecutionEndpoints.cs:131`). The ratchet is a good primitive measuring badly: it
  needs valid request bodies, and the count re-derived, before any number is quoted.
- **No hash chain on the authoritative journal**, so the ledger that the whole "prove the number"
  promise rests on is not tamper-evident.
- **Money-path in-memory fallback is a durability limit, not a truthfulness one.** *Corrected:* the
  first draft called it unannounced. It is announced — the host forces a non-real provenance label
  (`UiServer.cs:516-519`), `ProductionRegistrationGuardService.cs:30-43` refuses an unlabeled local
  graph where durability is required, and readiness reports `PERSISTENCE: PARTIAL` or
  `PERSISTENCE: NONE — every money-path store is in-memory and loses data on restart`
  (`UiServer.cs:968-988`). Production posture fails readiness rather than proceeding quietly. State
  still evaporates on restart in that mode; the operator is told.
- **Reconciliation covers cash and positions; transaction matching is the gap.** *Corrected:* the
  first draft said cash and transaction reconciliation "does not function end to end." Cash and
  positions are populated (`RetainedInternalReconciliationPopulationProvider.cs:86-89`, covered by
  `RetainedInternalReconciliationPopulationProviderTests.cs:22-51`) and `StatementMatchingEngine.cs:111-163`
  matches cash within tolerance. Only the ledger-transaction population is deliberately empty, so
  **imported transaction rows become unmatched breaks** while balances and positions reconcile against
  the retained book. The remaining work is the journal→transaction projection, which is a bounded
  modeling decision rather than the whole wedge.

## Prioritized improvement list (by end-user value uplift)

Reordered after correction. Three of the top four are now **wiring work on things already built**,
which is both the cheapest and the most reliable kind of change.

| # | Improvement | Why it is high-value to the end user | Effort |
| --- | --- | --- | --- |
| 1 | **Wire the break-queue grid to the bulk rails that already exist** | Highest value-to-effort item in the review; batch work is why teams keep the spreadsheet, and the idempotent dry-run backend is built and tested | S–M |
| 2 | **Agree the journal→transaction projection so imported transactions can match** | Balances and positions already reconcile; transaction rows all become breaks. Bounded modeling decision, not a rebuild | L |
| 3 | **Fix the authorization sweep to post valid bodies, then re-derive the count** | The ratchet is a good primitive currently producing a number nobody should quote; today it conflates 400-on-bad-body with unguarded | S |
| 4 | **Register the TracerProvider so the eleven existing span sites emit** | Turns aggregate counters into per-request diagnosis across pipeline stages; the layer is written | S |
| 5 | **Close the concurrency-guard gaps (journal drafts first), then expose versions as `ETag`/`If-Match`** | Most money paths are already guarded; this makes coverage uniform and gives clients a conflict contract | M |
| 6 | **Freeze a `/api/v1` surface for the routes external systems bind to** | The contract is already discoverable via Swagger; what is missing is a stability promise | M |
| 7 | **Audit file stores for cross-process lease coverage; migrate the highest-growth collections** | The break queue's lease pattern is correct — standardize it, and remove the O(n) rewrite cliff | M–L |
| 8 | **Fail-closed tenancy and a hash-chained journal** | The two governance claims the brand rests on that are genuinely not yet true | L |

Items 1, 3, and 4 are small and unblock disproportionate value. Item 1 is the one a user would notice
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

Four more, all of which this review initially got wrong and which deserve explicit credit: the
in-memory fallback is **announced**, loudly, at readiness and through forced provenance labelling —
the truth-discipline work of `W9-TRUTH-001` did land. The break queue's file store holds a real
cross-process lease, not just an in-process gate. `expectedVersion` concurrency control is applied
across the ledger, direct-lending, security-master, operations-continuity, and scoped-access stores,
with its own exception type. And the reconciliation path genuinely matches cash within tolerance and
positions against the retained book — the deliberate gap is transaction rows alone.

## Relationship to the existing tracker

The `PRD-000`…`PRD-019` production-readiness issues already name several items restated here —
`PRD-001` (fail-closed authorization and tenancy), `PRD-009` (durability under concurrency),
`PRD-015` (backup, restore, and DR), `PRD-019` (probe/scrape auth). Those rows stay authoritative;
this review adds evidence to them rather than opening a competing lane.

Findings not covered by an existing row were opened as issues. Three were opened against the first
draft's claims and have been corrected in place, so read the issue rather than the original title:

| Finding | Issue | Post-correction state |
| --- | --- | --- |
| N1 — tracing registered nowhere | [#2696](https://github.com/rodoHasArrived/Meridian-main/issues/2696) | Unchanged; the only new finding that survived review intact |
| N2 — concurrency coverage uneven, no HTTP contract | [#2694](https://github.com/rodoHasArrived/Meridian-main/issues/2694) | Rewritten — the "exactly two subsystems" premise was false |
| N3 — unversioned route surface | [#2695](https://github.com/rodoHasArrived/Meridian-main/issues/2695) | Rewritten — Swagger *is* served; only versioning survives |
| N4 — rewrite cost and inconsistent lease coverage | [#2697](https://github.com/rodoHasArrived/Meridian-main/issues/2697) | Rewritten — the representative store has a cross-process lease |

Two findings went to existing rows instead of new issues: N5 (bulk actions) as evidence on
`W10-RECON-002` [#2639](https://github.com/rodoHasArrived/Meridian-main/issues/2639), and the
baseline-accuracy question on `W9-GOV-008`
[#2633](https://github.com/rodoHasArrived/Meridian-main/issues/2633). Both carry follow-up comments
correcting the same errors.

**On the review method itself.** One of five new findings survived unchanged; the automated review of
PR #2698 caught the rest. That ratio is the most useful output of this pass. A source-evidence review
run without the ability to build or execute the system will over-produce absence claims, because
absence is the one thing a search can appear to prove and cannot. The mitigation is not more searching
— it is requiring, for every negative finding, a positive search for the mechanism that would refute
it, named in whatever idiom the codebase actually uses.

## Brainstorm — where the next unit of effort buys the most user value

*Speculative. Nothing below is evidence; it is idea generation prompted by the findings above, and
should be treated as working input rather than a proposal.*

**1. Break triage autopilot.** Breaks arrive in families — one bad FX rate makes 200 breaks, one
missed corporate action makes 40. Cluster by signature (same security, same delta shape, same
custodian, same day), then let the operator dispose of the cluster with one governed decision that
records the rule it applied. Over time the accepted rules become proposals. Cheaper than it looks:
the bulk execute/dry-run/receipt rails already exist and are idempotent (N5), so this is a clustering
function plus a screen, not a new backend. Produces training data for `W10-RECON-004`; pairs with
`W10-RECON-002`. **Value: very high. Effort: M.**

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
