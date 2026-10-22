# What To Work On Next — Meridian (2026-10-22)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-22
**Baseline:** `origin/main` at `52c1a6feef0e05e6a95e3295a15335e4089e068d` (2026-10-21 20:02 UTC)
**Supersedes:** nothing. This document **does not re-rank**. The
[2026-10-21 determination](https://github.com/rodoHasArrived/Meridian-main/pull/3249) (#3249, unmerged — the file is not on `main`) ranked this
program correctly and its ranking stands; everything below either verifies one of its rows against
today's head or adds a fact it could not have had.
**Method:** grouped every `push` workflow run on `main` by `head_sha` and workflow name across the
25 most recent completed runs, to test the head for a complete green receipt; read the per-job and
per-step conclusions of the `Meridian CI` run at the tagged commit and pulled its failing job log;
compared `refs/tags/v0.1.0-rc.1` against the commit `DEC-PRD000-SUPPORT-MATRIX-001` authorizes;
re-measured all 38 open pull requests with
`git merge-tree --write-tree --name-only origin/main refs/remotes/pr/<n>`, counting `CONFLICT (...)`
lines and classifying every path against `is_generated()` in
`build/scripts/resolve-generated-merge-conflicts.py:72`; counted
`tempfile.TemporaryDirectory(` call sites under `tests/scripts`.

> This document ranks nothing new. The roadmap registry remains authoritative for status;
> `docs/product/implementation-todo-list.md` remains authoritative for the P0 release gate.
> Recheck CI results and conflict counts against the live head before acting.

## Why this document is three sections long

Twelve determinations exist. Five are open and unmerged (#3170, #3197, #3207, #3223, #3249). The
2026-10-20 determination recommended no twelfth and was right in substance — the two things that
moved that week moved without a ranking document. The 2026-10-21 determination then had to be
written anyway, because its bar genuinely tripped: the RC tag appeared and its first post-tag
blockers were found.

The same logic applies today. The analysis is not the bottleneck and has not been for two weeks.
So this document records only what changed in the last 24 hours and the two measurements that
changed shape, and it recommends merging the analysis that already exists rather than adding to it.

## Three of the 2026-10-21 P0 rows closed in the last 24 hours

Verified against `52c1a6fe` today:

| 2026-10-21 P0 row | State today |
| --- | --- |
| Merge #3244 (`vite` 8.3.1 → 8.3.5), close #3248 as duplicate | **Closed, via the other PR.** #3248 merged as `8c723514`, taking `vite` to 8.3.5; `npm audit` reports zero vulnerabilities in its commit body |
| Regenerate `coverage-report.md` and `doc-health-dashboard.{json,md}` | **Closed at the head.** `verify-docs` passes on `52c1a6fe`; the drift that failed the tagged commit is gone from `main` |
| `Production Certification` failing | **Green at the head.** It is `success` on `52c1a6fe`, where it was `failure` on `c51c5d8d` |

The one P0 row that is unchanged and unverifiable from here is **provisioning
`MDC_SIGNING_CERT_PFX_BASE64` and `MDC_SIGNING_CERT_PASSWORD`** in the `desktop-release-signing`
environment. It is an administrator action, outstanding about a month, and it still gates whether a
tag mints any certification or publish-smoke evidence at all.

## New: the `rc.2` target the plan calls for now exists, and it is the best receipt in the repo's history

#3251 records the corrected candidate as *"`v0.1.0-rc.2` cut from a green `main` head"* and names no
commit — correctly, because at its baseline none existed. `c51c5d8d`, which carries the
`v0.1.0-rc.1` tag, is red: its `Meridian CI` push run fails `verify-docs` at *"Verify whole-repo
generated documentation is committed"*, on a stale `doc-health-dashboard` (165,105 committed lines
against 165,201 regenerated), which fails `quality-gate`.

**Four consecutive green heads have landed since, and the newest carries a complete push receipt.**
Grouping all `push` runs at `52c1a6fe` by workflow:

| Workflow | Conclusion |
| --- | --- |
| Meridian CI | success |
| Production Certification | success |
| Windows Desktop Build | success |
| CodeQL | success |
| Golden Path Validation | success |
| CI | success |
| Documentation Automation | success |
| Demo Smoke | success |
| Pipeline Budget Benchmark | success |
| Roadmap Source Docs | success |
| Generated Repair Publisher | skipped (by design on `push`) |

**Ten workflows green on one commit.** That matches `a9d470c3`'s ten-workflow receipt — the one the
2026-10-20 determination called the best in the repository's history — and replaces two of its
entries with `Windows Desktop Build` and `Demo Smoke`, which matters for a desktop-installer release
line. The readiness tracker's retained baseline `e0ecd952` records eight.

So the `rc.2` decision no longer needs a commit to be found. It needs the signing secrets, and then
a tag. The window is perishable in the way the predecessor described: every merge moves the head off
the receipt.

## New: the conflict tax nearly doubled, and 79% of it is one directory the resolver already claims

Re-measured today across all 38 open PRs, against `52c1a6fe`:

**31 of 38 PRs conflict. 682 total conflicts** — up from 378 across 35 PRs yesterday and 367 across
32 PRs on 2026-10-20. The load has roughly doubled in 48 hours while the queue grew by six PRs.

The composition is the finding, and it has shifted:

| Class | Conflicts | Share |
| --- | --- | --- |
| Built workstation bundle (`src/Meridian.Ui/wwwroot/workstation/assets/*.js`) | **541** | 79% |
| Generated docs (`docs/status/*`, `docs/source/generated/*`, `docs/generated/*`, `docs/roadmap/generated/*`) | 115 | 17% |
| Hand-written | **26** | 4% |

Three determinations aimed the resolver work at `docs/status/*`. That is no longer where the cost
is: 127 tracked, content-hashed bundle files produce rename/rename churn on every PR that rebuilds
them, and they now outweigh every generated doc path combined by nearly five to one.
`doc-health-dashboard.{json,md}` is still the most *widespread* path at 25 of 38 PRs each, but the
bundle is the *volume*.

**The part that makes this ranking, not just measurement:** all 656 generated conflicts are already
inside the resolver's declared scope. `is_generated()` returns true for anything under
`WORKSTATION_ROOT` as well as the `GENERATED_FILES` set
(`build/scripts/resolve-generated-merge-conflicts.py:42,72`). **96% of today's tax is mechanically
resolvable by a script that exists and is correct.** Nothing new needs to be written for it. The
only reason the queue carries 682 conflicts is that the automation which would apply that script has
never once succeeded — the `Generated Repair Publisher` was at 474 runs and zero successes
yesterday, and #3232, which re-architects it, is itself still an open draft.

The 26 hand-written conflicts span 13 paths, the largest being
`docs/architecture/security-master-extensibility-review.md` (6, from the stacked Security Master
review PRs) and `database/manifest/*.json` (10 across four files). Nothing in the queue is blocked by
a real disagreement about code.

## New: `main` carries stale reviewed UI hashes, and it fails the repair automation on every non-UI PR

Found by this document's own pull request. `Generated Repair` (`generated-repair.yml`, the
`pull_request_target` sandbox, not the Publisher) fails on a docs-only PR with
`status: review_required`, `conflicts: []` and `changed_paths: []`. The two modules it names are:

```text
SRC-UI            Merged module/README differs from the selected reviewed entry
SRC-UI-DASHBOARD  Merged module/README differs from the selected reviewed entry
```

**The drift is on `main`, not on the branch.** Running the repository's own validator against a
clean checkout of `52c1a6fe` reproduces it exactly:

```text
$ python3 build/scripts/docs/validate-doc-hashes.py --summary
documentation hash validation: 2 error(s), 0 warning(s)
ERROR: src/Meridian.Ui: source hash drift; update docs or rerun hash manifest after review
ERROR: src/Meridian.Ui/dashboard: source hash drift; update docs or rerun hash manifest after review
```

`52c1a6fe` rebuilt the workstation bundle and regenerated `ui-api-routes.generated.ts`; the reviewed
entries in `docs/source/generated/source-hash-manifest.json` were refreshed before that final
rebuild rather than after it, so the manifest records the pre-rebuild tree.

Two consequences, and the second is the ranking one:

- **Any PR that does not itself touch `src/Meridian.Ui/**` inherits the failure and cannot clear
  it.** A branch whose `src/` tree object is byte-identical to the base produces a merged tree whose
  UI modules are the base's, so the mismatch is structurally untouchable from such a PR. Both
  docs-only PRs opened against this base today — #3258 at 16:04 UTC and #3259 at 16:24 UTC — failed
  this check; the UI-touching PRs #3206 and #3257, which refresh the hashes themselves, passed.
- **It takes the repair sandbox offline for exactly the PRs that need it most.** The sandbox exits
  before generating anything (`generation: null`), so the queue's generated-conflict load — 96% of
  which this same automation is supposed to clear — gets no patch on any docs-only or
  backend-only PR while the manifest stays stale.

The fix is one command plus a genuine review of the two modules
(`validate-doc-hashes.py --write-module SRC-UI --write-module SRC-UI-DASHBOARD`). It belongs to a PR
that touches the UI modules, because refreshing a *reviewed*-hash entry asserts that someone
reviewed that tree; a prioritization document is the wrong place to make that claim.

## Carried, verified unchanged

The two `verify-workflows` races are **still unfixed on day six**: `tests/scripts` has 321
`tempfile.TemporaryDirectory(` call sites and **zero** using `ignore_cleanup_errors`, and the
cancellation/allocation window at `tools/schema_control/disposable.py:432-443` is unchanged. The
lane is green, so this stays P1 — but the `rc.2` plan depends on a head whose gate is *re-runnable*,
not green once, and that is a weaker position than it looks.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0** | **Merge [#3251](https://github.com/rodoHasArrived/Meridian-main/pull/3251)** | It is the release record: it establishes `v0.1.0-rc.1` as invalid on two independent grounds and names `rc.2` as the corrected candidate. It is **not** a draft, and the decision log and evidence chain disagree with reality until it lands |
| **P0** | **Provision `MDC_SIGNING_CERT_PFX_BASE64` / `MDC_SIGNING_CERT_PASSWORD`** | Unchanged and unverifiable from here. Administrator action, outstanding ~1 month. Gates whether a tag mints *any* certification or publish-smoke evidence |
| **P0** | **Tag `v0.1.0-rc.2` on `52c1a6fe`** once the secrets exist | The target the plan asked for now exists and is verified today: ten workflows green on one commit, including `Windows Desktop Build` and `Demo Smoke`. Perishable — every merge moves the head off the receipt |
| **P0** | **Give `Production Certification` a tag trigger**, or decouple `production`/`smoke` from `needs: eligibility` | Carried from 2026-10-21 unchanged. Without it a tag yields certification evidence only if signing succeeds first, which is why `rc.1` minted nothing |
| **P1** | **Land #3232, then run the resolver across the queue** | **96% of 682 conflicts are already in the resolver's scope** and need no new code. The publisher has never succeeded in 474+ runs. This is the single highest-leverage delivery item in the program |
| **P1** | **Stop the bundle churn at source** — a merge driver for `src/Meridian.Ui/wwwroot/workstation/`, or regenerate-on-merge | **541 conflicts, 79% of the tax, one directory.** Four determinations have ranked a generic `.gitattributes` item; this is the specific target. `.gitattributes` already has `eol` rules for exactly these paths and no merge strategy |
| **P1** | **Refresh the `SRC-UI` / `SRC-UI-DASHBOARD` reviewed hashes on `main`** | `main` is stale against its own `validate-doc-hashes.py`, which fails `Generated Repair` on every PR that does not touch the UI modules — taking the repair sandbox offline for precisely the docs-only and backend-only PRs whose generated conflicts it exists to clear |
| **P1** | **Fix the two `verify-workflows` races** | Day six, unchanged: 321 call sites, zero `ignore_cleanup_errors`. The RC's gate needs to be re-runnable, not green once |
| **P2** | **Resolve the determination backlog: merge #3249 and #3170; close #3197, #3207, #3223** | Five open ranking documents. #3249 is current and correct; #3170 carries a written fix for the dangling certification-evidence anchor. The other three were written against premises that have since changed twice |

## The bar for the next determination

**The next determination is due when `v0.1.0-rc.2` is tagged and produces a certification receipt —
or when a tag runs and produces none again.**

Both of today's new findings point the same way: the measurements keep moving, and none of the
movement is waiting on analysis. The RC target is identified, the conflict tax is 96% resolvable by
existing code, and the remaining P0 set is one administrator action and one merge. A fourteenth
determination written before a tag mints evidence would be measuring the delivery path, not
informing it.
