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

FSharp Trading contains functional trading calculations and models that support execution and research workflows.

## Layer responsibility

This layer should keep trading calculations isolated from broker implementation and UI behavior.

## Key folders and files

- `Meridian.FSharp.Trading.fsproj` - F# trading project boundary.
- Trading calculation modules and functional trading models.

## Important workflows

Use this module for trading calculations that need deterministic scenario coverage.

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
