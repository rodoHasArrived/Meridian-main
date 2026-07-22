---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-LAUNCHER
path: src/Meridian.Launcher
status: active
owner_lane: Runtime Host
last_reviewed: 2026-07-19
---

# src/Meridian.Launcher

## Purpose

Windows consumer entry point and the installer's single Start Menu target. It is intentionally a
thin shim over the bundled `Meridian.LifecycleSupervisor.exe`.

## Layer responsibility

The launcher assigns a unique request ID, starts or contacts the supervisor, verifies the terminal
startup receipt for that exact request, and returns the receipt's stable exit code. The supervisor owns port selection,
database and host process lifecycle, readiness, browser opening, and operational receipts.

## Key folders and files

- `Program.cs` - supervisor process invocation, terminal mapping, and user-visible recovery dialog.
- `StartupOutcomeReceiptMonitor.cs` - baseline fingerprinting and bound startup-receipt validation.
- `Meridian.Launcher.csproj` - Windows executable and publish configuration.

## Important workflows

The launcher fingerprints existing startup receipts, passes its request ID through the helper
process and named-pipe command, and accepts only a new immutable terminal outcome whose operation ID
and correlation match that request. Each retry receives a new attempt file; a concurrent request in
the same supervisor session cannot satisfy the launcher gate.

A supervisor exit without verified startup evidence fails closed even when its process exit code is
zero. `Succeeded` and `CompletedWithWarnings` return exit code `0`, `Failed` returns `1`, and
`Blocked` returns `4`. After a verified receipt, the persistent supervisor continues independently;
the launcher does not wait for the host session. Failure dialogs identify receipt and log locations
and carry repair, preflight, and retry guidance. Missing executables, process-start exceptions,
process exit without the request receipt, and launcher observation timeout retain a separate
validated launcher outcome instead of ending as message-only failures.

## Diagrams

No registry-backed diagram is assigned to this module.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-LAUNCHER -->
- No roadmap items registered.
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-LAUNCHER -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```powershell
dotnet test tests/Meridian.LifecycleSupervisor.Tests/Meridian.LifecycleSupervisor.Tests.csproj --configuration Release --disable-build-servers /m:1
```

## Module boundary

Keep this executable a receipt-verifying entrypoint. Lifecycle ownership, readiness polling, repair,
browser opening, and persistent process state remain in `Meridian.LifecycleSupervisor`.

## Change rules

Fail closed when a receipt is missing, invalid, stale, or bound to another launch. Preserve exact
operation and correlation identity and update lifecycle tests whenever startup terminal semantics or
installed paths change.

## Related docs

- `docs/adr/020-lifecycle-control-plane.md`
- `docs/adr/021-verified-operation-outcomes-and-case-history.md`
- `docs/reference/lifecycle-control-plane.md`
- `docs/reference/verified-operation-outcomes.md`
- `docs/operators/verified-outcome-recovery.md`
