# Meridian Vision

**Status:** active guidance  
**Owner:** core-team  
**Reviewed:** 2026-06-16

## What Meridian Is

Meridian is an investment operations platform focused on the operational record required to run funds, portfolios, accounting workflows, reconciliation, approvals, audit evidence, and governed reports.

It should help operators understand and control:

- portfolio holdings, positions, transactions, and valuations;
- fund accounting records, ledgers, journal entries, close workflows, and capital activity;
- reconciliation breaks, retained source evidence, exception handling, and approvals;
- audit trails, evidence packets, report provenance, and operator actions;
- data acquisition, provider health, lineage, validation, and confidence signals.

Meridian's near-term wedge is the operational proof layer: the connected chain from source evidence through validated records, reconciliation, ledger impact, capital-account impact, close readiness, report-line provenance, delivery evidence, and audit support.

## Core Modules

Meridian's long-term module map centers on:

- Portfolio Management
- Fund Accounting
- General Ledger
- Capital Accounts
- Reconciliation Studio
- Audit Studio
- Reporting Studio
- Data and Provider Operations
- Strategy and paper-governed execution support where it strengthens the operational record

The active product baseline should keep these modules anchored to W1-W5 operational records before broad platform expansion:

- data confidence and retained source evidence;
- reconciliation and exception casework;
- approval and scoped authority;
- accounting records, journal evidence, and capital-account impact;
- multi-asset operational coverage;
- governed reporting, delivery evidence, and audit packages.

## What Meridian Is Not

Meridian should not drift into being primarily:

- a trading OMS or EMS;
- a CRM;
- a cap table platform;
- a payment processor;
- a mobile-first product;
- a no-code workflow builder;
- a general enterprise-risk or forecasting suite detached from operational records.

AI-generated work should also avoid treating Meridian as an autonomous accounting agent. AI can assist with extraction, matching, summarization, discrepancy detection, draft preparation, and review support. It must not bypass operator approval, retained evidence, ledger controls, period locks, segregation-of-duties rules, report release checks, or payment controls.

## Architectural Implication

Every new feature should strengthen at least one operational-record capability: data confidence, retained evidence, reconciliation, approvals, accounting records, multi-asset operational coverage, or governed reporting.

The default implementation shape is shared-first: domain rules and read models belong in shared contracts, services, and endpoint seams that can support both `src/Meridian.Wpf/` and `src/Meridian.Ui/dashboard/` without creating divergent business state.
