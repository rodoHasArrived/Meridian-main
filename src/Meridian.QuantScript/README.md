---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-QUANTSCRIPT
path: src/Meridian.QuantScript
status: active
owner_lane: Strategy and Research
last_reviewed: 2026-05-20
---

# src/Meridian.QuantScript

## Purpose

QuantScript provides scripting and research tooling for strategy development, analysis, and operator-facing strategy workflows.

## Layer responsibility

This layer should support research workflows without bypassing strategy lineage, validation, or promotion evidence.

## Key folders and files

- `Meridian.QuantScript.csproj` - QuantScript project boundary.
- Script runtime, command, and research support files.

## Important workflows

Use this module for QuantScript execution, research scripting, and strategy analysis support.

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

Keep script execution evidence-linked and avoid unvalidated promotion from research output to paper or live readiness.

## Related docs

- `docs/source/generated/source-roadmap-traceability.md`
- `docs/plans/waves-2-4-operator-readiness-addendum.md`
