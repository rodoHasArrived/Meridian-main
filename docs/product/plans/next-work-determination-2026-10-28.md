# What To Work On Next — Meridian (2026-10-28)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-28
**Baseline:** `origin/main` at `d537b8c3e5a50e16b7954f306ede0b559e63d59e` (2026-10-28 10:23 UTC)
**Supersedes:** the ranking carried by the merged
[2026-10-27 determination](next-work-determination-2026-10-27.md), whose bar is **not** met —
`v0.1.0-rc.2` is not tagged and the signing secrets have not been refused.
**Method:** listed `git ls-remote --tags` against the live remote; read every `Release Readiness`
run in the workflow's history and the per-step conclusions of the newest run's three jobs; read
every `Meridian CI` and `Production Certification` `push` run on `main` by `head_sha` back through
the predecessor's baseline; listed the `Generated Repair Publisher` run history and grepped `build/`
and `tools/` for the base-SHA guard string; ran `git fetch --unshallow` and
`+refs/pull/*/head:refs/remotes/pr/*`, then measured all 40 open pull requests with
`git merge-tree --write-tree --name-only origin/main refs/remotes/pr/<n>`, counting `CONFLICT`
messages and conflicted-file entries separately and classifying every conflicted path by importing
`build/scripts/resolve-generated-merge-conflicts.py` and calling its own `conflict_strategy` against
`build/scripts/maintained-generation-outputs.json`; re-counted the `tests/scripts`
temporary-directory call sites; parsed `roadmap-items.yml` with `yaml.safe_load`; read the dashboard
`package.json` and the `PRD-002` seams in source.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; `docs/product/implementation-todo-list.md`
> remains authoritative for the P0 release gate. Recheck CI results and conflict counts against the
> live head before acting.

## The bar is not met, and this time the receipt is same-day

The predecessor set one bar: **`v0.1.0-rc.2` tagged on a signed tag run, or the signing secrets
formally refused.** Neither happened.

- `git ls-remote --tags origin` returns exactly two refs, unchanged for a week:
  `eval-v0.1.0-eval.1` and `v0.1.0-rc.1` (`c51c5d8d`). There is no `rc.2`.
- `MDC_SIGNING_CERT_PFX_BASE64` and `MDC_SIGNING_CERT_PASSWORD` are still absent, and the evidence
  is now hours old rather than days. `Release Readiness` run
  [40294900031](https://github.com/rodoHasArrived/Meridian-main/actions/runs/40294900031) was
  **re-run as attempt 2 today at 05:34 UTC**; `check-protected-signing-readiness` failed at the
  "Inspect certificate without persisting its private key" step in **one second**
  (05:35:15 → 05:35:16), and `report-release-prerequisites` failed behind it.
- No refusal exists. `decision-log.yml` ends at `DEC-PRD000-SUPPORT-MATRIX-001` (2026-10-20); no
  later entry refuses the secrets or rescopes `PRD-014`.

Somebody deliberately re-ran the non-destructive check this morning and it still fails closed. The
instrumentation the predecessor asked for is being used; the secrets are simply not there.

**`v0.1.0-rc.1` was pushed on 2026-10-21. The release has now been held seven days on two
repository secrets.** That is the whole finding on the release path, and no engineering moves it.

## Meanwhile the green head stopped being a one-off

The predecessor's decisive fact was a single head green across both gating lanes. It now holds
across a run of heads, which is the difference between a window and a baseline:

| Head | Time (UTC) | Meridian CI | Production Certification |
| --- | --- | --- | --- |
| `d537b8c3` (**current head**, #3310) | 2026-10-28 10:23 | queued — **at risk, see below** | queued |
| `16bf6a30` (#3232) | 2026-10-28 09:35 | **success** | **success** |
| `46052c2e` (#3274) | 2026-10-28 09:35 | **success** | **success** |
| `af7f0c6a` (#3294) | 2026-10-28 08:34 | not sampled | **success** |
| `4fb27518` (predecessor's baseline) | 2026-10-27 15:04 | **success** | **success** |

The `rc.2` precondition is therefore not a narrow window that a bad push would close. It is the
normal state of `main`. **The ordering stands unchanged: secrets first, tag second.**
`desktop-installer-packaging.yml:67` still runs `check-release-signing.ps1`, which still throws
`'Signing secrets are unavailable.'` with `problem = 'signing-secrets-unavailable'` before any
packaging job. Tagging today still burns a second immutable tag for the same reason as the first.

## The one engineering finding that is time-critical: #3310 left `main`'s generated docs stale

**The current head carries generated documentation that does not match its own source, and the
`verify-docs` job that enforces that has not reported yet.**

`#3310` ("docs: document public classes and interfaces") merged at 10:23 UTC as `1cb95287`:
**799 files changed, 4,607 insertions**, all XML doc comments — including
`src/Meridian.Wpf/ViewModels/`. Those files feed the `Render WPF UI diagrams` generator
(`npm run generate-diagrams`, step 8 of the maintained-generation profile) and the API-contract and
coverage dashboards. The PR did not regenerate them.

Running the repository's own docs lane on this head rewrites **50 declared generated outputs**: all
44 `docs/diagrams/ui*` `.dot`/`.svg` pairs, `docs/status/wpf-screen-development-tracker.{json,md}`,
`docs/status/api-contract-coverage-dashboard.{json,md}`, `docs/status/coverage-report.md` and
`docs/status/TODO.md`. The lane's final step then reports:

```text
Generated files are stale. Download generated-repair.patch from the documentation artifact ...
```

The mechanism is visible in one line of the diff — the generator stamps its inputs:

```diff
-  generated [... label="Source fingerprint: cdec2e5a7f36\nWorkspaces: 7\nRegistry pages: 98"];
+  generated [... label="Source fingerprint: 5dcbf7e68964\nWorkspaces: 7\nRegistry pages: 98"];
```

**Attributed by bisection, not inference.** In a detached worktree at `16bf6a30` — the parent head,
green on both lanes — running the same generator reproduces the committed bytes exactly: the
fingerprint stays `cdec2e5a7f36` and every rendered output reports `unchanged`. The drift appears
only once `#3310` is in the tree. That bisection also establishes that the local toolchain renders
byte-identically to the committed output, so regenerating is a mechanical repair, not a re-render
under a different graphviz.

Every one of the 50 paths is a declared generated output under
`build/scripts/maintained-generation-outputs.json` (checked against its `whole_files`,
`whole_trees`, `protected_suffixes` and `hybrid_files` sets); no hand-written file is involved.

**Why this is the one item that cannot wait for the weekly cadence.** The four-head green streak is
what makes `main` `rc.2`-eligible, and `verify-docs` is one of the seven `Meridian CI` jobs. This is
the same failure mode that made `v0.1.0-rc.1` uncertifiable: `c51c5d8d`'s `Meridian CI` was red on
`verify-docs` from source-hash drift the #3239/#3243 merge left behind, and the tag workflow's
packaging jobs depend on that reusable job. A stale-docs red on `d537b8c3` does not merely cost a
cycle — it removes the green head the release is waiting on, for a reason that is one command to
fix.

The hosted verdict is still pending (`Meridian CI` run 5990 was `queued` at the time of writing).
The prediction is testable: if that run goes red, `verify-docs` is why. Either way the repair is the
same and the drift is real on the current head.

## Three of the predecessor's P1 items actually closed

This is the first pass in the series where acting on the ranking, not just restating it, moved
several items at once.

**`#3232` merged (`16bf6a30`), and the repair automation is green.** `Generated Repair Publisher`
runs 711 through 717 on that head all conclude `success`, and the string
`"Target run is not bound to its base SHA"` no longer appears anywhere under `build/` or `tools/`.
The lane that had never produced a green run in its history now produces nothing else.

**The five superseded determination PRs are closed.** #3207, #3223, #3249, #3259 and #3264 are all
gone from the open queue, and the effect is measurable: `docs/product/plans/README.md` has dropped
out of the hand-written conflict hotspots entirely, from five PRs to zero. The predecessor
diagnosed that hotspot as self-inflicted by this lane; closing the stale copies fixed it.

**The dependency holds resolved the way the predecessor ranked them.** #3277 (`typescript` 7.0.2,
outside every `typescript-eslint` peer range) is closed. #3278 landed — `package.json` now reads
`lucide-react 1.61.0`. #3274, #3275 and #3276 merged, and the `zod 4.6.7` override is in place.

## The conflict tax fell 24%, and is now 89% concentrated

Re-measured against `d537b8c3` on an unshallowed clone. **All 40 open PRs measured** — including
#3263, which was unfetchable last pass.

| Measure | `4fb27518` (predecessor) | `d537b8c3` (now) | Change |
| --- | --- | --- | --- |
| Open PRs measured | 45 of 46 | **40 of 40** | all measurable |
| Conflicting PRs | 34 of 45 | **20 of 40** | −14 |
| `CONFLICT` messages | 452 | 471 | +19 |
| Conflicted-file entries | 833 | **632** | **−201 (−24%)** |
| Resolver-covered share | 94.6% | 93.2% | −1.4 pt |
| Hand-written (manual) entries | 48 | **43** | −5 |

The first real decrease in the series, and it came from closing stale PRs rather than from any
merge-strategy change.

**The concentration tightened rather than dispersing.** The same five long-open PRs now carry
almost the whole remainder:

| PR | `CONFLICT` msgs | Conflicted files | Resolver-covered |
| --- | --- | --- | --- |
| #3169 | 87 | 118 | 116 |
| #3168 | 85 | 116 | 115 |
| #3119 | 82 | 114 | 113 |
| #2896 | 77 | 109 | 109 |
| #3004 | 71 | 106 | 102 |
| **Five PRs** | **402** | **563 (89%)** | **555** |

563 of 632 conflicted files, 555 of them resolver-covered, on five PRs untouched since 2026-10-16
and 2026-10-19. The remaining 15 conflicting PRs carry 69 files between them. **Running the
resolver on those five alone clears 89% of the tax** — up from 82% last pass, on a smaller total.
The tool is correct, covers these paths, and has still never been run across the queue.

**The hand-written load has one owner now.** Of the 43 manual entries, **#3245 carries 19 (44%)**,
and they are genuine C# conflicts the resolver covers none of:

| Path | Conflicts on | Change |
| --- | --- | --- |
| `docs/architecture/security-master-extensibility-review.md` | 4 PRs | down from 7 |
| `src/Meridian.Application/SecurityMaster/ISecurityResolver.cs` | 2 PRs | new |
| `src/Meridian.Application/SecurityMaster/SecurityResolver.cs` | 2 PRs | new |
| `src/Meridian.Contracts/SecurityMaster/ISecurityMasterQueryService.cs` | 2 PRs | new |
| `docs/status/wpf-screen-development-tracker.{json,md}` | 2 PRs each | — |
| `build/config/contracts/type-parity-registry.json` | 2 PRs | — |
| `src/Meridian.Infrastructure/README.md` | 2 PRs | — |
| `database/manifest/contracts.json` | 1 PR | down from 4 |

#3245 ("Decouple execution and backtesting Security Master contracts from Application") has 23
conflicted files of which only 4 are resolver-covered. It is seven days old and is the single
largest hand-written conflict source in the repository. It needs a person to rebase it or a
decision to close it; no automation will help.

## The `verify-workflows` race surface is growing faster than anyone is fixing it

Re-counted on this head: `tests/scripts` now holds **360 `tempfile.TemporaryDirectory(` call sites
across 114 files**, with **zero** using `ignore_cleanup_errors` — up from **321 across 98 files**
yesterday. That is **+39 call sites and +16 files in one day**.

The lane is green, so this is latent. But the previous pass argued a spurious red matters more once
`main` is otherwise clean, and that argument is now stronger in both terms: `main` is green across
four heads, and the surface that can produce a spurious red grew 12% in a day. The
`tools/schema_control/disposable.py` cancellation/allocation reconcile window is likewise unchanged
— its last commit, `bcacfc2fb` ("Wait for owned container creation before cancellation cleanup"),
predates the finding and addresses a different window.

The `pwsh`-preflight recommendation carries forward intact, including its own correction: a
preflight that fails the lane **once** with an accurate message, never a `skipUnless` guard, which
`AGENTS.md:58` forbids.

## The dependency queue reopened with four majors, and one of them is the wrong fix

Ten dependabot PRs opened today (#3299–#3309), on top of #3279 and #3280. Four are majors that need
a decision rather than a merge, and the dashboard's current pins are the context:

- **#3300 (`vitest` 4.1.11 → 5.0.5) — do not take this as the advisory fix.** The two moderate
  `@vitest/mocker` advisories are fixed in **4.1.12**, a patch. #3300 overshoots it by a major
  version of the test runner. Take `4.1.12`; treat 5.0.5 as a separate, deliberate upgrade.
- **#3302 / #3303 (`react` and `react-dom` 18.3.1 → 19.x) — hold pending a build and visual check.**
  These are runtime dependencies that reach the shipped `wwwroot/workstation/` bundle. Same class as
  #3278, which was correctly held for a build last pass.
- **#3299 (`@types/node` 20.19.0 → 26.6.5)** and **#3301 (`jsdom` 25.0.1 → 30.1.3)** are
  dev-only but six and five majors wide respectively; both need a typecheck and a full dashboard
  test run before merge, not an auto-merge.

#3304 through #3309, #3307, #3279 and #3280 are routine point bumps.

## New this pass: the repair publisher's cost moved from failures to volume

`Generated Repair Publisher` is at **727 runs**, up from **625** yesterday — **102 runs in roughly
19 hours**, against ~15/day before. At the time of writing, runs 722 through 727 are all **queued
simultaneously on the same head** (`d537b8c3`), most triggered by `workflow_run` completions from
dependabot activity.

The lane is green, so this is not the old problem. It is a new one: a `workflow_run` trigger that
fans out once per upstream completion, on a day with ten dependabot PRs, produces six concurrent
runs that will each repair the same head. Worth a concurrency group or a trigger narrowing before
the next dependabot batch, and worth recording now while the cause is visible.

## What has not moved: the product lane, still ungated

No product-status movement for a fourth consecutive determination. `roadmap-items.yml` parses to
**48 rows: 17 `done`, 15 `planned`, 9 `accepted`, 7 `in_progress`, 0 `ready_for_acceptance`**. The
seven `in_progress` rows are the two W8 UI-lane rows (`W8-WPF-PARITY-001`, `W8-UX-CONSOL-001`, both
`on_track`) plus the five W10 depth rows (`W10-MARK-001`, `W10-PROV-001`, `W10-JRNL-001`,
`W10-SEAM-001`, `W10-LOT-002`, all `green`).

`PRD-002` is likewise unchanged and remains the tracker's only `Implementation-in-progress` P0. The
single provider-wide resolver named in its remaining clause is still exactly that in source:
`IProviderCredentialResolver` is declared at `ProviderFactory.cs:491` and registered once as
`EnvironmentCredentialResolver` at `ProviderServiceExtensions.Composition.cs:43`.

The user-requested queue is unchanged: **`W10-LOT-002`, then `W10-MARK-001`, then `W10-SEAM-001`,
before `W10-RECON-001`** in that queue only.

**This is the lane to work, and the case is now stronger than when the predecessor said so.** Then,
the argument was that nothing on the release path was engineering work. Now three of the
predecessor's own engineering P1s have closed, `main` is green across four heads, and the release
path contains one human signature that has been outstanding for a week. There is no engineering
reason left to hold product work, and holding it buys nothing.

## Process: the predecessor's recommendation worked, so the cadence question is now answered

The 2026-10-27 pass recommended closing the superseded determinations and reducing the cadence of
both scheduled passes. The first half was done, and it produced the first measured decrease in the
conflict tax in the series — −201 conflicted files, and one hotspot eliminated outright.

That is evidence for the second half. **Seven consecutive determinations have now named the same
human blocker.** The release state has been changed exactly once in that span, by a two-file
dependency override. This pass found real new information — a 24% conflict drop, #3232 landing,
#3245 emerging as the hand-written conflict owner, a growing race surface, a trigger fan-out, and
four dependency majors — but none of it is release-path work, and none of it required a daily pass
to find.

Open determination PRs are back to zero. The security-master stack is down to six: **#3111, #3188,
#3196, #3240, #3247, #3263**, plus the related #3245. **A weekly cadence would have caught
everything in this document.**

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0** | **Provision `MDC_SIGNING_CERT_PFX_BASE64` and `MDC_SIGNING_CERT_PASSWORD`** in the protected `desktop-release-signing` environment — or formally refuse them and rescope `PRD-014` | The only thing between the program and a signed artifact, and the only item here no engineering can move. Named by seven consecutive determinations. `Release Readiness` run 40294900031 attempt 2 failed at the certificate step in one second **today at 05:35 UTC** — a same-day receipt from a deliberate re-run |
| **P0** | **Do not tag `v0.1.0-rc.2` until those secrets exist** — then dispatch `Release Readiness` to confirm, and only then push the tag | `check-release-signing.ps1` still fails closed with `signing-secrets-unavailable` before any packaging job, which is exactly how `rc.1` became an uncertifiable immutable tag. `main` is green across four heads, so the precondition is a baseline, not a window — there is no rush that justifies spending a second tag |
| **P0** | **`PRD-002`'s remaining clauses** — browser/WPF/setup/default-runtime credential ownership; legacy ownership/verification migration, rollback and rotation proof | Still the tracker's only code-rather-than-signature P0. Verified unchanged in source: one provider-wide `IProviderCredentialResolver` at `ProviderFactory.cs:491`, registered once at `ProviderServiceExtensions.Composition.cs:43` |
| **P0** | **Regenerate `main`'s stale generated documentation** — `python build/scripts/run-maintained-generation.py --docs-only`, then commit the 50 declared outputs | #3310 changed 799 source files without regenerating the WPF UI diagrams, screen tracker, API-contract and coverage dashboards. Bisection at `16bf6a30` shows the generator reproduced committed bytes before #3310 and rewrites 50 outputs after it. `verify-docs` is one of the seven `Meridian CI` jobs, and stale-docs red on `verify-docs` is exactly what made `rc.1` uncertifiable. One command; removes the rc.2-eligible head if left |
| **P1** | **Work the product lane: `W10-LOT-002` → `W10-MARK-001` → `W10-SEAM-001`, before `W10-RECON-001`** | The user-requested queue, fully ungated. 48 roadmap rows unchanged for four determinations; three of the predecessor's engineering P1s closed this pass; `main` green across four heads; the release path is one human signature, seven days old |
| **P1** | **Run the resolver on #3169, #3168, #3119, #2896 and #3004** | Those five carry 563 of 632 conflicted files (**89%**, up from 82%), 555 of them resolver-covered, and have been untouched since 2026-10-16/19. Five PRs and one tool clears nearly the whole tax |
| **P1** | **Rebase or close #3245** | 19 of the 43 hand-written conflict entries (44%) — real conflicts in `ISecurityResolver.cs`, `SecurityResolver.cs` and `ISecurityMasterQueryService.cs`. Only 4 of its 23 conflicted files are resolver-covered, so it is the one conflict load automation cannot touch. Seven days open |
| **P1** | **Fix the two `verify-workflows` races** — `ignore_cleanup_errors=True` across the `tests/scripts` call sites; the `disposable.py` reconcile window | Now **360 call sites across 114 files**, zero using it — **+39 sites and +16 files in one day**. Latent while green, but the surface is growing 12%/day while `main`'s cleanliness makes a spurious red costlier |
| **P1** | **Add a `pwsh` preflight to the script-test lane that fails once with an accurate message** — not a `skipUnless` guard | Carried forward intact. CI must keep failing; only the diagnostic should change. `AGENTS.md:58` forbids the guard |
| **P2** | **Bump `vitest` to `4.1.12`, and do not merge #3300 as the fix** | 4.1.12 is the patch that clears the two moderate `@vitest/mocker` advisories. #3300 jumps to 5.0.5 — a major test-runner upgrade standing in for a patch. Separate the two decisions |
| **P2** | **Hold #3302/#3303 (`react` 19) for a build and visual check; typecheck #3299 and #3301 before merge** | react/react-dom are runtime dependencies inside the shipped workstation bundle — the same class as #3278, correctly held last pass. `@types/node` 20→26 and `jsdom` 25→30 are dev-only but six and five majors wide |
| **P2** | **Add a concurrency group or narrow the `workflow_run` trigger on `Generated Repair Publisher`** | **727 runs, +102 in ~19 hours** against ~15/day before, with runs 722–727 queued simultaneously on one head from dependabot `workflow_run` fan-out. The lane is green; the cost moved from failures to volume |
| **P2** | **Reduce the cadence of both scheduled passes to weekly** | Closing the five stale determinations produced the series' first conflict decrease (−201 files, one hotspot eliminated), which is evidence the recommendation was right. Seven passes have named the same human blocker; the release state changed once, by a two-file override. Everything new in this document would have been caught weekly |

## The bar for the next determination

**The next determination is due when `v0.1.0-rc.2` is tagged on a signed tag run, or when the
signing secrets are formally refused — and not before.**

The predecessor's bar, deliberately unchanged for the third time, now with the cadence clause made
explicit. Its engineering half has been complete for two days and has since been reinforced: `main`
is green across four heads, not one. If the next pass finds the same two secrets outstanding, the
answer to "what should be worked on next" is not in this repository. It is the question of who owns
that decision — and it should be asked weekly, not daily.
