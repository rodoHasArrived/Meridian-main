# What To Work On Next — Meridian (2026-10-28)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-28
**Baseline:** `origin/main` at `79e6994fad7f31554867678616bda7d303870098` (2026-10-28 15:28 UTC)
**Supersedes:** the ranking carried by the merged
[2026-10-27 determination](next-work-determination-2026-10-27.md), whose bar is **not** met —
`v0.1.0-rc.2` is not tagged and the signing secrets have not been refused. It also supersedes the
unmerged #3313, an earlier attempt at today's pass that was closed at 11:30 UTC; the process
section explains why its figures no longer hold.
**Method:** read every `Meridian CI` and `Production Certification` `push` run on `main` by
`head_sha`; listed every `Release Readiness` run and read the per-job failure logs of the most
recent attempt; confirmed the tag set with `list_tags` and `git ls-remote`; grepped
`decision-log.yml` for any refusal of the signing secrets; re-measured all 14 open pull requests
with `git merge-tree --write-tree --name-only origin/main refs/remotes/pr/<n>` after
`git fetch --unshallow`, counting `CONFLICT` messages *and* conflicted-file entries separately and
classifying each path against the resolver's generated, workstation-bundle and manifest sets;
diffed `roadmap-items.yml` row-by-row against the predecessor's baseline `4fb27518` with
`yaml.safe_load`; verified all eleven `W10-RECON-001` evidence paths with `git cat-file -e` against
`origin/main`; re-counted the `tests/scripts` temporary-directory call sites; grepped the tree for
the `generated_repair.py` base-SHA guard string; read the four ADR-023 seams `PRD-002` names; and
ran `npm audit --json` and the dashboard suite in a clean `npm ci` tree; and bisected the
`vitest` 4.1.12 resolution failure by diffing `npm view vitest@4.1.11 peerDependencies` against
`4.1.12` and re-requesting the current pin as a control.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; `docs/product/implementation-todo-list.md`
> remains authoritative for the P0 release gate. Recheck CI results and conflict counts against the
> live head before acting.

## The bar is not met, and the release is now seven days old

Both halves fail, and the evidence is same-day rather than inferred:

- **`v0.1.0-rc.2` is not tagged.** The repository holds exactly two tags, `v0.1.0-rc.1`
  (`c51c5d8d`) and `eval-v0.1.0-eval.1` (`179090a3`) — unchanged for a week.
- **The signing secrets are not refused.** `decision-log.yml` holds 18 decisions and none refuses
  them. The only entry that names them, `DEC-PRD000-SUPPORT-MATRIX-001`, *requires* them: the
  corrected candidate is "`v0.1.0-rc.2` cut from a green `main` head once
  `MDC_SIGNING_CERT_PFX_BASE64` / `MDC_SIGNING_CERT_PASSWORD` are provisioned."
- **The absence has a receipt dated today.** `Release Readiness` run
  [40294900031](https://github.com/rodoHasArrived/Meridian-main/actions/runs/40294900031) was
  re-run as **attempt 2** at 2026-10-28 05:34:42 UTC. `check-protected-signing-readiness` failed
  at the certificate-inspection step with `Process completed with exit code 1`, uploading a
  374-byte `signing.json` and nothing else. That lane has still run only twice in its history,
  both on the weekly `cron`.

`v0.1.0-rc.1` was pushed on 2026-10-21. **The release has been held seven days on two repository
secrets.** This is the seventh consecutive determination to name them.

## `main` is still rc.2-eligible

| Lane | Run | Conclusion | Finished (UTC) |
| --- | --- | --- | --- |
| `Meridian CI` | 6013 (`40716346792`) | **success** | 2026-10-28 15:43 |
| `Production Certification` | 335 (`40716346888`) | **success** | 2026-10-28 15:46 |

The precondition the tracker names is satisfied and has been for four heads. The ordering is
unchanged and still the whole release path: **secrets first, tag second.**
`check-release-signing.ps1` throws `'Signing secrets are unavailable.'` with
`problem = 'signing-secrets-unavailable'` and exits `1` before any packaging job, which is exactly
how `rc.1` became an uncertifiable immutable tag. Tagging today burns a second one.

## The decisive change: the queue drained

Open pull requests went from **46 to 14** in one day. Almost every item the predecessor ranked was
resolved — though mostly by *merging* rather than by the mechanism it recommended:

| Predecessor item | Outcome |
| --- | --- |
| Close the five superseded determinations | **Done.** #3207, #3223, #3249, #3259, #3264 all closed unmerged |
| Merge or close the security-master stack | **Done.** #3111, #3188, #3240, #3247, #3263 merged; #3196, #3220, #3234, #3258, #3260 closed |
| Run the resolver on #3169, #3168, #3119, #2896, #3004 | **Overtaken.** All five *merged* at 11:16 UTC via #3297. The resolver still has never been run |
| Land #3232 | **Done.** Merged 09:35 UTC |
| Merge #3275, hold #3277, review #3278 | #3275 merged, #3277 **closed** — and #3278 merged, see below |
| `PRD-002` clauses | **Unmoved** |
| The two `verify-workflows` races | **Unmoved, and larger** |

### The conflict tax collapsed

Re-measured against `79e6994f`. **All 14 open PRs measured; 5 conflict.**

| Measure | `4fb27518` (predecessor) | `79e6994f` (now) | Change |
| --- | --- | --- | --- |
| Conflicting PRs | 34 of 45 | **5 of 14** | −29 |
| `CONFLICT` messages | 452 | **71** | **−84%** |
| Conflicted-file entries | 833 | **99** | **−88%** |
| Resolver-covered share | 94.2% | **97.0%** | +2.8 pt |
| Hand-written entries | 48 | **3** | **−94%** |

**88 of the 99 remaining entries sit on one PR**, #3311, and all 88 are
`src/Meridian.Ui/wwwroot/workstation/` bundle assets — the same content-hashed-filename mechanism
as before, now concentrated in a single place. The other four conflicting PRs carry 11 files
between them (#3312 five, #3314 two, #3105 two, #3175 one).

Every hand-written hotspot the series tracked for six passes is gone:

| Path | On `4fb27518` | Now |
| --- | --- | --- |
| `security-master-extensibility-review.md` | 7 PRs | **0** |
| `docs/product/plans/README.md` | 5 PRs | **0** |
| `database/manifest/contracts.json` | 4 PRs | **0** |
| `doc-health-dashboard.{json,md}` | 27 PRs each | **2** |

The three hand-written entries that remain are single-PR: `src/Meridian.Application/README.md`,
`src/Meridian.Application/FundStructure/InMemoryFundStructureService.cs` and
`src/Meridian.Ui/dashboard/README.md`.

**The resolver campaign is therefore moot, and the honest reading is that it was never the fix.**
Six determinations ranked running a tool across the queue; what actually cleared 88% of the load
was merging and closing the pull requests. The `.gitattributes` / regenerate-on-merge item still
has standing merit for the #3311 class of conflict, but it is no longer a P1 — there is not enough
tax left to justify it.

### The repair automation is fixed

`generated_repair.py:212`'s guard was the predecessor's blocker. The string
`"Target run is not bound to its base SHA"` **no longer occurs anywhere in the tree**, and
`Generated Repair Publisher` runs **756–760 are all green**, five of them on the current head.
#3232 did what it was ranked to do.

One thing did not improve: the lane is at **760 total runs, up from 625 two days ago — 135 runs in
a single day.** The publisher now succeeds; it simply runs constantly. The cadence concern the
predecessor raised is the only live issue left on that surface.

## P1: the largest remaining engineering item, and it grew

This is the one item that moved backwards.

| Measure | 10 days ago | `4fb27518` | Now |
| --- | --- | --- | --- |
| `tempfile.TemporaryDirectory(` call sites in `tests/scripts` | 321 | 321 | **368** |
| Files | 98 | 98 | **116** |
| Using `ignore_cleanup_errors` | 0 | 0 | **0** |

**+47 call sites across +18 files in two days, still none guarded.** The
cancellation/allocation reconcile window in `tools/schema_control/disposable.py` is likewise
untouched — last modified by `bcacfc2fb` on **2026-10-07**, three weeks ago.

`verify-workflows` is green on this head, which is exactly why this now matters more than when it
was ranked. It is the mechanism by which the lane reddens with no code defect behind it, and a
spurious red on the next `main` push costs the rc.2 window. With the conflict tax gone and the
repair publisher fixed, **this is the biggest piece of engineering work left anywhere in this
document**, and it is growing faster than anyone is fixing it.

The `pwsh`-preflight recommendation carries forward intact, including its own correction: a
preflight that fails the lane **once** with an accurate message, never a `skipUnless` guard, which
`AGENTS.md:58` forbids.

## P0: `PRD-002` has not moved

All four ADR-023 seams the tracker names are still present and still provider-wide:

| Seam | Current state |
| --- | --- |
| `settings-screen.tsx` provider-wide save/clear | Present — save at `:1749`/`:1765`, clear at `:1840`, UI at `:5517`/`:5545` |
| `AddProviderWizardPage` unscoped save | Present — `src/Meridian.Wpf/Views/AddProviderWizardPage.xaml.cs` |
| `ProviderSetupService.ConfigureAsync` | Present — `src/Meridian.Application/ProviderRouting/ProviderSetupService.cs:76` |
| Single provider-wide `IProviderCredentialResolver` | Present — `EnvironmentCredentialResolver` still `TryAddSingleton` at `ProviderServiceExtensions.Composition.cs:43` |

It remains the tracker's only `Implementation-in-progress` P0 and has not moved since the
concurrent-configuration and credential/audit clauses closed on 2026-10-20. Legacy
ownership/verification migration, rollback and rotation proof are still outstanding. Two open
drafts touch adjacent ground — #3296 ("scoped credentials") and #3316 — so the next push on this
row should start from them rather than from `main`.

## The product lane moved for the first time in four determinations

`roadmap-items.yml` now parses to **53 rows: 17 `done`, 19 `planned`, 9 `accepted`,
8 `in_progress`, 0 `ready_for_acceptance`** — up from 48 rows, static across the previous three
passes. Two changes, both legitimate:

**Five new `W12` planned rows**, registered under `DEC-FUND-OPERATIONS-DIRECTION-001`:
`W12-BOOK-001` (accounting book, basis and FX control workbench), `W12-CAPITAL-001` (governed
investor capital and allocation lifecycle), `W12-YEND-001` (fiscal year-end close and
opening-balance roll-forward), `W12-FINRPT-001` (ledger-backed financial statement control pack)
and `W12-CREDIT-001` (DirectLoan operations and evidence reconciliation). This is governed
registration of direction, not registry drift, and all five sit inside the program's scope gate.

**`W10-RECON-001` went `planned` → `in_progress`, health `green` → `yellow`.** Its implementation
is #3004, which **merged today** at 11:16 UTC, and all eleven evidence paths the row cites resolve
on `origin/main`. The row is yellow on its own terms: its summary records that it was
"reconstructed after workspace maintenance removed an earlier unpublished implementation", that
the earlier local test result "is not acceptance evidence for this head", and that "focused,
hosted, Windows and rendered operator verification remain required."

**One point of precision, because it is easy to get wrong.** `W10-RECON-001` opening is **not** a
breach of the user-requested queue. That override is explicit that it applies "in that queue only"
and "adds no roadmap dependency"; `RECON-001` is the roadmap's own rank-2 row, and the roadmap
sequence is what opened it. The user-requested queue is unchanged: **`W10-LOT-002`, then
`W10-MARK-001`, then `W10-SEAM-001`, before `W10-RECON-001`.**

What *is* worth acting on is that `W10-RECON-001` is now the **only non-green row** among the five
W10 depth rows, and what it needs is not code but the hosted/Windows/rendered verification its own
summary names. That is a cheap, well-specified close.

## P2: the dependency queue, and one finding worth more than the rest

Nine of the 14 open PRs are dependabot. One correction matters:

**The `vitest` advisories are fixed by a patch — but that patch is not installable as it stands,
and the reason explains why dependabot offered a major instead.** `npm audit --json` in a clean
`npm ci` tree reports two moderate advisories against `@vitest/mocker` —
[`GHSA-qxg6-89mg-rqv8`](https://github.com/advisories/GHSA-qxg6-89mg-rqv8) (code injection via
unauthenticated manual mock registration, `>=2.1.0-beta.7 <4.1.12`) and
[`GHSA-xp26-3454-mjgp`](https://github.com/advisories/GHSA-xp26-3454-mjgp) (Windows backslash
traversal bypassing the mocker redirect allowlist, `>=4.1.11 <4.1.12`) — and names the fix as
`{"name": "vitest", "version": "4.1.12", "isSemVerMajor": false}`.

That advertised non-major is a trap. `npm install vitest@4.1.12` **crashes npm 10.9.4**:

```text
TypeError: Cannot read properties of null (reading 'edgesOut')
    at #loadPeerSet (@npmcli/arborist/lib/arborist/build-ideal-tree.js:1289:38)
```

The last manifest fetched before the crash is `@vitest/browser-playwright@5.0.5`, and the cause is
a one-character change between the two releases. `4.1.11` pins every sibling peer exactly;
`4.1.12` relaxes one of them:

| Peer | `4.1.11` | `4.1.12` |
| --- | --- | --- |
| `@vitest/browser-webdriverio` | `4.1.11` | **`^4.1.11`** |

That caret admits `@vitest/browser-webdriverio` 5.x, which peer-depends on `vitest` 5.x, which
drags a whole vitest-5 peer set into the graph and sends arborist into the cycle it crashes on.
Re-requesting the currently pinned `4.1.11` resolves cleanly, so this is specific to `4.1.12`, not
to the environment or the lockfile.

**The fix is the mechanism this repository already uses.** Pinning the relaxed peer through the
dashboard's existing `overrides` block — the same instrument that cleared the `zod` advisory in
#3270 — resolves the graph and lands the patch:

```json
{
  "overrides": {
    "postcss": "8.5.31",
    "nanoid": "^3.3.18",
    "zod": "^4.6.7",
    "@vitest/browser-webdriverio": "4.1.11"
  }
}
```

With that one line beside `vitest: 4.1.12`, `npm install` succeeds, `node_modules/vitest` and its
`@vitest/mocker` both resolve to `4.1.12`, and `npm audit` reports **0 vulnerabilities** against
the previous 2 moderate. **This change is carried by this document's own branch, with evidence
below.** #3300 (`vitest` → `5.0.5`) is then unnecessary and should be closed: a two-line override
beats a major-version migration for two moderate advisories.

**#3278 (`lucide-react` 0.468.0 → 1.61.0) merged at 07:35 UTC without the check it was held for.**
The predecessor ranked it "needs a build and visual check — not an auto-merge", because it is a
major jump on a declared *runtime* dependency that reaches the shipped workstation bundle. It is
now `dependencies.lucide-react = 1.61.0` on `main`. Both gating lanes are green over it, and the
dashboard suite and production build pass on this tree (below), so there is no defect to report —
but no rendered or visual check is recorded anywhere, and the bundle is what ships to operators.
Worth one deliberate look rather than a second assumption.

The rest of the queue is four more majors that each need their own decision rather than a batch
merge: **#3299** (`@types/node` 20 → 26), **#3301** (`jsdom` 25 → 30), **#3302** (`react` +
`@types/react`) and **#3303** (`react-dom` + `@types/react-dom`). **#3304** (python tooling) and
**#3306** (`FluentValidation` 12.1.1 → 12.2.0) are routine and conflict-free.

## Process: the measurement is now churning faster than the document can merge

**#3313 is the clearest evidence yet for the cadence recommendation.** It was an earlier attempt at
today's determination, opened at 11:02 UTC against `d537b8c3` and closed unmerged at 11:30 UTC —
28 minutes later. Its substantive half, regenerating the generated documentation #3310 left stale,
was overtaken by #3297 merging at 11:16; `verify-docs` is green on the current head, so that drift
is repaired. Its ranking half is already wrong: it measured **632 conflicted files across 20 of 40
PRs**, and four hours later the true figures are **99 across 5 of 14**.

That is the argument, and it no longer needs to be made from principle. Seven determinations have
named the same human blocker. The release state has changed exactly once in that span, by a
two-file dependency override. Meanwhile a prioritization pass written in the morning is
stale by mid-afternoon. **Reduce this pass to weekly**, and keep the `Release Readiness` dispatch
as the instrumentation for the one thing that actually gates the release.

## Validation of the change this document carries

The `vitest` bump is the only code change on this branch. Evidence, all on
`79e6994f` + this branch:

| Check | Before (`vitest` 4.1.11) | After (`vitest` 4.1.12) |
| --- | --- | --- |
| `npm audit` | **2 moderate** (`@vitest/mocker` ≤4.1.11) | **0 vulnerabilities** |
| `npm --prefix src/Meridian.Ui/dashboard run test` | 4023 passed / 0 failed / 0 skipped, exit 0 | **4023 passed / 0 failed / 0 skipped, exit 0** |
| `testIdentityDigest` | `3fd5ca7baccbda2ee297a2952e33bca45dd87ca64d878825dba09cf340235d57` | **identical** |
| `node_modules/vitest` | 4.1.11 | 4.1.12 |
| `@vitest/mocker` | 4.1.11 | 4.1.12 |
| `npm --prefix src/Meridian.Ui/dashboard run build` | — | ✓ built, **`wwwroot/workstation` byte-identical to the committed bundle** |

Two points carry the most weight. The **`testIdentityDigest` is unchanged**, so the same test
identity set ran before and after — the bump did not silently drop or rename a test. And the
**built bundle is byte-identical**, which confirms `vitest` is dev-only and this change cannot
reach shipped output. That same build is the production-build half of the #3278 `lucide-react`
check noted above; the rendered/visual half is still unrecorded.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0** | **Provision `MDC_SIGNING_CERT_PFX_BASE64` and `MDC_SIGNING_CERT_PASSWORD`** in the protected `desktop-release-signing` environment — or formally refuse them and rescope `PRD-014` | Seven consecutive determinations. `main` is green on both gating lanes, the engineering queue behind this is now essentially empty, and the release has been held **seven days**. `Release Readiness` run 40294900031 attempt 2 (today, 05:34 UTC) is a same-day receipt of their absence. No engineering moves this |
| **P0** | **Do not tag `v0.1.0-rc.2` until those secrets exist** — then dispatch `Release Readiness` to confirm, and only then push the tag | `check-release-signing.ps1` fails closed with `signing-secrets-unavailable` before any packaging job. That is exactly how `rc.1` became an uncertifiable immutable tag. Tagging now burns a second one |
| **P0** | **`PRD-002`'s remaining clauses** — browser/WPF/setup/default-runtime credential ownership; legacy ownership/verification migration, rollback and rotation proof | Still the tracker's only code-rather-than-signature P0. All four ADR-023 seams verified still present and still provider-wide. Start from the open drafts #3296 and #3316, not from `main` |
| **P1** | **Fix the two `verify-workflows` races** — `ignore_cleanup_errors=True` across the `tests/scripts` call sites; the `disposable.py` reconcile window | **Now the largest engineering item in this document, and it grew: 368 call sites across 116 files, up from 321/98 in two days, still zero guarded.** `disposable.py` untouched since 2026-10-07. The lane is green, which is why a spurious red matters more — it would cost the rc.2 window with no defect behind it. Deserves its own PR |
| **P1** | **Add a `pwsh` preflight to the script-test lane that fails once with an accurate message** — not a `skipUnless` guard | Carried forward intact, including its own correction. CI must keep failing; only the diagnostic should change |
| **P1** | **Close `W10-RECON-001`'s verification gap** — focused, hosted, Windows and rendered operator verification | The roadmap moved for the first time in four passes: #3004 merged today, all eleven evidence paths resolve on `main`, and the row is `in_progress`/**yellow** — the only non-green W10 depth row. What it needs is verification, not code |
| **P1** | **The user-requested product queue is unchanged: `W10-LOT-002` → `W10-MARK-001` → `W10-SEAM-001`, before `W10-RECON-001`** | `RECON-001` opening at its roadmap rank 2 creates no dependency and no breach — the override is explicit that it binds that queue only. Nothing on the release path is engineering work any more, so this lane is unblocked |
| **P2** | **Bump `vitest` to `4.1.12` with an `@vitest/browser-webdriverio` override; close #3300** | npm advertises `4.1.12` as `isSemVerMajor: false`, but installing it **crashes npm 10.9.4** in `#loadPeerSet`: `4.1.12` relaxed that one peer from `4.1.11` to `^4.1.11`, admitting a vitest-5 peer set. Pinning it through the existing `overrides` block — the instrument that cleared `zod` in #3270 — lands the patch and takes `npm audit` from 2 moderate to **0**. **Carried by this branch**; #3300's `5.0.5` major is then unnecessary |
| **P2** | **Give #3278's `lucide-react` 1.61.0 one deliberate rendered check** | A major bump on a runtime dependency inside the shipped bundle merged today without the visual check it was explicitly held for. Tests and build pass, so this is diligence, not a defect |
| **P2** | **Decide the four open majors individually** — #3299, #3301, #3302, #3303 | `@types/node` 20→26, `jsdom` 25→30, and `react`/`react-dom` 18→. Each needs its own call, not a batch merge. #3304 and #3306 are routine |
| **P2** | **Reduce this pass to weekly; reduce the `Generated Repair Publisher` cadence** | #3313 was opened and closed inside 28 minutes today and its conflict figures were stale within four hours. The publisher is fixed but ran **135 times in one day** (625 → 760). Both passes now cost more than they inform |
| **—** | **Dropped: the resolver campaign and the `.gitattributes` item** | Six passes ranked these. The queue drained by merging instead: 833 conflicted files → **99**, 48 hand-written → **3**, and every tracked hotspot at zero. 88 of the 99 that remain sit on #3311 alone. There is no longer enough tax to justify the work |

## The bar for the next determination

**The next determination is due when `v0.1.0-rc.2` is tagged on a signed tag run, or when the
signing secrets are formally refused — and not before, on a weekly cadence at most.**

The predecessor wrote that if neither had happened, the finding would not be a ranking but a
question of ownership. That is now the whole document. The engineering queue it used to rank is
drained: the conflict tax fell 88%, the repair publisher is fixed, the stale determinations and the
security-master stack are closed, and the roadmap moved for the first time in four passes. One
genuine engineering item is left — the `verify-workflows` races, which grew — and it does not gate
the release either.

Seven days, seven determinations, two repository secrets. **The only question worth asking is who
owns that decision**, and the next pass should spend its effort on that rather than re-measuring a
queue that is now almost empty.
