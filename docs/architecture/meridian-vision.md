# Meridian Vision

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-30

## What Meridian Is

Meridian is a modular, configurable, self-hosted platform for financial operations, strategy
formulation, order management, and governed strategy execution. Operators should be able to develop
and validate investment strategies, manage orders and broker-confirmed executions, and reconcile
their economic effects through portfolio records, accounting, close, reporting, and retained
evidence.

The shared model starts with organization, entity, portfolio, account, book, period, operational
event, strategy definition and run, order, execution, evidence, approval, journal, report, and audit
trail. Fund management, investor capital, and
private-capital operations are first-class specializations of that model. Ordinary accounting,
portfolio, and reporting workflows must work without requiring a fund.

This vision summarizes the [design charter](../product/meridian-design-document.md) and
[domain model](meridian-domain-model.md). The requirements below express product intent and
architectural constraints; their inclusion does not establish implemented coverage, operator
acceptance, or production readiness.

## Product Promise: Decisions, Execution, and Numbers With Proof

The product promise connects a retained strategy or order decision to its observed outcome and
connects a financial number to its source, calculation, accounting treatment, reconciliation,
approval, and downstream use. Operators
and reviewers should be able to answer three questions: what changed, who approved it, and why an
affected operation or output remains blocked.

The operational proof chain connects retained source evidence, normalized and validated records,
reconciliation and exception resolution, reviewed posting candidates, immutable journals, close
packages, report lines, delivery records, and audit support. Capital-account impact joins that
chain where the workflow is fund-specific. Strategy and order workflows add the retained
definition, run inputs, validation results, promotion decision, scoped order intent, risk verdict,
broker observations, and fill-to-accounting handoff where those stages apply.

The **Number Passport** is the shared, read-only proof view for a financial subject. It binds
stable record and version identifiers to authenticated owner scope, book, basis, amount and
currency or unit, effective and as-of dates, calculation versions, reconciliation, approval, and
retained evidence. Corrections go through the owning workflow; the passport shows their lineage.
Broader passport coverage is registered direction, with implementation and acceptance recorded in
the [roadmap registry](../roadmap/README.md).

Current evidence assessment remains separate from accounting lifecycle and released history.
A posted journal stays posted when its supporting evidence becomes stale or requires review.
A released package retains the figures, source versions, approval, and proof assessment used at
release; later corrections create linked amendments or restatements. Missing, ambiguous,
restricted, unsupported, or stale support must be visible without exposing unauthorized evidence.

## Three Connected Operator Outcomes

| Outcome | Required operator result |
| --- | --- |
| Formulate and validate a strategy | Retain the strategy version, rules, universe, parameters, data dependencies, assumptions, results, and review decisions; distinguish backtest, paper, and live modes. |
| Manage orders and execute strategies | Review discretionary or strategy-originated intent, apply account authority and risk controls, submit and monitor orders, govern amendments and cancellations, and recover broker-confirmed outcomes. |
| Reconcile, close, and report | Connect independent source and execution evidence to portfolio and accounting effects, resolve exceptions, approve results, and release reproducible reports. |

Order management and strategy formulation/execution are explicit product objectives under
`DEC-TRADING-STRATEGY-DIRECTION-001`, with planned delivery and acceptance tracked by `W13-OMS-001`
and `W13-STRAT-001`. They share the operational evidence model with financial operations.
Accounting workflows need no strategy origin; a research experiment need not post a journal.
The existing close package does not gate all strategy or order-management development.

The first OMS slice must declare its broker/account, instrument, order-type, and mode support.
A manual order requires an explicit governed intent and account-authority contract; it must not
fabricate a strategy run to satisfy today's strategy-linked live-admission gate. A strategy slice
must retain one supported formulation-to-result-to-promotion-to-execution path. Both build on the
existing OMS, strategy lifecycle, risk, and gateway services and require their own operator evidence.
See [strategy, orders, and execution](../domain/strategy-order-execution.md) for shared concepts and
[implementation follow-up](../product/implementation-todo-list.md#expanded-strategy-and-order-management-objectives)
for concrete work.

### First Trusted Close

The financial-operations package begins with a bounded close-support workflow for one entity, one supported book
and functional currency, and one declared period on durable storage. The
[delivery and acceptance plan](../product/meridian-design-document.md#226-delivery-acceptance-and-rollout)
defines its dependencies and financial gates:

1. Declare owner scope, official or shadow-book authority, period, policies, source cutoffs, and
   approval roles.
2. Retain and validate source activity, mappings, opening balances, and the economic history needed
   by the supported workflow. A tied opening trial balance alone does not prove opening-state
   completeness.
3. Reconcile explicitly identified source and accounting populations, investigate a controlled
   exception, and retain its disposition and supporting evidence.
4. Review the proposed accounting result, apply the governing approval and period controls, and
   retain the posted outcome and durable receipt.
5. Produce a tied trial balance, balance sheet, income statement, and cash bridge with report-line
   evidence through an existing supported renderer and governed release path.

Changed sources, rules, or mappings must invalidate affected pending review. Reopening the original
retained package must reproduce its original figures. An incomplete package exposes its blockers;
it cannot be presented as a completed close.

This slice proves a declared configuration. Additional books, bases, currencies, investor methods,
annual-close treatments, and loan conventions each require their own supported contracts and
acceptance evidence.

## Core Modules

The module map describes responsibilities and intended continuity. It is not a claim that every
listed workflow or financial convention is delivered.

| Capability | Responsibility |
| --- | --- |
| Financial Operations and General Ledger | Entity-owned books, reviewed posting candidates, balanced immutable journals, period controls, corrections, and close workflows. |
| Portfolio and Asset Operations | Holdings, lots, positions, valuations, obligations, and economic events connected to their accounting and evidence context. |
| Reference Data and Security Master | Canonical security identity, identifiers, classification, approved reference evidence, and governed economic terms. |
| Reconciliation Studio | Source comparisons, expected-versus-actual checks, exception casework, explanations, review, and close or release blockers. |
| Evidence Vault and Audit Studio | Retained sources, immutable manifests, evidence requests, audit trails, and reconstructable support packages. |
| Reporting Studio and Delivery | Approved datasets, report-line provenance, controlled financial statements, retained packages, recipient authority, and amendment history. |
| Data and Provider Operations | Governed connections, acquisition, normalization, validation, source freshness, lineage, and recovery. |
| Strategy Formulation and Validation | Versioned definitions, supported Designer/QuantScript/template workflows, reproducible research and backtests, paper evaluation, promotion decisions, and retained lifecycle evidence. |
| Order Management and Execution | Discretionary and strategy-originated order intent, scoped risk and admission, routing, broker-confirmed lifecycle, amendments, cancellations, recovery, operator controls, and financial handoff. |
| Fund Accounting and Capital Accounts | Fund-specific participation, contributions, allocations, fees, distributions, investor statements, and capital-to-ledger reconciliation. |

The shared core should support assets, liabilities, commitments, and other already-modeled
obligations through explicit financial contracts. The contract-pack direction extends schemas,
lifecycle events, valuation, accounting, validation, and reporting rules while retaining the
versions used by existing records.

Registered depth includes books, accounting bases and FX; investor capital and allocations;
fiscal close; financial statement controls; and the existing DirectLoan operations path. These
directions deepen the common model. Their sequencing and completion remain registry decisions.

## Architectural Implication

Every new feature should strengthen strategy formulation and validation, order lifecycle integrity,
governed execution, data confidence, retained evidence, reconciliation, approvals, accounting
records, multi-asset operational coverage, close support, or governed reporting.

The implementation follows the [modular operational monolith](../adr/017-modular-operational-monolith.md)
and [module boundaries](module-map.md). Security Master owns canonical security identity;
Asset Operations owns economic interpretation and projections; Financial Operations owns candidate
generation, book context, and review; Ledger and Storage own durable accounting truth. Evidence
graphs, search, analytics, and UI read models compose those records without acquiring posting or
approval authority.

Strategy services own definitions, run evidence, and promotion/lifecycle decisions. Execution
services own order admission, risk/control coordination, routing, and retained broker outcomes.
Both workstations use the same OMS and gateway seams. An order request, pending replacement, or
cancellation request cannot be represented as a confirmed broker outcome; strategy approval alone
does not authorize an account for live trading. Execution evidence reaches the existing governed
financial handoff without granting the OMS authority to mutate posted journals.

### Financial Authority and Integrity

- **Explicit book ownership.** Each book belongs to an entity and declares its basis, currency,
  calendar, policies, and periods. Financial accounts, ledger accounts, books, and capital accounts
  keep distinct identities. Consolidated views retain their contributing books and perimeter.
- **Declared adoption authority.** During shadow adoption, the customer's designated external
  books remain official. Meridian owns its parallel records and posted journals. Cutover requires
  an authorized decision and reconciled opening-state evidence; imports and comparison reports
  cannot change official-book designation.
- **Immutable accounting.** Posted journals are authoritative within their designated book and
  remain balanced and immutable. Corrections follow governed reversal and rebook or adjustment
  workflows, preserving original records and correction lineage.
- **Current approval and scope.** Financial actions validate authenticated tenant and owner scope,
  book, basis, effective and posting dates, period posture, retained source versions, and the
  authority of the approval. Stale review or foreign scope must not authorize a write.
- **Durable, repeatable outcomes.** Concurrent commands, retries, and restarts must preserve
  idempotency and recover retained results without duplicate financial effects. Unsupported
  persistence fails closed in supported production profiles; uncertain outcomes require resolution
  before retry.
- **Truthful evidence.** Missing required inputs and unsupported conventions block dependent
  operations. Simulated, sample, synthetic, and fixture-derived values are visibly labeled;
  estimated and backfilled inputs retain their provenance wherever consumed. A successful
  technical operation cannot silently establish reconciliation or approval.

These rules are detailed in the [domain model](meridian-domain-model.md),
[accounting context](../ai/context/accounting-context.md), and
[operational evidence context](../ai/context/operational-evidence-context.md).

### Co-Equal Browser and Desktop Workstations

The browser workstation in `src/Meridian.Ui/dashboard/` and the WPF workstation in
`src/Meridian.Wpf/` are active, co-equal operator surfaces. They consume shared domain services,
contracts, authorization, endpoint read models, calculations, identifiers, evidence links, and
workflow states. Presentation can differ; financial results and authority must agree.

Shared API and read-model support belongs in `src/Meridian.Ui.Services/` and
`src/Meridian.Ui.Shared/`. Keep visible root navigation to `Trading`, `Portfolio`, `Accounting`,
`Reporting`, `Strategy`, `Data`, and `Settings`. A new capability extends these workspaces through
the owning shared service before creating presentation-specific logic.

## What Meridian Is Not

Meridian should not drift into being primarily:

- a CRM, full cap-table platform, or outsourced services operation;
- a payment processor or live bill-pay operation;
- a broad self-service investor portal;
- a generic no-code workflow builder;
- an enterprise-risk or forecasting suite detached from operational records.

There is no mobile development lane. Responsive browser validation supports the browser
workstation; native mobile clients and mobile-first workflows require a separate explicit roadmap
decision. Other expansion gates remain in the
[deferred expansion boundaries](../product/deferred-expansion-boundaries.md).

AI assistance may extract, classify, match, explain, summarize, identify discrepancies, and prepare
drafts with evidence links. It must not approve its own work or downstream workflows depending on
its suggestions; replace deterministic accounting, tax, or performance calculations; post material
journals; alter posted history; override period locks; release payments or governed reports;
change ownership; or erase evidence. Deterministic straight-through execution requires a
human-approved, versioned policy, bounded eligibility and materiality, retained audit evidence,
sampling review, and a kill switch; material or high-risk actions retain per-item approval.

## Scope and Acceptance

Activate and prove existing capabilities through real operator workflows as the expanded
strategy and order-management objectives are delivered in bounded slices.
The earlier W1-W5 baseline establishes operational-record invariants and sequencing evidence;
current charter, roadmap, source evidence, and explicit user direction govern expansion. Earlier
baselines are not development ceilings.

Use the source that owns the question:

| Question | Authority |
| --- | --- |
| Product intent, invariants, and candidate design | [Design charter](../product/meridian-design-document.md). |
| Adopted scope, sequencing, delivery state, and capability acceptance | [Roadmap registry](../roadmap/README.md), whose canonical data is `docs/roadmap/data/*.yml`; generated views summarize it. |
| Implemented behavior and physical ownership | Owning source, tests, and [source documentation mesh](../source/README.md). |
| Production-release blockers and required evidence | [Implementation and Readiness Tracker](../product/implementation-todo-list.md). |

Development follows the [solo-developer workflow](../ai/solo-developer-workflow.md): use
deterministic synthetic data, focused checks, and separate AI critique where the changed risk
warrants it. Routine development does not require live accounts, institutional operators, or
production certification. AI critique is not independent human approval.

A supported operator slice states its data and operating scope, exposes truthful blockers,
retains reproducible results, and proves its applicable durability and recovery requirements.
Synthetic verification, live-provider verification, product acceptance, and release certification
remain separate claims. The developer owns merge and activation decisions; required product
approvals and GitHub rules remain in force.

Production-release claims additionally require packaging, operator preflight, and the applicable
hosted checks and retained release evidence on one frozen commit. The W13 decision adopts product
direction only; it does not change existing delivery ranks, certify a capability or release, or
authorize live broker activity.
