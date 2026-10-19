# What To Work On Next — Meridian (2026-10-19)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-19
**Baseline:** `origin/main` at `1a6cf086bb5e2aba6ea5d267dbe1860639bbc120` (2026-10-19 08:36 UTC)
**Supersedes:** nothing on `main`. The newest determination merged to `main` remains
[2026-10-16](next-work-determination-2026-10-16.md). **Three determinations are open and unmerged** —
[2026-10-15 (#3170)](https://github.com/rodoHasArrived/Meridian-main/pull/3170),
[2026-10-17 (#3197)](https://github.com/rodoHasArrived/Meridian-main/pull/3197) and
[2026-10-18 (#3207)](https://github.com/rodoHasArrived/Meridian-main/pull/3207). The product ranking
in all three is re-verified here and carried forward intact; **this document does not re-derive it.**
**Method:** read the 20 most recent `Meridian CI` and `Production Certification` `main` push runs and
resolved **per-job** conclusions on each candidate head rather than trusting workflow conclusions;
pulled the failing job logs of both certification failures in that window and extracted the named
failing test from each; pulled the *same commit's* `Meridian CI` service-backed job log to compare
selections; counted every `Generated Repair Publisher` run by event and conclusion across all four
result pages; read the first `Release Readiness` run's failing job logs; re-measured all **34** open
pull requests with `git merge-tree --write-tree --name-only origin/main pr/<n>`, classifying every
conflicting path against `build/scripts/resolve-generated-merge-conflicts.py`'s own
`generated()` predicate; scanned all 34 open PR branches for each carried fix; parsed the roadmap
registry and risk register with `yaml.safe_load`; read the ADR status lines, the remote tag list,
`.gitattributes`, and the two claim sites in current source.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; `docs/product/implementation-todo-list.md`
> remains authoritative for the P0 release gate. Recheck conflict counts and CI results against the
> live head before acting.

## The bar, and the prediction that came true

The 2026-10-18 determination set its bar as: *"due when the statement-import claim fix has landed and
`Production Certification` is green on the head, when the RC is tagged, or when a named signature is
refused."* Measured today:

| Clause | State |
| --- | --- |
| Claim fix landed | **no** — `AcquirePublicationClaimAsync` appears in no file under `src/` on `main` |
| `Production Certification` green on the head | **yes** — [#272](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39223939600) on `1a6cf086` |
| RC tagged | **no** — `GET /repos/.../tags` returns exactly one tag, `eval-v0.1.0-eval.1`. No `v*` exists |
| A named signature refused | **no** — `docs/adr/019-production-support-matrix-and-deployment-posture.md:3` still reads `Proposed (awaiting core-team sign-off; signing this ADR is the PRD-000 "signed support matrix" artifact)`; `docs/adr/020-lifecycle-control-plane.md:3` still reads `Proposed` |

The conjunction in the first clause is unmet, so by the letter of that bar a tenth ranking is not
due — and this document is not one. It exists because #3207 named the condition under which it
*should* be written, and that condition is now met in fact:

> *"If a tenth determination is written while `IndependentWriters_ClaimOneCompleteImport` is still
> failing one run in twenty, the finding to record is not that the backlog grew. It is that the
> program found the single defect blocking its own release gate, found the fix already written, and
> left both where they were."*

**It failed again, sixteen hours after that was written.** That is the record below, together with
one genuinely new fact — the signing question is answered — and one new causal finding: the conflict
tax the last four determinations ranked as hygiene is now the thing holding the P0 fix out of `main`.

## P0, third occurrence: the statement-import claim defect broke the release gate again

`Production Certification` [#265](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39187752012)
on `97ec959d` (2026-10-19 01:58 UTC) failed on exactly one test out of 1,308:

```text
Meridian.Tests.Reconciliation.StatementStoreDurabilityTests.IndependentWriters_ClaimOneCompleteImport [FAIL]
  Assert.Equal() Failure: Values differ
Expected: 1
Actual:   2
  at .../tests/Meridian.Tests/Reconciliation/StatementImportAndMatchingTests.cs:line 328
Failed!  - Failed: 1, Passed: 1307, Skipped: 0, Total: 1308
```

Byte-for-byte the same assertion, the same two values, the same line as
[#262](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39114466292) the previous day —
re-verified from #262's own job log today, not quoted from #3207. Line 328 is
`Assert.Equal(1, results.Count(result => result == "created"))`: four independent *processes* race for
the import id `durable-import`, and **two of them both reported `created`.** This is a violated
uniqueness invariant in the statement-import durability path, not harness teardown.

**In the 20 most recent `Production Certification` `main` pushes there are exactly two failures, and
both are this test and nothing else:**

| Run | Date (UTC) | Head | Failure |
| --- | --- | --- | --- |
| [#262](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39114466292) | 2026-10-18 09:36 | `a4b156a2` | `IndependentWriters_ClaimOneCompleteImport`, 1 of 1,301 |
| [#265](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39187752012) | 2026-10-19 01:58 | `97ec959d` | `IndependentWriters_ClaimOneCompleteImport`, 1 of 1,308 |

With #226 (2026-10-14, carried from #3207) that is **three gate failures in five days, one cause,
across three different commits** — and the two most recent are on consecutive days.

### The same commit passed and failed the identical selection, for the second time in two days

`Production Certification`'s `deterministic-integrations` job and `Meridian CI`'s
`service-backed-integrations` leg call the same reusable workflow, so each push rolls this selection
twice. On `97ec959d` the two rolls disagreed:

| Lane | Job | Result |
| --- | --- | --- |
| `Meridian CI` | [117853666617](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39187752013) | `Passed! - Failed: 0, Passed: 1308, Total: 1308` |
| `Production Certification` | [117853666553](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39187752012) | `Failed! - Failed: 1, Passed: 1307, Total: 1308` |

#3207 recorded the first such disagreement on `a4b156a2`. This is the second, on a different commit,
the next day. **A commit's certification status is currently decided by which roll of the dice the
release gate happens to read**, which is the precise reason the release gate in
`implementation-todo-list.md` — every P0 row complete *on the same release commit* — cannot be
satisfied by any commit today, including a green one.

### The mechanism is unchanged, and so are both call sites

`src/Meridian.Infrastructure/Reconciliation/BrokerStatementInfrastructure.cs:217-225` still reads:

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

On Unix a non-overwriting `File.Move` is a destination-existence check followed by an ordinary
`rename(2)`, which replaces its destination silently, so two processes that both pass the check
before either renames both return `true`. The comment above the call is the assumption that fails.
*Not reproduced locally — this environment has no .NET SDK — so the evidence is the three CI
failures and the two same-commit disagreements above, not a local repro.*

`src/Meridian.FinancialOperations/Reconciliation/StatementRepositories.cs:546-563` is still the
identical true/false claim contract (early `File.Exists` check at `:545`, `File.Move(..., overwrite:
false)` at `:556`, `catch (IOException) when (File.Exists(path)) → return false` at `:563`) on the
same primitive, in the Financial Operations statement-run recovery path, with no concurrent test
covering it. Its clean CI record is an absence of coverage, not evidence of correctness.

### Five days, three gate failures, and the defect is still written down nowhere

`grep` for `IndependentWriters_ClaimOneCompleteImport` across every `*.md`, `*.json` and `*.yml` in
the tree returns **nothing**; the only hit in the repository is the test source itself.
`build/scripts/ci/script-test-quarantine.json` reads `"quarantined_modules": {}`.
`docs/roadmap/data/risk-register.yml` holds five open rows — doc drift, AI doc skip, simulated-data
presentation, stale marks, calendar coverage — **and none covers this.** The single defect that has
taken the release gate down three times in five days is absent from the risk register that exists to
carry exactly this.

> This is not a request to quarantine it. The repository's own rules forbid reaching green by
> skipping a test, and a release cannot be certified on a gate that was silenced. The gap is that a
> recurring, root-caused, release-blocking defect has no tracked row and therefore no owner.

## P0 made worse by neglect: the only PR carrying the fix re-conflicted in 24 hours

Scanned all 34 open PR branches for `AcquirePublicationClaimAsync`. **Exactly one carries it:**
[#3105](https://github.com/rodoHasArrived/Meridian-main/pull/3105). Nothing else in the queue holds
any form of the fix.

#3207 measured #3105 at **`mergeable: true`, 0 conflicts** on 2026-10-18 and argued that #3197's
recommendation to close it would have discarded the fix. Measured today against `1a6cf086`:
**`mergeable: false`, `dirty`, 7 conflicts.** Every one is a generated file, and **three are outside
the repository's own resolver:**

| Conflicting path in #3105 | In `resolve-generated-merge-conflicts.py`? |
| --- | --- |
| `docs/status/doc-health-dashboard.json` | yes (`GENERATED_FILES`) |
| `docs/status/doc-health-dashboard.md` | yes (`GENERATED_FILES`) |
| `docs/status/coverage-report.md` | yes (`GENERATED_FILES`) |
| `docs/source/generated/source-hash-manifest.json` | yes (`REVIEWED_MANIFEST`, `:42`) |
| `database/manifest/contracts.json` | **no** — written by `tools/schema_control/render.py:390` |
| `database/manifest/dependencies.json` | **no** — written by `tools/schema_control/render.py:391` |
| `docs/generated/database/data-object-catalog.md` | **no** — header reads *"Generated by build/scripts/schema-control.py. Do not edit directly."* |

**This closes the loop the last four determinations left open.** The conflict tax was ranked as
delivery-system hygiene in #3170, #3187, #3197 and #3207. It is not hygiene any more: it is the
mechanism by which the fix for the only defect blocking release certification fell out of mergeable
state overnight, and three of the seven paths doing it are generated files the repo's own resolver
was never told about.

**The recommendation from #3207 still stands and is still cheapest:** extract the claim change into a
small PR — one `src/` file plus a concurrency regression test — rather than waiting on 131 files and
3,544 additions. A PR that narrow touches none of the seven paths above, so it cannot acquire this
tax. #3105's regression is the argument for promoting the resolver item, not a reason to wait for
#3105.

## New: the signing question is answered. The secret is not there

Four determinations named "provision the signing secret" as a P0 and could not say whether it
existed. #3207 observed that `.github/workflows/release-readiness.yml` had **zero runs** and that its
first scheduled firing was due this morning. **It fired, and it answered.**

[Run #1](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39225101960) (schedule,
2026-10-19 08:45 UTC, head `1a6cf086`):

| Job | Result | Evidence |
| --- | --- | --- |
| `observe-release-runner-capabilities` | **success** | `runners.json` retained (325 bytes) |
| `check-protected-signing-readiness` | **failure** | `##[error]Release signing prerequisite failed (signing-secrets-unavailable); inspect the protected desktop-release-signing environment.` |
| `report-release-prerequisites` | **failure** | `Release readiness: blocked; full report: artifacts/release-readiness/report.json` |

Both failures are the instrument working. `build/scripts/ci/release-readiness.py:277` returns
non-zero precisely when a `summary` is `blocked` or `unknown`, and the signing job retained its
non-secret evidence artifact before exiting. **This is a reported status, not a broken workflow** —
and it is the first independent confirmation that `secrets.MDC_SIGNING_CERT_PFX_BASE64` /
`MDC_SIGNING_CERT_PASSWORD` are absent from the protected `desktop-release-signing` environment.

Two consequences:

1. **One of the four non-engineering P0 items stops being an open question and becomes a provisioning
   task.** Nobody needs to investigate the signing secret again; it needs to be installed.
2. **This now reports itself every Monday at 08:23 UTC** (`cron: "23 8 * * 1"`) and will keep
   reporting red until the secret lands, so the blocker can no longer quietly persist unmeasured.
   Expect one red `Release Readiness` run per week until then; it is signal, not noise.

## The certifiable window re-opened, and is open right now

Five `main` pushes since the #265 certification failure are **dual-green on per-job conclusions** —
`Meridian CI` 7/7 and `Production Certification` success:

| Push (UTC) | Head | `Meridian CI` | `Production Certification` |
| --- | --- | --- | --- |
| 2026-10-19 02:24 | `7c902128` | success (7/7) | success (#266) |
| 2026-10-19 03:23 | `ed9ba7a3` | success (7/7) | success (#267) |
| 2026-10-19 03:53 | `a2a30e56` | success (7/7) | success (#268) |
| 2026-10-19 04:24 | `1f171816` | success (7/7) | success (#269) |
| **2026-10-19 08:36** | **`1a6cf086`** (head) | **success (7/7)** | **success (#272)** |

Two pushes are omitted as non-candidates: `bb4a0899` and `b0abdef9` had their `Meridian CI` runs
*cancelled* by concurrency supersede, not failed — the same treatment #3207 applied to `34a02682` and
`c9f58998`.

Adding the two dual-green pushes before #265 (`75950a39`, `213a4db3`, both verified 7/7 per-job) to
#3207's eight, **the program has produced about fifteen dual-green commits since 2026-10-16 and
tagged none of them.** Ten have been merged past. Five are live as of this writing, including the
head.

**Read the window honestly, though.** A green certification run on a commit carrying an intermittent
uniqueness defect is not evidence the commit is sound; it is evidence this roll passed. #265 and
#262 are what that window looks like from the other side, and the same-commit disagreements above
show both outcomes occurring on one tree. **Tagging an RC off today's green head would certify a
dice roll.** That is why the claim fix is P0 rather than P1, and why "wait for a green head" is not a
substitute for landing it.

## Carried and materially worse: `Generated Repair Publisher`

Counted today across all four result pages — **302 runs, zero successes in its entire history:**

| Event | Conclusion | Count |
| --- | --- | --- |
| `workflow_run` | **failure** | **272** |
| `push` | skipped | 26 |
| `schedule` | skipped | 4 |

The trajectory across four determinations: **31 of 31** (2026-10-16) → **91 of 91** (2026-10-17) →
**153 of 153** (2026-10-18) → **272 of 272** (2026-10-19). It has never once succeeded since it
landed on 2026-10-16 00:00 UTC, and it accelerated: 119 new failures in the last 24 hours alone,
because every completed `main` workflow run re-triggers it.

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
regression test to the existing `tests/scripts/test_generated_repair.py`. **Scanned all 34 open PR
branches: no branch carries this change.**

**Read the workflow file, not the check name.** `generated-repair.yml` (`pull_request_target`,
surfaces `Prepare generated repair patch`) **works**. `generated-repair-publish.yml`
(`workflow_run`) is the one with no green run ever. Mistaking one for the other is the easiest way to
dismiss this.

## Carried and re-measured: the queue grew, and the bundle churn came back

**34 open PRs** (up from 30): 19 `dirty`, 11 `unstable`, 3 `clean`, 1 `unknown`. Measured across the
19 conflicted PRs: **353 `CONFLICT` lines over 837 conflicting paths.** Classified against the
resolver's own `generated()` predicate (`:70-71`):

| Class | Paths | Covered by the resolver? |
| --- | --- | --- |
| `src/Meridian.Ui/wwwroot/workstation/` built bundle | **767** | yes — `WORKSTATION_ROOT` prefix |
| Enumerated generator outputs (`doc-health-dashboard.{json,md}`, `coverage-report.md`, `source-hash-manifest.json`, `repository-structure.md`) | 55 | yes |
| `docs/status/example-validation.md` | 7 | **no** |
| `database/manifest/contracts.json` | 3 | **no** |
| `database/manifest/dependencies.json` | 3 | **no** |
| `docs/generated/database/data-object-catalog.md` | 1 | **no** |
| `src/Meridian.Contracts/README.md` (#3222) | 1 | **n/a — hand-written** |

**Exactly one hand-written conflict exists in the entire 34-PR queue.** 836 of 837 colliding paths
are regenerable output.

Two corrections to earlier determinations, both from re-measurement rather than argument:

1. **The workstation-bundle `rename/rename` churn is back.** #3187 recorded that it was "gone from
   the active queue"; it is now **767 of 837 colliding paths**, concentrated in the five oldest PRs
   (#3168, #3169, #3119, #3004, #2896 — 143 to 159 bundle paths each). It did not return because
   anything regressed; those PRs simply aged further behind a rebuilt bundle. The resolver covers it,
   so this is tax, not risk — but "that churn is gone" is no longer a true statement.
2. **The resolver gap is four paths, not one.** #3187 and #3207 named `example-validation.md` as *the*
   single gap. Three more generated paths collide today and are absent from `GENERATED_FILES`:
   both `database/manifest` outputs and `docs/generated/database/data-object-catalog.md` — **and
   those three are what make #3105 unresolvable by the repo's own tooling.** Adding four lines to
   `GENERATED_FILES` (`:20-39`) makes the resolver complete over 836 of today's 837 colliding paths,
   every conflicted PR except #3222, and all seven of #3105's. The file's own comment rules out a
   wildcard over `docs/status` or `docs/generated`, so this is four explicit entries.

`.gitattributes` is still **488 bytes with zero `merge=` entries**, so #3170's merge-driver item
remains wholly unimplemented while the dashboard pair conflicts in 19 of 19 conflicted PRs.

## Nobody is holding any of the carried fixes. Fifth consecutive day

Scanned all 34 open PR branches for each carried one-liner:

| Carried fix | First named | In any open PR? |
| --- | --- | --- |
| `merge=` driver in `.gitattributes` | 2026-10-15 | **no** |
| `ignore_cleanup_errors` in `tests/scripts` | 2026-10-16 | **no** — **98** `TemporaryDirectory(` sites, **0** guarded, unchanged for four days |
| `example-validation.md` in `GENERATED_FILES` | 2026-10-16 | **no** |
| `return 0` for a `manual` disposition | 2026-10-16 | **no** |
| `AcquirePublicationClaimAsync` claim fix | 2026-10-18 | **only #3105**, which went `dirty` today |
| 42→48 row correction at `implementation-todo-list.md:97` | 2026-10-17 | **no** |

Six items, each named across two to five determinations, zero owners. `verify-workflows` has been
green on every one of the 20 most recent `main` pushes, so the teardown race has not fired lately —
which makes `ignore_cleanup_errors` less urgent than #3207 rated it, not more correct to leave.

## What has not changed

- **The user-requested work queue is unchanged:** `W10-LOT-002`, then `W10-MARK-001`, then
  `W10-SEAM-001`, before `W10-RECON-001` in that queue only. Registry ranking remains MARK 1,
  RECON 2, PROV 3, JRNL 5, SEAM 7, LOT 12.
- **The registry holds 48 rows** — 17 `done`, 15 `planned`, 9 `in_progress`, 6 `accepted`,
  1 `ready_for_acceptance` — unchanged since #3185. `docs/product/implementation-todo-list.md:97`
  still reads *"The registry contains **42 rows**"* with 9 `planned`, stale in the document
  authoritative for the P0 release gate, and now named in three consecutive determinations.
- **`W9-CORPACT-011` is still the program's only `ready_for_acceptance` row** — `last_reviewed:
  2026-09-22`, so **27 days** in that posture. Taking or declining it is zero engineering; declining
  is a result.
- **The P0 tracker is unchanged:** `PRD-002` implementation-in-progress, `PRD-000` and `PRD-013`
  through `PRD-017` evidence-gated, zero rows production-certified.
- Every product item in #3170's, #3197's and #3207's summary tables stands as written.

## Process finding: the determination backlog is three deep, and one of them is mergeable today

#3170 (10-15), #3197 (10-17) and #3207 (10-18) are all open. Measured today:

| PR | State | Conflicts | Note |
| --- | --- | --- | --- |
| [#3197](https://github.com/rodoHasArrived/Meridian-main/pull/3197) | open, ready | **0 — `clean`** | 37 review comments addressed; it is the document that re-ranked when the gate bar was met |
| [#3207](https://github.com/rodoHasArrived/Meridian-main/pull/3207) | open, **draft** | 2 (the dashboard pair) | Holds the P0 root-cause analysis this document extends |
| [#3170](https://github.com/rodoHasArrived/Meridian-main/pull/3170) | open, ready | 2 (the dashboard pair) | Still carries the written one-line repair of the dangling `known-vulnerabilities.md` anchor at `docs/engineering/production-certification-evidence-chain.md:280` |

**#3197 is mergeable right now with zero conflicts.** Four consecutive determinations have observed
that rankings accumulate faster than they are merged; this is the first day the top of that backlog
needs no conflict resolution at all. The useful action in this directory is merging #3197, taking
#3207 out of draft, and resolving #3170's two generated conflicts — not writing an eleventh ranking.

This document adds no new product ranking. Its three additions are the third occurrence of a known
P0 defect, the answer to the signing question, and the finding that the generated-file tax is now on
the critical path to the fix.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0** | **Extract #3105's `AcquirePublicationClaimAsync` claim fix into a small PR** — one `src/` file plus a cross-process concurrency regression test | `IndependentWriters_ClaimOneCompleteImport` has now broken the release gate **three times in five days** (2026-10-14, 2026-10-18, 2026-10-19), is the **only** failure in the 20 most recent certification pushes, and passed and failed the identical selection on one commit twice in two days. The no-overwrite rename at `BrokerStatementInfrastructure.cs:220` is not a uniqueness boundary on Linux. A narrow PR cannot acquire the generated-file tax that just made #3105 unmergeable |
| **P0** | **Then apply the same exclusion at `StatementRepositories.cs:556`** | Byte-for-byte the same true/false claim contract on the same primitive, in Financial Operations statement-run recovery, with no concurrent test. Its clean record is missing coverage, not correctness |
| **P0** | **Provision `MDC_SIGNING_CERT_PFX_BASE64` / `MDC_SIGNING_CERT_PASSWORD` in the `desktop-release-signing` environment** | **No longer an open question.** `Release Readiness` run #1 reported `signing-secrets-unavailable` and `Release readiness: blocked` on the head this morning, and will re-report every Monday 08:23 UTC until it lands. Zero engineering |
| **P0** | **The rest of #3170/#3197/#3207's non-engineering P0, unchanged** — sign `ADR-019`/`ADR-020`, decide the ~1 GB consumer download, continue `PRD-002` from #2931's residual list | Re-verified today: both ADRs read `Proposed`, one tag, no `v*`. **About fifteen dual-green commits since 2026-10-16; ten merged past unused; five live right now.** None of it is engineering |
| **P1** | **Add four paths to `GENERATED_FILES`** at `resolve-generated-merge-conflicts.py:20-39`: `docs/status/example-validation.md`, `database/manifest/contracts.json`, `database/manifest/dependencies.json`, `docs/generated/database/data-object-catalog.md` | Four lines make the resolver complete over **836 of 837** colliding paths in the queue — every conflicted PR except #3222, **including all seven conflicts now blocking the P0 fix in #3105**. Promoted from hygiene because it is on the critical path. The file's own comment rules out a wildcard |
| **P1** | **Return 0 for a `manual` disposition** at `generated_repair.py:544-546`, with a regression test | **272 of 272 failed; still zero green ever**; 119 new failures in 24 hours. 31 → 91 → 153 → 272 across four days. The workflow's own step conditions (`:62,86`) prove `manual` was meant to finish green. In no open PR |
| **P1** | **Add a `merge=` driver to `.gitattributes`** for whole-file generator outputs | The dashboard pair conflicts in **19 of 19** conflicted PRs; `.gitattributes` still has zero `merge=` entries. One change ends the tax for the whole queue at once instead of per-path |
| **P1** | **Open a risk-register row for the statement-import claim defect** | Three gate failures in five days and the test name appears in **no** `*.md`, `*.json` or `*.yml` in the tree. The register's five open rows do not cover it, so the defect has no owner. **Not a quarantine request** — the row is what makes it owned |
| **P2** | **Merge #3197, un-draft #3207, resolve #3170's two generated conflicts** | #3197 is `clean` with 0 conflicts today — the first day the top of the determination backlog needs no resolution. #3170 still carries a written one-line repair of a dangling anchor in the certification evidence chain |
| **P2** | **Correct the 42-row inventory** at `implementation-todo-list.md:97` to 48 rows / 15 `planned` | Stale since #3185 in the document authoritative for the P0 release gate. Named in three consecutive determinations |
| **P2** | **Take or decline `W9-CORPACT-011`** (27 days at `ready_for_acceptance`) | Carried from #3170, #3197 and #3207. Zero engineering; declining is a result |
| ~~P1~~ **de-escalated** | ~~`ignore_cleanup_errors=True` across the `tests/scripts` `TemporaryDirectory` sites~~ | Still 98 sites, 0 guarded — but `verify-workflows` was green on all 20 of the most recent `main` pushes, so this is latent rather than active. Fix it when `tests/scripts` is next touched; it no longer competes with the rows above |

## The bar for the next determination

**The next determination is due when the statement-import claim fix has landed on `main`, when the
RC is tagged, or when `Release Readiness` reports anything other than `signing-secrets-unavailable`.**

The conjunctive clause from the last bar is dropped deliberately: "cert green on the head" is
satisfiable by chance while the defect stands, so pairing it with the fix made the bar readable two
ways. The fix landing is the single engineering event that matters, and it is ~25 lines plus a test.

The two non-engineering clauses are now asymmetric in a useful way. The signing secret has stopped
being an investigation and become an action with a weekly automated receipt; the ADR signatures have
not. **If an eleventh determination is written while `ADR-019` still reads `Proposed`, the signing
secret is still absent, and `AcquirePublicationClaimAsync` is still in exactly one conflicted PR,
the finding to record is not that the backlog grew and not that the defect recurred a fourth time.
It is that this directory has now measured the same six one-line fixes and the same four decisions on
five consecutive days, and that measurement is not the scarce resource.**
