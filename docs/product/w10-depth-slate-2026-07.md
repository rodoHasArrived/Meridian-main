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
reconciliation economics, then accounting depth, then genuinely new capability last.

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

**New capability last (ranks 10, 11).** These add rather than activate, and `W10-PERF-001` depends
on rank 1's mark discipline to be honest about what it reports.

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

### `W10-RECON-001` — break identity

- Two incompatible identifiers exist. The statement matcher mints a random per-run identifier; the
  queue projection derives one from a fingerprint that hashes the variance amount, the tolerance, the
  as-of date, and the accounting period — so a one-cent move produces a different break.
- A single-hop, caller-supplied re-key hook already acknowledges the instability, but it is a patch,
  not a lineage chain.
- The SLA policy declares a holiday-calendar field that is never read; the calendar is weekend-only.

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
- The realized gain and loss contract that already reaches the workstation exposes a single scalar
  with no character split — extending it is the lowest-friction first move.
- The shared contracts must not reference the ledger implementation assembly; define contract-side
  types and map in the shared UI or financial-operations layer. Dashboard TypeScript types are
  hand-maintained.

### `W10-SEAM-001` — close readiness

- Four services compute close readiness and a fifth computes asset-class coverage under a readiness
  name. Between them they encode readiness five incompatible ways: a scored close-readiness record, an
  evidence-status enum, an accounting-readiness enum, and two different free-string status
  vocabularies.
- The scored record is only reachable nested inside a workflow record, never as its own payload.
- The cross-lane operator readiness console aggregates client-side in roughly two thousand lines,
  which is why the desktop parity plan is scheduled to reimplement rather than consume it.
- The command-center read service already injects the calendar and cockpit services, making it the
  natural consolidation point.

### `W10-RECON-003` — tolerance and replay

- Three tolerance shapes exist and the engine consumes only the flattest, so the price, basis-point
  cash, and settlement-date rules are structurally unreachable.
- Scoping is by profile identifier alone; the file-backed provider is load-once and read-only with no
  write path and no edit surface.
- The statement checkpoint store is a resumption cursor holding counts for the newest run per account
  — not a replay surface.
- **Normalized-entity and match-result repositories already exist** with file-backed implementations
  and zero references outside their own file. Retention is activation work, not new storage.
- Match-kernel determinism is unverified and must be proven before any simulation result is shown.

### `W10-RECON-004` — learned matching

- The engine's match result carries a rule identifier list and an explanation, but that result is
  ephemeral: persistence keeps only the **first** rule identifier as a single tolerance-rule field,
  with no promoting operator anywhere. Durable attribution needs a contract and storage change.
- The matching engine is sealed with a hard-coded stage ladder and no stage abstraction, so an ordered
  stage collection must be introduced.
- The matcher deliberately separates position, cash, and transaction matching and enforces currency
  and instrument identity as prerequisites. A learned rule generalized without those predicates could
  let a reviewed pattern in one currency suppress an unrelated break in another.
- A competing reconciliation vocabulary exists in the functional calculation projects; do not grow a
  second matching model there.

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
