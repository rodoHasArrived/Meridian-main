# Persona Panels

Choose primary panel members from the canonical Persona Matrix in
`docs/product/meridian-design-document.md`. Advisory lenses can enrich a panel, but they are not
presented as Meridian customer research.

## Contents

- [Panel presets](#panel-presets)
- [Selection rules](#selection-rules)
- [Canonical personas](#canonical-personas)
- [Advisory lenses](#advisory-lenses)
- [Legacy-name handling](#legacy-name-handling)

## Panel Presets

| Panel | Default canonical roles | Best for |
| --- | --- | --- |
| `core-finance` | Financial Operations Professional, Investment Accountant, Fund Accountant, Operations Manager, Portfolio Manager, CFO | Broad Meridian product reviews |
| `research` | Investment Analyst, Quantitative Researcher, Portfolio Manager, CIO | Research, strategy, backtesting, and analytical workflows |
| `operations-controls` | Financial Operations Professional, Reconciliation Analyst, Operations Manager, Controller, Compliance Officer, Auditor | Reconciliation, accounting, approvals, and auditability |
| `growth-adoption` | Reporting Analyst, Fund Investor / LP, RIA Client, Family Beneficiary, Trustee, Portfolio Manager | Stakeholder delivery, comprehension, and adoption |
| `trading-risk` | Portfolio Manager, Trader, Risk Manager, Compliance Officer, CIO | Orders, positions, limits, and escalation |
| `data-operations` | Data Operations Analyst, Integration Administrator, Operations Manager, System Administrator, Security Administrator | Providers, imports, lineage, storage, and recovery |
| `reporting-stakeholders` | Reporting Analyst, Fund Accountant, Controller, CFO, Fund Investor / LP, RIA Client | Reports, packages, evidence, and distribution |
| `admin-security` | System Administrator, Security Administrator, Integration Administrator, Compliance Officer, Operations Manager | Configuration, access, integrations, and platform health |

Default: `core-finance`.

## Selection Rules

- Use at least four distinct personas for a panel.
- Prefer the smallest panel that covers the core operator, approver, downstream consumer, and
  failure/recovery owner for the artifact.
- Honor user-specified roles exactly. Treat an unknown explicit role as `custom`; do not silently
  replace it with a canonical role.
- Use advisory lenses only when they add a pressure point missing from the canonical panel. Label
  them `advisory` in the output.
- For cross-surface reviews, select roles whose jobs actually span both browser and WPF workflows.
- For release gates, include at least one operational role and one governance, administration, or
  downstream consumer role.

## Canonical Personas

| Persona | Category | Core goal | Primary pressure point |
| --- | --- | --- | --- |
| Financial Operations Professional | Primary Operator | Keep financial data accurate, reconciled, and auditable | Can the operator see what changed, why it matters, and what to do next? |
| Investment Accountant | Primary Operator | Produce accurate accounting and reporting support | Are classifications, adjustments, evidence, and exports defensible? |
| Reconciliation Analyst | Primary Operator | Resolve breaks quickly and clearly | Can breaks be matched, assigned, explained, resolved, and escalated? |
| Fund Accountant | Primary Operator | Support NAV, fund reporting, and investor activity | Are positions, capital activity, valuations, and expenses traceable? |
| Operations Manager | Primary Operator / Manager | Monitor operational health and team workload | Are queues, aging, ownership, SLA risk, and recovery visible? |
| Data Operations Analyst | Primary Operator | Keep pipelines and provider feeds healthy | Are imports, reruns, provider states, and quality issues diagnosable? |
| Treasury Operations Specialist | Primary Operator | Manage liquidity and cash movement | Are initiation, approval, evidence, and reconciliation separated safely? |
| Reporting Analyst | Primary Operator | Produce accurate reports and packages | Are approved inputs, templates, runs, evidence, and distribution clear? |
| Portfolio Manager | Investment User | Monitor and manage portfolio outcomes | Are exposures, performance, risk, and next decisions concise? |
| Investment Analyst | Investment User | Research investments and opportunities | Can evidence be compared, challenged, and turned into a defensible memo? |
| Quantitative Researcher | Investment User | Develop and validate strategies | Are data, assumptions, backtests, simulations, and promotion reproducible? |
| Trader | Investment User | Execute or monitor trading activity | Are order state, liquidity, constraints, and recovery unambiguous? |
| Risk Manager | Governance / Investment User | Monitor investment and operational risk | Are limits, concentrations, stress results, and escalations explainable? |
| CFO | Executive | Oversee financial accuracy and liquidity | Are cash, exceptions, approvals, and reporting decision-ready? |
| CIO | Executive | Oversee portfolio strategy and risk | Are performance, allocation, risk, and recommendations coherent? |
| Controller | Governance | Ensure accounting governance and audit readiness | Can journals, reconciliations, sign-offs, and evidence withstand review? |
| Compliance Officer | Governance | Ensure policies and controls are followed | Are approvals, access, policy mapping, and audit trails explicit? |
| Fund Investor / LP | Stakeholder | Monitor performance and capital activity | Are statements, returns, documents, and capital-account changes understandable? |
| RIA Client | Stakeholder | Understand personal portfolio and advisor reports | Are performance, holdings, allocation, and communication approachable? |
| Family Beneficiary | Stakeholder | Understand family assets and distributions | Are summaries, distributions, reports, and documents comprehensible? |
| Trustee | Stakeholder | Exercise fiduciary oversight | Are reports, approvals, distributions, and legal evidence reviewable? |
| Auditor | External / Governance | Verify accuracy and evidence | Can source data, reconciliations, approvals, and audit trails be inspected? |
| System Administrator | Administration | Maintain platform health and access | Are users, logs, integrations, settings, and operational state manageable? |
| Security Administrator | Administration | Protect the platform and manage permissions | Are roles, scopes, policies, grants, revocations, and reviews defensible? |
| Integration Administrator | Administration | Maintain provider and system connections | Are credentials, mappings, tests, failures, and monitoring safe and clear? |

## Advisory Lenses

Use these only as explicitly labeled non-canonical perspectives:

| Lens | Pressure point |
| --- | --- |
| Owner-Operator | Product coherence, leverage, differentiation, and support cost |
| Support / Onboarding Lead | Prerequisites, error explanations, first win, and ticket burden |
| Implementation Consultant | Deployment boundaries, role mapping, adoption, and teachability |
| Data Engineer | Schemas, machine-readable manifests, automation, and brittle handoffs |
| Academic Researcher | Provenance, reproducibility, assumptions, and publication-grade evidence |
| Hobbyist Builder | Approachability, examples, experimentation, and dead-end avoidance |

An advisory lens may be a fifth panel member, but it should not displace the canonical operator or
stakeholder whose job the artifact is intended to support.

## Legacy-Name Handling

Use these mappings only for old presets or fixtures. If the user explicitly supplies a legacy name,
preserve it as a custom role unless they ask for normalization.

| Legacy label | Canonical default |
| --- | --- |
| Quantitative Analyst | Quantitative Researcher |
| Fund Manager | Portfolio Manager |
| Fund Operations Lead | Operations Manager |
| Individual Trader | Trader |
| Data Operations Manager | Data Operations Analyst |
| Risk / Compliance Lead | Risk Manager plus Compliance Officer |
| Trading Operations Lead | Trader plus Operations Manager |
