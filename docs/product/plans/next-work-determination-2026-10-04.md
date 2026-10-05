# What To Work On Next — Meridian (2026-10-04)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-04
**Baseline:** `main` at `06e785246d7c8be2ba3b7342785fe7905eceb7e8`
**Supersedes:** [2026-10-02](next-work-determination-2026-10-02.md), and the 2026-09-28, 2026-09-29
and 2026-10-03 determinations proposed in still-open
[#3012](https://github.com/rodoHasArrived/Meridian-main/pull/3012),
[#3030](https://github.com/rodoHasArrived/Meridian-main/pull/3030) and
[#3053](https://github.com/rodoHasArrived/Meridian-main/pull/3053)
**Method:** re-verified every carried-forward claim against **current source on `main`**, not against
its own record; re-measured the whole open pull-request queue with
`git merge-tree --write-tree --name-only origin/main origin/<branch>`, counting only
`CONFLICT (...): Merge conflict in <path>` lines and splitting generated from hand-written paths;
read live GitHub Actions job *and step* state for `Production Certification` and `CI`; reproduced the
failing gate locally; read the roadmap registry (`docs/roadmap/data/*.yml`) and the readiness tracker
(`docs/product/implementation-todo-list.md`). Every claim below is anchored to a run, a commit, a PR
number, a `file:line`, or a reproducible command.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; the tracker remains authoritative for the
> P0 release gate. Recheck conflict counts and CI results against the live head before acting.

**Why another determination two days later.** The 2026-10-02 document set the bar explicitly: "the
next one is due when the queue drains or the RC lands, not tomorrow." Both halves of its headline
have since inverted. **48 commits merged** into `main` (`git log --oneline 021966720..origin/main |
wc -l`), draining nine PRs from the queue — and the certification lane it certified as "green on all
four lanes, three consecutive runs" has been **red on five consecutive runs** since 2026-10-03.

## Headline

**`main` stopped producing a certifiable commit on 2026-10-03 and has not produced one since. The
cause is not a code regression and not anything in the 48 merged commits — it is a newly published
npm advisory with no upstream fix, which turned the dependency gate red by itself. The release
candidate that the last determination said nothing engineering-shaped blocked is blocked again, and
the thing in front of it is now a one-hour security-register decision.**

- **Five consecutive red `Production Certification` runs on `main`:** #131 (`b50b6111`), #132 and
  #133 (`94c53780`), #134 (`36bf2975`), #135 (`06e78524`,
  [37213316219](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37213316219)). The last
  green run was **#130** at `2dbd8723` on 2026-10-02 22:43Z — about four hours after the 2026-10-02
  determination was written.
- **Exactly one job fails, at exactly one step.** In run #135 the documentation-evidence, PostgreSQL
  integration, and encrypted backup/restore jobs are all green. `NuGet and npm dependency evidence`
  fails at step 10, "Assert both dependency gates passed", with
  `Dependency evidence failed for: npm (validate-npm-audit.py)`. The NuGet gate passes.
- **`Meridian CI` is green.** Run #6791 at `06e78524` succeeded. Only the certification lane is red,
  which is why merges are still landing while the RC cannot be minted.

## Root cause, reproduced locally

`npm audit` in `src/Meridian.Ui/dashboard` reports **5 high advisories, all one root cause**:

```
$ cd src/Meridian.Ui/dashboard && npm audit --json
metadata.vulnerabilities: {info: 0, low: 1, moderate: 0, high: 5, critical: 0, total: 6}
braces       high  via GHSA-vfj7-8cjw-p6xm  ->  effects: micromatch, chokidar
micromatch   high  via braces               ->  effects: fast-glob, tailwindcss
fast-glob    high  via micromatch
chokidar     high  via braces
tailwindcss  high  via micromatch/chokidar   (isDirect: true)
```

**[GHSA-vfj7-8cjw-p6xm](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm)** — "braces vulnerable to
stack-exhaustion denial of service through deeply nested patterns", CWE-674, CVSS 3.1 **7.5 high**,
vulnerable range **`<=3.0.3`**.

**Nothing in this repository changed to cause it.**

```bash
git diff --stat 2dbd8723 b50b6111 -- src/Meridian.Ui/dashboard/package.json \
  src/Meridian.Ui/dashboard/package-lock.json build/config/security/
# (no output)
```

Not one byte of the dashboard manifest, the lockfile, or the security register differs between the
last green certification run and the first red one. `npm audit` queries a live advisory database, so
**the gate flipped on a clock, not on a commit.** Any determination, tracker entry, or review that
treats a green certification run as a durable property of a commit is wrong about this lane: a
frozen commit can go red without being touched.

**There is no patched `braces`, so there is no cheap dependency bump.**

```bash
npm view braces versions --json   # newest published: 3.0.3
grep -A4 '"node_modules/braces"' package-lock.json   # version 3.0.3, "dev": true
```

The advisory's vulnerable range is `<=3.0.3` and **3.0.3 is the newest version that exists**. An
`overrides` entry cannot pin to a fixed release because none has been published, and
`src/Meridian.Ui/dashboard/package.json` already carries `overrides` for `postcss` and `nanoid`, so
the mechanism is wired and still cannot help here. The only remediation npm offers is
`tailwindcss@4.3.3`, flagged `isSemVerMajor: true` against the installed `tailwindcss 3.4.15` — it
"fixes" the advisory by dropping the `braces`/`micromatch` dependency path entirely.

**All five advisories are build-time only.** `tailwindcss 3.4.15` is a `devDependency`
(`src/Meridian.Ui/dashboard/package.json`), and `braces` is `"dev": true` in the lockfile. None of
these packages ships in the browser workstation bundle; the vulnerable code path is Tailwind's
content-scanning glob expansion at build time, and the attack requires an attacker-authored deeply
nested glob pattern inside the build configuration.

## Tier 0 — Decide the braces advisory. It is the only thing between `main` and a certifiable commit.

There are exactly two paths, and they are not close in cost.

**Recommended: register a time-boxed, reviewed acceptance.** This is precisely the mechanism the gate
was built for, and the register is currently empty
(`build/config/security/npm-audit-accepted-advisories.json` → `"accepted": []`). The validator's own
docstring says a raw `npm audit` "keeps the production certification dependency gate permanently red,
which hides every new advisory behind an expected failure" — which is the state `main` is in today.

1. Add one entry to `build/config/security/npm-audit-accepted-advisories.json` with the eight
   required fields (`id`, `ghsa`, `package`, `max_severity`, `reason`, `owner`, `accepted_on`,
   `review_by` — `validate-npm-audit.py:38-47`), naming `GHSA-vfj7-8cjw-p6xm` at `max_severity: high`.
2. Mirror it as an accepted-risk record in `docs/security/known-vulnerabilities.md` under the
   existing `KV-YYYY-NNN` convention; `KV-2026-001` is the worked precedent, including how a
   retirement is recorded when an upstream fix lands.
3. Rationale to record: dev-only dependency, absent from the shipped bundle; **no patched version
   exists at any severity**; exploitation requires an attacker-controlled glob pattern in the build
   configuration. Set `review_by` to the Tailwind 4 migration decision date, not a quarter out, so
   the acceptance expires into a real decision.

**Note the register's two-sided fail-closed behavior.** Entries expire at `review_by`, *and* an entry
that no longer matches any reported advisory also fails the gate — so this acceptance must be retired
in the same change that lands Tailwind 4, or it will red the lane from the other direction.

**Not recommended now: take the Tailwind 3 → 4 major.** It is the only true remediation and it should
be scheduled, but it is a framework migration on the browser workstation's styling layer (CSS-first
configuration, removed and renamed APIs, changed shipped CSS), it needs visual verification across
every workstation screen, and the last determination's own rule — "majors before the freeze. #2878
and #2587 wait" — applies with more force to the styling framework than to an icon package. Do it
**after** the RC, as its own lane, with the acceptance retired in the same change.

**Everything else in the previous Tier 0 still stands and is still not engineering work.** Provide
`MDC_SIGNING_CERT_PFX_BASE64` and `MDC_SIGNING_CERT_PASSWORD` in the protected
`desktop-release-signing` environment; decide the ~1 GB consumer installer question; freeze a green
head and tag `v0.1.0-rc.1` (still no `v*` tag has ever existed — `git tag --list` returns nothing at
all on this checkout, so the first tag is not blocked by the version scheme); then activate the
required release check, sign off ADR-019/ADR-020, and schedule the operator drill review. **None of
it can start until a certification run is green again**, because the tag's whole purpose is
same-commit evidence.

## Correction to the previous determination

**#3041 does have CI, and so does #3048.** The 2026-10-02 document reported "#3041 also has no CI at
all — `get_status` returns `total_count: 0`, so not one check has run against `c8faa234`" and ranked
it on that basis. `get_status` reads **commit statuses**; this repository reports through **check
runs**. Read correctly, #3041 carries **25 check runs** and #3048 carries **23**. The substantive
recommendation survives — both are red on `quality-gate` and should not be reviewed as ready — but the
reason is a real gate failure, not an absence of evidence. Anyone triaging PR CI here should use
check runs, not commit statuses, exactly as the predecessor's own caveat says to read step `outcome`
rather than `conclusion` in the certification lane.

## Tier 1 — The registry parse break is fixed in an open PR. Merge it, do not rewrite it.

The last determination ranked "fix the registry parse break and enforce the schemas" as P1 work.
**It is already implemented in #3048, and verified:**

```bash
git show origin/codex/meridian-next-tasks:docs/roadmap/data/roadmap-items.yml > /tmp/ri.yml
python3 -c "import yaml;yaml.safe_load(open('/tmp/ri.yml'))"   # parses clean
python3 -c "import yaml;yaml.safe_load(open('docs/roadmap/data/roadmap-items.yml'))"
# on main: ScannerError: mapping values are not allowed here, line 797, column 886
```

`#3048` quotes the offending scalars (24 lines in `roadmap-items.yml`) **and deletes 90 lines from
`build/scripts/docs/common.py`** — the hand-rolled subset parser whose silent fallback is why
`validate-roadmap-registry.py` exits 0 against a file no standard reader can load. That is both
halves of the P1 recommendation.

**What it needs is a merge, not a reimplementation.** #3048 is red on `quality-gate`, `verify-docs`
and `regenerate-docs`; the `regenerate-docs` log shows the failure is **generated-doc drift only** —
the doc-health dashboard differs by `| 1970-01-01 | 82 | 726 | 250 | 0 |` versus
`| 1970-01-01 | 81 | 688 | 248 | 0 |`. Merge `main`, re-run the deterministic documentation
automation, commit the regenerated output. It has 6 conflicts, 1 hand-written
(`database/manifest/contracts.json`).

**Two cautions.** First, #3048 is a multi-concern PR — its title is "Post canonical lot amortization
atomically and harden Vault exports and roadmap YAML", so the registry fix rides alongside
accounting changes and the `quality-gate` failure may belong to either; triage before assuming the
whole PR is mechanical. Second, the predecessor's note still binds: **the quoting fix must not share
a commit with any other edit to `roadmap-items.yml`**, because a mistake in an unparseable file is
invisible to every tool that reads it.

## Tier 2 — The queue is markedly cheaper than it was two days ago. Drain it.

Re-measured today against `06e78524`. **28 open PRs.** Counting only real conflict markers and
splitting generated paths (`docs/generated/`, `docs/source/generated/`, `docs/status/`,
`docs/ai/generated/`, `docs/diagrams/`, `src/Meridian.Ui/wwwroot/workstation/`) from hand-written:

| PR | Branch | Conflicts | Hand-written | Change since 10-02 | Disposition |
| --- | --- | --- | --- | --- | --- |
| #2878, #2932, #2944, #2981, #3011, #3031, #3033, #3034, #3035, #3036 | — | **0** | **0** | #2981 and #3011 newly clean | **10 clean** |
| #2903 | `postcss-selector-parser` | 1 | **0** | was 1 hand-written | now mechanical |
| #2928 | `codex/p0-csv-evidence` | 2 | **0** | **was 10 / 6 hand-written** | now mechanical |
| #2930 | `p0-statement-durable-commit` | 2 | 0 | 3–4 → 2 | mechanical |
| #3016 | `windows-atomic-snapshot-publication` | 2 | 0 | newly measured | mechanical |
| #2307 | dashboard build-tooling (**79 days**) | 2 | **0** | **was 2 hand-written** | lock no longer conflicts |
| #3054, #3055 | new 10-04 drafts | 2 | 0 | new | mechanical |
| #2920 | `p0-cash-ladder-currency-evidence` | 3 | 0 | 3–4 → 3 | mechanical |
| #3049 | `codex/recovery-evidence` | 3 | 0 | new | mechanical |
| #3053 | 2026-10-03 determination | 3 | 0 | new | see Tier 4 |
| #2826 | `codex/first-trusted-close` | 4 | **0** | **was 7 / 7 hand-written** | **read before merging — see below** |
| #3050 | `codex/current-basis-relief` | 4 | 1 | new | `contracts.json`; follows #3048 |
| #2896 | `replay-parameter-fail-closed` | 5 | 0 | 3–4 → 5 | mechanical |
| #3048 | `codex/meridian-next-tasks` | 6 | 1 | new | **Tier 1** |
| #3004 | `reconciliation-lineage-2636` | 7 | 1 | **was 17 / 3** | `contracts.json` only |
| #3041 | `codex/w10-amount-provenance` | 9 | 1 | was 3 / 0 | `contracts.json`; see Tier 3 |
| #2789 | `backtesting-quantscript-milestone-1` | 25 | 15 | 32 / 12 | stays `[DRAFT — RE-CUT REQUIRED]` |
| #2587 | `react-router` (**major**) | **340** | **269** | 413 / 351 | re-cut; do not merge |

**The drain is now unusually cheap: 10 PRs merge clean and 13 more conflict only in generated
output.** The predecessor's prediction that the queue "decays" held for the *generated* tree — `main`
regenerated the content-hashed workstation bundle, so every stale branch now collides on
`src/Meridian.Ui/wwwroot/workstation/assets/*` — but the hand-written cost **fell** across the board
as `main` absorbed the overlapping work. Three PRs went from needing genuine review to needing none.

**#2826 must be read before it is merged, precisely because it is now mechanical.** Two days ago all
seven of its conflicts were hand-written source, four in files #3026 had just rewritten, and the
recommendation was "expect to close rather than resolve". Today it has **zero** hand-written
conflicts — consistent with `main` having absorbed that work by another route. A branch whose
substance has landed elsewhere and whose conflicts are now purely generated is the easiest kind of PR
to merge by reflex and the worst kind to merge unread: nothing will stop it from silently
reintroducing a superseded shape. Read its diff against current `main` and expect to **close** it.

**The merge driver is still unimplemented**, and this measurement is the strongest case yet for it:
13 PRs are blocked on nothing but regenerating files a script already knows how to regenerate.
`.gitattributes` defines no driver for the generated trees, and
`build/scripts/resolve-generated-merge-conflicts.py:19-40` deliberately excludes `docs/diagrams/`,
`docs/ai/generated/`, and the reviewed source-hash manifest.

**Sequencing against the freeze.** The RC freezes the dependency set, so the ten clean PRs are
pre-freeze work or an explicitly recorded deferral. But **Tier 0 comes first**: merging dependency
bumps while the gate is red buries them in a lane that was already failing, and the acceptance entry
is what makes a green run — and therefore a real pre-merge signal — possible again.

## Tier 3 — Row hygiene: six rows now understate or misdescribe what source does

Measured from the registry today. None of this is engineering work.

| Row | Status | `last_reviewed` | Age | Issue |
| --- | --- | --- | --- | --- |
| `W8-WPF-PARITY-001` | `in_progress` | 2026-07-06 | **90 days** | An active co-equal UI lane and named productization target, unreviewed for three months |
| `W8-UX-CONSOL-001` | `in_progress` | 2026-07-19 | **77 days** | Also a named productization target; its `current_summary` is the unparseable scalar #3048 fixes |
| `W10-PROV-001` | **`planned`** | 2026-07-31 | 65 days | #3041 implements it. A row nobody has started has an open implementation PR |
| `W10-RECON-001` | `planned` | 2026-07-31 | 65 days | Rank 2 on the slate, behind three in-progress rows |
| `W9-CORPACT-011` | `ready_for_acceptance` | 2026-09-22 | **12 days** | Its stated precondition is met on PostgreSQL. Overdue, zero engineering cost. **Declining is a result too** |
| `W9-GOV-008` | `in_progress` | 2026-09-28 | 6 days | Reviewed three days before #3028 merged; criterion two is now implemented on `main` |
| `W9-INGEST-009` | `in_progress` | 2026-09-25 | 9 days | Reviewed five days before #3026 merged; the PostgreSQL round trip its record calls missing now exists |

The three broken internal links in the P0 tracker are **unchanged** — reproduced today with
`python3 build/scripts/docs/repair-links.py --summary`: 1,873 links checked, **3 broken, all three in
`docs/product/implementation-todo-list.md`** (lines 64, 91 and 107). Two are the tracker's own
citations of the recorded operator-session plan, so the P0 gate still cites its sequencing authority
through a link that does not resolve. No job gates on link integrity — the certification lane's
documentation job gates on generated-output *drift* — which is why they keep surviving green runs.

## Tier 4 — W10, and a determination pile-up that is now four deep

- **`W10-LOT-002` is finally being implemented.** Still the slate's only `critical` row. Verified
  again today: `grep -rn "Successor" src/Meridian.Contracts/Accounting/Lots/` returns **nothing** on
  `main`, so append-only predecessor/successor corporate-action lot mutations remain absent from the
  lot contracts. But #3048 and #3050 (*"W10-LOT-002: atomic relief from current basis (partial,
  follows #3048)"*) are an explicit chain against it. **Land #3048 first** — #3050 declares the
  dependency, and #3048 carries the Tier 1 registry fix.
- **`W10-MARK-001` and `W10-SEAM-001`** remain one operator session each from closing; both blockers
  are live operator certification, not engineering. The recorded operator-session plan still
  certifies `SEAM` first.
- **Four determinations are now open or superseded at once:** #3012 (2026-09-28), #3030
  (2026-09-29), #3053 (2026-10-03), and this one. **Recommend closing #3012, #3030 and #3053 without
  merging.** #3012's baseline is hundreds of commits behind; #3030's top two recommendations merged
  on 09-30 and 10-01 and its tag-scheme claim was corrected on 10-02; #3053 predates the five-run
  certification failure that is this document's headline and therefore ranks a release candidate as
  unblocked when it is not. Landing four documents that each claim to supersede 2026-09-27 would
  leave the register describing a program nobody can reconstruct. That is a maintainer call, not one
  this document makes.
- **The cadence problem is real and this document is part of it.** Six determinations in seventeen
  days is not a planning rhythm, it is a symptom: the queue holds work that is ranked repeatedly and
  merged rarely. The bar for the next one is unchanged and should be enforced — **when a
  certification run is green again, or when the RC lands.** If a seventh is written before either
  happens, the ranking is not what needs attention.

## What not to work on

- **New product surface.** The `program-state.yml` `scope_gate` is explicit, and eight rows are in
  progress or awaiting acceptance across three waves.
- **The Tailwind 4 migration, today.** It is the only real fix for the braces advisory and it should
  be scheduled — after the RC, as its own lane, with the acceptance retired in the same change.
- **Re-running the certification lane hoping for green.** The failure is deterministic against the
  live advisory database: it will red every run until the register decision in Tier 0 is made.
- **Re-implementing the registry YAML fix.** It exists in #3048 and parses clean. Merge it.
- **Majors before the freeze.** #2878 (`lucide-react` 0 → 1) and #2587 (`react-router`) wait.
- **Re-litigating accepted W9 rows.** Six are accepted under recorded decisions and one is done.
- **Re-cutting the large stranded branches** beyond #2789 and #2587.
- **Untracking the built asset tree.** `PRD-013` and `PRD-018` both require one tracked canonical
  tree; fix the merge driver, not the tracking — Tier 2 is the business case.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0** | Register a time-boxed acceptance for `GHSA-vfj7-8cjw-p6xm` in `npm-audit-accepted-advisories.json` + `known-vulnerabilities.md`, or consciously accept a red lane | Five consecutive red certification runs. Dev-only dependency, **no patched version exists**, and the only offered fix is a framework major. Under an hour, and nothing else in Tier 0 can start until a run is green |
| **P0** | Merge #3048 (base merge + regenerate docs) | It fixes the registry parse break **and** deletes the silent fallback parser — the whole previous P1 — and #3050 declares a dependency on it |
| **P0** | Then: signing secrets, the ~1 GB installer decision, freeze a green head, tag `v0.1.0-rc.1` | Unchanged and still not engineering work. The first tag is **not** blocked by the version scheme; it is blocked by the red lane |
| **P1** | Drain the 10 clean PRs after the gate is green; resolve the 13 generated-only conflicts | The RC freezes the dependency set, and a green gate is what makes the pre-merge signal meaningful again |
| **P1** | Take or decline `W9-CORPACT-011` (**12 days**) | Zero engineering; its stated precondition is met on PostgreSQL |
| **P1** | Read #2826 against current `main` — expect to close, not merge | Its hand-written conflicts went 7 → **0** as `main` absorbed the work. Now trivially mergeable and the worst candidate for a reflex merge |
| **P1** | Refresh `W8-WPF-PARITY-001` (**90 days**) and `W8-UX-CONSOL-001` (**77 days**); re-review `W9-GOV-008` and `W9-INGEST-009` against current source | Both W8 rows are named productization targets; the W9 rows were reviewed before #3026 and #3028 landed |
| **P2** | Reconcile `W10-PROV-001`'s `planned` status against #3041; do not review #3041 or #3048 as ready while `quality-gate` is red | A `planned` row has an open implementation PR. **Read check runs, not commit statuses** — see the correction above |
| **P2** | Continue `W10-LOT-002` via #3048 → #3050 | The only `critical` row; successor mutations verified absent from the lot contracts again today |
| **P2** | Close #3031 (obsolete), and the stale determinations #3012, #3030 and #3053 | Their substance has landed or been superseded; #3053 ranks the RC as unblocked when it is not |
| **P3** | Implement the `.gitattributes` merge driver / documented regenerate-on-merge for the generated trees | **13 PRs are blocked on nothing else.** Recurring by construction; the existing script excludes diagrams, `docs/ai/generated/`, and the source-hash manifest |
| **P3** | Fix the three broken tracker links and consider gating link integrity in CI | Unchanged since 2026-10-02. No job gates on it, so they survive green runs |
| **P3** | Schedule the Tailwind 3 → 4 migration as a post-RC lane, retiring the acceptance in the same change | The only true remediation. The register fails closed on stale entries, so the retirement must ride with the migration |

## A durability note worth recording in the tracker

This is the finding most likely to outlive the advisory. **A green `Production Certification` run is
not a property of a commit.** Run #130 and run #135 differ by 48 commits, but runs #132 and #133
differ by *nothing at all* — same head `94c53780`, one push-triggered and one scheduled — and both
are red where the same lane was green hours earlier at a commit nobody has touched since. The
dependency gate consults a live advisory database, so a frozen RC commit can turn red without being
modified.

The release gate in `docs/product/implementation-todo-list.md` requires every P0 row complete "on the
same release commit". That requirement is sound, but it should say explicitly that same-commit
evidence is **evidence as of the run**, and that an RC's certification must be re-run — not assumed —
before a stable tag. The acceptance register is the mechanism that makes this survivable: it is what
converts "a new advisory silently reds the freeze" into "a new advisory requires a dated, owned,
expiring decision". Leaving it empty is not a neutral default.

## On archiving the predecessors

Convention (`docs/product/plans/README.md`) says to move a superseded determination into
`archive/docs/plans/`. The 2026-09-27 document is again **left in place**, for the same reason the
2026-10-02 document left it: it is load-bearing in governance data rather than merely historical.
`docs/roadmap/data/program-state.yml:43` cites it by path as the recorded operator-session plan, and
`docs/product/implementation-todo-list.md:64,91` deep-link one of its section anchors — the two links
that are currently broken. This document **preserves** that operator-session plan rather than
replacing it (see Tier 4). Archiving 2026-09-27 and 2026-10-02, with the `program-state.yml` and
tracker references updated in the same pass, remains a separate governance-reviewed step.
