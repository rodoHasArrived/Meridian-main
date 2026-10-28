# Product Plans

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-20

Home for product delivery plans and prioritization inputs: wave close-out plans, priority slates,
remediation plans, and next-work determinations. This collection is registered in the single
[Plans and Blueprints Register](../../engineering/blueprints/README.md#working-plans). These are
planning inputs, not status sources: live roadmap status stays in the
[roadmap registry](../../roadmap/README.md), and the
[Implementation and Readiness Tracker](../implementation-todo-list.md) remains the P0 release gate.

## Next-Work Determinations

This section owns the latest prioritization pointer. Other indexes link here instead of repeating
which dated determination is latest. A determination ranks work; it does not change roadmap status
or certify a release. A verification record is not a determination: it closes a single open question
with retained evidence and carries no ranking.

| Plan | Purpose |
|---|---|
| [What To Work On Next (2026-10-28)](next-work-determination-2026-10-28.md) | Latest prioritization input. Records that the 2026-10-27 bar is **not** met — `v0.1.0-rc.2` is untagged (two tags only, unchanged for a week) and no `decision-log.yml` entry refuses the signing secrets — with a same-day receipt: `Release Readiness` run 40294900031 was re-run as attempt 2 at 05:34 UTC and `check-protected-signing-readiness` failed at certificate inspection, so **the release has been held seven days on two repository secrets**. Finds `main` still rc.2-eligible (`Meridian CI` run 6013 and `Production Certification` run 335 both green on `79e6994f`) and the engineering queue behind it essentially drained: open PRs fell 46 → 14, the five superseded determinations and the security-master stack are closed, #3232 merged and `Generated Repair Publisher` runs 756–760 are green with the base-SHA guard string gone from the tree, and the conflict tax collapsed to 71 conflicts over 99 conflicted files across 5 of 14 PRs (97.0% resolver-covered, 88 of them on #3311 alone) with hand-written entries down 48 → 3 and every tracked hotspot at zero — so it **drops** the resolver campaign and the `.gitattributes` item as no longer worth the work. Promotes the `verify-workflows` races to the largest remaining engineering item, having grown to 368 `tests/scripts` call sites across 116 files (up from 321/98) with zero `ignore_cleanup_errors`. Records the first roadmap movement in four passes — 48 → 53 rows, five new `W12` rows under `DEC-FUND-OPERATIONS-DIRECTION-001`, and `W10-RECON-001` `planned` → `in_progress`/`yellow` after #3004 merged — while holding that this breaches no user-queue ordering. Corrects the `vitest` item: npm names the fix as `4.1.12`, `isSemVerMajor: false`, so #3300's `5.0.5` major is more change than the advisories need |
| [What To Work On Next (2026-10-27)](next-work-determination-2026-10-27.md) | Newest determination merged to `main` before 2026-10-28. Records that the 2026-10-25 bar is **not** met — `v0.1.0-rc.2` is untagged and the signing secrets are unrefused — but that its engineering half is complete: `4fb27518` is the first `main` head green across both `Meridian CI` (all seven jobs) and `Production Certification`, ending eight consecutive red pushes, so the tracker's `rc.2` precondition is satisfied. Finds that tagging now would burn a second immutable tag, because `check-release-signing.ps1` fails closed with `signing-secrets-unavailable` before any packaging job, and names `Release Readiness` (`workflow_dispatch`, two runs in its history) as the non-destructive way to test the secrets. Re-measures the conflict tax at 452 conflicts over 833 conflicted files across 34 of 45 measured PRs (94.2% resolver-covered) — flat at +3, with 82% of it concentrated on five PRs (#3169, #3168, #3119, #2896, #3004) as `rename/rename` on content-hashed workstation bundle assets — and holds #3277 (`typescript` 7.0.2 outside every `typescript-eslint` peer range) while ranking #3275 as the queue's safest merge |
| [What To Work On Next (2026-10-25)](next-work-determination-2026-10-25.md) | Newest determination merged to `main` before 2026-10-27. Records that the 2026-10-24 bar is **not** met — `v0.1.0-rc.2` is untagged and the signing secrets are unrefused — and that of the predecessor's three P0 items one self-healed (`Meridian CI` is green again on `main`, confirming the transient-`pwsh` diagnosis), one stayed human (the `desktop-release-signing` secrets), and one went unactioned. Lands that third item rather than re-recommending it: `zod` is pinned to `4.6.7` through the dashboard's existing `overrides` block, clearing `GHSA-mrhx-c7xc-m3w5` and the five-push `Production Certification` red, with the gate reproduced FAIL-then-PASS, 43/43 dashboard tests, and a byte-identical built `wwwroot` bundle. Re-measures the generated-conflict load at 449 conflicts across 34 of 40 open PRs (94.6% resolver-covered) and identifies `docs/product/plans/README.md` as a new hand-written hotspot manufactured by this determination lane itself |
| [What To Work On Next (2026-10-24)](next-work-determination-2026-10-24.md) | Newest determination merged to `main` before 2026-10-25. Records that the 2026-10-20 bar is met — `v0.1.0-rc.1` exists on `c51c5d8d` and its first post-tag blockers are found — and that every 2026-10-20 P0 decision cleared (ADR-019/ADR-020 signed, platform fail-closed landed, `PRD-002` C7/C8 closed, registry drift reconciled). Finds the tag uncertifiable on two counts (tag run failed closed on `signing-secrets-unavailable`; `c51c5d8d`'s `Meridian CI` failed on `verify-docs`), identifies a red `Production Certification` on `main` from an unaccepted high `zod` advisory as the blocker for `rc.2`, and diagnoses the head's `verify-workflows` red as a transient `pwsh` SIGABRT needing only a re-run |
| [What To Work On Next (2026-10-20)](next-work-determination-2026-10-20.md) | Newest determination merged to `main` before 2026-10-24. Records that the 2026-10-16 bar is met — `Meridian CI / quality-gate` green on six consecutive `main` pushes and a ten-workflow green receipt on `a9d470c3` — so the RC tag finally has a target; reframes `PRD-002`'s non-Windows key clause as a missing fail-closed platform rejection inside ADR-019's Windows-only v1 envelope; records the closed W9 slate and the resulting `program-state.yml` drift; and re-measures the generated-conflict load at 360 of 367 conflicts across 23 of 32 open PRs |
| [What To Work On Next (2026-10-16)](next-work-determination-2026-10-16.md) | Newest determination merged to `main` before 2026-10-20. Does not re-rank: it carries the unmerged 2026-10-15 determination (#3170) forward intact and adds three delivery-system findings — a red `Meridian CI / quality-gate` on `main`, a `Generated Repair Publisher` with no green run in its history, and an open-queue conflict load that is entirely generated files |
| [Certification Unblock Verification (2026-10-12)](certification-unblock-verification-2026-10-12.md) | Verifies that #3133 clears the `GHSA-vfj7-8cjw-p6xm` certification blocker and states the merge order; carries no ranking of its own |
| [What To Work On Next (2026-10-05)](next-work-determination-2026-10-05.md) | Newest determination merged to `main` before 2026-10-16; its ranking was carried forward by the unmerged 2026-10-15 determination (#3170). The 2026-10-10 determination (#3132) was closed unmerged on 2026-10-16 |
| [What To Work On Next (2026-10-04)](next-work-determination-2026-10-04.md) | Superseded by the 2026-10-05 determination |
| [What To Work On Next (2026-10-02)](next-work-determination-2026-10-02.md) | Superseded by the 2026-10-05 determination |
| [What To Work On Next (2026-09-27)](next-work-determination-2026-09-27.md) | Superseded by the 2026-10-05 determination; retained in place because `docs/roadmap/data/program-state.yml` and the readiness tracker cite it as the recorded operator-session plan |

## Delivery Plans and Slates

| Plan | Purpose |
|---|---|
| [2026-08 W9 Close-Out Delivery Plan](w9-close-out-delivery-plan-2026-08.md) | Sequence for the W9 close-out rows (`DEC-W9-CLOSEOUT-001`) |
| [2026-07 First-Order Improvement Slate](product-roadmap-priorities-2026-07.md) | Ranked W9 priority rationale (`DEC-PRIORITY-SLATE-001`) |
| [2026-07 Depth Slate](w10-depth-slate-2026-07.md) | W10 rationale for deepening existing functionality (`DEC-DEPTH-SLATE-001`) |
| [Adversarial Review 2026-08 Remediation Plan](adversarial-review-2026-08-remediation-plan.md) | Every 2026-08 review finding as a tracked, code-ready todo |
| [Production-Readiness Backlog (2026-08)](production-readiness-backlog-2026-08.md) | Ten-item production-readiness ordering |

When a newer determination or slate supersedes one of these, update this index. Archive the old
file under [`archive/docs/plans/`](../../../archive/docs/plans/README.md) once active evidence
references no longer require its path; otherwise label it superseded and explain why it remains.
Update the register when a document is added, moved, or retired.
