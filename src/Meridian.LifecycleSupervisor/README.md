---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-LIFECYCLE-SUPERVISOR
path: src/Meridian.LifecycleSupervisor
status: active
owner_lane: Runtime Host
last_reviewed: 2026-07-17
---

# src/Meridian.LifecycleSupervisor

## Purpose

Persistent per-user owner for the installed Meridian host and its dedicated local PostgreSQL
instance. It exposes `start`, `run`, `stop`, `restart`, `status`, and `preflight` commands over a
current-user-only named pipe. External-database mode validates configuration but never starts,
stops, or force-terminates the external database.

## Layer responsibility

This module owns installed-process identity, start/stop ordering, database lifecycle, readiness
waits, forced-termination safeguards, and session receipts. The host owns its cooperative drain and
flush sequence; the supervisor owns the final process deadline and only terminates an exact process
identity that it started.

## Key folders and files

- `LifecycleSupervisorRuntime.cs` - persistent owner loop and host/session sequencing.
- `LifecycleSupervisorDatabase.cs` - dedicated PostgreSQL preflight, initialization, and ownership.
- `LifecycleSupervisorPipe.cs` - current-user-only command channel.
- `LifecycleSupervisorConfiguration.cs` - manifest validation, paths, and per-install identity.

## Important workflows

The consumer-facing `Meridian.exe` launcher is a thin shim over this executable. Runtime identity
and session receipts live below the configured data root; the compatibility HTTP shutdown token is
stored only as a DPAPI-protected sidecar and is never written into runtime JSON.

The dedicated cluster is initialized with SCRAM authentication. Its generated password is retained
in a separate current-user DPAPI sidecar and injected only into the owned host process environment.
Initialization occurs in an operation-specific staging directory so cancellation cannot leave a
partial cluster at the authoritative data path. External mode fans its explicitly named connection
string into the owned host process but retains no database ownership.

The default installed layout resolves the host from `host/Meridian.exe`, PostgreSQL binaries from
`database/bin`, and data from `%LOCALAPPDATA%\Meridian\Data`. A manifest at
`service/lifecycle-supervisor.json` can select a fixed loopback port, alternate data/config paths,
or external-database mode. Relative manifest paths resolve beneath the install root.

## Diagrams

No registry-backed diagram is assigned to this module.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-LIFECYCLE-SUPERVISOR -->
- No roadmap items registered.
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-LIFECYCLE-SUPERVISOR -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```powershell
dotnet test tests/Meridian.LifecycleSupervisor.Tests/Meridian.LifecycleSupervisor.Tests.csproj --configuration Release --disable-build-servers /m:1
```

## Module boundary

Preserve the host-plus-supervisor ownership split authorized by ADR-019. Do not move application
drain/flush work into the supervisor, reintroduce launcher-owned raw shutdown tokens, or allow the
supervisor to terminate an external database.

## Change rules

Keep per-user IPC current-user-only, revalidate exact process identity before escalation, preserve
atomic receipts, and update installer plus x64/ARM64 smoke coverage when the installed layout changes.

## Related docs

- `docs/adr/019-production-support-matrix-and-deployment-posture.md`
- `docs/adr/020-lifecycle-control-plane.md`
- `docs/reference/lifecycle-control-plane.md`
- `docs/operators/browser-workstation-installer.md`
