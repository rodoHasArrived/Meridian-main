# What To Work On Next — Meridian (2026-10-18)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-18
**Baseline:** `origin/main` at `a4b156a2170df6ae790a515347b285c136c2cbc6` (2026-10-18 09:13 UTC)
**Supersedes:** the [2026-10-16 determination](next-work-determination-2026-10-16.md) as the latest
merged prioritization pointer. Two determinations are still open and unmerged — the
[2026-10-15 (#3170)](https://github.com/rodoHasArrived/Meridian-main/pull/3170) and the
[2026-10-17 (#3197)](https://github.com/rodoHasArrived/Meridian-main/pull/3197). The product ranking
in both is re-verified here and carried forward; this document does not re-derive it.
**Method:** read every `Meridian CI` and `Production Certification` `main` push run by conclusion,
then pulled **per-job** conclusions on the head rather than trusting workflow conclusions; pulled the
**failing job logs** of both certification failures in the last 40 pushes and extracted the named
failing test from each; read the failing test, its child-process helper, and the store method it
exercises in current source; counted every `Generated Repair Publisher` run by event and conclusion
across both result pages; re-measured all **30** open pull requests with
`git merge-tree --write-tree --name-only origin/main refs/remotes/pr/<n>`, counting only
`CONFLICT (...)` lines and classifying each path as generated or hand-written; checked every open PR
branch for each carried one-line fix to see whether any is in flight; parsed the roadmap registry
with `yaml.safe_load`; read the ADR status lines, the remote tag list, `.gitattributes`, the resolver
source, and the `Release Readiness` workflow and its run history.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; `docs/product/implementation-todo-list.md`
> remains authoritative for the P0 release gate. Recheck conflict counts and CI results against the
> live head before acting.

## The bar is unmet, and the thing its predecessor predicted is what happened

The 2026-10-17 determination set its bar as: *"due when the RC is tagged, when a named signature is
refused, or when `Meridian CI / quality-gate` goes red on `main` again."* **None of the three has
occurred.** Re-verified in current source and on the remote today:

- `docs/adr/019-production-support-matrix-and-deployment-posture.md:3` still reads
  **`Proposed (awaiting core-team sign-off; signing this ADR is the PRD-000 "signed support matrix"
  artifact)`**.
- `docs/adr/020-lifecycle-control-plane.md:3` still reads **`Proposed`**.
- `GET /repos/.../tags` returns **exactly one tag**, `eval-v0.1.0-eval.1`. No `v*` exists.
- `Meridian CI` has **not** gone red on `main` since `e5c20ca7` (2026-10-16 09:13). Nine pushes,
  zero red.

By that bar a ninth ranking is not due, and this document is not one. #3197 also wrote down what to
record instead: *"If a ninth determination is written while `ADR-019` still reads `Proposed` and
`0342643d` has been merged past without a tag, the finding to record is not a new backlog item. It
is that the program produced a certifiable commit and let it expire."*

**It produced eight.** Every `main` push where `Meridian CI` and `Production Certification` were
both green, in order:

| Push (UTC) | Head | `Meridian CI` | `Production Certification` |
| --- | --- | --- | --- |
| 2026-10-16 21:48 | `162df384` | success | success (#251) |
| 2026-10-17 08:06 | `e447587e` | success | success (#252) |
| 2026-10-17 09:22 | `1f33f090` | success | success (#254) |
| 2026-10-17 14:24 | `0342643d` | success | success (#255) ← #3197's named RC target |
| 2026-10-17 14:50 | `30b98549` | success | success (#256) |
| 2026-10-17 15:30 | `5bba87d7` | success | success (#257) |
| 2026-10-18 01:46 | `72eab810` | success | success (#258) |
| 2026-10-18 02:30 | `a4762d48` | success | success (#259) |
| **2026-10-18 09:13** | **`a4b156a2`** | **success (7/7)** | **failure (#262)** |

Eight consecutive dual-green commits across **29 hours**. Not one was tagged. The ninth — the
current head — is the first commit in forty pushes where certification is red, so **the window is
closed right now**, and it was not closed by the calendar. It was closed by the finding below.

Two pushes are omitted as non-candidates: `34a02682` and `c9f58998` had their `Meridian CI` runs
*cancelled* by concurrency supersede, not failed.

## New P0: the certification blocker is a real cross-process claim defect, and it has broken the release gate twice in four days

`Production Certification` #262 failed on exactly one test out of 1,301:

```text
Meridian.Tests.Reconciliation.StatementStoreDurabilityTests.IndependentWriters_ClaimOneCompleteImport [FAIL]
  Assert.Equal() Failure: Values differ
Expected: 1
Actual:   2
  at .../tests/Meridian.Tests/Reconciliation/StatementImportAndMatchingTests.cs:line 328
Failed!  - Failed: 1, Passed: 1300, Skipped: 0, Total: 1301
```

**This is not harness teardown.** Line 328 is
`Assert.Equal(1, results.Count(result => result == "created"))`. The test starts four independent
*processes*, releases them on a shared gate file, and requires that exactly one claims the import
`durable-import` and three see a duplicate. **Two processes both reported `created`.** The assertion
is on product behavior, and the behavior it describes is a violated uniqueness invariant in the
statement-import durability path.

### It is non-deterministic, and today's run proves it on one commit

`Production Certification`'s `deterministic-integrations` job and `Meridian CI`'s
`service-backed-integrations` leg call the **same reusable workflow**
(`.github/workflows/production-certification.yml:29` and the `referenced_workflows` entry on the
`Meridian CI` run both resolve to `./.github/workflows/service-backed-integrations.yml`, differing
only in `artifact-name`). So on `a4b156a2` the identical selection ran twice, eleven minutes apart:

| Lane | Job | Result |
| --- | --- | --- |
| `Meridian CI` | [117637550791](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39114466333) | `Passed! - Failed: 0, Passed: 1301, Total: 1301` |
| `Production Certification` | [117639469639](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39114466292) | `Failed! - Failed: 1, Passed: 1300, Total: 1301` |

Same commit, same tests, opposite outcomes. The lane rolls twice per push, and today it disagreed
with itself.

### It is recurring, and it is the only thing that has broken certification in forty pushes

Of the last **40** `Production Certification` `main` pushes, exactly **two** failed, and **both
failed on this one test and nothing else**:

| Run | Date (UTC) | Head | Failure |
| --- | --- | --- | --- |
| [#226](https://github.com/rodoHasArrived/Meridian-main/actions/runs/38515346248) | 2026-10-14 05:16 | `7eb68f3f` | `IndependentWriters_ClaimOneCompleteImport`, 1 of 1,219 |
| [#262](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39114466292) | 2026-10-18 09:36 | `a4b156a2` | `IndependentWriters_ClaimOneCompleteImport`, 1 of 1,301 |

Two certification failures, four days apart, one cause. **Neither was recorded anywhere**: the test
name appears in no document, no quarantine file and no risk-register row (`grep` over `*.md` and
`*.json` returns only the test source). Eight determinations have ranked delivery-system hygiene
while the single test that has taken the release gate down went unwritten.

### The mechanism, and why it is deducible rather than guessed

The only cross-process exclusion in the path is at
`src/Meridian.Infrastructure/Reconciliation/BrokerStatementInfrastructure.cs:218-225`:

```csharp
// The no-overwrite rename is the cross-process uniqueness and visibility boundary.
try
{
    File.Move(temporaryPath, targetPath, overwrite: false);
}
catch (IOException) when (File.Exists(targetPath))
{
    return false;
}
```

For two processes to both return `true`, both `File.Move` calls must have completed without
throwing, against one `targetPath`. On Unix a non-overwriting `File.Move` is a destination-existence
check followed by an ordinary `rename(2)`, and `rename(2)` replaces the destination silently — so two
processes that both pass the check before either renames will both succeed, the later silently
replacing the earlier, and both return `true`. That is the only mechanism consistent with
`Expected: 1, Actual: 2` on a single target path, and the comment above the call is the assumption
that fails: the no-overwrite rename is **not** a uniqueness boundary on Linux.

Not reproduced locally — this environment has no .NET SDK — so the evidence is the two CI failures
and the same-commit disagreement above, not a local repro.

### The fix already exists, in a PR yesterday's determination recommended closing

[#3105](https://github.com/rodoHasArrived/Meridian-main/pull/3105) carries it, and its comment names
the same mechanism independently:

```csharp
// Unix no-overwrite moves can check destination absence before an ordinary rename.
// Serialize publication across processes, retaining the lock inode for future writers.
await using var publicationClaim = await AcquirePublicationClaimAsync(targetPath, ct).ConfigureAwait(false);
ct.ThrowIfCancellationRequested();
if (File.Exists(targetPath))
    return false;
```

`AcquirePublicationClaimAsync` opens `targetPath + ".lock"` with `FileShare.None` and a bounded
30-second retry, serializing publication before the check-and-rename. **This is the right shape for
this repository.** The cheaper alternative — creating the target directly with `FileMode.CreateNew`
(`O_EXCL`), which is genuinely atomic and which this very method already uses for its temporary file
at `:209` — would abandon the write-temp-then-rename publish, and `CLAUDE.md` forbids bypassing the
repository's atomic-write patterns in cleanups. The lock keeps the atomic publish and adds the
exclusion it was missing.

**This is a direct correction to #3197**, which ranked #3105 P2 as *"Close or rebase #3105 … stale
ten days with 118 conflicts … Reconstruction costs less than resolution."* Measured today, after an
update at 10:09 UTC: **`mergeable: true`, 0 conflicts**, 131 files, and it holds the fix for the only
thing that has broken release certification in forty pushes. Closing it would have discarded that.

**Recommendation: extract the claim fix, do not wait for #3105.** #3105 is 3,541 additions over 131
files and its own `Meridian CI` is red — one vitest assertion,
`src/screens/report-run-governance-screen.test.tsx > loads the exact query run and enables only
server-authorized lifecycle actions` (`AssertionError: expected "vi.fn()" to be called with
arguments: [ 'run-1', …(1) ]`, 1 of 121). The claim fix is ~25 lines in one file plus a test. Landing
it alone restores a certifiable head today; landing #3105 requires fixing an unrelated screen test
first.

Two review points on the fix, neither a blocker: the `.lock` sidecar is deliberately retained, so the
`statement-imports` directory accumulates one `.lock` per import id (harmless to `ListImportsAsync`,
which globs `*.json`, and to the test's `*.tmp` assertion); and the 30-second bound converts a stuck
peer into a `return`-less throw rather than a `false`.

### The same idiom is unfixed at four more durability sites

`File.Move(..., overwrite: false)` guarded by `catch (IOException) when (File.Exists(...))` is the
publish boundary at:

| Site | Shape |
| --- | --- |
| `src/Meridian.Infrastructure/Reconciliation/BrokerStatementInfrastructure.cs:220` | true/false claim — **the failing one**; fixed only in #3105 |
| `src/Meridian.FinancialOperations/Reconciliation/StatementRepositories.cs:556` | **identical** true/false claim (`:545` early-exists check, `:562` catch → `return false`) — unfixed in every branch |
| `src/Meridian.Infrastructure/Reconciliation/StatementDurabilityInfrastructure.cs:207, 450, 570` | read-back-on-collision rather than a claim, so a double-publish is last-writer-wins silently instead of a double `true` |
| `src/Meridian.Ui.Shared/Services/ReportPackDeliveryService.cs:519` | read-back-on-collision |

Sixteen `overwrite: false` call sites exist in `src/`; the five above are the ones used as a
cross-process publish or uniqueness boundary. **`StatementRepositories.cs:556` is the one that
matters next** — it is the same true/false claim contract as the failing site, in the Financial
Operations statement-run recovery path, with no concurrent test covering it. That it has not failed
in CI is an absence of coverage, not evidence of correctness.

**Why this is P0 and not a flaky-test row.** It is in the reconciliation and retained-evidence path
that `program-state.yml`'s scope gate puts ahead of every expansion lane; its milestone
`W5X-STMT-ONBOARD-001` is recorded `done` with `evidence_posture: complete`; and the release gate in
`implementation-todo-list.md` requires every P0 row complete **on the same release commit**. A
uniqueness invariant that fails roughly one run in twenty cannot be certified on any commit, and it
is what makes the current head uncertifiable.

## New P1: the workflow that measures the signing secret has never run

Four determinations have named the same non-engineering P0 set: four signatures, one signing secret,
one product decision. On 2026-10-16 00:00 UTC, #3179 landed
`.github/workflows/release-readiness.yml`, whose three jobs are exactly the instrumentation for two
of those blockers:

| Job | What it produces |
| --- | --- |
| `observe-release-runner-capabilities` | recent assigned native lifecycle and packaging runner observations → `runners.json` |
| `check-protected-signing-readiness` | inspects `secrets.MDC_SIGNING_CERT_PFX_BASE64` / `MDC_SIGNING_CERT_PASSWORD` in environment `desktop-release-signing` and retains **non-secret** certificate validity evidence → `signing.json` |
| `report-release-prerequisites` | `release-readiness.py summary` over both → `report.json`, "prerequisite status and next actions" |

**It has zero runs.** `GET /actions/workflows/386118281/runs` returns an empty list three days after
it shipped.

That is not a defect — its triggers are `workflow_dispatch` and `cron: "23 8 * * 1"`, and today is
Sunday, so the first scheduled run is **2026-10-19 08:23 UTC**, tomorrow. The finding is what the
zero means: *the evidence that the top P0 has been blocked on for four determinations is one manual
dispatch away, and nobody has dispatched it.* The signing job answers "does the signing secret exist
and is the certificate valid" without exposing it — a question this directory has restated four
times and never resolved. Dispatch it on the head before the next RC attempt, or wait a day and read
tomorrow's scheduled report; either way the answer stops being an open question.

## Carried, re-measured, and materially worse: `Generated Repair Publisher`

Counted today across both result pages — **172 runs, zero successes in its entire history**:

| Event | Conclusion | Count |
| --- | --- | --- |
| `workflow_run` | **failure** | **153** |
| `push` | skipped | 16 |
| `schedule` | skipped | 3 |

The trajectory across three determinations: **31 of 31** (2026-10-16) → **91 of 91** (2026-10-17) →
**153 of 153** (2026-10-18). **Twenty-seven of those failures are on the current head alone** — each
completed `main` workflow run re-triggers it, so one push now manufactures a double-digit burst of
red. This is the single largest source of failure noise in the repository, and every one of those
153 failures is a benign disposition misreported.

The cause is unchanged at `build/scripts/ci/generated_repair.py:544-546`:

```text
except (RepairError, OSError, ValueError, KeyError, zipfile.BadZipFile) as exc:
    write_outputs({"status": "manual", "reason": str(exc)}, args.result)
    return 1
```

`inspect` returns `{"status": "manual", ...}` as a normal outcome in four places (`:302, 333, 343,
346`), and `.github/workflows/generated-repair-publish.yml:62,86` gate every later step on
`status == 'ready' || status == 'retry'` — so `manual` was *designed* to skip the rest and finish
green. Return 0 for a `manual` disposition, reserve non-zero for genuine faults, and add the
regression test to the existing `tests/scripts/test_generated_repair.py`.

**Read the workflow file, not the check name.** `generated-repair.yml` (`pull_request_target`,
surfaces `Prepare generated repair patch`) **works** — it is green on #3105's head right now.
`generated-repair-publish.yml` (`workflow_run`) is the one with no green run ever.

## Carried and re-measured: the conflict tax is back to ~100% generated, and one gap now blocks eight PRs

Re-measured against `a4b156a2` across all **30** open PRs. **15 conflict, for 48 conflicts, of which
47 are in generated files.** Exactly **one** hand-written conflict exists in the whole queue:

| Conflicting path | PRs affected | In the resolver? |
| --- | --- | --- |
| `docs/status/doc-health-dashboard.json` + `.md` | **15 of 15** (30 conflicts) | yes (`:23-24`) |
| `docs/status/example-validation.md` | **8** | **no** |
| `docs/source/generated/source-hash-manifest.json` | 5 | yes (`REVIEWED_MANIFEST`, `:42`) |
| `docs/status/coverage-report.md` | 3 | yes (`:29`) |
| `docs/generated/repository-structure.md` | 1 | yes (`:21`) |
| `docs/product/plans/README.md` (#3170) | 1 | **n/a — hand-written** |

Two of #3197's P2 rows are **spent, by someone else's work rather than by this ranking**: #3176's
conflict in `tests/scripts/test_schema_control_disposable.py` is gone (it is down to the dashboard
pair), and #3105's 118 conflicts are gone (0 today). #3175's generator-manifest conflicts are gone
too. The queue's only real disagreement today is the determination index itself.

`docs/status/example-validation.md` is **still** absent from `GENERATED_FILES` in
`build/scripts/resolve-generated-merge-conflicts.py:20-39`, so the resolver reports `REVIEW` and
stops on **8** PRs — up from 4 yesterday. Five of the six colliding generated paths are covered;
this is the only gap, it is one line, and the file's own comment rules out a wildcard.

`.gitattributes` is still **488 bytes with zero `merge=` entries**, so #3170's merge-driver item
remains wholly unimplemented while the dashboard pair conflicts in 15 of 15 conflicted PRs.

## Nobody is holding any of the carried fixes

Checked every one of the 30 open PR branches for each carried one-liner. **None is in flight:**

| Carried fix | In any open PR? |
| --- | --- |
| `merge=` driver in `.gitattributes` | no |
| `ignore_cleanup_errors` anywhere in `tests/scripts` | no — **98** `TemporaryDirectory(` sites, **0** guarded, unchanged from yesterday |
| `example-validation.md` in `GENERATED_FILES` | no |
| `return 0` for a `manual` disposition | no |

Four items, named across two or three determinations each, zero owners. This is the same observation
four earlier determinations made about rankings accumulating faster than they are acted on — except
#3197 recorded that the trend had broken (the `test_schema_control_disposable` race *was* root-caused
and fixed with a 40/40 reproduction). The trend broke for one item and held for these four.

## What has not changed

- **The user-requested work queue is unchanged:** `W10-LOT-002`, then `W10-MARK-001`, then
  `W10-SEAM-001`, before `W10-RECON-001` in that queue only. Registry ranking remains MARK 1,
  RECON 2, PROV 3, JRNL 5, SEAM 7, LOT 12.
- **The registry holds 48 rows** — 17 `done`, 15 `planned`, 9 `in_progress`, 6 `accepted`,
  1 `ready_for_acceptance` — unchanged since #3185. `docs/product/implementation-todo-list.md:97`
  still reads *"The registry contains **42 rows**"* with 9 `planned`. Only the `planned` and total
  counts are stale, in the document authoritative for the P0 release gate, and the repair has now
  been named in two consecutive determinations.
- **`W9-CORPACT-011` is still the program's only `ready_for_acceptance` row** — now **26 days**, since
  2026-09-22. Taking or declining it is zero engineering; declining is a result.
- Every product item in #3170's and #3197's summary tables stands as written.

## Process finding: the determination backlog is now two deep and both are conflicted

#3170 (2026-10-15) and #3197 (2026-10-17) are **both open, both draft**. #3197 is `dirty` on the
dashboard pair; #3170 carries 5 conflicts including the hand-written index collision, and still
carries the written, reviewed one-line repair of the dangling `known-vulnerabilities.md` anchor at
`docs/engineering/production-certification-evidence-chain.md:280`.

This document deliberately adds no new engineering ranking beyond the P0 above — the backlog it
would rank is already written twice over. **The useful action in this directory is merging #3197 and
resolving #3170, not writing a tenth document.** This one exists because the certification failure is
a product defect that no open document names, and because the eight expired dual-green commits are
the predicted outcome its predecessor asked to have recorded.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0** | **Fix the statement-import claim: extract #3105's `AcquirePublicationClaimAsync` change into a small PR with a concurrency regression test** | `IndependentWriters_ClaimOneCompleteImport` is the **only** failure in the last 40 `Production Certification` pushes and it has caused **both** of them (2026-10-14, 2026-10-18). Two writers both returned `created`; the no-overwrite rename at `BrokerStatementInfrastructure.cs:220` is not a uniqueness boundary on Linux. It is red on the current head, so there is no certifiable commit until it lands. #3105 holds the fix but is 131 files with its own red browser lane |
| **P0** | **Then apply the same exclusion at `StatementRepositories.cs:556`** | Byte-for-byte the same true/false claim contract on the same primitive, in Financial Operations statement-run recovery, with no concurrent test. Its clean record is missing coverage, not correctness |
| **P0** | **Everything in #3170/#3197's non-engineering P0, unchanged** — sign `ADR-019`/`ADR-020`, provision the signing secret, decide the ~1 GB consumer download, continue `PRD-002` from #2931's residual list | Re-verified today: both ADRs read `Proposed`, one tag, no `v*`. **Eight dual-green commits expired unused in 29 hours** while these waited. None of it is engineering |
| **P1** | **Dispatch `Release Readiness` on the head** (or read tomorrow's 08:23 UTC scheduled run) | Zero runs in three days. Its `check-protected-signing-readiness` job answers the signing-secret question four determinations have left open, without exposing the secret, and its summary job reports the remaining prerequisites |
| **P1** | **Return 0 for a `manual` disposition** at `generated_repair.py:544-546`, with a regression test | **153 of 153 failed; still zero green ever**, 27 on the current head alone. 31 → 91 → 153 across three days. The workflow's own step conditions (`:62,86`) prove `manual` was meant to finish green |
| **P1** | **Add `docs/status/example-validation.md` to `GENERATED_FILES`** at `resolve-generated-merge-conflicts.py:20-39` | Now blocks **8** PRs, up from 4. Five of six colliding generated paths are already covered; this is the only gap. One line |
| **P1** | **Add a `merge=` driver to `.gitattributes`** for whole-file generator outputs | The dashboard pair conflicts in **15 of 15** conflicted PRs. `.gitattributes` still has zero `merge=` entries. One change ends the tax for the whole queue at once |
| **P1** | **Add `ignore_cleanup_errors=True`** to the `tests/scripts` `TemporaryDirectory` sites hosting a live `.git`, starting at `test_merge_reviewed_doc_hashes.py:93` | 98 sites, 0 guarded, unchanged. Carried from #3197. Still the most likely cause of the next `verify-workflows` red |
| **P2** | **Merge #3197 and resolve #3170** | Both open, both draft, both conflicted; between them they hold the current product ranking and a written one-line repair of a dangling anchor in the certification evidence chain |
| **P2** | **Correct the 42-row inventory** at `implementation-todo-list.md:97` to 48 rows / 15 `planned` | Stale since #3185, in the document authoritative for the P0 release gate. Named in two consecutive determinations |
| **P2** | **Take or decline `W9-CORPACT-011`** (now 26 days at `ready_for_acceptance`) | Carried from #3170 and #3197. Zero engineering; declining is a result |
| ~~P2~~ **spent** | ~~Close or rebase #3105~~; ~~resolve #3176's `test_schema_control_disposable.py` conflict~~ | **Both resolved by others before this document.** #3105 is `mergeable: true` with 0 conflicts and carries the P0 fix above — closing it would have discarded that. #3176 is down to the dashboard pair |

## The bar for the next determination

**The next determination is due when the statement-import claim fix has landed and
`Production Certification` is green on the head, when the RC is tagged, or when a named signature is
refused.**

The gate clause from two bars ago is retired and should stay retired. What replaces it is narrower:
there is now a *named product defect* standing between the program and a certifiable commit, it has
an owner-less fix sitting in an open PR, and it is the first time in eight determinations that the
top of this list is engineering rather than signatures. That is a better problem than the one this
directory has had all week, and it is small.

If a tenth determination is written while `IndependentWriters_ClaimOneCompleteImport` is still
failing one run in twenty, the finding to record is not that the backlog grew. It is that the program
found the single defect blocking its own release gate, found the fix already written, and left both
where they were.
