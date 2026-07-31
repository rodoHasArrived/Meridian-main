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

- `W10-MARK-001` discharges part of `RISK-SIM-REAL-001` and applies the `W9-TRUTH-001` fail-closed
  doctrine to the one input that most quietly corrupts a valuation.
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

## Known Risks at Adoption

1. **`W10-RECON-003` is the weakest row.** Its two prerequisites — unifying three incompatible
   tolerance shapes, and retaining replayable run artifacts where only a resumption cursor exists —
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
