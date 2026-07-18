---
title: Lifecycle Control Plane Reference
status: active
owner: core-team
reviewed: 2026-07-17
audience: developers-and-operators
---

# Lifecycle Control Plane Reference

The Lifecycle Control Plane is the installed Meridian ownership and observability boundary defined
by [ADR-019](../adr/019-production-support-matrix-and-deployment-posture.md) and
[ADR-020](../adr/020-lifecycle-control-plane.md). It separates durable OS-process
ownership from cooperative application shutdown:

- The persistent per-user supervisor owns the exact host identity, the dedicated PostgreSQL
  identity, startup ordering, deadlines, restart, and the final session receipt.
- The host owns readiness evaluation, acceptance gating, drain/flush participant sequencing, and
  the host shutdown receipt.
- Browser and WPF clients observe shared contracts and issue guarded commands. They never own or
  terminate the host or database process.

## Runtime states

Lifecycle JSON enums are serialized as strings. `state` can be `Created`, `Bootstrapping`,
`Validating`, `StartingHost`, `EvaluatingReadiness`, `Ready`, `Degraded`,
`ShutdownRequested`, `Draining`, `Flushing`, `StoppingHost`, `Stopped`, or `Failed`.

`readiness` can be `Starting`, `Ready`, `Degraded`, `NotReady`, `Stopping`, or `Failed`.
A host accepts new work only when `acceptingWork` is `true` and readiness is `Ready` or
`Degraded`. A degradable failed check can produce `Degraded`; a required failed check prevents
readiness.

## HTTP surfaces

| Method and route | Authentication | Result |
| --- | --- | --- |
| `GET /livez` | None | `200` while the host process serves HTTP. |
| `GET /readyz` | None | `200` with a lifecycle snapshot only when accepting work in Ready/Degraded; otherwise `503`. |
| `GET /startupz` | None | Sanitized snapshot: `202` while starting, `200` in Ready/Degraded, `503` after failure or shutdown begins. |
| `GET /startup` | None | Pre-login startup-progress HTML. |
| `GET /api/system/lifecycle` | Loopback plus authenticated `AdminMaintenance`, or the supervisor capability | Current lifecycle snapshot. |
| `POST /api/system/shutdown` | Same guarded boundary | Accepts a typed request and returns `202`, an operation id, and a polling URI. |
| `GET /api/system/shutdown/{operationId}` | Same guarded boundary | Current shutdown stage/outcome, or `404` for another/unknown operation. |
| `GET /api/system/shutdown/receipts/latest` | Same guarded boundary | Latest host receipt, or `404` before one exists. |

Non-loopback lifecycle-control calls return `403`. Missing login/capability returns `401`; an
authenticated user without `AdminMaintenance` returns `403`. The supervisor capability is an
internal DPAPI-protected compatibility mechanism. Operators and clients must not extract, persist,
or pass it directly.

The lifecycle snapshot includes `sessionId`, state/readiness, phase timestamps, accepting/shutdown
flags, sanitized process/port/config metadata, uptime, and check rows. Each check includes a stable
id, display name, `Required` or `Degradable` requirement, status, message, timestamp, and duration.

Shutdown requests contain `reason`, optional `detail`, and optional `requestedBy`. Reasons are
`Operator`, `Restart`, `Supervisor`, `HttpLocalShutdown`, `ConsoleCancel`,
`ExternalCancellation`, `ProcessExit`, or `StartupFailure`. The host transitions through
stop-accepting, drain, flush, receipt persistence, and host release. Participant and aggregate
outcomes are `Pending`, `Succeeded`, `SucceededWithWarnings`, `TimedOut`, `Forced`, `Failed`, or
`Cancelled`.

## Supervisor commands

Run commands from the installation root:

```powershell
./Meridian.LifecycleSupervisor.exe preflight
./Meridian.LifecycleSupervisor.exe start
./Meridian.LifecycleSupervisor.exe status
./Meridian.LifecycleSupervisor.exe open
./Meridian.LifecycleSupervisor.exe restart
./Meridian.LifecycleSupervisor.exe stop
```

`start` establishes the single per-user owner and opens the browser after readiness. `run` performs
the same owner loop without opening a browser. If an owner already exists, `start` routes to its
`open` command. The command channel is a current-user-only named pipe keyed by the canonical
installation path; a per-install mutex prevents duplicate owners.

## Manifest

The supervisor creates `<install-root>\service\lifecycle-supervisor.json` when it is absent.
Schema version 1 supports:

```json
{
  "schemaVersion": 1,
  "databaseMode": "Dedicated",
  "hostRelativePath": "host\\Meridian.exe",
  "configPath": null,
  "dataRoot": null,
  "httpPort": null,
  "databasePort": 54329,
  "postgreSqlBinPath": null,
  "externalConnectionStringEnvironmentVariable": null,
  "startupTimeoutSeconds": 60,
  "shutdownTimeoutSeconds": 45,
  "databaseTimeoutSeconds": 60
}
```

Relative paths resolve beneath the installation root. A null `dataRoot` resolves to
`%LOCALAPPDATA%\Meridian\Data`; a null `httpPort` reserves an available loopback port. Timeouts must
be 1-600 seconds and ports must be valid TCP ports.

## Database ownership

`Dedicated` is the installed default. PostgreSQL binaries resolve from
`<install-root>\database\bin`, an explicit `postgreSqlBinPath`, or the developer-only
`MDC_POSTGRES_HOME` fallback. Data lives under `<data-root>\postgresql\data`; logs remain beside it.
The supervisor initializes the cluster once with SCRAM authentication, starts it before the host,
passes a loopback connection string to the host, and stops it after the host completes. Forced database termination is permitted
only after PID, executable path, start time, data directory, and port still match the process the
supervisor started.

`External` is strictly non-owning. The supervisor validates the named connection-string environment
variable, passes that value into the owned host's database-specific connection variables, starts no
database process, and never stops or force-terminates the external database.

## Receipts and secrets

Lifecycle evidence is written atomically below
`<data-root>\runtime\lifecycle\receipts`. Host receipts record participant stages, durations, and
outcomes. Supervisor session receipts combine the host receipt with database outcome and explicit
host/database forced-termination flags. Runtime identity JSON contains no shutdown secret.

The internal shutdown capability is stored only at
`%LOCALAPPDATA%\Meridian\service\lifecycle-shutdown-token.dpapi`; the dedicated PostgreSQL SCRAM
credential is stored at
`%LOCALAPPDATA%\Meridian\service\lifecycle-postgresql-password.dpapi`. Both are protected for the
current Windows user. The temporary `initdb` password file is deleted immediately after cluster
initialization. Never include either secret in logs, support bundles, runtime JSON, screenshots, or
issue text.

## Client behavior

The browser Settings diagnostics panel refreshes lifecycle state while open and requires explicit
confirmation for restart or shutdown. The WPF startup window gates sign-in on server readiness; its
Settings lifecycle page refreshes on load and exposes the same guarded operations. Closing either
client leaves the persistent service running.
