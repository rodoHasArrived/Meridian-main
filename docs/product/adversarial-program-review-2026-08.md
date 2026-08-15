# Adversarial Program Review — Meridian (2026-08)

**Status:** independent review input; not a governance or roadmap-status document
**Owner:** review author (independent adversarial pass)
**Reviewed:** 2026-08-11 at commit `01ad9aeb`; corrected 2026-08-15 across seven review rounds on PR #2698
**Scope:** whole-program review of Meridian's high-level functionality, focused on end-user value
**Method:** source-evidence audit of the wired code paths. This checkout has no .NET SDK, so **no
finding below was confirmed at runtime** — every claim is anchored to `file:line` and is a
static-evidence claim. Where a runtime check would change the conclusion, that is stated inline.
This pass deliberately re-tests the [2026-07-21](adversarial-program-review-2026-07.md) and
[2026-07-26](../../archive/docs/assessments/adversarial-program-review-2026-07-26.md) reviews before
adding new findings, so remediated items are credited rather than re-litigated.

> **Corrected seven times, 2026-08-15, across seven rounds of automated review on PR #2698.**
> **Twenty-five** claims were checked against source and found wrong, overstated, unsupported, or
> internally inconsistent — seven in the first draft, then four, four, one (partly), three, four, and
> two across the corrections. All are rewritten below rather than annotated.
>
> **Read this document accordingly.** Its surviving findings are modest and heavily qualified. Its most
> reliable content is the record of how a source-only review fails, below. If you want to know what to
> fix in Meridian, the issues linked at the end are better maintained than the narrative here.
>
> Six failure modes, recorded because they are what this review method does wrong:
>
> 1. **Truncated searches read as exhaustive.** "Version-checked writes exist in exactly two
>    subsystems" came from a `grep … | head -10`. The real count is 366.
> 2. **Absence of one idiom read as absence of the capability.** "No route serves the OpenAPI document"
>    came from searching for `AddOpenApi`/`MapOpenApi` — the .NET 9+ built-in. Meridian uses
>    Swashbuckle and serves `/swagger/v1/swagger.json`.
> 3. **A scenario asserted without being found.** The "crash mid-workflow across breaks, journals, and
>    report packs" hazard was constructed from the shape of the storage layer, not observed. No such
>    write path exists.
>
> 4. **Counting the wrong unit.** "Eleven call sites create spans" counted symbol occurrences, not
>    span-producing calls (seven). "Exactly one route is versioned" counted a `MapGroup` call, not
>    routes (seven), against a denominator counting `Map*` call sites. Both were quoted in the headline.
>
> 5. **Corrections left the document internally inconsistent.** Round five found a heading still
>    asserting "no HTTP-level contract" after the body retracted it, and a priority-table effort
>    estimate still at `S–M` after the detailed finding raised it to `M`. Round six found the product
>    index still summarizing the first draft, and a "every still-open row is systemic" claim the
>    document's own findings contradict. A reader skimming headings, tables, and index entries would
>    have taken away claims the body had retracted.
> 6. **Asserting no-change without measuring change.** "Maintainability hazards have not improved" was
>    stated against a baseline that shows every named file shrinking. The real finding was the *rate*
>    (−0.5% to −3.6% in three weeks), which is a more useful claim and required the same measurement
>    the draft skipped.
>
> **The same failure mode recurred at round seven.** Failure mode 2 — asserting absence without
> searching — was documented after round one and then produced two more errors six rounds later:
> "nothing enforces the single-writer constraint" (three enforcement mechanisms exist) and "the ratchet
> needs step targets" (a burn-down plan with those exact targets already existed, and an open issue
> #2675 already covers the ratchet mechanism). Seven rounds of documenting a bias did not stop it. The
> only thing that has reliably caught it is someone else running the counter-search.
>
> Worth stating plainly: **the second round of errors repeated the first.** After documenting
> "truncated searches read as exhaustive" as a root cause, the correction then counted version
> references inside `FileManualJournalEntryDraftStore`, found zero, and reported journal drafts as
> unguarded — while the guard sat in `ManualJournalEntryWorkbenchService`. Naming a bias is not the
> same as removing it; the counter-search has to actually be run, per claim, at the layer where the
> mechanism would live.
>
> Where a corrected finding survives at reduced scope, that is stated; where it does not survive, it is
> withdrawn. The `file:line` anchors were captured at `01ad9aeb` and may have drifted.

## Headline

The two prior reviews found, in order, "built but not wired" and "a broken first mile and an
unsupported last mile." Sixteen days on, **the first mile is genuinely fixed** — the workstation
bundle is committed, the demo seeds six subsystems, paper fills cost money, and two institutional
statement formats landed. The theme has moved again:

> **Meridian's remaining gap is mostly the last mile of things already built.** After seven rounds of
> correction, this review found almost nothing missing outright. What it found is capability that is
> present but unreachable or inconsistent: bulk reconciliation casework implemented end-to-end and
> called by no screen; a tracing layer with seven span-producing sites and no registered provider;
> working concurrency control whose request/response shape differs per route family; a served Swagger
> document over a route surface where a single seven-route group is versioned, and it mirrors an
> unversioned twin.

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
| Money-path stores fall back to in-memory | **Corrected — announced, not silent.** Forced non-real provenance label, a registration guard, and a `PERSISTENCE: NONE` readiness line. Durability limit, not a truthfulness gap | `src/Meridian/UiServer.cs:516-519,968-988`; `ProductionRegistrationGuardService.cs:30-43` |
| Reconciliation never sees the ledger side | **Corrected — partially open.** Cash and positions populate and match; only ledger-transaction population is empty, so transaction rows alone become breaks | `RetainedInternalReconciliationPopulationProvider.cs:86-89`; `StatementMatchingEngine.cs:111-163` |

The remediation stream is real and is hitting the things prior reviews named. The rough pattern —
stated as a tendency, not a rule — is that **the closed rows were bounded, in-product defects, while
the ones still open lean systemic or architectural.** Fail-closed tenancy and a hash-chained journal
are posture changes; the fixed rows were bugs and gaps with clear edges.

The tendency is not exhaustive, and this review's own findings show why: the remaining
journal→transaction projection is bounded modelling work, not a systemic posture, and it is open.
Read the pattern as "the harder-to-bound work is what remains," not as a claim about every row.

## New findings

### N1. Distributed tracing is dead code — every span is dropped (medium)

*Scoped claim. Metrics and health are genuinely wired and are not part of this finding:*
`/metrics` serves the `prometheus-net` `DefaultRegistry` plus ten legacy hand-written series
(`src/Meridian.Application/Http/Endpoints/StatusEndpointHandlers.cs:319-343`), and `/health` returns a
dependency-aware response covering provider connectivity and storage health, with a 503 path
(`src/Meridian.Ui.Shared/Endpoints/StatusEndpoints.cs:28`). Counters and health work.

**Tracing does not.** `OpenTelemetrySetup` is a ~470-line layer with OTLP exporters, sampling config,
and dev/prod profiles, and seven production call sites create spans through `MarketDataTracing`
(backfill fetch, storage, WAL recovery, pipeline processing). But **`AddOpenTelemetryTracing` and
`Initialize` have zero callers anywhere in `src/`** — no `TracerProvider` is ever registered, so every
`Activity` those seven sites create has no listener and is discarded.

- `src/Meridian.Platform/Tracing/OpenTelemetrySetup.cs:33,116` — the two entrypoints, uncalled
- `src/Meridian.Infrastructure/Adapters/Core/Backfill/BackfillWorkerService.cs:558,609,633` — spans created into the void

**User impact:** counters tell an operator *that* backfill throughput dropped; nothing lets them follow
one slow backfill across fetch → validate → store to find *where*. For multi-stage pipelines — which is
most of what Meridian does — aggregate metrics without spans is the hard half of diagnosis missing.
The instrumentation cost is already being paid for no return.
**Improvement:** call `AddOpenTelemetryTracing` from host composition and point it at the OTLP endpoint
the config already models. **Value: medium. Effort: S** — the layer is written; this is a wiring fix.

### N2. The 409 conflict contract differs in shape per route family (low–medium)

**Corrected.** The first draft claimed version-checked writes existed in "exactly two subsystems."
That was wrong — it came from a truncated search. `expectedVersion` appears **366 times** across
`src/`, including `PostgresLedgerJournalStore`, `PostgresDirectLendingStateStore`,
`PostgresSecurityMasterEventStore`, `OperationsContinuityWorkflowService`, and
`ScopedAccessAssignmentStore`, and there is a dedicated `SecurityMasterConcurrencyException`. The
money paths named in the first draft as unguarded are largely guarded.

**Corrected a second time.** The first correction claimed manual journal drafts had no guard (0 version
references) and that there was "no HTTP-level concurrency contract." Both were wrong, and by the same
mechanism as the original error — counting inside one file and generalizing:

- Journal drafts **are** guarded, at the service layer:
  `ManualJournalEntryWorkbenchService.cs:284` (`existing.Version != request.Draft.Version`), with
  further checks at `:371`, `:425`, and `AccountingCloseReceipts.cs:141`, returning 409 to the caller.
  Counting references inside `FileManualJournalEntryDraftStore` missed all of it.
- HTTP-level conflict signalling **does** exist — it is body-based rather than header-based. Security
  Master and fund-structure routes return `409 Conflict`
  (`SecurityMasterEndpoints.cs:860,1035,1104`; `FundStructureEndpoints.cs:1363-1413`), and
  automated-journal and manual-journal routes accept a version and 409 on stale writes.

After two corrections this finding has shrunk to something genuinely minor, and it is more honest to
say so than to keep it at its original weight:

**What is left.** There is no `ETag`/`If-Match` *header* convention — those names appear nowhere in
`src/` — and the body-based contract that replaces it is not uniform. Different route families carry
the expected version under different field names and return different 409 response shapes (a
`ProblemDetails` in some places, a typed readiness DTO in others). A generic client cannot implement
one conflict-handling path; it must special-case per family.

There is also a narrower real defect worth separating out: several service-layer guards are
read-check-write sequences rather than atomic compare-and-swap, so two simultaneous requests can both
pass the version check before either writes. Whether that is reachable depends on the store's locking
underneath (see N4), which differs per store.

**Improvement:** standardize the conflict contract — one field name for the expected version, one 409
body shape — and document it; separately, confirm the check-then-write sequences are covered by a
lease or make them atomic. **Value: low–medium. Effort: S–M.** This is consistency work on a mechanism
that is present and largely working, not a missing capability.

### N3. The API surface is served and documented, but unversioned (medium)

**Corrected.** The first draft claimed no route serves an OpenAPI document. That was wrong: the host
registers a v1 Swagger document via `AddSwaggerGen` (`src/Meridian/UiServer.cs:428-442`) and serves it
through `UseSwagger()`/`UseSwaggerUI()` at `/swagger/v1/swagger.json`
(`UiServer.cs:566-573`); `docs/reference/api-reference.md:116` advertises `/swagger`. The search that
produced the error looked for `AddOpenApi`/`MapOpenApi`/`openapi.json` — the .NET 9+ built-in idiom —
and missed Swashbuckle. A discoverable contract exists.

What survives is the versioning half:

**Corrected a third time.** The first draft said "exactly one route is versioned," counting the
`MapGroup` call rather than routes. `MapRiskRoutes` maps **seven** endpoints, so seven versioned
routes exist — and the same method is invoked twice, at `/api/risk` (`RiskEndpoints.cs:78`) *and*
`/api/v1/risk` (`:80`), so the versioned group is a mirror of an unversioned twin rather than a
migration. The `~1,168` denominator counts `Map*` call sites and is a different unit again; both
numbers should be re-derived from the composed `EndpointDataSource` before either is quoted.

What is not in doubt is the shape: **one route family out of roughly fifty carries a version**, it
duplicates rather than replaces its unversioned form, and every other family is unversioned
`/api/<family>`. The Swagger document is titled "v1" but describes a route surface
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

**Corrected a second time.** Two further claims in the first correction were unsupported:

- **The "no cross-store transaction" concern was invented, not found.** I asserted "a workflow touching
  breaks, journals, and report packs" without identifying one. Searching for classes referencing both
  the break-queue and report-pack repositories returns four: `FundOperationsWorkspaceReadService` (a
  read service that writes only report packs), `WorkstationServiceCollectionExtensions` (DI
  composition), `LedgerAmountProvenanceService` (provenance reads), and `DemoTenantProvisioner` (the
  demo seeder). None is a transactional three-store mutation. **The claim is withdrawn**; if such a
  path exists, this review did not find it, and the crash-inconsistency scenario built on it was
  hypothetical presented as observed.
- **"Move behind the Postgres stores that already exist" understated the work.** There is **no**
  Postgres implementation of `IReconciliationBreakQueueRepository` or `IGovernanceReportPackRepository`
  — a search for `class Postgres*ReconciliationBreak*` / `*GovernanceReportPack*` returns zero. The
  Postgres stores that exist serve other domains and cannot be substituted. This is a new durable
  store plus schema, backfill, and cutover, not a migration.

**What survives** is one measurable concern and one open question:

1. **O(n) write amplification.** Every mutation re-serializes the whole collection, so cost grows with
   the data an engaged customer accumulates. This is a property of the design, not a defect, and it
   only matters above some volume this review did not establish — no benchmark was run.
2. **Lease coverage across the other file-backed stores is inconsistent, and at least one store is
   documented as single-process.** The break queue's `FileShare.None` lease is the correct pattern.
   `JsonlFilePaperSessionStore` is the opposite case, and says so in its own summary: *"Exactly one
   process may write a base directory; cross-process transactional locking is intentionally outside
   this local-file store's contract"* — it serialises appends on a process-local `SemaphoreSlim` only.

   That is a deliberate, documented design decision rather than an oversight.

   **Corrected:** the first version of this said "nothing enforces the single-writer constraint."
   Wrong again, and by the same method as every other error here — asserting absence without
   searching. Single-instance enforcement exists for the supported deployments:
   `src/Meridian.Wpf/App.xaml.cs:158-165` acquires a single-instance lock and exits secondary desktop
   instances; `src/Meridian.LifecycleSupervisor/Program.cs:61-78` takes a named mutex so one supervisor
   owns the host lifecycle; and `deploy/k8s/deployment.yaml:15` sets `replicas: 1`, with the manifest
   noting that multi-replica needs a coordination-capable shared volume.

   So the residual hazard is narrow: a direct-host launch that bypasses the supervisor, or the
   experimental multi-replica path. Neither is a supported configuration, which makes this a
   consistency question — should the 52 stores' locking postures be classified and documented — rather
   than a live end-user risk.

*On the count.* Earlier drafts said "56 production classes." Enumerating the distinct class names
matched by the pattern used (`class File*Store`, `class File*Repository`, `class Jsonl*`) gives **56
types, of which 52 are genuine file- or JSONL-backed stores and repositories** — from
`FileAccountingConfigurationStore` through `JsonlStrategyDesignRepository`. The other four are
`JsonlBatchOptions`, `JsonlStoragePolicy`, `JsonlReplayer`, and `JsonlStorageSink`, which the `Jsonl*`
half of the pattern swept in. **52 is the number to audit.**

A fourth-round review comment argued this set contained "at least ten" non-stores, citing
`FilePermissionsOptions`, `FileSearchResult`, `FileToDelete`, and `FileDropRouter`. Those four classes
exist in the repository but do **not** match the pattern used here — none ends in `Store` or
`Repository` — so they were never in the count. That specific evidence does not hold; the smaller
correction above does, and is applied. Recorded because a review that has been wrong repeatedly still
has to check the corrections.

**Improvement:** audit the file stores for lease coverage and standardize on the break queue's pattern
— small, mechanical, and it either confirms the layer is sound or finds the real gaps. Defer any
durable-store work until a volume measurement justifies it. **Value: medium for the audit; unproven
for migration. Effort: S** for the audit.

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

**The rails have a hard 100-case cap.** `MaximumBulkCaseCount = 100`
(`FileReconciliationBreakQueueRepository.cs:17`), and `ValidateBulkRequest` rejects any request whose
`BreakIds.Count` exceeds it regardless of the caller's `MaxCaseCount`
(`FileReconciliationBreakQueueRepository.Casework.cs:428-431`). So a grid that selects 200 rows and
calls execute once gets a rejection. Chunking works, but each chunk carries its own idempotency key and
produces its own receipt — which is a UX decision (partial-failure handling across chunks) and an audit
decision (one operator action appearing as N receipts), not just a loop.

**User impact:** still the highest value-to-effort item in this review. Reconciliation and close work is
batch-shaped — "accept these 140 sub-cent FX breaks", "assign this custodian's 80 breaks to one owner"
— and a one-at-a-time UI is the most common reason ops teams keep the spreadsheet they were told to
replace.
**Improvement:** wire the break-queue grid to the existing bulk routes. Decide the >100 case first:
either raise the backend cap, or chunk and define how partial failure and multi-receipt audit trails
are presented. Then extend the same selection pattern to the close checklist and journal drafts, and
keep receipts distinguishable from N individually reviewed decisions.
**Value: high. Effort: M** — raised from S–M, since the cap makes this more than pure wiring.

### N6. Two governed coverage metrics can be moved by prose (medium)

Found accidentally, by this PR moving it. The generated dashboard scores an endpoint as `Documented`
if its route path appears anywhere in the documentation corpus — including documents that describe no
contract at all.

This review mentions `POST /api/auth/accounts/{username}/password-reset` while explaining why an
authorization sweep misclassifies it. That mention alone flipped the entry:

```
merge base:  | POST | /api/auth/accounts/{username}/password-reset | Gap        |    249 / 621 documented
this branch: | POST | /api/auth/accounts/{username}/password-reset | Documented |    250 / 621 documented
```

No request shape, response shape, or usage was documented anywhere. A review document that criticizes
the route improved the route's documentation score.

**Why it matters:** this is a governed dashboard used as evidence of contract coverage, and it is
gameable by accident — any prose naming a path raises it. The number therefore cannot distinguish
"documented" from "mentioned," which makes it unusable for the purpose it exists to serve. The failure
is silent and one-directional: scores drift up as documentation volume grows, regardless of whether
contract documentation was written.

**The same flaw affects `coverage-report.md`, and this PR moved that too.** Documented public types
went 2,791 → 2,801. All ten additions are incidental mentions in this review — including
`FileDropRouter`, `FilePermissionsOptions`, `FileSearchResult`, and `FileToDelete`, which appear here
**only in a rebuttal explaining that they are not stores.** Arguing that four classes were miscounted
caused them to be counted as documented.

So two governed dashboards score prose as documentation, and a single review document moved both
without documenting anything. That is the strongest available evidence that the corpus rule, not the
individual metric, is what needs fixing.

**Improvement:** restrict both generators' corpora to actual contract/reference documentation
(`docs/reference/**`, the OpenAPI document from N3), or require the symbol to appear in a structured
block rather than free text. Tracked separately — this is a generator fix in `build/scripts/docs/**`,
which is a governed surface and does not belong in this PR. Both inflations are committed here because
`regenerate-docs` requires the artifacts to match a fresh regeneration; expect both to fall when the
corpus rule is fixed.

### N7. God files are shrinking, but too slowly to matter yet (low–medium)

**Corrected.** An earlier draft titled this "have not improved," which the 07-21 baseline contradicts.
Every file that review named is smaller now:

| File | 07-21 | Now | Δ |
| --- | ---: | ---: | ---: |
| `src/Meridian.Ui/dashboard/src/screens/settings-screen.tsx` | 7,428 | 7,391 | −37 |
| `src/Meridian.Wpf/ViewModels/Accounting/AccountingConfigureViewModel.cs` | 5,556 | 5,357 | −199 |
| `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs` | 4,630 | 4,478 | −152 |

Other current large files: `accounting-screen.view-model.ts` (7,147), `dev-fixtures.ts` (6,673),
`accounting-screen.tsx` (6,126).

So the direction is right and the rate is the finding: **−0.5% to −3.6% over three weeks on files of
5,000–7,400 lines.** At that rate `settings-screen.tsx` reaches a reviewable size somewhere past 2030.
The CI god-file ratchet prevents regression, which is why the numbers move down rather than up, but a
ratchet only forbids growth — nothing drives decomposition, so files shrink by whatever incidental
tidying a feature change happens to include.

The built workstation is 4.2 MB, with a 465 KB entry chunk and a 431 KB accounting chunk.

**Corrected:** an earlier version recommended adding step targets to the ratchet. Those already exist.
`docs/development/god-file-burn-down-plan.md:125-137` proposes retiring **at least two files per
release** and reducing capped lines **15% per quarter**, with concrete sequencing at `:142-191` — and
it prefers *files retired* over *lines removed* precisely because lines can fall by moving code
sideways. The plan also names its own gap: the targets are "**proposed**, not yet a registered
commitment," because `docs/roadmap/data/` holds no god-file item, so nothing tracks them.

**Improvement:** adopt the existing plan into the roadmap registry so the targets become tracked scope,
and give the ratchet a downward-only mechanism to lock in reductions (already open as
[#2675](https://github.com/rodoHasArrived/Meridian-main/issues/2675)). Nothing here needs designing.
**Value: low–medium** (developer velocity, not user-facing). **Effort: S** to adopt; **L** and ongoing
to execute.

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
| 1 | **Wire the break-queue grid to the bulk rails that already exist** | Highest value-to-effort item in the review; batch work is why teams keep the spreadsheet, and the idempotent dry-run backend is built and tested. Includes deciding the >100-case path (see N5) | M |
| 2 | **Agree the journal→transaction projection so imported transactions can match** | Balances and positions already reconcile; transaction rows all become breaks. Bounded modeling decision, not a rebuild | L |
| 3 | **Fix the authorization sweep to post valid bodies, then re-derive the count** | The ratchet is a good primitive currently producing a number nobody should quote; today it conflates 400-on-bad-body with unguarded | S |
| 4 | **Register the TracerProvider so the seven existing span sites emit** | Turns aggregate counters into per-request diagnosis across pipeline stages; the layer is written | S |
| 5 | **Freeze a `/api/v1` surface for the routes external systems bind to** | The contract is already discoverable via Swagger; what is missing is a stability promise | M |
| 6 | **Classify and document the 52 file stores' locking postures** | Locking is inconsistent by design: the break queue leases, `JsonlFilePaperSessionStore` is single-process. Supported deployments already enforce single-instance, so this is consistency work, not a live risk | S |
| 7 | **Standardize the 409 conflict contract across route families** | Concurrency control works; its request/response shape differs per family, so no generic client can handle conflicts once | S–M |
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
| N2 — inconsistent 409 contract shape | [#2694](https://github.com/rodoHasArrived/Meridian-main/issues/2694) | Rewritten twice — both the "two subsystems" and the "no HTTP contract" premises were false; now a consistency item |
| N3 — unversioned route surface | [#2695](https://github.com/rodoHasArrived/Meridian-main/issues/2695) | Rewritten — Swagger *is* served; only versioning survives |
| N4 — file-store lease-coverage audit | [#2697](https://github.com/rodoHasArrived/Meridian-main/issues/2697) | Rewritten twice — the lease exists, the cross-store hazard was withdrawn, and no Postgres counterpart exists to migrate to |

Two findings went to existing rows instead of new issues: N5 (bulk actions) as evidence on
`W10-RECON-002` [#2639](https://github.com/rodoHasArrived/Meridian-main/issues/2639), and the
baseline-accuracy question on `W9-GOV-008`
[#2633](https://github.com/rodoHasArrived/Meridian-main/issues/2633). Both carry follow-up comments
correcting the same errors.

**On the review method itself.** Across seven rounds, automated review found **twenty-five** wrong,
overstated, unsupported, or internally inconsistent claims. No new finding survived entirely unchanged: N1 held in substance
but overcounted its evidence (eleven "instrumented sites" were seven span-producing calls); N5
survived with its scope inverted from "missing" to "built but unwired," then had a 100-case backend
cap added; N2 and N4 shrank to consistency and audit items; N3 lost its larger half and then its
numerator. That record, not the findings, is the most useful output of this pass.

A fourth failure mode, visible only in the third round: **counting the wrong unit.** "Eleven call sites create spans" counted symbol occurrences, not span-producing calls.
"Exactly one route is versioned" counted a `MapGroup` call, not routes, against a denominator that
counted `Map*` call sites. Both numbers were quoted in the headline. A ratio is worth stating only
when numerator and denominator are the same unit and both were counted deliberately.

And a fifth, visible only in the fifth round: **the corrections themselves introduced inconsistency.**
A heading kept asserting a claim its own body had retracted; a priority table kept an effort estimate
the detailed finding had raised. Both are what a stakeholder skims. Rewriting a section is not
finished until every heading, table row, and summary line that references it has been re-read — which
is more work than the rewrite, and was skipped twice.

Two conclusions follow. First, **a source-evidence review with no ability to build or run the system
will over-produce absence claims**, because absence is the one thing a search can appear to prove and
cannot. Second, and less comfortable: **naming that bias did not prevent repeating it.** The first
correction reproduced the original error one paragraph after documenting it. The mitigation has to be
procedural rather than intentional — for every negative claim, run the counter-search at the layer
where the mechanism would plausibly live (service, not just store; endpoint, not just service), and
record what was searched. A claim of the form "X does not exist" that cannot name where it looked
should not ship.

A practical consequence for anyone reading this document: **its positive observations are more
reliable than its negative ones.** Where it says something exists and works, that was seen. Where it
says something is missing, treat it as a hypothesis until someone with a running system confirms it.

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
