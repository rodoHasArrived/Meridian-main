# What To Work On Next — Meridian (2026-10-05)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-05
**Baseline:** `main` at `bf4a6a987856f4119d7d23aa161c6d4e654324b2`
**Supersedes:** [2026-10-04](next-work-determination-2026-10-04.md)
**Method:** re-verified every carried-forward claim against **current source on `main`** and against
live GitHub Actions *job logs* (not job conclusions — see the API caveat below); re-measured the whole
open pull-request queue with `git merge-tree --write-tree --name-only origin/main origin/<branch>`,
counting only `CONFLICT (...): Merge conflict in <path>` lines and splitting generated from
hand-written paths; reproduced the failing gate's advisory locally; read the roadmap registry
(`docs/roadmap/data/*.yml`) and the readiness tracker (`docs/product/implementation-todo-list.md`).

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; the tracker remains authoritative for the
> P0 release gate. Recheck conflict counts and CI results against the live head before acting.

## Why this document is short

The 2026-10-04 determination set an explicit bar: the next one is due **"when a certification run is
green again, or when the RC lands."** Neither has happened. It also predicted its own failure mode:
*"If a seventh is written before either happens, the ranking is not what needs attention."*

That prediction was correct, so this document does not re-rank the program. **The ranking is
unchanged and re-verified.** What follows is (1) proof that the top of the stack is still exactly
where it was, (2) three corrections and new findings that materially change what someone should do,
and (3) one item that this document closes rather than ranks.

## The P0 is unchanged, unaddressed, and now three days old

**`main` has produced no certifiable commit since 2026-10-02.** Verified today:

- **Run #144** ([37336412357](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37336412357))
  at today's head `bf4a6a98` — **failure**. Also red: **#143** (`11804328`) and **#137**
  (`2d537fd1`). Runs **#138–#142** were cancelled by concurrency, not scored. Together with
  **#131–#135** recorded by the predecessor, the last green run remains **#130** at `2dbd8723`,
  2026-10-02. (Run **#136** is the one run in the window this document did not read; it changes no
  recommendation below.)
- **Still exactly one job, at exactly one step.** In #144 the documentation-evidence, PostgreSQL
  integration, and encrypted backup/restore jobs are all green. `NuGet and npm dependency evidence`
  fails at step 10. The job log names the cause verbatim:

  ```
  npm-audit gate: UNACCEPTED GHSA-vfj7-8cjw-p6xm (braces, high): no acceptance entry
  npm-audit gate: FAIL — see unaccepted or stale entries above.
  ...
  Dependency evidence failed for: npm (validate-npm-audit.py)
  ```

  The NuGet gate passes (`if ('success' -ne 'success')` in the assert step's own echoed script).
- **`Meridian CI` is green.** Run **#4823** at `bf4a6a98` succeeded. Only the certification lane is
  red, which is why merges keep landing while the RC cannot be minted.
- **The acceptance register is still empty.** `build/config/security/npm-audit-accepted-advisories.json`
  → `"accepted": []`. The decision the predecessor ranked as the P0 unblock has not been made.
- **There is still no upstream fix.** `npm view braces versions` ends at **3.0.3**; the advisory's
  vulnerable range is `<=3.0.3`. `braces` remains `"dev": true` in the lockfile and `tailwindcss`
  remains pinned at `3.4.15` as a `devDependency`. The repo's `overrides` block still carries only
  `postcss` and `nanoid`, so no override can patch this.

**Recommendation: unchanged from 2026-10-04.** Register a time-boxed, reviewed acceptance for
`GHSA-vfj7-8cjw-p6xm` with the eight required fields (`validate-npm-audit.py:38-47`), mirror it as a
`KV-YYYY-NNN` record in `docs/security/known-vulnerabilities.md`, and set `review_by` to the Tailwind
4 migration decision date. Retire it in the same change that lands Tailwind 4, because the register
fails closed from both directions. **This document does not make that decision**: accepting a
security advisory is an owned governance call, and `CLAUDE.md` requires human review for CI
governance changes.

**The honest finding is about elapsed time, not analysis.** The unblock was scoped at "under an hour"
three days and eight scored red runs ago. No further ranking changes that. If the next determination
also opens on an empty register, the thing to examine is the decision path, not the backlog.

## Correction: the three "broken tracker links" were never broken

Three determinations have carried "fix the three broken internal links in the P0 tracker" as a P3
item, on the strength of `repair-links.py --summary` reporting them. **All three are false positives,
and `repair-links.py` is the defect.**

`build/scripts/docs/repair-links.py` collapsed consecutive hyphens when generating an anchor slug.
**GitHub's slugger does not collapse them.** For a heading containing a stripped character between
two spaces — an em dash, which these headings use — the two rendered hyphens survive on github.com
and were collapsed to one by the checker:

| | `docs/product/implementation-todo-list.md:64,91` |
| --- | --- |
| Heading | `## Tier 5 — W10: two rows await an operator session, one has an undefined criterion` |
| GitHub anchor | `tier-5--w10-two-rows-await-an-operator-session-one-has-an-undefined-criterion` |
| Checker's anchor | `tier-5-w10-two-rows-await-an-operator-session-one-has-an-undefined-criterion` |
| Link in tracker | `tier-5--w10-…-criterion` — **matches GitHub, not the checker** |

The same applies to line 107 (`W9-TRUTH-001 owner exception — 2026-09-26` →
`w9-truth-001-owner-exception--2026-09-26`).

**This matters beyond three links.** The recommendation on the books was to *fix the links*. Acting on
it would have rewritten three correct deep links into the checker's collapsed form — breaking them on
github.com to satisfy a bug, in the file that is the P0 release gate. A validator that is wrong in
the direction of "reports valid things as broken" invites exactly that repair.

**Closed in this change, not ranked:** the collapse step and its now-unused `_SLUG_COLLAPSE` pattern
are removed, the two comments documenting the wrong rule are corrected, and
`tests/scripts/test_repair_links_anchors.py` locks the behaviour with 6 cases including both real
tracker anchors. The repo now reports **1,875 links checked, 0 broken** (from 3), with no link
anywhere in 404 files depending on the collapsed form. The P3 item "fix the three broken tracker
links" is retired; **"consider gating link integrity in CI" survives and is now worth more**, because
the checker it would gate on no longer produces false positives.

## Correction: #3031 is provably a no-op, and it is not the braces fix

The predecessor recommended closing #3031 as obsolete. Confirmed with stronger evidence, and one
trap removed:

```bash
git diff --stat origin/main...origin/codex/brace-expansion-certification-fix   # (no output)
git log --oneline origin/main..origin/codex/brace-expansion-certification-fix | wc -l   # 2
```

**Two commits, zero net diff.** Its own head commit is *"Merge main and preserve landed
brace-expansion security fix"* — the substance landed by another route and the branch now contributes
nothing. Close it.

**The trap:** #3031 is titled *"fix(deps): clear brace-expansion production certification failures"*,
which reads like the fix for the lane that is red right now. It is not. **`brace-expansion` and
`braces` are different packages with different advisories.** The `brace-expansion` work has landed;
`GHSA-vfj7-8cjw-p6xm` in `braces` is untouched and is what fails run #144. Anyone scanning PR titles
for the certification fix will find #3031 and conclude the problem is already handled.

## Queue re-measured: 27 open PRs, and one branch is decaying fast

Measured today against `bf4a6a98`, splitting generated paths (`docs/generated/`,
`docs/source/generated/`, `docs/status/`, `docs/ai/generated/`, `docs/diagrams/`,
`src/Meridian.Ui/wwwroot/workstation/`) from hand-written:

| PR | Conflicts | Hand-written | Change since 10-04 | Disposition |
| --- | --- | --- | --- | --- |
| #3058, #3057, #3049, #3036, #3031, #2981, #2944, #2932 | **0** | **0** | #3049 was 3; #3058/#3057 new | **8 clean** |
| #2903 | 1 | 0 | — | mechanical |
| #3054, #3030, #3012, #2930, #2928 | 2 | 0 | — | mechanical |
| #3037, #3016, #2920 | 3 | 0 | #3037 newly measured | mechanical |
| #2826 | 4 | **0** | unchanged | **read before merging — expect to close** |
| #3050 | 4 | 1 | — | `contracts.json`; follows #3048 |
| #2307 | 2 | 0 | — | mechanical |
| #2896 | 6 | 0 | was 5 | mechanical |
| #3048 | 6 | 1 | — | **registry parse fix — still the one to merge** |
| #3004 | 8 | 1 | was 7 | `contracts.json` only |
| #3041 | 9 | 1 | — | `contracts.json`; `W10-PROV-001` still `planned` |
| #3053 | 5 | 1 | new | 2026-10-03 determination; `plans/README.md` |
| #2789 | **41** | **28** | **was 25 / 15** | **decaying — re-cut, do not resolve** |
| #2587 | 340 | 269 | unchanged | re-cut; do not merge |

**8 clean, 12 generated-only, 7 with hand-written conflicts.** The clean count fell from 10 as #2878
(`lucide-react`) closed and reopened as **#3058** (`0.468.0 → 1.49.0`, still a major — it waits) and
**#3057** (`react-router-dom` patch) arrived.

**#2789 is the one real change in the queue's shape.** It went 25 → 41 conflicts and **15 → 28
hand-written**, now spanning backtesting, execution, storage, five READMEs and the database manifest.
It has carried `[DRAFT — RE-CUT REQUIRED]` in its own title for weeks; this measurement is what that
label costs when it is left open. Re-cut it against current `main` or close it — resolving 28
hand-written conflicts across those subsystems is strictly more work than redoing the change.

**The merge driver case is unchanged and still the best P3 in the program:** 12 PRs are blocked on
nothing but regenerating files a script already knows how to regenerate.
`build/scripts/resolve-generated-merge-conflicts.py:19-40` still excludes `docs/diagrams/`,
`docs/ai/generated/`, and the reviewed source-hash manifest.

## Still true, still unaddressed (verified today, not restated from the record)

- **The roadmap registry still does not parse, and its validator still passes.**
  `yaml.safe_load(docs/roadmap/data/roadmap-items.yml)` → `ScannerError: mapping values are not
  allowed here, line 797, column 886`, while `validate-roadmap-registry.py` **exits 0** against that
  same file. The silent fallback parser in `build/scripts/docs/common.py` is why. The fix is
  implemented in **#3048**, which quotes the offending scalars and deletes the fallback. Merge it;
  do not reimplement it. #3050 declares a dependency on it.
- **`W10-LOT-002`** remains the slate's only `critical` row, now `in_progress`.
  `grep -rn "Successor" src/Meridian.Contracts/Accounting/Lots/` returns **nothing** on `main` again
  today, so append-only predecessor/successor corporate-action lot mutations are still absent from
  the lot contracts. The #3048 → #3050 chain is the route.
- **Row hygiene, one day older:** `W8-WPF-PARITY-001` **91 days** unreviewed (`in_progress`, `high`,
  an active co-equal UI lane and named productization target); `W8-UX-CONSOL-001` **78 days**;
  `W9-CORPACT-011` **13 days** in `ready_for_acceptance` with its precondition met on PostgreSQL —
  **declining is a result too**; `W9-GOV-008` (7 days) and `W9-INGEST-009` (10 days) were both last
  reviewed before the PRs that implemented their open criteria merged. `W10-PROV-001` and
  `W10-RECON-001` are both still `planned` and last reviewed **2026-07-31**, while #3041 implements
  `W10-PROV-001`.
- **Four determinations remain open at once** (#3012, #3030, #3053, plus this one). The predecessor's
  recommendation to close #3012, #3030 and #3053 unmerged stands and is a maintainer call.

## New method caveat: the Actions API will hand you a stale queue

Anyone re-running this analysis should know: `list_workflow_runs` returned **stale pages** for both
workflows depending on the filter. Asking for `production-certification.yml` with
`branch: main` reported `total_count: 22` with run **#111** (2026-09-29) as newest; asking with
`status: completed` and no branch reported `total_count: 144` with **#144** (today) as newest. The
same split hit `meridian-ci.yml` — `status: completed` returned #4606 from 2026-09-29, while adding
`event: push` returned **#4823** from today.

**Cross-check the newest run's `created_at` against the head commit's date before trusting any
conclusion drawn from a run listing.**

**A determination PR must declare a phase, and a body edit will not deliver it.** Any PR touching
`docs/status/**` or `build/scripts/docs/**` triggers `Roadmap Source Docs`, whose `scope-gate` job
requires an explicit phase from `tools/roadmap/enforce_phase_scope.py` — a `phase:PRx` label, a
`<!-- phase:PRx -->` body marker, or a dispatch input. Two things are easy to get wrong:

1. **Once a phase is declared the gate checks every changed file, not just the governed ones.** A
   determination that also edits a script under `build/` needs **PR6** (`build/**`); `docs/**` alone
   is PR1 and `tests/**` is PR4. Take the minimum that passes — the gate's own hint says to widen
   only when roadmap governance allows. Generated artifacts (`docs/generated/**`,
   `doc-health-dashboard.*`, `example-validation.md`) are exempt, so a docs-only determination needs
   no marker at all.
2. **Adding the marker to the PR body does not re-deliver it.** The job reads
   `github.event.pull_request.body`, and the workflow's `pull_request` trigger does not include
   `edited` or `labeled`, so a body edit fires nothing. Re-running the failed job replays the
   *original* event payload, so the gate still reads the pre-edit body and fails identically. Only a
   new commit (a `synchronize` event) carries the updated body. Declare the phase **when the PR is
   opened**, or expect to spend a commit on it. This compounds the predecessor's correction — read **check
runs**, not commit statuses — and the one before it: in this lane read step **`outcome`**, not
`conclusion`. In run #144 both gate steps report `conclusion: success` while the job fails, because
they are `continue-on-error`; the only reliable source is the job **log**.

## What not to work on

- **New product surface.** The `program-state.yml` `scope_gate` is explicit, and nine rows are in
  progress or awaiting acceptance across three waves.
- **The Tailwind 4 migration, today.** The only true remediation; schedule it post-RC as its own
  lane, retiring the acceptance in the same change.
- **Re-running the certification lane hoping for green.** Deterministic against the live advisory
  database until the register decision is made.
- **"Fixing" the three tracker links.** They were never broken. The checker was.
- **Re-implementing the registry YAML fix.** It exists in #3048 and parses clean.
- **Majors before the freeze.** #3058 (`lucide-react` 0 → 1) and #2587 (`react-router`) wait.
- **Resolving #2789's conflicts.** 28 hand-written across six subsystems. Re-cut or close.
- **Untracking the built asset tree.** `PRD-013` and `PRD-018` both require one tracked canonical
  tree; fix the merge driver.
- **Writing an eighth determination before the bar is met.** It is unchanged: a green certification
  run, or the RC.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0** | Register the time-boxed acceptance for `GHSA-vfj7-8cjw-p6xm`, or consciously accept a red lane | Eight scored red runs; no certifiable commit for three days. No patched `braces` exists at any severity. An owned governance decision, not engineering |
| **P0** | Merge #3048 (base merge + regenerate docs) | Fixes the registry parse break **and** deletes the silent fallback parser; verified again today that both defects persist on `main` |
| **P0** | Then: signing secrets, the ~1 GB installer decision, freeze a green head, tag `v0.1.0-rc.1` | Unchanged and not engineering work; blocked on a green run, not the version scheme |
| **P1** | Drain the 8 clean PRs once the gate is green; resolve the 12 generated-only conflicts | A green gate is what makes the pre-merge signal meaningful again |
| **P1** | Re-cut or close #2789 | **25 → 41 conflicts, 15 → 28 hand-written in one day.** The only branch actively getting worse |
| **P1** | Take or decline `W9-CORPACT-011` (**13 days**) | Zero engineering; precondition met on PostgreSQL |
| **P1** | Read #2826 against current `main` — expect to close | 4 conflicts, **0 hand-written**: trivially mergeable and the worst candidate for a reflex merge |
| **P1** | Refresh `W8-WPF-PARITY-001` (**91 days**) and `W8-UX-CONSOL-001` (**78 days**); re-review `W9-GOV-008` and `W9-INGEST-009` | Both W8 rows are named productization targets; the W9 rows predate the PRs that implemented their criteria |
| **P2** | Close #3031 — **two commits, zero net diff** | Proven no-op. Its title names `brace-expansion`, not the `braces` advisory that is actually red |
| **P2** | Reconcile `W10-PROV-001`/`W10-RECON-001` (`planned` since 2026-07-31) against #3041 | A `planned` row has an open implementation PR |
| **P2** | Continue `W10-LOT-002` via #3048 → #3050 | The only `critical` row; successor mutations verified absent again today |
| **P2** | Close the stale determinations #3012, #3030, #3053 | Maintainer call; their substance has landed or been superseded |
| **P3** | Implement the `.gitattributes` merge driver / documented regenerate-on-merge | **12 PRs blocked on nothing else.** Recurring by construction |
| **P3** | Gate link integrity in CI | Now worth more: the checker no longer emits false positives, so a gate would mean something |
| **P3** | Schedule Tailwind 3 → 4 post-RC, retiring the acceptance in the same change | The register fails closed on stale entries |

## On archiving the predecessors

Convention (`docs/product/plans/README.md`) says to move a superseded determination into
`archive/docs/plans/`. The 2026-09-27 document is again **left in place**: `program-state.yml:43`
cites it by path as the recorded operator-session plan and the tracker deep-links its section
anchors. **Those deep links are now verified working** rather than listed as broken, which removes the
one argument that the references were already degraded. Archiving 2026-09-27, 2026-10-02 and
2026-10-04 with the `program-state.yml` and tracker references updated in the same pass remains a
separate governance-reviewed step.
