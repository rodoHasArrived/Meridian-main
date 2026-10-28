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

`BankStatementClosingBalanceAuthority` projects complete highest-revision batches before selecting
balances or aggregating cash flows. Competing batches at the same account/header-date/revision remain
unavailable, including when their closing balances happen to agree. Currency filtering follows revision
selection so a replacement cannot leak a superseded currency's amount. Source rows remain retained.

Reconciliation readers can load an exact account set through one batched retained-result query,
optionally filtering to active breaks. PostgreSQL checks strict tenant ownership for the whole
requested batch before returning any result. Readiness and workstation queues avoid querying
every historical run individually; immutable run totals and original result outcomes remain history.

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

Sync-history correlations deduplicate atomically within account and capability using a PostgreSQL
unique index/upsert. Concurrent or delayed retries return the retained identity and cannot regress
a successful or completed attempt to a weaker outcome. Existing duplicate rows remain retained with
supersession links and no longer count as current attempts.

Reconciliation results retain their original outcome and durable replacement/resolution links.
Verified reruns supersede only the same account, date, category and stable check identity; missing
evidence never clears a break. Delayed older runs cannot supersede newer observations, and equal-time
authority stays unresolved. Readiness, shared workspace summaries and break queues count the same
active-result state. New migrations backfill unambiguous historical replacements; local snapshots
restore the same lifecycle from retained observations.

Bank closing balances use statement date, explicit revision and source row sequence. Corrected
revisions and original rows remain retained; ingestion time and GUID ordering cannot choose financial
authority. Ambiguous legacy rows remain unverified until an exact reimport supplies their
source sequence, which both stores then retain. Read cancellation is not uniform across all ports.
Both stores refuse a bank statement revision with no lines: authority is selected from retained
lines, so an empty higher revision would otherwise leave the lower revision authoritative.

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
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~PostgresFundAccountStore" /p:EnableWindowsTargeting=true /p:NodeReuse=false
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
