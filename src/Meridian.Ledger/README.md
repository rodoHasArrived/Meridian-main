---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-LEDGER
path: src/Meridian.Ledger
status: active
owner_lane: Accounting and Ledger
last_reviewed: 2026-11-13
---

# src/Meridian.Ledger

## Purpose

Provide shared accounting domain models and deterministic calculations for books, journals,
reconciliation, financial statements, lot economics and governed reporting evidence.

## Layer responsibility

Ledger owns accounting behavior and financial invariants. PostgreSQL transactions and persistence
belong to `Meridian.Storage.Accounting`; application services and shared workstation APIs own
orchestration and access enforcement. Keep rendering and inventory-role classification separate
from authority to approve or post journals.

## Key folders and files

- `Ledger.cs`, `JournalEntry.cs` and `LedgerEntry.cs` define balanced journal and balance behavior.
- `LedgerAccounts.cs` and `ChartOfAccounts.cs` define shared posting accounts and report hierarchies.
- `LedgerFinancialStatementBuilder.cs` and `LedgerReportPackBuilder.cs` derive accounting reports and retained evidence.
- `LedgerInventoryAccountRoleClassifier.cs` distinguishes source-backed non-lot assets from instrument inventory.

## Important workflows

`LedgerJournalReversal` preserves explicit transaction/functional currency and acquisition FX
while swapping both functional and transaction debit/credit amounts. A correction never drops the
currency evidence from its immutable source.

Explicitly classified investing activities recognize either an asset carrying leg or a
liability carrying leg (short securities payable). A short cover with retained investing
classification reports its actual cash cost while preserving the booked realized result.

Optional intercompany agreement/revision, chart-node/binding and transaction identities retain
source provenance on immutable journal legs. Null fields are omitted to preserve historic evidence
serialization. Reviewed legacy-attribution sidecars add missing historical identity without editing
posted legs; consolidation output remains a separate reviewed overlay. See
[the domain dictionary](../../docs/domain/intercompany-agreements-and-entity-lifecycle.md).
Cost-basis relief uses monetary cost per recorded quantity. Face-value amortization targets the
economic face represented by that quantity and retains decimal unit-cost precision; share rounding
is unchanged. Unaffected lots retain their exact basis. Known exact whole-lot basis follows each
split, factor, amortization, capital-return and wash-sale monetary transform.

`JournalEntryMetadata.PreparerAttestation` retains the exact native cash-dividend preparer receipt,
owner scope and posting fingerprints on the immutable journal for current and frozen report proof.
The absent property is omitted from JSON so historical journal hashes remain unchanged. This
classification establishes preparer attestation only; source verification, independent approval
and financial coverage remain separate.

`LedgerLineDimensionSet` retains optional `IntercompanyAgreementId` and approved version as a pair.
Canonicalization, grouping and replay preserve the distinction between agreement revisions without
changing legacy signatures when references are absent. Agreement provenance explains a line; it
does not create paired postings, settlement or agreement-level consolidation. The durable storage
boundary validates supplied references and historical reversals. See
[intercompany agreements](../../docs/domain/intercompany-agreements.md).

`StructuredNoteAccountingTemplate` recognizes dated fair-value gains and losses in profit or loss
through the existing governed daily-valuation drafts. It converts face quantities and raw percentage
quotes through the shared lot model, applies currency minor units, and retains template/convention
evidence with its retry identity. Structured notes do not enter the bond coupon-accrual route.

`LedgerAccounts.ShortSaleCollateralReceivable` is the asset account for collateral the broker holds against open Reg T short sales.

Share-class redemptions round cash to the class currency's minor units (`NavPerUnitCalculator.CashForRedemption(units, nav, currency)`).

`PrivateCapitalPostingAuthority` binds frozen call plans and approved commitment revisions to
governed issuance/funding drafts. Native funding has a distinct stable receipt identity for each
partial receipt while retaining the originating installment and fund-event identity. Historical
factory calls without a receipt identity retain their previous idempotency keys. Bank evidence
must identify a positive, unvoided `CapitalCallFunding` transaction matching entity, currency,
amount and effective date; source snapshots are hashed and rechecked before posting.

`IPrivateCapitalStore` exposes durable commitment revisions, frozen call plans, and immutable posting
receipts without owning a storage engine. `PrivateCapitalAuthorityRules` validates explicit scope
and source terms; `PrivateCapitalCallPlanHash` binds the reviewed allocation while excluding later
posting observations. The commitment-to-cash workflow reuses the existing capital-call kernel and
requires source-backed opening authority instead of inferring commitments from assignments.
Call allocation uses called capital as of its notice date. The shared authority rule also walks
all posted issuance dates and approved effective-date checkpoints, so backdated calls cannot break
a future reduction and later increases cannot authorize an earlier overcall. At one effective date,
the highest approved revision owns the cap; superseded and draft terms do not constrain the timeline.

`BuiltInLedgerReportBinaryRenderer.RenderWorkbook(IReadOnlyList<LedgerReportTable>)` is the shared
dependency-free XLSX path for frozen accounting datasets. It retains all cells as text, preserving
exact decimal digits and preventing source descriptions from becoming spreadsheet formulas.

## Diagrams

See `DIA-ASSURANCE-LOOP` in the [diagram index](../../docs/source/data/diagram-index.yml).

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-LEDGER -->
| Roadmap item | Title |
| --- | --- |
| `W14-ATTEST-001` | Single-principal attestation for governed approvals in the single-operator envelope |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W4-RPT-001` | Governed report pack readiness |
| `W5-ACCT-001` | Accounting records and operational evidence |
| `W9-ASSET-010` | Asset Accounting Event Spine and atomic lot posting |
| `W10-MARK-001` | Fail-closed stale-mark policy and mark-age surfacing |
| `W10-JRNL-001` | Durable recurring journal schedules and draft runner |
| `W10-TAX-001` | Tax character, wash-sale, and lot-relief operator surface |
| `W10-PERF-001` | Portfolio and investor return measurement |
| `W10-CONSOL-001` | Intercompany elimination on consolidated ledger views |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-LEDGER -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.Ledger/Meridian.Ledger.csproj
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~LedgerFinancialStatement|FullyQualifiedName~LedgerInventoryAccountRoleClassifier|FullyQualifiedName~StandardChartOfAccounts"
```

## Change rules

Preserve balanced journals, immutable evidence, exact decimal economics and existing serialized
contracts. Add focused financial-invariant coverage for behavior changes; do not turn source-role
classification or preparer attestation into approval or posting authority.

## Related docs

- [Accounting adapter source guide](../Meridian.Storage.Accounting/README.md)
- [Account and book scopes](../../docs/domain/account-and-book-scopes.md)
- [Governed reporting operations](../../docs/operators/governed-reporting-operations.md)
- [Module map](../../docs/architecture/module-map.md)
