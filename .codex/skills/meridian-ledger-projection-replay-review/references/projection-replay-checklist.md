# Projection Replay Checklist

Use this checklist when immutable journal facts are replayed into balances, capital-account state,
close packages, reporting read models, or audit evidence.

## Required Inputs

- Durable inputs: immutable journal facts, retained evidence ids, projection version, and replay
  batch identity.
- Ordering: event sequence, posting date, effective date, correlation id, causation id, and tie-break
  rule for deterministic ordering.
- Replay controls: idempotent replay, duplicate detection, out-of-order handling, stale projection
  invalidation, and rebuild checkpoints.
- Consumers: balance read model, capital account impact, close package, report line, delivery record,
  and audit packet where applicable.

## Fail-Closed Conditions

- Replay can rewrite posted journal facts.
- Duplicate events can double count balances.
- Out-of-order events produce different results without detection.
- Projection version changes lack migration, backfill, or report-line provenance.
- Balance drift is not detectable or explainable from retained facts and evidence.

## Expected Output

Return a replay risk map, rebuild assumptions, projection-version posture, impacted consumers,
required tests, validation commands, and residual projection risk.
