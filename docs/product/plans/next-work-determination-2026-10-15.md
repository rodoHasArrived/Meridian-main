# What To Work On Next — Meridian (2026-10-15)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-15
**Baseline:** `origin/main` at `2083befe36b39b17ba72705c24a1ab499dbb88cf` (2026-10-15 07:55 UTC) for
the CI and source readings; the queue was re-measured against
`5204b8dfb61fab89685e6e64f354ee262ad843a9` after #3157 merged mid-session — see
[Queue](#queue-24-open-prs-and-one-merge-re-dirtied-most-of-it)
**Supersedes:** [2026-10-05](next-work-determination-2026-10-05.md) (the newest determination on
`main`) and the unmerged drafts in
[#3112](https://github.com/rodoHasArrived/Meridian-main/pull/3112) (2026-10-07),
[#3125](https://github.com/rodoHasArrived/Meridian-main/pull/3125) (2026-10-09) and
[#3132](https://github.com/rodoHasArrived/Meridian-main/pull/3132) (2026-10-10)
**Method:** re-ran the failing gate's own reproduction (`npm audit --package-lock-only --json` in
`src/Meridian.Ui/dashboard`) rather than reading its history; filtered every `push` workflow run at
the head commit by `head_sha` and read conclusions per workflow; read the retained eight-workflow
receipt at the last full-coverage commit; re-measured all 25 open pull requests with
`git merge-tree --write-tree --name-only origin/main origin/<branch>`, separating
`Merge conflict in <path>` lines from `rename/rename`, `rename/delete` and `modify/delete` lines and
classifying each by path; diffed the two newest pull requests against each other file by file; read
the credential store, the ADR status lines, the installer workflow's signing requirement and
`.gitattributes` in current source; parsed the roadmap registry with `yaml.safe_load` and computed
row ages from it; ran `repair-links.py --summary` and read both reported targets.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; `docs/product/implementation-todo-list.md`
> remains authoritative for the P0 release gate. Recheck conflict counts and CI results against the
> live head before acting.

## Addendum — 2026-10-17: what this document got to, and what went stale

This determination sat unmerged for two days while `main` moved heavily. Re-verified against branch
head `94e843cb` and `main` `34a02682`, **the P0 ranking below is unchanged** and three of its
recommendations have been executed. The ranking was not re-done; only the facts behind it were
re-read.

**Executed.** The P1 "land #3162 then #3164" is **done** — both merged through
[#3186](https://github.com/rodoHasArrived/Meridian-main/pull/3186) (`integrate-ready-prs-20261016`),
along with #3131, which this document listed as having lost its clean state.

**Still open and still the recommendation.** **#3168 and #3169 are both unmerged**, so the
"triage before merging either" ask holds — and matters more now, because `main` has absorbed two
days of reconciliation, provider and Security Master work underneath both of them. **Re-measure
both before choosing; the 47-differing-file count below is a 10-15 figure.**

**Re-verified true, two days on.** Every P0 item in the summary still reads exactly as recorded:
`ADR-019` and `ADR-020` are still `Proposed`, the repository still has exactly one tag
(`eval-v0.1.0-eval.1`) and no `v*`, and `FileProviderCredentialStore` still writes the non-Windows
AES-GCM key beside the ciphertext it opens, with its own comment still saying so. **Nothing on the
release-freeze path moved in two days**, which is itself the finding: the blockers are signatures,
a secret and a product decision, and elapsed time does not clear any of them.

**Stale, and to be re-read rather than trusted:**

- **The registry is now 48 rows, not 42.** Six `planned` `W11-*` rows (`PASSPORT`, `COVER`, `STAKE`,
  `SHADOW`, `PACK`, `EXEC`) arrived with the design-charter v1.3 work
  ([#3185](https://github.com/rodoHasArrived/Meridian-main/pull/3185)). Current split: 17 `done`,
  **15 `planned`**, 9 `in_progress`, 6 `accepted`, 1 `ready_for_acceptance`. The row-hygiene table
  below predates them, and a new `W11` direction slate is a scope question this document did not
  rank.
- **Every row age below is two days short**, and `W9-CORPACT-011` has now been one signature away
  for **25 days**.
- **Every queue and conflict count below is a 10-15 measurement.** #3162, #3164 and #3131 have since
  landed, so the "24 open PRs" table and the 564-line bundle figure are both historical.

## The bar is met: the certification lane is green, and it was fixed rather than accepted

Four determinations (10-04, 10-05, 10-07, 10-09, 10-10) ranked one P0 — the `braces` advisory
`GHSA-vfj7-8cjw-p6xm` reddening `Production Certification` — and each set the same bar for its
successor: *a green certification run, or the RC*. **That bar is now met**, which is why this
document re-ranks the program instead of re-confirming a blocker.

Verified today, at the head commit:

- **`Production Certification` #239**
  ([38702038000](https://github.com/rodoHasArrived/Meridian-main/actions/runs/38702038000)) —
  **success**. So are **#238, #237, #234, #233, #232, #231** and **#230**; #235 and #236 were
  cancelled by concurrency and not scored. That is **eight consecutive scored successes**, against
  zero in the 10-10 reading.
- **`Meridian CI` #5352** at the same commit — **success**. `CI` #7351, `Documentation Automation`
  #5995, `Roadmap Source Docs` #7655 and `CodeQL` #8256 are all green on this commit as well.
- **The gate was fixed, not waived.** `build/config/security/npm-audit-accepted-advisories.json`
  still reads `"accepted": []` — no risk was accepted — and `npm audit --package-lock-only --json`
  on the current lockfile reports **`{"info": 0, "low": 0, "moderate": 0, "high": 0, "critical": 0,
  "total": 0}`**. `package.json` carries `tailwindcss` **4.3.3** and `@tailwindcss/postcss`
  **4.3.3**; the lockfile contains **zero** `braces` entries. The Tailwind 4 migration that five
  determinations deferred as "the only true remediation" is on `main`, and it deleted the vulnerable
  subtree instead of recording it.
- **`KV-2026-003` was closed correctly.** Its heading now reads *Tailwind 4 remediation candidate*
  with **"Status: implementation remediated"**, and its lapsed 2026-10-07 decision point and
  2026-10-19 hard expiry are moot — the acceptance route was never taken. The fallback that was
  decaying on its own clock expired unused, which is the right outcome.

**The predecessors' standing advice was followed** — *do not register the acceptance as the first
move; it buys days and records a risk rather than removing one.* The migration landed as
`db2329d3b` (*"migrate workstation to Tailwind 4 and clear the certification blocker"*, 2026-10-11)
via **#3133**, merged 2026-10-12, and
[certification-unblock-verification-2026-10-12.md](certification-unblock-verification-2026-10-12.md)
recorded the result. Both that fix and this ranking came out of the same lane, which is the one
useful thing to take from the four determinations that preceded it: the analysis was right, and it
was worth less than the merge.

### A full-coverage green commit already exists, and the head is not it

The head commit is docs-only, so path filters kept **`Windows Desktop Build`** and **`Golden Path
Validation`** from running on it. The freeze candidate is therefore not today's head but
**`e0ecd952d24b1e8d09a8de282ed90c98cffe1bb3`** (2026-10-14), where **all eight** workflows ran green
and the [retained receipt](../../testing/evidence/program-state-20261014/main-validation.json) records
each run id: Golden Path Validation `38631108961`, CodeQL `38631108936`, Windows Desktop Build
`38631109067`, CI `38631108934`, Roadmap Source Docs `38631108954`, Documentation Automation
`38631109100`, Meridian CI `38631109092`, Production Certification `38631109170`.

**Anyone freezing a head must check which workflows actually ran on it, not just that the ones which
ran were green.** A docs-only commit cannot carry desktop or golden-path release evidence, and the
release gate requires every P0 row complete *on the same commit*.

## So the P0 moved, and most of what is left is not engineering

With the lane green, the binding constraint on `PRD-000` and `PRD-013`–`PRD-017` is signatures and
secrets, not code. Each item below was read in current source today:

| Blocker | State in current source | Who can clear it |
| --- | --- | --- |
| **`ADR-019` is still `Proposed`** | Its own status line reads *"Proposed (awaiting core-team sign-off; **signing this ADR is the `PRD-000` 'signed support matrix' artifact**)"* | Core team. No engineering work exists for it |
| **`ADR-020` is still `Proposed`** | `docs/adr/020-lifecycle-control-plane.md` | Core team |
| **Release signing secret** | `desktop-installer-packaging.yml:215` and `:370` **throw** without `MDC_SIGNING_CERT_PFX_BASE64` in the protected `desktop-release-signing` environment. Every lifecycle receipt so far was anchored by a throwaway self-signed certificate and is not release evidence (`PRD-014`) | Repository administrator |
| **The ~1 GB consumer download** | `Meridian-Setup.exe` is **1,043,350,783 bytes** — both architectures plus a full PostgreSQL distribution. The evidence chain says explicitly this "is a product question this ledger does not settle" | Product |
| **Recovery-drill receipt** | `PRD-015` needs a dated release-commit `production-recovery-drill-*` artifact evaluated to `objectiveStatus: proven` under RPO 3600s / RTO 7200s, plus operations-owner review | Operations owner |
| **Required-check activation** | `PRD-016` needs the protected workflow activated as a required release check | Repository administrator |
| **No release tag** | The repository has exactly **one** tag, `eval-v0.1.0-eval.1`. No `v*` tag exists | Release engineering, after the above |

**`PRD-000` is step 1 of the tracker's own execution order.** Signing ADR-019 is free, available
today, and gates the step that gates everything after it. It has been the cheapest unblocked item in
the program for five determinations while attention went to a dependency advisory.

## The one P0 that still needs code: `PRD-002`

`PRD-002` is the tracker's single `implementation-in-progress` P0, and the defect is still on `main`.
Read today in `src/Meridian.DataIntegration/Credentials/FileProviderCredentialStore.cs`:

- DPAPI protection is Windows-only (`:1170`, `:1185`–`:1186`).
- On every other platform the AES-GCM key is written **into the same directory as the ciphertext it
  opens** (`_keyPath = Path.Combine(_directoryPath, KeyFileName)`, `:63`; written at `:1252`–`:1253`).
  The file's own comment at `:1233`–`:1234` states the problem without mitigating it: *"the AES-GCM
  key lives in the same directory as the ciphertext it opens, so on Unix the file mode is the only
  thing separating the two: a reader who can open the vault can open the key."*

**Correction to the tracker: #2931 is not a draft.** It **merged on 2026-09-30** (121 files,
+9,609/−4,269), fifteen days ago, yet the tracker — reviewed 2026-10-14 — cites it as
`draft implementation evidence` at line 38, as a reason the row is reopened at line 121, and as
`Draft [#2931]` at line 241. **The row's disposition is still correct** (verified above), but its
evidence citation is stale, and the residual scope should be re-sourced from #2931's own merged
description, which enumerates it: default/new-connection ownership and runtime cutover, remaining
browser/WPF/setup consumers, concurrent whole-configuration snapshot callers, **non-Windows key
protection**, credential/audit commit ambiguity, evidence-backed legacy ownership migration, and
tenant-aware operational history.

That is a scoped engineering lane with a named list, and it is the only code on the P0 critical path.
[#3094](https://github.com/rodoHasArrived/Meridian-main/pull/3094) (*Restrict legacy OAuth token
permissions during scoped startup*) sits in the same subsystem, is security-labelled, conflicts only
in one generated file, and has been untouched for **nine days**.

## New finding: #3168 and #3169 are two different fixes to the same code, both green

The two newest pull requests were opened **45 minutes apart today by the same author** with
near-identical scope:

| | #3168 `codex/workflow-correctness` | #3169 `codex/workflow-integrity-fixes` |
| --- | --- | --- |
| Files changed | **131** | **127** |
| Merge state vs `2083befe` | **clean** | **clean** |
| Merge state vs `5204b8df` (after #3157) | 5 conflicts, **0 hand-written** | 6 conflicts, 2 hand-written |
| Checks | **24 success, 2 by-design skips** | **24 success, 2 by-design skips** |

They share **68 files**, and **47 of those 68 differ between the two branches** — these are two
separate attempts, not one rebased on the other. The overlap is not peripheral: both rewrite
`StatementMatchingEngine.cs`, `StatementRunMatcher.cs` and `StatementRunComparisonEvidence.cs`; both
touch `ProviderFactory.cs`, `ProviderServiceExtensions.cs`, `ICredentialContext.cs` and
`ProviderModuleContext.cs`; both edit six provider corporate-action adapters, the Alpaca gateway and
trade-updates client, `LedgerFinancialStatementBuilder.cs`, the payment-approvals and cash-ladder
dashboard screens, `validate-monitoring-deployment.py`, the monitoring alert rules, and
**`docs/product/implementation-todo-list.md`** — the P0 release gate itself. On one shared file,
`StatementRunMatcher.cs`, #3168 is +27/−10 and #3169 is +21/−14.

**Each is mergeable only because the other has not landed.** Neither one's own conflicts are a
problem — they are generated and bundle churn. But merging either turns the other into a 47-file
hand-written conflict across reconciliation, provider routing, credential context, the ledger and the
workstation — the most expensive conflict shape in the repository, and the one the queue has learned
to avoid.

**Triage before merging either.** Pick the better of the two, close the other, and re-cut anything
unique to the loser against the merged result. Two green PRs are not twice the progress here; they
are a 47-file conflict with a 50% chance of being discovered after the merge button.

## Queue: 24 open PRs, and one merge re-dirtied most of it

Measured first against `2083befe` and then re-measured against `5204b8df` after **#3157 merged while
this document was being written** — which turned out to be the most instructive measurement of the
day. Content conflicts are separated from rename/delete churn, and every path is classified:

| PRs | Content conflicts | Hand-written | Disposition |
| --- | --- | --- | --- |
| #3111, #3138, #3140, #3141, #3142 | **0** | **0** | **5 clean** |
| #3094 (1), #3116 (2), #2928 (3), #3162 (3), #3164 (3), #3037 (4), #3168 (5) | 1–5 | **0** | mechanical; resolver's job |
| #3016, #3119, #3125, #3132 | 4–6 | **1** each | one README or index line |
| #3112 (5), #3169 (6) | 5–6 | 2 | #3112 is superseded by this document |
| #2896 | 12 | 3 | two READMEs + `quant-lab-screen.tsx` |
| #3131 | 16 | 7 | **was clean before #3157 landed** |
| #3004 | 18 | 9 | seven READMEs, `contracts.json`, roadmap generated |
| #3105 | 19 | 10 | **read the dependency warning below before resolving** |
| #2789 | 44 | **31** | `[DRAFT — RE-CUT REQUIRED]` in its own title since September |
| #2587 | 535 | **456** | **was 340/269 on 10-05.** Its `modify/delete` lines are files `main` has since deleted |

**One merge cost the queue its clean state.** Before #3157 landed, eleven PRs merged clean, #3162 and
#3164 among them. After it: **five**. #3162, #3164 and #3168 each picked up 3–5 conflicts with **zero
hand-written lines** — pure generated and bundle churn — #3169 picked up 6, and **#3131 went from
clean to 16 conflicts with 7 hand-written.** Nobody made a mistake; a single ordinary merge did this,
and it will happen again on the next one.

**So the merge-driver case is not hygiene, it is the queue's dominant cost.** Six PRs — #3119 (158),
#3105 (98), #2896 (95), #2789 (95), #3168 (59) and #3169 (59) — now carry **564 non-content conflict
lines, and every single one of them is inside `src/Meridian.Ui/wwwroot/workstation/`**: rebuilt
bundle hashes diverging, nothing to decide. That is up from 445 across four PRs an hour earlier,
because #3157 rebuilt the bundle too. `.gitattributes` still contains **no `merge=` driver** — only
`text`/`eol` normalization, including four rules for that very bundle.

**#2587 and #2789 are past repair.** #2587's 456 hand-written conflicts include files `main` deleted
weeks ago (`archive/docs/assessments/README.md`, `docs/development/wpf-web-ui-alignment-plan.md`,
`src/Meridian.Application/Monitoring/StatusHttpServer.cs`). Re-cut or close both; resolving them is
strictly more work than redoing the change.

## Warning: resolving #3105 by taking its side would undo the certification fix

[#3105](https://github.com/rodoHasArrived/Meridian-main/pull/3105) conflicts in
`src/Meridian.Ui/dashboard/package.json`, `package-lock.json` **and `postcss.config.cjs`** — the
three files the Tailwind 4 remediation just rewrote. The inverse of the 10-10 correction now applies:

| Key | `main` | #3105 | Take |
| --- | --- | --- | --- |
| `tailwindcss` / `@tailwindcss/postcss` | 4.3.3 | 4.3.3 | either |
| `vite` | **8.3.1** | 8.0.16 | **`main`** |
| `autoprefixer` | **absent** | `10.4.20`, and added as a PostCSS plugin | **`main`** |

`main`'s `postcss.config.cjs` loads `@tailwindcss/postcss` **alone**; #3105's re-adds
`autoprefixer: {}` alongside it. Tailwind 4 subsumes that step, and the remediation dropped the
dependency deliberately. **Taking #3105's dashboard config wholesale would reinstate a dependency
the certification fix removed and downgrade `vite` below a version `main` has already validated.**
Resolve key by key, keep `main`'s `postcss.config.cjs`, regenerate the lockfile with `npm install`,
and re-run the dependency gate on the merge result.

## Closed in this change: one real broken link. Not closed: the checker's scope

`repair-links.py --summary` reports **2,526 links checked, 2 broken** — one more than on 10-10, and
the new one is a genuine stale reference rather than a false positive:

```
docs/engineering/production-certification-evidence-chain.md:280
  -> ../security/known-vulnerabilities.md#pending-decision-braces-stack-exhaustion-2026-10-05
     (Anchor not found in target file)
```

The Tailwind remediation renamed that heading when it restructured `KV-2026-003`, and the evidence
chain's deep link into the October 5 investigation was left pointing at the old slug. **Repaired in
this change** to the heading that now owns that content,
`#historical-october-5-investigation-and-unaccepted-proposal`. The prose is unchanged; only the
anchor moves, and it is a link into *retained historical investigation*, which is exactly what the
sentence claims it is. After this change the checker reports **2,530 links checked, 1 broken**.

**The second one still must not be repaired.** The 10-10 finding stands verbatim:
`docs/testing/evidence/wal-checksum-2026-10-07/components/native/doc_benchmarks.md:3 -> ../readme.md`
is inside a copied upstream artifact retained as WAL-checksum evidence. Rewriting a link inside
third-party evidence corrupts the evidence. `repair-links.py:43,55` already has `EXCLUDE_DIRS` and
`EXCLUDE_FILES`; retained vendored evidence under `docs/testing/evidence/**` belongs in one of them,
and **that scoping is still the prerequisite for gating link integrity in CI** — gating today would
red the lane on a file nobody is allowed to edit.

## Row hygiene, computed from the registry today

The registry parses (`yaml.safe_load` succeeds, 42 items; no fallback parser in
`build/scripts/docs/common.py`). Statuses: **17 `done`, 9 `planned`, 9 `in_progress`, 6 `accepted`,
1 `ready_for_acceptance`.**

| Row | Status | Priority | Days since review |
| --- | --- | --- | ---: |
| `W5X-OEG-001` *Operational evidence graph product surface* | `planned` | high | **122** |
| `W8-WPF-PARITY-001` | `in_progress` | high | **101** |
| `W8-UX-CONSOL-001` | `in_progress` | medium | **88** |
| `W10-RECON-001/002/003/004`, `W10-TAX-001`, `W10-PERF-001`, `W10-CONSOL-001` | `planned` | high/medium | **76** |
| `W9-CORPACT-011` | `ready_for_acceptance` | high | **23** |
| `W10-MARK-001`, `W10-SEAM-001` | `in_progress` | high | 22 |
| `W9-GOV-008`, `W9-INGEST-009` | `in_progress` | high | 17 / 20 |
| `W10-PROV-001`, `W10-JRNL-001` | `in_progress` | high | 13 / 14 |
| `W10-LOT-002` | `in_progress` | **critical** | **2** |

- **`W9-CORPACT-011` has been one signature away for 23 days.** Its PostgreSQL precondition is met
  and the retained 500-share/USD 120 dividend round trip covers approval, one balanced journal,
  reload, replay and unchanged holdings. **Declining is a result too**; leaving it is not.
- **`W5X-OEG-001` is the oldest unreviewed row in the program at 122 days** — `high`, `planned`, and
  flagged by the last three determinations without being dated or deferred. A `high` row nobody has
  looked at since June is untracked, not prioritized.
- **`W10-LOT-002` is the healthiest row in the program** — the only `critical` row, reviewed two days
  ago, and the first item in the user-requested work queue. See below.

## The user-requested queue has two green, clean PRs waiting right now

`program-state.yml` records the user-requested order as **LOT → MARK → SEAM**, then `W10-RECON-001`
in that queue only. Two `W10-LOT-002` lane PRs are mergeable today:

- **[#3162](https://github.com/rodoHasArrived/Meridian-main/pull/3162)** *align successor candidate
  and atomic posting guards* — **20 checks success, 1 by-design skip**, including `quality-gate`,
  `integration-gate`, both CodeQL analyses, `verify-desktop`, `WPF W4 Acceptance` and the PostgreSQL
  integration job.
- **[#3164](https://github.com/rodoHasArrived/Meridian-main/pull/3164)** *reject standalone tax-lot
  writes in governed stores* — **16 checks success, 1 by-design skip**.

Both were clean against `2083befe` and now carry **3 conflicts each with zero hand-written lines**
after #3157 landed: a base merge and a regenerate, nothing to decide. They are still the two cheapest
substantive merges in the queue.

The lot contracts now carry the successor surface the 10-05 determination found missing:
`OpenLotSuccessors.cs`, `OpenLotCapitalReturn.cs`, `OpenLotAmortizationContinuation.cs` and
`OpenLotValidation.cs` all exist in `src/Meridian.Contracts/Accounting/Lots/`. The tracker's own
residual list for LOT is consumer integration and parity, broader action/correction and
successor-schedule cases, compatibility retirement, and live shadow-operation acceptance — and
**"shadow-operation acceptance" still has no source presence and still cannot be burned down as
written**, which is the 10-10 recommendation to scope or drop that criterion, unactioned.

## Process finding: the ranking landed; the determinations did not

Three determinations (10-07, 10-09, 10-10) exist **only as unmerged draft PRs**. The newest one on
`main` is 10-05, ten days old, and `docs/product/plans/README.md` currently points readers at *"the
unmerged 2026-10-10 determination (#3132)"* as the carrier of the live ranking — an index on `main`
whose pointer resolves to a pull request.

Every one of those determinations was substantively right, including about the thing that actually
fixed the lane. **The analysis was never the scarce resource; landing it was.** If this document is
also left unmerged, the pointer problem compounds rather than repeats: #3112, #3125, #3132 and this
one all edit `docs/product/plans/README.md`, which is precisely why three of them now conflict there.

**Close #3112, #3125 and #3132 as superseded when this lands.** Their substance is carried forward
above: the merge-order guidance is spent (#3102/#3133 landed), the `package.json` key-by-key
correction is restated for #3105, and the vendored-link scoping and merge-driver items survive with
today's measurements attached.

## What not to work on

- **Registering any `braces` acceptance.** The advisory is gone from the dependency graph. The
  register is correctly empty and `npm audit` reports zero findings.
- **Re-running the certification lane hoping for a change.** It is green, eight scored runs deep.
- **Freezing today's head.** It is docs-only; `Windows Desktop Build` and `Golden Path Validation`
  did not run on it. Freeze `e0ecd952` or a later commit with full workflow coverage.
- **Merging #3168 or #3169 before triaging them against each other.** 47 shared files differ.
- **Taking #3105's dashboard config.** It re-adds `autoprefixer` and downgrades `vite`.
- **Resolving #2789's or #2587's conflicts.** 31 and 456 hand-written. Re-cut or close.
- **Hand-resolving the workstation bundle anywhere.** 564 lines across six PRs, zero decisions.
- **"Repairing" the vendored evidence link.** Scope the checker; do not edit retained evidence.
- **Gating link integrity in CI before that scoping.** It would red the lane on that file.
- **Majors before the freeze.** #3141 (`lucide-react` 0 → 1) and #2587 (`react-router`) wait.
- **New product surface.** The `program-state.yml` `scope_gate` is explicit and nine rows are
  `in_progress` across three waves.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0** | **Sign `ADR-019` and `ADR-020`.** Both still read `Proposed`; ADR-019's status line says signing it *is* the `PRD-000` signed-support-matrix artifact | Step 1 of the tracker's execution order, zero engineering, and the cheapest unblocked item in the program. It has been available throughout the five determinations that ranked a dependency advisory instead |
| **P0** | **Provision `MDC_SIGNING_CERT_PFX_BASE64`/`MDC_SIGNING_CERT_PASSWORD` in the protected `desktop-release-signing` environment, then run a signed tag build** | The installer lane is mechanically proven end to end, but every receipt so far used a throwaway self-signed certificate and is not release evidence (`PRD-014`, and `PRD-000`'s update/rollback receipts) |
| **P0** | **Decide the ~1 GB consumer download** (1,043,350,783 bytes: both architectures plus full PostgreSQL) | A product question the evidence chain explicitly refuses to settle, and it shapes what gets signed |
| **P0** | **Freeze a full-coverage green commit and tag the RC.** `e0ecd952` has all eight workflows green with a retained receipt; the repo has one tag, `eval-v0.1.0-eval.1`, and no `v*` | The lane that blocked this for thirteen days is green. Check *which* workflows ran on the candidate, not only that they passed |
| **P0** | **Continue `PRD-002` from #2931's merged residual list** — non-Windows key protection first | The only P0 needing code. The key still sits beside the ciphertext it opens (`FileProviderCredentialStore.cs:1233-1253`), and the file says so itself |
| **P1** | **Triage #3168 vs #3169 before either merges** | 68 shared files, **47 differing**, both fully green. Whichever lands second becomes a 47-file hand-written conflict across reconciliation, providers, credentials and the ledger |
| **P1** | **Land #3162 then #3164** | Both clean and fully green today, both in the `W10-LOT-002` lane, which is first in the recorded user-requested queue and the only `critical` row |
| **P1** | **Take or decline `W9-CORPACT-011`** (**23 days** at `ready_for_acceptance`) | Zero engineering; precondition met on PostgreSQL. Declining is a result |
| **P1** | **Correct the tracker's three `#2931` "draft" citations** (lines 38, 121, 241) | It merged 2026-09-30. The row's disposition is right; its evidence pointer is fifteen days stale on the file that *is* the release gate |
| **P1** | **Date or defer `W5X-OEG-001` (122 days); refresh `W8-WPF-PARITY-001` (101) and `W8-UX-CONSOL-001` (88)** | Both W8 rows are named productization targets; `W5X-OEG-001` is the oldest unreviewed row in the program and has been flagged three times |
| **P2** | **Triage #3094** (OAuth token permissions) | Security-labelled, in `PRD-002`'s own subsystem, one generated-file conflict, untouched nine days |
| **P2** | **Close #3112, #3125, #3132 as superseded** | Their substance is carried forward here; they conflict with each other in `plans/README.md`, which is the symptom of the process finding above |
| **P2** | **Scope or drop `W10-LOT-002`'s "shadow-operation acceptance" criterion** | No source presence; it cannot be burned down as written. Unactioned since 10-10 |
| **P2** | **Re-cut or close #2789 and #2587** | 31 and 456 hand-written conflicts; #2587's include files `main` has deleted |
| **P3** | **Implement the `.gitattributes` merge driver / documented regenerate-on-merge** | **564 conflict lines across six PRs, all inside the workstation bundle, zero decisions in any of them.** #3157 alone added 118 of them and cost six PRs their clean state while this document was being written. Recurring by construction, and now measured |
| **P3** | **Exclude retained vendored evidence from `repair-links.py`, then gate link integrity in CI** | 2,530 links, 1 remaining break, inside third-party evidence. Scope first or the gate reds on an uneditable file |
| **P3** | **Review the dashboard majors after the freeze** | #3141 and #2587 |

## The bar for the next determination

The predecessors' bar was met, so this one sets a different test: **the next determination is due
when the RC is tagged, or when a signature it names is refused.**

What stands between `main` and an RC is now **four signatures, one secret and one product decision** —
listed above with the file and line that each one blocks. None of them is analysis, and no further
ranking produces them. If a determination is written while ADR-019 still reads `Proposed`, the thing
to examine is the decision path, not the backlog.

## On archiving the predecessors

Convention (`README.md` in this directory) says to move a superseded determination into
`archive/docs/plans/`. **2026-09-27 is again left in place**: `docs/roadmap/data/program-state.yml`
cites it by path as the recorded operator-session plan and the readiness tracker deep-links its
section anchors. Archiving 2026-09-27, 2026-10-02, 2026-10-04 and 2026-10-05 with those references
updated in the same pass remains a separate governance-reviewed step.
