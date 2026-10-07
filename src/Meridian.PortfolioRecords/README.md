---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-DESIGN-PORTFOLIO-RECORDS
path: src/Meridian.PortfolioRecords
status: active
owner_lane: Accounting and Ledger
last_reviewed: 2026-10-07
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

Runtime hosts and one-shot commands select the same account authority through Application's
`PortfolioRecordsServiceRegistration`: PostgreSQL when fund-account persistence is configured,
otherwise the tenant-guarded local snapshot service. `IFundAccountService`,
`IAccountManagementService`, and `IAccountQueryService` share one service instance. Strict tenant
enforcement refuses unpartitioned local snapshots; explicit deployment-boundary compatibility is
required for reviewed local migration access.

## Important workflows

Use this README to understand the module before editing source files. Update the registry when validation, roadmap links, diagrams, or ownership changes.

Statement ingestion validates every line's account and batch identity before writing; custodian
lines must also match the batch's as-of date. Invalid line collections cannot partially persist a
batch. PostgreSQL account creation is insert-only so concurrent requests cannot overwrite an
existing account, and account mutations require an existing account. Balance snapshots retain
the owning account's fund identity.

Cash continuity compares independent run and brokerage snapshots in the same currency. If only
foreign-currency sync evidence exists, the check remains `Unverified`. Readiness includes
inactive or unavailable account status, incomplete sync status, and degraded provider-link state
so these records cannot appear ready solely because a balance exists.

Correlated margin corrections retain the existing snapshot ID when effective time changes.
Corrections onto another snapshot's effective time are rejected without replacing either record.

Sync-history correlation deduplication still uses a service-level lookup before insertion and is
not atomic across concurrent PostgreSQL writers. Read cancellation is not uniform across all
ports; same-day bank closing-balance ordering and historical reconciliation-break resolution
remain follow-up work under the existing contracts.

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
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~PortfolioRecords|FullyQualifiedName~FundAccountEndpointAuthorizationTests" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~PostgresFundAccountStoreTests" /p:EnableWindowsTargeting=true /p:NodeReuse=false
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
