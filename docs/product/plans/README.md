# Product Plans

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-31

Home for product delivery plans and prioritization inputs: wave close-out plans, priority slates,
remediation plans, and next-work determinations. This collection is registered in the single
[Plans and Blueprints Register](../../engineering/blueprints/README.md#working-plans). These are
planning inputs, not status sources: live roadmap status stays in the
[roadmap registry](../../roadmap/README.md), and the
[Implementation and Readiness Tracker](../implementation-todo-list.md) remains the P0 release gate.

## Next-Work Determinations

**These are dated snapshots, not live status.** The roadmap registry
([`docs/roadmap/README.md`](../../roadmap/README.md) plus `docs/roadmap/data/*.yml`) and CI are the
live truth. Do not create a new determination report for a small change; write one only when a
release or planning decision needs a dated snapshot. Each report's body is preserved unchanged for
the historical record.

This section owns the latest prioritization pointer. Other indexes link here instead of repeating
which dated determination is latest. A determination ranks work; it does not change roadmap status
or certify a release. A verification record is not a determination: it closes a single open question
with retained evidence and carries no ranking.

| Plan | Purpose | Status |
|---|---|---|
| [What To Work On Next (2026-10-27)](next-work-determination-2026-10-27.md) | Prioritization input: the `rc.2` bar is not met (untagged, signing secrets unrefused) although its engineering half is green on `main`. | Latest dated snapshot |
| [What To Work On Next (2026-10-25)](next-work-determination-2026-10-25.md) | Prioritization input: `rc.2` bar not met; pins `zod` to clear the dashboard advisory. | Historical snapshot |
| [What To Work On Next (2026-10-24)](next-work-determination-2026-10-24.md) | Prioritization input: `rc.1` exists; finds it uncertifiable and names the `rc.2` blockers. | Historical snapshot |
| [What To Work On Next (2026-10-20)](next-work-determination-2026-10-20.md) | Prioritization input: CI-green bar met, so the RC tag has a target; records W9 close-out drift. | Historical snapshot |
| [What To Work On Next (2026-10-16)](next-work-determination-2026-10-16.md) | Carries the unmerged 2026-10-15 ranking forward and adds delivery-system findings. | Historical snapshot |
| [Certification Unblock Verification (2026-10-12)](certification-unblock-verification-2026-10-12.md) | Verification record: #3133 clears the `GHSA-vfj7-8cjw-p6xm` blocker; no ranking. | Historical snapshot |
| [What To Work On Next (2026-10-05)](next-work-determination-2026-10-05.md) | Prioritization input carried forward by later determinations; the 2026-10-10 draft (#3132) was closed unmerged. | Historical snapshot |
| [What To Work On Next (2026-10-04)](next-work-determination-2026-10-04.md) | Prioritization input, superseded by 2026-10-05. | Historical snapshot |
| [What To Work On Next (2026-10-02)](next-work-determination-2026-10-02.md) | Prioritization input, superseded by 2026-10-05. | Historical snapshot |
| [What To Work On Next (2026-09-27)](next-work-determination-2026-09-27.md) | Prioritization input, superseded by 2026-10-05; kept in place because `program-state.yml` and the readiness tracker cite it. | Historical snapshot |

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
