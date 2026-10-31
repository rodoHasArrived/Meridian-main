# What To Work On Next — Meridian (2026-10-20)

**Snapshot:** Historical snapshot - superseded by roadmap registry for current status.

**Status:** prioritization input; not a governance decision and not a roadmap-status document
**Owner:** core-team
**Reviewed:** 2026-10-20
**Baseline:** `origin/main` at `a9d470c32fc46fbc94fac4dccf95a5dec9b0394c` (2026-10-20 15:51 UTC)
**Supersedes:** the ranking carried by the merged
[2026-10-16 determination](next-work-determination-2026-10-16.md) and by #3170, whose predecessor
bar is **met** for the first time (see below). It does not supersede the three unmerged
determinations #3197, #3207 and #3223, which were written against a red gate and are addressed
directly in the process section.
**Method:** read every `Meridian CI` and `CI` `push` run on `main` by `head_sha` (14 most recent of
each) and the per-job conclusions of the head run; grouped **all** `push` workflow runs at the three
newest heads by workflow name to test for a complete green receipt; pulled the live failing job log
of the newest `Generated Repair Publisher` run and counted conclusions across its 100 most recent
runs; re-measured every open pull request with
`git merge-tree --write-tree --name-only origin/main refs/remotes/pr/<n>`, counting only
`CONFLICT (...)` lines and classifying each path as generated or hand-written; parsed
`roadmap-items.yml` with `yaml.safe_load` and diffed it against the tree at the 2026-10-16 merge
(`1f33f090`); read ADR-019's support-matrix table and traced the credential-vault and
production-posture code paths it governs in current source.

> This document ranks work. It does not move a roadmap row, accept an item, or certify a release.
> The roadmap registry remains authoritative for status; `docs/product/implementation-todo-list.md`
> remains authoritative for the P0 release gate. Recheck CI results and conflict counts against the
> live head before acting.

## The bar is met: `main` now produces a fully green commit

The 2026-10-16 determination set the bar for the next determination as *"`Meridian CI / quality-gate`
green on three consecutive `main` pushes, or the RC tagged."* The first condition is satisfied, and
by more than its margin.

**Fourteen most recent `Meridian CI` push runs on `main`: nine `success`, five `cancelled`, zero
failures.** The six newest are consecutive successes:

| Run | Head | Conclusion |
| --- | --- | --- |
| [5713](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39452276601) | `a9d470c3` (**current head**) | success |
| [5704](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39388732894) | `6d39bc80` | success |
| [5700](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39355358903) | `a93fb1c5` | success |
| [5698](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39352850647) | `1327e06f` | success |
| [5694](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39349654346) | `79fa17a0` | success |
| [5688](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39346280551) | `2b1d3be3` | success |

The five `cancelled` runs are concurrency supersessions, not failures: each was superseded by a newer
`main` push within minutes (`1135db16` 00:08:51 → `2b1d3be3` 00:12:41; `0a4277dd` 23:42:53 →
`fffbb4fb` 23:48:57; `7a53e20d` 22:53:12 → `2e29769c` 22:53:22). That reading is from the timing
pattern, not from a cancellation record.

On the head, every job passed individually — `verify-workflows`, `verify-dotnet`, `verify-browser`,
`verify-docs`, the PostgreSQL `service-backed-integrations` job, `integration-gate`, and
`quality-gate` itself.

**More than the bar asked: `a9d470c3` carries a complete green push receipt.** Grouping *all* push
runs at that head by workflow:

| Workflow | Conclusion |
| --- | --- |
| Meridian CI | success |
| Production Certification | success |
| Windows Desktop Build | success |
| CodeQL | success |
| Golden Path Validation | success |
| CI | success |
| Documentation Automation | success |
| IB API Smoke Build | success |
| Pipeline Budget Benchmark | success |
| Roadmap Source Docs | success |
| Generated Repair Publisher | skipped (by design on `push`) |

Ten workflows green on one commit. The readiness tracker's retained baseline, `e0ecd952`
(2026-10-14), records an **eight**-workflow receipt. `a9d470c3` matches it and adds Golden Path
Validation and Pipeline Budget Benchmark.

**One honest caveat, and it matters for the tag.** The two `verify-workflows` races the 2026-10-16
determination named were never fixed. `tests/scripts` still has **98 files and 321 call sites** using
`tempfile.TemporaryDirectory(` with **zero** using `ignore_cleanup_errors`, and the
cancellation/allocation window at `tools/schema_control/disposable.py:432-443` is unchanged. The
lane went green without either repair, so the intermittency is **latent, not removed**. A green
receipt on a specific commit is still a valid receipt for that commit — that is what certification
evidence is — but a re-run of `a9d470c3` is not guaranteed to reproduce it. That is an argument for
tagging the commit that already has the receipt, and for fixing the races before anyone depends on
re-running the gate.

## P0: tag the RC candidate against `a9d470c3`

The last three determinations ranked *"freeze a full-coverage green commit and tag the RC"* and each
recorded that the instruction had no target. **It has one now.** The repository still holds
**exactly one tag, `eval-v0.1.0-eval.1`, and no `v*` tag at all.**

This is a decision plus a `git tag`. No part of it is engineering analysis, and the window is
perishable: `a9d470c3`'s receipt is a property of that commit, and every merge moves the head away
from it. The 2026-10-16 determination noted that the then-current receipted commit was "two days and
three merges behind the head." Freezing today avoids repeating that.

## P0: the one P0 clause everyone has been calling implementation is a fail-closed gap, not a keyring

This is the one new engineering finding in this document, and it changes the shape of the P0 set.

`PRD-002` is the readiness tracker's only P0 row rated *implementation-in-progress* — the only P0
that is code rather than a signature. Its open clauses include **"protect non-Windows vault keys with
an OS keyring or KMS-backed mechanism."** Three facts in current source, read together, say that
clause is not v1 work:

1. **ADR-019's v1 production envelope is Windows-only.** Its support matrix
   (`docs/adr/019-...:46`) lists *"Windows 11 x64, .NET 10, installed via the desktop-installer
   lane"* as supported and *"Linux container (`deploy/docker/`, `deploy/k8s/`), `deploy/systemd/`
   service"* as **explicitly experimental**. The ADR then states that everything in that right-hand
   column *"must be rejected — not silently degraded — when a production posture selects it."*
2. **The adjacent AES key is only reachable outside that envelope.**
   `FileProviderCredentialStore.ProtectAsync` returns `("dpapi-current-user", ...)` on Windows and
   falls through to `("local-aes-gcm", ...)` otherwise
   (`src/Meridian.DataIntegration/Credentials/FileProviderCredentialStore.cs:1167-1176`). The raw
   32-byte key in `provider-credentials.key` beside the vault is the non-Windows branch only. The
   code says as much itself: *"Owner-only is therefore a correctness requirement of the encryption,
   not defence in depth."*
3. **Nothing rejects the unsupported OS.** `MeridianDeploymentPosture`,
   `ProductionRegistrationGuardService`, and the `ProductionServiceRegistrationPolicy` behind
   `ValidateSupportedLocal` contain **no platform check of any kind**. The only platform
   fail-closed in the production path is `LifecycleProtectedSecretStore` inside the lifecycle
   supervisor (`LifecycleSupervisorRuntime.cs:1164,1205`), and it guards one secret store, not host
   composition. A `Production` posture started on Linux therefore composes, starts, and silently
   selects `local-aes-gcm`.

So the gap is not a missing keyring. **It is the missing fail-closed rejection that ADR-019 already
requires**, and `PRD-000`'s own re-scoped evidence item asks for exactly this: *"every experimental
posture fails closed with a diagnostic naming the prohibited bindings."* Adding a platform check to
the production posture guard — with the startup-refusal tests that surface already has for
prohibited in-memory bindings — converts an open P0 implementation clause into an in-envelope
scope-out that the ADR-019 signature ratifies. That is small, bounded, testable work in a file that
already does this job for a different dimension.

**What this does not claim.** `PRD-002` does not reduce to one platform guard. Its other open
clauses are real implementation: *"complete browser and remaining WPF/setup/default-runtime
ownership; resolve concurrent configuration writes and credential/audit commit ambiguity; prove
legacy ownership/verification migration, rollback and rotation."* Those stay engineering work.

**Also a tracker correction.** The tracker (reviewed 2026-10-14) cites
[#2931](https://github.com/rodoHasArrived/Meridian-main/pull/2931) twice as *"draft PR"* and *"draft
implementation evidence."* **It merged on 2026-09-30.** Its own body lists the residual work, so the
citation's substance holds; only the word "draft" is wrong, and the P0 row that the whole release
gate hangs on should not describe its evidence as unlanded.

## P0 (unchanged, now the whole remainder): four signatures, one secret, one product decision

Re-verified today, unchanged and four days older:

- `docs/adr/019-production-support-matrix-and-deployment-posture.md:3` still reads **`Proposed
  (awaiting core-team sign-off; signing this ADR is the PRD-000 "signed support matrix" artifact)`**.
- `docs/adr/020-lifecycle-control-plane.md:3` still reads **`Proposed`**.

With the gate green, a receipted commit available, and `PRD-002`'s non-Windows clause reframed
above, **the P0 set is now almost entirely decisions.** That was the predecessor's diagnosis of the
program; it is now the whole of it rather than one item in a list. If an RC is not tagged this week,
the thing to examine is the decision path, not the backlog.

## The W9 slate closed, and `program-state.yml` has not caught up

First product-status movement in weeks. Diffing `roadmap-items.yml` against the 2026-10-16 merge
(`1f33f090`):

| Row | Was | Now |
| --- | --- | --- |
| `W9-CORPACT-011` | `ready_for_acceptance` | **`accepted`** (`DEC-W9-ACCEPTANCE-007`, 2026-10-19) |
| `W9-GOV-008` | `in_progress` | **`accepted`** (`DEC-W9-ACCEPTANCE-006`, 2026-10-18) |
| `W9-INGEST-009` | `in_progress` | **`accepted`** (`DEC-W9-ACCEPTANCE-006`, 2026-10-18) |

**All eleven W9 rows are now `accepted` or `done`. The slate is closed**, and the program has **no
`ready_for_acceptance` row for the first time in 27 days** — which resolves the "take or decline
`W9-CORPACT-011`" item that four consecutive determinations carried. Registry inventory is now 48
rows: **17 `done`, 15 `planned`, 9 `accepted`, 7 `in_progress`, 0 `ready_for_acceptance`.**

**Small follow-up, mechanical.** `docs/roadmap/data/program-state.yml` still carries the
pre-acceptance world: `snapshot_date: 2026-10-14`, `active_scope` asserting *"W9-GOV-008 and
W9-INGEST-009 remain in progress"*, both rows still under `active_productization_targets:25-36`, and
*"Operator acceptance review of W9-CORPACT-011"* still in `current_focus:50`. The two canonical
registries disagree. The readiness tracker's "42 rows" inventory is stale by the same drift.

**What this leaves open.** The seven `in_progress` rows are the two W8 UI-lane rows plus the five
W10 depth rows. The user-requested queue is unchanged and now describes the entire open product
lane: `W10-LOT-002` (rank 12), then `W10-MARK-001` (rank 1), then `W10-SEAM-001` (rank 7), before
`W10-RECON-001` (rank 2, `planned`) in that queue only. All three queued rows are `in_progress` with
`health: green`.

## P1: the generated-conflict tax got much worse, and its fix is written but unmerged

The 2026-10-16 determination measured **15 conflicts across 6 PRs, 100% generated**, and called the
remaining load "smaller and entirely automatable." Re-measured today against `a9d470c3`:

**32 open PRs. 23 conflict. 367 total conflicts.** The rename/rename churn inside the built
workstation bundle, which that document reported gone from the active queue, is back — it is most of
the four 58-to-67-conflict PRs.

The composition is the finding:

| Class | Conflicts | Detail |
| --- | --- | --- |
| Generated output | **360** | workstation bundle renames, `docs/status/*`, `docs/source/generated/*`, `docs/generated/*`, `docs/roadmap/generated/*`, `type-parity-registry.json` |
| Hand-written | **7** | `src/Meridian.Infrastructure/README.md` (2 PRs), `src/Meridian.Contracts/README.md` (2 PRs), `docs/product/meridian-design-document.md` (1 PR) |

**Only 4 of 32 open PRs (#3004, #3105, #3169, #3195) contain a single hand-written conflict**, and
the three distinct paths are two source READMEs and one design document. Nothing in the queue is
blocked by a real disagreement about code.

Per-path frequency, which is what makes this worth ranking:

| Path | Conflicts on | In the resolver? |
| --- | --- | --- |
| `docs/status/doc-health-dashboard.{json,md}` | **22 of 32 open PRs** | yes (`GENERATED_FILES:20-39`) |
| `docs/source/generated/source-hash-manifest.json` | 15 PRs | yes (`REVIEWED_MANIFEST:42`, dedicated hash merger) |
| `docs/status/example-validation.md` | 11 PRs | **no** |

`docs/status/example-validation.md` declares *"Auto-generated by `validate-examples.py`. Do not edit
manually."* and is rewritten by `run-docs-automation.py:103-104`. It is a generated file the resolver
reports `REVIEW` on and stops. The 2026-10-16 determination found this and proposed the one-line
addition; **it is still absent from `GENERATED_FILES` on `main`, and also absent in #3232.** One line
covers 11 PRs.

**The automation that would apply the resolver still has never worked, and it has gotten worse.** The
`Generated Repair Publisher` is now at **383 runs. Zero successes, ever.** Across its 100 most recent
runs (since 2026-10-19 08:39 UTC): **85 `workflow_run` invocations, all 85 failures**; the other 15
are `push`/`schedule` skips. Confirmed live at the newest run
([#383](https://github.com/rodoHasArrived/Meridian-main/actions/runs/39455073057), 2026-10-20
16:11 UTC), whose job log ends:

```text
Generated repair: manual. Target run is not bound to its base SHA
##[error]Process completed with exit code 1.
```

Still the exact path diagnosed four days ago: `RepairError` at `generated_repair.py:213` caught by
the handler at `:544-546`, which writes a by-design `manual` disposition and returns 1.

**The fix exists, is bigger than the one proposed, and is stuck behind this same tax.**
[#3232](https://github.com/rodoHasArrived/Meridian-main/pull/3232) rewrites this surface: it removes
the "not bound to its base SHA" check entirely, re-architects authorization so a
`pull_request_target` run can only *request* generation while publication is dispatched on the
default branch, and relabels the exception handler's status `failed` rather than `manual`. So it
addresses the root cause structurally rather than by flipping an exit code. Two consequences worth
stating: in-flight requests will still error until the workflow is upgraded (`:205` — *"Run title
lacks valid v2 repair provenance; dispatch a new run after upgrading the workflow"*), and **#3232 is
itself one of the 23 conflicted PRs**, on `doc-health-dashboard.{json,md}` and
`source-hash-manifest.json` — all three already covered by the resolver it is fixing.

**Recommendation.** Land the one-line `example-validation.md` addition first (it is independent and
unblocks 11 PRs), then #3232, then run the resolver across the queue. Promote the `.gitattributes`
merge-driver / regenerate-on-merge item that #3170 ranked P3 and 2026-10-16 argued for promoting: at
22 of 32 PRs on one generated path, the churn is recurring by construction, and three determinations
have now measured it on three different file sets.

## Process: four unmerged determinations, and this is the eleventh

This matters because it is the pattern the last four documents each flagged and none changed.

| Determination | PR | State |
| --- | --- | --- |
| 2026-10-15 | [#3170](https://github.com/rodoHasArrived/Meridian-main/pull/3170) | open, 5 days |
| 2026-10-16 | #3187 | **merged** |
| 2026-10-17 | [#3197](https://github.com/rodoHasArrived/Meridian-main/pull/3197) | open |
| 2026-10-18 | [#3207](https://github.com/rodoHasArrived/Meridian-main/pull/3207) | open, draft |
| 2026-10-19 | [#3223](https://github.com/rodoHasArrived/Meridian-main/pull/3223) | open, draft |

Five determinations in six days; one merged. All four open ones were written against a red
`quality-gate` and an absent RC target, **and both of those premises changed today.** Their
delivery-system rankings are therefore stale at the top, whatever remains correct further down.

**Recommendation: close #3197, #3207 and #3223 unmerged, and merge #3170 or close it.** Closing a
superseded ranking is a result — #3132 was closed unmerged on 2026-10-16 and that was the right
outcome. #3170 is the only one carrying an independent reason to merge: it holds the written one-line
fix for the dangling `known-vulnerabilities.md` anchor in
`docs/engineering/production-certification-evidence-chain.md:280`.

And the standing observation now has a cleaner test. The gate went green and the W9 slate closed in
the four days since the last merged determination — **both while the ranking documents were piling
up unread.** The backlog did not need an eleventh ranking to move; it needed signatures and
acceptances, which is what the determinations kept saying. This document is deliberately short for
that reason, and it recommends no twelfth.

## Summary

| Priority | Work | Why now |
| --- | --- | --- |
| **P0** | **Tag the RC against `a9d470c3`** | The instruction three determinations carried finally has a target: ten workflows green on one commit, exceeding the tracker's own eight-workflow baseline. A decision plus a `git tag`; still zero `v*` tags. Perishable — every merge moves the head off the receipt |
| **P0** | **Sign `ADR-019` and `ADR-020`; provision the signing secret; decide the ~1 GB download** | Re-verified: both still read `Proposed`. With the gate green and `PRD-002` reframed below, the P0 set is now almost entirely decisions. No part of it is engineering analysis |
| **P0** | **Add a platform fail-closed to the production posture guard** (`ProductionServiceRegistrationPolicy` / `ProductionRegistrationGuardService`, which today contain no platform check at all) | ADR-019's v1 envelope is Windows-only and requires experimental postures be *"rejected — not silently degraded."* The adjacent-AES-key path `PRD-002` names is reachable only off Windows. This converts an open P0 implementation clause into a scope-out the ADR-019 signature ratifies — and is what `PRD-000`'s re-scoped evidence item already asks for |
| **P0** | **`PRD-002`'s remaining clauses** — browser/WPF/setup/default-runtime ownership, concurrent configuration writes, credential/audit commit ambiguity, legacy migration/rollback/rotation proof | Real implementation, unaffected by the reframe above. The tracker's only code-rather-than-signature P0 |
| **P1** | **Add `docs/status/example-validation.md` to `GENERATED_FILES`** in `resolve-generated-merge-conflicts.py:20-39` | One line, 11 of 32 open PRs. Found and proposed on 2026-10-16; still absent on `main` **and** in #3232 |
| **P1** | **Land #3232, then run the resolver across the queue** | 360 of 367 conflicts in the open queue are generated output; only 4 PRs hold a single hand-written conflict each. The publisher is at 383 runs with zero successes ever, confirmed live today |
| **P1** | **Promote the `.gitattributes` / regenerate-on-merge item** | `doc-health-dashboard.{json,md}` conflicts on **22 of 32 open PRs**. Three determinations have now measured this churn on three different file sets. Recurring by construction |
| **P1** | **Fix the two `verify-workflows` races** — `ignore_cleanup_errors=True` across 321 `tests/scripts` call sites; the window at `disposable.py:432-443` | Demoted from P0 because the lane is green. Neither fix landed, so the flakiness is latent: the RC's receipt is valid, but re-running the gate on it is not guaranteed to reproduce |
| **P2** | **Refresh `program-state.yml` and the tracker's row inventory** | The two canonical registries disagree: W9-GOV-008/W9-INGEST-009 are `accepted` in `roadmap-items.yml` and still "in progress" in `program-state.yml`. Mechanical |
| **P2** | **Correct the tracker's "draft #2931"** | #2931 merged 2026-09-30. The substance of the citation holds; the P0 row gating the release should not describe its evidence as unlanded |
| **P2** | **Close #3197, #3207, #3223; merge or close #3170** | All four were written against a red gate and an absent RC target; both premises changed today. #3170 alone carries a written fix worth merging (the dangling certification-evidence anchor) |

## The bar for the next determination

**The next determination is due when the RC tag exists and its first post-tag blocker is found, or
when a signature named above is formally refused.**

The predecessor's gate-based bar is spent: it was met today. What replaces it is deliberately not a
schedule. Every remaining P0 is a decision or a bounded, already-specified change, and the two
things that moved this week — the gate going green and the W9 slate closing — moved without a
ranking document. A twelfth determination written while `ADR-019` still reads `Proposed` and no `v*`
tag exists would be measuring the decision path, not informing it.
