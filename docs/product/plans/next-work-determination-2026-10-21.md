# What To Work On Next — Meridian (2026-10-21)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-21
**Baseline:** `origin/main` at `c51c5d8d2cab537cb422408f175587b59938c8a5` (2026-10-21 15:58 UTC), which is
also the commit tagged `v0.1.0-rc.1`
**Supersedes:** the ranking carried by the merged
[2026-10-20 determination](next-work-determination-2026-10-20.md), whose bar is **met** — the RC tag
exists and its first post-tag blockers are found. It does not supersede the unmerged determinations
#3170, #3197, #3207 and #3223.
**Method:** listed every workflow run on `refs/tags/v0.1.0-rc.1` and every `push` run at the tagged
commit, grouped by workflow name; read the per-step conclusions of each failing job and pulled the
live failing log of both; **reproduced both certification failures locally** rather than reading them
off the run (`build/scripts/docs/run-docs-automation.py` then `git status`; `npm audit --json` in
`src/Meridian.Ui/dashboard`); read `DEC-PRD000-SUPPORT-MATRIX-001` in the decision log and compared
the commit it authorizes against the commit actually tagged; traced the tag trigger of
`production-certification.yml` and the `needs:` graph of `desktop-installer-packaging.yml`;
re-measured all 35 open pull requests with
`git merge-tree --write-tree --name-only origin/main refs/remotes/pr/<n>`, counting `CONFLICT (...)`
lines and classifying each path.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; `docs/product/implementation-todo-list.md`
> remains authoritative for the P0 release gate. Recheck CI results and conflict counts against the
> live head before acting.

## Headline

**The RC tag exists. It was cut onto a commit that fails `Production Certification`, 68 commits past
the commit the signing decision authorized, and it produced no release evidence whatsoever.**

Three things changed since yesterday, and all three are real progress:

- `ADR-019` and `ADR-020` both read **`Accepted`** (core-team sign-off 2026-10-20, recorded as
  `DEC-PRD000-SUPPORT-MATRIX-001`). Four determinations asked for these signatures.
- **`v0.1.0-rc.1` exists** — the first `v*` tag in the repository's history. `git ls-remote --tags`
  now returns two refs where it returned one.
- The carried one-line resolver fix **landed**: `docs/status/example-validation.md` is in
  `GENERATED_FILES` (`build/scripts/resolve-generated-merge-conflicts.py:25`).

And the tag is not a receipt. The three blockers below are its first post-tag failures. Two are small
and already have fixes; the third is the administrator action that has been outstanding for a month
and that turns out to gate *all* tag-time certification evidence, not just the installer.

## Blocker 1 — the tag is on the wrong commit, and that commit is red

`DEC-PRD000-SUPPORT-MATRIX-001` (`docs/roadmap/data/decision-log.yml:177`) is explicit about which
commit to freeze:

> Release candidate v0.1.0-rc.1 is to be tagged on **a9d470c3**, the commit carrying the
> ten-workflow green push receipt recorded in
> `docs/product/plans/next-work-determination-2026-10-20.md`

The tag is on `c51c5d8d`. That is **68 commits and five PR merges later** (#3238, #3236, #3243,
#3239 among them). The ten-workflow green receipt is a property of `a9d470c3`; it does not travel.

And `c51c5d8d` is not green. Grouping all `push` runs at the tagged commit:

| Workflow | Conclusion at `c51c5d8d` |
| --- | --- |
| **Production Certification** | **failure** ([run 298](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39630535664)) |
| **Documentation Automation** | **failure** ([run](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39630535396), `regenerate-docs`) |
| CI | success |
| Golden Path Validation | success |
| Roadmap Source Docs | success |
| Maintenance | success |
| Meridian CI | still `in_progress` at the time of writing |
| CodeQL, Windows Desktop Build | still `in_progress` |
| Generated Repair Publisher | failure (see below) |

The predecessor warned that the receipt was perishable — *"every merge moves the head off the
receipt"* — and recommended freezing *that* commit for exactly this reason. The tag moved instead.

## Blocker 2 — `Production Certification` fails on two of four jobs, and both reproduce locally

Run 298 at the tagged commit. The two failures are independent, and neither is flake:

**2a. Generated-documentation drift — this is `PRD-017`'s named job.**
Job `same-commit documentation evidence`, step 6 **"Reject generated documentation drift."**
Reproduced locally from a clean tree:

```
$ python3 build/scripts/docs/run-docs-automation.py     # all 13 generators report success
$ git status --porcelain
 M docs/status/coverage-report.md
 M docs/status/doc-health-dashboard.json
 M docs/status/doc-health-dashboard.md
```

It is **real content drift, not timestamp noise**: the documented-item denominator moves
9,577 → 9,579 and the documentation line total 165,105 → 165,201. The committed generated docs were
produced from an earlier tree and never refreshed after the final content landed. The fix is one
command plus a commit of three files.

**2b. One unaccepted high-severity npm advisory.**
Job `NuGet and npm dependency evidence`, step 10 **"Assert both dependency gates passed."** Read the
step conclusions in the API and both gates appear to pass — that is misleading. Both carry
`continue-on-error: true` (`production-certification.yml:82,101`), so the reported *conclusion* is
masked and the assertion step reads the true `outcome`. The gate logic is correct; a gate really
failed. Reproduced locally:

```
$ npm audit --json   # in src/Meridian.Ui/dashboard
severity counts: {'high': 1, 'critical': 0, 'moderate': 0, 'low': 0, 'total': 1}
  high  vite  (installed 8.3.1, vulnerable 8.3.0 - 8.3.2)
```

`vite` 8.3.1 carries `GHSA-587h-7wmj-h5pm` (high — launch-editor `fs.existsSync()` existence check
bypass on Windows) plus two moderates. `build/config/security/npm-audit-accepted-advisories.json`
has `"accepted": []`, so `validate-npm-audit.py --fail-level high` fails closed, correctly.

**The fix has been merge-ready since before the tag was cut.**
[#3244](https://github.com/rodoHasArrived/Meridian-main/pull/3244) — dependabot, `vite` 8.3.1 →
8.3.5, non-draft — opened **10:35 UTC today, five and a half hours before the tag**. `npm audit`
confirms `fixAvailable: {vite 8.3.5, isSemVerMajor: false}`, so it is a patch-range bump.
[#3248](https://github.com/rodoHasArrived/Meridian-main/pull/3248) (draft, opened 16:16 UTC)
duplicates it and should be closed.

**What passed, and is worth recording:** the `encrypted backup and clean restore drill` job
succeeded and uploaded its dated recovery evidence (that is `PRD-015`'s artifact), and the
`deterministic PostgreSQL integration and coverage evidence` job succeeded with its zero-skip gate
green. Two of the four certification jobs are genuinely good on this commit.

## Blocker 3 — the tag run minted nothing, and the reason is structural

`Desktop Installer Release` [run 19](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39630903593)
is the only substantive workflow that ran on the tag. It failed at the **first step of the first
job** — `validate-release-eligibility`, step "Validate signing prerequisites before builds"
(`desktop-installer-packaging.yml:62`), in under a second:

```
SIGNING_PFX:
SIGNING_PASSWORD:
##[error]Release signing prerequisite failed (signing-secrets-unavailable);
         inspect the protected desktop-release-signing environment.
```

`MDC_SIGNING_CERT_PFX_BASE64` and `MDC_SIGNING_CERT_PASSWORD` were never provisioned — the decision
log names this as an outstanding administrator action, and the tag was pushed anyway. **Every other
job skipped**: `package-desktop-msix`, `package-consumer-setup`, both clean-install lifecycle jobs,
the consumer-EXE lifecycle job, `verify-and-publish-release-evidence`, and the nested `ci`,
`codeql`, `secrets`, `smoke` and `production` jobs.

**Now correct a premise that three determinations carried.** The 2026-09-27 determination asserted
that `Production Certification` triggers on `v*` tags, and 2026-10-16 and 2026-10-20 inherited the
conclusion that *"the tag run mints `PRD-013`'s publish-smoke attachment, `PRD-015`'s dated drill
artifact, `PRD-016`'s hosted run and `PRD-017`'s documentation-evidence job."* **It does not.**

```yaml
# .github/workflows/production-certification.yml:3-10
on:
  workflow_call:
  workflow_dispatch:
  schedule:
    - cron: "17 3 * * 0"
  push:
    branches:
      - main        # ← no `tags:` entry anywhere
```

On a tag, certification is reachable only as the installer workflow's `workflow_call` job
(`desktop-installer-packaging.yml:99-101`), and that job is `needs: eligibility`. So is `smoke`,
which is `PRD-013`'s publish-smoke evidence (`:103-105`). **The signing secret is not one
prerequisite among several — it is the gate that decides whether a tag produces any certification
evidence at all.** That reframes the item: provisioning the secret is not merely `PRD-014`'s
blocker, it is the precondition for `PRD-013` and `PRD-016` too.

## What to do about `v0.1.0-rc.1`

Two coherent paths. **Recommendation: fix forward and re-tag.**

1. **Fix forward, then tag `v0.1.0-rc.2`.** Merge #3244, regenerate the three generated docs,
   provision the signing secrets, confirm a full green push receipt on the resulting commit, then
   tag that. Nothing is lost by abandoning `rc.1`: it has **no evidence attached to it**, because
   every evidence-producing job skipped. An RC tag whose only artifacts are a
   `signing-readiness` failure record and a `release-readiness: blocked` report is not evidence
   anyone will cite.
2. **Retag `v0.1.0-rc.1` onto `a9d470c3`** as the decision authorized. This honours the decision
   record exactly, but it moves a published tag — and `a9d470c3`'s receipt predates the vite
   advisory's disclosure window, so its green `Production Certification` may not reproduce on a
   re-run. Prefer option 1.

Either way the sequence is the same three fixes first, and two of the three are already written.

## Delivery system: carried items, re-measured

**The conflict queue got slightly worse, and its composition is unchanged.**
**35 open PRs. 25 conflict. 378 total conflicts** (yesterday: 32 / 23 / 367).

| Class | Conflicts |
| --- | --- |
| Generated output and workstation-bundle rename churn | ~365 |
| Hand-written | **13**, across 7 distinct paths |

The hand-written set is still tiny and still contains no disagreement about code:
`docs/architecture/security-master-extensibility-review.md` (5),
`src/Meridian.Infrastructure/README.md` (2), `docs/product/plans/README.md` (2),
`src/Meridian.Contracts/README.md`, `docs/roadmap/data/decision-log.yml`,
`docs/product/meridian-design-document.md`, and
`build/scripts/resolve-generated-merge-conflicts.py` (1 each).

Per-path frequency, all generated, all already covered by the resolver:

| Path | Conflicts on |
| --- | --- |
| `docs/status/doc-health-dashboard.md` / `.json` | **19 of 35 open PRs** each |
| `docs/status/example-validation.md` | 11 PRs |
| `docs/source/generated/source-hash-manifest.json` | 11 PRs |
| `docs/status/coverage-report.md` | 6 PRs |

Note the last row: `coverage-report.md` is both a top conflict path **and** one of the three files
drifting at the tagged commit. The same staleness is blocking certification and taxing the queue.

**`Generated Repair Publisher`: still zero successes, now 474 runs.** Runs 469, 470 and 471 all
failed on the tagged commit; 472, 473 and 474 were queued at the time of writing. The structural fix
remains [#3232](https://github.com/rodoHasArrived/Meridian-main/pull/3232), still open, still draft,
still itself conflicted (3 conflicts, all generated).

**Also still unfixed:** the two `verify-workflows` races (`ignore_cleanup_errors` across the
`tests/scripts` `TemporaryDirectory` call sites; the window at
`tools/schema_control/disposable.py:432-443`). Unchanged for five days.

**One citation correction for the record:** the last two determinations cited the resolver as
`build/scripts/ci/resolve-generated-merge-conflicts.py`. It is at
`build/scripts/resolve-generated-merge-conflicts.py` — there is no `ci/` segment.

## Product lane

No roadmap row moved since yesterday. Registry inventory is unchanged: **48 rows — 17 `done`,
15 `planned`, 9 `accepted`, 7 `in_progress`, 0 `ready_for_acceptance`.** The seven `in_progress`
rows are the two W8 UI-lane rows plus five W10 depth rows, and the user-requested queue is
unchanged: `W10-LOT-002`, then `W10-MARK-001`, then `W10-SEAM-001`, before `W10-RECON-001` in that
queue only.

`program-state.yml` **was** refreshed (`snapshot_date: 2026-10-20`) and now records the closed W9
slate correctly, which closes yesterday's P2. It does not yet record the ADR signatures, the
`DEC-PRD000-SUPPORT-MATRIX-001` scope-outs, or the RC tag; `production_readiness.reviewed` still
reads 2026-10-14 with `baseline_commit: e0ecd952`.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0** | **Provision `MDC_SIGNING_CERT_PFX_BASE64` and `MDC_SIGNING_CERT_PASSWORD` in the `desktop-release-signing` environment** | Administrator action, outstanding a month. Newly reframed: because `production` and `smoke` are `needs: eligibility`, this secret gates whether a tag produces **any** certification or publish-smoke evidence — `PRD-013` and `PRD-016`, not just `PRD-014` |
| **P0** | **Merge [#3244](https://github.com/rodoHasArrived/Meridian-main/pull/3244) (`vite` 8.3.1 → 8.3.5); close #3248 as duplicate** | Closes the one high advisory failing the dependency-evidence gate. Patch-range, `isSemVerMajor: false`, merge-ready since 10:35 UTC — 5.5 h before the tag was cut |
| **P0** | **Regenerate `docs/status/coverage-report.md` and `doc-health-dashboard.{json,md}`** | One deterministic command. Fixes `Production Certification`'s `same-commit documentation evidence` job — `PRD-017`'s named gate — and relieves the #1 and #4 conflict paths in the open queue |
| **P0** | **Decide `rc.1`: fix forward to `rc.2` (recommended) or retag onto `a9d470c3`** | The tag sits 68 commits past the authorized commit, on a commit where certification **failed**, and it minted no evidence. Nothing is lost by abandoning it |
| **P1** | **Give `Production Certification` a tag trigger**, or decouple the `production`/`smoke` jobs from `needs: eligibility` | `production-certification.yml:3-10` has no `tags:` entry. Today a tag yields certification evidence only if signing succeeds first, which couples two unrelated gates and is why `rc.1` produced nothing |
| **P1** | **Land #3232, then run the resolver across the queue** | ~365 of 378 conflicts are generated; only 13 hand-written across 7 paths. Publisher now at 474 runs, zero successes ever |
| **P1** | **Promote the `.gitattributes` / regenerate-on-merge item** | `doc-health-dashboard.{md,json}` conflicts on 19 of 35 PRs. Four determinations have now measured this churn. Recurring by construction |
| **P1** | **Fix the two `verify-workflows` races** | Unchanged for five days. The RC's gate needs to be re-runnable, not green once |
| **P2** | **Refresh the readiness tracker and `program-state.yml` for the ADR signatures, the scope-outs and the RC** | `production_readiness.reviewed` still 2026-10-14 at `baseline_commit: e0ecd952`; `PRD-000`/`PRD-002` text predates the signatures and the `DEC-PRD000-SUPPORT-MATRIX-001` keyring scope-out |
| **P2** | **Correct the tracker's "draft #2931"** | Carried, unchanged. #2931 merged 2026-09-30 |

## The bar for the next determination

**The next determination is due when a tag run produces a complete signed evidence set, or when the
signing secret is formally declined.**

The decision path is no longer the constraint — the signatures came, and the tag came. What the
`rc.1` run proves is narrower and more useful: the remaining gap is three concrete, verified,
cheap fixes, two of which were already written and sitting in the queue when the tag was cut. That
is not a prioritization problem, and it does not need a thirteenth ranking document. It needs the
signing secret provisioned and #3244 merged.
