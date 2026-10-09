# What To Work On Next — Meridian (2026-10-09)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-09
**Baseline:** `origin/main` at `83d6440e50c6bdb7fbc334c8aaec791c151c573f` (2026-10-08)
**Supersedes:** [2026-10-05](next-work-determination-2026-10-05.md) (the newest determination on `main`)
and the unmerged 2026-10-07 draft in
[#3112](https://github.com/rodoHasArrived/Meridian-main/pull/3112)
**Method:** re-verified every carried-forward claim against **current source on `origin/main`**; read
the live certification lane with the `event: push` filter and cross-checked the newest run's
`created_at` against the head commit date; read the **job log**, not step conclusions; reproduced the
failing gate locally with `npm audit --json` and read `fixAvailable` per advisory; measured each
candidate branch with `git merge-tree --write-tree --name-only origin/main origin/<branch>` and
classified every conflicted path against the resolver's own coverage list; parsed the roadmap registry
with `yaml.safe_load` (it parses) and computed row ages from it.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; `docs/product/implementation-todo-list.md`
> remains authoritative for the P0 release gate. Recheck conflict counts and CI results against the
> live head before acting.

## Why this document exists when the bar was not met

The bar set on 2026-10-04 and restated on 2026-10-05 and 2026-10-07 is **"a green certification run,
or the RC."** Neither has happened. This is not another re-ranking: **the ranking is unchanged in
substance and the route to it changed twice in two days.** Three things below are new and change what
someone should do this morning; one retires an item that three determinations have carried.

## The blocker is unchanged, and now seven days old

Verified today, not restated:

- **No successful push run in the certification lane since the predecessor.** Runs **#143–#202**
  (2026-10-05 → 2026-10-08) are **40 failures and 20 concurrency cancellations, zero successes**. The
  last green on record remains **#130** at `2dbd8723`, **2026-10-02**. Newest run **#202**
  ([37835107017](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37835107017)) is
  `failure` at today's head `83d6440e`.
- **Back to exactly one failing job.** In #202 the documentation-evidence, PostgreSQL integration and
  encrypted backup/restore jobs are **green** — the two extra failures #3106 introduced on 2026-10-07
  (generated-doc drift and `UiApiRoutes_CategorizedCorrectly`) are closed on `main` by other means.
  Only `NuGet and npm dependency evidence` fails, and its log names the cause verbatim:

  ```
  npm-audit gate: UNACCEPTED GHSA-vfj7-8cjw-p6xm (braces, high): no acceptance entry
  npm-audit gate: FAIL — see unaccepted or stale entries above.
  ```

  The NuGet half passes: every project reports "no vulnerable packages given the current sources".
- **The acceptance register is still `"accepted": []`.** The governance decision four determinations
  have ranked as the P0 unblock has not been made in seven days.
- **The Tailwind patch bump landed and did not help.** The dependabot build-tooling group (#2307,
  merged 2026-10-08) moved `tailwindcss` to `3.4.19`;
  `braces` is still `3.0.3`, `"dev": true`, and `overrides` still carries only `postcss` and `nanoid`.

### New measurement: upstream itself says the only fix is leaving Tailwind 3

Running the audit locally and reading `fixAvailable` per entry — which no predecessor did — collapses
the picture. The **7** reported vulnerabilities are **2 root advisories**, and every high entry names
the same single remedy:

| Entry | Severity | Advisory | `fixAvailable` |
| --- | --- | --- | --- |
| `braces` | high | `GHSA-vfj7-8cjw-p6xm` (`<=3.0.3`) | `tailwindcss@4.3.3`, semver-major |
| `chokidar`, `micromatch`, `fast-glob`, `tailwindcss` | high | none of their own — transitive via `braces` | same |
| `postcss-selector-parser` | moderate | `GHSA-rj75-hqrm-r3gf` (`<7.1.6`) | same |
| `postcss-nested` | moderate | none of its own — via `postcss-selector-parser` | same |

The gate runs `--fail-level high`, so **one** entry blocks the lane and **one** acceptance entry would
clear it. But `braces` has no non-vulnerable version at any severity, so an acceptance is a recorded
risk with a review date, not a fix. The chain terminates at Tailwind 3 itself.

## Finding 1 — closing #3109 did not lose the Tailwind 4 migration; #3102 carries it

The 2026-10-07 determination made landing **#3109** the P0 because it deletes the vulnerable subtree
instead of accepting it. **#3109 was closed unmerged at 19:14 UTC that day "in favor of #3102"**, and
its own body records that its five review repairs were pushed to `a4cfc443d` *after* the close. That
reads like the P0 fix was discarded with the duplicate lot work.

**It was not, and the close was not careless.** `codex/lot-successor-posting` (#3102) had already
taken the migration across two hours earlier, in commit **`fb426a729`** — *"Consolidate Tailwind 4
workstation remediation from PR 3109"*, 17:05 UTC, a commit that exists on no other branch. Measured
on the branch today at head `5534c814e`:

| Branch | `tailwindcss` | `@tailwindcss/postcss` | `vite` | `braces`/`micromatch` in lockfile |
| --- | --- | --- | --- | --- |
| `origin/main` | 3.4.19 | — | 7.x | `braces@3.0.3` (dev) |
| **#3102** `codex/lot-successor-posting` | **4.3.3** | **4.3.3** | **8.0.16** | **NONE** |
| #3109 `codex/workstation-and-lot-posting` (closed) | 4.3.3 | 4.3.3 | 8.0.16 | NONE |
| #3096 `codex/tailwind-v4-postcss-repair` | 4.3.3 | 4.3.3 | 8.0.16 | NONE |

**The P0 fix is live in the one open non-draft PR in the queue.** The 2026-10-07 framing — #3102 as
the lot-only alternative, #3109 as the migration — is stale, and acting on it today would mean
reviving a closed branch to re-land something #3102 already has. `git merge-base --is-ancestor`
confirms `a4cfc443d` is **not** an ancestor of #3102, so #3109's five review repairs are a separate
question from the migration; carry them across on their merits, not to recover Tailwind 4.

## Finding 2 — #3102's 139 conflicts are three real paths, and 238 are already automated

#3102 measured 3 generated-only conflicts on 2026-10-07. Today it measures **139**, because every
merge to `main` since then rebuilt the tracked workstation bundle. Classifying all **241** distinct
conflicted paths against `build/scripts/resolve-generated-merge-conflicts.py`'s own coverage —
`GENERATED_FILES` plus the whole `src/Meridian.Ui/wwwroot/workstation/` root:

| Class | Paths | Disposition |
| --- | --- | --- |
| Covered by the existing resolver | **238** | `--apply` seeds them from incoming `main`, then regenerate |
| `docs/source/generated/source-hash-manifest.json` | 1 | Regenerable; excluded from the resolver on purpose because it is reviewed |
| `src/Meridian.Ui/dashboard/package.json` | 1 | **Real decision:** take Tailwind 4.3.3 over `main`'s 3.4.19 |
| `src/Meridian.Ui/dashboard/package-lock.json` | 1 | Same decision; regenerate with `npm install`, do not hand-merge |

**So the P0 is mechanical work plus one dependency decision already evidenced — not a governance
call.** It is also why the standing P3 merge-driver item should stop being read as hygiene: the
built-asset tree is now generating conflicts **on the P0's own path**, and it will do so again on
every merge to `main` while #3102 waits.

## Finding 3 — #3102's only red check is `pipeline-budget`, and #3123 fixes exactly that, cleanly

At `5534c814e`, **25 of 26 check runs are green**, including `quality-gate`, `integration-gate`,
`verify-dotnet`, `verify-browser`, `verify-desktop`, `verify-docs`, `verify-workflows`,
`schema-control`, the service-backed PostgreSQL job, CodeQL and Secret Scan. The single failure is
**`pipeline-budget`**: the benchmark runs clean and the budget validator exits 1 on WAL checksum
latency — a path #3102 does not touch.

**#3123 (`codex/wal-checksum-performance`) is that fix, and it is ready now.** Its own summary states
it fixes the repeated WAL checksum failures that blocked #3109; it is `mergeable_state: clean`, based
on today's head `83d6440e`, and reports every portable stage under cap on hosted runs at its final
head `602b191f`:

| Stage | Mean ns | Cap ns |
| --- | ---: | ---: |
| WAL small | 154.69 | 400 |
| WAL medium (900 B) | 170.50 | 600 |
| WAL large (4,096 B) | 357.71 | 1,200 |
| Dedup hit / miss | 42.39 / 327.00 | 200 / 800 |

It is still a **draft** at 304 files and +86k lines (mostly retained measurement evidence), and its
own checklist leaves `scripts/ci.sh` on the integrated revision unticked. Read it as the P0
dependency it is.

**Merge order, therefore:** #3123 → re-merge `main` into #3102 → `resolve-generated-merge-conflicts.py
--apply` → take Tailwind 4 in the two dependency manifests and regenerate the lockfile and the
source-hash manifest → **re-run the dependency gate on the merge result**, because #3102's green npm
evidence predates today's head → land. Then freeze a green head and tag.

## The coupling risk is unchanged — it moved PRs rather than being removed

#3102 is **336 files**: 130 built workstation assets, 85 dashboard source, 50 other `src/` (.NET), 38
docs, 27 tests. It welds the certification unblock to `W10-LOT-002` corporate-action lot posting —
the slate's only `critical` row. The 2026-10-07 determination recommended splitting the
dashboard/dependency slice out and landing it first. That did not happen; the PR changed instead. The
recommendation stands, and the slice is small: 6 dashboard config/manifest files
(`package.json`, `package-lock.json`, `postcss.config.cjs`, `tailwind.config.ts`, `index.html`,
`README.md`) plus the Tailwind 4 class changes and the rebuilt bundle. **Either split it now, or
review #3102 as the P0 it is — knowing that one review round on lot posting holds the lane red.**

## Correction — the PostgreSQL statement/reconciliation round trip exists

Carried as the highest-value pure engineering item since 2026-09-27 and re-measured "absent" as
recently as 2026-10-07. **It landed on 2026-10-08** in #3118:
`tests/Meridian.Tests/Integration/StatementLedgerReconciliationPostgresTests.cs` and its `.Harness.cs`
partial (535 lines, three tests) cover **BAI2 and camt.053** import → journal-sourced ledger match →
casework projection, asserting survival across fresh service instances and retries, with EUR book
currency and distinct booking/settlement dates on the camt.053 case.

**The standing check was keyed on the wrong attribute.** It greps
`grep -rl "LedgerDatabaseFact" tests/ | grep -iE "statement|reconcil|ingest"`; this file uses
`[ReportingDatabaseFact]`, so the check reports nothing on a file that is exactly what it was looking
for. This is the same failure mode as the `repair-links.py` false positives corrected on 2026-10-05: a
validator wrong in the direction of "reports a real thing as missing" invites building it twice.
**Retire the item.** On this evidence `W9-INGEST-009` is a candidate for `ready_for_acceptance` — an
owner call, not this document's.

## Row hygiene, computed from the registry today

42 rows parse: 17 `done`, 9 `planned`, 9 `in_progress`, 6 `accepted`, 1 `ready_for_acceptance`.

| Row | Status | Priority | Days since review |
| --- | --- | --- | --- |
| `W5X-OEG-001` | `planned` | high | **116** |
| `W8-WPF-PARITY-001` | `in_progress` | high | **95** |
| `W8-UX-CONSOL-001` | `in_progress` | medium | **82** |
| `W10-RECON-001/002/003/004`, `W10-TAX-001`, `W10-PERF-001`, `W10-CONSOL-001` | `planned` | high/medium | **70** |
| `W9-CORPACT-011` | `ready_for_acceptance` | high | **17** |
| `W10-MARK-001`, `W10-SEAM-001` | `in_progress` | high | 16 |
| `W9-INGEST-009` | `in_progress` | high | 14 |
| `W9-GOV-008` | `in_progress` | high | 11 |
| `W10-JRNL-001`, `W10-PROV-001`, `W10-LOT-002` | `in_progress` | high/critical | 7–8 |

Two notes the predecessors' tables did not carry:

- **`W5X-OEG-001` is the oldest unreviewed row in the program at 116 days** — `high`, `planned`, three
  stage gates, four workspaces — and no determination has mentioned it. Either it is a real candidate
  and needs a review date, or it is deferred and should say so.
- **`W10-CONSOL-001` and `W10-TAX-001` have shipped first slices and still read `last_reviewed:
  2026-07-31`.** #3107 (two-entity consolidation) and #3104 (retained disposal tax results in journal
  detail) both merged 2026-10-07, and both rows' `current_summary` describes the landed slice while
  explaining why full status stays `planned`. The summaries were updated; the review dates were not.
  That is a hygiene gap, not a status error.

`"ShadowOperation"` still returns **nothing** anywhere in `src/` or `tests/`, so that `W10-LOT-002`
criterion remains unburndownable as written: **define it or drop it.**

## Queue: 20 open PRs, down from 27

The 2026-10-05 and 2026-10-07 cleanup recommendations were largely executed: #3048 merged (the
registry now parses and the silent fallback parser is gone from `common.py`), and #3031, #3012, #3030,
#3053, #2826, #2920, #2930, #2932, #2944, #2981, #3036, #3049, #3050, #3057, #2903, #2307 and #3041
are all off the queue. What is left that matters: **#3123** and **#3102** (above), **#3094**
(`Restrict legacy OAuth token permissions during scoped startup`, security-labelled, untouched since
2026-10-06), **#2789** (still self-labelled `[DRAFT — RE-CUT REQUIRED]`), **#2587** (`react-router`
major) and **#3058** (`lucide-react` 0 → 1 major).

## What not to work on

- **Registering the `braces` acceptance as the first move.** It is the fallback. It records a risk for
  a package with no fixed version, then retires it days later when #3102 lands.
- **Reviving #3109's branch or landing #3096 for the migration.** #3102 already carries Tailwind 4;
  #3096 measures 106 conflicts against it.
- **Re-adding the statement/reconciliation PostgreSQL round trip.** It exists — see the correction.
- **Re-running the certification lane hoping for green.** Deterministic against the live advisory
  database until #3102 lands or the register changes.
- **Hand-merging #3102's 139 conflicts.** 238 of 241 paths are the resolver's job.
- **Majors before the freeze.** #3058 and #2587 wait — but note #3102 already carries `vite` 8, so
  that major is inside the P0 and needs review as such.
- **Resolving #2789's hand-written conflicts.** Re-cut or close.
- **New product surface.** The `program-state.yml` `scope_gate` is explicit and nine rows are
  `in_progress` across three waves.
- **Writing a tenth determination before the bar is met.** Two are now open as unmerged drafts
  (#3112 and this one). Close the superseded one rather than accumulating.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0** | **Merge #3123**, then re-merge `main` into **#3102**, resolve, regenerate, re-run the dependency gate, land | #3102 carries the Tailwind 4 migration that deletes `braces`/`micromatch` — the lane's single blocker. Its only red check is `pipeline-budget`, which #3123 fixes and which is `clean` on today's head. 238 of its 241 conflicted paths are already automated |
| **P0** | Split #3102's dashboard/dependency slice, or accept the coupling knowingly | 336 files weld the CI unblock to the only `critical` row; one lot-posting review round holds the lane red |
| **P0** | Re-run the npm/NuGet dependency gate **on the merge result** | #3102's `passed: true` evidence predates `83d6440e` and is hashed against an older baseline |
| **P0** | Then: signing secret, the ~1 GB installer decision, freeze a green head, tag `v0.1.0-rc.1` | Unchanged and not engineering work. Five P0 tracker rows wait on same-commit evidence no run has ever minted; no `v*` tag has ever existed |
| **P1** | Take or decline `W9-CORPACT-011` (**17 days** at `ready_for_acceptance`) | Zero engineering; precondition met on PostgreSQL. **Declining is a result too** |
| **P1** | Re-review `W9-INGEST-009` against #3118's round trip | The evidence its record said was missing now exists on `main` |
| **P1** | Fix the `LedgerDatabaseFact` grep used to measure PostgreSQL coverage | It reported a real, landed test as absent for a day; the same class of defect as the link checker |
| **P1** | Refresh `W8-WPF-PARITY-001` (**95**) and `W8-UX-CONSOL-001` (**82**); date `W5X-OEG-001` (**116**) or defer it | Both W8 rows are named productization targets; `W5X-OEG-001` is the program's oldest unreviewed row and has never been ranked |
| **P2** | Bump `last_reviewed` on `W10-CONSOL-001` and `W10-TAX-001` | First slices merged 2026-10-07; the summaries say so and the dates do not |
| **P2** | Triage #3094 (OAuth token permissions) | Security-labelled and untouched for three days |
| **P2** | Close #3112 as superseded; close #3096 | Two open determinations and a superseded migration PR |
| **P2** | Scope or drop `"shadow-operation acceptance"` | Zero source presence; cannot be burned down as written |
| **P3** | Implement the `.gitattributes` merge driver / documented regenerate-on-merge | No longer hygiene: the built-asset tree is generating conflicts on the P0's path, and will again on every merge to `main` |
| **P3** | Gate link integrity in CI | The checker no longer emits false positives, so a gate would mean something |
| **P3** | Re-cut or close #2789; hold #2587 and #3058 until after the freeze | Decaying draft; majors wait |

## The bar for the next determination

**Unchanged: a green certification run, or the RC.** It is no longer blocked on a decision nobody has
made, and it is no longer blocked on a PR nobody has reviewed in isolation — it is two merges in a
known order, one of which is already `clean` on today's head. If a tenth determination opens on a red
lane with #3123 and #3102 both still unmerged, the thing to examine is the merge path, not the
backlog.

## On archiving the predecessors

Convention (`README.md` in this directory) says to move a superseded determination into
`archive/docs/plans/`. **2026-09-27 is again left in place**: `docs/roadmap/data/program-state.yml`
cites it by path as the recorded operator-session plan and the readiness tracker deep-links its
section anchors. Archiving 2026-09-27, 2026-10-02, 2026-10-04 and 2026-10-05 with those references
updated in the same pass remains a separate governance-reviewed step.
