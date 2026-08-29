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

**For current figures, run the ratchet — do not trust the numbers below.**

```bash
python3 build/scripts/ci/check-file-size.py
```

It prints tracked files, capped lines, current lines and reclaimable slack on every run, including
`--update-baseline`. Those totals move whenever anything lands on `main` that touches a baselined
file, so any figure written into this document is stale by the next merge. The snapshot below is
kept for the shape of the problem, not as a live reading.

Snapshot at `bcd8295a` (threshold 2,000 lines):

| Metric | Value |
| --- | ---: |
| Baselined files | 50 |
| Capped lines | 169,329 |
| Current lines | 169,280 |
| Reclaimable slack (current below cap) | **49 lines** |

By surface, counted from the baseline's recorded caps:

| Surface | Files | Lines | Share |
| --- | ---: | ---: | ---: |
| C# (`.cs`) | 29 | 87,859 | 51% |
| TypeScript (`.ts`) | 15 | 57,891 | 34% |
| TSX (`.tsx`) | 6 | 23,579 | 13% |

The browser workstation is **48% of the baselined debt** (21 files, 81,470 lines) despite being one
of several surfaces. Any plan that only addresses C# ViewModels leaves half the problem untouched.

## The finding that shapes the plan

**Every baselined file is within 25 lines of its cap, and nearly all sit at exactly zero headroom**
— 46 of 50 in the snapshot above, and the count has only ever moved by the handful of lines a
merge happens to reclaim.

That is not the occasional cliff issue #2619 describes — it is the steady state of the entire
baseline. For a pinned file, adding one line fails CI; the few with slack reach their cap and pass
once, because the ratchet rejects only `lines > cap`. The practical consequences:

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
problem is a prerequisite, not a nicety.

**Resolved: `--relax-baseline` is that mechanism** (see "Granting working headroom" below). Running
it raised 43 of the 50 caps and granted 1,896 lines of working headroom, and the `TIGHT` list — 39
files, 23 of them at exactly zero spare — is now empty. The pressure this section describes is what
it removes: an ordinary edit to a god file no longer forces a choice between an unrelated refactor
and a `--update-baseline` commit, so the ratchet stops manufacturing the baseline bumps it exists
to prevent.

The slack that does exist is the trend working. It appears when a change lands on `main` that
removes lines from a file whose cap this baseline still carries — 1 line when this plan was first
written, 49 by `bcd8295a`, with the pinned count falling 49 → 48 → 46 over the same span. That is
exactly the signal the reporting below exists to make visible, and it is also why the
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

## Locking in a reduction

After a decomposition lands, lock the reclaimed lines in with the downward-only tightener (#2675):

```bash
python3 build/scripts/ci/check-file-size.py --tighten-baseline           # default 50-line buffer
python3 build/scripts/ci/check-file-size.py --tighten-baseline --buffer 25
```

Its contract, shaped by the six defects review found in a withdrawn first attempt:

- **A cap only moves down** — each becomes `min(old cap, current lines + buffer)`. Raising a cap
  still requires `--update-baseline` plus justification in review.
- **The buffer is working headroom, not slack.** It is recorded in the baseline's `headroom` map,
  so the trend report does not count it as reclaimable and nothing recommends re-pinning it away.
- **An entry retires only when the threshold itself supplies the requested headroom**
  (`lines + buffer <= threshold`). A file one line under the threshold keeps a lowered cap rather
  than being handed the harder brand-new-god-file failure. The one exception is a genuinely
  deleted file, which always retires — there is nothing left to protect, whatever the buffer —
  while a file that merely became empty follows the ordinary rules.
- **It refuses to run while the ratchet is failing**, and **fails closed on any unreadable governed
  source**, tracked or not — a file that cannot be read is not a file with zero lines, and a
  directory that cannot be enumerated hides every governed file under it. Deleted-file
  reductions are counted when the entry retires.
- `--buffer` outside tightening, or `--tighten-baseline` with an explicit `--threshold`, are hard
  errors rather than accepted-and-ignored options.

The reclaimable figure in the trend report is therefore exactly the reduction not yet locked in;
run the tightener whenever it is non-zero and the tree is green.

## Granting working headroom

`--tighten-baseline` cannot solve the pinning problem, because it only ever lowers a cap: a file
already at `cap == lines` computes `min(old cap, lines + buffer) == old cap` and stays pinned. The
upward counterpart grants the buffer instead:

```bash
python3 build/scripts/ci/check-file-size.py --relax-baseline            # default 50-line buffer
python3 build/scripts/ci/check-file-size.py --relax-baseline --buffer 25
```

Its contract mirrors the tightener's, inverted where the direction demands it:

- **A cap only moves up** — each becomes `max(old cap, current lines + buffer)`. Lowering stays the
  tightener's job; reclaiming here would silently destroy a deliberate reduction.
- **Only the buffer counts as deliberate headroom.** A file whose cap sits far above its current
  size keeps reporting the rest of that gap as reclaimable, so granting room to edit never
  disguises itself as progress. Headroom a previous tightening recorded is never reduced.
- **Nothing retires.** Dropping a protection is a reduction-locking act and belongs to the
  tightener; a command that grants headroom must not also remove caps.
- **It refuses while the ratchet is failing**, and **fails closed on any unreadable governed
  source** — the same two guards, and the first matters more here: relaxing a failing tree is
  precisely how a file that already grew past its cap would be waved through. Absorbing real growth
  still requires `--update-baseline` plus justification in review.

Use it when the `TIGHT` list is long enough that ordinary edits are being taxed. It is not a
substitute for decomposition: it moves no file out of the baseline and reduces no capped line. It
buys the room in which the extractions this plan depends on can actually be made.

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

`WorkstationEndpoints.cs` (4,478) decomposes into **independent capability groups**, not into more
partials of itself. [Module conventions](../architecture/module-conventions.md) names that
distinction explicitly: a capability group is a static class exposing one `Map…` extension that
registers its own `MapGroup("/api/workstation/<capability>")`, and *"accreting handlers onto a single
`partial class` such as `WorkstationEndpoints`"* is called out there as an anti-pattern being
retired. `FundAccountEndpoints.cs` and `FundStructureEndpoints.cs` are the models.

The distinction is about what a class owns, not about the `partial` keyword — `FundStructureEndpoints`
is itself spread over six files. Splitting a group that already owns one capability is fine;
splitting the monolith into more partials keeps one class owning many capabilities and moves nothing.

`SecurityMasterWorkbenchQueryService.cs` (4,740) is the same shape one layer down and splits into
composed services. For both, the line count falls as a consequence of moving capability logic into
owning modules per ADR-017 — the same move issue #2611 made for GL dimensions.

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
python3 build/scripts/ci/check-file-size.py --tighten-baseline
```

This only ever lowers caps and retains a working buffer above each file's current size (see
"Locking in a reduction" above).

If the `TIGHT` list grows back — caps drift to `cap == lines` as edits land — restore working
headroom with the upward counterpart (see "Granting working headroom"):

```bash
python3 build/scripts/ci/check-file-size.py --relax-baseline
```

`--update-baseline` remains the escape hatch for absorbing growth a file has *already* made, and
its diff should be justified in review. The distinction is the point: relaxing grants room to edit
a file nobody grew, updating records growth that already happened.
