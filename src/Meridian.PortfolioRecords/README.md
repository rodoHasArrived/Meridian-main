---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DESIGN-PORTFOLIO-RECORDS
path: src/Meridian.PortfolioRecords
status: active
owner_lane: Accounting and Ledger
last_reviewed: 2026-06-05
---

# src/Meridian.PortfolioRecords

## Purpose

Physical bounded-context module project for portfolio records, positions, activity, holdings,
account records, fund-account balances, account readiness, statement intake, reconciliation runs,
sync history, margin snapshots, and fund-operation ownership conformance.

## Layer responsibility

This module belongs to the Design Module layer. Keep changes within that ownership boundary and update the registry if the boundary changes.

## Key folders and files

- `Accounts/` - account query and account management service ports consumed by fund-structure,
  shared UI, and desktop/browser account-record workflows.
- `FundAccounts/` - in-memory and PostgreSQL-backed fund-account services for account snapshots,
  statement intake, account readiness, provider-link sync history, reconciliation runs, and margin
  snapshots.

Bank-statement file imports should enter through `IFundAccountService.IngestBankStatementAsync`
after transport and CSV mapping validation; scheduled SFTP or API feed adapters should reuse that
same service boundary rather than posting ledger entries directly.

## Important workflows

Use this README to understand the module before editing source files. Update the registry when validation, roadmap links, diagrams, or ownership changes.

## Diagrams

`DIA-ASSURANCE-LOOP`

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-DESIGN-PORTFOLIO-RECORDS -->
| Roadmap item | Title |
| --- | --- |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W5-ACCT-001` | Accounting records and operational evidence |
| `W5-MASSET-001` | Multi-asset operational coverage proof lane |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-DESIGN-PORTFOLIO-RECORDS -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.PortfolioRecords/Meridian.PortfolioRecords.csproj /p:EnableWindowsTargeting=true
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~FundAccountServiceTests" /p:EnableWindowsTargeting=true /p:NodeReuse=false
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

Preserve the module boundary declared in `docs/source/data/source-modules.yml` and update the nearest docs when behavior or workflow semantics change.

## Related docs

- `docs/source/README.md`
- `docs/source/generated/source-module-index.md`
- `docs/architecture/module-map.md`
