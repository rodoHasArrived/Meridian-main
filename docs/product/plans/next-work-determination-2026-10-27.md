# What To Work On Next — Meridian (2026-10-27)

**Snapshot:** Dated snapshot (latest at time of writing) - the roadmap registry (`docs/roadmap/README.md`) is authoritative for current status.

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-27
**Baseline:** `origin/main` at `4fb27518f844f383ce3e9217005c399d2d21f229` (2026-10-27 15:04 UTC)
**Supersedes:** the ranking carried by the merged
[2026-10-25 determination](next-work-determination-2026-10-25.md), whose bar is **not** met —
`v0.1.0-rc.2` is not tagged and the signing secrets have not been refused. It does not supersede
the five unmerged determinations #3207, #3223, #3249, #3259 and #3264, which the process section
addresses.
**Method:** read every `Meridian CI` and `Production Certification` `push` run on `main` by
`head_sha` plus the other six lanes that ran on the current head; read the trigger blocks of the two
lanes that did not run on it; listed every `Release Readiness` run and the per-job, per-step
conclusions and summary-job log of the most recent one; read
`build/scripts/ci/check-release-signing.ps1` and both workflows that call it; re-measured all 46
open pull requests with `git merge-tree --write-tree --name-only origin/main refs/remotes/pr/<n>`
at both this head and the predecessor's, counting `CONFLICT (...)` messages *and* conflicted-file
entries separately and classifying each path against the resolver's `GENERATED_FILES`,
`WORKSTATION_ROOT` and `REVIEWED_MANIFEST`; parsed `roadmap-items.yml` with `yaml.safe_load`;
re-counted the `tests/scripts` temporary-directory call sites; read `generated_repair.py` and the
`Generated Repair Publisher` run history; read the declared `typescript` peer ranges in #3277's own
lockfile and grepped the repository for `tsserver`.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; `docs/product/implementation-todo-list.md`
> remains authoritative for the P0 release gate. Recheck CI results and conflict counts against the
> live head before acting.

## The decisive change: `main` is rc.2-eligible for the first time

`4fb27518` is the first `main` head in this series green across **both** gating lanes:

| Head | Time (UTC) | Meridian CI | Production Certification |
| --- | --- | --- | --- |
| `4fb27518` (**current head**) | 2026-10-27 15:04 | **success** | **success** |
| `2b5630fa` | 2026-10-27 15:04 | cancelled (superseded) | failure |
| `65e65ed3` | 2026-10-26 03:16 | success | failure |
| `e9ab984d` | 2026-10-25 08:31 | success | failure |
| `65dbf4f9` | 2026-10-25 07:56 | success | failure |

`Production Certification` was red on **eight consecutive `main` pushes**, from `b020b537`
(run 307, 2026-10-23 21:43) through `2b5630fa` (run 315, 2026-10-27 15:04) — the last green before
that being `6ca5515b` (run 306) on 2026-10-23 15:45 — all on the same unaccepted high `zod`
advisory. The 2026-10-25 determination's two-file override cleared it, and the lane passed on the
first head that carried the fix. `2b5630fa` and `4fb27518` were pushed in the same minute; only the
second carried the override, and only the second is green.

Inside `Meridian CI`, **all seven jobs** are green on this head: `verify-docs`, `verify-dotnet`,
`verify-browser`, the service-backed PostgreSQL integration job, `verify-workflows`,
`integration-gate` and `quality-gate`. Five further lanes are green as well — `CodeQL`, `CI`,
`Golden Path Validation`, `Roadmap Source Docs` and `Documentation Automation`. `Windows Desktop
Build` and `Pipeline Budget Benchmark` did not run: both are `paths`-filtered and this head touches
none of their trigger paths.

**So the tracker's `v0.1.0-rc.2` precondition — "a green `main` head" — is satisfied right now.**
Every engineering blocker named by the last six determinations is closed.

## …and the right next action is *not* to tag it

This is the most useful finding in this document, because the obvious move is the wrong one.

`desktop-installer-packaging.yml:67` runs `build/scripts/ci/check-release-signing.ps1` in the
protected `desktop-release-signing` environment. That script's first act is:

```powershell
if ([string]::IsNullOrWhiteSpace($env:SIGNING_PFX) -or [string]::IsNullOrWhiteSpace($env:SIGNING_PASSWORD)) {
    throw 'Signing secrets are unavailable.'
}
```

with `problem = 'signing-secrets-unavailable'` already seeded into the receipt, and it exits `1`
before any packaging job runs. That is exactly how `v0.1.0-rc.1` was lost: pushed on `c51c5d8d`,
tag run failed closed at eligibility, tag immutable, cannot certify. The readiness tracker already
records the constraint in the `PRD-014` row — the candidate is cut from a green head *"once the
`desktop-release-signing` secrets exist"* — and the repository still holds exactly two tags,
`v0.1.0-rc.1` and `eval-v0.1.0-eval.1`.

**Tagging `v0.1.0-rc.2` today would burn a second immutable release tag for the same reason as the
first.** The ordering is secrets first, tag second, and that ordering is now the whole release
path.

## P0: the one human action, with a one-day-old receipt

`MDC_SIGNING_CERT_PFX_BASE64` and `MDC_SIGNING_CERT_PASSWORD` are still not provisioned. The
newest independent evidence is one day old and did not require anyone to spend a tag:
`Release Readiness` run [40294900031](https://github.com/rodoHasArrived/Meridian-main/actions/runs/40294900031)
(2026-10-26 08:49 UTC) failed with `check-protected-signing-readiness` aborting in **zero seconds**
at the certificate-inspection step, and its summary job printing:

```text
Release readiness: blocked; full report: artifacts/release-readiness/report.json
```

Two facts about that lane are worth acting on rather than re-reading:

- It is the **non-destructive** way to test the secrets. It carries `workflow_dispatch`, inspects
  the certificate without persisting its private key, and retains a 90-day non-secret validity
  artifact. Whoever provisions the secrets should dispatch it to confirm **before** a tag is
  pushed; whoever decides to refuse them has a dated receipt to attach to the refusal.
- It has run **twice in its history**, both on the weekly `cron`. For the one prerequisite now
  gating the entire release, a weekly unattended check is the only instrumentation.

This item has been named by six consecutive determinations. It is the sole remaining blocker to a
signed artifact, and no engineering moves it.

## P0: the one code item left on the tracker

`PRD-002`'s remaining clauses are unchanged: browser and remaining WPF/setup/default-runtime
credential ownership across the ADR-023 seams (`settings-screen.tsx` provider-wide save/delete,
`AddProviderWizardPage` unscoped save, `ProviderSetupService.ConfigureAsync`, the single
provider-wide `IProviderCredentialResolver`), then legacy ownership/verification migration,
rollback and rotation proof. It remains the tracker's only `Implementation-in-progress` P0 — the
only one that is code rather than a signature — and has not moved since the concurrent-configuration
and credential/audit clauses closed on 2026-10-20.

## P1: the conflict tax is flat, and 82% of it sits on five pull requests

Re-measured against `4fb27518`. **45 of 46 open PRs measured** (#3263's head ref is still not
fetchable): **34 conflict**, **452 `CONFLICT` messages** over **833 conflicted-file entries**.

| Measure | `e9ab984d` (predecessor) | `4fb27518` (now) | Change |
| --- | --- | --- | --- |
| Conflicting PRs | 34 of 45 | 34 of 45 | — |
| `CONFLICT` messages | 449 | 452 | +3 |
| Conflicted-file entries | 830 | 833 | +3 |
| Resolver-covered share | 94.6% | **94.2%** | −0.4 pt |

The predecessor's figures reproduce exactly at its own baseline, so the "three consecutive
increases" trend is real but has now gone flat: +3 in two days, against +61 and +82 before it. The
composition holds a sixth time — 785 of 833 conflicted files are generated output, workstation
bundle, or the reviewed hash manifest, all already resolver-covered.

**What is new is how concentrated the load is.** Five long-open PRs carry it almost entirely:

| PR | Conflicted files | of which `wwwroot/workstation/` bundle assets |
| --- | --- | --- |
| #3169 | 150 | 142 |
| #3168 | 147 | 142 |
| #3119 | 145 | 141 |
| #2896 | 140 | 135 |
| #3004 | 134 | 124 |
| **Five PRs** | **716 (86%)** | **684 (82% of all 833)** |

The remaining 29 conflicting PRs carry 117 conflicted files between them — a median of 3. And the
mechanism is visible in the conflict kinds: on #2896, 42 of 64 messages are `rename/rename` and 8
more are `rename/delete`, which is what Vite content-hashed asset filenames do when both sides
rebuild the bundle. These are not competing edits; they are two regenerations of the same output.

**This changes the shape of the P1, not its direction.** Running the resolver across the open queue
is still the cheapest item in this document, but it does not need to be a queue-wide campaign:
running it on those five PRs alone clears 82% of the entire tax. The resolver is correct, covers
every one of the top five paths, and has still never been run across the queue.

**The hand-written hotspots — 48 entries, 5.8%.**

| Path | Conflicts on | Change |
| --- | --- | --- |
| `docs/architecture/security-master-extensibility-review.md` | 7 PRs | — |
| `docs/product/plans/README.md` | **5 PRs** | — |
| `database/manifest/contracts.json` | **4 PRs** | up from 1 |
| `docs/status/wpf-screen-development-tracker.{json,md}` | 2 PRs each | — |
| `build/config/contracts/type-parity-registry.json` | 2 PRs | — |
| `src/Meridian.Infrastructure/README.md` | 2 PRs | — |

Both of the top two are manufactured by scheduled lanes appending to one hand-written index — the
five conflicting `plans/README.md` PRs are the five unmerged determinations, and this document adds
a sixth row to the same file. `database/manifest/contracts.json` is the new riser, from 1 PR to 4 in
two days, carried by the Security Master contract work (#3245, #3260 and the review stack).

**Method note for the next pass, because it nearly produced a wrong answer here.** This measurement
requires a full clone. In the shallow checkout these sessions start with (273 commits of `main`),
`git merge-tree` aborts with `fatal: refusing to merge unrelated histories` on 13 of the 45 PRs and
silently reports 99 conflicts instead of 452 — a four-fold under-count that looks like a dramatic
improvement. Run `git fetch --unshallow` before measuring. Count conflicted-file entries rather
than `CONFLICT` messages: `rename/rename` and `rename/delete` messages name two paths each, so the
message count under-states files by roughly half on bundle-heavy PRs.

## P1: the repair automation is still broken and still accelerating

`Generated Repair Publisher` is at **625 runs**, up from 595 two days ago — 30 runs in two days.
Run 625 fired on the current head at 16:00 UTC today and **failed**. `generated_repair.py:212` still
raises `"Target run is not bound to its base SHA"`, #3232 — which re-architects this surface — is
still unmerged and still carries 5 conflicts of its own on paths its own resolver covers, and
`doc-health-dashboard.{json,md}` still conflict on 27 of the 45 measured PRs each, recurring by
construction.

## P1: the two `verify-workflows` races remain unfixed

Re-counted and unchanged for ten days: `tests/scripts` holds **321 `tempfile.TemporaryDirectory(`
call sites across 98 files** with **zero** using `ignore_cleanup_errors`, and the
cancellation/allocation reconcile window in `tools/schema_control/disposable.py` is untouched.
Latent while the lane is green — and `verify-workflows` *is* green on this head — but this is the
mechanism
by which it reddens the release branch with no code defect behind it, which now matters more, not
less: a spurious red on the next `main` push costs the rc.2 window.

The `pwsh`-preflight recommendation carries forward intact, including the predecessor's own
correction: a preflight that fails the lane **once** with an accurate message, never a `skipUnless`
guard, which `AGENTS.md:58` forbids and which its own evidence showed would have skipped nothing.

## P2: the dependency queue grew by seven, and two of them need a decision

Seven dependabot PRs opened on 2026-10-26 (#3274–#3280). All seven are **conflict-free** against
this head, so they are the only part of the queue that can merge without a resolver pass. Two
should not be merged as they stand:

- **#3277 (`typescript` 5.7.2 → 7.0.2) — hold or close.** Every `typescript-eslint` 8.71.2 package
  in the lockfile declares `peerDependencies.typescript: ">=4.8.4 <6.1.0"`, and `7.0.2` is outside
  it. The PR's own lockfile shows what npm did about that: instead of satisfying the range it
  nested a duplicate `typescript-eslint` subtree under
  `node_modules/typescript-eslint/node_modules/`. TypeScript 7 is also a different distribution —
  twenty per-platform native binary packages, and the `tsserver` bin is gone (only `tsc` remains).
  The missing `tsserver` is *not* itself a blocker: nothing in `package.json`, the dashboard
  scripts, the workflows or `scripts/` invokes it. The unsatisfied peer range is the blocker, and
  it needs a `typescript-eslint` release that accepts 7.x before this can land.
- **#3278 (`lucide-react` 0.468.0 → 1.61.0) — needs a build and visual check.** A major jump on a
  declared *runtime* dependency that reaches the shipped workstation bundle, unlike every other
  open dashboard bump. Not an auto-merge.

The rest are routine. **#3275** is the safe one and the best merge candidate in the whole open
queue: `postcss` 8.5.31 and `typescript-eslint` 8.72.0, already rebased onto the `zod` override,
zero conflicts.

**The `vitest` item is still open, and #3275 does not contain it.** `vitest` stays at `4.1.11`
there, so the two moderate `@vitest/mocker` advisories (`<4.1.12`, non-major fix) are untouched.
They sit below the npm-audit lane's `high` fail threshold, so they gate nothing — which is why this
is a P2 and why it stays out of this document rather than being bundled into it.

## What has not moved: the product lane, now ungated

No product-status movement. `roadmap-items.yml` parses to **48 rows: 17 `done`, 15 `planned`,
9 `accepted`, 7 `in_progress`, 0 `ready_for_acceptance`** — identical to the last three
determinations. The seven `in_progress` rows are the two W8 UI-lane rows (`W8-WPF-PARITY-001`,
`W8-UX-CONSOL-001`, both `on_track`) plus the five W10 depth rows (`W10-MARK-001`, `W10-PROV-001`,
`W10-JRNL-001`, `W10-SEAM-001`, `W10-LOT-002`, all `green`).

The user-requested queue is unchanged: **`W10-LOT-002`, then `W10-MARK-001`, then `W10-SEAM-001`,
before `W10-RECON-001`** in that queue only. The 2026-10-25 determination said this lane becomes
the one to return to "as soon as `rc.2` is cut". That condition has effectively arrived from the
engineering side: the release path now contains nothing but a human decision, and holding product
work behind it buys nothing. **This is the lane to work.**

## Process: a sixth consecutive recommendation, and the first one with nothing to land behind it

The 2026-10-25 determination merged quickly (#3270) and its fix did exactly what it predicted —
first green `Production Certification` on the first head carrying it. That is the cleanest result
this series has produced, and it came from landing a change rather than ranking it.

This determination deliberately lands nothing, and the reason is the point: **there is no
engineering item left on the release path to land.** The two candidate code items both fail the
test of belonging in a prioritization PR — the `vitest` bump gates nothing at the lane's `high`
threshold, and the `ignore_cleanup_errors` change touches 321 call sites across 98 files and
deserves its own review rather than a ride on a document.

Open determination PRs are unchanged at five: **#3207** (10-18), **#3223** (10-19), **#3249**
(10-21), **#3259** (10-22), **#3264** (10-23). The security-master stack is unchanged at nine:
**#3111, #3188, #3196, #3220, #3234, #3240, #3247, #3258, #3263**. **Fourteen of the 45 measured
open PRs are unmerged scheduled-document output**, and they remain the queue's two largest
hand-written conflict sources.

The standing recommendation therefore repeats for a sixth time, and the evidence for it is now
arithmetic rather than argument: six determinations have named the signing secrets, and the single
act that changed the release state was a two-file dependency override. **Close the superseded
determinations unmerged, merge or close the security-master stack, and reduce the cadence of both
scheduled passes.**

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0** | **Provision `MDC_SIGNING_CERT_PFX_BASE64` and `MDC_SIGNING_CERT_PASSWORD`** in the protected `desktop-release-signing` environment — or formally refuse them | `PRD-014`. With `main` green on both gating lanes, this is the **only** thing between the program and a signed artifact, and the only item here no engineering can move. Named by six consecutive determinations. `Release Readiness` run 40294900031 (2026-10-26) is a one-day-old receipt of its absence |
| **P0** | **Do not tag `v0.1.0-rc.2` until those secrets exist** — then dispatch `Release Readiness` to confirm, and only then push the tag | `check-release-signing.ps1` fails closed with `signing-secrets-unavailable` before any packaging job, which is exactly how `rc.1` became an uncertifiable immutable tag. The green head satisfies the precondition; the secret is the gate. Tagging now burns a second tag |
| **P0** | **`PRD-002`'s remaining clauses** — browser/WPF/setup/default-runtime credential ownership; legacy ownership/verification migration, rollback and rotation proof | Still the tracker's only code-rather-than-signature P0, unchanged since the 2026-10-20 closures |
| **P1** | **Return to the product lane: `W10-LOT-002` → `W10-MARK-001` → `W10-SEAM-001`, before `W10-RECON-001`** | The user-requested queue, no longer gated behind a red release lane. 48 roadmap rows unchanged for three determinations; nothing on the release path is engineering work any more |
| **P1** | **Run the resolver on #3169, #3168, #3119, #2896 and #3004 first** | Those five carry 684 of the 833 conflicted files (82%), 124–142 each in the `wwwroot/workstation/` bundle, mostly `rename/rename` from content-hashed asset names. 785 of 833 entries (94.2%) are resolver-covered queue-wide. The tool is correct and has never been run |
| **P1** | **Land #3232, then promote the `.gitattributes` / regenerate-on-merge item** | `Generated Repair Publisher` is at 625 runs, up 30 in two days, with run 625 failing on the current head today; `generated_repair.py:212` unchanged. `doc-health-dashboard.{json,md}` conflicts on 27 of 45 PRs — recurring by construction, now measured six times |
| **P1** | **Fix the two `verify-workflows` races** — `ignore_cleanup_errors=True` across 321 `tests/scripts` call sites; the `disposable.py` reconcile window | Unchanged for ten days. The lane is green on this head, which is precisely why a spurious red matters more now: it would cost the rc.2 window with no code defect behind it |
| **P1** | **Add a `pwsh` preflight to the script-test lane that fails once with an accurate message** — not a `skipUnless` guard | Carried forward intact, including the predecessor's own correction. CI must keep failing; only the diagnostic should change |
| **P2** | **Merge #3275; hold #3277; review #3278 with a build** | #3275 (`postcss` 8.5.31, `typescript-eslint` 8.72.0) is rebased and conflict-free. #3277 moves `typescript` to 7.0.2, outside every `typescript-eslint` 8.71.2 peer range (`>=4.8.4 <6.1.0`), which npm papered over by nesting a duplicate subtree. #3278 is a major jump on a runtime dependency inside the shipped bundle |
| **P2** | **Bump `vitest` to `4.1.12`** | Clears the two remaining moderate advisories (`@vitest/mocker`, `<4.1.12`, non-major fix). #3275 does **not** include it — `vitest` stays at `4.1.11` there. Below the lane's `high` threshold, so it gates nothing |
| **P2** | **Close the superseded determinations (#3207, #3223, #3249, #3259, #3264); merge or close the security-master stack (#3111–#3263)** | 14 of the 45 measured open PRs are unmerged scheduled-document output, and they are the queue's two largest hand-written conflict sources — `plans/README.md` on 5 PRs and `security-master-extensibility-review.md` on 7, both self-inflicted by these lanes |
| **P2** | **Reduce the cadence of both scheduled passes** | Six determinations have named the same human blocker. The one act that changed the release state was a two-file override, and this pass has no engineering item left to land |

## The bar for the next determination

**The next determination is due when `v0.1.0-rc.2` is tagged on a signed tag run, or when the
signing secrets are formally refused.**

This is the predecessor's bar, unchanged, because the predecessor's bar was right and its
engineering half is now complete. The difference is that there is no longer a second thing to look
at: if neither has happened by the next pass, the finding is not a ranking, a backlog or a
measurement. It is that a release has been held for more than a week on two repository secrets, and
the only question worth asking is who owns that decision.
