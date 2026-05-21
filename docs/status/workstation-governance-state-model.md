# Workstation Governance State Model (v1)

**Last Updated:** 2026-05-19  
**Compatibility Contract:** v1 additive-only

This document defines the shared workstation governance lifecycle used by readiness and operator
inbox workflows.

## v1 compatibility expectation (additive)

- v1 consumers must tolerate **additive** fields in objects and additive enum values.
- Existing fields and current enum semantics are stable in v1 and must not be repurposed.
- New states may be introduced as additive values; clients should default unknown states to
  non-terminal warning behavior and preserve raw state text for diagnostics.

## Canonical state model

| State | Meaning | Terminal | Required metadata |
| --- | --- | --- | --- |
| `Open` | Work item was created and awaits owner action. | No | `createdAtUtc`, `createdBy`, `reasonCode` |
| `InReview` | Work item is actively triaged by an assignee/reviewer. | No | `assignedTo`, `reviewStartedAtUtc` |
| `Approved` | Governance-approved and eligible for downstream progression. | Yes | `approvedBy`, `approvedAtUtc`, `approvalNote` |
| `Rejected` | Governance decision blocked progression; corrective follow-up required. | Yes | `rejectedBy`, `rejectedAtUtc`, `rejectionReason`, `rejectionCategory` |
| `Reopened` | Previously terminal item was reopened due to new evidence or policy drift. | No | `reopenedBy`, `reopenedAtUtc`, `reopenReason`, `reopenSourceEventId` |
| `Dismissed` | Item intentionally suppressed with audit justification. | Yes | `dismissedBy`, `dismissedAtUtc`, `dismissReason` |

## Transition table

| From | Event | To | Guardrails |
| --- | --- | --- | --- |
| `Open` | Assign/Triage | `InReview` | Must capture assignee identity and ownership timestamp. |
| `InReview` | Approve | `Approved` | Must include approval note and policy scope reference. |
| `InReview` | Reject | `Rejected` | Must include rejection category, reason, and corrective action hint. |
| `Open`/`InReview` | Dismiss | `Dismissed` | Must include dismissal reason and scope (account/global). |
| `Approved`/`Rejected`/`Dismissed` | Reopen | `Reopened` | Must cite source event/audit trigger and reopen reason. |
| `Reopened` | Resume review | `InReview` | Requires new assignee acknowledgment. |

## Governance and approval requirements

1. Every terminal decision (`Approved`, `Rejected`, `Dismissed`) must capture operator identity,
   UTC timestamp, and free-text rationale.
2. Rejection must include a structured category and a next-step remediation expectation.
3. Reopen must include provenance (`reopenSourceEventId`) to tie the action back to the triggering
   audit/event evidence.
4. Account-scoped actions must retain `fundAccountId` when present to preserve account operating
   context.
