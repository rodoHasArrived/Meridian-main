---
title: Financial Visualization Workflows
status: active
owner: core-team
reviewed: 2026-11-12
audience: operators
---

# Financial visualization workflows

The browser workstation adds graphical investigation to existing operational tables and
inspectors. Read the source, currency, observation date, coverage and finality alongside each
view. A plotted point is an investigation target; it does not grant execution or accounting authority.

| Question | Browser surface | Meaning |
| --- | --- | --- |
| Where do loan maturities concentrate? | Portfolio → Loan Book | Current outstanding principal grouped by contractual maturity quarter and currency; not scheduled repayments or forecast refinancing. Select a bar to filter the facility table. |
| Which exercise governs a bond? | Security Master → Evidence → Bond analytics | Server-calculated discrete exercise yields. Yield to worst considers issuer calls and maturity, excluding holder puts. The payment strip retains contractual maturity payments per 100 face. |
| How large is a quantity relative to the book? | Data → Market data → Quotes | Illustrative consumption of the returned bid/ask snapshot. Beyond displayed levels is a coverage limit, not proof of market-wide exhaustion or predicted execution. |
| Where does margin disagree? | Accounting → Margin Control | Broker requirements versus Meridian estimates, grouped by currency, with a zero excess-liquidity boundary. Broker authority remains explicit. |
| Why does the tax result differ? | Posted journal → disposal tax details | Retained holding-period days and economic gain/loss. Select a parcel for recognized gain, deferred loss, character and provisional/settled evidence. |
| What happened to an order? | Trading → selected order | Explicitly linked audit events and fills, with expandable evidence and source availability. An unrecorded check is unknown; sequence jumps are not proof of missing order events. |
| Through which entities is an asset held? | Portfolio → Family Office | Current, authorized legal ownership relationships. Account/custody association and economic exposure remain distinct. Multiple clients require explicit selection. |
| What dates occur this week? | Corporate-action inbox → Calendar | Ex, record, payment and separately sourced election deadlines. The calendar describes retained events in scope; it does not promise a complete external market feed. |
| Where is private capital committed and returned? | Portfolio → Family Office → Private-capital lifecycle | Separate source-reported commitment, called, unfunded, distributions and NAV amounts. No currency conversion or consolidated-total inclusion is inferred. |
| Which option contracts and strategies warrant review? | Data → Market data → Options | Contract screening by DTE/delta and liquidity criteria, plus an explicit strategy basket and dated imported broker assessments. Contract filter results are distinct from whole-basket assessment. |

## Private-capital source observations

`ViewPrivateCapitalRecords` permits reads. `ManagePrivateCapitalRecords` permits registration and
source observation imports, subject to authorized entity/security and evidence scope. Standard
accounting roles receive the latter; technical and viewing roles do not inherit it.
The permission must also authorize the holder's `LegalEntity` scope. Read grants cannot accept
observations; entity choices and entry controls show only holders with a management grant.

1. Retain the source document in Evidence Vault. Record its vault ID, document ID and SHA-256.
2. Register the holder entity, `PrivateFundInterest` security and currency explicitly. Each distinct
   commitment receives its own identity; names and legacy assignment references are not identity joins.
3. Enter an observation or import canonical CSV with existing commitment IDs. Preserve the financial
   as-of date and separate NAV valuation date. Blank amounts mean unavailable, including when other
   amounts are reported as zero.
4. Preview and review the source values, evidence and correction references, then explicitly accept.
   Acceptance sends the preview identity and fingerprint; the service retains the source input and receipt.
5. Correct an observation by explicitly superseding its retained snapshot with a rationale. The prior
   version remains in history and cannot reappear as current evidence at an earlier financial date.

Canonical observation CSV headers:

```text
commitment_id,as_of_date,nav_valuation_date,commitment_amount,called_amount,unfunded_amount,cumulative_distributions,nav_amount,source_reference,supersedes_snapshot_id
```

These are holder-side source observations. They are separate from issuer-side commitment approval,
capital-call capacity, notices, funding intake and posted capital accounts. Called and unfunded
are not forced to partition commitment; source-reported recallable distributions may matter.
Distributions and remaining NAV stay separate. Lifecycle values do not enter consolidated totals
until explicit reconciliation and representation mapping prevents duplicate counting.

## Election-deadline evidence

Record deadlines on an existing legitimate corporate-action case. Terms-resolution or election
permission is required; recording a deadline does not submit an election or alter financial terms.
Use the source's issuer, broker or custodian cutoff kind and the exact account/intermediary scope.
Retain the original source text, evidence reference, digest and rationale.

A date-only cutoff means its time was not supplied. A timed cutoff requires an explicit offset;
a supplied named timezone must agree. Never substitute an offer expiration, case creation time or
`ElectionPending` state for a deadline. Corrections and withdrawals append evidence to the same
deadline series. Frozen cases retain their existing governed reopen/restatement requirement.

The CSV editor exposes the canonical header and validates the same rules as manual capture.
Imports report each committed row and any failed row. After an uncertain or partial response,
retry the exact original request with its retained command identity rather than starting another import.
Calendar coverage stays partial until every page loads; a changed revision requires a fresh range read.

## Imported broker option assessments

Construct the exact basket first: resolved contracts and deliverables, sides, opening/closing intent,
ratios, quantity, optional equity legs, net order terms, account and margin regime. Import reviewed
normalized fields plus the original broker report using manual entry or the canonical CSV template.
`ManageOptionAssessments` is separate from market-data access and order-execution permission.

An assessment matches only its complete basket/account fingerprint and observation context.
The display says **Broker assessed as of …**. Any changed leg, size, terms or account invalidates
the match. Account permissions and restrictions are separate evidence. Missing or incomplete
coverage means **Cannot assess**, even if an import succeeded. A retained what-if report does not
establish current collateral, broker acceptance or future execution; no capacity is reserved.

## Recovery and verification

Inspect the retained source and receipt before replacing a request whose outcome is uncertain.
Scope changes discard obsolete responses and drafts. Failed authoritative reads do not substitute
development fixtures. Synthetic browser checks demonstrate interaction behavior only; persisted
service/store tests separately establish restart, replay and correction behavior.
