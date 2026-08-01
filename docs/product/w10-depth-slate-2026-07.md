# 2026-07 Depth Slate (W10 Roadmap Rows)

**Status:** accepted planning input; live status lives in the roadmap registry
**Owner:** core-team
**Reviewed:** 2026-07-31
**Registry decision:** `DEC-DEPTH-SLATE-001` in [`docs/roadmap/data/decision-log.yml`](../roadmap/data/decision-log.yml)
**Registry rows:** `W10-MARK-001` through `W10-CONSOL-001` in [`docs/roadmap/data/roadmap-items.yml`](../roadmap/data/roadmap-items.yml)

This document records the depth slate adopted on 2026-07-31 and maps each rank to its durable
roadmap row and to the production-readiness rows it strengthens. Treat the roadmap registry as live
truth; this page preserves the ranking rationale so the ordering decision stays explainable.

## Snapshot

Where the W9 slate asked *what most improves Meridian's ability to prove a number for a real
evaluator*, this slate asks a narrower follow-on question: **where has Meridian's engine and
calculation depth outrun the operator surface, and in what order should that gap close?**

The dominant finding is that the recurring gap is not missing capability — it is capability that was
built, tested, and never connected. **Six of the eleven rows wire existing code rather than write
new code.** Verified at adoption time against current source:

- Bulk break resolution exists end to end — contracts with an idempotency key and bounded case
  count, a repository implementation with dry-run and retained receipts, a mapped endpoint, and
  three browser client functions — and no screen calls any of them.
- A Number Passport component exists and never fetches provenance; it infers every row by
  keyword-scanning relationship labels. The amount-provenance service behind it has no client
  consumer in any lane.
- The recurring-journal service holds its schedules *and* its idempotency guard in memory and is not
  registered in dependency injection at all.
- The wash-sale and tax-character engine terminates in one report-pack CSV artifact that no endpoint
  serves and no screen reads.

Return measurement is the exception — a genuine gap rather than an unwired one — but it is narrower
than a first pass suggested. A brokerage-sourced fund-account performance endpoint already exists,
returning a single-period cash-adjusted return (ending equity minus beginning equity minus net cash
flow) over balance snapshots. It is registered inline with no constant in `UiApiRoutes.cs`, which is
why a route-constant search misses it. What is absent is ledger-derived, time-weighted,
money-weighted, and investor-level return — so `W10-PERF-001` extends or supersedes that seam rather
than building beside it.

The ordering is truth and identity first, then the shared seam later rows depend on, then
reconciliation economics, then accounting depth, then the new surface that stands outside those arcs
last.

## Ranked Slate

| # | Roadmap row | Improvement | Kind | Why it's ranked here |
| -- | --- | --- | --- | --- |
| 1 | `W10-MARK-001` | Fail-closed stale-mark policy and mark-age surfacing | activation | An aged mark can price a valuation silently; same doctrine as `W9-TRUTH-001`, and days of work |
| 2 | `W10-RECON-001` | Durable break lineage identity and run-over-run diff | new seam | Breaks have no identity that survives a run, so aging and grouping are unsound without it |
| 3 | `W10-PROV-001` | Ledger-amount evidence subject and shared proof drawer | activation | The platform bet — four later rows each want the same proof drawer |
| 4 | `W10-RECON-002` | Break clustering and bulk-resolution activation | activation | Resolving 340 breaks costs 340 actions when the bulk rails are already built |
| 5 | `W10-JRNL-001` | Durable recurring journal schedules and draft runner | activation | The recurrence engine plans occurrences that can never become journals |
| 6 | `W10-TAX-001` | Tax character, wash-sale, and lot-relief operator surface | activation | A tax engine that shipped this month is invisible to every operator |
| 7 | `W10-SEAM-001` | Unified close-readiness projection behind one shared contract | consolidation | Five readiness encodings, and the console aggregates client-side so the desktop lane must fork it |
| 8 | `W10-RECON-003` | Unified tolerance model and what-if replay workbench | new capability | Changing a tolerance is a blind edit discovered on the next production run |
| 9 | `W10-RECON-004` | Operator-taught match rules with promotion gate | new capability | Every manual match is discarded; the governed-autonomy answer to agentic reconciliation |
| 10 | `W10-PERF-001` | Portfolio and investor return measurement | new capability | The only return today is a brokerage single-period cash-adjusted figure; nothing is ledger-derived, time-weighted, or investor-level |
| 11 | `W10-CONSOL-001` | Intercompany elimination on consolidated ledger views | new capability | Consolidated trial balances double-count; the enum value for the fix has no producer |

## Ordering Rationale

**Truth and identity first (ranks 1–2).** `W10-MARK-001` is the cheapest row in the slate and closes
a live path where an unbounded-age mark prices a valuation without saying so. `W10-RECON-001` is
promoted ahead of clustering because exploration found no break identity that survives a run: the
statement matcher mints a random identifier, and the queue projection's identifier hashes the
variance amount, the tolerance, and the as-of date, so a one-cent move produces a different break.
Ranks 4 and 10 are unsound without these two.

**The shared seam second (ranks 3, 7).** `W10-PROV-001` is the load-bearing row. Ranks 6, 7, 10, and
11 each independently want a proof drawer; building it once as a registered evidence contributor
behind the existing subject-addressed route family is the difference between one component and four.
`W10-SEAM-001` follows because it must land before the desktop parity lane builds its own readiness
console, or the browser's client-side aggregation gets duplicated instead of retired.

**Reconciliation economics third (ranks 4, 8, 9), strictly ordered.** Clustering supplies the
grouping primitive that the tolerance preview and the learned-rule promotion gate both report
against.

**Accounting depth fourth (ranks 5, 6), parallel.** Neither depends on the other. `W10-JRNL-001`
has no dependencies at all and can start earlier if capacity allows; only its phase gate waits.

**Standalone new surface last (ranks 10, 11).** Four rows carry the `new capability` kind — ranks 8
through 11 — so this group is not the whole of it. Ranks 8 and 9 sequence inside the reconciliation
arc above because they depend on clustering; ranks 10 and 11 are the two that stand outside every
other arc, and `W10-PERF-001` depends on rank 1's mark discipline to be honest about what it reports.

## Posture Relative to Production Readiness

The program's production readiness is `blocked` in `docs/roadmap/data/program-state.yml`, gated on
every P0 row in [`implementation-todo-list.md`](implementation-todo-list.md) completing on one
release commit.

**This slate is post-W9 depth work and does not precede certification.** Two rows are the deliberate
exceptions, and they are pulled forward because they *serve* the release gate rather than compete
with it:

- `W10-MARK-001` discharges `RISK-STALE-MARK-001` and applies the `W9-TRUTH-001` fail-closed
  doctrine to the one input that most quietly corrupts a valuation. It is tracked as its own risk
  rather than against `RISK-SIM-REAL-001`: an aged mark is real data presenting as current, not
  simulated data presenting as real, and conflating them would overstate simulation-safety burn-down.
- `W10-SEAM-001` removes a client-side fork of product state that the WPF parity lane would
  otherwise duplicate into the desktop workstation.

## Why a New Wave Token

`W9`'s numeric suffix encodes rank within the 2026-07 first-order slate — stated explicitly in
`DEC-PRIORITY-SLATE-001`. Appending eleven rows would corrupt that meaning, so `W10` opens with
conventional per-area numbering and rank is recorded in this document's table instead.

## Production-Readiness Tracker Mapping

Mapped conservatively. A row appears here only where it advances the tracker control as that control
is actually defined; where no honest relationship exists the cell is empty, because a wrong mapping
lets release planning count W10 delivery against an unrelated production blocker.

| Roadmap row | Related tracker rows |
| --- | --- |
| `W10-MARK-001` | `PRD-005` (no authoritative-looking output without authoritative inputs) |
| `W10-RECON-001` | `PRD-101` (deterministic reconciliation semantics) |
| `W10-PROV-001` | `PRD-005` (authoritative as-of outputs with retained support) |
| `W10-RECON-002` | `PRD-101`, `PRD-007` (retained case evidence under bulk action) |
| `W10-JRNL-001` | `PRD-003`, `PRD-004` (governed posting boundary and close safety), `PRD-007` (durable workflow state) |
| `W10-TAX-001` | `PRD-005` |
| `W10-SEAM-001` | `PRD-100`, `PRD-110` (workstation surface honesty and route parity) |
| `W10-RECON-003` | `PRD-101` (deterministic matching) |
| `W10-RECON-004` | `PRD-101`, `PRD-007` |
| `W10-PERF-001` | `PRD-005` (authoritative as-of outputs); mark-gap periods must block rather than interpolate |
| `W10-CONSOL-001` | `PRD-003` (governed posting boundary for elimination drafts) |

Three mappings from the first draft were removed as incorrect: `PRD-000` governs deployment-posture
parsing and supported startup policy, not valuation freshness; `PRD-010` governs provider and
statement ingress security with bounded parsing, not tolerance replay; `PRD-017` governs
documentation automation and same-commit drift, not product evidence artifacts.

## What This Slate Is Not

- It is not a completion claim: every W10 row enters the registry as `planned` with
  `planned_evidence` posture and no evidence entries.
- It is not a replacement for the production-readiness tracker's P0 release gate; `P0` rows remain
  release blockers regardless of slate order.
- It does not reopen deferred lanes (mobile, client portal, forecasting, enterprise risk, treasury
  payment execution, capital-structure modeling, no-code workflow designer). `W10-CONSOL-001` is
  scoped to wholly owned fully consolidated entities precisely to stop short of capital-structure
  modeling.
- It does not add an eighth root workspace; every row lands inside the seven charter roots.
- It does not claim the activation rows are trivial. "Already built" describes the backend, not the
  governance, the operator surface, or the tests each row still owes.

## Known Constraints from Source

The roadmap rows state outcomes and acceptance evidence, which is what a `planned` registry row is
for. This section carries the implementation constraints found while checking each row against the
code, so they are available as blueprint input without the registry asserting design nobody has
validated against a build.

Every entry below was verified against the type that owns the behavior. **Read this section before
blueprinting any row** — several of these are the reason a row's outcome is phrased the way it is.

### `W10-MARK-001` — valuation freshness

- The daily pricing policy defaults stale-price handling to disabled, and only one production caller
  overrides it. Flipping the default is a behavioral change, and the policy type is public, so it is
  breaking for external constructors.
- Two freshness controls overlap: the ledger stale-price policy and the mark-price quality policy,
  both driven from the same maximum-age input in the one production path.
- **A mark dated after the valuation date is currently treated as fresh.** The assessment computes a
  negative age, clamps it to zero, and returns fresh; a test pins that behavior. A fail-closed policy
  that only blocks *old* marks still admits prices that were not observable as of the valuation.
- The shared position read models carry no mark observation date or age. The fund portfolio position,
  the portfolio position summary, and the workstation trading position row all live in the shared
  contracts and none exposes freshness; the trading row carries a mark price with no date attached.
  Without extending those contracts, each client would infer freshness independently or omit it.

### `W10-RECON-001` — break identity

- Two incompatible identifiers exist. The statement matcher mints a random per-run identifier; the
  queue projection derives one from a fingerprint that hashes the variance amount, the tolerance, the
  as-of date, and the accounting period — so a one-cent move produces a different break.
- A single-hop, caller-supplied re-key hook already acknowledges the instability, but it is a patch,
  not a lineage chain.
- **The SLA policy declares calendars the calculator never reads.** `BusinessCalendarId` and
  `HolidayCalendarIds` sit on the policy contract, while the calculator inspects only `DayOfWeek` and
  skips Saturday and Sunday. A break detected around a market holiday is escalated early and banded
  at the wrong business age, so lineage-derived age needs calendar resolution and holiday-boundary
  tests, not just a stable identity.
- The desktop lane consumes the same queue: the WPF fund reconciliation workbench service loads the
  same break-queue item collection and maps it into the Fund Ledger queue. Lineage and occurrence
  fields added for the browser alone would leave the two co-equal lanes with different queue
  semantics.
- **Absence from a run is only evidence of clearing when that run finished — and "finished" is not
  `Completed`.** `StatementRunStatus` (`StatementReconciliationDtos.cs:9-20`) carries both
  `ReviewRequired = 6` and `Completed = 7` as successful terminal states, and
  `ReconciliationApiService.cs:92` assigns `breakDtos.Length == 0 ? Completed : ReviewRequired`
  (same shape at `:187-188`, `:232`, `:634`). **`Completed` is therefore exactly the set of runs with
  zero breaks.** An implementation gating the diff on `status == Completed` would suppress
  run-over-run comparison precisely when breaks exist: in a successor where break A remains and
  break B disappeared, B would never clear. The conclusive successor is a *successfully reconciled
  terminal run* — `ReviewRequired` or `Completed` — while `ValidationFailed`, `Failed`, and
  `Canceled` are inconclusive. Both compared runs must be conclusive in that sense and bound to the
  same account, source, period, and profile version; otherwise the prior break stays open or its
  state reads as unknown.
- The queue carries a single break identifier and nothing distinguishing a lineage from an occurrence
  of it. Adding lineage alone leaves a cleared-then-recurring break able to reopen its original item
  and keep the original SLA age, or to overwrite the interval during which it was clear. Lineage and
  occurrence need separate identifiers with explicit recurrence and split semantics.

### `W10-PROV-001` — amount provenance

- The amount-provenance service is served by one legacy route and has **no client consumer** in the
  browser workstation, the shared UI services, or the desktop workstation.
- The Number Passport component exists and never fetches provenance — it infers all its rows by
  keyword-scanning relationship labels.
- The proof drawer is a private, unexported function inside one explorer screen.
- The evidence graph service already fans out over a registered contributor collection behind a
  subject-addressed route family, which is the seam to extend rather than duplicate.
- The provenance service currently substring-matches a full break-queue scan and scrapes provider
  detail out of delimited key-value strings; that fragility should not be carried forward.
- **Related-case lookup matches on text, not identity.** It scans the tenant and company queue and
  substring-matches break ID, routing text, explainability text, account name, and symbol, without
  enforcing the report's fund, ledger book, period, or an exact evidence identifier. Two funds with
  overlapping account names or symbols in one tenant can therefore attach each other's cases to an
  amount, and the passport would present unrelated support as proof. Wiring the service unchanged
  reproduces that; exact scope matching and collision tests are prerequisites.

### `W10-RECON-002` — clustering and bulk resolution

- Bulk casework is **implemented end to end and unwired**: contracts with an idempotency key and a
  bounded case count, a repository implementation with dry-run and retained receipts, a mapped
  endpoint, and browser client functions that no screen calls.
- The break classifier is constructed inline rather than injected, so its materiality policy is never
  configurable.
- The classifier's break type and recommended action survive only inside formatted message strings;
  materiality and absolute variance do not survive at all. The natural grouping key is therefore
  present in prose but not queryable on a stored break.
- Grouping must not include the lineage key from `W10-RECON-001` — that key identifies a single
  break, so including it would make every group contain exactly one member.
- **Materiality is a per-break property, not a group property.** The classifier sets `IsMaterial`
  from each break's absolute variance. A group whose signed aggregate falls below a group threshold
  can still contain a material member, and gating only on the aggregate lets that member bypass the
  independent approval it individually requires. Any group cap must be measured on gross exposure,
  and the presence of one material or high-risk member must force the approval separation.
- **The bulk request carries no expected versions and no preview receipt.** It holds break IDs, an
  action, an actor, command and correlation IDs, a source, an idempotency key, dry-run and
  partial-success flags, and optional reason, assignee, and priority — and the repository reads each
  case's version only once execution begins. A dry run and the execution that follows are therefore
  independent reads, so a concurrent edit between them applies the action to state the preview never
  showed. Binding execution to the previewed versions is what turns the existing dry run into a
  governance control rather than a display.

### `W10-JRNL-001` — recurring journals

- The owning service is **not registered in dependency injection anywhere**; only tests reference it.
- Its schedules *and* its posted-occurrence idempotency guard are both in-memory.
- A schedule holds only a template identifier, while the template book and the locked-period book are
  separate in-memory fields — so durable schedules alone leave a restarted worker unable to
  materialize a journal or evaluate a lock.
- The planner already emits an idempotency key of the right shape, and a durable schedule store, a
  time-provider-driven worker with a deterministic single-run seam, and an evidence-carrying intake
  path all already exist for monthly automated journals. Copy that pattern.
- The existing intake carries evidence links and an evidence assessment; a recurring draft must pass
  the same admission gate rather than relying on persistence and lock checks alone.
- **Idempotent posting does not imply idempotent drafting.** The due-occurrence calculation keeps
  returning an occurrence until it is confirmed posted, so a runner polling while the first draft
  awaits approval will enqueue another one every pass. Preventing duplicate *postings* leaves the
  approval queue accumulating conflicting drafts for the same occurrence. The claim has to be taken
  at initial enqueue, keyed by schedule and occurrence date, with retries returning the existing
  draft.

### `W10-TAX-001` — tax character and relief

- The engine computes per-parcel character, holding period, wash-sale holding-period extension, short
  and long term totals, and disallowed loss, and terminates in a single report-pack artifact that no
  endpoint serves and no screen reads. The tax character type has no reference in the shared
  contracts or either workstation.
- **Wash-sale deferral is computed from the sale aggregate.** The projector returns no wash sale
  whenever the aggregate realized result is nonnegative, and its own documentation records that mixed
  gain and loss sales are not decomposed. A disposal containing loss shares can therefore report zero
  exposure. Per-loss-lot decomposition is a prerequisite for presenting exposure as decision support.
- **Account relief policy is not effective-dated.** The policy book holds one policy per account and
  registration replaces it; resolution *throws* when the stored policy postdates the disposal. After a
  method change, regenerating an earlier pack fails outright, or silently produces different relief
  and realized-gain figures if the new policy is backdated. Append-only effective-dated versions are
  required for reproducibility.
- **An open replacement window makes zero exposure provisional, not settled.** The wash-sale policy
  defines a symmetric window before and after the sale, so an acquisition after the disposal can
  still change the disallowed loss. The engine will happily compute zero from the acquisitions known
  today, and a generic incomplete-input rule does not catch that — the inputs are complete, the
  window simply has not closed. The figure needs an as-of/provisional label, the remaining window,
  and re-evaluation or governed finalization when it closes.
- The realized gain and loss contract that already reaches the workstation exposes a single scalar
  with no character split — extending it is the lowest-friction first move.
- The shared contracts must not reference the ledger implementation assembly; define contract-side
  types and map in the shared UI or financial-operations layer. Dashboard TypeScript types are
  hand-maintained.
- The desktop lane presents the same positions through its own blotter and fund-ledger view models, so
  a browser-only surface leaves the co-equal lane showing positions without their tax character or
  wash-sale state. The same applies to the mark-age surfacing in `W10-MARK-001`.

### `W10-SEAM-001` — close readiness

- Four services compute close readiness and a fifth computes asset-class coverage under a readiness
  name. Between them they encode readiness five incompatible ways: a scored close-readiness record, an
  evidence-status enum, an accounting-readiness enum, and two different free-string status
  vocabularies.
- The scored record is only reachable nested inside a workflow record, never as its own payload.
- The cross-lane operator readiness console aggregates client-side in roughly two thousand lines,
  which is why the desktop parity plan is scheduled to reimplement rather than consume it.
- The command-center read service already injects the calendar and cockpit services, making it the
  natural consolidation point — **but it takes both as optional constructor parameters defaulted to
  null** and computes readiness without requiring the calendar. Moving consumers onto it therefore
  carries the false-ready path forward rather than closing it: a lane that is unregistered or failing
  is indistinguishable from a lane with nothing to report. The projection needs an explicit
  contributor manifest, so an absent contributor is a blocking incomplete state.
- **Present and fresh is not the same as about the same thing.** `FundLedgerViewModel.cs:1037-1038`
  calls `IFinancialOperationsCommandCenterReadService.GetCommandCenterAsync(fundProfileId:
  activeFund.FundProfileId, ct: ct)` — only the fund profile id, leaving fund account, ledger book,
  period, and entity null. `FinancialOperationsCommandCenterReadService.cs:42-43` then calls
  `ListAsync(fundAccountId, periodId, status: null, ct, ledgerBookId: ledgerBookId)` with all three
  null, so the listing spans every fund; `ResolveActiveWorkflow` picks one, `:57` adopts its fund via
  `effectiveFundAccountId = fundAccountId ?? activeWorkflow?.FundAccountId`, and `:66` queries the
  cockpit with the *requested* profile. So a projection can combine one fund's workflow blockers with
  another fund's cockpit while every contributor is registered, healthy, and current.
- **A second subject-binding gap, same-fund this time.** `FundOperationsWorkspaceReadService` does
  scope correctly by fund — `:2600-2614` filters workflow summaries to the requested fund's account
  ids before taking the most recently updated one — but that selection ignores the summary's
  `LedgerBookId` and `PeriodId`. The result is a right-fund, wrong-book-or-period mismatch. Subject
  binding across fund profile, ledger book, fund account, entity, and period is a separate
  requirement from presence, and both services need it.

### `W10-RECON-003` — tolerance and replay

- Three tolerance shapes exist and the engine consumes only the flattest, so the price, basis-point
  cash, and settlement-date rules are structurally unreachable.
- Scoping is by profile identifier alone; the file-backed provider is load-once and read-only with no
  write path and no edit surface.
- The statement checkpoint store is a resumption cursor holding counts for the newest run per account
  — not a replay surface.
- **Normalized-entity and match-result repositories already exist** with file-backed implementations
  and zero references outside their own file. Retention is activation work, not new storage.
- **Those two stores cover only half a run.** The retained normalized entities are the statement-side
  populations — positions, cash balances, transactions, securities, source rows — and the match-result
  store retains outputs, while the matching engine also requires internal positions, internal cash
  balances, and internal ledger transactions. Replaying against live internal state would attribute
  intervening portfolio, cash, or ledger movement to the tolerance change. The internal side has to be
  retained immutably or version-addressed alongside the existing artifacts.
- Match-kernel determinism is unverified and must be proven before any simulation result is shown.
- **Committing a tolerance is a straight-through policy change, not an operator preference.** A
  widened tolerance automatically suppresses future breaks, which is exactly the per-policy-approval
  move the charter's section 21 governs. A successful preview establishes the effect, not the
  authority: activation still needs versioned approval with materiality caps, retained reversible
  evidence, sampling review, a kill switch, and per-item handling for material classes.
- The tolerance profile already carries a version, but the file-backed provider is a static load-only
  class with no save, write, or compare-and-swap path. Adding an edit surface without a version
  boundary lets a preview taken against version N be retained as justification for a commit that
  lands on N+1 — the same preview-to-execute drift as the bulk-casework gap, in a different seam. The
  commit needs to carry the expected profile version and a receipt binding the proposed profile to
  the run inputs it was previewed against.

### `W10-RECON-004` — learned matching

- The engine's match result carries a rule identifier list and an explanation, but that result is
  ephemeral: persistence keeps only the **first** rule identifier as a single tolerance-rule field,
  with no promoting operator anywhere. Durable attribution needs a contract and storage change.
- That durable contract is a domain type, not a financial-operations one — the retained match link
  lives in the domain assembly's reconciliation aggregate. Work confined to Financial Operations,
  Strategies, and the dashboard cannot widen the schema that has to carry the attribution.
- The matching engine is sealed with a hard-coded stage ladder and no stage abstraction, so an ordered
  stage collection must be introduced.
- The matcher deliberately separates position, cash, and transaction matching and enforces currency
  and instrument identity as prerequisites. Position matching requires `SecurityId` on both exact and
  tolerance tiers, and transaction matching compares `SecurityId` *instead of* currency whenever
  either side is security-backed. So instrument is an identity predicate in its own right, not a
  subcase of currency: a rule generalized on counterparty, description, account, sign, and a numeric
  tolerance could match an unrelated security in the same account. A promoted rule has to retain every
  immutable predicate its match kind enforces.
- A competing reconciliation vocabulary exists in the functional calculation projects; do not grow a
  second matching model there.
- The desktop lane performs manual matches too: `MatchSelectedReconciliationItemsAsync` resolves a
  selected break through the shared reconciliation workbench service. Capture wired only to the
  browser would teach rules from one lane's matches and discard the other's, so the shared contract
  and endpoint owners are on the path as well as both clients.
- **Promotion is a straight-through lane, and the charter governs those.** Section 21 permits moving
  from per-item to per-policy approval only under all of: deterministic rules rather than model
  output, a human-approved versioned policy defining the eligible class with materiality caps, full
  retained evidence and audit events that stay reversible through governed correction, sampling
  review and a kill switch, and material or high-risk classes remaining per-item. A promotion gate
  built on sample size and precision alone omits most of that — and an over-broad rule could suppress
  a material break before the independent-approval control ever runs.

### `W10-PERF-001` — return measurement

- A brokerage-sourced performance endpoint already exists, registered inline with no shared route
  constant, returning a single-period cash-adjusted return (ending equity minus beginning equity minus
  net cash flow) over balance snapshots for one linked account.
- The daily pricing projection is a **single-day snapshot with no cross-day persistence**, so a
  chainable series has to be added.
- The capital-account subledger supplies dated investor cash flows but **no residual ending value**,
  which a complete money-weighted return requires.
- It records approval state and posted state separately; admitting approved-but-unposted activity puts
  flows into a return the ledger-derived residual value does not carry.
- **Chaining sub-period returns across an external capital flow attributes that flow to performance.**
  Time-weighted return needs a subperiod boundary and valuation at each external flow, or an
  explicitly documented and labeled approximation.
- The extended internal rate of return kernel is internal to the backtesting assembly and unreachable
  from the ledger and financial-operations lanes.
- **That kernel does not detect multiple roots.** Newton-Raphson runs from a fixed 0.10 guess and
  returns the first rate it converges to; the bisection fallback searches one bracket and returns
  `NaN` when the endpoints share a sign. A cash-flow series with several sign changes — contribution,
  distribution, later recall — can admit more than one valid rate, and the kernel returns whichever
  one it happens to reach, with nothing marking the result ambiguous. Non-convergence and
  multiple-root detection, a declared convention, and golden cases for non-conventional series are
  prerequisites for reporting a money-weighted return.
- **Capital-account activity is partitioned by currency.** The projection builder groups subledger
  entries by capital account, investor, *and* currency, so an investor transacting in two currencies
  has two activity rows. Summing their nominal amounts into one cash-flow series produces a figure
  with no meaning — a USD contribution against a EUR distribution — while still satisfying every
  completeness label. Returns need per-currency reporting or translation at each flow's as-of rate,
  with a missing rate blocking the figure.
- The investor and capital-account activity projection — cash-flow dates, investor identity, and
  posted state — is owned by the Financial Operations design module, not by Ledger. A kernel built
  from Ledger and Contracts alone would bypass the authoritative investor cash-flow source.

### `W10-CONSOL-001` — intercompany elimination

- Intercompany and consolidation-elimination are **accounting treatment kinds**, selected through the
  accounting policy rule — not journal sources. That rule already carries journal template, evidence,
  approval, and auto-posting settings, which is the seam for producing elimination drafts.
- The consolidated trial balance sums its sub-ledgers with no elimination step, and no rule produces
  the elimination treatment today.
- Posting entity and counterparty are **separate optional dimensions** and the consolidated book key
  carries no legal-entity identity, so pairing on counterparty alone can combine balances from
  unrelated entities facing the same affiliate.
- Effective-dated ownership links with an ownership percentage already exist and are the authoritative
  source for resolving an as-of consolidation perimeter.
- Without a deterministic key over perimeter, as-of date, reciprocal pair, and rule version plus a
  stale-version guard, a rerun can produce a duplicate draft and both can be approved, double-
  eliminating the balance. The row maps to a tracker control that requires exactly that discipline.
- An unapproved draft must not reach a reported figure. Subtracting proposed eliminations from the
  consolidated view satisfies "no double-counting" while concealing a genuine intercompany balance
  behind work nobody approved; gross views stay ledger-derived, eliminated figures consume only posted
  entries, and drafts appear as a labeled preview.
- The policy-rule and draft-production services that own both seams live in the Financial Operations
  design module, not in Ledger. Implementation routed through Ledger, Contracts, and UI Shared alone
  can add consolidated-view logic without ever defining the elimination treatment on the authoritative
  policy path.
- **The charter's ledger invariants bind this work directly.** Concurrency control requires writes
  that depend on balance or approval state to use optimistic version checks and fail closed on stale
  state; posted immutability requires corrections to go through reversing or adjusting journals linked
  to the original record. So an elimination draft must carry expected versions for *both* reciprocal
  sides at approval, and a corrected rerun must reverse and rebook rather than rely on the
  same-perimeter idempotency key to overwrite an obsolete elimination.
- The desktop lane builds its own consolidated trial balance through `FundLedgerScope.Consolidated`
  in the WPF fund-ledger read service. Without routing that lane, WPF keeps presenting an unlabeled
  consolidated view that cannot distinguish gross from eliminated figures.

## Known Risks at Adoption

1. **`W10-RECON-003` is the weakest row.** Its two prerequisites — unifying three incompatible
   tolerance shapes, and activating the unwired normalized-entity and match-result repositories that
   replay retention needs, since the statement checkpoint store is only a resumption cursor —
   are each larger than the headline preview feature. Splitting the tolerance-model unification into
   its own row, or dropping the row from v1, is a live option.
2. **`W10-SEAM-001` races the WPF parity lane.** It needs an explicit sequencing handshake with
   `W8-WPF-PARITY-001` before its phase gate closes.
3. **`W10-MARK-001` changes behavior rather than preserving it.** It will block valuations that
   silently passed, and the affected policy type is public, so the default change is breaking for
   external constructors. It ships behind a preview and an owner decision.
4. **Match-kernel determinism is unverified** and gates `W10-RECON-003`. A kernel that reads
   wall-clock time or mutable reference data would make tolerance replay actively misleading.

## Validation

```bash
python3 build/scripts/docs/validate-roadmap-registry.py --summary
python3 build/scripts/docs/render-roadmap-docs.py --summary
python3 build/scripts/docs/validate-docs-structure.py --summary
```
