# Meridian Domain Model

**Status:** active guidance  
**Owner:** core-team  
**Reviewed:** 2026-10-30

## Purpose

This document defines the compact domain model that AI sessions should use before creating Meridian entities, services, UI workflows, tests, or reports.

## Core Concepts

The core model is customer-neutral. `Fund` remains a first-class specialization, but the platform
root is a governed operational record scoped by organization, entity, portfolio, account, book,
period, strategy, order, execution, evidence, approval, journal, report, and audit trail.
Strategy formulation, order management, and governed strategy execution join financial operations
as explicit product objectives. Their expanded workflows remain planned under `W13-STRAT-001` and
`W13-OMS-001`; the concepts below do not assert complete runtime coverage.

| Concept | Definition | Primary relationships |
| --- | --- | --- |
| Organization | Customer or operating group whose entities, portfolios, accounts, books, users, and reports are governed together. | Governs entities, portfolios, operating policies, reports, and audit scope; its entities own the financial accounts and accounting books. |
| Entity | Legal, reporting, operating, or economic party whose activity needs controlled evidence and accounting treatment. | Belongs to an organization; can own accounts, portfolios, books, obligations, reports, and close periods. |
| Portfolio | Investment sleeve, strategy container, treasury pool, or operating portfolio used to group holdings and activity. | Belongs to an organization or entity; holds positions; receives transactions and valuations. |
| Account | Financial container where assets, cash, obligations, or observed activity live, such as a bank, custody, brokerage, operating, vendor, or customer account. | Retains entity and tenant ownership; links transactions, positions, reconciliations, counterparties, and evidence. Its identity is distinct from a Ledger Account. |
| Book | Entity-owned accounting record with an explicit basis, functional currency, calendar, and policy versions. | Contains accounting periods, ledger accounts, immutable journals, balances, close posture, and report outputs; portfolio and financial-account scopes link activity without replacing the owning entity. |
| Accounting Period (Period) | Book-scoped accounting interval with governed review, close, lock, and reopen state. | Owns the period authority checked when journals are posted or corrected. |
| Ledger Account | Accounting classification used for balanced debit and credit postings within a book. | Belongs to the book's chart of accounts and receives journal lines; a financial Account can map to several ledger accounts under declared accounting policy. |
| Fund | Legal or reporting vehicle whose books, capital, positions, and reports must be controlled. | Specializes entity/portfolio/book behavior for fund administration, investor capital, and private-capital operations. |
| Security | Canonical identity for a financial instrument or investable interest. | Security Master owns identity, identifiers, classification, and reference evidence; Instruments and Asset Operations add terms, roles, positions, and economic projections keyed by `SecurityId`. |
| Strategy Definition / Retained Artifact | Versioned investment logic, universe, parameter schema, data dependencies, risk constraints, and supported execution modes. | Belongs to an authorized owner scope; a run binds its exact retained version and inputs. This is a domain concept, not a claim of one universally adopted implementation type. |
| Strategy Run | Scoped evaluation or execution of a retained strategy version in a declared backtest, replay, paper, or live mode. | Retains inputs, data and calculation versions, account context where required, results, lifecycle events, and parent/child run lineage. |
| Promotion Decision | Retained authorization to advance a particular strategy/run into a permitted next mode under current policy. | Binds exact evidence and reviewed operands; does not itself place orders or replace account authorization and runtime admission. |
| Order Intent | Reviewed discretionary instruction or strategy decision requesting an order in a declared financial account. | Retains origin, owner/account scope, requested terms, strategy/run lineage where applicable, risk evidence, and command identity before admission to the OMS. |
| Order | Authoritative order-management record joining admitted intent, client identity, requested terms, observed lifecycle, and broker identities. | Owns amendment/replacement and cancellation history; broker observations establish external outcomes, and approved execution controls govern actions. |
| Execution Report / Fill | Retained broker or supported simulator observation about an order and any executed quantity, price, fees, and timing. | Links to exact order and event identity, lifecycle recovery, portfolio effects, and the governed accounting handoff; it cannot directly rewrite a posted journal. |
| Instrument Role | Effective-dated relationship an instrument has to a governed scope, such as holder, issuer, lender, borrower, payer, or receiver. | Connects a Security to a book, entity, portfolio, account, or counterparty without changing Security Master ownership. |
| Netting Set Exposure Snapshot | Append-only, versioned valuation of the bilateral contracts in one netting set on one date: netting basis, gross and net exposure, collateral held and the credit support movement each side's agreement terms imply. | Derived from the netting set's contracts, each contract's valuation and the collateral balances under its collateral agreement; cites the Securities valued; is not a ledger balance. See [Credit Support](../domain/credit-support.md). |
| Margin Call | One collateral movement from an exposure snapshot taken through issue, agreement, dispute, settlement or cancellation; its amount is copied from the snapshot. | Belongs to a netting set and its collateral agreement, cites the snapshot it was issued from, and is recorded in the Security Master governance log with its actor on every step. |
| Position | Quantity and valuation exposure for a security in a portfolio at a point in time. | Derived from transactions, prices, corporate actions, and accounting events. |
| Book Position | Effective-dated, versioned position context for a Security in an accounting book and owner scope. | References Security Master identity, an instrument role, book scope, source evidence, and projected economic state; it is not a ledger balance. |
| Position Economic State | Rebuildable projection of economically relevant position state at an effective time. | Derived from retained source events and position version; can support a posting candidate but cannot replace a posted journal. |
| Economic Event Reference | Typed identity and evidence pointer for the operational event that may have accounting impact. | Connects legacy source-event identity, Security, book position, effective date, and retained evidence to candidate and journal lineage. |
| Projection Lineage | Read-only trace of the projection model, run, event, input evidence, and version used to derive economic state or amount. | Explains a posting candidate and downstream journal without becoming posting authority. |
| Transaction | Business event that changes holdings, cash, obligations, ledger balances, or evidence state. | References scope, account or portfolio, source evidence, effective date, and processing status. |
| Operational Event | End-to-end operating object such as a trade, cash movement, invoice, fee, subscription, redemption, transfer, corporate action, valuation, reconciliation, adjustment, approval, close task, or fund event. | Connects evidence, workflow, posting candidate, journal lifecycle, ledger impact, reporting, delivery, and audit history. |
| Ledger | Authoritative double-entry accounting record within an entity-owned accounting book. | Contains immutable journal entries and ledger-account balances; reporting across organization, portfolio, account, or fund scopes retains the contributing books and perimeter. |
| Accounting Book Context | Authoritative ledger-book, period, basis, policy, currency, owner-scope, and dimension context resolved for a posting workflow. | Snapshotted in shared contracts for validation and evidence; client assertions must match server-owned book state. |
| Accounting Rule Pack Reference | Typed reference to the existing promoted accounting policy rule pack and selected rule/version. | Explains candidate generation without defining another rules engine or source of accounting truth. |
| Journal Entry | Authoritative balanced accounting aggregate after posting. | Owns child ledger entries and posts debits and credits to ledger accounts; requires source, effective date, posting date, approval state, evidence, and governed book context. |
| Capital Account | Investor-level record of contributions, allocations, distributions, fees, and balances. | Belongs to investor and fund; impacted by fund accounting events. |
| Investor | Person or entity participating in a fund. | Owns one or more capital accounts and receives statements/reports. |
| Reconciliation | Controlled comparison between internal records and external evidence. | Produces breaks, explanations, approvals, and audit evidence. |
| Audit Evidence | Retained source packet or generated manifest proving why a record changed. | Linked to transactions, journal entries, reconciliations, reports, and approvals. |
| Fund Event | Fund/private-capital specialization of Operational Event for closings, capital calls, distributions, investments, fees, valuations, tax requests, audit requests, or wind-down steps. | Connects fund evidence, workflow, treasury expectation, ledger impact, capital-account impact, reconciliation, reporting, delivery, tax, and audit history. |
| Operational Evidence Graph | Proof chain that connects source evidence to normalized records, validation, reconciliation, ledger impact, capital accounts, close, reporting, delivery, and audit evidence. | Links all operational records that explain why a number is trusted, blocked, approved, reported, delivered, or restated. |

## Invariants

- Accounting-impacting changes must preserve double-entry balance.
- Financial Account, Book, Ledger Account, and Capital Account retain distinct identities and
  purposes. A custody account is not a general-ledger classification; a capital-account subledger
  reconciles to the ledger rather than replacing its journals.
- An accounting Book has an explicit owning entity. Organization, portfolio, account, fund, and
  consolidated views declare their scope and source books; a reporting aggregate does not become
  a new posting book or supply cross-entity posting authority. See
  [Account and Book Scopes](../domain/account-and-book-scopes.md).
- Journal entries are immutable after posting; corrections occur through reversal and rebook.
- `JournalEntry` is the accounting aggregate. `Transaction` remains the broader business-event term;
  the two names must not be treated as interchangeable.
- Candidate journals are reviewed drafts, while position state, projection events, and balance
  snapshots are rebuildable views. The posted journal remains the sole accounting truth.
- Operational records require retained source evidence or an explicit operator rationale.
- Strategy validation, promotion, account authorization, and live order admission are separate
  decisions. A changed definition, run input, account, or evidence version cannot reuse stale
  approval. A manual order must use an explicit authority contract, never invented strategy lineage.
- Order intent and requested amendments/cancellations remain distinct from confirmed broker
  outcomes. Uncertain dispatch, pending replacement, and unresolved exposure must remain visible.
- Duplicate or out-of-order execution reports and restart recovery must preserve order identity
  and avoid duplicate economic effects or accounting handoff. The OMS owns order truth; the ledger
  owns posted accounting truth.
- Fund scope must not be required unless the workflow is explicitly fund, investor, or private-capital specific.
- UI workflows must expose status, source, validation state, and review actions when records affect accounting or reconciliation.
- Shared service/read-model seams should support both desktop and browser workstation surfaces.
- AI assistance can draft, classify, extract, match, summarize, and flag issues, but cannot approve its own work, post material journals, override period locks, release payments, publish governed reports, or erase evidence.
- A record is not complete until its evidence, approvals, ledger or capital impact, report usage, delivery posture, and audit history can be reconstructed where those dimensions apply.

## Shared Surface Rule

Browser and WPF workflows can use different presentation patterns, but they should share record identifiers, filters, saved views, status definitions, proof-ribbon states, evidence links, approval states, audit events, endpoint contracts, and read models. Presentation can diverge; business state cannot.

## Strategy and Execution Ownership

- Strategy services own formulation, retained definitions and run evidence, promotion decisions,
  and strategy lifecycle. Unsupported authoring constructs or execution modes remain explicit.
- Execution services own authoritative order admission, risk/control coordination, routing,
  lifecycle recovery, and retained broker outcomes. Strategies and operator tickets use shared
  execution services; clients do not maintain independent order truth or bypass the gateway.
- Portfolio and financial services consume retained execution effects through the existing
  governed handoff. Fill receipt, accounting acceptance, and journal posting retain distinct
  authority, outcome, and recovery evidence.

The [strategy, order, and execution dictionary](../domain/strategy-order-execution.md) connects
these requirements to current source boundaries and the planned W13 acceptance work. The existing
instrument-to-accounting dependency direction below remains intact.

## Instrument and Accounting Ownership

The dependency direction is:

```text
Reference Data / Security Master
-> Instruments / Asset Operations
-> Financial Operations
-> Ledger / Storage
```

- Security Master remains the canonical owner of security identity, identifiers, classification, and
  reference-data evidence. A unified passport or instrument read model may compose downstream terms,
  roles, positions, and proof without introducing a parent Instrument Master or reversing this
  dependency.
- Instruments and Asset Operations own economic interpretation: instrument roles, book-position
  context, position economic state, and projection lineage keyed by `SecurityId`.
- Financial Operations owns governed candidate generation, book-context resolution, accounting
  policy/rule-pack selection, evidence readiness, and approval workflow.
- Ledger and Storage own immutable `JournalEntry` append, balanced child `LedgerEntry` records,
  idempotency, period and basis guards, correction lineage, and durable accounting truth.

These concepts began as additive semantic contracts. Durable role, book-position, and economic-state
projections now live in the separate Asset Operations schema with effective dating, retained source
and approval provenance, optimistic concurrency, and append-only state history. Their adoption does
not migrate or backfill existing Security Master, direct-lending, portfolio, fund-account, or
asset-family records, and the projection schema never stores ledger balances.

## Expansion Notes

New domain objects should be added first to `docs/domain/` with definition, relationships, rules, examples, and future notes before broad code generation.
