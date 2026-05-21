# Provider Failover Hardening

## Summary

Failover orchestration under `src/Meridian.Infrastructure/Adapters/` now enforces deterministic provider selection when multiple candidates share the same priority, while preserving existing degraded-provider skip behavior.

## Deterministic Selection Contract

- Registry selection for backfill/search/streaming-capable provider metadata now applies:
  1. ascending `ProviderPriority`
  2. ascending ordinal `ProviderId` as a deterministic tie-break.
- This removes non-deterministic tie behavior that could vary based on dictionary iteration order.
- Degraded or unavailable providers are still skipped before tie-break selection is applied.

## Health Transition + Failover Behavior

- Streaming failover still triggers when a provider crosses `FailoverThreshold` and recovers once primary reaches `RecoveryThreshold`.
- Backup selection remains deterministic and respects configured backup order while skipping degraded candidates.

## Backfill/Failover Interplay

- Backfill provider selection now deterministically resolves equal-priority healthy candidates.
- If the top-priority provider is degraded/unavailable, selection advances to the next healthy candidate with the same deterministic tie-break.

## Evidence

Focused tests were added/expanded to validate:

- deterministic registry tie-break behavior;
- degraded-provider skip and health-transition behavior;
- failover/backfill interplay under equal-priority backup candidates.
