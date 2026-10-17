---
title: Bounded account oversight
status: active
owner: core-team
reviewed: 2026-10-14
audience: operators
---

# Bounded account oversight

Use the shared ledger API for the first account cash-balance continuity, reviewed close-price
set and cash-forecast workflows. These are bounded evidence workflows; they do not post journals,
approve period close or execute payments. Live operator acceptance remains separate from tests.

## Prerequisites and provisioning

Run the existing Meridian host against its durable data root. Authenticate with explicit tenant
and company scope. The selected ledger book must be owned by the exact Account structure node,
and its profile must already have retained matching tenant/company ownership. The API does not
claim an unowned profile on first use. A broader entity/fund book cannot stand in for this slice's
account-owned book.

The route prefix is `/api/ledger/books/{ledgerBookId}/accounts/{accountId}` using canonical GUIDs.
An administrator sends `POST oversight/provision` under that prefix to provision the two local
history stores before capture. Provisioning initializes empty stores only; missing/corrupt initialized
state requires restoring retained history. Back up the complete configured data root, including
`reconciliation/account-cash-continuity`, `asset-operations/account-cash-forecasts` and Evidence Vault.
Close-price snapshots require the existing PostgreSQL Security Master and migration 037.

## Shared routes

| Workflow | Routes under the account prefix | Expected result |
| --- | --- | --- |
| Access context | `GET oversight/context` | Verified account/book/base currency, session actor and current read/write/review/provisioning capabilities |
| Provisioning | `POST oversight/provision` (administrator only) | Explicit durable store initialization without resetting retained history |
| Cash balance comparison | `POST cash-reconciliation/runs` | Immutable complete two-source comparison and stable cash case history |
| Case oversight | `GET cash-reconciliation/cases`; `POST cash-reconciliation/cases/{caseId}/assignments` | Retained owner, version, aging and source resolutions; stale assignment versions refuse |
| Exact replay | `GET cash-reconciliation/runs/{runId}/replay` | Reproduced retained source/tolerance evidence; unrelated account scope refuses |
| Price preparation | `POST close-price-sets/{valuationDate}` | Immutable retained observations, units, supported overrides and blockers |
| Price history | `GET close-price-sets/{valuationDate}` | All retained versions and approvals for the exact account/book/date after restart |
| Price review | `GET close-price-sets/{valuationDate}/{snapshotId}`; `POST .../{snapshotId}/approval` | Independent reviewer and rationale bound to the exact unblocked snapshot |
| Account forecasting | `POST cash-forecasts`; `GET cash-forecasts`; `GET cash-forecasts/{snapshotId}` | Replayable forecasts, cash reconciliation and linked earlier predictions |

Use the shared request contracts in `AccountCashReconciliationModels`, `SecurityMasterClosePrices`
and `AccountCashForecastDtos`. Authenticated identity supplies reviewer/assignment actor and
tenant/company authority. Price preparation/review additionally requires Security Master write
permission. Read and write access otherwise follows existing ledger permissions.
Price reads also require Security Master view or write permission.

HTTP forecast bodies use `0` (Expected) or `1` (Actual) for `flows[].status`, and
`0` (Observation), `1` (Projection) or `2` (DeclaredFx) for `provenance.role`.
Reconciliation source types use `0` (Prime), `1` (Custodian), `2` (Administrator)
or `3` (InternalLedger). Price quote units use `"CurrencyPerUnit"` or `"PercentOfPar"`.
Date-only fields use `YYYY-MM-DD`. Observation and recording timestamps cannot exceed
the server's current UTC time. The reviewed source JSON format below uses enum names.

## Browser workbench

Open **Accounting → Reconciliation → Account oversight**. Explicitly select an account-owned
ledger book; the workbench does not select an unrelated account by default. Account and book
identities remain in the URL for reloads. Current server capabilities determine read-only and
independent-review controls. Administrators can initialize empty history from this workbench.

Use **Cash cases** to inspect stable case identity, source state, owner, aging, resolutions and
executed tolerance versions. Select an observation to replay its original rows and evidence.
An owner handoff requires a rationale and the retained case version; after a conflict, refresh
before retrying. Import a complete comparison handoff, inspect its structured preview, then
capture it explicitly. A failed capture retains the imported run identity for a safe retry.
Save or discard an owner assignment draft before capturing a comparison.

Use **Close prices** to prepare an explicit security population at elapsed UTC cutoffs, review
selected and competing observations, and inspect units, overrides and blockers. Select retained
history after restart. An independent reviewer supplies a rationale for an unblocked set;
corrections retain the same security population and link the latest version. After an uncertain
capture outcome, reload the original valuation date's history, then use **Verify retained price
capture** or **Retry same price capture** to submit the unchanged command with its retained
request identity. New captures and approvals stay locked until that command is confirmed.
After an uncertain approval outcome, return to the approval's valuation date and reload its
history; the workbench verifies the retained review there before unlocking further actions.
When Security Master is not configured, close-price routes report unavailability rather than
empty history.

Use **Cash forecast** to import a scoped forecast handoff, inspect its opening/closing cash,
expected and actual flows, three dates, declared FX, source reviews and factor revisions, then
capture it explicitly. The downloadable template leaves required amounts and reviewed evidence
incomplete. Complete them from retained sources rather than inventing settled cash or approval.
Certification and its blockers come from the server. Prior predictions remain in history.

Comparison and forecast handoffs use the corresponding shared request shapes. Monetary fields
must be plain decimal **strings** in browser handoffs so importing cannot round them through
JavaScript numbers. Original source payloads remain unchanged strings. The browser accepts named
or numeric shared enums and submits numeric request enums. These handoffs are distinct from the
independently reviewed source documents below. Source bytes and review authority are still
verified by the server. Changing account scope aborts old requests and requires confirmation
before discarding unsaved observations. Errors remain unavailable or blocked rather than empty
or certified, and these workflows confer no posting or payment authority.

## Cash-balance continuity

Choose one account and the cash-balance break class. Supply exactly two independent source IDs
and types, complete populations, capture times, source versions and retained evidence references.
Each source supplies at most one native-currency balance per currency with its original payload,
exact account and business date. This slice refuses undeclared FX, duplicate balances, unrelated
accounts and partial captures. Assign an initial owner and retain the full executed tolerance profile.

The two sources must reference different retained evidence and must not reuse original row
payloads. Matching balance amounts from independently identified observations remain valid.
Within the exact account/book scope, reuse a source version only with its unchanged capture,
provenance, rows and original payloads. Corrections require a new source version even if an
intermediate run omitted the old revision or compared that source with a different counterpart.
Rejected submissions do not append a run or change an existing case.
Reuse a tolerance profile ID and version only with the same complete rules in this account/book
scope. Changed thresholds, date windows or rules require a new version. This also applies when
the next run compares a different source pair. Owner assignment timestamps must be no earlier
than the latest case observation and prior assignment; equal timestamps are permitted.
Previously retained runs with shared evidence references or copied original payloads fail replay
and retry under these controls. Their stored case state and history are not rewritten; investigate
any prior `SourceCleared` result and submit a new run with independently sourced observations and
new source versions where the retained source content changes.

Run again after a source correction with a new run ID, later observation time and a business
date no earlier than the retained head; run IDs cannot contain whitespace or `/ \ ? # %`, at
least one cash tolerance rule is required, and reformatted copies of a source payload remain
dependent evidence. The amount,
row ID, business date and tolerance version do not create a different case. The case retains its
original first-seen time and owner. Equal or tolerated balances mark `SourceCleared`; missing
balances cannot clear an existing case. Recurrence reopens the same case while earlier resolution
observations remain in history. Source clearing does not dispose of governed close casework.
Aging is measured through the retained last observation or resolution time; it does not
advance without a new observation.

## Price snapshots

Prepare the explicit account/book/UTC valuation-date security population and close cutoff. Prices
come from the existing hierarchy and retained selection receipts. Stale, unsupported, nonpositive,
mixed-unit and excessive-dispersion inputs remain visible blockers. Overrides require supporting
evidence and rationale; they cannot conceal unsupported source inputs. An independent reviewer
approves the frozen set. Later quotes leave it unchanged. A correction names the predecessor
snapshot, retains the same security population and appends a version with correction rationale.
Both the price evaluation and knowledge cutoffs must have elapsed when the set is created.
If dispersion exceeds the supported decimal range, the set retains its exact source prices and
an explicit `SourceDispersion` blocker; correct the inputs before requesting approval.
Identical concurrent approval retries return the same retained review and timestamp. A different
reviewer or rationale cannot replace an existing approval.
API callers can supply a nonempty UUID `requestId` and must preserve it with the complete
unchanged command across retries. Its retained snapshot identity is the same UUID; exact scoped
retry returns that immutable snapshot and any approval even after later quotes or corrections.
A changed command needs a new identity. Omitting `requestId` keeps the original new-capture
behavior and does not provide identified retry recovery.

## Forecast source format and review

HTTP forecast capture accepts only supported typed source JSON retained in Evidence Vault for the
exact account subject. Unsupported formats remain explicit source-authority blockers. The schema
is `AccountCashForecastSourceDocument`, version 1, with camel-case fields and string enum values.
A balance source has this shape:

```json
{
  "schemaVersion": 1,
  "kind": "balance",
  "sourceDomain": "custodian",
  "sourceEntityId": "opening-cash-record",
  "evidenceVersion": 1,
  "accountId": "00000000-0000-0000-0000-000000000001",
  "bookId": "00000000-0000-0000-0000-000000000002",
  "effectiveDate": "2026-10-13",
  "role": "Observation",
  "balance": { "asOfDate": "2026-10-13", "amount": 1000, "currency": "USD", "isSettled": true }
}
```

Use actual authoritative IDs and source observations. A `flow` payload retains stable flow/version
IDs, type, amount, currency, expected/actual status, all three dates and optional projection/terms
lineage. An `fx` payload retains currency, base currency, conversion date and base units per native
unit. Duplicate or unknown JSON properties refuse. Evidence Vault's accepted independent review
must confirm the exact `sourceContentHashSha256`; provenance must match the retained document,
review identity/time, original source, version and account subject. The API verifies retained bytes
and compares every economic field before capture.

In provenance, `evidenceId` is the retained document ID (`doc:ev-...`), and `evidenceUri`
is its canonical `evidence://evidence-vault/ev-...` URI. Subject type is `account` and
subject ID is the exact account GUID. Copy the accepted review's reviewer and UTC timestamp
into both `reviewedBy`/`reviewedAtUtc` and `retainedBy`/`retainedAtUtc`; retention here
identifies the durable review commit. Record the review through the document-review API so
its `DocumentReviewRecorded` audit event exists. Confirm the exact field name
`sourceContentHashSha256` with the complete source-byte digest. Source JSON is limited
to one MiB. An unsupported or mismatched source returns HTTP 409 with
`CASH_FORECAST_SOURCE_AUTHORITY_REQUIRED`.

Opening means start-of-day settled cash; closing means end-of-day settled cash over the inclusive
period. Retain separately identifiable observations for both boundaries, even when a single-day
period has equal opening and closing amounts. Reusing source bytes or the same retained evidence
identity/version for both boundaries blocks certification. Receipt date changes cash; economic
and settlement dates explain the obligation. Linked expected and actual flows must retain the
same security identity; an identity present on only one side blocks certification. Actual
cash replaces its corresponding expectation in the forecast and is counted once. A dated declared
FX rate is required for each selected foreign cash observation or flow. Missing account scope,
source authority, FX, unsettled expectations or unexplained closing variance blocks certification.
Linked revisions retain prior predictions, including factor restatements.
Each flow `versionId` identifies one immutable source revision throughout the retained
account/book/period history, including snapshots that temporarily omit the flow. Changed
economics, status or identity require a new version and the applicable supersession link.
Valid older receipts replay unchanged. Previously certified receipts violating the boundary or
security-link rules fail replay closed. Preserve those receipts, review their source observations
and append a linked correction with the required independent evidence; never rewrite old history.
If a run temporarily omitted a flow, `supersedesVersionId` still names its last retained revision
with the same expected/actual status. `previousSnapshotId` names the latest snapshot for the
account/book/period. Reintroducing an unchanged revision preserves its original supersession link.

## Recovery and evidence

Preserve the original run/snapshot ID and response. Conflicting input under an existing run ID
requires a new correction, rather than replacement. On missing/corrupt durable state, stop capture
and restore the complete history from backup; provisioning is not a history-reset operation.
Acceptance proof is in `AccountCashReconciliationContinuityTests`,
`SecurityMasterClosePriceServiceTests`, `PostgresSecurityMasterClosePriceTests`,
`AccountCashForecastTests` and the account oversight endpoint/source-adapter tests.
