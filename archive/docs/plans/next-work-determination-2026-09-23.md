# What To Work On Next — Meridian (2026-09-23)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-09-23
**Baseline:** `main` at `13aa7576`
**Supersedes:** [2026-09-20](next-work-determination-2026-09-20.md); the 2026-09-22 determination in PR #2983 is unmerged and two of its findings are closed below
**Method:** read the roadmap registry (`docs/roadmap/data/*.yml`), the production-readiness tracker
(`docs/product/implementation-todo-list.md`), the decision log, and live GitHub Actions and
pull-request state; measured the stranded-branch queue directly with `git merge-tree` against
`origin/main`. Every claim is anchored to a run, a commit, a PR number, a `file:line`, or a
reproducible command.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; the tracker remains authoritative for the
> P0 release gate.

## Headline

**Three days of merges closed the previous list's top two items. The queue did not move at all, and
measuring it changed what to do about it.**

- The trunk is green, the certification lane's two named failures were repaired and landed, and a
  run dispatched by this change **confirms the lane green on current `main`** — the first success on
  `main` in the three runs since 2026-09-13. The P0 *gate* is still blocked, on different rows.
- The 2026-09-22 document's Tier 1 calendar defect was fixed on `main` before that document could
  merge — it is closed, not carried forward.
- The pull-request queue is still 43. Every one of the 22 stranded `codex/*` branches now conflicts
  with `main`. **But 93% of the conflicting paths are regenerable build output, not source.** That
  reverses the previous recommendation to re-cut the large branches.

## What closed since 2026-09-20

| Previous item | Status today | Evidence |
| --- | --- | --- |
| Futures expiry time bomb (`main` red on an unchanged commit) | **Closed.** Trunk green. | CI run [#6379](https://github.com/rodoHasArrived/Meridian-main/actions/runs/35871321988) success on `5d7d6797` |
| Two Production Certification failures (Security Master conflict persistence; tax-lot guard ordering) | **Repaired and landed**, re-certified in Tier 0 below | PR #2985, merged 2026-09-22 |
| `W9-CORPACT-011` approval and posting lane (only `red` row) | **Closed.** `ready_for_acceptance`, `health: green` since 2026-09-22 | PR #2986; registry row reviewed 2026-09-22 |
| `W10-LOT-002` acquisition-writer convergence and `AverageCost` relief | **Two of six remainders closed** | PRs #2986, #2988 |
| Trading-calendar defect: `TradingCalendarService` hard-coded nine 2026 holidays, omitted Juneteenth, went blank 2027-01-01 (raised 2026-09-22, PR #2983) | **Closed.** The service now delegates to the canonical `Meridian.Platform` `TradingCalendar`, which generates holidays per year and observes Juneteenth (`TradingCalendar.cs:470`). `AddHolidays2026` no longer exists in `src/`. | commit `1aba7941` (PR #2987), merged 2026-09-23 |

PR #2983 should be closed or re-cut rather than merged: its Tier 1 finding is fixed, its Tier 0 is
superseded by Tier 0 here, and the branch is already `dirty` against `main`.

## Tier 0 — Re-certify the P0 gate on current `main` (done in this change)

The certification repairs in PR #2985 landed on 2026-09-22. `Production Certification` runs on
`cron: "17 3 * * 0"` — **weekly, Sundays**. Its last run was
[#75](https://github.com/rodoHasArrived/Meridian-main/actions/runs/35486834227) on 2026-09-20, which
*predates the repairs*, and the next scheduled run is 2026-09-27. So for five days the only
published evidence about the P0 gate was a failing run against a commit that no longer exists in the
lane, and ordinary CI cannot substitute: both repaired tests sit behind `[LedgerDatabaseFact]` and
require the PostgreSQL service the certification workflow starts.

This change dispatched run
[#76](https://github.com/rodoHasArrived/Meridian-main/actions/runs/35885778558) against `main` at
`13aa7576`. **It passed — all four jobs green**, including `deterministic PostgreSQL integration and
coverage evidence` and its `Reject failed or skipped deterministic tests` step, which had carried
the two failures since 2026-09-13. On `main` the lane now reads: #74 (09-13) failed, #75 (09-20)
failed, **#76 (09-23) success**.

**State this precisely.** A green certification lane is not the P0 release gate. The gate in
`docs/product/implementation-todo-list.md` requires every P0 row complete on one release commit, and
`PRD-000` and `PRD-013` through `PRD-017` remain evidence-gated on external same-commit evidence
this run does not supply. What run #76 does establish is that the lane itself no longer fails, which
is the precondition three roadmap rows were waiting on.

**Consequences to act on:**

1. `W10-LOT-002`, `W10-MARK-001`, and `W10-SEAM-001` each carry the sentence "broader Production
   Certification remains failed" in their registry evidence. **That sentence is now false.** It
   should be corrected against run #76 — see Tier 3.
2. The weekly cadence is still too slow for the only lane that executes the PostgreSQL-backed tests:
   it left a five-day window in which the published evidence contradicted the code. Either add a
   `push: branches: [main]` trigger, or dispatch the workflow as part of landing any change that
   touches a `[LedgerDatabaseFact]` subject. Without that, this result decays the same way the last
   one did.

## Tier 1 — The stranded queue is blocked by build output, not by code

**This is the finding that changes what to do.** The 2026-09-20 determination recommended taking the
small branches first and re-cutting the large ones, sized by diff. Measured directly, that sizing is
wrong.

Probe (reproducible): `git merge-tree --write-tree origin/main origin/codex/<branch>` for all 22
stranded branches, classifying each conflicting path as generated/built or hand-written.

| Measure | Value |
| --- | --- |
| Stranded `codex/*` branches | 22 |
| Branches that conflict with `main` | **22 of 22** |
| Distinct conflicting paths, all branches | 483 |
| …in built browser assets (`src/Meridian.Ui/wwwroot/workstation/assets/`) | **379 (78%)** |
| …in other generated artifacts (source READMEs, `docs/generated/`, `docs/status/`, `docs/diagrams/`, `database/manifest/`, source-hash manifest) | 72 (15%) |
| …in **hand-written source, tests, or docs** | **32 (7%)** |
| Branches whose conflicts are *entirely* regenerable | **10 of 22** |

The built-asset bucket conflicts by construction: Vite emits content-hashed filenames into
`../wwwroot/workstation` (`vite.config.ts:188`), the tree is tracked in git, and 117 tracked files
there absorbed 2,073 file-touch events in the last 30 days. Any two branches that both rebuild the
dashboard produce add/add and rename/rename conflicts on nearly every asset, whatever they changed
in source. `.gitattributes` defines no merge driver for these paths.

**Untracking them is not the fix** — `PRD-013` and `PRD-018` both require one tracked canonical
workstation asset tree, and `PRD-018`'s closure candidate depends on it. The fix is to make the
merge mechanical.

### 1.1 Drain the ten branches that need no judgement

These conflict only in regenerable output. The procedure is identical for each and involves no
product decision: merge `origin/main`, resolve every generated path to `main`'s version, re-run the
generators (`npm --prefix src/Meridian.Ui/dashboard run build`,
`build/scripts/docs/run-docs-automation.py --profile core`), push, and let hosted `quality-gate`
grade it.

| PR | Branch | Conflicting paths | Hand-written |
| --- | --- | --- | --- |
| #2922 | `p0-oauth-vault` | 4 | 0 |
| #2916 | `p0-provider-egress` | 3 | 0 |
| #2948 | `ingest-casework-evidence` | 3 | 0 |
| #2912 | `service-robustness-fixes` | 3 | 0 |
| #2940 | `update-provider-capability-descriptor-catalog` | 4 | 0 |
| #2920 | `p0-cash-ladder-currency-evidence` | 4 | 0 |
| #2953 | `manual-journal-audit-recovery` | 52 | 0 |
| #2931 | `p0-scoped-credential-ownership` | 53 | 0 |
| #2896 | `replay-parameter-fail-closed` | 112 | 0 |
| #2897 | `portfolio-corporate-action-snapshots` | 119 | 0 |

Note #2912 in particular. The 2026-09-20 document named it "a strong re-cut candidate" on its
194-file diff. It conflicts on **three** paths, none of them hand-written, and it is the only
stranded branch with activity this week.

### 1.2 One shared test fixture unblocks six more

Of the 32 hand-written conflicting files, one appears in seven branches:

```
tests/Meridian.Tests/FinancialOperations/OperationsContinuity/
  FinancialOperationsCommandCenterReadService.PublicationTests.cs
```

with `tests/Meridian.Tests/Application/OperationsContinuityWorkflowServiceTests.cs` alongside it in
three. Six of the seven — #2929, #2930, #2921, #2928, #2926, #2954 — have **no other** hand-written
conflict, and they are exactly the branches #2954 ("Repair shared approval and provider fixtures")
was cut to serve; they vendor each other's copies of it. Resolving that one fixture once, on `main`,
converts all six into 1.1's mechanical case. The seventh, #2947, carries one further test file and
is handled in 1.3.

### 1.3 The genuine remainder

Six branches carry real source conflicts needing a decision, and they are small and separable:

- **#2956** `fund-structure-tenant-backfill` — 10 files, one coherent feature (planner, runner,
  command, Postgres store, migration `005`, its tests, operator doc).
- **#2959** `fail-closed-host-tenancy` — 4 files in composition registration. `W9-GOV-008` change 9.
- **#2789** `backtesting-quantscript-milestone-1` — 8 files across `OrderManagementSystem`,
  `SimulatedPortfolio`, `BacktestProxy`, and a migration waiver. Already self-labelled
  `[DRAFT — RE-CUT REQUIRED]`; that label is still correct, and it is the only one.
- **#2826** `first-trusted-close` — 4 files in the operations-continuity screen. The only branch with
  **zero** generated conflicts.
- **#2947** `corpact-postgres-roundtrip` — 2 test files. Likely **superseded**: PR #2985 already
  proved the `W9-CORPACT-011` lane end to end on PostgreSQL. Verify and close.
- **#2945** `corpact-review` — 3 roadmap registry YAML files. Almost certainly **superseded**: it
  records a safety acceptance and corrects corporate-action evidence that the registry has since
  moved past. Verify and close.

### 1.4 The durable fix

Add a merge policy for the generated tree so this does not recur. In order of cost:

1. A `.gitattributes` merge driver (`merge=ours` or a regenerate-then-take driver) for
   `src/Meridian.Ui/wwwroot/workstation/**`, `docs/generated/**`, `docs/status/**`,
   `docs/diagrams/**`, `database/manifest/**`, `docs/source/generated/**`, and `src/*/README.md`.
2. Or a documented "regenerate on merge" step in the contributing guide, so the resolution is a
   command rather than a judgement.

Neither changes what is tracked, so neither disturbs `PRD-013` or `PRD-018`.

## Tier 2 — Two rows await an operator decision and no engineering

Both are registry states, not work:

- **`W9-SAFETY-007`** has sat at `ready_for_acceptance` since 2026-09-01 with all four exit criteria
  evidenced — **22 days**. No `DEC-` entry exists for it.
- **`W9-CORPACT-011`** reached `ready_for_acceptance` on 2026-09-22 under the reopening
  `DEC-W9-ACCEPTANCE-002`, whose own text says acceptance "may be reconsidered only after the
  approval and posting lane is reachable and the criterion is implemented and evidenced." It now is,
  on PostgreSQL. The reconsideration is due.

Taking or declining either costs nothing and clears a lane. Declining is a result too.

## Tier 3 — W10: two rows are now one dependency away from closing

`W10-MARK-001` and `W10-SEAM-001` are both implementation-complete with hosted CI and Windows
automated acceptance passed at `d49fbc8e7`. Each registry record named two remaining blockers, and
Tier 0 just removed one of them:

- ~~broader Production Certification~~ — **green as of run #76**; both records still say "broader
  Production Certification remains failed" and need correcting against it. `W10-LOT-002`'s record
  carries the same stale sentence, plus a citation to run `33956884001` that "failed six other
  tests and its dependency gate"; run #76 passed both.
- live operator certification — WPF rendering and population-wide preview for `W10-MARK-001`, live
  operator certification for `W10-SEAM-001`. **This is now the sole blocker on both**, and it is an
  operator action, not engineering.

Correcting those three records is the cheapest high-value work on this list: it converts two rows
from "blocked on a failing gate" to "awaiting an operator session," which is a different and much
shorter conversation.

The engineering-shaped W10 work is one row: **`W10-LOT-002`**, the slate's only `critical`.
Amortization, corporate-action successors, advance refunding, and shadow-operation acceptance remain
open after the two closures of 2026-09-22/23. Continue there and do not start `W10-RECON-001` — nine
lanes are still open at once, which is why none of them closes.

`W10-SEAM-001` carries a recorded sequencing handshake with `W8-WPF-PARITY-001`, so closing it
removes a dependency from the desktop lane rather than adding one. That makes its live certification
worth scheduling ahead of `W10-MARK-001`'s.

## Tier 4 — Registry and tracker hygiene

Cheap, and currently misleading:

- `docs/roadmap/data/program-state.yml` still carries `snapshot_date: 2026-08-30` — **24 days
  stale**. Its `active_scope` still describes `W9-CORPACT-011` as reaching `ready_for_acceptance`
  "on 2026-09-22" (correct) but the snapshot date contradicts it.
- `docs/product/implementation-todo-list.md` is `Reviewed: 2026-08-19` — **35 days stale** — and its
  release-gate snapshot describes evidence on `65dc0107`, a frozen commit with no current green
  certification behind it. Tier 0's run gives it a current anchor on `13aa7576`. Its header still
  reads "production certification blocked"; that remains correct for the *gate* (`PRD-000`,
  `PRD-013`–`PRD-017`), but the *lane* is no longer the reason, and the tracker should say which.
- The risk register has four entries. The 2026-09-20 document proposed a fifth for clock-coupled
  fixtures; the 2026-09-22 measurement narrowed that class to roughly nine candidates and the one
  production defect it found is now fixed. **Register it as a low-severity risk, not a work item** —
  the sweep the first document proposed is not justified by the second's measurement.
- Nine near-identical "Security Master institutional-requirements pass" drafts are open (#2892,
  #2904, #2905, #2935, #2937, #2961, #2975, #2982, #2984), one per run from 2026-09-01 to
  2026-09-22, none consumed. Up two since 2026-09-20. Collapse them into one rolling record or stop
  the schedule; a pass that finds no drift should close its own PR rather than open one.

## What not to work on

- **New product surface.** The `scope_gate` in `program-state.yml` is explicit and nine lanes are
  already open.
- **A repository-wide `TimeProvider` sweep.** Proposed 2026-09-20, measured and downgraded
  2026-09-22, and its one real yield is now fixed on `main`.
- **Re-cutting the large stranded branches.** Tier 1 measures the cost and it is not where the diff
  size suggests. Re-cut #2789 only.
- **Re-litigating accepted W9 rows.** Six are accepted under recorded decisions and one is done.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| — | Re-certify the certification lane on current `main` | **Done in this change** — run #76 green on `13aa7576` |
| P0 | Correct the "broader Production Certification remains failed" sentence on `W10-LOT-002`, `W10-MARK-001`, `W10-SEAM-001` | Now false; it is the stated blocker on two rows whose only real remainder is an operator session |
| P0 | Trigger `Production Certification` on `main` pushes, or on any `[LedgerDatabaseFact]` change | Weekly cadence left a 5-day window where published evidence contradicted the code; without this the new result decays the same way |
| P1 | Drain the ten stranded branches whose conflicts are entirely regenerable | Mechanical, no product decision, implemented P0/P1 value |
| P1 | Resolve `FinancialOperationsCommandCenterReadService.PublicationTests.cs` once on `main` | Converts six more branches into the mechanical case |
| P1 | Take or decline `W9-SAFETY-007` (22 days) and `W9-CORPACT-011` acceptances | Zero engineering; clears two lanes |
| P2 | Continue `W10-LOT-002`; schedule `W10-SEAM-001` live certification | The only `critical` row, and the one that unblocks the desktop lane |
| P2 | Add a merge policy for the generated tree | 93% of all queue conflicts, recurring by construction |
| P2 | Verify and close #2947, #2945, #2983 as superseded | Their findings landed elsewhere |
| P3 | Refresh `program-state.yml` (24d) and the tracker (35d); register clock coupling as a low risk | Both currently misdescribe the program |
