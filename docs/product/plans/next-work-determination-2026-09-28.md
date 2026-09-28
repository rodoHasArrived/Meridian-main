# What To Work On Next — Meridian (2026-09-28)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-09-28
**Baseline:** `main` at `0b956b06`
**Supersedes:** [2026-09-27](../../../archive/docs/plans/next-work-determination-2026-09-27.md)
**Method:** re-verified every ranked item from the 2026-09-27 document against current source, the
roadmap registry (`docs/roadmap/data/*.yml`), the readiness tracker
(`docs/product/implementation-todo-list.md`), and live GitHub Actions and pull-request state;
re-measured the whole pull-request queue — the `codex/*` set **and the dependency set the last
document did not cover** — with `git merge-tree` against `origin/main`. Every claim is anchored to a
run, a commit, a PR number, a `file:line`, or a reproducible command.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; the tracker remains authoritative for the
> P0 release gate.

## Headline

**In twenty-four hours nothing on the ranked list moved, so the ranking below is unchanged where it
was re-verified — but re-measuring the full queue found a lane the last document never looked at:
ten open dependency pull requests, seven of which merge with zero conflicts — six once the one
major bump among them is held back — the oldest open for seventy-three days. That lane is sequenced against the release candidate, because the RC freezes
whatever dependency set is in the tree when it is tagged.**

- Every commit since the `5980fa00` baseline is documentation. Twelve commits, 118 changed paths
  under `docs/`, and the single `src/*.cs` change is a comment-only path repair
  (`src/Meridian.Contracts/Api/UiApiRoutes.cs:388`). **No product runtime behaviour changed** — but
  validation behaviour did: `80f54a3d` added `check_shared_project_context` to
  `build/scripts/docs/check-ai-inventory.py` and wired it into the inventory run
  (`check-ai-inventory.py:675,879`), which `documentation.yml` executes. A missing or divergent
  shared-context section now turns that workflow from passing to reporting drift. That is a new CI
  contract landing inside a range this document otherwise calls documentation-only, and it is worth
  knowing before anyone treats the range as inert.
- **`Production Certification` is green on the current tip** — [run #98](https://github.com/rodoHasArrived/Meridian-main/actions/runs/36386907387),
  `event: push`, `head_sha: 0b956b06`, all four jobs. The per-push trigger keeps paying out.
- **The pull-request queue went 25 → 26.** Two opened (#3010, #3011); **nothing closed**, including
  #2983, which the last document asked to close.
- **#2998 now merges with zero conflicts** — down from two yesterday. It is the cheapest merge in
  the entire queue and it is accounting work.

## Re-verification: the Tier 0–2 items are all exactly where they were

Each checked against current source today, not against its own record:

| Item (2026-09-27 rank) | Command / anchor | State today |
| --- | --- | --- |
| Cut an RC tag (Tier 0) | `git tag --list` | **Unchanged.** Still exactly one tag, `eval-v0.1.0-eval.1`. No `v*` tag has ever existed |
| Signing secret in `desktop-release-signing` | `desktop-installer-packaging.yml:129,172,183` | **Unchanged.** Still the one hard prerequisite **for freezing and running the signed tag**, still not engineering work. It does not gate the installer-size decision, the dependency drain, the tenancy work or the PostgreSQL test |
| ~1 GB consumer installer decision | tracker line 107 | **Unchanged.** `Meridian-Setup.exe` still recorded at 1,043,350,783 bytes, still "a product decision to revisit" |
| Tenancy fail-closed default (Tier 1, `W9-GOV-008`) | `TenantScopeServiceRegistration.cs:16-18` | **Unchanged.** Default is still `DeploymentBoundary`, still selectable by environment variable alone, still no configuration key |
| `W9-GOV-008` row hygiene | `roadmap-items.yml:1235` | **Worse.** `last_reviewed: 2026-08-29` is now 30 days stale; the self-contradicting remainder clause is still there |
| Statement/reconciliation PostgreSQL round trip (Tier 2) | `grep -rl "LedgerDatabaseFact" tests/ \| grep -i "statement\|reconcil\|ingest"` | **Unchanged. Still no matches.** Nothing proves that path on PostgreSQL |
| `.gitattributes` merge driver (Tier 3) | `cat .gitattributes` | **Unchanged.** Two rules, both `text`; no merge driver for any generated tree |
| `W9-CORPACT-011` acceptance (Tier 4) | `roadmap-items.yml:1335` | **Unchanged.** Still `ready_for_acceptance`, now **6 days** |
| `W10-LOT-002` "shadow-operation acceptance" (Tier 5) | `grep -rli "shadow.operation" src/ tests/` | **Unchanged. Still zero hits.** A criterion on the slate's only `critical` row that exists nowhere in the tree |

Nine re-verifications, nine unchanged. That is the finding, and it is why this document is short:
re-arguing a ranking that nothing has acted on would be noise. What follows is only what is new.

## New — Tier 1: the dependency queue, and it is sequenced against the RC

The 2026-09-27 document ranked the `codex/*` branches and did not mention dependency updates at
all. Measured today with `git merge-tree --write-tree origin/main <branch>`:

| PR | Update | Conflicts | Open since |
| --- | --- | --- | --- |
| #2968 | `Microsoft.AspNetCore.OpenApi` 10.0.11 → 10.0.12 | **0** | 2026-09-14 |
| #2967 | `Microsoft.Extensions.Configuration` and 4 others | **0** | 2026-09-14 |
| #2965 | dashboard testing group, 3 updates | **0** | 2026-09-14 |
| #2944 | `sharp` 0.35.3 → 0.35.4 | **0** | 2026-09-11 |
| #2932 | dashboard tooling group, 2 updates | **0** | 2026-09-07 |
| #2878 | `lucide-react` 0.468.0 → 1.34.0 (**major**) | **0** | 2026-08-31 |
| #2307 | dashboard build-tooling group, 6 updates | **0** | **2026-07-17 (73 days)** |
| #2903 | `postcss-selector-parser` 6.1.2 → 6.1.4 | 1, and it is `docs/source/generated/source-hash-manifest.json` | 2026-09-03 |
| #2981 | logging group, 3 updates | 1, `tests/Meridian.Tests/Futures/FutureProjectionServiceTests.cs` | 2026-09-21 |
| #2587 | `react-router` / `react-router-dom` (**major**) | 607 across 1,421 files | 2026-08-09 |

**Seven merge clean. Six of those seven touch one or two files each.** #2981 is green on all five
workflows — `Meridian CI`, `CI`, `Maintenance`, `CodeQL`, `Windows Desktop Build` — on
[run 35663808195](https://github.com/rodoHasArrived/Meridian-main/actions/runs/35663808195) and has
sat seven days since.

Why this is ranked rather than filed as chores: **an RC tag freezes the dependency set.** The Tier 0
sequence produces same-commit evidence for five P0 rows on a frozen commit. If that commit carries a
dependency set two to ten weeks behind, the first thing that happens after the RC is a wave of
bumps, and every one of them invalidates the same-commit evidence the tag was cut to mint. Draining
the eligible clean ones costs nothing and has to happen **before** the freeze, not after. Be precise
about which: seven carry zero conflicts (#2968, #2967, #2965, #2944, #2932, #2878, #2307), and
**#2878 is a major bump held back below, so the pre-freeze drain is the other six.**

Two exceptions to handle separately, not in the drain:

- **#2878 and #2587 are major bumps** (`lucide-react` 0→1, `react-router`). Majors do not belong in
  a pre-freeze drain; hold them until after the RC. #2587 is already correctly a draft, and its
  607-conflict / 1,421-file spread says it needs re-cutting rather than merging.
- **#2981's one conflict is a real test file**, not generated output — resolve it rather than
  batching it.

## New — Tier 2: #2998 is now a clean merge, and the queue is measurably decaying

Re-measured the whole `codex/*` set today. **Thirteen branches are open, not eleven** — the
2026-09-27 commit message said eleven while its own table listed thirteen; the table was right.

Two changes in one day, and they point in opposite directions:

- **#2998 `acct-checklist-06-gl-providers` went 2 conflicts → 0.** It merges cleanly right now. It
  completes credentialed Xero and NetSuite GL imports and controlled export review — accounting
  work, squarely inside the `scope_gate`. It was updated today. **This is the cheapest merge
  available and it should be taken first.**
- **#3004 `reconciliation-lineage-2636` went 1 hand-written conflict → 2.** The new one is
  `docs/roadmap/data/roadmap-items.yml`, and it appeared purely because `main` moved underneath it.
  Nothing was done to #3004.

That second line is the merge-driver argument acquiring its own measurement. The last document
argued from churn volume — 1,820 touch events across 1,452 distinct paths in 30 days over 118
tracked asset files, all still true today. **#3004 is the same argument observed happening**:
a branch got harder to merge in twenty-four hours of documentation-only commits. The queue decays
whether or not anyone works it.

Current hand-written conflict counts, generated trees excluded:

| PR | Branch | Total | Hand-written | Disposition |
| --- | --- | --- | --- | --- |
| #2998 | `acct-checklist-06-gl-providers` | **0** | **0** | **merges clean today** |
| #2999 | `flush-after-rejected-batches` | 2 | 0 | mechanical |
| #2928 | `p0-csv-evidence` | 2 | 0 | mechanical |
| #2920 | `p0-cash-ladder-currency-evidence` | 4 | 0 | mechanical |
| #2953 | `manual-journal-audit-recovery` | 52 | 0 | mechanical |
| #2896 | `replay-parameter-fail-closed` | 85 | 0 | mechanical |
| #2931 | `p0-scoped-credential-ownership` | 7 | 1 (`implementation-todo-list.md`, resolves to `main`) | mechanical |
| #2897 | `portfolio-corporate-action-snapshots` | 91 | 1 (`MultiSymbolMergeEnumerator.cs`) | one file |
| #3004 | `reconciliation-lineage-2636` | 8 | **2** (was 1) | registry + operators doc |
| #2929 | `p0-ofx-account-scope` | 162 | 2 | the two shared operations-continuity fixtures |
| #2930 | `p0-statement-durable-commit` | 162 | 2 | same two fixtures |
| #2826 | `first-trusted-close` | 4 | 4 | genuine; includes `LedgerJournalInternalTransactionSource.cs` |
| #2789 | `backtesting-quantscript-milestone-1` | 113 | 9 | still correctly `[DRAFT — RE-CUT REQUIRED]` |

> **Post-baseline note (added when this branch merged `main`):** **#2999 merged** in `80219e32`,
> after the `0b956b06` baseline this document was measured at. It is the first branch to leave the
> table above, and it left as the mechanical case the table predicted. The rest of the measurement
> stands as recorded at the baseline.

Resolving `OperationsContinuityWorkflowServiceTests.cs` and
`FinancialOperationsCommandCenterReadService.PublicationTests.cs` once on `main` still converts
#2929 and #2930 into the mechanical case — one resolution, two branches.

Also new: **#3010** ("Address #2931 review findings and merge current main", opened today, 114
files, 3 conflicts) is a live successor to #2931. Read the two together before merging either;
merging both independently is how the same change lands twice.

## Everything else carries forward unchanged

The 2026-09-27 ranking stands as written for:

- **Tier 0** — the RC sequence: signing secret → installer-size decision → freeze and tag →
  **dispatch `Publish Smoke` at the tagged SHA** → activate the required check → ADR-019/ADR-020 and
  support-matrix sign-off. **Correcting the 2026-09-27 claim that one tag run discharges four of the
  six evidence-gated P0 rows:** `Desktop Installer Release` and `Production Certification` do trigger
  on `push: tags: v*`, but `Publish Smoke` does not — `publish-smoke.yml:3-5` is `workflow_dispatch`
  only. The tracker's `PRD-013` row asks for the `web-workstation`/`win-x64` Publish Smoke run *on
  the frozen commit* (tracker line 106), so that row needs an explicit dispatch at the tagged SHA or
  the lane wired into the tag flow. The tag alone leaves `PRD-013` without its same-commit evidence.
- **Tier 1 (that document's)** — `W9-GOV-008`: backfill, promote `FailClosed` to the supported
  default with a real configuration key, add the rejection regressions, record the posture in the
  support matrix. Still sequenced with Tier 0 because `PRD-000` must declare this posture.
- **Tier 2 (that document's)** — `StatementReconciliationPostgresRoundTripTests`. Still the highest-value
  engineering item; still nothing covering that path on PostgreSQL; the identical gap held
  `W9-CORPACT-011` and its round trip found two real defects.
- **Tier 4** — take or decline `W9-CORPACT-011`. Zero engineering. Now six days.
- **Tier 5** — `W10-SEAM-001` then `W10-MARK-001` live certification; continue `W10-LOT-002` on
  successors and advance refunding; **scope or drop "shadow-operation acceptance."**

## What not to work on

Unchanged from 2026-09-27, and re-verified: no new product surface (`scope_gate`), not the other
ten planned W10 rows (`W10-RECON-001`, `W10-PROV-001`, `W10-RECON-002`, `W10-JRNL-001`,
`W10-TAX-001`, `W10-RECON-003`, `W10-RECON-004`, `W10-PERF-001`, `W10-CONSOL-001` and
`W10-DEBT-001` — counted from the registry at the baseline, where the 2026-09-27 document said
eight), no re-cutting the large stranded branches except #2789, no untracking the
built asset tree, no re-litigating accepted W9 rows, no re-proving the certification lane.

One addition: **do not treat "the list did not change" as permission to skip the list.** Nine
re-verifications came back identical because nothing was acted on, not because the items resolved
themselves. Two of them — the tenancy default and the missing PostgreSQL round trip — are open
correctness exposure, and one is a `critical` row carrying a criterion that exists nowhere in source.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| P0 | Provide `MDC_SIGNING_CERT_PFX_BASE64` in the protected `desktop-release-signing` environment | Hard prerequisite for **freezing and running the signed RC**, not for the rest of Tier 0; not engineering work. Everything else below can proceed while the credential is outstanding |
| P0 | Decide the ~1 GB consumer installer question | It ships inside the RC; decide before the tag |
| P0 | **Drain the six eligible zero-conflict dependency PRs before the freeze** (seven are clean; #2878 is held back) | The RC freezes the dependency set; bumping after the tag invalidates the same-commit evidence the tag exists to mint. Oldest has been open 73 days |
| P0 | Freeze a commit, cut `v0.1.0-rc.1`, **and separately dispatch `Publish Smoke` at the tagged SHA** | Still no `v*` tag in the repository. The tag run does not mint everything: `publish-smoke.yml:3` is `workflow_dispatch`-only, so `PRD-013`'s `web-workstation`/`win-x64` evidence needs its own dispatch at the frozen commit. `0b956b06` is green on run #98 |
| P0 | Backfill tenancy; make `FailClosed` the supported default with a real config key; add the rejection regressions | Re-verified today: still env-var-only, still defaults open. The one `W9-GOV-008` remainder |
| P0 | Add `StatementReconciliationPostgresRoundTripTests` | Re-verified today: still zero PostgreSQL coverage of statement/reconciliation |
| P1 | **Merge #2998 — it conflicts with nothing today** | Cheapest merge in the queue, in-scope accounting work, and clean merges do not stay clean |
| P1 | Take or decline `W9-CORPACT-011` (6 days) | Zero engineering; clears the last W9 acceptance lane |
| P1 | Resolve the two shared operations-continuity fixtures once on `main`; drain the five generated-only branches | One resolution converts #2929 and #2930; the other five need no product decision |
| P1 | Add the `.gitattributes` merge driver — **over generator-owned paths only, excluding `src/*/README.md`** | #3004 got harder to merge in one day of documentation-only commits; the decay is now measured, not projected. Narrowing the 2026-09-27 path list: `docs/documentation-ownership.md:21` classifies registered `src/**/README.md` as source-module truth, not generated output, so an ours-style driver there could silently discard real README changes |
| P2 | Read #3010 and #2931 together before merging either | #3010 is a live successor; merging both lands the same change twice |
| P2 | Schedule `W10-SEAM-001` live certification, then `W10-MARK-001` | Both one operator session from closing; `SEAM` unblocks the desktop lane |
| P2 | Continue `W10-LOT-002`; **scope or drop "shadow-operation acceptance"** | Only `critical` row, still carrying a criterion with zero source presence |
| P3 | Correct the `W9-GOV-008` row (self-contradicting remainder, now 30 days stale); close #2983 | Both still misdescribe the program; #2983 was asked to be closed yesterday and is still open |
| P3 | Hold #2878 and #2587 (major bumps) until after the RC; re-cut #2587 | Majors do not belong in a pre-freeze drain; #2587 spreads 607 conflicts over 1,421 files |
