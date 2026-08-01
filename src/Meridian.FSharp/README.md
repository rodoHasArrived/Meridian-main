---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-FSHARP
path: src/Meridian.FSharp
status: active
owner_lane: Strategy Analytics
last_reviewed: 2026-05-20
---

# src/Meridian.FSharp

## Purpose

FSharp contains deterministic functional models, calculations, and workflow support used by research, operations, governance, and domain logic.

## Layer responsibility

This layer should keep functional domain calculations reusable and testable from C# application and workflow code.

## Key folders and files

- `Meridian.FSharp.fsproj` - main F# project boundary.
- `Operations/OperationsContinuityRules.fs` - pure status precedence rules for Operations Continuity.
- `Operations/ReportPackValidationRules.fs` - data-driven report-pack validation rules.
- `Operations/SensitiveActionPolicy.fs` - pure sensitive-action approval and segregation-of-duties policy.
- `Operations/TradingReadinessRules.fs` - trading readiness and evidence-completeness scoring.
- Functional models and calculation modules.

## Important workflows

Use this module for deterministic business-rule kernels shared by strategy, trading, accounting, operations, and governed reporting workflows. Keep C# services responsible for orchestration, DI, storage, logging, endpoint composition, and UI-facing DTO assembly.

## API contract notes

- C# callers should enter through C#-friendly interop wrappers and should not depend on internal F# domain types.
- Operations kernels should remain pure and deterministic: inputs in, decisions/issues/statuses out.
- Stream-oriented helpers should receive ordered, bounded inputs. Page large storage/backfill batches before crossing into F#, and keep `mergeStreams` / `bufferByTime` on timestamp-ordered streams so they can avoid whole-batch sorting and grouping.
- Sensitive-action policy evaluates explicit guardrails such as MFA, dual approval, privileged roles, and segregation-of-duties before C# services write audit or workflow state.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-FSHARP -->
| Roadmap item | Title |
| --- | --- |
| `W3-CONT-001` | Research to paper continuity |
| `W6-BTSTUDIO-001` | Backtesting studio evidence loop |
| `W10-PERF-001` | Portfolio and investor return measurement |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-FSHARP -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj /p:FSharpTestSlice=MarketData --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj /p:FSharpTestSlice=Domain --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj /p:FSharpTestSlice=Operations --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
```

Use `/p:FSharpTestSlice=All` only when validating cross-slice F# changes.

## Change rules

Keep F# interop contracts explicit and covered by tests before changing C# consumers.

## Related docs

- `docs/ai/claude/CLAUDE.fsharp.md`
- `docs/source/generated/source-module-index.md`
