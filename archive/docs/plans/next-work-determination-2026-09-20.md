# What To Work On Next — Meridian (2026-09-20)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-09-20
**Baseline:** `main` at `531776b1`
**Method:** read the roadmap registry (`docs/roadmap/data/*.yml`), the production-readiness tracker
(`docs/product/implementation-todo-list.md`), the decision log, and the live GitHub Actions and
pull-request state. Every claim below is anchored to a run, a commit, a PR number, or a `file:line`.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; the tracker remains authoritative for the
> P0 release gate.

## Headline

**The highest-value work right now is not a roadmap row. It is trunk recovery.**

Three findings, in the order they block everything else:

1. **`main` is red**, and it went red without a code change — a dated test fixture lapsed on the
   calendar. Fixed in this change.
2. **The P0 release gate is red on `main`** and has been for at least a week, on two named and
   specific test failures.
3. **Roughly twenty already-implemented P0/P1 remediation branches are stranded**, last touched
   2026-09-11, on bases that `main` has since left behind.

Meanwhile nine roadmap lanes are open simultaneously and none of the three in-progress W10 rows has
closed. The project is generating work faster than it is landing it. The recommendation below is to
stop starting and start finishing.

## Tier 0 — Restore the trunk (today)

### 0.1 The futures expiry time bomb — *fixed in this change*

`main` was green on `17a0264a` in CI run
[#6311](https://github.com/rodoHasArrived/Meridian-main/actions/runs/35299405975) (2026-09-18) and
red on **the same commit** in run
[#6312](https://github.com/rodoHasArrived/Meridian-main/actions/runs/35415603933) (2026-09-19). No
code changed between them. The calendar did.

Two tests in `tests/Meridian.Tests/Futures/FutureProjectionServiceTests.cs` pinned an "active"
contract to a hard-coded expiry of `2026-09-18`:

```
Meridian.Tests.Futures.FutureProjectionServiceTests.GetFrontMonthAsync_IgnoresExpiredRollTargets
Meridian.Tests.Futures.FutureProjectionServiceTests.GetFrontMonthAsync_IgnoresPastExpiryRollTargetsEvenWhenLifecycleIsRollTarget
  Expected frontMonth not to be <null>.
```

`FutureProjectionService.GetExpiryLadderAsync` filters on
`DateOnly.FromDateTime(DateTime.UtcNow)` (`src/Meridian.Instruments/Futures/FutureProjectionService.cs:30`)
and takes no clock, so once `2026-09-18` passed, the only non-expired contract in each fixture
dropped out of the ladder and the front month became `null`.

This change anchors all four date-bearing cases in that class to the run date in whole months, so
they assert lifecycle and ordering behavior rather than the wall clock. Two further cases in the
same class were latent bombs on the same mechanism and are fixed alongside: the expiry-ladder case
would have fired on 2026-12-20 and the roll-target preference case on 2027-03-20.

The ladder case is also strengthened while it is being repaired: its retired contract now expires
*after* both survivors, so only the lifecycle stat can exclude it. Previously the retired and
expired rows were both in the past, and the date filter alone would have satisfied the assertion.

### 0.2 The systemic version of the same defect

The instance above is not isolated. Across the repository:

- **201** files under `src/` read `DateTime.UtcNow` directly.
- **324** test files under `tests/` carry hard-coded `new DateOnly(202x, …)` or
  `new DateTime(202x, …)` calendar literals.

Not every intersection is a time bomb, but the class is real and it fires silently: it turns the
trunk red on an unchanged commit, at an unpredictable future date, in a lane nobody was touching.
That is the most expensive failure mode a CI signal can have, because the first instinct is to
suspect the last merge.

**Recommended work item:** an audit pass that (a) enumerates services whose behavior is a function
of "now" and which take no clock seam, (b) introduces `TimeProvider` injection for the
date-filtering ones, and (c) sweeps test fixtures that pin near-future calendar dates against
clock-derived comparisons. This is mechanical, parallelizable, and pays for itself the first time
it prevents a red trunk. It also has no registry row today — `RISK-*` in
`docs/roadmap/data/risk-register.yml` carries four risks and none covers it.

## Tier 1 — The P0 release gate is red on `main`

`Production Certification` run
[#75 / 35486834227](https://github.com/rodoHasArrived/Meridian-main/actions/runs/35486834227)
(2026-09-20, `main` at `531776b1`) failed. So did run
[#74](https://github.com/rodoHasArrived/Meridian-main/actions/runs/34735719426) on 2026-09-13. Three
of four jobs are green — the recovery drill, the documentation evidence job, and both dependency
gates. The failure is confined to **`deterministic PostgreSQL integration and coverage evidence`**,
and it is narrow: **2 failed, 952 passed, 0 skipped**.

Both failures are worth fixing on their merits, independent of the gate:

**1. `PostgresSecurityMasterConflictServiceTests.ResolveAsync_PersistsWinnerAndResolverAcrossInstances`**

```
System.InvalidOperationException : Sequence contains no matching element
  at System.Linq.Enumerable.Single[TSource](IEnumerable`1 source, Func`2 predicate)
```

A resolved Security Master conflict is not being found after the store is re-opened. Either the
winner/resolver is not durably persisted across instances or the lookup key drifted. The test's own
name states the property under test, and that property is a durability claim — exactly the kind the
production definition in the tracker says must hold.

**2. `AtomicTaxLotJournalStoreTests.AppendAssetPostingAsync_AcquisitionReplayDisposalAndRollbackShareOneTransaction`**

```
Expected the exception message to match "*one exact asset-account debit*"
but "Journal entry '…' posts an instrument-bearing ledger entry without Security Master
     provenance for security '…'." does not.
```

This is **guard-ordering drift**, not a missing guard. A newer Security Master provenance check now
refuses the posting before the asset-account debit-shape check can. The posting is still refused, so
this is not an open hole — but the debit-shape guard is now unproven by this test, and the
diagnostic an operator sees for a malformed asset posting has silently changed. Decide which guard
should speak first and make the test assert that ordering deliberately.

**Note the adjacency:** this second failure sits in the tax-lot journal store, which is
`W9-ASSET-010`'s Asset Accounting Event Spine and the substrate for `W10-LOT-002`. Tier 1 and the
highest-priority W10 row are the same code. Fixing the gate here is not a detour from the roadmap;
it is the roadmap row's foundation.

## Tier 2 — Land the stranded backlog before opening anything new

**40 pull requests are open.** They break down as:

| Group | Count | Oldest | Last touched |
| --- | --- | --- | --- |
| `codex/*` P0/P1 remediation branches | ~20 | #2896 (2026-09-02) | 2026-09-14 |
| Recurring "Security Master institutional-requirements pass" drafts | 7 | #2892 (2026-09-01) | 2026-09-16 |
| Dependabot | 9 | #2307 (2026-07-17) | 2026-09-14 |
| Feature / other | 4 | #2566 (2026-08-04) | 2026-09-19 |

### 2.1 The remediation branches are the buried value

These twenty branches are not speculative work. They are implemented, self-documented fixes that the
tracker's own P0/P1 bands call for — provider egress hardening against DNS rebinding and private
IPv6 (#2916), OAuth token persistence moved into the encrypted credential vault (#2922), atomic and
retry-safe legacy credential migration (#2921), statement publication flushed before atomic commit
(#2930), CSV financial evidence and OFX currency retention (#2928, #2929), production throttles and
mutation retry headers (#2926), and fail-closed host tenancy as `W9-GOV-008` change 9 (#2959).

They are also **rotting**. #2912 and #2959 were last synced to bases from 2026-09-11; `main` has
taken 111 commits since (657 including merged branch history) in the nine days to 2026-09-20.
#2912 alone touches 194 files with 1,430 insertions and 972 deletions. Every day of drift raises the
rebase cost superlinearly, and several of these branches carry each other's fixtures (#2954's shared
approval and provider fixtures are vendored into #2912 and #2959), so they will conflict with one
another as well as with `main`.

Their PR bodies say plainly what is blocking them — "awaiting final-head validation and human
review", with local `scripts/ci.sh` runs that died on MSBuild memory exhaustion. The authoritative
hosted checks were never re-run on the final heads.

**Recommended sequence:** triage the twenty into land / re-cut / close. Take the small, isolated
ones first (#2929, #2930, #2921 are narrow), push each to a fresh hosted `quality-gate` run, and
land them one at a time. Re-cut anything that cannot be rebased cheaply — #2912's 194-file diff is a
strong re-cut candidate, and its own body already separates runtime changes from vendored fixtures.
**Do not open new remediation branches until this queue drains.** The queue is the constraint, not
the supply of findings.

### 2.2 The recurring Security Master pass is producing PRs nobody consumes

Seven near-identical drafts (#2892, #2904, #2905, #2935, #2937, #2961, #2975), one per audit run
from 2026-09-01 to 2026-09-16, all still open. A recurring job whose output accumulates unread is
worse than no job: it costs review attention and hides the one pass that might actually differ.
Either land them as they are produced, collapse them into a single rolling record, or turn the
schedule off. #2937 is explicitly titled "a no-drift interval" — that is a result that should close
its own PR, not open one.

## Tier 3 — The one red roadmap row

**`W9-CORPACT-011`** is the only row with `health: red`, the only acceptance ever reopened
(`DEC-W9-ACCEPTANCE-002`, 2026-08-30), and it has a precisely stated gap:

> `CorporateActionOperationsService.TransitionCaseAsync` refuses every transition to
> `ReadyForApproval` with `ProjectionStale` because the durable exact-version accounting projection
> authority is not persisted.

The approval and posting states are modelled in the contract but unreachable in the shipped
implementation, so unchanged exit criterion four is unmet across a subsystem of roughly 280 source
and test files. This is the best-specified open item in the registry: the defect is named, the
criterion is unchanged, and the acceptance decision is already recorded as reconsiderable once the
lane is reachable. #2947 already targets the PostgreSQL persistence proof it needs.

Everything else in W9 is settled. `W9-SAFETY-007` has sat at `ready_for_acceptance` since
2026-09-01 with all four exit criteria evidenced — **nineteen days waiting on a governance decision
that requires no engineering.** Taking or declining that decision costs nothing and clears a lane.

## Tier 4 — W10 sequencing: finish three before starting a fourth

The W10 depth slate is 3 in progress and 9 planned. Counting `W8-WPF-PARITY-001`,
`W8-UX-CONSOL-001`, `W9-GOV-008`, `W9-INGEST-009`, `W9-CORPACT-011`, plus the three W10 rows and
`W9-SAFETY-007` awaiting acceptance, **nine lanes are open at once.** That is the reason none of
them closes.

In closing order, by what each row's own registry record says remains:

1. **`W10-LOT-002`** (`priority: critical`, in progress). Open remainders are named: acquisition
   writer convergence, `AverageCost` basis redistribution, amortization, corporate-action
   successors, advance refunding, and shadow-operation acceptance. **Start here** — it is the only
   `critical` row, and Tier 1's second certification failure is in its substrate.
2. **`W10-MARK-001`** (sequence 1, in progress). Implementation is done and hosted CI passed at
   `d49fbc8e7`; what remains is live WPF rendering and population-wide preview certification. It
   also closes `RISK-STALE-MARK-001`, one of only four registered risks.
3. **`W10-SEAM-001`** (sequence 7, in progress). Same posture — implementation evidenced, live
   operator certification open. It carries a recorded sequencing handshake with
   `W8-WPF-PARITY-001`, so closing it removes a dependency from the desktop lane rather than adding
   one.
4. Only then **`W10-RECON-001`** (sequence 2), the first planned row and the durable break-lineage
   dependency for `W10-RECON-002`, `-003`, and `-004`. Starting it before the three above close
   would make ten open lanes.

## Tier 5 — Registry and tracker hygiene (cheap, and currently misleading)

- `docs/roadmap/data/program-state.yml` carries `snapshot_date: 2026-08-30`; the tracker's
  `implementation-todo-list.md` is `Reviewed: 2026-08-19`. Both are three to five weeks stale.
  Neither reflects `W9-DEMO-002` closing to `done` on 2026-09-16, and neither reflects the currently
  red trunk or red certification lane. The tracker's release-gate snapshot describes a frozen-commit
  evidence set on `65dc0107` that no longer has a green `main` behind it.
- The risk register has four entries and no coverage of clock-coupled fixtures (Tier 0.2). Worth a
  fifth.
- `W9-SAFETY-007`'s pending acceptance (Tier 3) is a registry state, not engineering work.

## What not to work on

- **New product surface.** The scope gate in `program-state.yml` is explicit, the deferred-lane list
  is long and deliberate, and nine lanes are already open. Nothing below Tier 4 should start.
- **#2977** (`feat(design-system): restyle Meridian to Programmed Institutionalism`) is the only
  non-draft feature PR with recent activity and carries `phase:PR9`, the unrestricted scope phase. A
  whole-repository restyle should not land ahead of a green trunk and a green certification lane.
- **Re-litigating accepted W9 rows.** Six are accepted under recorded decisions and one is done.
  Only `W9-CORPACT-011` is genuinely open, and it was reopened deliberately.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| P0 | Futures expiry time bomb | `main` is red on an unchanged commit — **fixed in this change** |
| P0 | Clock-coupling audit and `TimeProvider` seam | 201 src / 324 test files carry the same latent defect |
| P0 | Two certification failures (Security Master conflict persistence; tax-lot guard ordering) | The P0 release gate has been red on `main` for a week |
| P1 | Triage and land the ~20 stranded remediation branches | Implemented P0/P1 value, rotting at ~111 commits of drift |
| P1 | `W9-CORPACT-011` approval and posting lane | Only red row; best-specified gap in the registry |
| P1 | Take or decline the `W9-SAFETY-007` acceptance | 19 days idle, zero engineering cost |
| P2 | Close `W10-LOT-002`, then `W10-MARK-001`, then `W10-SEAM-001` | Finish three lanes before opening a fourth |
| P2 | Collapse or stop the recurring Security Master pass PRs | 7 unread drafts consuming review attention |
| P3 | Refresh `program-state.yml` and the tracker; add the clock-coupling risk | Both are 3–5 weeks stale and currently misleading |
