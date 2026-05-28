---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-FSHARP-LEDGER
path: src/Meridian.FSharp.Ledger
status: active
owner_lane: Governance and Ledger
last_reviewed: 2026-05-20
---

# src/Meridian.FSharp.Ledger

## Purpose

FSharp Ledger contains deterministic ledger models, accounting calculations, reconciliation matching, and reconciliation-case workflow rules.

## Layer responsibility

This layer should express ledger calculations in functional form while keeping persistence and UI concerns outside the project.

## Key folders and files

- `Meridian.FSharp.Ledger.fsproj` - F# ledger project boundary.
- `ReconciliationCaseWorkflow.fs` - pure transition legality and provider-ledger tolerance checks for reconciliation cases.
- Ledger calculation modules and functional domain types.

## Important workflows

Use this module for ledger calculations and deterministic reconciliation rules that support reconciliation and governed reporting. Keep repositories, hash persistence, provider clients, endpoint handlers, and UI composition in C# layers.

## API contract notes

- `LedgerInterop.ClassifyBreakFacts` trims string fields before mapping DTOs into `RawBreakFacts`, so classification is stable for equivalent break facts that differ only by surrounding whitespace.
- `ReconciliationCaseWorkflowInterop` exposes C#-friendly transition and provider-ledger checks while keeping the lifecycle rule table pure.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-FSHARP-LEDGER -->
| Roadmap item | Title |
| --- | --- |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W4-RPT-001` | Governed report pack readiness |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-FSHARP-LEDGER -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj --logger "console;verbosity=normal"
```

## Change rules

Keep ledger calculations deterministic and evidence-friendly.

## Related docs

- `docs/source/generated/source-roadmap-traceability.md`
- `docs/ai/claude/CLAUDE.fsharp.md`
