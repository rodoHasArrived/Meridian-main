# Carta-Inspired Commitment-to-Cash Delivery Plan

**Status:** active working plan; first slice in progress, later capabilities are candidates
**Owner:** Accounting and Ledger
**Reviewed:** 2026-11-10

## Selected increment

Deliver a durable commitment-to-cash workflow in the existing browser Accounting capital-calls
workspace. Reuse Meridian's commitment roll-forward, capital-call allocation and draft builders,
manual-journal approval/posting, bank-statement evidence, and certified reporting. This is a bounded
increment of [W12-CAPITAL-001](../../roadmap/generated/roadmap-register.md#w12-capital-001---governed-investor-capital-and-allocation-lifecycle), not
acceptance of its full investor-allocation, equalization, series, master-feeder or WPF scope. The
[roadmap registry](../../roadmap/README.md) remains the status authority; the older
[commitment blueprint](../blueprints/accounting/commitment-and-capital-call-engine.md) provides
broader design context, not a claim that every proposed interface is implemented.

The product pattern being adopted is a connected workflow with inspectable evidence: an approved
obligation backs a call, posted issuance raises a receivable, and separately observed cash settles
it. A generated notice is a reporting artifact. It is not delivery confirmation or cash received.

## First-slice contract

- Retain versioned commitment terms and approval evidence under exact tenant, company, entity,
  fund, book, investor/capital-account and currency scope. Amendments require renewed approval;
  client-supplied totals cannot replace the approved server record. The selected book must be owned
  by a `FundStructureNodeKindDto.Entity` with a concrete entity ID.
- Calculate called/uncalled balances from governed posted activity. Bind a call to the approved
  commitment revision, reject stale or foreign-scope inputs, and apply commitment capacity checks
  atomically when postings commit. Concurrent calls cannot exceed the same remaining commitment.
- Create call and funding drafts through the existing journal workbench. Draft, submitted,
  approved, posted, partially funded and funded states remain distinct. Issuance and funding
  preserve balanced journals, period locks, retained evidence and idempotency.
- Bind funding to an identified retained bank-statement line and posted call. A new request key
  cannot reuse the same bank line to create duplicate cash. Each receipt must match the entire
  retained bank transaction's amount, currency, entity and effective date; one bank line cannot be
  split across LPs. Partial funding means a smaller complete receipt against an outstanding call.
- Generate capital-call notices from a certified Reporting dataset only after issuance is posted.
  Retain source call/journal identities and approved reporting scope; block missing or stale
  authority. Reuse the reporting engine rather than adding a separate document renderer.
  Stable notice operation UUIDs recover the original certified package after response loss,
  restart and concurrent retries; deliberate regeneration uses a new UUID. PostgreSQL Reporting
  run claims are required. This bounded notice slice supports one complete posted installment
  per investor per call.
- Expose authority, current balances, blockers, source links and supported actions through shared
  contracts/services. Extend `/accounting/capital-calls` under the existing Accounting navigation;
  use the existing `/api/ledger/...` route family and authenticated server-side actors.

Production persistence uses PostgreSQL. Synthetic fixtures support development; in-memory behavior
alone cannot establish durable acceptance. No Carta connection, live money movement, automatic
notice delivery, legal subscription execution, new root navigation or WPF implementation is part of
this increment.

The shared wire contract uses `PrivateCapitalScopeDto`, versioned `PrivateCapitalCommitmentDto`,
frozen `PrivateCapitalCallDto` plans and `PrivateCapitalPostingReceiptDto` observations.
`IPrivateCapitalStore` owns persistence; the application service resolves approved terms rather than
accepting them from browser input. Commitment edits carry an expected revision, and retaining a
call carries the reviewed plan hash. These additive interfaces preserve the existing journal
workbench as approval/posting authority.

## Operator sequence and acceptance

1. Select the exact fund/book/currency scope. Record commitment terms with their source evidence,
   then approve the current revision through the supported commitment workflow. Existing unmatched
   historical capital activity blocks adoption pending opening reconciliation; this increment does
   not implement automatic opening-balance migration.
2. Preview a call against approved commitments. Review allocation totals, dates, remaining
   capacity and blockers; create the issuance drafts and complete the ordinary journal review,
   approval and posting path. A preview or generated draft does not issue a posted call.
3. Generate the notice through Reporting from the posted issuance. Review its dataset and retained
   journal references. Sending a notice remains a separate action outside this slice.
4. Upload a structured bank observation to Evidence Vault as `BankEvidence` or `BankStatement`,
   complete document review, then use **Import reviewed bank observation** with its vault reference
   and document ID. This imports the observed bank record, not a journal. Review the returned
   source amount/date and match it to the posted installment. Create the funding draft, complete
   journal approval/posting, and reconcile call receivable, cash and remaining commitment. Partial
   funding stays visible; a commitment is not cash and issuance is not settlement. Call and funding
   must use the same open accounting period, with each effective date inside that period. Funding
   across accounting periods is a later extension.
5. On retry or restart, recover the original committed operation/receipt. Posted journals and
   economic receipts remain immutable. Native capital-call reversal/rebooking and legacy opening
   migration are not implemented by this increment; those operations remain blocked rather than
   editing balances or implying an unsupported correction.

Funding requires actual retained banking rows from the same database used by ledger posting,
classified with `TransactionType = "CapitalCallFunding"`. Rows must be non-voided and positive;
uploading an evidence link alone does not create a banking row or establish cash receipt.

The reviewed source is one UTF-8 JSON object, for example:

```json
{
  "bankTransactionId": "66666666-6666-4666-8666-666666666666",
  "entityId": "11111111-1111-4111-8111-111111111111",
  "amount": "25000.00",
  "currency": "USD",
  "effectiveDate": "2026-11-10",
  "transactionDate": "2026-11-10",
  "settlementDate": "2026-11-10",
  "direction": "Credit",
  "statementReference": "synthetic-account/2026-11-10/line-1",
  "observedAtUtc": "2026-11-10T09:00:00Z"
}
```

Use the real owned entity and stable bank-line identity from the reviewed source. Effective and
settlement dates must agree; the observation must already be settled and observed before source
retention. Amounts must fit Banking's exact `NUMERIC(19,4)` storage. Imported observations retain
their original document identity/hash and reviewer; mismatched retries cannot overwrite them.

Before generating notices, configure exactly one `PrivateCapital:NoticeRecipients` entry per scoped
investor. Its `RecipientPrincipalId` explicitly identifies the intended recipient;
`ReviewGroupPrincipalId` is required and names the internal maker-checker review group. An
`InvestorId` is an economic identity, not a login or group principal. For example, use real configured
principal IDs in place of these synthetic placeholders:

```json
{
  "PrivateCapital": {
    "NoticeRecipients": [
      {
        "TenantId": "synthetic-tenant",
        "CompanyId": "synthetic-company",
        "FundProfileId": "synthetic-fund",
        "InvestorId": "synthetic-investor",
        "RecipientPrincipalId": "configured-recipient-principal",
        "ReviewGroupPrincipalId": "configured-internal-review-group"
      }
    ]
  }
}
```

Required verification before claiming this slice verified:

- Independent arithmetic example: a 1,000 commitment, 300 posted call and 100 receipt leaves 700
  uncalled and 200 receivable; funding does not reduce uncalled a second time.
- Reject foreign scope/currency, unapproved or changed commitment revisions, missing evidence,
  overcalls, overfunding, duplicate/split bank-line use, unmatched legacy activity, cross-period
  funding and locked-period postings. Refuse missing, duplicate or incomplete notice mappings.
- PostgreSQL tests cover competing calls/receipts, transaction rollback and reconstructed-service
  retry without duplicate journals or receipt consumption.
- Notice tests refuse unposted issuance and prove the retained certified dataset matches posted
  investor allocations. Browser tests exercise the workflow, blockers and partial funding through
  the shared API; desktop Chromium checks cover keyboard operation and readable dense layouts.

These are acceptance requirements, not a record that the checks have passed. Record actual command
results in the delivery report and link focused tests from the roadmap only when present.

## Later capability candidates

Research supports the following sequence after the first slice. These are proposed follow-ups under
existing roadmap ownership, not new accepted priorities or implementation claims.

| Candidate | Meridian owner / reuse | Boundary |
| --- | --- | --- |
| Portfolio-company financial/KPI requests with submission history and source tracebacks | Existing Data, Documents and valuation services; W10-PROV-001 evidence and applicable W11-PASSPORT-001 subjects | Extracted figures require review; a connector is not an audit of completeness |
| Valuation workpapers, approved posting preview and NAV impact drilldown | Existing valuation, ledger and W12-FINRPT-001 reporting seams | Preserve method/input versions; do not replace immutable books with scenario results |
| Entity/LP/GP relationship and waterfall-impact drilldowns | W12-CAPITAL-001 capital records and existing waterfall calculations | Legal ownership, economic participation and consolidation remain separate |
| Reserve and exit scenarios tied to actuals | Existing forecasting/portfolio services | Versioned hypothetical scenarios remain separate from posted state |
| Evidence-complete audit packages and controlled LP reporting | W12-FINRPT-001, W10-PROV-001 and W11-STAKE-001 | External access requires publication policy and recipient entitlements |
| Carry vesting, private-credit servicing and investor relationship workflows | Applicable W12-CAPITAL-001, W12-CREDIT-001 and existing workflow owners | Require their own bounded scope and numerical/persistence evidence |

Do not expand this increment into employee-equity administration, transfer agency, tax filing,
legal services, lending, payments, a generic CRM replacement or broad Carta API synchronization.
Carta's hosted services are product research, not a dependency on a Carta subscription.

## Research basis

Carta's official [capital-call tour](https://carta.com/explore/fund-erp/capital-calls/) documents
allocation checks and per-investor notice previews. Its
[data-collection tour](https://carta.com/explore/fund-erp/data-collection/) describes recurring
requests and source tracebacks; the
[valuation tour](https://carta.com/explore/fund-erp/portfolio-valuations/) links valuation workpapers
to accounting; the [auditor portal](https://carta.com/explore/fund-erp/auditor-portal/) organizes
retained evidence; and the [LP portal](https://carta.com/explore/fund-erp/lp-portal/) supports
controlled disclosure. These official descriptions were reviewed on 2026-11-10; feature availability
depends on Carta's product and plan. Meridian adopts relevant workflow capabilities through its own
domain and control boundaries.
