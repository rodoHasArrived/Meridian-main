# Product Plans

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05

Home for product delivery plans and prioritization inputs: wave close-out plans, priority slates,
remediation plans, and next-work determinations. This collection is registered in the single
[Plans and Blueprints Register](../../engineering/blueprints/README.md#working-plans). These are
planning inputs, not status sources: live roadmap status stays in the
[roadmap registry](../../roadmap/README.md), and the
[Implementation and Readiness Tracker](../implementation-todo-list.md) remains the P0 release gate.

## Next-Work Determinations

This section owns the latest prioritization pointer. Other indexes link here instead of repeating
which dated determination is latest. A determination ranks work; it does not change roadmap status
or certify a release.

| Plan | Purpose |
|---|---|
| [What To Work On Next (2026-10-04)](next-work-determination-2026-10-04.md) | Latest prioritization input |
| [What To Work On Next (2026-10-02)](next-work-determination-2026-10-02.md) | Superseded by the 2026-10-04 determination |
| [What To Work On Next (2026-09-27)](next-work-determination-2026-09-27.md) | Superseded by the 2026-10-04 determination; retained in place because `docs/roadmap/data/program-state.yml` and the readiness tracker cite it as the recorded operator-session plan |

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
