# Runtime Component State Boundaries

## Purpose

This document inventories Meridian runtime components and classifies each component as **stateless service** or **stateful dependency** so scaling, resiliency, and failover designs can be implemented consistently.

## Classification rubric

- **Stateless service**: no durable business state retained in-process between requests/events. Can be replaced or scaled horizontally without data migration.
- **Stateful dependency**: owns durable state, mutable checkpoints, or ordered logs that affect correctness and recovery.

## Stateless services

| Component | Runtime role | Notes |
| --- | --- | --- |
| `src/Meridian/Meridian.csproj` host process | API + orchestration host | Stateless when pointed at externalized storage/checkpoints. |
| `src/Meridian.Ui.Shared` endpoint surface | HTTP route composition, DTO projection | Stateless; sources state from services/providers. |
| `src/Meridian.Ui.Services` API adapters | UI/host connection and transport logic | Stateless transport + mapping layer. |
| `src/Meridian.Wpf` desktop shell process | Operator shell and workflow execution | Treat as stateless client runtime; critical state is server-side. |
| Provider adapters (`IMarketDataClient`, `IHistoricalDataProvider`) | Feed and backfill connectors | Keep sessions ephemeral; reconnect-safe through persisted checkpoints. |
| Pipeline processors (bounded channels and handlers) | Normalize/transform market events | Must remain replayable from durable input + checkpoints. |

## Stateful dependencies

| Dependency | State ownership | Boundary contract |
| --- | --- | --- |
| Write-ahead log (WAL) / event archives | Ordered ingest history and recovery source | Producers append-only; consumers replay idempotently. |
| Storage sinks (`IStorageSink`) | Durable bars/trades/depth datasets | Versioned schema and integrity validation required. |
| Checkpoint stores | Cursor/offset progression and replay continuity | Updates must be monotonic and atomic. |
| Reconciliation and accounting ledgers | Fund accounting truth and break queues | Strict consistency + audit traceability required. |
| Secrets/config stores | Runtime credentials and environment overlays | Read-only to app at runtime; rotate without rebuild. |
| Metrics/time-series backend | SLO, latency, backlog, and burn-rate evidence | Centralized sink for autoscaling and dashboarding. |

## Service boundaries

1. **Ingress boundary**: provider adapters -> bounded ingress queues.
2. **Processing boundary**: processor workers -> normalized event stream.
3. **Persistence boundary**: storage sink + WAL append with checkpoint updates.
4. **Operator boundary**: workstation endpoints aggregate read-models only.
5. **Control boundary**: health/readiness/metrics endpoints expose runtime posture but do not mutate domain state.

## Required reliability controls by boundary

- Ingress: bounded queues + overload shedding + retry with jitter for transient transport errors.
- Processing: worker concurrency caps + circuit breaker on repeated downstream failures.
- Persistence: dead-letter capture for non-retriable records and explicit replay tooling.
- Operator: degraded dependency surfacing in readiness payload and workstation status panels.

## Scaling policy anchors

- Horizontal scale **stateless services** based on queue depth, p95 processing latency, CPU%, memory%, and error budget burn.
- Scale or fail over **stateful dependencies** only through runbook-governed promotion and verified checkpoints.
