# God-File Burn-Down Plan

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-08-11

The no-new-god-file ratchet (`build/scripts/ci/check-file-size.py`) stops the problem growing. It
does not shrink it. This plan sets the target and sequencing that turn a frozen baseline into a
falling one, and records the mechanism changes that make progress visible.

- Ratchet: [`../../build/scripts/ci/check-file-size.py`](../../build/scripts/ci/check-file-size.py)
- Baseline: [`../../build/config/file-size-baseline.json`](../../build/config/file-size-baseline.json)
- Contracts: [ADR-017 (modular operational monolith)](../adr/017-modular-operational-monolith.md)
  and [module conventions](../architecture/module-conventions.md)
- Origin: issue #2619

## Where the baseline stands

Measured against the ratchet's own scanner on this branch (threshold 2,000 lines). Reproduce with
`python3 build/scripts/ci/check-file-size.py`, which prints the first three rows directly:

| Metric | Value |
| --- | ---: |
| Baselined files | 50 |
| Capped lines | 169,329 |
| Current lines | 169,308 |
| Reclaimable slack (current below cap) | **21 lines** |

By surface, counted from the baseline's recorded caps:

| Surface | Files | Lines | Share |
| --- | ---: | ---: | ---: |
| C# (`.cs`) | 29 | 87,859 | 51% |
| TypeScript (`.ts`) | 15 | 57,891 | 34% |
| TSX (`.tsx`) | 6 | 23,579 | 13% |

The browser workstation is **48% of the baselined debt** (21 files, 81,470 lines) despite being one
of several surfaces. Any plan that only addresses C# ViewModels leaves half the problem untouched.

## The finding that shapes the plan

**48 of the 50 baselined files sit at exactly zero headroom, and all 50 are within 25 lines of their
cap.** The 21 reclaimable lines are spread across two files.

That is not the occasional cliff issue #2619 describes — it is the steady state of the entire
baseline. Adding one line fails CI today for 48 of the 50 — the ratchet rejects only `lines > cap`,
so the two with slack would reach their caps and still pass, once. The practical consequences:

- Any ordinary change to a god file — a `using` directive, a guard clause, a log line — forces a
  choice between an unrelated refactor and a `--update-baseline` commit.
- That pressure systematically produces baseline bumps, because bumping is the cheap option under
  deadline. The ratchet therefore *records* growth rather than preventing it.
- It also makes the baseline actively hostile to the small, safe, incremental extractions this plan
  depends on, because those often add a line or two before they remove fifty.

This was not theoretical during the work that produced this plan: PR #2669 removed 91 lines from a
sibling partial class and still failed the ratchet, because adding one `using` directive pushed
`LedgerEndpoints.cs` from 2,824 to 2,825 against a 2,824 cap.

**A burn-down target is not achievable while every file is pinned at its cap.** Fixing the headroom
problem is a prerequisite, not a nicety — and no mechanism for it exists yet, which is what the
missing-mechanism section below records.

Those 21 lines are themselves the trend working. They appeared when #2669 landed on `main` and
removed lines from files whose caps this baseline still carries, moving the pinned count from 49 to
48. That is exactly the signal the reporting below exists to make visible, and it is also why the
numbers in this document are stated with the command that reproduces them rather than as fixed
values: they move whenever a decomposition lands.

## Mechanism changes (this change)

Reporting only. No enforcement rule is altered, so nothing that passes today starts failing.

1. **Trend reporting.** Every run prints baselined file count, total capped lines, total current
   lines, and reclaimable slack — including `--update-baseline`, which reports against the baseline
   it just wrote. That is the run immediately after a decomposition lands, so it is the one an
   operator most wants the numbers from, and every cap it re-pins shows up as `TIGHT` at the moment
   of re-pinning. The number the plan is trying to move is now visible in CI output instead of
   requiring a script to compute.
2. **Headroom warnings.** Files within 25 lines of their cap are listed as `TIGHT`. The wall is
   visible before a contributor hits it, and the report names decomposition candidates by urgency
   rather than by size.

## The missing mechanism

Reporting makes the problem visible. It does not fix it, and the gap is specific:

**There is no safe way to lock in a reduction.** `--update-baseline` regenerates from the tree, so
it can raise a cap as easily as lower one — which is why it is the escape hatch that should surface
in review as tracked debt. Nothing lowers caps only. Until that exists, lines reclaimed by a
decomposition sit unprotected: the old cap still permits the file to grow straight back.

**Nothing creates working headroom.** Any mechanism that lowers a cap to the current count re-pins
the file, which is the state this plan exists to escape. A useful tool has to lock in most of a
reduction while deliberately leaving room to edit.

A first attempt at both — a downward-only `--tighten-baseline` with a `--buffer N` option — was
drafted alongside this plan and **withdrawn**. Review found six distinct defects in it across five
rounds: retired files dropped out of the reclaimed total, retirement discarded a cap while the file
was still within the buffer of the threshold, an unreadable file was counted as empty and had its
cap written away, the command returned success while the ratchet was failing, `--buffer` was
accepted and ignored outside tightening, and the resulting slack made the tool recommend a command
that destroyed the headroom it had just created.

Every one of those lived in the same seam: the interaction between *file below threshold*, *file
still in baseline*, and *tree currently failing*. A read-only check never has to reason about it. A
mutating one does, on every path. That is the design work the mechanism needs, and it belongs in its
own change with its own review rather than riding along with a reporting improvement.

Until then, lock in a reduction with `--update-baseline` and justify the diff in review, as the
ratchet's own documentation already directs.

## Targets

These are **proposed** targets, not yet a registered commitment. `docs/roadmap/data/` is the
authoritative planning registry, and it currently holds no god-file or file-size-ratchet item;
adopting these numbers means adding one so registry validation, generated roadmap views, and status
reconciliation can track them. Until that entry exists, treat this table as a recommendation from
the audit rather than tracked delivery scope.

| Horizon | Proposed target | Rationale |
| --- | --- | --- |
| Per release | Retire **at least 2 files** from the baseline entirely | File count is the honest unit — a file drops out only when it is genuinely decomposed |
| Per quarter | Reduce total capped lines by **15%** (~25,400 lines) | Matches the reduction rate issue #2619 proposes |

Prefer *files retired* over *lines removed* as the headline metric. Lines can fall by moving code
sideways into a new file that is itself close to the threshold; a file leaving the baseline means a
real seam was found.

## Sequencing

**1. C# ViewModels with the most optional dependencies first — not the largest files.**

Counting method, so these can be rechecked: *lines* is the ratchet's own count; *public members* is
declarations at class indentation beginning with `public`, excluding the constructor; *ctor params*
and *optional* are the parameters of the public constructor and how many of those are nullable with
a default. Ordered by optional dependencies, which is the axis this step is about.

| File | Lines | Public members | Ctor params | Optional |
| --- | ---: | ---: | ---: | ---: |
| `src/Meridian.Wpf/ViewModels/Accounting/AccountingConfigureViewModel.cs` | 5,357 | 223 | 18 | 15 |
| `src/Meridian.Wpf/ViewModels/MainPageViewModel.cs` | 2,313 | 126 | 14 | 12 |
| `src/Meridian.Wpf/ViewModels/FundLedgerViewModel.cs` | 3,325 | 135 | 12 | 4 |
| `src/Meridian.Wpf/ViewModels/SecurityMasterViewModel.cs` | 4,408 | 211 | 12 | 1 |

Fifteen optional dependencies describe 2¹⁵ possible service configurations, none of which the type
system distinguishes, so every method re-checks which collaborators exist. Split by workflow area
into child ViewModels, each taking a **non-nullable** dependency set. The optional-dependency count
falling is the real win; the line count falling is a side effect.

Note that size and coupling genuinely diverge here, which is the point of ordering by coupling.
`SecurityMasterViewModel` is the second-largest file in the table and the *least* coupled of the
four — 12 constructor parameters, one of them optional. It is a big class, not a tangled one, so it
belongs in a size-driven pass rather than this one. `MainPageViewModel` is the smallest of the four
and the second most coupled; it will fight back hardest per line removed.

**2. Browser workstation screens, along the seams the UI already has.**

`settings-screen.tsx` (7,397) and `accounting-screen.tsx` (6,126) decompose along their existing
tab/section boundaries. `accounting-screen.view-model.ts` (7,147) and
`trading-screen.view-model.ts` (5,243) split by the same boundaries so view and view-model stay
paired. This is where the volume is — 48% of the baseline.

**3. Endpoint and query-service monoliths.**

`WorkstationEndpoints.cs` (4,478) and `SecurityMasterWorkbenchQueryService.cs` (4,740) split by
capability into partial classes or composed services. `LedgerEndpoints.cs` is already partial and
demonstrates the pattern; the remaining work is moving capability logic out of the endpoint layer
into owning modules, per ADR-017 — which is the same move issue #2611 made for GL dimensions.

## `dev-fixtures.ts` — the exclusion case is weaker than the audit stated

`src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts` is 6,673 lines, **4% of the entire baseline**,
and the third-largest entry. Issue #2619 and the Aug 2026 audit both describe it as purely dev-only —
dynamically imported behind `import.meta.env.DEV` and tree-shaken from production — and recommend
excluding fixture files from the ratchet rather than refactoring them.

**That premise does not hold as stated.** `src/Meridian.Ui/dashboard/vite.config.ts:6` imports
`resolveDevFixture` from this module *statically*, and the config is evaluated by `vite build`; its
preview proxy calls the resolver at line 80. So the module is reachable from the build toolchain, not
only from a guarded runtime path. The fixtures may still be absent from the emitted browser bundle —
that part of the audit was not contradicted — but "dev-only, dynamically imported" is not an accurate
description of the file's import graph, and it was the load-bearing half of the argument for
excluding it.

Two things follow. The exclusion may still be the right call on effort grounds: refactoring fixture
data buys little. But it should be decided on that basis, with the static config import either
accepted or isolated first, rather than on a dev-only claim that is not true.

**This plan makes no exclusion.** Removing 6,673 lines from a guardrail's scope is a policy decision
that should be taken deliberately and visibly, not folded into a reporting change — and it would drop
the headline number by 4% without decomposing anything, exactly the metric movement this plan tries
not to reward. Raised here for a decision.

## Tracking

Re-measure by running the ratchet; the trend line is now part of its normal output:

```bash
python3 build/scripts/ci/check-file-size.py
```

After a decomposition lands, lock in the reduction:

```bash
python3 build/scripts/ci/check-file-size.py --update-baseline
```

Review the diff carefully — this command can raise a cap as well as lower one, which is why a
downward-only alternative is the missing mechanism described above. Caps should only ever fall.
