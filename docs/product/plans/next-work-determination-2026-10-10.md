# What To Work On Next — Meridian (2026-10-10)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-10
**Baseline:** `origin/main` at `b85095b0d373c4b9701225cbba949942c4f7d93b` (2026-10-09)
**Supersedes:** [2026-10-05](next-work-determination-2026-10-05.md) (the newest determination on `main`)
and the unmerged drafts in
[#3112](https://github.com/rodoHasArrived/Meridian-main/pull/3112) (2026-10-07) and
[#3125](https://github.com/rodoHasArrived/Meridian-main/pull/3125) (2026-10-09)
**Method:** re-verified every carried-forward claim against current source on `origin/main`; read the
certification lane's **job logs and step list**, not step conclusions; reproduced the failing gate
locally with `npm audit --package-lock-only --json` and read `fixAvailable` per advisory; measured
#3102 with `git merge-tree --write-tree --name-only origin/main origin/codex/lot-successor-posting`
and classified every conflict **by kind** against the resolver's own coverage; diffed the two
dashboard manifests key by key; parsed the roadmap registry with `yaml.safe_load` and computed row
ages from it; read `resolve-generated-merge-conflicts.py`, `vite.config.ts` and `scripts/ci.sh` to
check whether a bad bundle merge can ship.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; `docs/product/implementation-todo-list.md`
> remains authoritative for the P0 release gate. Recheck conflict counts and CI results against the
> live head before acting.

## Why this is short

The 2026-10-09 determination is **correct and still current**. Its ranking stands and this document
does not re-rank the program. What changed in the last day is the *route*, in two ways that matter
before anyone touches a keyboard:

1. **The first step of its prescribed merge order is done.** #3123 merged.
2. **Its dependency-resolution guidance is now wrong in one direction.** `main` has overtaken #3102
   on three dashboard devDependencies, so "take #3102's manifest" would silently downgrade them.

One thing it flagged as a future risk has also already landed on `main` and is green, and the
fallback option is now visibly decaying on a clock it set for itself.

## The blocker is unchanged, and now eight days old

Verified today, not restated:

- **Production Certification #204** at today's head `b85095b0`
  ([37984405468](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37984405468)) —
  **failure**. Three of four jobs are green: `same-commit documentation evidence`, `deterministic
  PostgreSQL integration and coverage evidence`, and `encrypted backup and clean restore drill`.
  `NuGet and npm dependency evidence` fails at **step 10 of 11**, `Assert both dependency gates
  passed`, while steps 8 (`Fail on vulnerable NuGet dependencies`) and 9 (`Fail on unaccepted
  high-severity npm advisories`) both report `conclusion: success` because they are
  `continue-on-error`. **Read the job log, not the step conclusions** — the predecessors' caveat is
  reconfirmed verbatim on this run.
- **No green run.** Every certification run read in **#145–#174** and **#190–#204** is `failure` or
  concurrency-`cancelled`; zero successes. The last green remains **#130** at `2dbd8723`,
  **2026-10-02**. (#175–#189 were not read individually and change no recommendation below.)
- **`Meridian CI` is green** at `b85095b0` (run **#5174**). Only the certification lane is red, which
  is why merges keep landing while no commit can be certified.
- **The acceptance register is still `"accepted": []`** in
  `build/config/security/npm-audit-accepted-advisories.json`. Five determinations have ranked this
  decision; it has not been made.

### Reproduced locally on today's lockfile

`npm audit --package-lock-only --json` in `src/Meridian.Ui/dashboard` reports **7 vulnerabilities (5
high, 2 moderate)** that collapse to **2 root advisories**, and every single `fixAvailable` names the
same remedy:

| Entry | Severity | Own advisory | `fixAvailable` |
| --- | --- | --- | --- |
| `braces` | high | `GHSA-vfj7-8cjw-p6xm` | `tailwindcss@4.3.3`, `isSemVerMajor: true` |
| `chokidar`, `micromatch` | high | none — via `braces` | same |
| `fast-glob` | high | none — via `micromatch` | `true` |
| `postcss-selector-parser` | moderate | `GHSA-rj75-hqrm-r3gf` | `tailwindcss@4.3.3`, `isSemVerMajor: true` |
| `postcss-nested` | moderate | none — via `postcss-selector-parser` | `true` |
| `tailwindcss` | high | none — the aggregate of all five | `tailwindcss@4.3.3`, `isSemVerMajor: true` |

The gate runs `--fail-level high`, so **one** advisory reds the lane and **one** acceptance entry
would clear it. But `braces` has no non-vulnerable version at any severity, so an acceptance records
a risk with a review date; it does not fix anything. The chain terminates at Tailwind 3.

## Step 1 of the merge order is complete — #3102 is now the entire P0

The 2026-10-09 determination prescribed **#3123 → re-merge `main` into #3102 → resolve → regenerate →
re-run the dependency gate → land**. Step 1 landed the same day:

- **#3123 merged** as `21198583` (2026-10-09 18:53 UTC), 304 files, +86,474 lines.
- **`Pipeline Budget Benchmark` run #54** at `21198583` is **success**, against **failure** at #45
  (`1f94c380`) and #41 (`7fd56bb8`). The WAL checksum budget breach is fixed on `main`.

That matters because **`pipeline-budget` was #3102's only red check.** At its head `5534c814e`,
**25 of 26 check runs are green** — `quality-gate`, `integration-gate`, `verify-dotnet`,
`verify-browser`, `verify-desktop`, `verify-docs`, `verify-workflows`, `schema-control`,
`scope-gate`, the service-backed PostgreSQL job, both CodeQL analyses, Secret Scan, the WPF/browser
W4 acceptance jobs and the screenshot validation. The single failure is `pipeline-budget`, and
**merging `main` is what brings its fix**.

#3102 still carries the remediation: `tailwindcss` **4.3.3**, `@tailwindcss/postcss` **4.3.3**, and
**zero `braces` entries in its lockfile** (`main` has `braces@3.0.3` at lockfile line 1873). It has
not been touched since **2026-10-07 19:43 UTC** — three days.

## Correction: `main` has overtaken #3102, so "take theirs" is now a downgrade

The predecessor's table recorded `main` at `vite 7.x` and treated the `vite` 8 major as a risk
*inside* the P0 that would need review as such. **`main` took `vite` 8 itself on 2026-10-08** in
`49f92a25` (the #2307 build-tooling group) and has been green on `Meridian CI` ever since. The
major is no longer #3102's to carry — and #3102 is now *behind* `main` on it.

Diffed key by key today:

| Key | `main` `b85095b0` | #3102 `5534c814` | Take |
| --- | --- | --- | --- |
| `tailwindcss` | 3.4.19 | **4.3.3** | **#3102** |
| `@tailwindcss/postcss` | absent | **4.3.3** | **#3102** |
| `vite` | **8.3.1** | 8.0.16 | **`main`** |
| `@vitejs/plugin-react` | **6.1.1** | 6.0.2 | **`main`** |
| `postcss` | **8.5.28** | 8.5.23 | **`main`** |
| `overrides.postcss` | **8.5.28** | 8.5.23 | **`main`** |
| `overrides.nanoid` | `^3.3.18` | `^3.3.18` | either |

**So `package.json` is a six-key merge of two manifests, not a side to pick.** Resolving it by taking
#3102's file — the obvious move, and the one the predecessor's framing invites — would quietly roll
back three devDependency bumps that `main` has already validated. Resolve it key by key, then
regenerate `package-lock.json` with `npm install`; never hand-merge the lockfile.

## Correction: the conflict shape changed kind, and the bundle is still not a human's job

#3102 measured 3 generated-only conflicts on 2026-10-07 and 241 conflicted paths on 2026-10-09.
Against today's head it produces **141 `CONFLICT` lines** — and the *kinds* have changed, which is
the part that looks alarming and is not:

| Kind | Lines |
| --- | ---: |
| `rename/rename` | 51 |
| `rename/delete` | 41 |
| `modify/delete` | 41 |
| `content` | 8 |

**All 133 non-`content` lines are inside `src/Meridian.Ui/wwwroot/workstation/`** — verified: zero
non-`content` lines omit that prefix — spanning **236 distinct hashed asset paths** against a
**128-file** tracked bundle. Both sides rebuilt the bundle; the hashes diverged. The 8 `content`
conflicts are the whole human surface:

| Path | Disposition |
| --- | --- |
| `docs/status/coverage-report.md` | resolver `GENERATED_FILES` |
| `docs/status/doc-health-dashboard.json` | resolver `GENERATED_FILES` |
| `docs/status/doc-health-dashboard.md` | resolver `GENERATED_FILES` |
| `src/Meridian.Ui/wwwroot/workstation/index.html` | resolver `WORKSTATION_ROOT` |
| `docs/source/generated/source-hash-manifest.json` | regenerate; excluded from the resolver on purpose because it is reviewed |
| `database/manifest/contracts.json` | **review** |
| `src/Meridian.Ui/dashboard/package.json` | **the six-key decision above** |
| `src/Meridian.Ui/dashboard/package-lock.json` | regenerate with `npm install` |

**134 of 141 conflicts are the resolver's job; three need a human.**

Checked rather than assumed, because the new conflict kinds are exactly what would break a naive
resolver: `build/scripts/resolve-generated-merge-conflicts.py:88-95` already partitions candidates
against the incoming tree and runs `git rm --force` on names absent from it, which is precisely the
`rename/rename` and `rename/delete` case. `vite.config.ts:246-247` sets
`emptyOutDir: true`, so the regenerate step wipes the tree rather than layering a new build over a
merged one. And `scripts/ci.sh:204-212`'s PRD-018 freshness gate runs `git status --porcelain` over
the bundle after that build, so **any orphan asset left behind by a bad merge fails CI rather than
shipping.** Do not hand-resolve 133 asset conflicts; there is nothing to decide in them.

## The fallback is decaying on a clock it set for itself

`KV-2026-003` has been on `main` since #3065/#3068 as an explicit **proposal, not an acceptance**
(`docs/security/known-vulnerabilities.md:15`). Its own recorded terms have now lapsed:

- **Decision requested by 2026-10-07 17:00 America/Phoenix** (`2026-10-08T00:00:00Z`) — **three days
  past**, with no approval, named approver, or `accepted_on` recorded anywhere, and the machine
  register still empty. The proposal states that no response means no acceptance.
- **Hard expiry 2026-10-19 00:00 UTC**, `review_by: 2026-10-18`, and **late approval explicitly does
  not extend it.**

So registering the acceptance today buys **nine days** of certifiable green, and the validator fails
closed on a stale entry after that. On 2026-10-05 the acceptance was the recommended P0 unblock; it
is now the strictly worse of two routes, because #3102 deletes the vulnerable subtree instead of
recording it. **It remains the right fallback if #3102's review cannot be completed this week** — and
it remains an owned governance call that this document does not make, since `CLAUDE.md` requires
human review for CI governance changes.

## New finding: #3123's retained evidence broke the link checker, and the fix is its scope

`repair-links.py --summary` reports **2,522 links checked, 1 broken** — the first broken link since
the checker's own false positives were corrected on 2026-10-05. It is not a defect in Meridian prose:

```
docs/testing/evidence/wal-checksum-2026-10-07/components/native/doc_benchmarks.md:3
  -> ../readme.md (File not found)
```

That file is a **copied upstream artifact**, retained as WAL-checksum evidence by #3123 alongside
`lib_blake3_dotnet_Cargo.toml`, `src_Blake3_Hasher.cs` and the Rust sources. Its `../readme.md` points
at the upstream project's own root README, which is not part of the retained set — the sibling
`components/native/README.md` is the Meridian-authored SHA-256 diagnostic note, not the target.

**Do not repair it.** Rewriting a link inside retained third-party evidence corrupts the evidence,
which is the same trap as the three "broken" tracker links corrected on 2026-10-05 and the
`LedgerDatabaseFact` grep corrected on 2026-10-09: **the validator's scope is wrong, not the file.**
`repair-links.py:43,55` already has `EXCLUDE_DIRS` and `EXCLUDE_FILES` for this; retained vendored
evidence under `docs/testing/evidence/**` belongs in one of them.

This is also a hard prerequisite for the standing P3 item: **gating link integrity in CI today would
red the lane on a vendored file.** Scope the checker first, then gate it.

## Row hygiene, computed from the registry today

The registry parses: `yaml.safe_load(docs/roadmap/data/roadmap-items.yml)` succeeds, `grep fallback
build/scripts/docs/common.py` returns nothing, and `validate-roadmap-registry.py --summary` exits 0
with 0 errors on the same file. **#3048 landed and that defect pair is closed** — the 2026-10-05
P0 is retired.

42 rows: 17 `done`, 9 `planned`, 9 `in_progress`, 6 `accepted`, 1 `ready_for_acceptance`.

| Row | Status | Priority | Days since review |
| --- | --- | --- | ---: |
| `W5X-OEG-001` | `planned` | high | **117** |
| `W8-WPF-PARITY-001` | `in_progress` | high | **96** |
| `W8-UX-CONSOL-001` | `in_progress` | medium | **83** |
| `W10-RECON-001/002/003/004`, `W10-TAX-001`, `W10-PERF-001`, `W10-CONSOL-001` | `planned` | high/medium | **71** |
| `W9-REPORT-005`, `W9-NAV-006`, `W10-DEBT-001` | `accepted`/`planned` | high/medium | 42 |
| `W9-ALPACA-004` | `accepted` | high | 38 |
| `W9-CORPACT-011` | `ready_for_acceptance` | high | **18** |
| `W9-SAFETY-007`, `W10-SEAM-001`, `W10-MARK-001` | `accepted`/`in_progress` | high | 17 |
| `W9-PAPER-003`, `W9-INGEST-009` | `accepted`/`in_progress` | critical/high | 15 |
| `W9-TRUTH-001` | `accepted` | critical | 14 |
| `W9-GOV-008` | `in_progress` | high | 12 |
| `W10-JRNL-001` | `in_progress` | high | 9 |
| `W10-PROV-001`, `W10-LOT-002` | `in_progress` | high/critical | 8 |

Two notes:

- **`W10-PROV-001` is closed as a finding.** The 2026-10-05 determination carried it as a `planned`
  row with an open implementation PR; #3041 merged on 2026-10-05 and the row now reads `in_progress`,
  `last_reviewed: 2026-10-02`. Nothing to reconcile.
- **`W5X-OEG-001` is the program's oldest unreviewed row at 117 days** — `high`, `planned`, and
  mentioned for the first time by the 2026-10-09 determination. Either date it or defer it
  explicitly; a `high` row nobody has looked at since June is not a priority, it is an untracked one.

## Queue: 21 open PRs

Only three are not drafts: **#3102** (the P0), **#3122** (`Integrate Portfolio Records authority`) and
**#3131** (`Security Master` — opened today). New today: **#3130** (`make governed edits atomic
across instances`). Still waiting: **#3094** (OAuth token permissions, security-labelled, untouched
since 2026-10-06), **#2789** (still self-titled `[DRAFT — RE-CUT REQUIRED]`), **#2587**
(`react-router` major) and **#3058** (`lucide-react` 0 → 1 major).

`.gitattributes` exists and contains **no `merge=` driver** — only `text`/`eol` normalization,
including four rules specifically for the workstation bundle. The standing P3 merge-driver item is
unimplemented and is exactly what today's 133 asset conflicts are made of.

## What not to work on

- **Hand-resolving #3102's conflicts.** 134 of 141 are the resolver's; the bundle is rebuilt, not
  merged, and CI fails on a bad one.
- **Taking #3102's `package.json` wholesale.** It downgrades `vite`, `@vitejs/plugin-react` and
  `postcss` below `main`.
- **Reviewing the `vite` 8 major as part of the P0.** `main` took it on 2026-10-08 and is green.
- **Registering the `braces` acceptance as the first move.** It is the fallback, it buys nine days,
  and it records a risk rather than removing one.
- **Re-running the certification lane hoping for green.** Deterministic against the live advisory
  database until #3102 lands or the register changes.
- **Re-implementing the registry YAML fix.** #3048 landed; the registry parses and the fallback
  parser is gone.
- **Re-adding the statement/reconciliation PostgreSQL round trip.** #3118 landed it on 2026-10-08.
- **"Repairing" the one broken link.** It is inside retained third-party evidence; scope the checker instead.
- **Majors before the freeze.** #2587 and #3058 wait.
- **New product surface.** The `program-state.yml` `scope_gate` is explicit and nine rows are
  `in_progress` across three waves.
- **Writing an eleventh determination.** See below.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0** | **Review and land #3102.** Merge `main` in, run `resolve-generated-merge-conflicts.py --apply`, resolve `package.json` key by key per the table above, `npm install`, `npm run build`, regenerate the source-hash manifest, review `database/manifest/contracts.json`, then **re-run the dependency gate on the merge result** | It carries the Tailwind 4 migration that deletes `braces`/`micromatch` outright. Its only red check is `pipeline-budget`, whose fix merged to `main` on 2026-10-09 and is green there. 134 of its 141 conflicts are automated and the other three are a one-sitting decision |
| **P0** | Split #3102's six dashboard config/manifest files into their own PR, or accept the coupling knowingly | 336 files weld the certification unblock to `W10-LOT-002`, the slate's only `critical` row. One lot-posting review round holds the lane red. The recommendation is three determinations old and has not been acted on |
| **P0** | Decide `KV-2026-003`: approve the bounded acceptance, reject it and keep the lane red, or commission the migration | Its own decision point lapsed **2026-10-07** and its hard expiry is **2026-10-19**. An owned governance call either way; silence is the one option that has already been taken for three days |
| **P0** | Then: signing secrets, the ~1 GB installer decision, freeze a green head, tag `v0.1.0-rc.1` | 21 P0 tracker rows need same-commit evidence no run has minted since 2026-10-02; no `v*` tag exists |
| **P1** | Take or decline `W9-CORPACT-011` (**18 days** at `ready_for_acceptance`) | Zero engineering; its PostgreSQL precondition is met. **Declining is a result too** |
| **P1** | Re-review `W9-INGEST-009` against #3118's landed round trip | Its record's "missing" evidence exists on `main` |
| **P1** | Refresh `W8-WPF-PARITY-001` (**96**) and `W8-UX-CONSOL-001` (**83**); date or defer `W5X-OEG-001` (**117**) | Both W8 rows are named productization targets; `W5X-OEG-001` is the oldest unreviewed row in the program |
| **P2** | Bump `last_reviewed` on `W10-CONSOL-001` and `W10-TAX-001` | First slices merged 2026-10-07; the summaries say so and the dates still read 2026-07-31 |
| **P2** | Triage #3094 (OAuth token permissions) | Security-labelled, untouched for four days |
| **P2** | Close #3112 and #3125 as superseded | Three determinations open at once, including this one |
| **P2** | Scope or drop the `"shadow-operation acceptance"` criterion on `W10-LOT-002` | No source presence; cannot be burned down as written |
| **P3** | Implement the `.gitattributes` merge driver / documented regenerate-on-merge | No longer hygiene: it is 133 of the P0's own conflicts, and it recurs on every merge to `main` while #3102 waits |
| **P3** | Exclude retained vendored evidence from `repair-links.py`, **then** gate link integrity in CI | 1 broken link today, inside third-party evidence #3123 retained. Gating first would red the lane on a file that must not be edited |
| **P3** | Re-cut or close #2789; hold #2587 and #3058 until after the freeze | Decaying draft; majors wait |

## The bar for the next determination

**Do not write one.** The bar — a green certification run, or the RC — has now been missed by four
consecutive determinations (10-05, 10-07, 10-09, this one), three of which are open and unmerged.
Every one of them ranked the same P0 and every one was right. **The ranking has never been the
scarce resource.** Three determinations' worth of analysis now reduces to one review of one PR, one
six-key manifest decision, one `npm install`, one `npm run build`, and one governance decision that
expires on 2026-10-19.

The next useful artifact in this directory is a **merge or a decision record**, not a fifth ranking.
If a determination is written before one of those exists, the thing to examine is the decision path.

## On archiving the predecessors

Convention (`README.md` in this directory) says to move a superseded determination into
`archive/docs/plans/`. **2026-09-27 is again left in place**: `docs/roadmap/data/program-state.yml`
cites it by path as the recorded operator-session plan and the readiness tracker deep-links its
section anchors. Archiving 2026-09-27, 2026-10-02, 2026-10-04 and 2026-10-05 with those references
updated in the same pass remains a separate governance-reviewed step.
