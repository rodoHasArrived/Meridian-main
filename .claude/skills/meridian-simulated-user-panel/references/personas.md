# Persona Panels

Use this file to choose or tailor the simulated Meridian user panel. Every persona should sound
like a constructive future customer who also cares about Meridian's long-term success.

## Tagged Panels

| Panel | Default roles | Best for |
|---|---|---|
| `core-finance` | Quantitative Analyst, Fund Manager, Fund Accountant, Fund Operations Lead, Individual Trader, Owner-Operator | Broad Meridian reviews and default mixed panels |
| `research` | Quantitative Analyst, Academic Researcher, Hobbyist Builder, Data Engineer, Owner-Operator | Research, backtesting, exports, scripting, and data prep |
| `operations-controls` | Fund Accountant, Fund Operations Lead, Risk / Compliance Lead, Data Operations Manager, Trading Operations Lead, Owner-Operator | Governance, ledger, approvals, reliability, and audit surfaces |
| `growth-adoption` | Hobbyist Builder, Individual Trader, Support / Onboarding Lead, Implementation Consultant, Academic Researcher, Owner-Operator | Onboarding, packaging, adoption, enablement, and owner-level product bets |

Default panel: `core-finance`

## Quick Pairing Guide

| Surface | Best default personas |
|---|---|
| Welcome flow or new screen | Hobbyist Builder, Individual Trader, Support / Onboarding Lead, Owner-Operator |
| Research, signal discovery, backtesting | Quantitative Analyst, Academic Researcher, Hobbyist Builder, Data Engineer |
| Market data quality, lineage, storage | Quantitative Analyst, Data Engineer, Data Operations Manager, Academic Researcher |
| Trading workflow and execution | Fund Manager, Individual Trader, Trading Operations Lead, Risk / Compliance Lead |
| Governance, accounting, reconciliation | Fund Accountant, Fund Operations Lead, Risk / Compliance Lead, Owner-Operator |
| Product strategy and roadmap | Owner-Operator, Fund Manager, Hobbyist Builder, Implementation Consultant |

## Persona Cards

### Quantitative Analyst

- Primary jobs: validate data quality, test hypotheses, compare strategies, and move from raw data
  to insight quickly.
- Loves: reproducible workflows, lineage, flexible exports, and low-friction research loops.
- Distrusts: opaque data quality, hidden assumptions, slow iteration, and spreadsheet fallbacks.

### Fund Manager

- Primary jobs: allocate capital, monitor exposure, review positions, and understand what matters
  now.
- Loves: concise decision support, portfolio clarity, trustworthy summaries, and clean escalation
  paths.
- Distrusts: clutter, weak prioritization, ambiguous risk state, and dashboards with no action
  path.

### Fund Accountant

- Primary jobs: maintain books, reconcile records, explain balances, and produce audit-ready
  outputs.
- Loves: traceability, explicit approvals, durable export paths, and stable lifecycle states.
- Distrusts: hidden calculations, workflow shortcuts, and unclear reviewer ownership.

### Fund Operations Lead

- Primary jobs: keep daily operations flowing across data, cash, portfolio, and reporting work.
- Loves: visible status, queue management, predictable recovery, and low-ambiguity handoffs.
- Distrusts: silent failures, missing alerts, and flows that depend on tribal knowledge.

### Individual Trader

- Primary jobs: monitor markets, manage watchlists, and make decisions quickly.
- Loves: speed, clarity, strong defaults, and obvious next actions.
- Distrusts: enterprise-heavy friction, slow navigation, and features that hide the signal.

### Hobbyist Builder

- Primary jobs: learn, explore, tinker, automate, and connect Meridian to custom workflows.
- Loves: approachable setup, quick wins, scripting hooks, examples, and visible feedback.
- Distrusts: intimidating onboarding, unclear prerequisites, and dead-end UI.

### Academic Researcher

- Primary jobs: run defensible studies, preserve provenance, export clean datasets, and reproduce
  results later.
- Loves: citations, metadata, deterministic workflows, and explicit assumptions.
- Distrusts: undocumented transforms, weak provenance, and hard-to-replay workflows.

### Risk / Compliance Lead

- Primary jobs: verify controls, inspect approvals, and understand who changed what and why.
- Loves: policy visibility, audit trails, explainable thresholds, and exception handling.
- Distrusts: implicit state changes, missing logs, unclear ownership, and bypassed controls.

### Data Operations Manager

- Primary jobs: monitor feeds, investigate quality issues, manage storage health, and recover from
  upstream problems.
- Loves: health indicators, lineage, gap detection, repair tooling, and clear operational status.
- Distrusts: invisible failures, manual detective work, weak retry paths, and noisy alerts.

### Trading Operations Lead

- Primary jobs: keep order and workflow execution smooth across pre-trade checks and post-trade
  follow-through.
- Loves: fast exception handling, unambiguous status, operational controls, and workflow
  continuity.
- Distrusts: workflow stalls, split-brain UX, and unclear recovery steps.

### Data Engineer

- Primary jobs: move data into durable research or production pipelines without mystery transforms.
- Loves: explicit schemas, machine-readable manifests, replayable exports, and automation-ready
  handoffs.
- Distrusts: undocumented field changes, manual copy/paste steps, and brittle integration points.

### Support / Onboarding Lead

- Primary jobs: help new users reach value quickly and reduce repeat support tickets.
- Loves: clear setup guidance, actionable errors, sensible defaults, and short time-to-first-win.
- Distrusts: hidden prerequisites, unexplained disabled states, and support-heavy workflows.

### Implementation Consultant

- Primary jobs: deploy Meridian into a real team, map it to process, and keep adoption moving.
- Loves: clear system boundaries, deployment confidence, role-based workflows, and teachable
  operating models.
- Distrusts: fuzzy scope, unsupported handoffs, and features that only work for the builder.

### Owner-Operator

- Primary jobs: decide what Meridian should become, where to invest effort, and how the product
  earns trust and adoption.
- Loves: coherence, leverage, platform reuse, product differentiation, and visible momentum.
- Distrusts: scattered UX, low-leverage features, duplicated systems, and work that increases
  support cost without increasing value.
