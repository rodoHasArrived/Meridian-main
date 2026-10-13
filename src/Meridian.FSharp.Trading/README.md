---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-FSHARP-TRADING
path: src/Meridian.FSharp.Trading
status: active
owner_lane: Execution and Fund Accounts
last_reviewed: 2026-05-20
---

# src/Meridian.FSharp.Trading

## Purpose

FSharp Trading contains the deterministic strategy lifecycle state machine and its C# interoperability boundary.

## Layer responsibility

This layer decides lifecycle transitions without broker, storage, or UI side effects. C# adapters execute hooks and apply completion signals; the lifecycle manager retains intent and verified outcomes.

## Key folders and files

- `Meridian.FSharp.Trading.fsproj` - F# trading project boundary.
- `StrategyRunTypes.fs` and `StrategyLifecycleState.fs` - commands and lifecycle states.
- `StrategyLifecycleTransitions.fs` - admission, completion, and recovery rules.
- `Interop.fs` - structured transition verdicts and fault diagnostics for C# callers.
- `PromotionReadiness.fs` - terminal-state classification; this does not establish promotion eligibility.

## Important workflows

Use this module for lifecycle decisions that need deterministic scenario coverage.

Start enters `WarmingUp`; `WarmupCompleted` enters `Running`. Only a running strategy can pause.
Starting a paused strategy resumes its warmed state without repeating initialization. Stop enters
`Stopping`; only `StopCompleted`, after successful cleanup, enters `Stopped`. Repeated stop requests
while stopping are invalid. A faulted strategy requires successful cleanup before restarting through
warmup; failed or cancelled cleanup keeps it faulted.

Interop requires an explicit, case-sensitive state token. Missing, blank, or unsupported state
names return an invalid verdict with unchanged state and no emitted facts. Failure reasons must
be nonblank. Overloads accept the current fault reason, and verdicts retain previous and next
fault diagnostics separately from a command rejection reason.

## Diagrams

See `DIA-PAPER-SESSION-REPLAY` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-FSHARP-TRADING -->
| Roadmap item | Title |
| --- | --- |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W3-CONT-001` | Research to paper continuity |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-FSHARP-TRADING -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj /p:FSharpTestSlice=Trading --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
```

## Change rules

Keep calculation outputs stable and covered before wiring them into execution or UI flows.

## Related docs

- `docs/source/generated/source-roadmap-traceability.md`
- `docs/ai/claude/CLAUDE.fsharp.md`
