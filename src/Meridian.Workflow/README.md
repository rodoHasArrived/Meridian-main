---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DESIGN-WORKFLOW
path: src/Meridian.Workflow
status: active
owner_lane: Workstation Shell and UX
last_reviewed: 2026-06-06
---

# src/Meridian.Workflow

## Purpose

Physical bounded-context module project for workflow definitions, operator actions, presets,
runbook execution, environment design, continuity paths, and process ownership conformance.

## Layer responsibility

This module belongs to the Design Module layer. Keep changes within that ownership boundary and update the registry if the boundary changes.

## Key folders and files

- `Runbooks/` - persisted runbook definitions, JSON-backed runbook store, and deterministic
  runbook executor used by Application CLI adapters.
- `Workflows/` - shared fund workflow command-state handler for broker ingest, Security Master,
  ledger, reconciliation, approval, rejection, close, and governed reopen transitions.
- `EnvironmentDesign/` - local-first environment draft, validation, publish, rollback, and runtime
  projection implementation consumed through Contracts-owned service interfaces.

## Important workflows

Use this module when changing shared workflow definitions, operator actions, presets, or runbook
execution semantics. Application commands may adapt these services to CLI flags, but Workflow owns
the runbook models, persistence contract, executor behavior, fund workflow state transitions, and
Environment Designer runtime projection implementation.

## Diagrams

`DIA-ASSURANCE-LOOP`

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-DESIGN-WORKFLOW -->
| Roadmap item | Title |
| --- | --- |
| `W3-CONT-001` | Research to paper continuity |
| `W5-ACCT-001` | Accounting records and operational evidence |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-DESIGN-WORKFLOW -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.Workflow/Meridian.Workflow.csproj /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~FundWorkflowCommandHandlerTests|FullyQualifiedName~RunbookServicesTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.FundStructure.Tests/Meridian.FundStructure.Tests.csproj --filter "FullyQualifiedName~EnvironmentDesignerServiceTests" --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
```

## Optional conditional sections

Add only the sections that apply to this module:

- `### Plans and roadmap`
- `### End-user value`
- `### Benchmarks and performance`
- `### Operational evidence`
- `### Security and credentials`
- `### API and contract notes`
- `### Migration and archive notes`

## Change rules

Preserve the module boundary declared in `docs/source/data/source-modules.yml` and update the
nearest docs when behavior, runbook persistence, command adaptation, workflow state transitions,
Environment Design projection, or workflow semantics change.

## Related docs

- `docs/source/README.md`
- `docs/source/generated/source-module-index.md`
- `docs/architecture/module-map.md`
