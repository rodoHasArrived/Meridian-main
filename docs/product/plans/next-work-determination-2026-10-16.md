# What To Work On Next — Meridian (2026-10-16)

**Snapshot:** Historical snapshot - superseded by roadmap registry for current status.

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-16
**Baseline:** `origin/main` at `e5c20ca77ce154e7a3eac4d7cc3b24103faf6202` (2026-10-16 09:13 UTC)
**Supersedes:** nothing. It **extends** the unmerged
[2026-10-15 determination (#3170)](https://github.com/rodoHasArrived/Meridian-main/pull/3170), whose
ranking is re-verified below and carried forward intact. The newest determination on `main` remains
[2026-10-05](next-work-determination-2026-10-05.md)
**Method:** read every `push` workflow run at the head commit by `head_sha` and the last twelve
`Meridian CI` main runs by conclusion; pulled the **job logs** of each failing run and extracted the
named failing module rather than trusting job conclusions; re-ran the failing lane locally
(`build/scripts/ci/run-script-tests.py`) and recorded where the local environment diverges from CI;
read all 38 runs of the `Generated Repair Publisher` workflow and traced its exit path in source;
re-measured the active open pull requests with
`git merge-tree --write-tree --name-only origin/main origin/<branch>`, counting only
`CONFLICT (...): Merge conflict in <path>` lines and classifying each path as generated or
hand-written; parsed the roadmap registry with `yaml.safe_load`; read the ADR status lines and the
tag list in current source.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; `docs/product/implementation-todo-list.md`
> remains authoritative for the P0 release gate. Recheck conflict counts and CI results against the
> live head before acting.

## Why this document exists, and why it does not re-rank

The 2026-10-15 determination set its bar as: *"due when the RC is tagged, or when a signature it
names is refused."* **Neither has happened**, and its ranking is re-verified today:

- `docs/adr/019-production-support-matrix-and-deployment-posture.md:3` still reads
  **`Proposed (awaiting core-team sign-off; signing this ADR is the PRD-000 "signed support matrix"
  artifact)`**.
- `docs/adr/020-lifecycle-control-plane.md:3` still reads **`Proposed`**.
- The repository still has **exactly one tag**, `eval-v0.1.0-eval.1`. No `v*` tag exists.

So the P0 path is unchanged: four signatures, one secret, one product decision. **No part of it is
analysis, and this document does not re-derive it.** Read #3170 for that ranking.

What follows is only what changed in the last twenty-four hours, is not in any open document, and
changes what someone should do today. All three items are about the delivery system, not the
backlog.

## New P0: the authoritative merge gate is no longer reliable on `main`

`CLAUDE.md` names GitHub Actions `Meridian CI / quality-gate` the authoritative merge gate. **It is
red on `main`'s head, and it has been red for a third of recent pushes** — while merges continued.

Twelve most recent `Meridian CI` push runs on `main`: **four failures.** Three are confirmed to be
the same lane, `verify-workflows`, and each failed on a **different** test:

| Run | Head | Failing module | Cause in the log |
| --- | --- | --- | --- |
| [#5432](https://github.com/rodoHasArrived/Meridian-main/actions/runs/38878990123) | `e5c20ca7` (**current head**) | `test_schema_control_disposable.test_concurrent_worktrees_and_same_worktree_runs_are_isolated` | `AssertionError: [PosixPath('/tmp/tmpeddy4aqu/backend/resource-11fb29cfe0524bf78c88f2dd3720836c.json')] is not false` |
| [#5421](https://github.com/rodoHasArrived/Meridian-main/actions/runs/38837590796) | `bd1f9210` | `test_merge_reviewed_doc_hashes.test_ignored_dependency_symlinks_do_not_affect_source_hashes` | `OSError: [Errno 39] Directory not empty: '/tmp/tmpld7ddgxd/.git'` — raised inside `tempfile` cleanup, not by an assertion |
| [#5385](https://github.com/rodoHasArrived/Meridian-main/actions/runs/38794000163) | `2a191564` | `verify-workflows` (module not extracted) | lane failed; not root-caused here |
| [#5380](https://github.com/rodoHasArrived/Meridian-main/actions/runs/38784023273) | `32d1dba8` | not examined | — |

Three readings matter more than the count:

1. **None of these is product code.** All are repo-script test-harness teardown — a leaked fake
   resource file after `SIGTERM`, and a `TemporaryDirectory` cleanup that raced a live `.git`
   directory. The .NET, browser and docs lanes were **green in every one of these runs**, as were
   the PostgreSQL `integration-gate` and, on the current head,
   [`Production Certification` #250](https://github.com/rodoHasArrived/Meridian-main/actions/runs/38878990126).
2. **The `#5421` failure is not a defect at all** — it is Python's `TemporaryDirectory` raising
   because the tree mutated during teardown. It is deterministic to remove, and the removal is
   systemic: **`tests/scripts` has 94 files calling `tempfile.TemporaryDirectory(` and zero using
   `ignore_cleanup_errors`.** That is 94 call sites with the same latent failure mode gating `main`.
3. **The current head's failure is a real race, and it is a second attempt at the same bug.** The
   assertion at `tests/scripts/test_schema_control_disposable.py:419` requires every allocated
   resource to be gone after three concurrent runs are `SIGTERM`-ed. The cleanup it exercises
   reconciles a late allocation only while `allocation_uncertain` holds
   (`tools/schema_control/disposable.py:432-443`); outside that window it records
   `"container absent"` and removes nothing. The most recent commit to that test file is
   `bcacfc2f` (2026-10-07) — *"Wait for owned container creation before cancellation cleanup"* —
   an earlier fix for this same window. **This module passed locally (17 tests), so this is
   confirmed in CI and not reproduced locally; the window above is the suspect, not a proven cause.**

**Why this is a P0 and not lane hygiene.** #3170's fourth P0 is *"freeze a full-coverage green
commit and tag the RC."* That instruction now has no target: `main` does not reliably produce a
commit with all lanes green, and the last commit with a retained eight-workflow receipt,
`e0ecd952` (2026-10-14), is two days and three merges behind the head. A gate that is red a third
of the time while merges proceed is not gating anything, and the tracker's release gate requires
every P0 complete **on the same release commit**.

**The lane is intermittent, not broken, and that is the harder problem.** This document's own PR
([#3187](https://github.com/rodoHasArrived/Meridian-main/pull/3187)) ran `verify-workflows` **green**
on `d38694dd` at 16:24 UTC, with all 17 checks passing. A lane that passes most of the time is
exactly the one that gets waved through, which is how four red pushes accumulated on `main` without
anyone stopping. Pass/fail on a single head proves nothing either way here; the four runs tabulated
above are the evidence.

**Recommendation.** Fix the two named races — `ignore_cleanup_errors=True` across the
`tests/scripts` `TemporaryDirectory` call sites, and the cancellation/allocation window in
`disposable.py` — then re-run the lane on the head before anyone picks an RC commit. **Do not
quarantine either module:** the quarantine file is for tracked defects with owners and review
dates, the repository's own rules forbid skipping tests to reach green, and a release cannot be
certified on a gate that was silenced to pass.

## New P1: the `Generated Repair Publisher` has never once succeeded

**Read the workflow file, not the check name.** Two sibling workflows share the "generated repair"
prefix, and only one is broken:

| Workflow file | Trigger | Check surfaced on a PR | State |
| --- | --- | --- | --- |
| `.github/workflows/generated-repair.yml` | `pull_request_target` (`prepare`) | `Prepare generated repair patch` | **works** — green on this document's own PR |
| `.github/workflows/generated-repair-publish.yml` | `workflow_run` (`inspect` → `publish`) | none; runs after the fact on `main` | **never green** |

The finding below is **only** about the second. A green `Prepare generated repair patch` check does
not contradict it, and mistaking one for the other is the easiest way to dismiss this.

`generated-repair-publish.yml` landed on `main` yesterday at 00:00 UTC in
[#3179](https://github.com/rodoHasArrived/Meridian-main/pull/3179). Of its **38 runs**, the 7
`push`/`schedule` runs were `skipped` and **all 31 `workflow_run` runs failed. There is no green run
in its history.**

The cause is one line, and the workflow's own conditions prove the intent. `inspect` returns
`{"status": "manual", ...}` as a **normal** outcome in four places
(`build/scripts/ci/generated_repair.py:302, 333, 343, 346`), and
`.github/workflows/generated-repair-publish.yml:62,86` gate every later step on
`status == 'ready' || status == 'retry'` — so `manual` is designed to skip the rest and finish
green. But the exception handler writes the *same* `manual` status and **returns 1**:

```text
except (RepairError, OSError, ValueError, KeyError, zipfile.BadZipFile) as exc:
    write_outputs({"status": "manual", "reason": str(exc)}, args.result)
    return 1
```
`build/scripts/ci/generated_repair.py:544-546`

Every observed run takes that path via `RepairError("Target run is not bound to its base SHA")`
(`:213`) and reports a benign, by-design disposition as a workflow failure. Fixing it is a
one-line change — return 0 for a `manual` disposition, reserving non-zero for genuine faults —
plus a regression test in the existing `tests/scripts/test_generated_repair.py`.

**This is P1 rather than P3 because of what it was built to do.** See the next item.

## New P1: the open queue's conflicts are now 100% generated files, and the resolver already covers them

Re-measured today against `origin/main`. Twelve active open PRs; six are conflicted, and
**every one of the 15 conflicts is in a generated file. Zero hand-written conflicts anywhere in the
active queue:**

| PR | Conflicts | Files | Conflicting paths |
| --- | --- | --- | --- |
| #3186 | **0** | 124 | — |
| #3185 | **0** | 1 | — |
| #3184 | **0** | 3 | — |
| #3175 | **0** | 172 | — |
| #3170 | **0** | 7 | — |
| #3162 | **0** | 65 | — |
| #3180 | 2 | 41 | `docs/status/doc-health-dashboard.{json,md}` |
| #3176 | 2 | 60 | `docs/status/doc-health-dashboard.{json,md}` |
| #3169 | 2 | 134 | `docs/status/doc-health-dashboard.{json,md}` |
| #3168 | 2 | 131 | `docs/status/doc-health-dashboard.{json,md}` |
| #3177 | 3 | 19 | the two above + `docs/source/generated/source-hash-manifest.json` |
| #3164 | 3 | 13 | the two above + `docs/source/generated/source-hash-manifest.json` |

`docs/status/doc-health-dashboard.md` declares itself *"Auto-generated from canonical JSON payload."*
**All three paths are already enumerated in `build/scripts/resolve-generated-merge-conflicts.py`**
(`:23-24` and `REVIEWED_MANIFEST` at `:42`), which `build/scripts/repair-generated.py:23` imports —
the exact pair the `Generated Repair Publisher` exists to run.

So the two findings are one finding: **the queue's entire remaining conflict load is regenerable
output that an existing, tested resolver already handles, and the automation that would apply it has
failed 31 times out of 31 since it shipped.** This confirms #3170's P3 item (the `.gitattributes`
merge driver / regenerate-on-merge) but changes its shape — the churn has moved from the workstation
bundle to the doc-health dashboard — and it argues for promoting that item, because one one-line fix
now stands between the resolver and six PRs.

**Note a change in kind, not just count:** #3170 measured 564 conflict lines across six PRs,
dominated by `rename/rename` churn inside the built workstation bundle. That is gone from the active
queue today. What remains is smaller and entirely automatable.

## Sequencing warning: #3186 and #3180 add gating surface to the lane that is failing

[#3186](https://github.com/rodoHasArrived/Meridian-main/pull/3186) proposes integrating #3180, #3177,
#3164 and #3162, and is clean (0 conflicts, 124 files).
[#3180](https://github.com/rodoHasArrived/Meridian-main/pull/3180) **drains
`build/scripts/ci/script-test-quarantine.json`**: it carries all three currently quarantined modules
(`test_desktop_screen_blueprint_checklist`, `test_live_execution_controls_route_consistency`,
`test_export_project_artifact_workflow`) plus their doc owners.

Clearing the quarantine is correct work and the entries are due for review 2026-10-28. But it moves
three modules into a lane that is **already failing a third of the time for unrelated reasons**, and
#3180 touches neither race named above. Land the race fixes first, or land #3180 knowing the lane's
red rate will be harder to attribute. #3180 is also one of the six PRs whose only conflict is the
doc-health dashboard, so the P1 above unblocks it mechanically.

## What has not changed

- ~~**The roadmap registry is untouched since the 2026-10-14 review.** Parsed on 2026-10-16: **42
  rows — 17 `done`, 9 `planned`, 9 `in_progress`, 6 `accepted`, 1 `ready_for_acceptance`** — exactly
  the inventory the readiness tracker records. No row moved in two days.~~ **Superseded within a day —
  see [Update (2026-10-17)](#update-2026-10-17-three-of-this-documents-readings-moved-overnight).**
  The registry now holds **48 rows**.
- **The user-requested work queue is unchanged:** `W10-LOT-002`, then `W10-MARK-001`, then
  `W10-SEAM-001`, before `W10-RECON-001` in that queue only. Registry ranking remains MARK 1,
  RECON 2, PROV 3, JRNL 5, SEAM 7, LOT 12.
- **`W9-CORPACT-011` is still the program's only `ready_for_acceptance` row** — now **24 days** in
  that posture. #3170 ranked taking or declining it as P1; declining is still a result.
- Every product item in #3170's summary table stands as written.

## Update (2026-10-17): three of this document's readings moved overnight

`main` advanced from `e5c20ca7` to **`e447587e`** while this document sat in review, taking
[#3186](https://github.com/rodoHasArrived/Meridian-main/pull/3186) (which carried #3180, #3177, #3164
and #3162) and [#3185](https://github.com/rodoHasArrived/Meridian-main/pull/3185). Re-verified
against the merged tree:

**1. Correction — the registry is no longer unchanged. It now holds 48 rows, not 42.** #3185
registered **six new `W11` rows** (`W11-PASSPORT-001`, `W11-COVER-001`, `W11-STAKE-001`,
`W11-SHADOW-001`, `W11-PACK-001`, `W11-EXEC-001`), all `planned`. The count is now **17 `done`, 15
`planned`, 9 `in_progress`, 6 `accepted`, 1 `ready_for_acceptance`**. The `done`/`in_progress`/
`accepted`/`ready_for_acceptance` postures are untouched, so nothing in the ranking above changes —
but "no row moved" is no longer a true statement, and the readiness tracker's "42 rows" inventory is
now stale too.

**2. The sequencing warning above has already materialized.**
`build/scripts/ci/script-test-quarantine.json` now reads **`"quarantined_modules": {}`** — #3180
merged and drained all three entries. So `test_desktop_screen_blueprint_checklist`,
`test_live_execution_controls_route_consistency` and `test_export_project_artifact_workflow` are
gating `main` **as of now**, in the lane whose two teardown races are still unfixed. This does not
moot the warning; it closes the window on acting before the fact. The P0 above is unchanged and
slightly more urgent.

**3. The generated-conflict finding is reinforced, and this document's own PR proved it.**
Re-measured against `e447587e`: of six remaining active PRs, four conflict, for **15 conflicts that
are still 100% generated files** — now across **five** paths rather than three, adding
`docs/status/coverage-report.md` and `docs/status/example-validation.md`. And
[#3187](https://github.com/rodoHasArrived/Meridian-main/pull/3187) — this document — went `dirty` on
exactly `doc-health-dashboard.{json,md}` plus `example-validation.md` overnight, making it the
seventh PR to pay the same tax. Resolving it needed no decisions: the repo's own resolver handled the
dashboard pair, and regeneration produced the rest.

**New finding, found by using the resolver rather than reading it.**
`build/scripts/resolve-generated-merge-conflicts.py` covers four of the five colliding paths —
`doc-health-dashboard.{json,md}` and `coverage-report.md` in `GENERATED_FILES` (`:20-39`), and
`source-hash-manifest.json` through its dedicated hash merger (`:42`). **`docs/status/example-validation.md`
is in none of them**, so the resolver reports `REVIEW` and stops. It is a generated file:
`run-docs-automation.py --profile core` rewrites it from `validate-examples`. Adding that one path to
`GENERATED_FILES` makes the resolver complete over the currently-colliding set. The file's own comment
warns against replacing the list with a wildcard over `docs/status`, so this is a one-line addition,
not a glob.

## Process finding: one determination closed, one still open, and this is the seventh

[#3132](https://github.com/rodoHasArrived/Meridian-main/pull/3132) (2026-10-10) was **closed
unmerged today at 09:49 UTC**, which is the right outcome and the first movement on that backlog.
#3170 (2026-10-15) remains **open and draft**, and it is the only document carrying the current
ranking.

Four determinations in a row observed that rankings were accumulating faster than they were being
merged or acted on. This document does not solve that by re-ranking; it is deliberately scoped to
three verifiable system findings and defers to #3170 for priorities. **The most useful next action in
this directory is merging #3170, not writing an eighth ranking.**

There is now a mechanical reason to merge it, not only a process one. `repair-links.py --summary`
reports **2 broken links across 2,571 checked** today:

- `docs/engineering/production-certification-evidence-chain.md:280` points at
  `known-vulnerabilities.md#pending-decision-braces-stack-exhaustion-2026-10-05`, an anchor that no
  longer exists — the section was retitled *"Historical October 5 investigation and unaccepted
  proposal"* when the `braces` blocker was fixed rather than accepted. **#3170 already carries the
  one-line fix** that retargets it. Leaving #3170 unmerged leaves a dangling link in the
  certification evidence chain whose repair is written and reviewed.
- `docs/testing/evidence/wal-checksum-2026-10-07/.../doc_benchmarks.md:3` -> `../readme.md` is the
  retained third-party evidence #3170 already reported; the fix is to scope the checker, not edit
  retained evidence.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0** | **Everything in [#3170](https://github.com/rodoHasArrived/Meridian-main/pull/3170)'s summary, unchanged** — sign `ADR-019`/`ADR-020`, provision the signing secret, decide the ~1 GB download, continue `PRD-002` from #2931's residual list | Re-verified today: both ADRs still read `Proposed`, and the repo still has one tag and no `v*`. None of it is engineering analysis |
| **P0** | **Fix the two `verify-workflows` races, then re-run the lane on the head** — `ignore_cleanup_errors=True` across 94 `tests/scripts` `TemporaryDirectory` sites; the cancellation/allocation window at `tools/schema_control/disposable.py:432-443` | The authoritative merge gate is red on `e5c20ca7` and failed 4 of the last 12 main pushes, none of it product code. #3170's "freeze a full-coverage green commit and tag the RC" has no target until this is true |
| **P1** | **Return 0 for a `manual` disposition in `generated_repair.py:544-546`**, with a regression test | 31 of 31 `workflow_run` invocations failed; **zero green runs ever**. The workflow's own step conditions prove `manual` was meant to finish green |
| **P1** | **Then run the existing resolver over the six conflicted PRs** (`build/scripts/resolve-generated-merge-conflicts.py`) | 15 of 15 conflicts in the active queue are generated files already enumerated in that script. No PR in the active queue is blocked by a real disagreement |
| **P1** | **Promote #3170's `.gitattributes` / regenerate-on-merge item** | Same root cause as the two rows above, now measured on a different file set (`doc-health-dashboard`, not the workstation bundle). Recurring by construction |
| **P1** | **Add `docs/status/example-validation.md` to `GENERATED_FILES`** in `resolve-generated-merge-conflicts.py:20-39` | One line makes the resolver complete over the five currently-colliding generated paths. Found by running it on this document's own merge, where it reported `REVIEW` and stopped |
| ~~P2~~ **done** | ~~Sequence #3180 after the race fixes~~ | **#3180 merged via #3186 on 2026-10-17.** The quarantine is now empty and all three modules gate `main` with the two races unfixed, so the warning is spent and the P0 above is more urgent |
| **P2** | **Merge or close #3170** | It is the only document carrying the current ranking, and it is a draft |
| **P2** | **Take or decline `W9-CORPACT-011`** (now 24 days at `ready_for_acceptance`) | Carried from #3170. Zero engineering; declining is a result |

## The bar for the next determination

**The next determination is due when `Meridian CI / quality-gate` has been green on three
consecutive `main` pushes, or when the RC is tagged.**

The predecessor's bar — a tag or a refused signature — is still the right one for the program, and
it is unmet for the reason it predicted: the blockers are decisions, not analysis. This document adds
only that **the RC now also needs a gate that means something**, and that the three findings above
are each small, bounded, and owned by nobody today. If an eighth determination is written while
`ADR-019` still reads `Proposed` and the script-test lane is still flaky, the thing to examine is the
decision path, not the backlog.
