# Testing Documentation

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05

Use [Engineering](../engineering/README.md#buildtestrun) for current build and test commands.
This folder owns scenario-specific acceptance procedures and retained evidence.

| Task | Document | Scope |
| --- | --- | --- |
| Run the paper-cockpit regression slice | [Wave 2 acceptance tests](WAVE2_ACCEPTANCE_TESTS.md) | Automated checks and failure diagnosis. |
| Capture paper-session continuity evidence | [Wave 2 reliability runbook](wave2-cockpit-reliability-evidence-runbook.md) | Automated and manual evidence sequence. |
| Understand the original Wave 2 gates | [Wave 2 gate checklist](WAVE2_ACCEPTANCE_GATE_CHECKLIST.md) | Historical requirements and implementation snapshot. |
| Verify accounting trust corrections | [Accounting trust acceptance](accounting-trust-corrections.md) | Scenario checklist; operator decisions remain explicit. |
| Evaluate close readiness and mark freshness | [W10 operator acceptance](w10-mark-seam-operator-acceptance.md) | Candidate, population, criterion decisions, and validation limits. |
| Inspect retained W10 evidence | [Candidate evidence packet](evidence/w10-615abde9/README.md) | Results bound to its recorded commit and environment. |

A procedure describes what to verify; a retained packet records what was actually observed. Keep
packet dates, commits, and unresolved findings intact. Readiness and release acceptance remain in
the [readiness tracker](../product/implementation-todo-list.md) and [roadmap registry](../roadmap/README.md).
