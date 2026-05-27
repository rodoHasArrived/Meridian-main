# Readiness Claim Language Policy

Use this policy for delivery-status docs that summarize operator readiness posture.

## Required Evidence Anchor

Status updates **must** include a reference to the latest pass packet path or artifact id so reviewers can verify current evidence.

Accepted examples:

- `artifacts/provider-validation/_automation/2026-05-17/dk1-pilot-parity-packet.json`
- `artifacts/provider-validation/_automation/2026-05-17/`
- `Artifact ID: DK1-PARITY-2026-05-17`

## Prohibited Phrasing

Do not use unqualified claims that imply live readiness is complete. Denylist examples:

- `production ready for live trading`
- `live-readiness complete`
- `fully ready for live trading`
- `ready for live trading`

## Approved Scoped Phrasing

Use language that explicitly scopes confidence to local/paper evidence and remaining gaps.

Accepted examples:

- `paper-trading evidence`
- `paper workflow remains in progress`
- `local/paper evidence`
- `not readiness exits`
- `live-readiness remains open`

Automated enforcement: `scripts/check_status_delivery_claims.py`.
