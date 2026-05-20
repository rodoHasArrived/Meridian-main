# Provider Validation Evidence Artifact Schema (v1)

This document defines the fixed automation artifact contract emitted under:

`artifacts/provider-validation/_automation/<yyyy-mm-dd>/`

## Required Artifacts

1. `wave1-validation-summary.json` (machine-readable summary)
2. `wave1-validation-summary.md` (human-readable summary)
3. `dk1-pilot-parity-packet.json` + `dk1-pilot-parity-packet.md` (trust-gate packet)
4. `dk1-operator-signoff.json` (promotion sign-off metadata)

## Required Summary JSON Fields

`wave1-validation-summary.json` must include:

- `schemaVersion` (string; current: `provider-validation-evidence/v1`)
- `generatedAtUtc` (ISO-8601 UTC timestamp)
- `dateStamp` (`yyyy-mm-dd`)
- `runId` (stable run identity for the date scope)
- `configuration` (build/test configuration)
- `result` (`passed` or `failed`)
- `readinessImpact` object
  - `trustDecisionTarget`
  - `promotionRecommendation`
  - `summary`
- `calibration` object
  - `kernelVersion`
  - `sourceDocument`
- `testRunIds` (array of executed lane IDs)
- `activeProviderRows` (provider posture/lane/evidence rows)
- `steps` (per-step command status, duration, and log path)

## Readiness And Inbox Trust Mapping

- `result=failed` or any failed step keeps readiness posture blocked/degraded for promotion decisions.
- `readinessImpact.promotionRecommendation` feeds operator interpretation for:
  - `GET /api/workstation/trading/readiness`
  - `GET /api/workstation/operator/inbox`
- `calibration.kernelVersion` ties evidence to DK trust-threshold interpretation and stale-calibration checks.
- `dk1-pilot-parity-packet.json` resolves document/token and sample-review gates before operator review.

## Promotion Sign-off Responsibilities

Promotion beyond DK1 requires valid sign-off entries in `dk1-operator-signoff.json` for:

- Data Operations
- Provider Reliability
- Trading

Each approval must be bound to the exact reviewed packet metadata (path/status/generated timestamp/sample counts/contracts) before promotion can proceed.
