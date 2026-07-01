# Provider Validation Evidence Artifact Schema (v1)

**Status:** canonical  
**Owner:** core-team  
**Reviewed:** 2026-05-20

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
- `dk1-pilot-parity-packet.json` resolves document/token, sample-review, and search-dependency gates before operator review.

## DK1 Packet Search Dependency Review

`dk1-pilot-parity-packet.json` must include `searchDependencyReview` for symbol-search dependencies that can affect provider trust without being broker runtime feeds:

- `requiredCount`, `representedCount`, and `status` summarize the dependency gate.
- `dependencies[]` includes `provider`, `dependency`, `risk`, `governanceAction`, `evidenceAnchors`, `status`, and `missingRequirements`.
- OpenFIGI identifier mapping and EDGAR company ticker/reference-data endpoints are represented in this section until runtime search telemetry has its own packet input.
- Operator sign-off packet reviews bind `requiredSearchDependencyCount`, `representedSearchDependencyCount`, and `searchDependencyReviewStatus` alongside sample counts, evidence-document counts, and contract statuses.

## Promotion Sign-off Responsibilities

Promotion beyond DK1 requires valid sign-off entries in `dk1-operator-signoff.json` for:

- Data
- Provider Reliability
- Trading

Each approval must be bound to the exact reviewed packet metadata (path/status/generated timestamp/sample counts/contracts/search dependency review) before promotion can proceed.
