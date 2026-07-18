# ADR-020: Local Lifecycle Control Plane

**Status:** Proposed
**Date:** 2026-07-17
**Deciders:** core-team
**Supersedes:** —
**Superseded by:** —

## Context

The supported workstation currently spreads lifecycle ownership across process-signal handlers,
host mode runners, HTTP shutdown endpoints, PowerShell launchers, and optional graceful-shutdown
services. A single cancellation token represents both "stop accepting work" and "terminate", the
startup timer includes the full process lifetime, readiness checks only pipeline utilization, and
the launcher cannot complete dedicated PostgreSQL shutdown after the host exits.

The supported topology in [ADR-019](019-production-support-matrix-and-deployment-posture.md)
therefore needs one explicit control plane that coordinates the per-user supervisor, host,
dedicated database, browser workstation, and WPF shell without creating a remote or multi-node
deployment.

## Decision

### Two cooperating authorities

1. `Meridian.LifecycleSupervisor.exe` owns external process lifecycle. It provides `start`, `stop`,
   `restart`, `status`, `preflight`, and internal `run` commands; enforces one active session per
   installation; starts and stops the host; and owns dedicated PostgreSQL when the manifest selects
   `dedicated` database mode.
2. The in-process runtime lifecycle control plane owns startup phases, readiness, shutdown-request
   idempotency, accepting-work state, deterministic drain/flush, host receipt persistence, and the
   final termination signal.

The primary local control channel is a named pipe created with `PipeOptions.CurrentUserOnly`.
Loopback HTTP remains the browser control surface and preserves authentication, authorization, and
CSRF enforcement. Runtime state files are diagnostic projections, not command channels.

### Host lifecycle states

```text
Created -> Bootstrapping -> Validating -> StartingHost -> EvaluatingReadiness
        -> Ready | Degraded
        -> ShutdownRequested -> Draining -> Flushing -> StoppingHost
        -> Failed
```

The host exposes separate `StopWorkToken` and `TerminationToken` values. A shutdown request first
cancels `StopWorkToken`; mode runners exit only after the ordered shutdown sequence persists the
host receipt and cancels `TerminationToken`.

### Supervisor lifecycle states

```text
Stopped -> Preflight -> StartingDatabase -> StartingHost -> WaitingForReadiness
        -> Running | Degraded
        -> StoppingHost -> StoppingDatabase -> Stopped | Failed
```

### Readiness and startup contract

- `/healthz` and `/livez` report process liveness only.
- `/ready` and `/readyz` return success only when required checks pass and the host accepts work.
- `/startupz` exposes sanitized pre-login progress: `202` while starting, `200` when ready or
  degraded, and `503` when failed or stopping.
- `/api/system/lifecycle` exposes the authenticated lifecycle snapshot.
- Required checks cover configuration, writable `DataRoot`, authentication, workstation assets,
  configured PostgreSQL, and pipeline capacity. Optional providers are degradable unless their
  configuration marks them required.

### Deterministic shutdown

One `RuntimeShutdownSequence` executes registered `IRuntimeShutdownParticipant` instances in stable
stage and order sequence:

1. stop accepting new work;
2. drain in-flight work;
3. flush durable state;
4. atomically persist the host receipt;
5. release the host termination token.

`EventPipeline` is registered exactly once because its flush already drains its channel and flushes
its storage sink. Legacy graceful-shutdown services may exist as compatibility adapters for one
release but must never be registered alongside the authoritative sequence.

### Receipts and deadlines

Host receipts are atomically written under `<DataRoot>/runtime/lifecycle/receipts/`. The supervisor
adds host-exit, database-stop, escalation, and final-session results to a combined session receipt.
Defaults are 60 seconds for startup, 45 seconds for host shutdown, 10 seconds per shutdown
participant, and 60 seconds for dedicated-database shutdown.

### Security and process identity

- The named pipe is current-user-only.
- Raw shutdown tokens are not stored in runtime JSON.
- Browser shutdown requires loopback, `AdminMaintenance`, and CSRF validation.
- Pre-login status excludes secrets, absolute paths, connection strings, and command lines.
- Before signalling or terminating a process, the supervisor verifies PID, executable path, and
  process start time. Database ownership additionally verifies its data directory and port.
- `external` database mode is observation-only and never grants process ownership.

## Implementation Links

| Component | Location | Purpose |
|-----------|----------|---------|
| Shared contracts | `src/Meridian.Contracts/Lifecycle/` | Lifecycle states, checks, operations, and receipts |
| Runtime coordinator | `src/Meridian.Application/Composition/Startup/` | Startup, readiness, shutdown, and token authority |
| Receipt store | `src/Meridian.Storage/Runtime/` | Atomic lifecycle receipt persistence |
| HTTP surface | `src/Meridian.Ui.Shared/Endpoints/`, `src/Meridian/UiServer.cs` | Startup, readiness, lifecycle, and shutdown operations |
| Supervisor | `src/Meridian.LifecycleSupervisor/` | Process and dedicated-database lifecycle ownership |
| Browser | `src/Meridian.Ui/dashboard/` | Startup Center and Settings lifecycle panel |
| WPF | `src/Meridian.Wpf/` | Startup progress and lifecycle operations |
| Installer | `build/scripts/install/install-web-workstation.ps1` | Supervisor manifest and compatibility launcher |

## Rationale

A persistent supervisor can observe the host exit and still complete database shutdown, cleanup,
and receipt persistence. Keeping drain and flush ownership inside the host preserves dependency
injection, cancellation, and durable-service boundaries. Separate tokens prevent process
termination from racing the durability sequence, while the shared contracts keep browser and WPF
state consistent.

## Alternatives Considered

### PowerShell-only orchestration

**Pros:** no additional executable.

**Cons:** weak process identity, difficult current-user IPC, fragmented state handling, and no
reliable owner after the host exits.

**Why rejected:** it cannot provide the required recovery and receipt guarantees.

### Windows Service

**Pros:** machine-wide lifetime and service-control integration.

**Cons:** elevation, installation complexity, multi-user ambiguity, and a broader production
topology than ADR-019 authorizes.

**Why rejected:** the v1 envelope is per-user and single-operator.

### Host owns PostgreSQL directly

**Pros:** fewer processes.

**Cons:** the owner disappears before it can confirm database shutdown or record the final result.

**Why rejected:** process ownership must survive host termination.

## Consequences

### Positive

- Startup, readiness, shutdown, and recovery have one observable state model.
- Dedicated PostgreSQL has explicit, verifiable ownership.
- Browser and WPF consume the same lifecycle contracts.
- Shutdown results survive process termination as auditable receipts.

### Negative

- Packaging gains a second Meridian executable and manifest.
- Installer and smoke-test coverage must validate process identity and database modes.
- Compatibility launchers require a migration period.

### Neutral

- Existing `/healthz` and `/livez` routes retain their liveness meaning.
- External PostgreSQL remains supported but is never controlled by Meridian.

## Compliance

### Code Contracts

Implementations must provide `IRuntimeLifecycleControlPlane`, `IRuntimeReadinessCheck`,
`IRuntimeShutdownParticipant`, and `ILifecycleReceiptStore`. JSON lifecycle contracts must use a
source-generated serializer context, and lifecycle-sensitive methods must propagate cancellation.

### Runtime Verification

- Concurrent start requests resolve to the active session.
- Duplicate shutdown requests resolve to the active shutdown operation.
- Required readiness failures return `503` and prevent login launch.
- `EventPipeline` and its underlying storage sink are not flushed twice.
- Forced termination requires identity revalidation and is recorded in the receipt.
- External database mode never starts or stops PostgreSQL.

## References

- [ADR-019: Production Support Matrix and Typed Deployment Posture](019-production-support-matrix-and-deployment-posture.md)
- [Production-readiness tracker](../product/implementation-todo-list.md)
- [Operator documentation](../operators/README.md)
- [Architecture module map](../architecture/module-map.md)

---

*Last Updated: 2026-07-17*
