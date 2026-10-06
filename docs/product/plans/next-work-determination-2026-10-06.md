# What To Work On Next — Meridian (2026-10-06)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-06
**Baseline:** `main` at `9c96c3f72dd60111b62b8368d29e553ec58fd7a0`
**Supersedes:** [2026-10-05](next-work-determination-2026-10-05.md)
**Method:** reproduced every build and dependency claim locally from this commit's lockfile rather
than reading it from a record; read GitHub Actions *job logs* and *job conclusions* for the runs at
today's head; re-measured the whole open pull-request queue with
`git merge-tree --write-tree --name-only origin/main origin/<branch>`, counting only
`CONFLICT (...): Merge conflict in <path>` lines and splitting generated from hand-written paths;
queried the npm registry and the npm advisory database directly for patched versions; read the
roadmap registry (`docs/roadmap/data/*.yml`) and the readiness tracker
(`docs/product/implementation-todo-list.md`).

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; the tracker remains authoritative for the
> P0 release gate. Recheck conflict counts and CI results against the live head before acting.

## Headline

**The bar the predecessor set — "a green certification run, or the RC" — was not met. But the thing
it was waiting on no longer exists, and a larger failure took its place while nobody was watching
that lane.**

Three things changed in the fifteen hours before this document:

1. **`braces` is gone from the dependency tree.** Dependabot [#3090](https://github.com/rodoHasArrived/Meridian-main/pull/3090)
   bumped `tailwindcss` 3.4.15 → 4.3.3 on 2026-10-05, and Tailwind 4 dropped the
   `chokidar`/`micromatch`/`fast-glob` chain that carried it. The advisory three determinations
   ranked as the P0 — `GHSA-vfj7-8cjw-p6xm`, with no patch at any published version — **is no longer
   reachable from this repository.** The acceptance decision those documents asked for must not be
   made; the register fails closed on entries that match no reported advisory, so landing it now
   would turn a passing check red.
2. **The dependency gate is still red, for a different advisory with an ordinary fix.**
   `GHSA-68fv-2mgg-jv7q` in `source-map-js` (high, CVSS 7.5, vulnerable `>=1.0.0 <1.2.2`) is
   **patched in 1.2.2**, published 2026-09-30. The fix is a lockfile bump inside the range `postcss`
   already requires, and it is **already sitting in open PR [#3091](https://github.com/rodoHasArrived/Meridian-main/pull/3091)**.
3. **That same Tailwind bump broke the browser workstation.** `Meridian CI / quality-gate` — the
   authoritative merge gate — **is red on `main`** and has been since #3090 landed. The dashboard
   neither builds nor tests. Three more merges landed on top of it.

**The P0 is no longer a governance decision. It is two concrete engineering actions, one of which
is already written.**

## What closed, and what the record now gets wrong

| Previous item | Status today | Evidence |
| --- | --- | --- |
| Register a time-boxed acceptance for `GHSA-vfj7-8cjw-p6xm` (P0 for three determinations) | **Must not be done.** `braces` is absent from the lockfile. | `grep braces src/Meridian.Ui/dashboard/package-lock.json` → only `brace-expansion@5.0.12`; `validate-npm-audit.py:17-18` fails the gate on entries matching no reported advisory |
| Merge #3048 (registry parse fix + delete the fallback parser) | **Done.** | `yaml.safe_load(docs/roadmap/data/roadmap-items.yml)` parses clean (42 items); `grep -n fallback build/scripts/docs/common.py` → no match |
| Close #3031 (two commits, zero net diff) | **Done.** Absent from the open list. | open-PR list below |
| Close the stale determinations #3012, #3030, #3053 | **Done.** All three absent; no determination PR is open. | open-PR list below |
| Reconcile `W10-PROV-001` against #3041 | **Done.** #3041 merged; the row is now `in_progress`, reviewed 2026-10-02. | `roadmap-items.yml`; certification run #170 at `8e7d2b3a` |
| "Read #2826 against current `main` — 4 conflicts, **0 hand-written**, expect to close" | **Obsolete.** It is now **77 conflicts, 73 hand-written.** | measured today against `9c96c3f7` |
| "#2789 is decaying — 41 conflicts, 28 hand-written" | **Correct and far worse: 574 conflicts, 486 hand-written.** | measured today |
| "Schedule Tailwind 3 → 4 post-RC as its own lane" | **Vindicated, and overtaken.** The bump arrived by accident on 2026-10-05 with no migration. | `git show a5be8a58 --stat` → 2 files changed |

One correction worth recording because it is the opposite of what the last three documents said:
`docs/security/known-vulnerabilities.md:13-116` now carries a **"Pending decision: braces stack
exhaustion (2026-10-05)"** section with retained hosted evidence, bounded reachability probes, and a
drafted acceptance scope. That package — prepared under #3065 and #3068 — documents the dependency
chain at lines 71-73 as `tailwindcss 3.4.15 -> {chokidar, micromatch, fast-glob} -> braces 3.0.3`.
**It names its own expiry.** The Tailwind 4 bump removed that chain hours after the section was
written. The section should be closed out as *resolved by removal*, not accepted.

## P0 — The browser workstation does not build on `main`

This is new, it is the top item, and it is not in any record.

**`Meridian CI` run [#4996](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37435597202)
at today's head `9c96c3f7`: `verify-browser` **failure**, `quality-gate` **failure**.**
`verify-dotnet`, `verify-docs`, `verify-workflows`, `service-backed-integrations` and
`integration-gate` are all green. One lane is red, and it is the operator UI.

The job log names the cause verbatim, in `src/app.test.tsx`, in batch 1 of 41:

```
Error: [postcss] It looks like you're trying to use `tailwindcss` directly as a PostCSS
plugin. The PostCSS plugin has moved to a separate package, so to continue using Tailwind
CSS with PostCSS you'll need to install `@tailwindcss/postcss` and update your PostCSS
configuration.
```

**Reproduced locally from this commit**, not inferred: `npm ci && npm run build` in
`src/Meridian.Ui/dashboard` fails with the same error on `src/styles/index.css`,
`src/styles/app-shell.css` and the `index.html` inline block. The bundle is not produced.

### Why it happened

`a5be8a58` (#3090) changed exactly two files — `package.json` and `package-lock.json` — and moved a
**major version**:

```
$ git show --stat a5be8a58
 src/Meridian.Ui/dashboard/package-lock.json | 807 +---------------------------
 src/Meridian.Ui/dashboard/package.json      |   2 +-
```

Tailwind 4 moves the PostCSS plugin into `@tailwindcss/postcss`, stops auto-loading
`tailwind.config.ts`, and replaces the `@tailwind` directives. The repository still carries all
three v3 conventions: `postcss.config.cjs` lists `tailwindcss: {}`; `tailwind.config.ts` is a v3
config; `src/styles/index.css:34-36` holds `@tailwind base/components/utilities`. No
`@tailwindcss/postcss` is declared anywhere.

**318 of the 326 dashboard test files never ran.** The suite dies in the first batch.

### What the fix costs — measured, not estimated

A minimal migration was applied locally and reverted. Three files:

| Change | Detail |
| --- | --- |
| `package.json` | add `@tailwindcss/postcss@4.3.3` as a devDependency (15 transitive packages) |
| `postcss.config.cjs` | `tailwindcss: {}` → `"@tailwindcss/postcss": {}` |
| `src/styles/index.css` | `@tailwind base/components/utilities` → `@import "tailwindcss"; @config "../../tailwind.config.ts";` — **hoisted above the `@font-face` blocks**, because CSS requires `@import` to precede other rules. Leaving the import at line 34 builds no theme and fails with `Cannot apply unknown utility class 'font-medium'`. |

With those three changes: **`npm run build` succeeds, and the full suite is 3,551 tests across 41
batches, 0 failures.** Four of the five emitted CSS chunks are **byte-identical** to the tracked
bundle (same content hashes: `DH5VwHe8`, `BNbr9YRQ`, `BfPWbdfN`, `DXxnawaZ`).

**The one residual risk is real and should not be waved through.** The main stylesheet goes
**137,626 → 184,933 bytes (+34%)** — Tailwind 4 emits a different utility layer (oklch palette,
`@property` declarations) and changes v3 defaults for border colour, ring width and placeholder
colour. A green build and a green suite do not prove visual parity on a co-equal UI lane. The repo
already has the mechanism to check: `web-screenshot-capture.yml` and
`npm --prefix src/Meridian.Ui/dashboard run screenshots`. **Run it, diff it, then merge.**

### Do not revert the bump

The obvious reflex — pin `tailwindcss` back to 3.4.15 — **reintroduces the unfixable advisory**.
Measured against the pre-bump lockfile at `261b0984`:

```
braces present: node_modules/braces 3.0.3
dependents:     node_modules/chokidar -> ~3.0.2
                node_modules/micromatch -> ^3.0.3   (tailwindcss 3.4.15 -> micromatch ^4.0.8)
```

A revert trades a **fixable** build break for an advisory with **no patch at any published version**,
and resurrects the governance decision this document is closing. Finish the migration instead.

### It falsifies a P0 row the tracker still rates complete

`PRD-018`'s closure candidate reads: *"serving assets provably built from the same commit's dashboard
source"* and *"CI (`browser-workstation` job and `scripts/ci.sh`) fails when the tracked bundle lags
dashboard source"* (`implementation-todo-list.md:264`). On today's head **the tracked bundle cannot be
rebuilt from this commit's dashboard source at all** — `src/Meridian.Ui/wwwroot/workstation/assets/index-CMWZgeO9.css`
is Tailwind 3 output while `package.json` declares Tailwind 4. The tree is internally inconsistent,
and the freshness gate that was supposed to catch exactly this never reaches its own assertion
because the lane dies at the test step first. `PRD-013`'s publish-smoke freshness evidence is blocked
for the same reason. **No RC can be cut from a commit whose UI bundle is unreproducible.**

## P0 — Merge #3091. The dependency gate's fix is already written

`Production Certification` run [#178](https://github.com/rodoHasArrived/Meridian-main/actions/runs/37435597248)
at `9c96c3f7` fails in exactly one job, at exactly one step, as it has since 2026-10-02 — but the
advisory is not the one on the record:

```
npm-audit gate: UNACCEPTED GHSA-68fv-2mgg-jv7q (source-map-js, high): no acceptance entry
npm-audit gate: FAIL — see unaccepted or stale entries above.
```

The documentation-evidence, PostgreSQL integration and encrypted backup/restore jobs are green; the
NuGet half of the dependency job passes.

Verified directly against the registries:

| Fact | Source |
| --- | --- |
| `GHSA-68fv-2mgg-jv7q`, `source-map-js`, **high**, CVSS 7.5, vulnerable `>=1.0.0 <1.2.2` | npm bulk advisory endpoint |
| **First patched version: 1.2.2**, published **2026-09-30** | `registry.npmjs.org/source-map-js` `time` map |
| Lockfile holds **1.2.1**; the only dependant is `postcss@8.5.23` requiring `^1.2.1` | `package-lock.json` |

So the patch satisfies the existing range: no API change, no major, no override gymnastics. An
`overrides` entry matching the repo's existing `postcss`/`nanoid` pattern resolves it in a four-line
diff (verified locally: `npm install --package-lock-only` → `source-map-js 1.2.2`). **Dependabot
already opened #3091 for it at 03:07 today.** Read it, confirm it is the version bump and nothing
else, merge it.

Note that this advisory **was not introduced by the Tailwind bump** — `source-map-js@1.2.1` was
already in the pre-bump tree. It became visible when 1.2.2 and the advisory were published.

**Scored runs since the last green (#130, `2dbd8723`, 2026-10-02):** ten failures (#168, #169,
#171–#178) and five cancelled by concurrency (#164–#167, #170). Four days, no certifiable commit.

## Both are required, and in this order

Merging #3091 alone leaves `quality-gate` red, because the browser lane is a separate failure.
Fixing Tailwind alone leaves `Production Certification` red. **An RC needs both.** The signing
certificate, the ~1 GB consumer-installer decision and the `v*` tag that four determinations have
queued behind a green run are unchanged and still not engineering work — but they stay blocked until
these two land.

## Queue re-measured: 22 open PRs, and the whole queue decayed in one day

Roughly fifteen pull requests merged into `main` on 2026-10-05. Every long-lived branch paid for it.
Measured today against `9c96c3f7`, splitting generated paths (`docs/generated/`,
`docs/source/generated/`, `docs/status/`, `docs/ai/generated/`, `docs/diagrams/`,
`src/Meridian.Ui/wwwroot/workstation/`) from hand-written:

| PR | Conflicts | Hand-written | Change since 10-05 | Disposition |
| --- | --- | --- | --- | --- |
| #3093, **#3091**, #3058, #3057, #2944, #2932 | **0** | **0** | #3093/#3091 new | **6 clean — #3091 is the one to merge** |
| #3094 | 1 | 0 | new | mechanical |
| #3077, #3054 | 2 | 0 | — | mechanical |
| #3067, #2928, #2920 | 3 | 0 | #2928/#2920 were 2/3 | mechanical |
| #3074, #3037 | 4 | 0 | #3037 was 3 | mechanical |
| #3016 | 4 | 1 | was 3 / 0 | small |
| #3070 | 6 | 2 | new | small |
| #3004 | 16 | 8 | **was 8 / 1** | growing |
| #2896 | **84** | **75** | **was 6 / 0** | **re-cut or close** |
| #2826 | **77** | **73** | **was 4 / 0** | **re-cut or close — the old advice is void** |
| #2307 | **117** | **115** | **was 2 / 0** | **re-cut or close** |
| #2587 | 383 | 312 | was 340 / 269 | re-cut; do not merge |
| #2789 | **574** | **486** | **was 41 / 28** | **close it** |

**6 clean, 8 generated-only (22 generated conflicts in total), 8 with hand-written conflicts.**

Three branches the predecessor called "mechanical" or "trivially mergeable" are now nothing of the
sort. **#2826 went from 0 hand-written conflicts to 73 in a single day** — it was recommended for a
read-then-close and would now cost a day of merge resolution to even look at. **#2789 has carried
`[DRAFT — RE-CUT REQUIRED]` in its own title since 2026-08-18; it is now 486 hand-written conflicts
across backtesting, execution and storage.** That is not a backlog item. Close it.

**The merge-driver case is now sharper than "P3 convenience."** Twelve of the fourteen conflicted
PRs are blocked partly or wholly on regenerating files a script already knows how to regenerate —
and a large share of those are the built workstation bundle, **which cannot be regenerated at all
while Tailwind is broken**. The generated-conflict remedy and the PRD-018 freshness gate are both
downstream of the P0 above.
`build/scripts/resolve-generated-merge-conflicts.py:19-40` still excludes `docs/diagrams/`,
`docs/ai/generated/`, and the reviewed source-hash manifest.

## Row hygiene, measured today

| Row | Status | Unreviewed | Note |
| --- | --- | --- | --- |
| `W5X-OEG-001` | `planned`, high | **113 days** | Not raised by any predecessor. Oldest row in the registry |
| `W8-WPF-PARITY-001` | `in_progress`, high | **92 days** | Named productization target; an active co-equal UI lane |
| `W8-UX-CONSOL-001` | `in_progress`, medium | **79 days** | Named productization target |
| `W10-RECON-001` and five other W10 rows | `planned` | **67 days** | `W10-RECON-001` is rank 2 |
| `W9-CORPACT-011` | `ready_for_acceptance`, high | **14 days** | Precondition met on PostgreSQL. **Declining is a result too** |
| `W10-LOT-002` | `in_progress`, **critical** | 4 days | `grep -rn "Successor" src/Meridian.Contracts/Accounting/Lots/` still returns **nothing** — append-only predecessor/successor lot mutations remain absent. #3093 is open against it |

Both W8 rows are the operator-UI lane whose browser half does not currently build. Refreshing them
without recording that is worse than leaving them stale.

## What not to work on

- **Accepting `GHSA-vfj7-8cjw-p6xm`.** The package is gone. The register fails closed on entries that
  match no reported advisory, so the acceptance would itself turn the gate red.
- **Reverting `tailwindcss` to 3.4.15.** It brings back `braces@3.0.3`, which has no fix at any
  version. Finish the migration.
- **Merging the Tailwind fix without a screenshot diff.** The main stylesheet grows 34%; v4 changes
  v3 defaults. The tests passing is necessary, not sufficient.
- **Re-running either lane hoping for green.** Both failures are deterministic on this commit; both
  were reproduced locally.
- **Resolving conflicts on #2789, #2826, #2896 or #2307.** 486, 73, 75 and 115 hand-written. Re-cut
  or close.
- **Majors before the freeze.** #3058 (`lucide-react` 0 → 1) and #2587 (`react-router`) wait.
  **#3090 is what a dependabot major costs when it is merged on the gate's word alone.**
- **New product surface.** The `program-state.yml` `scope_gate` is explicit, and nine rows are in
  progress or awaiting acceptance across three waves.
- **Untracking the built asset tree.** `PRD-013` and `PRD-018` both require one tracked canonical
  tree.

## Method caveats for whoever runs this next

**The Actions API hands you stale pages, and the shape of the staleness moved.** The predecessor
found that `production-certification.yml` with `status: completed` returned fresh data. Today that
same call returned `total_count: 111` with run **#111 (2026-09-29)** as newest, while the true head
run is **#178 (today)**. `meridian-ci.yml` was stale on **three** filter combinations
(`status: completed`, `event: push`, and `branch: main` + `event: push`), each returning #4603 from
2026-09-29. Pagination is also unreliable: page 2 of a repo-wide listing jumped to **May 2026** and
the reported `total_count` changed between pages.

**What worked today:** a repo-wide `list_workflow_runs` with **no workflow id** and
`branch: main` returned the current head's runs for every workflow in one call. Use that, and
**always cross-check the newest run's `created_at` against the head commit's date** before drawing a
conclusion from a listing.

Also still true, and compounding: read **job logs**, not step `conclusion` — in the dependency job
both gate steps report `conclusion: success` while the job fails, because they are
`continue-on-error`.

**A determination PR must declare a phase when it is opened.** `tools/roadmap/enforce_phase_scope.py`
reads `github.event.pull_request.body` from the triggering event, and the `pull_request` trigger
includes neither `edited` nor `labeled` — so adding the marker to an open PR's body fires nothing,
and re-running the failed job replays the pre-edit payload. Declare it on open, or spend a commit.
A docs-only determination under `docs/product/plans/**` is PR1.

## The bar for the next determination

The predecessor's bar was right in form and should be kept, with the content updated to what is
actually blocking:

**Write the next one when `Meridian CI / quality-gate` is green on `main` again, or when the RC
lands.** Both P0 items above are bounded engineering with the fix already identified — one of them is
an open PR. If a tenth determination opens on a red `quality-gate` with the browser workstation still
unbuildable, the thing to examine is why a two-file dependabot major merged into an authoritative
gate and sat there for three more merges, not the backlog.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0** | Complete the Tailwind 4 migration (3 files: `@tailwindcss/postcss`, `postcss.config.cjs`, hoisted `@import` in `index.css`), then screenshot-diff the workstation | `quality-gate` is red on `main`; the dashboard neither builds nor tests; 318 of 326 test files never run. Measured: build green, 3,551 tests pass, 4 of 5 CSS chunks byte-identical, main stylesheet +34% |
| **P0** | Merge #3091 (`source-map-js` 1.2.1 → 1.2.2) | The certification lane's only failing gate. **Patched upstream, inside the range `postcss` already requires, PR already open.** Zero engineering |
| **P0** | **Do not** register the `braces` acceptance; close the `known-vulnerabilities.md` section as resolved by removal | The package left the tree with Tailwind 3. A stale acceptance entry fails the gate by design |
| **P1** | Then: signing secrets, the ~1 GB installer decision, freeze a green head, tag `v0.1.0-rc.1` | Unchanged and not engineering work. Now blocked on two named fixes rather than on a decision nobody was making |
| **P1** | Close #2789 (**574 conflicts, 486 hand-written**); re-cut or close #2307, #2896, #2826 | All four decayed by one to two orders of magnitude in a day. #2826's "trivially mergeable" rating is void |
| **P1** | Take or decline `W9-CORPACT-011` (**14 days**) | Zero engineering; precondition met on PostgreSQL |
| **P1** | Drain the 6 clean PRs and the 8 generated-only ones once `quality-gate` is green | A green gate is what makes the pre-merge signal mean anything again |
| **P2** | Refresh `W5X-OEG-001` (**113 days**), `W8-WPF-PARITY-001` (**92**), `W8-UX-CONSOL-001` (**79**) | `W5X-OEG-001` is the registry's oldest row and no predecessor has raised it. Record the browser-lane break on the W8 rows |
| **P2** | Continue `W10-LOT-002` via #3093 | The only `critical` row; successor mutations verified absent again today |
| **P2** | Reconcile `W10-RECON-001` (`planned` since 2026-07-31, rank 2) | Six W10 rows share that review date |
| **P3** | Implement the `.gitattributes` merge driver / documented regenerate-on-merge | 12 of 14 conflicted PRs depend on it — and it cannot work on the bundle until the P0 lands |
| **P3** | Require a human-reviewed migration note before a dependabot **major** can merge | #3090 is the specific cost: two files, one major, an authoritative gate red for three subsequent merges |
| **P3** | Gate link integrity in CI | Unchanged; worth more now that the checker emits no false positives |

## On archiving the predecessors

Convention (`docs/product/plans/README.md`) says to move a superseded determination into
`archive/docs/plans/`. The 2026-09-27 document is again **left in place**: `program-state.yml:43`
cites it by path as the recorded operator-session plan and the tracker deep-links its section
anchors. Archiving 2026-09-27, 2026-10-02, 2026-10-04 and 2026-10-05 with the `program-state.yml`
and tracker references updated in the same pass remains a separate governance-reviewed step. Five
determinations are now live in `docs/product/plans/`; that number is itself worth a decision.
