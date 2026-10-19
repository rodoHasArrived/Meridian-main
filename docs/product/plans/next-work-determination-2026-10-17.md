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
2026-10-17 head all **seven** jobs are green individually — `verify-dotnet`, `verify-browser`,
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

### Correction (2026-10-19): the bar was not literally met on this evidence — but it is met now

On the 2026-10-17 data above, **"green on three consecutive `main` pushes" was not satisfied.** The
sequence was two green (`162df384`, `e447587e`), then the cancelled `34a02682`, then two green
(`1f33f090`, `0342643d`). A superseded cancellation is not red, but it is also **not a green
result**, so the longest consecutive-green run was two, not three. Treating the cancellation as
simply "not red" silently relaxed the criterion, and that is exactly the reasoning this document
criticises elsewhere — more so now that a supersede has been observed surfacing as an outright
`failure` (see the method note below). The claim was premature and is withdrawn as stated.

**The bar is now met, on stronger evidence than the original claim rested on.** Re-measured on
2026-10-19, `Meridian CI` push runs on `main` show **eight consecutive successes with no
cancellation or failure between them**:

| # | Head | Pushed | `Meridian CI` |
| --- | --- | --- | --- |
| 1 | `a4b156a2` | 2026-10-18 09:13 | success |
| 2 | `75950a39` | 2026-10-18 17:38 | success |
| 3 | `213a4db3` | 2026-10-19 01:37 | success |
| 4 | `97ec959d` | 2026-10-19 01:58 | success |
| 5 | `7c902128` | 2026-10-19 02:24 | success |
| 6 | `ed9ba7a3` | 2026-10-19 03:23 | success |
| 7 | `a2a30e56` | 2026-10-19 03:53 | success |
| 8 | `1f171816` | 2026-10-19 04:24 | success |

Eight consecutive, against a bar of three. So the conclusion the document draws — that the gate is
reliable enough for an RC to be selected against it — **holds, and holds more strongly than on
2026-10-17**; only the 10-17 arithmetic was wrong. (The most recent push, `b0abdef9`, is itself
`cancelled` by supersede and so is neither green nor red.)

So the P0 path is unchanged in content and newly urgent in timing. **Corrected 2026-10-19 — an
earlier revision of this list was incomplete.** The readiness tracker, which is the authority here,
states at `docs/product/implementation-todo-list.md:177-180` that what "genuinely still requires a
person" is: the `MDC_SIGNING_CERT_PFX_BASE64` and `MDC_SIGNING_CERT_PASSWORD` secrets in the
protected `desktop-release-signing` environment (`PRD-014`), core-team sign-off on
`ADR-019`/`ADR-020` (`PRD-000`), **operator completion and acceptance of recovery evidence
(`PRD-015`, shared with `PRD-111`)**, and **required-check activation by a repository administrator
(`PRD-016`)**. The last two were omitted here and are not optional: an operator who completed only
the signatures, the secret, the download decision and `PRD-002` could reach a tag with two
production gates still open. Add both, with their release-commit evidence, to the checklist.

**And a second correction (2026-10-19): `PRD-002` is engineering, so "none of this is engineering"
was false and is withdrawn.** `implementation-todo-list.md:241` classifies it
**`Implementation-in-progress`**, with browser and remaining WPF/setup/default-runtime ownership,
non-Windows key protection via an OS keyring or KMS, concurrent configuration writes,
credential/audit commit ambiguity, and legacy migration/rollback/rotation proof all still open.
**A green CI head therefore does not mean the engineering precondition for an RC is satisfied**, and
`PRD-002` stays explicitly blocking on RC selection rather than being presented as human evidence
only. What *is* true, and is the point this document was reaching for, is that the ADR signatures,
the signing secret, the required-check activation and the download decision are not engineering —
`PRD-002` is the exception, and it is the one that can still move on its own. What is new is that the engineering precondition they were
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

**Corrected recommendation (2026-10-19).** An earlier revision said to set
`ignore_cleanup_errors=True` across these sites. **That is the wrong instrument, and it contradicts
this document's own next paragraph.** Suppressing cleanup errors makes a test pass even when
cleanup genuinely failed — when a writer, child process or `git` subprocess is still live under the
temporary tree — and can silently leave directories behind. That is a bypass of the same kind as
quarantine, just quieter, and it destroys the signal that would identify a real leak.

**What to do instead:** make teardown deterministic rather than silent. Terminate or join whatever
still holds the tree before cleanup (the `git` invocations here are synchronous
`subprocess.check_output` calls, so the residue is filesystem state, not a live writer), or wrap
cleanup in a bounded retry that **still fails** if the directory cannot be removed. Either keeps a
genuine leak loud while removing the race. `ignore_cleanup_errors` is acceptable only where the
residue is provably irrelevant to the assertion, and that case should be argued per site, not
applied across 98 of them.

**This is P1 rather than P0 because the gate is currently green** — but it is the single most likely
cause of the next red, and a red gate is what invalidates an RC commit. Fix it before, not after,
the tag.

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

**Corrected recommendation (2026-10-19).** An earlier revision of this document said to "return 0
for a `manual` disposition." **That was wrong and would have been unsafe.** The handler catches
`RepairError` broadly, and `RepairError` is the repository's carrier for genuine trust-boundary
violations, not just benign dispositions — among them `"Handwritten content changed"` (`:144`),
`"Path is outside the trusted generation policy"` (`:97`), `"Unsafe candidate path"` (`:135`),
`"Cannot add or delete a hybrid/protected file"` (`:146`),
`"Source run identity/event/path/attempt is not authorized"` (`:210`), and
`"Expected a signed artifact download redirect"` (`:262`). The same `except` also catches `OSError`,
`ValueError`, `KeyError` and `zipfile.BadZipFile`. A blanket `return 0` would report success when a
repair tried to rewrite handwritten content, escape the policy root, or run unauthorized — exactly
the conditions the exit code exists to surface.

**The fix must be narrow:** make only the specifically expected *unbound-run* condition
(`RepairError("Target run is not bound to its base SHA")`, `:213`) exit zero as a normal
disposition — ideally by raising a distinct exception type for it rather than matching on the
message — and keep a non-zero exit for every trust-boundary and operational failure. The regression
test in `tests/scripts/test_generated_repair.py` should assert **both** halves: the expected
condition exits 0, and an authorization or handwritten-content violation still exits non-zero.

**Second correction (2026-10-19): the narrowed exit-code fix is still not sufficient, and on its
own it buys nothing.** `inspect()` calls `verify_source_run()` as its first action (`:322-324`), and
the unbound-run error is raised *inside* that call (`:213`) — before `inspect` can validate an
artifact or return `ready`/`retry`. So the status written is `manual` either way, and the workflow's
own conditions (`.github/workflows/generated-repair-publish.yml:62,86`) then skip token creation and
publishing. Making that exception exit zero converts **a red no-op into a green no-op**: the check
stops complaining and the hosted repair flow remains just as unusable.

That reframes the finding, and makes it larger rather than smaller. **91 of 91 runs failing at
`:213` means this workflow has never once reached its publish path** — every invocation is rejected
at the binding check, so the automation the repository built for generated-file repair has never
actually repaired anything. The real work is to diagnose why
`run.head_sha != request["base_sha"]` holds on every invocation and correct or requeue the binding;
normalising the exit code is at most a cosmetic follow-up, and doing it *first* would destroy the
only signal that the flow is broken.

The 91-of-91 count and the reading that `manual` is a by-design disposition both stand. What
changed, twice now, is the prescribed fix — which is itself worth noting: the first revision
proposed a blanket `return 0`, the second a narrowed one, and only tracing the call order showed
that neither makes the publisher work.

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
| #3175 | 6 | 178 | **`database/manifest/{contracts,dependencies}.json`** — generator manifests, but **not in the resolver's allowlist** |
| #3105 | **118** | 138 | **many** — see below |

**Four** of these need a person, not a resolver (corrected 2026-10-19 — an earlier revision said three):

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
4. **#3175's two `database/manifest/{contracts,dependencies}.json` conflicts are not automatable
   either** (added 2026-10-19). Although both are generator output, **neither path appears in
   `resolve-generated-merge-conflicts.py`** — confirmed by a zero-match search for
   `database/manifest` in that file — and these are canonical schema-control artifacts whose
   refresh goes through a reviewed hosted-snapshot promotion, not a local regenerate. So the
   resolver cannot clear #3175, and the earlier count of three manual cases understated the queue.

**`docs/status/example-validation.md` is still absent from the resolver.** Confirmed today:
`build/scripts/resolve-generated-merge-conflicts.py` has no occurrence of `example-validation`, so
`GENERATED_FILES` (`:20-39`) still omits it and the resolver reports `REVIEW` and stops on the
three PRs where it collides (#3175, #3169, #3168, #3004). The one-line addition from yesterday is
unapplied.

## The measured root cause of the conflict tax, and why the fix is the hosted repair flow

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

**Corrected recommendation (2026-10-19).** An earlier revision of this document proposed declaring
the generator outputs `merge=ours` in `.gitattributes` and claimed that would "end the tax for
every branch in the queue at once." **The repository has already considered and rejected that, with
reasons this document should have read first.**
`docs/engineering/generated-merge-recovery.md` has a section titled *"Merge queues and custom merge
drivers"* stating that a custom `.gitattributes` merge driver **"depends on local Git configuration
and does not configure GitHub's merge environment,"** and **"cannot prove that a parent's assets
match the merged source."** It also records that merge-group freshness checks run against synthetic
trees that cannot be patched in place, so a stale combination must keep failing and be rebuilt.

A driver would therefore help only contributors who configured it locally, would do nothing for
GitHub-hosted merges or merge groups, and would weaken the trust boundary rather than close it.
**`.gitattributes` is still factually driver-free** (488 bytes, only `text=auto` and `eol=lf`), but
that absence is a decision, not an oversight.

**What the measurements above actually argue for**, then, is not a driver but **reducing how often
the 43% file has to be reconciled at all** — and the repo's own hosted repair/regeneration flow is
the sanctioned path. Concretely, in descending confidence: make the `Generated Repair Publisher`
work (the P1 above), which is the automation built for exactly this; add
`docs/status/example-validation.md` to `GENERATED_FILES` so the local resolver is complete over the
colliding set; and then put the open question to the owners — **whether the doc-health dashboard
needs to be a tracked artifact at all**, given it is rewritten in 43% of commits, declares itself
auto-generated, and is reproducible from `generate-health-dashboard` on demand. That last one is a
governance decision, not a patch, and this document does not presume it.

Worth noting the generator was already made deterministic in one dimension — the dashboard's own
header reads `_Generated: 1970-01-01T00:00:00+00:00_`, a normalized epoch stamp — so the remaining
churn is real content, and a timestamp fix will not help.

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

**Method note (2026-10-19): a superseded run does not always surface as `cancelled`.** When this
PR was pushed again, the in-flight run on the prior head had already started its PostgreSQL lane;
the supersede cancelled that lane mid-test, and the lane's *"Reject missing, failed, skipped or
empty deterministic test evidence"* guard then **failed closed**, because a cancelled test run
leaves no evidence to accept. `quality-gate` and `integration-gate` aggregated that into
**`failure`** on a commit that was no longer the head. So a `failure` conclusion alone does not
distinguish a real defect from having been pushed over, and the guard that produced it behaved
exactly as designed. Any reading of the red/green history in this document — including the table
above — should check whether a `failure` belongs to a superseded run before counting it.

The two commits `#3192` brought with it make the point without further comment:
`b3a7f78b chore: merge main and regenerate generated artifacts` and
`507a9d0c docs: regenerate doc-health dashboard after merging main`. **Three branches paid the same
tax on the same file pair inside one hour**, each by hand or by running the resolver, because no
`merge=` driver exists. The P1 row for `.gitattributes` is the one item on this list whose cost is
being paid continuously and visibly in commit subjects.

### Third round, 15:32 UTC: the `example-validation.md` gap reproduced on demand

`main` advanced again to **`5bba87d7`** (#3194, *"require the terms-format tag on every Security
Master write"*), **42 minutes after `30b98549`**, and this document's PR conflicted a second time —
now on **three** paths. Running the resolver printed the P1 finding above as output rather than as
a claim:

```text
Incoming main: 5bba87d73ff8c8e8886114d2443c33ef36afa819
GENERATED  docs/status/doc-health-dashboard.json
GENERATED  docs/status/doc-health-dashboard.md
REVIEW     docs/status/example-validation.md
2 generated conflict(s); 1 conflict(s) require review.
```

`docs/status/example-validation.md` is a generated file — `validate-examples.py` rewrites it whole,
and regenerating it here produced a clean report (464 files, 1,107 blocks, **0 invalid**). But
because the path is absent from `GENERATED_FILES`, the resolver classifies it `REVIEW` and stops,
turning a no-decision regeneration into a manual step. **That is the entire cost of the missing
one-line entry, and it now has a reproduction rather than an argument.**

`#3194` also arrived carrying three regeneration commits of its own —
`482b5dc0` and `933c6c91` (both *"chore: merge main and regenerate generated artifacts"*) and
`8c820d42` (*"docs: regenerate doc-health dashboard after merging main"*). **One pull request paid
this tax three times before it merged.** Across four base advances — two inside 68 minutes on 2026-10-17, then again on 2026-10-19 — this document's own branch paid it **four** times, every time on the same file pair, every time resolved by the resolver with nothing requiring review. The two P1 rows — the `merge=` driver and the one-line `GENERATED_FILES`
addition — are not hygiene items; they are the difference between these merges being automatic and
being hand-held.

## Re-verification (2026-10-19): every ranked item is still open, 53 commits later

This document was marked ready for review on 2026-10-19, two days after its baseline, with `main`
advanced **53 commits** to `bb4a0899` (`git rev-list --count 0342643d..bb4a0899`; the 37 figure an earlier revision carried was measured from `5bba87d7`, not from the declared baseline — corrected 2026-10-19). Every claim that drives the ranking was re-checked against
that tree rather than assumed to hold. **Nothing in the ranking changed.**

| Item | Re-verified on `bb4a0899` | State |
| --- | --- | --- |
| RC tagged? | `git ls-remote --tags origin` → still only `eval-v0.1.0-eval.1` | **no `v*`** |
| `ADR-019` | `**Status:** Proposed (awaiting core-team sign-off...)` | unchanged |
| `ADR-020` | `**Status:** Proposed` | unchanged |
| `generated_repair.py:544-546` | still `return 1` on the `manual` disposition | unchanged |
| `GENERATED_FILES` | zero occurrences of `example-validation` | unchanged |
| `ignore_cleanup_errors` | zero files in `tests/scripts` use it | unchanged |
| `.gitattributes` | zero `merge=` entries | unchanged |
| Registry | 48 rows: 17 `done`, 15 `planned`, 9 `in_progress`, 6 `accepted`, 1 `ready_for_acceptance` | unchanged |

Two consequences worth stating plainly:

**The P0's central prediction is now settled, not pending.** This document said the window "closes
on the next merge" and that `0342643d` was the target. `0342643d` was never tagged and is now 53
commits behind the head. The commit that had seven of seven lanes green on 2026-10-17 cannot be the
release candidate any more, and no commit has taken its place. **That is the failure mode the last
section of this document names**, and it has now happened once with a recorded target.

**Every P1 is still a one-line change that nobody owns.** Two days of active development — 37
commits, including the three pull requests that paid the generated-file tax in their own subjects —
moved none of them. That is the argument for treating them as delivery-system work with an owner
rather than as findings in a document.

`W9-CORPACT-011` is now **27 days** at `ready_for_acceptance`, and this is still the only
determination in the directory carrying a current ranking: the 2026-10-15 determination
([#3170](https://github.com/rodoHasArrived/Meridian-main/pull/3170)) remains unmerged, and no newer
determination has landed on `main`, whose index still points at 2026-10-16 as latest.

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
| **P0** | **Re-verify the current `main` head, then tag that commit once the gates are closed** — sign `ADR-019`/`ADR-020` (`PRD-000`), provision the `desktop-release-signing` secrets (`PRD-014`), complete and accept recovery evidence (`PRD-015`), have an administrator activate the required release check (`PRD-016`), decide the ~1 GB consumer download, and **finish `PRD-002`, which is still `Implementation-in-progress` and is a blocking engineering precondition** | **Corrected 2026-10-19: do not tag `0342643d`** — it was the green target on 2026-10-17, is now 53 commits behind, and was never tagged. The gate itself is reliable (eight consecutive green `main` pushes as of 2026-10-19), but a green head does **not** mean the engineering precondition is met while `PRD-002` is open. Gates per `implementation-todo-list.md:177-180` and `:241` |
| **P1** | **Make `tests/scripts` teardown deterministic** — terminate/join whatever holds the temporary tree, or use a bounded cleanup retry that **still fails** if the directory cannot be removed; start at `test_merge_reviewed_doc_hashes.py:93` | 98 call sites, zero guarded. The `OSError: [Errno 39]` was raised by `tempfile`, not an assertion. **Corrected 2026-10-19: not blanket `ignore_cleanup_errors=True`** — that passes tests whose cleanup genuinely failed and hides real leaks, which is the same bypass as quarantine. Likeliest cause of the next red, and a red gate invalidates the RC commit |
| **P1** | **Diagnose why every invocation fails the base-SHA binding check** at `generated_repair.py:213` and correct or requeue the binding; treat normalising the exit code as a cosmetic follow-up, not the fix | **91 of 91 runs failed; zero green ever.** **Corrected twice (2026-10-19):** `inspect()` calls `verify_source_run()` first (`:322-324`), so the error precedes any `ready`/`retry`, the status stays `manual`, and the workflow conditions (`:62,86`) skip publishing — an exit-code change yields a **green no-op**. 91 of 91 failing there means the publisher has **never reached its publish path** |
| **P1** | **Reduce how often the 43% file is reconciled, via the hosted repair flow** — fix the publisher above, complete the local resolver, then put the tracked-artifact question to the owners | Measured: the doc-health pair changes in **86 of the last 200 commits (43%)** and appears in **14 of the 15** conflicted PRs. **Corrected 2026-10-19: not a `merge=` driver.** `generated-merge-recovery.md` already rejects that — it depends on local Git config, does not configure GitHub's merge environment, and cannot prove a parent's assets match the merged source |
| **P1** | **Add `docs/status/example-validation.md` to `GENERATED_FILES`** in `resolve-generated-merge-conflicts.py:20-39` | Still absent today. The resolver reports `REVIEW` and stops on the four PRs where it collides. One line, and the file's own comment rules out a wildcard |
| **P2** | **Resolve #3176's conflict in `test_schema_control_disposable.py` deliberately** | The first genuine code disagreement in the queue in days, and it is on the race `main` just fixed. `main`'s version carries a 40/40 reproduction; #3176's should have to match it |
| **P2** | **Close or rebase #3105** | Stale ten days with 118 conflicts, 98 of them `modify/delete` and `rename/delete` against a workstation bundle `main` has deleted, plus real collisions in shared services, `package.json` and tests. Reconstruction costs less than resolution |
| **P2** | **Close #3170, keeping only its independent dangling-anchor fix** | Corrected 2026-10-19: this document now supersedes it as the pointer and carries its product ranking and queue/registry figures forward, so #3170 is **no longer** the only carrier of that ranking and should not be merged just to preserve it. What is still uniquely its own is the one-line retarget of the dangling `known-vulnerabilities.md` anchor; land that and close the draft rather than resolving its now-stale index conflict |
| **P2** | **Correct the 42-row inventory** at `implementation-todo-list.md:97` to 48 rows / 15 `planned` | Stale since #3185, in the document that is authoritative for the P0 release gate. Only the `planned` and total counts are wrong |
| **P2** | **Take or decline `W9-CORPACT-011`** (25 days at the baseline; **27 as of 2026-10-19**) | Carried from #3170. Zero engineering; declining is a result |

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
