---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-FSHARP
path: src/Meridian.FSharp
status: active
owner_lane: Strategy and Research
last_reviewed: 2026-05-20
---

# src/Meridian.FSharp

## Purpose

FSharp contains functional models, calculations, and workflow support used by research and domain logic.

## Layer responsibility

This layer should keep functional domain calculations reusable and testable from C# application and workflow code.

## Key folders and files

- `Meridian.FSharp.fsproj` - main F# project boundary.
- Functional models and calculation modules.

## Important workflows

Use this module for functional calculation changes shared by strategy, trading, or accounting workflows.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-FSHARP -->
| Roadmap item | Title |
| --- | --- |
| `W3-CONT-001` | Research to paper continuity |
| `W5-BTSTUDIO-001` | Backtesting studio evidence loop |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-FSHARP -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj --logger "console;verbosity=normal"
```

## Change rules

Keep F# interop contracts explicit and covered by tests before changing C# consumers.

## Related docs

- `docs/ai/claude/CLAUDE.fsharp.md`
- `docs/source/generated/source-module-index.md`
