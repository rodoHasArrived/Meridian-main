---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-QUANTSCRIPT
path: src/Meridian.QuantScript
status: active
owner_lane: Strategy Analytics
last_reviewed: 2026-05-20
---

# src/Meridian.QuantScript

## Purpose

QuantScript provides scripting and strategy analytics tooling for strategy development, analysis, and operator-facing strategy workflows.

## Layer responsibility

This layer should support strategy research workflows without bypassing strategy lineage, validation, or promotion evidence.

## Key folders and files

- `Meridian.QuantScript.csproj` - QuantScript project boundary.
- Script runtime, command, and strategy analytics support files.

## Important workflows

Use this module for QuantScript execution, strategy scripting, and strategy analysis support.

### Isolated execution boundary

`ScriptRunner` never executes Roslyn or user source in the workstation/server process. Each run
starts the dedicated `Meridian.QuantScript.Worker` sidecar under `Meridian.ProcessIsolation`
containment. Normal build and publish targets place a complete worker artifact under
`workers/quant-script`; missing worker/runtime artifacts or failed containment cause the run to
fail closed.

The parent and worker communicate over inherited anonymous pipes with a length-prefixed,
versioned, bounded JSON protocol. Standard output and error are separate bounded streams and are
not protocol channels. Host market-data access crosses only a typed `IQuantDataContext` RPC seam.
Run parameters are restricted to null, scalar numeric/Boolean/string/character values, dates,
timestamps, and GUIDs; arbitrary host objects and delegates fail closed at the boundary.
Notebook continuations replay prior successful source cells in a fresh worker, so a timeout,
cancellation, or worker crash cannot leave a live Roslyn session in the host. Replay can repeat
external side effects from earlier cells; notebook authors should keep setup cells deterministic.

The host admits at most two concurrent workers and eight queued requests by default. It kills the
complete child tree on cancellation, timeout, protocol/output violation, aggregate memory/CPU/process
breach, or host-data RPC quota violation. Windows Job Objects apply kill-on-close plus hard aggregate
memory, CPU-time, and active-process limits; Linux uses aggregate `/proc` tree observation and
recursive termination as a portable fallback. `RequireHardResourceLimits=true` fails worker startup
outside the current Windows Job Object implementation. A deliberately escaping descendant can still
race the short interval between process creation and containment assignment.

Host-data RPC is preflighted before provider access for total calls, distinct symbols, and date
range. Returned records are counted before JSON serialization, and source-generated JSON writes into
a bounded stream so an oversized response is stopped before the configured aggregate response-byte
budget can be materialized. These controls do not remove the launching user's file/network
permissions. `EnableUnsafeScripts` remains a trust decision, and production/customer composition
continues to reject Quant Lab pending certification of a hardened deployment profile.

### Configuration

`QuantLab:Enabled` controls route registration and defaults off. Runtime controls bind from the
`QuantScript` section; invalid bounds fail options resolution. The defaults below are representative
of the active contract (byte values are decimal JSON numbers):

```json
{
  "QuantLab": { "Enabled": false },
  "QuantScript": {
    "RunTimeoutSeconds": 300,
    "MaxWorkerMemoryBytes": 536870912,
    "MaxMemoryDeltaBytes": 402653184,
    "MaxWorkerCpuTimeSeconds": 60,
    "MaxWorkerProcessCount": 1,
    "RequireHardResourceLimits": false,
    "MaxConcurrentWorkers": 2,
    "MaxQueuedWorkerRequests": 8,
    "WorkerQueueWaitTimeoutMilliseconds": 30000,
    "MaxHostRpcCallsPerRun": 128,
    "MaxHostRpcRecordsPerRun": 100000,
    "MaxHostRpcResponseBytesPerRun": 8388608,
    "MaxHostRpcSymbolsPerRun": 32,
    "MaxHostRpcDateRangeDays": 3660,
    "MaxWorkerProtocolBytes": 16777216,
    "MaxWorkerStandardOutputBytes": 65536,
    "MaxWorkerStandardErrorBytes": 65536
  }
}
```

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-QUANTSCRIPT -->
| Roadmap item | Title |
| --- | --- |
| `W3-CONT-001` | Research to paper continuity |
| `W6-BTSTUDIO-001` | Backtesting studio evidence loop |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-QUANTSCRIPT -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.QuantScript.Tests/Meridian.QuantScript.Tests.csproj --logger "console;verbosity=normal"
```

## Change rules

Keep script execution evidence-linked and avoid unvalidated promotion from strategy output to paper or live readiness.

## Related docs

- `docs/source/generated/source-roadmap-traceability.md`
- `archive/docs/plans/waves-2-4-operator-readiness-addendum.md`
