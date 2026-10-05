# Runtime Component State Boundaries

## Purpose

This document distinguishes reusable processing services from the state and ownership controls
that determine restart, failover, and deployment safety. Meridian's current architecture is a
[modular operational monolith](../adr/017-modular-operational-monolith.md); a service interface does
not imply an independently deployable or automatically scalable service.

Maintenance check 2026-10-05: state classification and write boundaries were checked against the
[host source guide](../../src/Meridian/README.md),
[UI Services guide](../../src/Meridian.Ui.Services/README.md), and
[credential-store contract](../../src/Meridian.DataIntegration/Credentials/IProviderCredentialStore.cs).
This check does not certify multi-instance deployment or failover.

## Classification rubric

- **Stateless computation**: business results derive from explicit inputs and owned dependencies;
  no independent durable authority lives in the computation itself.
- **Stateful dependency**: retains authoritative records, checkpoints, credentials, ordered logs,
  or ownership needed for correctness and recovery.
- **Stateful runtime**: coordinates in-flight work, subscriptions, queues, locks, or sessions.
  Some state can be rebuilt, but restart and concurrent ownership still require explicit rules.

## Stateless services

These are logical responsibilities, not a claim that their containing process can be replicated:

| Component | Runtime role | State and ownership limits |
| --- | --- | --- |
| `Meridian.Ui.Shared` endpoint surface | HTTP dispatch, DTO projection, and governed command admission | Reads and writes delegate to owned services; authentication, scoped authority, idempotency, and persistence guards still apply. |
| Pure domain rules and projections | Validate inputs and derive results | Keep correctness independent of UI state; authoritative inputs and retained outputs belong to their owning stores. |
| API transport adapters | Send authorized commands and read shared results | Session, retry, and client cache state are not a second source of financial truth. |

## Stateful dependencies

| Dependency | State ownership | Boundary contract |
| --- | --- | --- |
| Host process and local data root | Coordinates workers and retains file-backed state, including identity/session data | Externalizing one database does not make the host stateless; inventory every store, worker, and ownership boundary before running concurrent instances. |
| WAL and event archives | Ordered ingest history and recovery source | Append durably and replay idempotently. |
| Storage sinks and checkpoint stores | Datasets, cursor progression, and replay continuity | Preserve versioned schemas and integrity checks; checkpoint updates must be atomic and monotonic within their replay/job contract. |
| Reconciliation and accounting stores | Governed records, journal truth, break queues, and evidence | Preserve transactions, scoped authority, optimistic concurrency, and audit lineage. |
| Credential/configuration stores | Encrypted secrets, ownership, verification, and configuration | Authorized runtime APIs can save, rotate, verify, and remove records; environment fallback is a separate store-owned policy. |
| Metrics and time-series backend | Operational telemetry and SLO evidence | Use the retained application record for financial or workflow authority. |

Provider adapters also hold connections and subscriptions. Pipelines and worker services retain
in-flight queues and ownership. Reconnect or replay capability must not be mistaken for safe
concurrent execution. WPF and browser clients have presentation/session state; financial authority
belongs to the shared governed service and store.

## Service boundaries

1. **Ingress boundary**: provider adapters feed bounded queues under their subscription owner.
2. **Processing boundary**: workers normalize events with explicit overload and cancellation policy.
3. **Persistence boundary**: writers coordinate durable data, evidence, and checkpoints under the
   relevant [write-path invariants](write-path-invariants.md).
4. **Operator boundary**: workstation endpoints expose both read models and governed mutations;
   commands must preserve authorization, scope, concurrency, idempotency, and evidence controls.
5. **Control boundary**: health/readiness/metrics reads report runtime posture. Separate lifecycle
   commands can change process state and have their own authorization and ownership requirements.

## Required reliability controls by boundary

- Ingress: bounded queues and an explicit, observable overload policy.
- Processing: bounded worker concurrency, cancellation, transient retry with jitter, and circuit
  breakers for repeated downstream failures.
- Persistence: dead-letter or other durable failure evidence and bounded replay or recovery for the
  owning operation.
- Operator: visible dependency failures, blocked outcomes, and supported recovery actions.

## Scaling policy anchors

Queue depth, latency, CPU, and error-budget data can inform capacity planning. Before adding a host
or worker, establish shared-store support, leader/job ownership, replay/idempotency behavior, and
session/credential continuity for that deployment. Use the [deployment guide](../operators/deployment-packaging.md)
and [failover runbook](../operators/failover-and-recovery.md); a high queue-depth reading alone does
not authorize additional concurrent owners.
