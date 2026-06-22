# Meridian Domain Model

**Status:** active guidance  
**Owner:** core-team  
**Reviewed:** 2026-06-16

## Purpose

This document defines the compact domain model that AI sessions should use before creating Meridian entities, services, UI workflows, tests, or reports.

## Core Concepts

The core model is customer-neutral. `Fund` remains a first-class specialization, but the platform
root is an operational finance record scoped by organization, entity, portfolio, account, book,
period, evidence, approval, journal, report, and audit trail.

| Concept | Definition | Primary relationships |
| --- | --- | --- |
| Organization | Customer or operating group whose entities, portfolios, accounts, books, users, and reports are governed together. | Owns entities, portfolios, accounts, books, operating policies, reports, and audit scope. |
| Entity | Legal, reporting, operating, or economic party whose activity needs controlled evidence and accounting treatment. | Belongs to an organization; can own accounts, portfolios, books, obligations, reports, and close periods. |
| Portfolio | Investment sleeve, strategy container, treasury pool, or operating portfolio used to group holdings and activity. | Belongs to an organization or entity; holds positions; receives transactions and valuations. |
| Account | Bank, custody, brokerage, operating, vendor, customer, or GL account where assets, cash, obligations, or activity live. | Links transactions, balances, reconciliations, ledger accounts, counterparties, and evidence. |
| Book | Accounting or operational record set for an organization, entity, portfolio, account, or fund. | Contains periods, policies, journal entries, balances, close posture, and report outputs. |
| Fund | Legal or reporting vehicle whose books, capital, positions, and reports must be controlled. | Specializes entity/portfolio/book behavior for fund administration, investor capital, and private-capital operations. |
| Security | Financial instrument or investable interest. | Has issuer/reference data; appears in positions and transactions. |
| Position | Quantity and valuation exposure for a security in a portfolio at a point in time. | Derived from transactions, prices, corporate actions, and accounting events. |
| Transaction | Business event that changes holdings, cash, obligations, ledger balances, or evidence state. | References scope, account or portfolio, source evidence, effective date, and processing status. |
| Operational Event | End-to-end operating object such as a trade, cash movement, invoice, fee, subscription, redemption, transfer, corporate action, valuation, reconciliation, adjustment, approval, close task, or fund event. | Connects evidence, workflow, posting candidate, journal lifecycle, ledger impact, reporting, delivery, and audit history. |
| Ledger | Double-entry accounting record for an organization, entity, portfolio, account, book, fund, or reporting scope. | Contains immutable journal entries and account balances. |
| Journal Entry | Balanced accounting entry. | Posts debits and credits to ledger accounts; requires source, effective date, posting date, and approval status. |
| Capital Account | Investor-level record of contributions, allocations, distributions, fees, and balances. | Belongs to investor and fund; impacted by fund accounting events. |
| Investor | Person or entity participating in a fund. | Owns one or more capital accounts and receives statements/reports. |
| Reconciliation | Controlled comparison between internal records and external evidence. | Produces breaks, explanations, approvals, and audit evidence. |
| Audit Evidence | Retained source packet or generated manifest proving why a record changed. | Linked to transactions, journal entries, reconciliations, reports, and approvals. |
| Fund Event | Fund/private-capital specialization of Operational Event for closings, capital calls, distributions, investments, fees, valuations, tax requests, audit requests, or wind-down steps. | Connects fund evidence, workflow, treasury expectation, ledger impact, capital-account impact, reconciliation, reporting, delivery, tax, and audit history. |
| Operational Evidence Graph | Proof chain that connects source evidence to normalized records, validation, reconciliation, ledger impact, capital accounts, close, reporting, delivery, and audit evidence. | Links all operational records that explain why a number is trusted, blocked, approved, reported, delivered, or restated. |

## Invariants

- Accounting-impacting changes must preserve double-entry balance.
- Journal entries are immutable after posting; corrections occur through reversal and rebook.
- Operational records require retained source evidence or an explicit operator rationale.
- Fund scope must not be required unless the workflow is explicitly fund, investor, or private-capital specific.
- UI workflows must expose status, source, validation state, and review actions when records affect accounting or reconciliation.
- Shared service/read-model seams should support both desktop and browser workstation surfaces.
- AI assistance can draft, classify, extract, match, summarize, and flag issues, but cannot approve its own work, post material journals, override period locks, release payments, publish governed reports, or erase evidence.
- A record is not complete until its evidence, approvals, ledger or capital impact, report usage, delivery posture, and audit history can be reconstructed where those dimensions apply.

## Shared Surface Rule

Browser and WPF workflows can use different presentation patterns, but they should share record identifiers, filters, saved views, status definitions, proof-ribbon states, evidence links, approval states, audit events, endpoint contracts, and read models. Presentation can diverge; business state cannot.

## Expansion Notes

New domain objects should be added first to `docs/domain/` with definition, relationships, rules, examples, and future notes before broad code generation.
