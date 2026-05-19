# Workstation Governance Approval Runbook

This runbook documents operator expectations for workstation governance approvals, rejections, and
reopen flows.

## Route catalog and examples

| Workflow | Method | Route | Example |
| --- | --- | --- | --- |
| Trading readiness snapshot | GET | `/api/workstation/trading/readiness` | `GET /api/workstation/trading/readiness?fundAccountId=<account-guid>` |
| Operator inbox queue | GET | `/api/workstation/operator/inbox` | `GET /api/workstation/operator/inbox?fundAccountId=<account-guid>` |
| Combined trading payload | GET | `/api/workstation/trading` | `GET /api/workstation/trading` |
| Replay explainability proof | GET | `/api/execution/sessions/{sessionId}/replay` | `GET /api/execution/sessions/abc123/replay` |

## Audit/event explainability model

Each governance transition must be explainable using a durable event envelope:

- `eventId` (stable id for cross-reference)
- `occurredAtUtc`
- `actorId` and `actorRole`
- `entityType` + `entityId`
- `fromState` + `toState`
- `reasonCode` + `reasonText`
- `metadata` (account context, policy references, replay/audit links)

Use this envelope so inbox/readiness decisions can be reconstructed during release reviews,
incident response, and regulator-facing audits.

## Rejection flow (operator guidance)

1. Move item to `Rejected` only from `InReview`.
2. Provide required metadata:
   - `rejectionCategory` (policy, data-quality, control-failure, or other controlled taxonomy)
   - `rejectionReason` (human-readable)
   - `rejectedBy`, `rejectedAtUtc`
   - remediation expectation (`nextActionOwner`, `nextActionDueAtUtc` when known)
3. Verify the rejected item remains visible in operator inbox/readiness work queues.

## Reopen flow (operator guidance)

1. Reopen only previously terminal states (`Approved`, `Rejected`, `Dismissed`).
2. Provide required metadata:
   - `reopenReason`
   - `reopenSourceEventId` (links to incident/audit trigger)
   - `reopenedBy`, `reopenedAtUtc`
3. Route reopened items back to `InReview` with a fresh assignee acknowledgment before any
   additional approval/rejection action.

## v1 additive compatibility

- Consumers must tolerate additive fields in queue/readiness payloads.
- Unknown state values should be handled as non-terminal and surfaced as warnings.
- Existing fields, routes, and current state semantics are v1-stable.
