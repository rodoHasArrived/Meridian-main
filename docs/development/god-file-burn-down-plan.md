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

Measured 2026-08-11 against the ratchet's own scanner (threshold 2,000 lines):

| Metric | Value |
| --- | ---: |
| Baselined files | 50 |
| Capped lines | 169,329 |
| Reclaimable slack (current below cap) | **1 line** |

By surface:

| Surface | Files | Lines | Share |
| --- | ---: | ---: | ---: |
| C# (`.cs`) | 29 | 87,858 | 52% |
| TypeScript (`.ts`) | 15 | 57,891 | 34% |
| TSX (`.tsx`) | 6 | 23,579 | 14% |

The browser workstation is **48% of the baselined debt** (21 files, 81,470 lines) despite being one
of several surfaces. Any plan that only addresses C# ViewModels leaves half the problem untouched.

## The finding that shapes the plan

**49 of the 50 baselined files sit at exactly zero headroom.** One file has a single line spare.
None has more.

That is not the occasional cliff issue #2619 describes — it is the steady state of the entire
baseline. Adding one line fails CI today for 49 of the 50 — the ratchet rejects only `lines > cap`,
so the file with a line spare would reach its cap and still pass, once. The practical consequences:

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
problem is a prerequisite, not a nicety — which is why the mechanism changes below come first.

## Mechanism changes (this change)

Reporting only. No enforcement rule is altered, so nothing that passes today starts failing.

1. **Trend reporting.** Every run prints baselined file count, total capped lines, total current
   lines, and reclaimable slack. The number the plan is trying to move is now visible in CI output
   instead of requiring a script to compute.
2. **Headroom warnings.** Files within 25 lines of their cap are listed as `TIGHT`. The wall is
   visible before a contributor hits it, and the report names decomposition candidates by urgency
   rather than by size.
3. **`--tighten-baseline`.** Rewrites caps toward current line counts but **only downward** — it can
   never record growth, unlike `--update-baseline`. This is what makes reclaimed lines permanent:
   run it after a decomposition and the ground gained cannot be silently given back. It refuses to
   run under a threshold other than the baseline's own, since scanning at a higher threshold would
   retire files the baseline still protects.
4. **`--buffer N`.** Tightening to the exact current count leaves the file pinned again, which is
   the state this plan exists to escape. `--tighten-baseline --buffer 25` locks in the reduction
   while asking for 25 lines of working room. It still never raises a cap above its existing value,
   so a file that has barely moved keeps whatever headroom its old cap allowed — the command reports
   the smallest amount actually retained rather than the amount requested. An entry is also held
   rather than retired until the threshold itself provides the requested room, so a file parked just
   under 2,000 lines cannot lose its cap and then fail as a brand-new god file on the next line.

The distinction matters. `--update-baseline` is the escape hatch and should stay visible in review
as tracked debt. `--tighten-baseline` is the ratchet actually ratcheting, and is safe to run
routinely.

## Targets

Expressed against the 169,329-line baseline recorded above.

| Horizon | Target | Rationale |
| --- | --- | --- |
| Immediate | After each decomposition run `--tighten-baseline --buffer 25` | Locks in the reduction while leaving the file editable |
| Per release | Retire **at least 2 files** from the baseline entirely | File count is the honest unit — a file drops out only when it is genuinely decomposed |
| Per quarter | Reduce total capped lines by **15%** (~25,400 lines) | Matches the reduction rate issue #2619 proposes |

Prefer *files retired* over *lines removed* as the headline metric. Lines can fall by moving code
sideways into a new file that is itself close to the threshold; a file leaving the baseline means a
real seam was found.

## Sequencing

**1. Highest-coupling C# ViewModels first — not the largest files.**

| File | Lines | Public members | Injected deps |
| --- | ---: | ---: | ---: |
| `src/Meridian.Wpf/ViewModels/Accounting/AccountingConfigureViewModel.cs` | 5,358 | 224 | 18 (13 nullable) |
| `src/Meridian.Wpf/ViewModels/SecurityMasterViewModel.cs` | 4,408 | 212 | 20 |
| `src/Meridian.Wpf/ViewModels/FundLedgerViewModel.cs` | 3,325 | 152 | 14 |
| `src/Meridian.Wpf/ViewModels/MainPageViewModel.cs` | 2,313 | 134 | 19 |

Thirteen nullable dependencies describe 2¹³ possible service configurations, none of which the type
system distinguishes, so every method re-checks which collaborators exist. Split by workflow area
into child ViewModels, each taking a **non-nullable** dependency set. The dependency count falling
is the real win; the line count falling is a side effect.

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

## `dev-fixtures.ts` — exclude rather than refactor

`src/Meridian.Ui/dashboard/src/lib/dev-fixtures.ts` is 6,673 lines, **4% of the entire baseline**,
and is the third-largest entry. It is also dev-only: dynamically imported behind
`import.meta.env.DEV`, tree-shaken from production builds, and verified by the Aug 2026 audit as not
leaking into production.

Refactoring it would spend real effort for no production benefit. The recommendation is to exclude
fixture files from the ratchet's scan — the same treatment `_is_excluded` already gives test files
and generated sources, and for the same stated reason: *"tracked debt of a different kind"* that
"would only add noise here."

**This plan does not make that exclusion.** Removing 6,673 lines from a guardrail's scope is a policy
decision that should be taken deliberately and visibly, not folded into a reporting change — and it
would drop the headline number by 4% without decomposing anything, which is exactly the kind of
metric movement this plan is trying to avoid rewarding. Raised here for a decision.

## Tracking

Re-measure by running the ratchet; the trend line is now part of its normal output:

```bash
python3 build/scripts/ci/check-file-size.py
```

After a decomposition lands, lock in the reduction while keeping the file editable:

```bash
python3 build/scripts/ci/check-file-size.py --tighten-baseline --buffer 25
```

Drop `--buffer` when retiring a file or when you want the cap pinned exactly. Either way, review the
diff — caps should only ever fall.
