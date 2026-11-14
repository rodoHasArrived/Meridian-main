# Intercompany agreements

**Status:** active
**Owner:** Accounting and Ledger
**Reviewed:** 2026-11-14

An intercompany agreement records the reviewed terms between two legal entities in one tenant.
It explains the contractual source of intercompany activity; it is not a journal, a payment or
evidence that two balances match. The [technical blueprint](../engineering/blueprints/intercompany-agreement-registry.md)
defines its contracts, effective-date rules and implementation boundaries.

## Identity, terms and approval

The agreement has a stable ID and approved integer versions. Each approved version retains its
parties and roles, currency, effective dates, terms, submitted evidence hashes, actors and decision
time. Party identities, roles, type and currency are fixed after activation. A replacement or
novation needs a new agreement. There is one open proposal per agreement; draft edits retain their
history, and submission freezes the reviewed snapshot. Ordinary independent approval requires a
reviewer who did not author or submit it. The separate external-review attestation described below
allows the preparing operator to record an external review without impersonating that reviewer.
Authenticated server context supplies tenant and actor.

The registry covers loans, management fees, cost shares, cash sweeps, guarantees and other documented
bilateral arrangements. Rates, schedules and allocation descriptions are retained terms. The registry
itself calculates nothing; the one governed calculation over these terms is the
[loan-interest accrual](#governed-loan-interest-accrual) below. Legacy `SettlementDays` means calendar days. Evidence must resolve to an
accepted retained Evidence Vault document in the operator's tenant/company scope; an arbitrary URL,
client-provided hash or inaccessible artifact cannot authorize approval.

## Structured declarations and date previews

Optional `Terms.StructuredTerms` adds versioned legal declarations to this registry; it does not
introduce a parallel contract record or an editable legal field on accounting profiles. An absent
structured companion means `Unstructured`. Null companions are omitted from JSON so retained
command/review hashes and approved payloads remain unchanged. Existing approved revisions are not
rewritten; adding structured terms requires a new legal proposal and review.

| Kind | Structured declarations |
| --- | --- |
| Loan | Fixed annual decimal rate, or declared floating index and spread in basis points; explicit day count, compounding and schedule. |
| Management fee | Fixed monetary amount, or annual decimal fee rate and calculation-base reference; schedule. |
| Cost share | Fixed percentages among distinct existing agreement parties totaling exactly 100%, or an explicit allocation-driver reference; schedule. |
| Guarantee | Beneficiary reference and optional declared limit; disclosure only, without a posting treatment or obligation schedule. |

Currency remains the agreement's legal currency. Structured schedules declare bounds, payment
frequency, calendar, business-day convention and `SettlementBusinessDays`. That business-day lag
does not reinterpret legacy calendar-day `SettlementDays`. Decimal rates use fractions (`0.05`
means 5% annually), spread uses basis points, and all new decimal declarations travel as exact
decimal strings. Structured completeness does not establish that fixings, measured allocation
drivers or an amount-calculation engine are available.

The shared date preview maps declared terms to the existing schedule/day-count/calendar kernels.
It returns accrual boundaries and payment dates with the legal subject hash, kernel version and
calendar ID/hash. Missing calendars, unsupported conventions or incomplete declarations produce
an unavailable preview. `WEEKENDS` must be selected explicitly; no fallback or prose interpretation
supplies missing terms. Calendar-data changes affect preview evidence rather than rewriting the
approved legal declaration.

The date preview calculates dates only. It does not calculate interest or fees, allocate costs, fetch
fixings, generate counterpart proposals, settle balances or infer due dates for retained journals.

## Governed loan-interest accrual

A separate, operator-requested accrual turns a fixed-rate, simple-interest loan's approved structured
terms into an amount for a half-open window `[AccrualFrom, AccrualTo)`. [ADR-028](../adr/028-governed-intercompany-interest-accrual.md)
records the decision and the calculation rule `intercompany-accrual-v1`.

Each day accrues under the approved `LoanInterest` accounting-profile revision applicable on that day
and the legal revision it pins. The window is cut at every profile-revision boundary, schedule-period
boundary and principal change; each slice is principal times annual rate times the day-count fraction
of its own revision. A rate reset or amendment inside a period is therefore prorated by day. Slices are
summed unrounded and rounded once, half away from zero, to the agreement currency's minor unit.

Principal is not entered. It is the posted balance of the approved `Loan` profile of the same legal
agreement in the source book, with a movement taking effect on its own date. A receivable balance
makes the source book the lender and a payable balance makes it the borrower.

The amount becomes a draft in the source book's ordinary journal review, dated on the last accrued
day. It is not submitted, approved or posted for the operator. After a reviewer posts it, the existing
proposal pipeline delivers the counterpart as a draft into the destination book's review queue. The
draft retains the agreement version and payload hash and the profile revision of every slice, the
principal journals, the calendar and kernel identity and any FX conversion, with a digest. A client
save cannot replace that evidence, lines that no longer match it block review, and it is carried onto
the posted journal.

Nothing is estimated. The accrual is unavailable, with no amount, when any day lacks a single approved
profile revision, falls outside the declared schedule or lacks provable principal; when terms are
unstructured, floating-rate or compounding; when the calendar is missing; when a posted, unreversed
accrual already covers part of the window; or when the source book has no single open period
containing the posting date. A cross-currency loan converts through the governed FX authority or is
unavailable. Fees are not calculated: a fixed fee has no declared amount period and a percentage fee
has no measured base in terms schema v1.

## External-review attestation

An explicit external-review decision binds the exact submitted hash and expected aggregate
version. It retains a distinct external reviewer's name/reference, non-future review date,
accepted immutable document evidence, rationale, and the authenticated attesting operator/time.
The preparing operator may attest; the named external reviewer must be distinct from that
operator and is not represented as an authenticated Meridian approver.

The evidence authority resolves the exact retained document artifact and source-byte hash in
the operator's tenant/company scope. Changed evidence, an altered subject hash or missing authority
blocks the decision without changing the retained legal state. Clients label this decision
“Externally reviewed; attested by …”. Omitting the external-review companion retains the existing
independent-review rules and historical JSON representation. This choice grants no journal
approval or posting authority.

## Effective time and historical knowledge

Business-effective dates use `[EffectiveFrom, EffectiveTo)`; recorded approval timestamps use UTC.
For a given date, select the approved revision with the greatest effective start, breaking equal
starts by approval version. Check its exclusive end without falling back to an older expired
revision. An approval's sequence number is separate from the revision applicable on a date.

A retrospective amendment requires rationale. If April terms have already been approved, a later
February-effective amendment governs February through March; it does not silently replace April.
An amendment's semantic base is the revision selected at its proposed start, while the aggregate
version independently protects concurrent edits. A final termination decision supplies a cutoff
across all revisions and never permits scheduled reactivation. Historical knowledge queries exclude
facts recorded after their requested cutoff.

## Journal references and coverage

Journal lines may supply `IntercompanyAgreementId` and `IntercompanyAgreementVersion` together.
They identify the exact approved revision, rather than a mutable latest agreement. A new posting
validates the reference against current authority on its accounting date, the authoritative book
entity, reciprocal counterparty and transaction currency. PostgreSQL retains the authority lease
through journal commit, including when the agreement and ledger use separate database connections.
Unsupported external-transaction paths refuse references rather than bypass their guard.

Missing references remain permitted and appear as advisory coverage warnings on supported
receivable/payable accounts. Invalid supplied references block. An amendment may make an unposted
draft stale; it does not rewrite a posted journal. Exact committed retries recover retained
receipts. A verified inverse of an immutable source journal may preserve its original provenance;
a source ID by itself cannot justify an exception, and a rebook uses current authority.

Agreement coverage and source-line drill-through are advisory. They do not change the current
[consolidation](intercompany-consolidation.md) calculation, determine settled amounts, infer a
contract from matching amounts, or authorize netting between different agreements.

## Configuration and operator surfaces

The registry uses the configured PostgreSQL fund-structure store and its additive migration `006`.
Missing durable registry authority produces an explicit unavailable response, without creating an
unattributed in-memory fallback. Both legal entities and every disclosed historical party require
the caller's tenant ownership and scoped access. Reads and commands use the shared
`/api/fund-structure/intercompany-agreements` routes.

The browser Accounting configuration view and desktop Fund Operations Agreements tab use those
same contracts. Operators can retain drafts, inspect evidence and revision history, submit changes,
and review eligible proposals. Mutation capabilities are calculated by the server. A stale version
requires comparison/reload; the client retains local input after refusal.

`MERIDIAN_INTERCOMPANY_REFERENCE_AUTHORING_ENABLED=true` enables the browser's optional journal
reference editor only when the host also has the posting authority registered. It defaults off.
Enable it after every reachable writer, worker and importer supports the reference contract and
validation. The gate controls authoring availability; it never disables validation of a supplied
reference. Rollback must retain compatible writers and historical readers after references exist.

Registry approval does not post either side of an entry. Automatic or scheduled accruals, automatic
paired posting, cash allocation/settlement, wider consolidation and FX translation remain outside P1.
The operator-requested loan-interest accrual above produces reviewed drafts only. Native WPF runtime
validation requires Windows; Linux source checks do not establish native interaction acceptance.
