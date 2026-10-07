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

## Prerequisites and execution context

Complete [preflight](preflight-checklist.md) and use a signed-in operator in the intended company,
fund/account, ledger book, and accounting period. Provider connection changes require
`ManageCredentials`. Import preview/reads require `AdminMaintenance` or `ManageFundStructure`;
creating and certifying export packages require `AdminMaintenance` plus the scoped workflow
evidence described below. Possessing a provider credential does not grant Meridian workflow access.

The procedure uses the Windows browser/WPF workstation and its current loopback host. For API
inspection, use PowerShell 7 and the [preflight operator session](preflight-checklist.md#authenticated-evidence-collection)
in a separate terminal. Perform mutations through the governed UI or a client that supplies the
session's CSRF protection and the exact request scope; a bare endpoint path is not a complete request.

## Configure and verify

Save credentials through Settings / provider connections. The provider credential
catalog supplies the required fields; the encrypted provider vault owns secrets.
Refresh tokens rotate in that vault before further reads. Do not put secrets in
configuration JSON, evidence links, source control, or support packets.

Rotation retains a complete credential snapshot, including credentials initially
read from environment variables, in both the encrypted primary and recovery vault.
Once a replacement token arrives, caller cancellation does not cancel its save.
The vault must be writable; a failed rotation save stops the import before further
provider reads. Repair storage access and verify the retained credentials before
retrying; obtain fresh consent if the provider has invalidated an unretained token.

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
The connection lifecycle records one verification result with the requesting
operator's identity. Imports record their own verification result. Replacing any
credentials clears verification, including replacements that keep the same external
company. Successful verification or a complete successful import is required before
export review can resume.
Token saves compare the expected connection atomically under the vault writer lock.
Every credential save changes an opaque generation identifier; import and verification
results can only update that same generation. Replacing credentials during an in-flight
request therefore preserves the operator's replacement and blocks the stale operation.
Retry the import or verification against the current connection after replacement.

## Import semantics and boundaries

Use `POST /api/accounting-system/import/preview` with an explicit provider, ledger book,
accounting/access scope, and inclusive `PeriodStart` / `PeriodEnd`. Retained imports
must use `PersistPreview=true` before export review. Preview-only results do not
replace retained evidence. The integration service supplies scoped content hashes.

- Xero reads the chart, accrual Journals and TrialBalance report. Journals are
  scanned by journal-number offset, including backdated entries, then filtered to
  the requested accounting dates. Report account attributes identify accounts;
  the YTD debit/credit columns provide balances in organisation base currency.
  The organisation's financial year-end determines the income-statement start.
  Account `SystemAccount=RETAINEDEARNINGS` identifies prior-year carry-forward;
  income-statement imports require that identity to be unambiguous.
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
  The chart includes only accounts assigned to the selected subsidiary, or to an
  ancestor with Include Children enabled. The role must expose the ancestor chain;
  unavailable or cyclic hierarchy evidence fails the import. Accounts without a
  number use the stable `netsuite-account:{internalId}` code. Xero accounts without
  a code similarly use `xero-account:{accountId}`. Map these codes explicitly in
  Meridian; ambiguous duplicate codes are rejected.
- Xero GET reads retry HTTP 429 responses at the same cursor when `Retry-After`
  supplies a valid delay or HTTP date. Each read allows up to three retries and a
  maximum two-minute delay per retry. Waiting is cancellable. Missing or invalid
  hints, longer daily-limit delays and exhausted retries fail the import. Token
  exchanges and NetSuite requests are not automatically retried.
- Unrecovered rate limits, expired consent, permission errors, malformed amounts, duplicate
  identities, incomplete pagination, unbalanced journals or trial balances, and
  negative or two-sided trial-balance amounts fail the entire
  import. No partial result replaces the last retained import. Requests are
  cancellable. HTTP timeouts record a sanitized provider failure; cancellation
  requested by the caller remains cancellation. A failed or cancelled read may
  already have rotated a refresh token.
- Xero imports stop with an error after 1,000 nonempty journal pages; NetSuite
  stops with an error if additional rows remain beyond the REST SuiteQL
  100,000-row ceiling. Narrow or repair the provider source before retrying;
  these limits never return a silently truncated success.

Retained imports carry a typed trial-balance basis separately from the requested
journal/export dates. Reconciliation reads Meridian history through the as-of
date, keeps cumulative balance-sheet activity, and moves income/expense activity
before the provider's report year into retained earnings for comparison. This is
a report projection, not a ledger mutation. Date filtering applies to each journal,
using its accounting effective date, with UTC event dates only for legacy entries.
This includes late-posted journals and periods that cut through a month. Export review uses only
gross activity within the requested inclusive dates; opening balances and report
carry-forward amounts never become generated export activity. The import and
reconciliation hashes retain this distinction and invalidate stale certifications.
Provider and Meridian ledger-book currencies must match, including for zero balances
omitted from a provider report. Equal numeric amounts in different currencies remain
reconciliation breaks. Export review retains Meridian's currency and provider
certification blocks mismatches even when balanced reconciliation is not requested;
this adapter does not perform currency conversion.
Both provider reports use accrual accounting. Reconciliation and each export boundary
require a resolved Meridian book with Primary or Gaap accounting basis. Cash, Tax,
Statutory and unresolved book bases are unsupported and are rejected even when
numeric balances match or balanced reconciliation is not requested. Xero wage and
superannuation expense account types participate in the income-year roll-forward.
Meridian's period-close closing journals and their reversals are excluded from both
provider-basis totals and export activity. The report projection derives retained
earnings from underlying income activity, so a completed monthly close cannot erase
current-year income or double-count earlier carry-forward.

Certified account mappings resolve imported account identities to Meridian account
names before comparison, including income and retained-earnings classification.
Thus `Assets:Cash` can reconcile with a differently numbered Xero/NetSuite account.
Mappings must belong to the same provider, fund, ledger book and access scope.
Current support requires unambiguous one-to-one mappings; many-to-one allocations,
missing external identities and collisions with other account names are rejected.
Export creation, certification and manifest reads use the package's selected mapping
profile even when another certified profile is newer.

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
All three boundaries require the current credential record to be verified; replacing
the client ID, client secret or refresh token blocks even a previously certified
package until verification succeeds. A failed verification also blocks review.
Changing the external connection or retaining a new import invalidates old
provider-control references; create a new review package with fresh evidence.
Live posting stays disabled even after successful certification.

## Expected result and failure recovery

A successful retained import identifies the provider connection generation, exact scope/period,
and content hashes. Reconciliation matches the provider's stated trial-balance basis; an export
review package retains its own mapping and approval references. Certification still leaves
`SupportsPosting=false`. Retain the import/reconciliation/package identifiers and evidence before
replacing credentials or importing a new period.

| Failure | Next action |
| --- | --- |
| `401/403` or missing accounting scope | Correct the operator session, permissions, company/fund/book assignment, and exact period. |
| Token rotation cannot be retained | Repair primary/recovery vault storage; verify the retained credential generation before another provider read. |
| Expired consent or provider permission/entitlement error | Restore the selected organisation/subsidiary/book access, verify the connection, then rerun the full period import. |
| Incomplete pages, unsupported report basis, unbalanced totals, or currency mismatch | Preserve the failed evidence and repair/narrow the source or supported scope. No partial import can replace the last accepted one. |
| Review invalidated after credential/import replacement | Verify the current generation and create fresh package-specific review evidence; stale approvals cannot certify the replacement. |

## Validation evidence

`ExternalGlLiveProviderTests`, `ExternalGlFailureBoundaryTests`, `NetSuiteTrialBalanceTests`,
`ExternalGlCredentialRecoveryTests`, `ExternalGlConnectionLifecycleTests`,
`ExternalGlRateLimitTests`, `ExternalGlScopeTests`, and the shared
`AccountingSystemIntegrationServiceTests` live-provider/period cases exercise credentialed HTTP
request/response contracts, pagination, safe failure, scoped evidence, and the
controlled export path without tenant secrets. They are transport contract tests,
not a live customer-tenant attestation. Deployment owners must retain the actual
credentialed import and provider-report reconciliation evidence before approving
their own export package. Multi-year regressions cover all five income-statement
types, calendar-year boundaries, profit and loss carry-forward, direct retained
postings, offsetting prior-year accounts, account renaming, scope filters,
period-end journal exclusion, invalid identity and malformed aggregates. The
normalization creates report evidence only; no synthetic closing journal is posted.
Additional regressions cover cancellation immediately after token rotation followed
by primary-vault corruption, environment credential migration, unnumbered charts,
inherited subsidiary accounts, multi-period reconciliation, Xero fiscal boundaries,
partial-month exports, and certification invalidation after gross activity changes.
Credential replacement regressions cover creation, certification and manifest reads;
real-vault tests require one verification audit event with the requesting actor.
Rate-limit regressions cover delta and HTTP-date delays, unchanged pagination cursors,
retry exhaustion, daily limits, malformed hints and cancellation during the wait.
`ExternalGlCredentialConcurrencyTests` pause token exchanges and provider reads while
another vault instance replaces credentials, proving that stale rotations and
verification results cannot overwrite or verify that replacement. Reconciliation
tests use real period-close projections and reversals, different internal/external
account names, and a competing certified mapping profile.

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
[NetSuite subsidiary account assignments](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_N1440518.html),
[NetSuite multiselect filtering](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/article_1029114633.html),
[NetSuite reports and period-end journals](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_1513210940.html),
and [NetSuite OAuth](https://blogs.oracle.com/developers/netsuite-as-oidc-provider).
