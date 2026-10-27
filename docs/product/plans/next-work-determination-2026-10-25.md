# What To Work On Next — Meridian (2026-10-25)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-25
**Baseline:** `origin/main` at `e9ab984db264720e91de0053cbbbfbe5b2f4632b` (2026-10-25 08:31 UTC)
**Supersedes:** the ranking carried by the merged
[2026-10-24 determination](next-work-determination-2026-10-24.md), whose bar is **not** met —
`v0.1.0-rc.2` is not tagged and the signing secrets have not been refused. It does not supersede
the five unmerged determinations #3207, #3223, #3249, #3259 and #3264, which the process section
addresses.
**Method:** read every `Meridian CI` and `Production Certification` `push` run on `main` by
`head_sha` and the per-job conclusions of each red run; pulled the complete failing-job log for
`Production Certification` run `40176974289` on the current head; reproduced the npm-audit gate
locally at the baseline with `npm ci` + `npm audit --json` + `validate-npm-audit.py` at the CI
lane's own `--fail-level high`, then re-ran it after the fix; traced every declared dependent of
`zod` and its accepted range out of `package-lock.json`; ran the dashboard test suite and
production build and diffed the built `wwwroot` output; re-measured all 41 open pull requests with
`git merge-tree --write-tree --name-only origin/main refs/remotes/pr/<n>`, counting `CONFLICT (...)`
lines and classifying every conflicting path against the resolver's `GENERATED_FILES`,
`WORKSTATION_ROOT` and `REVIEWED_MANIFEST`; parsed `roadmap-items.yml` with `yaml.safe_load`;
re-read `generated_repair.py` and the `Generated Repair Publisher` run history.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; `docs/product/implementation-todo-list.md`
> remains authoritative for the P0 release gate. Recheck CI results and conflict counts against the
> live head before acting.

## The bar is not met, and the reason is the most useful finding here

The 2026-10-24 determination set the bar as *"`v0.1.0-rc.2` is tagged on a commit green across both
`Meridian CI` and `Production Certification`, or the signing secrets are formally refused."*
Neither happened. The repository still holds exactly two tags, `v0.1.0-rc.1` and
`eval-v0.1.0-eval.1`.

What did happen in the intervening 24 hours is worth stating precisely, because it resolves the
predecessor's three P0 items in three different ways:

| 2026-10-24 P0 item | What happened |
| --- | --- |
| Re-run the failed `verify-workflows` job on `05840a0a` | **Resolved with no action.** `Meridian CI` is green on both subsequent `main` pushes — `65dbf4f9` (run 5877) and `e9ab984d` (run 5879). The transient-`pwsh` diagnosis held; the re-run was never needed |
| Provision the `desktop-release-signing` secrets | **Unchanged.** Still the one purely human action |
| Fix the `zod` npm-audit gate | **Unchanged until this document.** `zod` was still `4.4.3`, the acceptance register was still `"accepted": []`, and `Production Certification` stayed red |

So one P0 healed itself, one is not an engineering task, and one — a two-file dependency bump the
predecessor had already diagnosed down to the advisory ID and both candidate routes — sat untouched
for a day while it blocked the release candidate. That is an execution gap, not a ranking gap, and
it is the reason this determination lands the fix instead of recommending it a second time.

## The head is now one advisory away from rc.2-eligible

This is the decisive change since 2026-10-24, and it narrows the release path to a single item.

`Meridian CI` is **green** on the current head. `Production Certification` is **red**, and it is red
on one job out of four:

| Head | Time (UTC) | Meridian CI | Production Certification |
| --- | --- | --- | --- |
| `e9ab984d` (**current head**) | 2026-10-25 08:31 | **success** | **failure** |
| `65dbf4f9` | 2026-10-25 07:56 | **success** | **failure** |
| `d92e29f5` | 2026-10-25 07:43 | cancelled | **failure** |
| `05840a0a` | 2026-10-23 22:44 | failure (`pwsh`) | **failure** |
| `b020b537` | 2026-10-23 21:43 | success | **failure** |

Five consecutive `main` pushes red on `Production Certification`, spanning roughly two and a half
days, all on the same cause. The failing job is **"NuGet and npm dependency evidence"**, and the
NuGet half is entirely clean — all 56 projects report *"no vulnerable packages given the current
sources."* The gate output on the current head is one line:

```text
npm-audit gate: UNACCEPTED GHSA-mrhx-c7xc-m3w5 (zod, high): no acceptance entry
npm-audit gate: FAIL — see unaccepted or stale entries above.
```

Because `Meridian CI` is green and this is the only other red lane, **this single advisory is the
last engineering blocker between `main` and a head that satisfies the tracker's `rc.2` precondition.**
Everything else on the release path is the signing secret, which is a human action.

## What this determination changed

The fix is in this branch. It is two files, and the evidence is below rather than in a promise.

**What the advisory actually is.** `GHSA-mrhx-c7xc-m3w5` is *"Zod can exhaust memory while
validating oversized constrained collections"* — a memory-exhaustion denial of service, rated
**high**, affecting `<4.6.6`, with `fixAvailable: true`. The installed version was `4.4.3`; `4.6.7`
is the current registry latest.

**Why a bump was the right route, on stronger evidence than the predecessor had.** `zod` is **not a
declared dependency of the dashboard at all** — it appears in neither `dependencies` nor
`devDependencies`. It is purely transitive, and it has exactly two dependents, both dev-only:

| Dependent | Declared range | `4.6.7` in range? |
| --- | --- | --- |
| `eslint-plugin-react-hooks` | `^3.25.0 \|\| ^4.0.0` | yes |
| `zod-validation-error` | `^3.25.0 \|\| ^4.0.0` | yes |

Both already accept the whole `4.x` line, so moving to `4.6.7` needs no dependent upgrade and
crosses no major boundary.

**The mechanism is the one the repository already uses.** `package.json` carries an `overrides`
block holding `postcss` and `nanoid` for exactly this purpose, so the change is one entry
(`"zod": "^4.6.7"`) in an existing block — no new pattern, and nothing added to a project file in a
way that would cut across central package management.

**Validation, in order:**

1. **Reproduced the failure** at the baseline: `npm ci` then the lane's own
   `validate-npm-audit.py --fail-level high` exited `1` with the same `UNACCEPTED
   GHSA-mrhx-c7xc-m3w5 (zod, high)` line CI prints.
2. **Applied the override** and regenerated the lockfile with `npm install --package-lock-only`.
   `node_modules/zod` is now `4.6.7` and still carries `"dev": true`.
3. **Re-ran the identical gate:** `npm-audit gate: PASS — every gated advisory is explicitly
   accepted and unexpired.` (exit `0`), with the acceptance register still empty.
4. **Dashboard tests:** 43 of 43 passed across 3 files.
5. **Production build:** succeeded, and **the built `src/Meridian.Ui/wwwroot/workstation/` output is
   byte-identical** — the build produced no asset change at all. That is direct evidence that `zod`
   never reaches the shipped workstation bundle, which the dev-only lockfile flag had only implied.

**Why not the acceptance-register route.** It was defensible — the package never ships — but it
costs more and buys less: it needs an owner, a rationale and a `review_by` in
`docs/security/known-vulnerabilities.md` plus a mirroring entry, it expires and returns as a stale
entry, and it would leave a known high advisory in the tree when a patched version was already
inside both dependents' declared ranges. The register staying at `"accepted": []` is a property
worth preserving.

**What is deliberately not in this change.** `npm audit` still reports **two moderate** advisories,
both `@vitest/mocker` reached through `vitest` (`<4.1.12`, fixed by the non-major `4.1.12`). They are
**below the lane's `high` fail threshold and are not what failed**, so bundling them would widen a
release-unblocking fix for no gate benefit. They are ranked separately below.

## P0: still human, still the same two secrets

`PRD-014` is unchanged. `MDC_SIGNING_CERT_PFX_BASE64` and `MDC_SIGNING_CERT_PASSWORD` are still not
provisioned in the protected `desktop-release-signing` environment, and the tracker still records
`v0.1.0-rc.1`'s tag run as having failed closed with `signing-secrets-unavailable`. The gate behaved
correctly — it refused rather than emitting an unsigned artifact — so there is no defect to fix, and
no amount of engineering moves this item.

With the npm gate fixed, this is the **only** thing left between the program and a signed artifact.
It has now been named as the sole human blocker by five consecutive determinations.

## P1: the conflict tax grew again, and this document's own lane is now part of it

Re-measured against `e9ab984d`. **40 of the 41 open PRs measured** (#3263's head ref was again not
fetchable): **34 conflict, 449 total conflicts** — up from 29 of 40 and 428 four days ago, which was
up from 367 before that. Three consecutive measurements, three increases.

Classifying all 830 conflicting path entries:

| Class | Entries | Share |
| --- | --- | --- |
| Resolver-covered (generated output, workstation bundle, reviewed hash manifest) | **785** | 94.6% |
| Hand-written | **45** | 5.4% |

The composition the last four determinations found holds a fifth time. Per-path frequency:

| Path | Conflicts on | In the resolver? |
| --- | --- | --- |
| `docs/status/doc-health-dashboard.{json,md}` | **27 PRs** each | yes |
| `docs/status/example-validation.md` | 16 PRs | yes |
| `docs/source/generated/source-hash-manifest.json` | 15 PRs | yes (dedicated hash merger) |
| `docs/generated/repository-structure.md` | 9 PRs | yes |

Every one of the top four is already covered, and `example-validation.md` has now risen from 14 to
16 PRs since the one-line fix that covers it landed. **The resolver is correct and has still never
been run across the queue.** It remains the cheapest item in this document.

**The hand-written hotspots, one of which this scheduled task creates.**

| Path | Conflicts on |
| --- | --- |
| `docs/architecture/security-master-extensibility-review.md` | 7 PRs |
| `docs/product/plans/README.md` | **5 PRs** |
| `docs/status/wpf-screen-development-tracker.{json,md}` | 2 PRs each |
| `build/config/contracts/type-parity-registry.json` | 2 PRs |
| `src/Meridian.Infrastructure/README.md` | 2 PRs |

`docs/product/plans/README.md` was a 2-PR conflict four days ago and is now a 5-PR one. The cause is
not incidental: **it is the index every determination appends its own row to, and the five
conflicting PRs are the five unmerged determinations.** This lane now manufactures its own
hand-written conflict source, and the document you are reading adds a sixth row to the same file.
The security-master review stack does the same thing to
`security-master-extensibility-review.md` — nine open review passes, each appending to one
hand-written document. No resolver can fix either; they are genuine competing edits.

**The automation that would apply the resolver is still broken and still growing.** `Generated
Repair Publisher` is at **595 runs**, up from 582. Every one of the twelve most recent
`workflow_run` invocations **failed**, including six on the current head today. #3232, which
re-architects this surface, is **still unmerged**, `generated_repair.py:213` still raises
`"Target run is not bound to its base SHA"`, and #3232 still carries 5 conflicts of its own on paths
its own resolver covers.

## P1: the two `verify-workflows` races remain unfixed

Unchanged from the last three determinations, and unchanged by the `pwsh` outage having healed:
`tests/scripts` still has **321 `tempfile.TemporaryDirectory(` call sites across 98 files** with
**zero** using `ignore_cleanup_errors`, and the cancellation/allocation reconcile window in
`tools/schema_control/disposable.py` is unchanged. Latent while the lane is green, but it is the
second mechanism by which this lane reddens the release branch with no code defect behind it.

The predecessor's `pwsh`-preflight recommendation also stands, and its reasoning survived the week:
the outage surfaced as 130 assertion failures across twelve modules when the real fault was one
sentence long. A preflight that fails the lane **once**, with an accurate message, preserves the
failure `AGENTS.md:58` requires while replacing the misleading spray. It must not become a skip.

## What has not moved: the product lane

No product-status movement. `roadmap-items.yml` parses to **48 rows: 17 `done`, 15 `planned`,
9 `accepted`, 7 `in_progress`, 0 `ready_for_acceptance`** — identical to the last two determinations.
The seven `in_progress` rows are the two W8 UI-lane rows (`W8-WPF-PARITY-001`, `W8-UX-CONSOL-001`,
both `on_track`) plus the five W10 depth rows (`W10-MARK-001`, `W10-PROV-001`, `W10-JRNL-001`,
`W10-SEAM-001`, `W10-LOT-002`, all `green`).

The user-requested queue is unchanged: **`W10-LOT-002`, then `W10-MARK-001`, then `W10-SEAM-001`,
before `W10-RECON-001`** in that queue only. With the npm gate now passing, this is the lane to
return to as soon as `rc.2` is cut — it is no longer gated behind a red release lane.

## Process: the fifth consecutive determination to make the same recommendation

The 2026-10-24 determination merged quickly (#3267, #3268), which is an improvement. It also had to
**correct its own P1 in a follow-up commit** (`13e54b9b`, "correct the pwsh P1 recommendation"),
having first proposed a `skipUnless` guard that its own evidence showed would have skipped nothing
and that `AGENTS.md:58` forbids.

Open determination PRs are unchanged at five: **#3207** (10-18), **#3223** (10-19), **#3249**
(10-21), **#3259** (10-22), **#3264** (10-23). The security-master stack is unchanged at nine:
**#3111, #3188, #3196, #3220, #3234, #3240, #3247, #3258, #3263**. **Fourteen of the 41 open PRs are
unmerged scheduled-document output** — roughly a third of the queue, and the source of both
hand-written conflict hotspots above.

The standing recommendation therefore repeats for a fifth time, now with a cleaner demonstration
behind it than argument: in the 24 hours covered here, the predecessor's P0 set resolved itself
(the `pwsh` red), stayed human (the secrets), or waited for someone to spend twenty minutes (the
`zod` bump). A sixth ranking document was not what any of those needed. **Close the superseded
determinations unmerged, merge or close the security-master stack, and reduce the cadence of both
scheduled passes** — both lanes now cost the queue more in conflicts than they return in direction.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0 — done in this branch** | **`zod` npm-audit gate fixed** — `"zod": "^4.6.7"` added to the dashboard's existing `overrides` block; lockfile regenerated to `4.6.7` | `Production Certification` was red on five consecutive `main` pushes on `UNACCEPTED GHSA-mrhx-c7xc-m3w5 (zod, high)`. Gate reproduced FAIL at baseline then PASS after; 43/43 dashboard tests; build clean with a byte-identical `wwwroot` bundle. `zod` is undeclared, transitive and dev-only, and both dependents already accept `^4.0.0` |
| **P0** | **Provision `MDC_SIGNING_CERT_PFX_BASE64` and `MDC_SIGNING_CERT_PASSWORD`** in the protected `desktop-release-signing` environment | `PRD-014`. With the npm gate fixed, this is the only remaining blocker to a signed artifact, and the only item here no engineering can move. Named by five consecutive determinations |
| **P0** | **Tag `v0.1.0-rc.2`** once a `main` head is green across both `Meridian CI` and `Production Certification` | `Meridian CI` is already green on `e9ab984d`; the npm fix should clear the other lane. `v0.1.0-rc.1` carries neither receipt — its tag run failed closed on secrets and `c51c5d8d`'s `Meridian CI` failed on `verify-docs` |
| **P0** | **`PRD-002`'s remaining clauses** — browser/WPF/setup/default-runtime ownership; legacy ownership/verification migration, rollback and rotation proof | Still the tracker's only code-rather-than-signature P0, unchanged since C7/C8 and the platform clause closed on 2026-10-20 |
| **P1** | **Run the resolver across the open queue** | 785 of 830 conflicting path entries (94.6%) are already resolver-covered, including all four top paths. 34 of 40 measured PRs conflict; 449 conflicts, up from 428 and 367. The tool is correct and has never been run |
| **P1** | **Land #3232, then promote the `.gitattributes` / regenerate-on-merge item** | `Generated Repair Publisher` is at 595 runs with all twelve recent `workflow_run` invocations failed; `generated_repair.py:213` unchanged. `doc-health-dashboard.{json,md}` conflicts on 27 of 40 PRs — recurring by construction, now measured five times |
| **P1** | **Add a `pwsh` preflight to the script-test lane that fails once with an accurate message** — not a `skipUnless` guard | Carried forward intact, including the predecessor's own correction: `pwsh` was installed and aborting, so a `which` guard would have skipped nothing, and the guard that spared `test_desktop_msix_packaging` is `os.name == "nt"`. CI must keep failing; only the diagnostic should change |
| **P1** | **Fix the two `verify-workflows` races** — `ignore_cleanup_errors=True` across 321 `tests/scripts` call sites; the `disposable.py` reconcile window | Unchanged for eight days. The lane gates the release branch and this is the second way it reddens without a code defect |
| **P2** | **Bump `vitest` to `4.1.12`** | Clears the two remaining moderate advisories (`@vitest/mocker`, `<4.1.12`, non-major fix). Deliberately kept out of the release-unblocking change: both sit below the lane's `high` threshold and neither is what failed |
| **P2** | **Close the superseded determinations (#3207, #3223, #3249, #3259, #3264); merge or close the security-master stack (#3111–#3263)** | 14 of 41 open PRs are unmerged scheduled-document output, and they are now the queue's two largest hand-written conflict sources — `plans/README.md` on 5 PRs and `security-master-extensibility-review.md` on 7, both self-inflicted by these lanes |
| **P2** | **Reduce the cadence of both scheduled passes** | Of the predecessor's three P0s, one self-healed, one was human, and one needed twenty minutes of work. None needed a sixth ranking document |

## The bar for the next determination

**The next determination is due when `v0.1.0-rc.2` is tagged, or when the signing secrets are
formally refused.**

This is the predecessor's bar with its engineering half removed, because that half is now done. If
`Production Certification` is green on the next `main` push and `rc.2` still is not tagged this
week, there is exactly one thing to examine — the signing-secret decision path — and it is not a
backlog question, not an analysis question, and not a question another document in this series can
answer.
