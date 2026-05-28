---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-LEDGER
path: src/Meridian.Ledger
status: active
owner_lane: Governance and Ledger
last_reviewed: 2026-05-20
---

# src/Meridian.Ledger

## Purpose

Ledger provides accounting and books support for reconciliation, governed reporting, and fund operations.

## Layer responsibility

This layer should model ledger behavior and accounting evidence without owning UI presentation or storage engine mechanics.

## Key folders and files

- `Meridian.Ledger.csproj` - ledger project boundary.
- Ledger models, accounting services, and reconciliation support files.

## Important workflows

Use this module for books, ledger behavior, reconciliation evidence, and accounting workflow support.
`ChartOfAccounts` supports customizable colon-delimited account hierarchies such as
`Assets:Cash:Brokerage` and can roll flat trial-balance output up to parent accounts for
fund/accounting reports.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-LEDGER -->
| Roadmap item | Title |
| --- | --- |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W4-RPT-001` | Governed report pack readiness |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-LEDGER -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
```

## Change rules

Preserve auditability and evidence links when changing ledger or reconciliation behavior.

## Related docs

- `docs/status/contract-compatibility-matrix.md`
- `docs/source/generated/source-roadmap-traceability.md`
