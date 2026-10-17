# What To Work On Next — Meridian (2026-10-17)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-17
**Baseline:** `origin/main` at `0342643deec73314b71bd792ceaca3f5c66f6cc5` (2026-10-17 14:24 UTC).
`main` advanced to `30b98549` at 14:50 UTC, 26 minutes later — see
[Update (2026-10-17, 14:55 UTC)](#update-2026-10-17-1455-utc-the-window-closed-on-0342643d-in-26-minutes)
**Supersedes:** the [2026-10-16 determination](next-work-determination-2026-10-16.md) as the latest
prioritization pointer. Its bar has been **met**, which is why this document re-ranks rather than
deferring. The [2026-10-15 determination (#3170)](https://github.com/rodoHasArrived/Meridian-main/pull/3170)
is still open and draft; its product ranking is re-verified and carried forward here.
**Method:** read every `Meridian CI` `main` push run by conclusion and pulled the **per-job**
conclusions of the head run rather than trusting the workflow conclusion; read all 20 most recent
`Generated Repair Publisher` runs and pulled the **failed job log** of the newest to confirm the
exit path verbatim; re-measured every one of the 25 open pull requests with
`git merge-tree --write-tree --name-only origin/main <head>`, counting only `CONFLICT (...)` lines
and classifying each path as generated or hand-written; measured generated-file churn by
intersecting the last 200 commits with each file's own history; parsed the roadmap registry with
`yaml.safe_load`; read the ADR status lines, the remote tag list, `.gitattributes`, and the
resolver source in current source.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; `docs/product/implementation-todo-list.md`
> remains authoritative for the P0 release gate. Recheck conflict counts and CI results against the
> live head before acting.

## The bar was met, and that changes the headline

The 2026-10-16 determination set its bar as: *"due when `Meridian CI / quality-gate` has been green
on three consecutive `main` pushes, or when the RC is tagged."* **The gate clause is met.**

Every `Meridian CI` `main` push since the last red, in order:

| Push | Head | `Meridian CI` | Note |
| --- | --- | --- | --- |
| 2026-10-16 09:13 | `e5c20ca7` | **failure** | the red head that yesterday's P0 was written about |
| 2026-10-16 21:48 | `162df384` | success | |
| 2026-10-17 08:06 | `e447587e` | success | |
| 2026-10-17 08:45 | `34a02682` | *cancelled* | superseded by the next push's run via concurrency, **not red** |
| 2026-10-17 09:22 | `1f33f090` | success | |
| 2026-10-17 14:24 | `0342643d` | **success** | **current head** |

Four green, one cancelled by supersede, **zero red** in the five pushes since `e5c20ca7`. On the
current head all **seven** jobs are green individually — `verify-dotnet`, `verify-browser`,
`verify-docs`, `verify-workflows`, the PostgreSQL `service-backed-integrations` leg,
`integration-gate`, and `quality-gate`
([run 39040596751](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39040596751)).
`verify-workflows`, the lane that produced all three recent failures, is green here.

**And one of the two named races was actually fixed, with reproduction evidence.** Commit
`4213b943` ("test(schema-control): make the fake Docker safe for concurrent runs") root-caused the
exact failure yesterday's document flagged on `e5c20ca7`: `find_resource` parsed every
`resource-*.json` with an unguarded read, so a sibling's `rm` could delete a file between the glob
and the read. The fix skips files that vanish mid-scan and publishes resource files atomically. The
commit records the reproduction: injecting a 30 ms delay before the read, **the unfixed code failed
3/10 and the fixed code passed 40/40.** This is the standard the repository should hold findings
to, and it closes yesterday's P0 by half.

## P0: the RC window is open for the first time, and it closes on the next merge

#3170's fourth P0 is *"freeze a full-coverage green commit and tag the RC."* Yesterday that
instruction had no target. **Today it has one: `0342643d`** — all seven lanes green, plus
`Production Certification` and the always-reported `integration-gate`.

This is the only item on this list that is **time-boxed by someone else's actions**. The window is
not a property of the calendar; it is a property of this commit still being the head. Twelve
merges landed in the last two days. The next one may or may not be green, and yesterday's evidence
says roughly one in four has not been.

**What still blocks the tag is not engineering.** Re-verified in current source today:

- `docs/adr/019-production-support-matrix-and-deployment-posture.md:3` still reads
  **`Proposed (awaiting core-team sign-off; signing this ADR is the PRD-000 "signed support matrix"
  artifact)`**.
- `docs/adr/020-lifecycle-control-plane.md:3` still reads **`Proposed`**.
- `git ls-remote --tags origin` returns **exactly one tag**, `eval-v0.1.0-eval.1`. No `v*` exists.

So the P0 path is unchanged in content and newly urgent in timing: **four signatures, one signing
secret, one product decision (the ~1 GB consumer download), and `PRD-002` continued from #2931's
residual list.** None of it is analysis. What is new is that the engineering precondition they were
waiting on is, for this commit, satisfied.

## P1: the remaining teardown race is the thing that will take the gate back

The second race yesterday named is **untouched, and the surface grew**. Measured today:

- `tests/scripts` has **98** files calling `tempfile.TemporaryDirectory(` — up from 94.
- **Zero** of them use `ignore_cleanup_errors`.

The exact failing site is unchanged and worth naming precisely, because the fix is one keyword.
`tests/scripts/test_merge_reviewed_doc_hashes.py:93`:

```python
def setUp(self):
    self.temp = tempfile.TemporaryDirectory()
    self.addCleanup(self.temp.cleanup)
    self.repo = Path(self.temp.name)
    self.git("init", "--initial-branch=main")
```

The test `git init`s a live repository inside the temporary directory and then lets
`TemporaryDirectory.cleanup` tear it down. On `bd1f9210` that cleanup raised
`OSError: [Errno 39] Directory not empty: '/tmp/tmpld7ddgxd/.git'` — **raised by `tempfile`, not by
an assertion.** Nothing about the code under test failed; the harness could not delete a directory
git was still touching.

`ignore_cleanup_errors=True` on the sites that host a live `.git` removes this class of failure
outright. **This is P1 rather than P0 because the gate is currently green** — but it is the single
most likely cause of the next red, and a red gate is what invalidates an RC commit. Fix it before,
not after, the tag.

**Do not quarantine the module.** `build/scripts/ci/script-test-quarantine.json` now reads
`"quarantined_modules": {}` — #3180 drained it — and the repository's own rules forbid skipping
tests to reach green. A release cannot be certified on a gate that was silenced to pass.

## P1: the `Generated Repair Publisher` has now failed 91 times out of 91

Yesterday this was 31 of 31. **Today it is worse by 53 failures in a single day**, and it is still
the same one line. Of the 20 most recent runs, 19 are `workflow_run` failures and one `push` run
was skipped. **There is still no green run anywhere in its history.**

It failed **four more times on the current head alone** — runs
[39041027436](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39041027436),
[39041359049](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39041359049),
[39041505584](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39041505584) and
[39042015558](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39042015558), at 14:30,
14:34, 14:36 and 14:44 UTC.

The failed job log of the newest reads, verbatim:

```text
Generated repair: manual. Target run is not bound to its base SHA
##[error]Process completed with exit code 1.
```

That is `RepairError` (`build/scripts/ci/generated_repair.py:213`) taking the handler at
`:544-546`, which writes a `manual` status and then `return 1`:

```text
except (RepairError, OSError, ValueError, KeyError, zipfile.BadZipFile) as exc:
    write_outputs({"status": "manual", "reason": str(exc)}, args.result)
    return 1
```

The workflow's own conditions prove `manual` was meant to finish green: `inspect` returns
`{"status": "manual", ...}` as a normal outcome in four places (`:302, 333, 343, 346`), and
`.github/workflows/generated-repair-publish.yml:62,86` gate every later step on
`status == 'ready' || status == 'retry'`. A `manual` disposition is **designed** to skip the rest
and succeed.

Return 0 for a `manual` disposition, reserving non-zero for genuine faults, and add the regression
test to the existing `tests/scripts/test_generated_repair.py`.

**Read the workflow file, not the check name.** Two sibling workflows share the prefix and only one
is broken: `generated-repair.yml` (`pull_request_target`, surfaces `Prepare generated repair patch`)
**works**; `generated-repair-publish.yml` (`workflow_run`) is the one with no green run. A green
`Prepare generated repair patch` check does not contradict this finding.

## Correction: the queue's conflicts are **no longer** 100% generated files

Yesterday's document reported 15 conflicts across the active queue, all in generated files, and
concluded "no active PR is blocked by a real disagreement." **Re-measured today across all 25 open
PRs against `0342643d`, that is no longer true.** 15 of 25 PRs conflict, for **163 conflicts**, and
some are now genuine:

| PR | Conflicts | Changed | Hand-written conflicts |
| --- | --- | --- | --- |
| #3195, #3194, #3193, #3192, #3190, #3188, #3142, #3141, #3140, #3138 | **0** | — | — |
| #3016, #3111, #3116, #2928 | 2 each | 9 / 6 / 19 / 26 | none — dashboard pair only |
| #3094, #2896, #3189, #3168, #3119 | 3 each | 8 / 83 / 63 / 131 / 81 | none — generated only |
| #3004 | 4 | 134 | none — generated only |
| #3170 | 4 | 7 | **`docs/product/plans/README.md`** |
| #3169 | 5 | 134 | none — generated only |
| #3176 | 3 | 60 | **`tests/scripts/test_schema_control_disposable.py`** |
| #3175 | 6 | 178 | `database/manifest/{contracts,dependencies}.json` (generator manifests) |
| #3105 | **118** | 138 | **many** — see below |

Three of these need a person, not a resolver:

1. **#3176 conflicts on `tests/scripts/test_schema_control_disposable.py`** — the exact file `main`
   just fixed in `4213b943`. Two parties edited the same race. This is the first genuine code
   disagreement in the queue in days, and it is in safety-relevant test code, so it should be
   resolved deliberately: `main`'s fix carries a 40/40 reproduction, so the burden is on #3176's
   version to show it preserves that.
2. **#3105 is stale and should be closed or rebased, not merged.** Last updated 2026-10-07, ten
   days ago, with **118 conflicts** — 44 `modify/delete` and 40 `rename/delete` against the
   workstation bundle `main` has since deleted, 14 `rename/rename`, plus real hand-written
   collisions in `src/Meridian.Ui.Shared/Services/LedgerReportingAuthoritativeSource.cs`,
   `src/Meridian.Ui/dashboard/package.json`, `package-lock.json`, `postcss.config.cjs`, two test
   files, and four module READMEs. This is the `rename/rename` bundle churn #3170 measured, now
   isolated in one abandoned branch. Reconstructing it on a current base will cost less than
   resolving it.
3. **#3170 conflicts on `docs/product/plans/README.md`** — the determination index, because both it
   and the merged 2026-10-16 document repoint the same pointer. **This document touches that file
   too**, so whoever merges second resolves it; the resolution is mechanical (keep both rows, point
   "latest" at the newest date) but it is hand-written and no resolver covers it.

**`docs/status/example-validation.md` is still absent from the resolver.** Confirmed today:
`build/scripts/resolve-generated-merge-conflicts.py` has no occurrence of `example-validation`, so
`GENERATED_FILES` (`:20-39`) still omits it and the resolver reports `REVIEW` and stops on the
three PRs where it collides (#3175, #3169, #3168, #3004). The one-line addition from yesterday is
unapplied.

## The measured root cause of the conflict tax, and why a merge driver is the fix

Yesterday's document inferred this from file names. It is now measured. **Share of the last 200
commits that touch each generated path:**

| Path | Commits / 200 | Share | In conflicted PRs |
| --- | --- | --- | --- |
| `docs/status/doc-health-dashboard.md` | **86** | **43%** | **14 of 15** |
| `docs/status/doc-health-dashboard.json` | **86** | **43%** | **14 of 15** |
| `docs/source/generated/source-hash-manifest.json` | 76 | 38% | 6 |
| `docs/generated/repository-structure.md` | 73 | 37% | 2 |
| `docs/status/coverage-report.md` | 28 | 14% | 1 |
| `docs/status/example-validation.md` | 9 | 5% | 4 |

**A file that changes in 43% of commits conflicts with essentially any branch older than a day.**
That is the whole mechanism: the dashboard pair appears in 14 of the 15 conflicted PRs not because
anyone disagrees about it, but because it is rewritten twice per three commits. The most recent
commit to touch it is `f1772b3a`, titled *"docs: regenerate doc-health dashboard after merging
main"* — the repository is already paying this tax by hand, one merge at a time, and naming it in
commit subjects.

**The structural fix does not exist yet.** `.gitattributes` is 488 bytes and contains **no `merge=`
driver at all** — only `text=auto` and `eol=lf` entries. #3170's `.gitattributes` /
regenerate-on-merge item is wholly unimplemented, and these numbers are the argument for it:
declare the whole-file generator outputs `merge=ours` (or a regenerate driver) and the 43% file
stops producing conflicts for every branch in the queue simultaneously.

Worth noting the generator was already made deterministic in one dimension — the dashboard's own
header reads `_Generated: 1970-01-01T00:00:00+00:00_`, a normalized epoch stamp — so the remaining
churn is real content, and a timestamp fix will not help. The choice is a merge driver, or not
tracking the file.

## Doc-alignment finding: the readiness tracker's roadmap inventory is stale

The registry, parsed today, holds **48 rows: 17 `done`, 15 `planned`, 9 `in_progress`,
6 `accepted`, 1 `ready_for_acceptance`** — unchanged since #3185 registered the six `W11` rows.

`docs/product/implementation-todo-list.md:97` still states: *"The registry contains **42 rows** as
of this review: **17 `done`, 9 `in_progress`, 6 `accepted`, 1 `ready_for_acceptance`, and 9
`planned`**."* That is the pre-#3185 inventory. The `done`/`in_progress`/`accepted`/
`ready_for_acceptance` counts are all still correct; only `planned` (9 → 15) and the total
(42 → 48) are stale. Yesterday's document predicted this; it is still unrepaired, in the document
that is authoritative for the P0 release gate.

## What has not changed

- **The user-requested work queue is unchanged:** `W10-LOT-002`, then `W10-MARK-001`, then
  `W10-SEAM-001`, before `W10-RECON-001` in that queue only. Registry ranking remains MARK 1,
  RECON 2, PROV 3, JRNL 5, SEAM 7, LOT 12.
- **`W9-CORPACT-011` is still the program's only `ready_for_acceptance` row** — now **25 days** in
  that posture, since 2026-09-22. Taking or declining it remains zero engineering, and declining is
  still a result.
- Every product item in #3170's summary table stands as written.

## Update (2026-10-17, 14:55 UTC): the window closed on `0342643d` in 26 minutes

`main` advanced from `0342643d` to **`30b98549`** (#3192, *"commitment and fund-unit lots; paper
lots join the shared lot shape"*) at 14:50 UTC — **26 minutes after the baseline above**, and
before this document was opened for review. This is not a correction to the analysis; it is the
analysis happening on schedule, and it is worth recording precisely because it was the predicted
failure mode rather than a surprise.

Two consequences, re-verified against the new head:

**1. The RC target moved, and the tag was not taken.** `0342643d` was a commit with seven of seven
lanes green, and it is now two commits behind the head with no `v*` tag pointing at it. `Meridian CI`
on `30b98549` ([run 39042429968](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39042429968))
was still **queued** when this update was written, so whether the new head is also certifiable is
not yet known. **The P0 above is unchanged in substance and its timing claim is now demonstrated:**
the window is a property of the head, the head moved inside half an hour, and the signatures that
would have let someone tag `0342643d` are still absent. Re-verify the current head's lanes before
picking an RC commit; do not inherit `0342643d`'s green from this document.

**2. This document's own PR became the next data point in its own finding.**
[#3197](https://github.com/rodoHasArrived/Meridian-main/pull/3197) went un-mergeable within the
hour, conflicting on exactly **`docs/status/doc-health-dashboard.{json,md}`** — the pair measured
above at 43% of the last 200 commits. The resolution required no decisions:
`build/scripts/resolve-generated-merge-conflicts.py` reported
`2 generated conflict(s); 0 conflict(s) require review` and resolved both.

The two commits `#3192` brought with it make the point without further comment:
`b3a7f78b chore: merge main and regenerate generated artifacts` and
`507a9d0c docs: regenerate doc-health dashboard after merging main`. **Three branches paid the same
tax on the same file pair inside one hour**, each by hand or by running the resolver, because no
`merge=` driver exists. The P1 row for `.gitattributes` is the one item on this list whose cost is
being paid continuously and visibly in commit subjects.

## Process finding: the acting is now outpacing the ranking

This is the eighth determination, and for the first time the previous one's findings were **acted
on rather than re-stated**: the `test_schema_control_disposable` race was root-caused and fixed with
a 40/40 reproduction, the quarantine was drained, and the gate went green. Four determinations in a
row had observed that rankings accumulated faster than they were acted on. That trend broke this
week.

What did not break is the merge backlog in this directory: **#3170 is still open and draft** after
two days, and now carries a conflict on the index. Its one-line repair of the dangling
`known-vulnerabilities.md` anchor in
`docs/engineering/production-certification-evidence-chain.md:280` is still written, still reviewed,
and still unmerged.

**Why this document recommends patches rather than applying them.** Three of the items below are
one-line changes, and it is tempting to just land them. Two of the three —
`generated_repair.py` and `.gitattributes` / the resolver — are **CI governance changes**, and
`CLAUDE.md` states that all CI governance changes require human review. They are therefore written
out precisely enough to apply in minutes, and left to a person.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0** | **Tag the RC on `0342643d` once the signatures exist** — sign `ADR-019`/`ADR-020`, provision the signing secret, decide the ~1 GB consumer download, continue `PRD-002` from #2931's residual list | For the first time the "freeze a full-coverage green commit" instruction has a target: seven of seven lanes green, zero red in the five pushes since `e5c20ca7`. The window closes on the next merge, and about one merge in four has not been green. Both ADRs still read `Proposed`; the repo has one tag and no `v*`. None of this is engineering |
| **P1** | **Add `ignore_cleanup_errors=True`** to the `tests/scripts` `TemporaryDirectory` sites that host a live `.git`, starting at `test_merge_reviewed_doc_hashes.py:93` | 98 call sites, zero guarded. This is harness teardown, not a defect — `OSError: [Errno 39]` was raised by `tempfile` itself. It is the most likely cause of the next red, and a red gate invalidates the RC commit. Do not quarantine: the quarantine file is now empty and skipping tests to reach green is forbidden |
| **P1** | **Return 0 for a `manual` disposition** at `generated_repair.py:544-546`, with a regression test | **91 of 91 runs failed; still zero green ever**, four of them on the current head. Confirmed verbatim in today's job log. The workflow's own step conditions (`:62,86`) prove `manual` was meant to finish green |
| **P1** | **Add a `merge=` driver to `.gitattributes`** for the whole-file generator outputs, or stop tracking them | Measured: the doc-health pair changes in **86 of the last 200 commits (43%)** and appears in **14 of the 15** conflicted PRs. `.gitattributes` has no `merge=` entry at all. One change ends the tax for every branch in the queue at once |
| **P1** | **Add `docs/status/example-validation.md` to `GENERATED_FILES`** in `resolve-generated-merge-conflicts.py:20-39` | Still absent today. The resolver reports `REVIEW` and stops on the four PRs where it collides. One line, and the file's own comment rules out a wildcard |
| **P2** | **Resolve #3176's conflict in `test_schema_control_disposable.py` deliberately** | The first genuine code disagreement in the queue in days, and it is on the race `main` just fixed. `main`'s version carries a 40/40 reproduction; #3176's should have to match it |
| **P2** | **Close or rebase #3105** | Stale ten days with 118 conflicts, 98 of them `modify/delete` and `rename/delete` against a workstation bundle `main` has deleted, plus real collisions in shared services, `package.json` and tests. Reconstruction costs less than resolution |
| **P2** | **Merge or close #3170** | Still open, still draft, and now conflicting on the determination index. It is the only document carrying the current product ranking, and its dangling-anchor fix is written and reviewed |
| **P2** | **Correct the 42-row inventory** at `implementation-todo-list.md:97` to 48 rows / 15 `planned` | Stale since #3185, in the document that is authoritative for the P0 release gate. Only the `planned` and total counts are wrong |
| **P2** | **Take or decline `W9-CORPACT-011`** (now 25 days at `ready_for_acceptance`) | Carried from #3170. Zero engineering; declining is a result |

## The bar for the next determination

**The next determination is due when the RC is tagged, when a named signature is refused, or when
`Meridian CI / quality-gate` goes red on `main` again.**

The gate clause from the previous bar has been met and should not be re-used: it is now a
maintenance property, not a milestone. What remains is the decision path, and it has been unchanged
across eight determinations while the engineering around it was repeatedly brought to ready. The
ranking above is therefore mostly not a ranking of engineering work — the top item is four
signatures and a product decision, and the three P1 rows exist to protect the commit those
signatures would be applied to.

If a ninth determination is written while `ADR-019` still reads `Proposed` and `0342643d` has been
merged past without a tag, the finding to record is not a new backlog item. It is that the program
produced a certifiable commit and let it expire.
