# Intercompany agreements

**Status:** active
**Owner:** Accounting and Ledger
**Reviewed:** 2026-11-12

An intercompany agreement records the reviewed terms between two legal entities in one tenant.
It explains the contractual source of intercompany activity; it is not a journal, a payment or
evidence that two balances match. The [technical blueprint](../engineering/blueprints/intercompany-agreement-registry.md)
defines its contracts, effective-date rules and implementation boundaries.

## Identity, terms and approval

The agreement has a stable ID and approved integer versions. Each approved version retains its
parties and roles, currency, effective dates, terms, submitted evidence hashes, actors and decision
time. Party identities, roles, type and currency are fixed after activation. A replacement or
novation needs a new agreement. There is one open proposal per agreement; draft edits retain their
history, submission freezes the reviewed snapshot, and approval requires a reviewer who did not
author or submit it. Authenticated server context supplies tenant and actor.

The registry covers loans, management fees, cost shares, cash sweeps, guarantees and other documented
bilateral arrangements. Rates, schedules and allocation descriptions are retained terms, not an
accrual or settlement calculation. Settlement days mean calendar days. Evidence must resolve to an
accepted retained Evidence Vault document in the operator's tenant/company scope; an arbitrary URL,
client-provided hash or inaccessible artifact cannot authorize approval.

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

Registry approval does not post either side of an entry. Automatic accruals, paired posting, cash
allocation/settlement, wider consolidation and FX translation remain outside P1. Native WPF runtime
validation requires Windows; Linux source checks do not establish native interaction acceptance.
