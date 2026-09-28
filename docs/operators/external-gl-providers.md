---
title: External GL import and controlled export review
status: active
owner: core-team
reviewed: 2026-09-28
---

# External GL import and controlled export review

Xero (`xero`) and NetSuite (`netsuite`) provide credentialed, read-only accounting
evidence through the same accounting-system endpoints as QuickBooks Online.
The `xero-fixture` and `netsuite-fixture` providers remain separate demo sources.
All four advertise `SupportsPosting=false`. Certification produces a retained
review artifact; it never sends a journal to an external system.

## Configure and verify

Save credentials through Settings / provider connections. The provider credential
catalog supplies the required fields; the encrypted provider vault owns secrets.
Refresh tokens rotate in that vault before further reads. Do not put secrets in
configuration JSON, evidence links, source control, or support packets.

| Provider | Required fields | Provider prerequisites |
| --- | --- | --- |
| Xero | `ClientId`, `ClientSecret`, `RefreshToken`, `TenantId` | OAuth consent for the selected organisation, `offline_access`, `accounting.settings.read`, `accounting.journals.read`, `accounting.reports.read`; authorising user and app must have Journals/report access. |
| NetSuite | `ClientId`, `ClientSecret`, `RefreshToken`, `AccountId`, `SubsidiaryId`, `AccountingBookId` | OAuth 2.0 REST Web Services access and a role allowed to query the account, subsidiary, currency, primary accounting book, transaction and accounting-line records through SuiteQL. |

`CompanyName` is optional. NetSuite account IDs such as `123_SB1` resolve only to
their account-specific `suitetalk.api.netsuite.com` host; arbitrary URLs are not
accepted. Subsidiary/book identifiers must be positive integers. Configure one
credential record per provider deployment. Multiple simultaneous external
connections for a single provider are outside this adapter's current scope.

Connection verification exchanges the token and reads the selected organisation
or subsidiary/book. It does not certify all import permissions. Run a complete
import for the intended period to verify journal/report access.

## Import semantics and boundaries

Use `POST /api/accounting-system/import/preview` with an explicit provider, ledger book,
accounting/access scope, and inclusive `PeriodStart` / `PeriodEnd`. Retained imports
must use `PersistPreview=true` before export review. Preview-only results do not
replace retained evidence. The integration service supplies scoped content hashes.

- Xero reads the chart, accrual Journals and TrialBalance report. Journals are
  scanned by journal-number offset, including backdated entries, then filtered to
  the requested accounting dates. Report account attributes identify accounts;
  the YTD debit/credit columns provide balances in organisation base currency.
  The import does not substitute manual journals for full GL journal access.
- NetSuite reads posted accounting lines for one subsidiary and its primary
  accounting book, in subsidiary base currency. Its standard date-based accrual
  Trial Balance uses calendar-year-to-date balances for Income, Expense, Other
  Income, Other Expense and Cost of Goods Sold. Prior-calendar-year net income or
  loss is added to cumulative direct postings in the system retained-earnings
  account. Other balance-sheet accounts remain cumulative through the end date.
  The adapter reads cumulative and prior-year amounts together under the same
  subsidiary, book and end-date filters. The requested journal start does not
  change the trial-balance year boundary, including when a request spans years.
  Period-end journals (`PEJrnl`) are excluded from both populations because they
  belong to the separate post-closing report.
- NetSuite identifies system retained earnings from Account `sspecacct`
  (`RetEarnings`), not an account name, account number or a residual used to force
  balance. Missing, ambiguous, inactive or non-equity system identity fails the
  import. The role must expose that metadata and all accounting rows in the
  selected scope; use an unrestricted accounting role for report comparison.
  Report basis and retained-earnings identity appear in trial-balance evidence.
  Report customisations, consolidated eliminations, secondary books, cash-basis,
  period-based and post-closing reporting remain unsupported. Reconcile to the
  provider's standard Trial Balance for the same subsidiary, primary book and
  As of date, with Report by Period disabled, before certifying a review package.
- NetSuite SuiteQL uses POST only for read queries; no record-write URL exists in
  this adapter. Pages use a fixed 1,000-row limit and locally computed offsets.
- Rate limits, expired consent, permission errors, malformed amounts, duplicate
  identities, incomplete pagination, unbalanced journals or trial balances, and
  negative or two-sided trial-balance amounts fail the entire
  import. No partial result replaces the last retained import. Requests are
  cancellable. A failed or cancelled read may already have rotated a refresh token.
- Xero imports stop with an error after 1,000 nonempty journal pages; NetSuite
  stops with an error if additional rows remain beyond the REST SuiteQL
  100,000-row ceiling. Narrow or repair the provider source before retrying;
  these limits never return a silently truncated success.

## Provider-owned export checks

Normal mapping certification, human-origin checks, exact package/period/book
approval evidence, generated-line provenance, and reconciliation safeguards still
apply. The provider additionally requires a retained live import for the current
external connection, same ledger book and exact period, and balanced export lines
targeting active imported accounts in the imported currency. Export review lines
retain gross account activity, so a line can contain both debit and credit totals;
this does not make it an external posting instruction.

Each required control must have its own exact retained approval reference:

```text
approval:external-gl-provider:{provider}:{control}:import:{importId}:ledger-book:{ledgerBookId}:{yyyy-MM-dd-start}:{yyyy-MM-dd-end}
```

| Provider | Required control identifiers |
| --- | --- |
| Xero | `tracking-category-options`, `contact-mapping`, `tax-rate-mapping`, `bank-account-scope` |
| NetSuite | `subsidiary-scope`, `classification-segments`, `entity-mapping`, `intercompany-controls` |

These references represent retained human review evidence, including a documented
not-applicable conclusion where appropriate. Their presence validates the review
contract; it is not a claim that Xero or Oracle has approved the integration.
Supply them when creating the review package. The existing certification action
still requires the human approver's package-specific certification evidence.
Do not invent approval references to make a blocked package appear ready.

The provider checks run at package creation, certification, and manifest read.
Changing the external connection or retaining a new import invalidates old
provider-control references; create a new review package with fresh evidence.
Live posting stays disabled even after successful certification.

## Validation evidence

`ExternalGlLiveProviderTests`, `ExternalGlFailureBoundaryTests`, `NetSuiteTrialBalanceTests`, and
`AccountingSystemIntegrationServiceTests.LiveProviders` exercise credentialed HTTP
request/response contracts, pagination, safe failure, scoped evidence, and the
controlled export path without tenant secrets. They are transport contract tests,
not a live customer-tenant attestation. Deployment owners must retain the actual
credentialed import and provider-report reconciliation evidence before approving
their own export package. Multi-year regressions cover all five income-statement
types, calendar-year boundaries, profit and loss carry-forward, direct retained
postings, offsetting prior-year accounts, account renaming, scope filters,
period-end journal exclusion, invalid identity and malformed aggregates. The
normalization creates report evidence only; no synthetic closing journal is posted.

Issue #2752 originally referred to `docs/status/accounting-productization-checklist.md`.
That historical snapshot is retained in the [archived checklist](../../archive/docs/summaries/accounting-productization-checklist.md).
Current acceptance is tracked in the [active implementation list](../product/implementation-todo-list.md#acct-checklist-06-external-gl-provider-depth).
Implementation acceptance requires passing validation on the current PR head;
contract tests do not constitute customer-tenant smoke tests or vendor approval.

Protocol references: [Xero Accounting OpenAPI](https://github.com/XeroAPI/Xero-OpenAPI/blob/master/xero_accounting.yaml),
[Xero journal pagination](https://developer.xero.com/documentation/best-practices/api-call-efficiencies/rate-limits),
[NetSuite SuiteQL REST](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_157909186990.html),
[NetSuite Trial Balance semantics](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N1520986.html),
[NetSuite system retained earnings](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N1457773.html),
[NetSuite Account analytics metadata](https://www.netsuite.com/help/helpcenter/en_US/srbrowser/Browser2020_2/analytics/record/account.html),
[NetSuite reports and period-end journals](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_1513210940.html),
and [NetSuite OAuth](https://blogs.oracle.com/developers/netsuite-as-oidc-provider).
