---
title: External GL import and controlled export review
status: active
owner: core-team
reviewed: 2026-09-25
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
  accounting book, in subsidiary base currency. Period journals include all
  posting transaction types. Trial balance aggregates accounting-line net amounts
  through period end. It is an unconsolidated accounting-line balance view; it
  does not reproduce report customisations, consolidated eliminations, or a
  secondary book's currency/reporting rules. Reconcile the extracted balances to
  the provider-owned report before certifying any review package.
- NetSuite SuiteQL uses POST only for read queries; no record-write URL exists in
  this adapter. Pages use a fixed 1,000-row limit and locally computed offsets.
- Rate limits, expired consent, permission errors, malformed amounts, duplicate
  identities, incomplete pagination and unbalanced journals fail the entire
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
targeting active imported accounts in the imported currency.

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

`ExternalGlLiveProviderTests`, `ExternalGlFailureBoundaryTests`, and
`AccountingSystemIntegrationServiceTests.LiveProviders` exercise credentialed HTTP
request/response contracts, pagination, safe failure, scoped evidence, and the
controlled export path without tenant secrets. They are transport contract tests,
not a live customer-tenant attestation. Deployment owners must retain the actual
credentialed import and provider-report reconciliation evidence before approving
their own export package.

Issue #2752 referred to `docs/status/accounting-productization-checklist.md`, which
is absent from the current source tree. This procedure and the source/test changes
record the implemented scope without recreating a competing status checklist.

Protocol references: [Xero Accounting OpenAPI](https://github.com/XeroAPI/Xero-OpenAPI/blob/master/xero_accounting.yaml),
[Xero journal pagination](https://developer.xero.com/documentation/best-practices/api-call-efficiencies/rate-limits),
[NetSuite SuiteQL REST](https://docs.oracle.com/en/cloud/saas/netsuite/ns-online-help/section_157909186990.html),
and [NetSuite OAuth](https://blogs.oracle.com/developers/netsuite-as-oidc-provider).
