# SOC 2 Evidence Calendar (Meridian)

**Last Updated:** 2026-05-27

This cadence defines minimum evidence collection frequency and review ownership for SOC readiness, Type I preparation, and Type II sustainment.

## Monthly cadence

| Evidence stream | Collection action | Owner | Due (monthly) |
|---|---|---|---|
| Access control posture | Capture auth/RBAC configuration snapshot, permission changes, and credential workflow checks. | Security Lead | 5th business day |
| Vulnerability and dependency posture | Record current vulnerability status, accepted risks, and remediation deltas. | Security Lead | 5th business day |
| Change-management evidence | Archive build/test/security workflow runs and release validation evidence. | Release Manager | 7th business day |
| Operations runbook execution | Capture preflight/deployment/reconciliation runbook artifacts for the period. | Operations Lead | 7th business day |
| Provider governance | Archive provider validation outcomes and degradation/promotion decisions. | Data Platform Lead | 10th business day |

## Quarterly cadence

| Evidence stream | Collection action | Owner | Due (quarterly) |
|---|---|---|---|
| Threat model review | Revalidate trust boundaries, attack surfaces, and residual risks. | Security Lead | Quarter +10 business days |
| Scope and control matrix review | Confirm in-scope systems, control ownership, and evidence source integrity. | Security Lead + Engineering Manager | Quarter +10 business days |
| DR/availability review | Validate SLO posture and high-availability/recovery assumptions. | Operations Lead | Quarter +12 business days |
| Vendor/provider risk review | Review provider endpoint governance, credential workflows, and calibration policy updates. | Data Platform Lead | Quarter +12 business days |
| Executive compliance checkpoint | Review evidence completeness, gaps, and remediation commitments. | Program Manager | Quarter +15 business days |

## Evidence packaging standards

- Store each monthly and quarterly packet under a dated folder structure (e.g., `YYYY/MM` and `YYYY/Q#`).
- Include source links to Meridian docs plus generated artifacts (logs, CI reports, screenshots, approvals).
- Maintain reviewer sign-off per packet (preparer + reviewer).

## Escalation SLA

- Missing monthly evidence older than 10 business days is escalated to Engineering Manager.
- Missing quarterly evidence older than 15 business days is escalated to executive sponsor.
