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
> Queue counts, conflict measurements and "today" below refer to the `0b956b06` snapshot, not a
> continuously refreshed queue. This branch later integrated `87dc5bf15`; the post-baseline note
> records known completed actions. Recheck each remaining PR's current head, reviews and gates
> before acting on the baseline ranking.

## Headline

**In twenty-four hours nothing on the ranked list moved, so the ranking below is unchanged where it
was re-verified — but re-measuring the full queue found a lane the last document never looked at:
ten open dependency pull requests, seven of which merge with zero conflicts — six once the one
major bump among them is held back — the oldest open for seventy-three days. That lane is sequenced against the release candidate, because the RC freezes
whatever dependency set is in the tree when it is tagged.**

- The measured `5980fa00..0b956b06` baseline range is documentation and validation work. Twelve commits, 118 changed paths
  under `docs/`, and the single `src/*.cs` change is a comment-only path repair
  (`src/Meridian.Contracts/Api/UiApiRoutes.cs:388`). **No product runtime behaviour changed** — but
  validation behaviour did: `80f54a3d` added `check_shared_project_context` to
  `build/scripts/docs/check-ai-inventory.py` and wired it into the inventory run
  (`check-ai-inventory.py:675,879`), which `documentation.yml` executes. A missing or divergent
  shared-context section now turns that workflow from passing to reporting drift. That is a new CI
  contract landing inside a range this document otherwise calls documentation-only, and it is worth
  knowing before anyone treats the range as inert.
- **`Production Certification` is green on baseline `0b956b06`** — [run #98](https://github.com/rodoHasArrived/Meridian-main/actions/runs/36386907387),
  `event: push`, `head_sha: 0b956b06`, all four jobs. This is baseline evidence only: the PR has
  since integrated `87dc5bf15`, including the #2999, #3017 and #2998 source changes. This run does not
  certify that updated head or a future frozen release commit.
- **The pull-request queue holds 26 open PRs at the baseline** — counted directly, and the number to
  trust. The 2026-09-27 document reported 25, and two opened since (#3010, #3011), which would give
  27; the delta does not reconcile, so one of the two counts is wrong and I did not verify the
  earlier one before repeating it. Stated as measured rather than as a trend. **#2983 is still
  open**, which the last document asked to close.
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
| Statement/reconciliation PostgreSQL **round trip** (Tier 2) | `grep -rho "\[[A-Za-z]*DatabaseFact\]" tests/ \| sort -u`, then the statement/reconciliation subset | **Gap is narrower than the 2026-09-27 document said.** That document probed only `LedgerDatabaseFact`; there are **seven** `*DatabaseFact` attributes, and statement/reconciliation rows *are* covered on PostgreSQL — `StatementReconciliationReportAuthorityStoreTests.cs`, and `PostgresFundAccountStoreTests.cs` for atomic/idempotent custodian-statement persistence and rollback. What is missing is the **end-to-end import → journal-sourced ledger → deterministic match → casework round trip**, not database coverage as such |
| Generated-tree merge remedy (Tier 3) | `cat .gitattributes`; `docs/engineering/docs-regeneration-automation-design.md:263-267` | **The carried-forward remedy was the wrong one.** `.gitattributes` still has two `text` rules and no driver — but the repository has already **formally rejected** a `.gitattributes` driver for this exact workflow. The remedy is the documented resolve-and-regenerate command, not a driver |
| `W9-CORPACT-011` acceptance (Tier 4) | `roadmap-items.yml:1335` | **Unchanged.** Still `ready_for_acceptance`, now **6 days** |
| `W10-LOT-002` "shadow-operation acceptance" (Tier 5) | `grep -rli "shadow" docs/engineering/blueprints/`; `security-lot-convergence-blueprint.md:252-253` | **The 2026-09-27 reading was wrong, and so was mine.** The `src/`+`tests/` grep returns nothing, but that probe never searched `docs/`. Step 5 of the roadmap-linked lot-convergence blueprint **defines** shadow operation: dual-project legacy and canonical results, block cutover on quantity, basis, relief, amortization or corporate-action differences. It is **unimplemented, not unscoped** |

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

**Seven merge clean. Six of those seven touch one or two files each.** #2981 has successful runs
on head `f928a6d97d2246e0ecc9b721fd771e6646ff2fc5` for all five workflows:
[Meridian CI](https://github.com/rodoHasArrived/Meridian-main/actions/runs/35663808195),
[CI](https://github.com/rodoHasArrived/Meridian-main/actions/runs/35663808173),
[Maintenance](https://github.com/rodoHasArrived/Meridian-main/actions/runs/35663808166),
[CodeQL](https://github.com/rodoHasArrived/Meridian-main/actions/runs/35663808189), and
[Windows Desktop Build](https://github.com/rodoHasArrived/Meridian-main/actions/runs/35663808186).
Those runs establish that head's results; resolving its conflict requires fresh final-head validation
before review and merge.

Why this is ranked rather than filed as chores: **an RC tag freezes the dependency set.** The Tier 0
sequence produces same-commit evidence for five P0 rows on a frozen commit. If that commit carries a
dependency set two to ten weeks behind, the first thing that happens after the RC is a wave of
bumps, and every one of them invalidates the same-commit evidence the tag was cut to mint. Draining
the eligible clean ones costs nothing and has to happen **before** the freeze, not after. Be precise
about which: seven carry zero conflicts (#2968, #2967, #2965, #2944, #2932, #2878, #2307), and
**#2878 is a major bump held back below, so the pre-freeze drain is the other six.**
Since that snapshot, #2967 merged in `8123e84798`; the other five baseline members need a fresh
readiness check. The six-member count remains the baseline measurement, not six still-open PRs.

Three exception groups to handle separately from the six clean PRs:

- **#2878 and #2587 are major bumps** (`lucide-react` 0→1, `react-router`). Majors do not belong in
  a pre-freeze drain; hold them until after the RC. #2587 is already correctly a draft, and its
  607-conflict / 1,421-file spread says it needs re-cutting rather than merging.
- **#2981's one conflict is a real test file**, not generated output — resolve it rather than
  batching it, then validate and review the refreshed head before including it in the candidate.
- **#2903 is a non-major patch update to prepare before the freeze.** Resolve its source-hash
  manifest conflict using the [source documentation workflow](../../source/README.md): review the
  affected source/README alignment before refreshing only the justified module hashes. The
  [merge-recovery guide](../../engineering/generated-merge-recovery.md) treats that manifest as a
  reviewed baseline, so do not accept either side or new hashes automatically. Regenerate any
  genuinely affected output, validate the final head, and obtain review before merging it into the
  candidate. If it cannot pass those gates, explicitly record its deferral before freezing; it is
  not silently excluded with the two major updates.

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

That second line measures the decay the last three documents argued from churn volume — 1,820
touch events across 1,452 distinct paths in 30 days over 118 tracked asset files, all still true
today. **#3004 is that argument observed happening**: a branch got harder to merge in twenty-four
hours of documentation-only commits. The queue decays whether or not anyone works it.

**But the remedy those documents proposed does not work, and the repository already said so.**
The `.gitattributes` merge driver carried forward since 2026-09-23 is listed under **Rejected
alternatives** in `docs/engineering/docs-regeneration-automation-design.md:263-267`: "A custom
driver runs only where `merge.<driver>.driver` is configured locally, and GitHub's server-side
merge inherits no contributor configuration, so it would never run for the normal pull-request
merge this work exists to fix." `docs/engineering/generated-merge-recovery.md:71-75` says the same
and explains why an explicit command exists instead. So the recommendation was for a mechanism that
cannot act on the merges being measured — the decay is real, the proposed fix was not.

**The remedy is the resolve-and-regenerate command that `generated-merge-recovery.md` already
documents**, or the hosted automation its design note describes. This is not theory: resolving
*this* document's own base-merge conflict is exactly what worked — take the base side of the
generated status files, re-run `run-docs-automation.py --profile core`, verify both sides survive.
That is the drain procedure, and it needs no `.gitattributes` change at all.

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
> **Follow-up integration (through reviewed head `27712ad`):** #2967 merged in `8123e84798`, #2998
> merged in `87dc5bf15`, and **#2968 and #2965 are also in this branch's ancestry** via
> `208e8c480` and `952e99815`. That is **three of the six eligible baseline dependency PRs closed**,
> leaving #2944, #2932 and #2307. Their baseline recommendations are completed and must not be
> queued again. These known closures do not establish a new whole-queue count or refresh other PRs'
> conflict evidence.

Resolving `OperationsContinuityWorkflowServiceTests.cs` and
`FinancialOperationsCommandCenterReadService.PublicationTests.cs` once on `main` still converts
#2929 and #2930 into the mechanical case — one resolution, two branches.

Also new: **#3010** ("Address #2931 review findings and merge current main", opened today, 114
files, 3 conflicts) is a live successor to #2931. Read the two together before merging either;
merging both independently is how the same change lands twice.

## Everything else carries forward unchanged

The 2026-09-27 ranking stands as written for:

- **Tier 0** — the RC sequence, **reordered**: both signing secrets → installer-size decision →
  **fix the prerelease → MSIX version mapping** → dependency drain → **tenancy fail-closed and the
  reconciliation round-trip test** → only then freeze and tag → **dispatch `Publish Smoke` at the
  tagged SHA** → **operator replay/reconciliation review of the recovery-drill artifact** →
  activate the required check → ADR-019/ADR-020 and support-matrix sign-off.
  **Two corrections to the order the 2026-09-27 document implied.** First, the tracker requires
  every P0 row complete on *one* release commit (`implementation-todo-list.md:125-127`), so the
  tenancy fix and the new round-trip test — both candidate-changing P0 work — must land *before* the
  freeze; tagging first would certify the pre-fix tenancy posture, omit the test, and force a second
  tag and a second full evidence run. This document made that mistake itself: it argued that
  post-tag dependency bumps invalidate same-commit evidence and then sequenced two P0 code changes
  after the tag. Second, `v0.1.0-rc.1` cannot be the tag as written — see the version-identity row
  in the summary. **Correcting the 2026-09-27 claim that one tag run discharges four of the
  six evidence-gated P0 rows:** `Desktop Installer Release` and `Production Certification` do trigger
  on `push: tags: v*`, but `Publish Smoke` does not — `publish-smoke.yml:3-5` is `workflow_dispatch`
  only. The tracker's `PRD-013` row asks for the `web-workstation`/`win-x64` Publish Smoke run *on
  the frozen commit* (tracker line 106), so that row needs an explicit dispatch at the tagged SHA or
  the lane wired into the tag flow. The tag alone leaves `PRD-013` without its same-commit evidence.
- **Tier 1 (that document's)** — `W9-GOV-008`: backfill, promote `FailClosed` to the supported
  default with a real configuration key, add the rejection regressions, record the posture in the
  support matrix. Still sequenced with Tier 0 because `PRD-000` must declare this posture.
- **Tier 2 (that document's)** — `StatementReconciliationPostgresRoundTripTests`, still the
  highest-value engineering item, but **stated correctly**: the gap is the end-to-end round trip,
  not database coverage. Statement rows already have PostgreSQL evidence
  (`StatementReconciliationReportAuthorityStoreTests.cs`, `PostgresFundAccountStoreTests.cs`);
  what is missing is import → journal-sourced ledger → deterministic match → casework in one test
  over the real stores. The identical *round-trip* gap held `W9-CORPACT-011`, and closing it found
  two real defects.
- **Tier 4** — take or decline `W9-CORPACT-011`. Zero engineering. Now six days.
- **Tier 5** — `W10-SEAM-001` then `W10-MARK-001` live certification; continue `W10-LOT-002` on
  successors and advance refunding. **Correcting 2026-09-27 and my own first pass: do not drop
  "shadow-operation acceptance."** Both readings grepped only `src/` and `tests/` and concluded the
  criterion was undefined. It is defined — `security-lot-convergence-blueprint.md:252-253`, step 5
  of the convergence sequence — so it is implementation-and-test work. Dropping it would remove the
  stage that blocks a lot-model cutover on quantity, basis, relief, amortization and
  corporate-action differences, which is the one gate standing between a canonical-lot cutover and
  silent basis corruption.

## What not to work on

Unchanged from 2026-09-27, and re-verified: no new product surface (`scope_gate`), not the other
ten planned W10 rows (`W10-RECON-001`, `W10-PROV-001`, `W10-RECON-002`, `W10-JRNL-001`,
`W10-TAX-001`, `W10-RECON-003`, `W10-RECON-004`, `W10-PERF-001`, `W10-CONSOL-001` and
`W10-DEBT-001` — counted from the registry at the baseline, where the 2026-09-27 document said
eight), no re-cutting the large stranded branches except #2789, no untracking the
built asset tree, no re-litigating accepted W9 rows, no re-proving the certification lane.

One addition: **do not treat "the list did not change" as permission to skip the list.** The
re-verifications came back identical because nothing was acted on, not because the items resolved
themselves. The tenancy default and the missing end-to-end reconciliation round trip are both open
correctness exposure.

## A note on method, because this document got several things wrong

Review of this document found eleven defects, and the pattern behind the worst of them is worth
recording so the next determination avoids it. **Three came from probing too narrow a surface and
reporting the null result as fact:**

- "No PostgreSQL coverage of statement/reconciliation" came from grepping one attribute name.
  There are seven `*DatabaseFact` attributes and the coverage exists; the real gap was narrower.
- "Shadow-operation acceptance exists nowhere in the tree" came from grepping `src/` and `tests/`
  but never `docs/`. It is defined in a roadmap-linked blueprint. Acting on that reading would have
  deleted a specified pre-cutover safety gate.
- The merge-driver remedy was carried forward for three documents without checking whether the
  repository had already evaluated it. It had — under **Rejected alternatives**, for this exact
  workflow.

**A grep that returns nothing is evidence about the grep, not about the repository.** Before a
determination states that something does not exist, it should search the documentation tree and the
design notes, not just source and tests — and before it recommends a mechanism, it should check
whether that mechanism has already been considered and rejected here. Four further defects were
miscounts inherited verbatim from 2026-09-27 and republished under a headline claiming
re-verification; a carried-forward claim is a claim, and re-verification has to include it.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| P0 | Provide **both** `MDC_SIGNING_CERT_PFX_BASE64` **and `MDC_SIGNING_CERT_PASSWORD`** in the protected `desktop-release-signing` environment | Hard prerequisite for **freezing and running the signed RC**, not for the rest of Tier 0; not engineering work. The 2026-09-27 document named only the PFX: the password is passed to Authenticode signing (`desktop-installer-packaging.yml:383,389`) and into installed lifecycle certification (`:483,492`), where `certify-desktop-install-lifecycle.ps1:177` imports the PFX with it, and the tracker names both (`implementation-todo-list.md:120-122`). Provisioning only the PFX leaves the tag workflow unable to sign or import a password-protected certificate. Everything else below can proceed while they are outstanding |
| P0 | Decide the ~1 GB consumer installer question | It ships inside the RC; decide before the tag |
| P0 | **Fix the prerelease → MSIX version mapping, or choose a tag scheme with distinct increasing identities** | **Blocks the tag.** `desktop-installer-packaging.yml:196-199` matches `^v(\d+)\.(\d+)\.(\d+)` and stamps `$1.$2.$3.0`, so `v0.1.0-rc.1` and a later `v0.1.0` both produce MSIX `0.1.0.0`. An operator who installs the signed RC then cannot be updated to the final package, and `certify-desktop-install-lifecycle.ps1:206-209` throws "Update did not advance package version" on exactly that transition. Tagging `v0.1.0-rc.1` as written burns the `0.1.0.0` identity on a throwaway candidate |
| P0 | **Drain the remaining three members of the six-PR baseline dependency set before the freeze — #2944, #2932, #2307** (#2967, #2968 and #2965 have merged; #2878 remains outside that set); separately resolve, validate and review #2981 and #2903 for inclusion, or record an explicit pre-freeze deferral | Recheck current heads and gates first. The RC freezes the dependency set; bumping after the tag invalidates same-commit evidence. #2903's source-hash conflict requires the reviewed-baseline procedure above, not automatic hash acceptance |
| P0 | Backfill tenancy; make `FailClosed` the supported default with a real config key; add the rejection regressions — **before the freeze** | Re-verified today: still env-var-only, still defaults open. The one `W9-GOV-008` remainder, and it changes the candidate, so it cannot follow the tag |
| P0 | Add `StatementReconciliationPostgresRoundTripTests` — **before the freeze** | Corrected scope: statement rows **do** have PostgreSQL coverage (`StatementReconciliationReportAuthorityStoreTests.cs`, `PostgresFundAccountStoreTests.cs`). The gap is the **end-to-end round trip** — import → journal-sourced ledger → deterministic match → casework — over the real stores. Candidate-changing, so it precedes the tag |
| P0 | **Then** freeze a commit, cut the RC tag, **and separately dispatch `Publish Smoke` at the tagged SHA** | Still no `v*` tag in the repository. **This is the last row that can change the candidate:** the tracker requires every P0 row complete on **one** release commit (`implementation-todo-list.md:125-127`), so a tag cut before the rows above would certify the pre-fix tenancy posture, omit the new test, and force a second tag and a full second evidence run. The tag run also does not mint everything: `publish-smoke.yml:3` is `workflow_dispatch`-only, so `PRD-013`'s `web-workstation`/`win-x64` evidence needs its own dispatch at the frozen commit |
| P0 | **After the tag run completes**, schedule the operator replay/reconciliation review of the recovery-drill artifact | `PRD-015` is not closed by the tag run alone. `implementation-todo-list.md:197` requires a "Successful dated `production-recovery-drill-*` artifact on the release commit **and operator review of replay/reconciliation**". The artifact does not exist until the tag run uploads it — `production-certification.yml:361-366` publishes `production-recovery-drill-${{ github.run_id }}` — so this review **must follow** the row above, not precede it. It is a human gate like the ADR sign-off and the required-check activation; omitting it lets the sequence finish with a production-blocking P0 row still open |
| P1 | **Completed after baseline: #2998 merged in `87dc5bf15`** | The baseline recommendation is retained as history; no further merge action remains |
| P1 | Take or decline `W9-CORPACT-011` (6 days) | Zero engineering; clears the last W9 acceptance lane |
| P1 | Resolve the two shared operations-continuity fixtures once on `main`; drain the **four** remaining generated-only branches (#2928, #2920, #2953, #2896) | One resolution converts #2929 and #2930; the four need no product decision. The baseline table listed five — **#2999 merged in `80219e32`** and has left the queue |
| P1 | **Adopt the documented resolve-and-regenerate command** for generated-tree conflicts — **not** a `.gitattributes` merge driver | #3004 got harder to merge in one day of documentation-only commits, so the decay is measured, not projected. But `docs-regeneration-automation-design.md:263-267` lists a custom driver under **Rejected alternatives** — it runs only where configured locally and never during GitHub's server-side merge, i.e. never for the merges being measured. `generated-merge-recovery.md:71-75` says the same and documents the command instead. Two further reasons the old path list was wrong: it covered `src/*/README.md`, which `documentation-ownership.md:21` classifies as source-module truth rather than generated output, so an ours-style driver there could silently discard real README changes |
| P2 | Read #3010 and #2931 together before merging either | #3010 is a live successor; merging both lands the same change twice |
| P2 | Schedule `W10-SEAM-001` live certification, then `W10-MARK-001` | Both one operator session from closing; `SEAM` unblocks the desktop lane |
| P2 | Continue `W10-LOT-002`; **implement and test "shadow-operation acceptance" — do not drop it** | Only `critical` row. It is **defined** at `security-lot-convergence-blueprint.md:252-253` (dual-project legacy and canonical, block cutover on quantity/basis/relief/amortization/corporate-action differences); the earlier "no definition anywhere" reading came from grepping `src/` and `tests/` but never `docs/`. Unimplemented, not unscoped — and it is the gate protecting the lot-model cutover |
| P3 | Correct the `W9-GOV-008` row (self-contradicting remainder, now 30 days stale); close #2983 | Both still misdescribe the program; #2983 was asked to be closed yesterday and is still open |
| P3 | Hold #2878 and #2587 (major bumps) until after the RC; re-cut #2587 | Majors do not belong in a pre-freeze drain; #2587 spreads 607 conflicts over 1,421 files |
