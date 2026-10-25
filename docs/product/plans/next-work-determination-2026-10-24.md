# What To Work On Next — Meridian (2026-10-24)

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-24
**Baseline:** `origin/main` at `05840a0ae6c4d50413909c037583ee7241cc786c` (2026-10-23 22:44 UTC)
**Supersedes:** the delivery-system ranking carried by the merged
[2026-10-20 determination](next-work-determination-2026-10-20.md), whose bar is **met** — the RC
tag exists and its first post-tag blocker is found. It does not supersede the five unmerged
determinations #3207, #3223, #3249, #3259 and #3264, which are addressed in the process section.
**Method:** read every `Meridian CI` and `Production Certification` `push` run on `main` by
`head_sha` and the per-job conclusions of each red run; downloaded and parsed the complete job-log
archives for the three red runs (`40035508067`, `40028833332`, `39630903593`) rather than reading
log tails; reproduced the `verify-workflows` script-test lane locally at the baseline commit;
grouped all 130 CI `FAIL`/`ERROR` lines by module and correlated them against which modules execute
`pwsh`; diffed `b020b537..05840a0ae` to test whether the red lane could be the diff's; re-measured
every open pull request with `git merge-tree --write-tree --name-only origin/main refs/remotes/pr/<n>`,
counting `CONFLICT (...)` lines and classifying each conflicting path against the resolver's
`GENERATED_FILES`; parsed `roadmap-items.yml` with `yaml.safe_load`; read the npm-audit gate, its
acceptance register and the dashboard lockfile in current source.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; `docs/product/implementation-todo-list.md`
> remains authoritative for the P0 release gate. Recheck CI results and conflict counts against the
> live head before acting.

## The bar is met: the RC tag exists, and its first post-tag blocker is found

The 2026-10-20 determination set the bar as *"the RC tag exists and its first post-tag blocker is
found, or a signature named above is formally refused."* Both halves of the first condition are
satisfied.

**The tag exists.** `v0.1.0-rc.1` was pushed on 2026-10-21 against `c51c5d8d` (the merge of #3239).
The repository now holds two tags where it held one.

**Every decision the predecessor ranked P0 has cleared.** Re-verified in current source:

| 2026-10-20 P0 item | State today |
| --- | --- |
| Tag the RC | **Done** — `v0.1.0-rc.1` on `c51c5d8d`, 2026-10-21 |
| Sign `ADR-019` | **Done** — `Accepted (core-team sign-off 2026-10-20 … DEC-PRD000-SUPPORT-MATRIX-001)` |
| Sign `ADR-020` | **Done** — `Accepted (core-team sign-off 2026-10-20, recorded as DEC-PRD000-SUPPORT-MATRIX-001)` |
| Platform fail-closed in the production posture guard | **Landed** — `ProductionServiceRegistrationPolicy.CollectPlatformViolation` / `IsPlatformGatedLocalComposition`, consumed by `ProductionRegistrationGuardService:74-93` |
| `PRD-002` C7/C8 (concurrent configuration writes, credential/audit commit) | **Closed** per the tracker, same day |
| Add `example-validation.md` to `GENERATED_FILES` | **Landed** — `build/scripts/resolve-generated-merge-conflicts.py:25` |
| Refresh `program-state.yml` / tracker inventory | **Done** — both now read 48 rows; the registry disagreement is resolved |
| Correct the tracker's "draft #2931" | **Done** — now `merged 2026-09-30` |
| Close #3197; merge or close #3170 | **Done** — neither is open |

That is an unusually complete close-out, and it is worth saying plainly: the predecessor's diagnosis
("the P0 set is now almost entirely decisions") was right, and the decisions were then taken.

**And the blocker is found.** It is not one blocker but three, and only one of them is a signature.
The rest of this document is about them.

## P0: the RC tag cannot certify, for two independent reasons

### 1. Its tag run failed closed on the signing secrets

The `Desktop Installer Release` run on the tag ref
([19 / 39630903593](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39630903593))
failed. Two jobs went red, and `validate-release-eligibility` names the cause exactly:

```text
##[error]Release signing prerequisite failed (signing-secrets-unavailable); inspect the protected
desktop-release-signing environment.
```

This is `PRD-014`, unchanged: `MDC_SIGNING_CERT_PFX_BASE64` and `MDC_SIGNING_CERT_PASSWORD` are
still not provisioned in the protected `desktop-release-signing` environment. It is the one item in
this document that is purely a human action, and it is the single thing standing between the
program and a signed release artifact. The gate behaved correctly — it refused rather than
producing an unsigned artifact — so there is no engineering defect here, only an unprovisioned
secret.

**Worth noting about the tag ref:** only two workflows ran on it at all — `Desktop Installer
Release` (failure) and `Copilot Setup Steps` (success). `Production Certification` and `Publish
Smoke` did not run on the tag. The tracker's own release-evidence model expects those lanes on the
frozen commit, so the tag produced substantially less evidence than the model assumes.

### 2. The commit it points at was never green

`c51c5d8d`'s `Meridian CI` run
([5792 / 39630535634](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39630535634))
**failed**: `verify-docs` went red, and `quality-gate` with it. `verify-workflows`, `verify-dotnet`,
`verify-browser`, the PostgreSQL job and `integration-gate` all passed, but the authoritative merge
gate did not.

This is the detail that matters most about the tag. The 2026-10-20 determination identified a
specific commit — `a9d470c3` — that carried a **ten-workflow green receipt**, and argued the window
was perishable: *"every merge moves the head away from it."* `a9d470c3` is an ancestor of the tag,
but the tag was placed on `c51c5d8d`, three merges later, on a commit whose merge gate was red. The
receipt the determination had found was not the thing that got frozen.

So `v0.1.0-rc.1` carries neither a signing receipt nor a green merge-gate receipt. The tracker
already reaches the same conclusion and names `v0.1.0-rc.2` on a green `main` head as the corrected
candidate. This document agrees and adds what has to be true first.

## P0: `Production Certification` is red on `main`, and it is a real security finding

This is the new engineering finding, and it is the thing most worth fixing today, because it blocks
`rc.2` directly.

`Production Certification` is **red on the two most recent `main` pushes** and has been for roughly
two days:

| Head | Time (UTC) | Production Certification |
| --- | --- | --- |
| `05840a0a` (**current head**) | 2026-10-23 22:44 | **failure** |
| `b020b537` | 2026-10-23 21:43 | **failure** |
| `6ca5515b` | 2026-10-23 15:45 | success |
| `9a8fd24c` | 2026-10-22 22:32 | success |

It is not the transient fault described in the next section: `b020b537`'s `Meridian CI` run passed
cleanly at 21:43, an hour before the `pwsh` window opened, and its `Production Certification` still
failed.

The failing job is **"NuGet and npm dependency evidence"**, and the gate output is unambiguous:

```text
3 vulnerabilities (2 moderate, 1 high)
npm-audit gate: UNACCEPTED GHSA-mrhx-c7xc-m3w5 (zod, high): no acceptance entry
npm-audit gate: FAIL — see unaccepted or stale entries above.
```

The NuGet half of the job is entirely clean — every project reports *"no vulnerable packages given
the current sources."* The failure is one npm advisory.

What current source says about it:

- **`zod` is at `4.4.3`** in `src/Meridian.Ui/dashboard/package-lock.json`, and **`4.6.7` is the
  current latest** on the registry, so a patched version is available.
- **It is a dev-only, transitive dependency.** The lockfile entry carries `"dev": true`, and the
  only dependents are `eslint-plugin-react-hooks` and `zod-validation-error`. Nothing reaches the
  shipped workstation bundle through it.
- **The acceptance register is empty.** `build/config/security/npm-audit-accepted-advisories.json`
  reads `"accepted": []`, and `docs/security/known-vulnerabilities.md` has no `zod` record.
- **The gate's threshold is `high`** (`validate-npm-audit.py:70-72`, `SEVERITY_RANK` at `:36`), so
  the two moderate advisories are below it and are not what fails. Only the `zod` high does, and it
  fails because it is unaccepted rather than because it is unfixable.

**Both routes are legitimate and both are small.** Bumping `zod` is the better call: the dependency
is dev-only and transitive, a patched version exists, and a lockfile override carries no production
risk. The alternative — a registry record in `known-vulnerabilities.md` plus a mirroring acceptance
entry with owner, rationale and `review_by` — is a defensible scope-out precisely because the
package never ships, but it spends a review cycle and expires. Either way this is bounded work in
two files, and it is the gate that `rc.2` has to pass.

## P0: the head's other red lane is a transient toolchain crash — re-run it, do not debug it

`Meridian CI` is also red on `05840a0a`
([5861 / 40035508067](https://github.com/rodoHasArrived/Meridian-main/actions/runs/40035508067)),
and this one is not the repository's fault. The distinction matters, because the obvious reading —
"130 tests failed, something is badly broken" — is wrong, and acting on it would waste a day.

`verify-workflows` was the only failing lane; `verify-dotnet`, `verify-browser`, `verify-docs`, the
PostgreSQL job and `integration-gate` all passed. Its summary:

```text
Script-test lane: ran 1813 tests, 0 module(s) quarantined, failures=116, errors=14, skipped=16.
```

**Every one of those 130 failures is `pwsh` itself dying.** Grouping the distinct error messages
across the whole log:

| Message | Count |
| --- | --- |
| `AssertionError: -6 != 0 : Stack overflow.` | 80 |
| `AssertionError: 'receipt' not found in 'stack overflow.\n'` | 17 |
| `AssertionError: False is not true : Stack overflow.` | 8 |
| `AssertionError: unexpectedly None : Stack overflow.` | 4 |
| `CalledProcessError: … pwsh … died with <Signals.SIGABRT: 6>` | 9 |

`-6` is SIGABRT. PowerShell was aborting on startup with a stack overflow, so every test that
shelled out to it failed.

**The correlation is exact.** Twelve modules failed. All twelve execute `pwsh`. Of the 21 modules
that mention `pwsh`, the nine that did **not** fail either skip it off Windows
(`test_desktop_msix_packaging:53` — `@unittest.skipUnless(os.name == "nt" and shutil.which("pwsh"))`)
or mock `subprocess` (`test_run_dotnet_ci_tests:312`). **Zero of the 113 modules that never invoke
`pwsh` failed.**

**The diff cannot be the cause.** `b020b537..05840a0ae` is 29 files: security-master C# and SQL,
`database/manifest/*`, and generated docs. It touches no `pwsh` script, no `tests/scripts` test, and
no workflow — nothing the script-test lane exercises.

**And it has already healed.** `verify-workflows` ran the full hygiene lane in 5m36s and **passed**
at 02:29 UTC on 2026-10-24
([5873 / 40054342341](https://github.com/rodoHasArrived/Meridian-main/actions/runs/40054342341), job
`120753877113`) on a merge ref containing `main`. Every `Meridian CI` run from 00:11 UTC onward is
green. The fault window was roughly 22:44–23:01 UTC on 2026-10-23.

So the correct action is **one re-run of the failed job on `05840a0a`**, not a code change. This is
the narrow, legitimate flake case — an error naming a toolchain the diff does not touch, which the
same lane contradicts on the same content hours later. What it is not is a reason to relax the lane.

## P1: the script-test lane diagnoses a broken interpreter as 130 unrelated test failures

The outage above was surfaced as 130 assertion failures spread across twelve modules, each one
reading like an independent defect in recovery receipts, PostgreSQL payloads or desktop build
receipts. The actual fault was one sentence long: the host's PowerShell could not start. That gap
between the signal and the cause is the defect worth fixing, and it cost most of the diagnosis time
behind this document.

**What this is not an argument for.** An earlier draft of this section proposed routing the twelve
`pwsh`-executing modules through the `skipUnless` guard that `test_desktop_msix_packaging:53` uses.
That recommendation was wrong on its own evidence, and the correction is worth recording because the
same reasoning error is easy to repeat:

- **A `which`-based guard would not have skipped anything.** `pwsh` was *installed* during the
  outage — the lane's own `CalledProcessError` names `/usr/bin/pwsh` — and it aborted on execution.
  `shutil.which("pwsh")` therefore returned a real path, so the guard would have been true and all
  130 tests would have run and failed exactly as they did.
- **What actually skipped `test_desktop_msix_packaging` was `os.name == "nt"`**, which is false on
  every Linux runner. Copying that guard to the twelve cross-platform modules would not have made
  them resilient; it would have **deleted their Linux coverage permanently**, outage or not. Those
  modules normally run and pass on Linux.
- **The ownership safeguard that section claimed does not exist.**
  `build/scripts/ci/check-test-skip-register.py:607` scans only `**/*.cs` and `**/*.fs`. Python
  suites are outside its scope, so skips added in `tests/scripts` would carry no registered owner
  and no expiry.
- **And it is prohibited.** `AGENTS.md:58` forbids disabling, weakening, or *conditionally
  bypassing* tests. Turning a red lane green by skipping is precisely that, and no
  signal-quality argument overrides it.

**The distinction that matters.** An interpreter that is *absent* and one that is *present but
non-functional* are different conditions and deserve different outcomes. Absent is a local-developer
condition and may legitimately skip: running this lane in a container without PowerShell yields
**49 failures and 4 errors** purely from `shutil.which("pwsh")` returning `None`, which is noise
rather than information. Present-but-aborting is an infrastructure fault, and CI must **keep
failing** on it — the lane was right to go red; it was only wrong about how it said so.

**So the work here is diagnosis, not suppression.** A single preflight at lane start that executes
`pwsh -NoProfile -Command $PSVersionTable.PSVersion` and, when that aborts, fails the lane once with
an explicit "PowerShell runtime is present but non-functional on this host" message would preserve
the failure that `AGENTS.md` requires while replacing 130 misleading assertions with one accurate
line. The narrower inconsistency can be settled alongside it: `test_production_recovery:33` asserts
on a missing interpreter while `test_recovery_evidence:57` raises in `setUpClass`, so the same
precondition is reported as a failure in one module and an error in the other. Making those agree is
cleanup; it must not become a skip in CI.

## P1: the two `verify-workflows` races are still unfixed, four days on

Re-measured at the baseline, unchanged from the last two determinations:

- `tests/scripts` has **321 `tempfile.TemporaryDirectory(` call sites across 98 files** and
  **zero** using `ignore_cleanup_errors`.
- The cancellation/allocation reconcile window in `tools/schema_control/disposable.py` is unchanged.

These are latent, not active — the lane is otherwise green — but they are the second mechanism by
which this lane can go red without a code defect, and the lane gates the release branch.

## P1: the generated-conflict tax grew again

Re-measured against `05840a0a`. **40 of the 41 open PRs measured** (#3263's head ref was not
fetchable): **29 conflict, 428 total conflicts.** The 2026-10-20 determination measured 367 across
23 of 32.

Classifying every conflicting path:

| Class | Paths | Detail |
| --- | --- | --- |
| Generated output | **778** | workstation bundle renames, `docs/status/*`, `docs/generated/*`, `docs/source/generated/*`, `docs/roadmap/generated/*` |
| Hand-written | **31** | see below |

Roughly 96% generated — the composition the last three determinations found, holding a fourth time.

Per-path frequency:

| Path | Conflicts on | In the resolver? |
| --- | --- | --- |
| `docs/status/doc-health-dashboard.{json,md}` | **22 PRs** each | yes |
| `docs/generated/repository-structure.md` | 17 PRs | yes |
| `docs/status/example-validation.md` | 14 PRs | **yes, now** |
| `docs/source/generated/source-hash-manifest.json` | 14 PRs | yes (dedicated hash merger) |

**The one-line fix the last two determinations asked for did land** —
`resolve-generated-merge-conflicts.py:25` now lists `example-validation.md`. It still conflicts on
14 PRs because **nobody has run the resolver across the queue.** The tool is now correct and unused.
That is the cheapest item in this document: the resolver already covers every one of the four paths
above.

**A new hand-written hotspot, and it is self-inflicted.**
`docs/architecture/security-master-extensibility-review.md` conflicts on **7 PRs** (#3188, #3196,
#3220, #3234, #3240, #3247, #3258) — a stack of scheduled security-master review passes, each
appending to the same document on its own branch. Unlike the generated churn, no resolver can fix
this: they are genuine competing edits to one hand-written file. The remaining hand-written
conflicts are thin and scattered — `wpf-screen-development-tracker.{json,md}` (2 PRs),
`docs/product/plans/README.md` (2), `type-parity-registry.json` (2), `Meridian.Infrastructure/README.md`
(2), and single-PR conflicts in #3260, #3245, #3195 and #3004.

**The automation that would apply the resolver still has never worked, and it has gotten worse.**
`Generated Repair Publisher` is at **582 runs**, up from 383 four days ago. Every `workflow_run`
invocation among the 15 most recent **failed**; the rest are `push`/`schedule` skips. #3232, which
re-architects this surface, is **still unmerged** — `generated_repair.py:213` still raises `"Target
run is not bound to its base SHA"` — and #3232 now carries 5 conflicts of its own, all on paths its
own resolver covers.

## Process: five unmerged determinations, and scheduled documents are now a third of the queue

The predecessor's process recommendation **worked**: #3170 and #3197 are both resolved, and the
2026-10-20 determination itself merged. That is the first time this pattern cleared.

It then immediately re-formed. Open determination PRs: **#3207** (10-18), **#3223** (10-19),
**#3249** (10-21), **#3259** (10-22), **#3264** (10-23) — five, and this document makes a sixth.
Three of them were written after the predecessor explicitly *"recommend[ed] no twelfth."*

And the same shape now exists in a second lane. Open security-master institutional-requirements
passes: **#3188, #3196, #3220, #3234, #3240, #3247, #3258, #3263** — eight, several stacked on each
other, all appending to the one document that is now the queue's largest hand-written conflict
source.

**Thirteen of the 41 open PRs are unmerged scheduled-document output.** That is where the
`security-master-extensibility-review.md` conflicts come from, and it is a self-sustaining cost: each
new pass conflicts with the unmerged ones before it.

The standing observation survives another week with better evidence than before. In the four days
since the last merged determination, two ADRs were signed, a platform fail-closed shipped, `PRD-002`
C7/C8 closed, an RC was tagged, and the registry drift was reconciled. **None of that needed a
ranking document, and all of it happened while five of them sat unread.** Meanwhile the three things
this document ranks P0 are a secret, a dependency bump and a button labelled "re-run jobs."

The honest recommendation is therefore the same one, applied to both lanes: **close the superseded
determinations unmerged, merge or close the security-master stack, and reduce the cadence of both
scheduled passes.** A document that re-measures a conflict count every 24 hours is not informing the
decision path; it is adding to it.

## What has not moved: the product lane

No product-status movement in the four days since 2026-10-20. `roadmap-items.yml` parses to **48
rows: 17 `done`, 15 `planned`, 9 `accepted`, 7 `in_progress`, 0 `ready_for_acceptance`** — identical
to the predecessor's inventory. `program-state.yml` and the readiness tracker now both read 48 rows,
so the registries agree.

The seven `in_progress` rows are the two W8 UI-lane rows plus the five W10 depth rows. The
user-requested queue is unchanged: **`W10-LOT-002` (rank 12), then `W10-MARK-001` (rank 1), then
`W10-SEAM-001` (rank 7), before `W10-RECON-001` (rank 2, `planned`) in that queue only.** All three
queued rows are `in_progress` with `health: green`.

This is the lane to return to once the head is green and `rc.2` is cut. It is not the lane to work
today, because a red `Production Certification` on `main` blocks the release candidate and product
work does not.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0** | **Fix the `zod` npm-audit gate** — bump `zod` from `4.4.3` (dev-only, transitive; `4.6.7` is current), or record a `known-vulnerabilities.md` entry plus a mirroring acceptance entry | `Production Certification` has been red on `main` for two days on `UNACCEPTED GHSA-mrhx-c7xc-m3w5 (zod, high)`. Not a flake — it failed on `b020b537`, whose `Meridian CI` passed. It is a release-evidence lane, so it blocks `rc.2`. Bounded work in two files |
| **P0** | **Re-run the failed `verify-workflows` job on `05840a0a`** | All 130 failures are `pwsh` aborting with SIGABRT/`Stack overflow.`; all 12 failing modules execute `pwsh`, zero of 113 non-`pwsh` modules failed, the diff touches nothing the lane exercises, and the same lane passed at 02:29 UTC on 2026-10-24. One re-run, no code change |
| **P0** | **Provision `MDC_SIGNING_CERT_PFX_BASE64` and `MDC_SIGNING_CERT_PASSWORD`** in the protected `desktop-release-signing` environment | The `v0.1.0-rc.1` tag run failed closed: `Release signing prerequisite failed (signing-secrets-unavailable)`. `PRD-014`. The only purely human item here, and the one thing between the program and a signed artifact |
| **P0** | **Tag `v0.1.0-rc.2` on a head green across both `Meridian CI` and `Production Certification`** | `v0.1.0-rc.1` carries neither receipt: its tag run failed on secrets, and `c51c5d8d`'s `Meridian CI` failed on `verify-docs`. Only two workflows ran on the tag ref at all. The predecessor found a ten-workflow receipt on `a9d470c3`; a later, red commit was tagged instead |
| **P0** | **`PRD-002`'s remaining clauses** — browser/WPF/setup/default-runtime ownership; legacy ownership/verification migration, rollback and rotation proof | Still the tracker's only code-rather-than-signature P0. C7/C8 and the platform clause closed on 2026-10-20; these did not |
| **P1** | **Add a `pwsh` preflight to the script-test lane that fails once with an accurate message** — not a `skipUnless` guard, which `AGENTS.md:58` forbids and which would not have helped | The outage surfaced as 130 assertion failures across twelve modules when the real fault was that PowerShell could not start. `pwsh` was installed (`/usr/bin/pwsh`) and aborted on execution, so a `which`-based guard would have been true and skipped nothing; the guard that spared `test_desktop_msix_packaging` is `os.name == "nt"`, and copying it would delete those modules' Linux coverage outright. CI must keep failing here — only the diagnostic should change |
| **P1** | **Run the resolver across the open queue** | 778 of 809 conflicting paths are generated, and the resolver already covers all four top paths — including `example-validation.md`, whose one-line fix landed but has never been applied. 29 of 40 measured PRs conflict; 428 conflicts total, up from 367 |
| **P1** | **Land #3232, then promote the `.gitattributes` / regenerate-on-merge item** | `Generated Repair Publisher` is at 582 runs, up from 383, with every recent `workflow_run` failing; `generated_repair.py:213` is unchanged. `doc-health-dashboard.{json,md}` conflicts on 22 of 40 PRs — recurring by construction, now measured four times |
| **P1** | **Fix the two `verify-workflows` races** — `ignore_cleanup_errors=True` across 321 `tests/scripts` call sites; the `disposable.py` reconcile window | Unchanged for four days. Latent while the lane is green, but it is the second way this lane reddens the release branch without a code defect |
| **P2** | **Close the superseded determinations (#3207, #3223, #3249, #3259); merge or close the security-master stack (#3188–#3263)** | 13 of 41 open PRs are unmerged scheduled-document output, and that stack is now the queue's largest hand-written conflict source (7 PRs on one review document). The predecessor's identical recommendation worked for #3170/#3197 |
| **P2** | **Reduce the cadence of both scheduled passes** | Two ADR signatures, a platform fail-closed, `PRD-002` C7/C8, an RC tag and the registry reconciliation all landed in four days, none of them prompted by a ranking document, while five sat unread |

## The bar for the next determination

**The next determination is due when `v0.1.0-rc.2` is tagged on a commit green across both
`Meridian CI` and `Production Certification`, or when the signing secrets are formally refused.**

The predecessor's bar is spent. What replaces it is deliberately narrow, because the P0 set has
shrunk to a shape no ranking document improves: one dependency bump, one job re-run, one secret, and
then a tag. None of it is analysis. If `rc.2` is not tagged this week, the thing to examine is the
signing-secret decision path and the `Production Certification` red — not the backlog, and not this
document's successor.
