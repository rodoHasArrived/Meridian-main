# Product Documentation

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-05-30

This is the canonical stakeholder-facing entrypoint for Meridian product direction, capability status, and roadmap interpretation. It replaces the old pattern of treating multiple roadmap, status, plan, audit, and evaluation files as competing front doors.

## Product Promise

Meridian is a self-hosted evidence-backed investment operations platform.

The central product question is:

> Can Meridian prove, book, reconcile, approve, and report an investment decision?

That framing is intentionally narrower than a generic trading dashboard or broad front-to-back suite. Meridian should connect trusted data, research, paper validation, books, reconciliation, approvals, and governed reporting into one explainable chain.

## Product Design Foundation

The detailed design charter now lives in this canonical document and is the stakeholder-facing design source for this rebuild wave:

- Target users: financial operations professionals, fund admins, RIAs, family offices, and related governance roles.
- Core loop: `Import -> Validate -> Reconcile -> Investigate -> Approve -> Report`.
- Primary operating model: configurable financial operations platform with one tenant-aware system, not separate products per customer type.
- Core domains: data integration, treasury/payments, portfolio/investment operations, financial operations, reference data, instrument obligations, entity/relationships, alternative assets, financing, and planning/forecasting.
- Delivery posture: preserve the closed W1-W4 evidence baselines, then advance W5 Backtest Studio and W6 live-readiness prerequisites through shared contracts, strong auditability, configurable workflows, and approval controls.

## Canonical Product Design Artifact

- [Meridian Design Document (Draft v1.0)](meridian-design-document.md) is the single canonical hand-authored product vision and capability source.
- Current wave interpretation must not override this charter; deviations must be justified in roadmap or status evidence.

## Operator Lifecycle

The release-level story is the Meridian Assurance Loop:

```text
Data Trust Passport
-> Run Evidence Graph
-> Promotion Passport
-> Accounting-Grade Paper Trading
-> Governed Report Pack
```

The practical operator workflow is:

1. `Data` establishes provider trust, symbol/reference readiness, and evidence-backed data quality.
2. `Strategy` turns trusted data into reviewed runs, comparisons, and promotion evidence.
3. `Trading` validates approved decisions in paper-first workflows before live risk.
4. `Portfolio` reviews positions, account posture, brokerage sync evidence, and run impact.
5. `Accounting` reviews ledger, cash-flow, reconciliation, casework, close, and sign-off evidence.
6. `Reporting` packages governed outputs with retained provenance.
7. `Settings` keeps credentials, capabilities, storage, and environment posture reproducible.

Visible root operator navigation remains limited to `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`. Legacy `Research`, `Data Operations`, and `Governance` names may appear as compatibility groupings, not as new root navigation.

## Current Roadmap Posture

Durable roadmap truth belongs in `docs/roadmap/data/*.yml` and generated views under `docs/roadmap/generated/`. This page summarizes the stakeholder interpretation but does not replace the registry.

| Wave | Current posture | Stakeholder meaning |
| --- | --- | --- |
| W1 Provider confidence and checkpoint evidence | Done | Provider trust and DK1-style evidence are closed baselines that must stay synchronized when provider evidence changes. |
| W2 Paper-trading cockpit | Done | Paper cockpit acceptance is a closed baseline; future work should preserve evidence rather than reopen the wave. |
| W3 Shared run / portfolio / ledger continuity | Done | Shared run, portfolio, ledger, brokerage/account, reconciliation, and evidence continuity are closed baselines. |
| W4 Governance and fund operations | Done | Close/case/report/evidence acceptance is a closed baseline as of the current W4 packet; future work is maintenance unless explicitly scoped otherwise. |
| W5 Backtest Studio unification | Planned | Next planned product wave; do not pull live-readiness scope forward under W5 without a roadmap change. |
| W6 Live integration readiness | Planned | Later controlled-live-readiness wave; read-only and paper-first defaults remain. |
| Optional advanced tracks | Optional | L3, scale, advanced research, and performance tracks should not displace the core operator path. |

## Capability Map

| Area | Current posture | Canonical detail |
| --- | --- | --- |
| Product design foundation | Active design source | [Meridian Design Document (Draft v1.0)](meridian-design-document.md) |
| Provider confidence and Data Trust Passport | Active W1 baseline plus ongoing provider validation, degradation, and capability governance. | [Reference](../reference/README.md), [Providers](../providers/README.md), [Provider Validation Matrix](../status/provider-validation-matrix.md) |
| Research, strategy, and run evidence | Active support through shared strategy/run contracts; Backtest Studio remains W5. | [Current Direction and Status](../plans/current-direction-and-status.md), [Feature Inventory](../status/FEATURE_INVENTORY.md) |
| Trading and paper validation | W2 baseline is closed; live readiness remains W6 and must stay read-only/paper-first until accepted. | [Project Roadmap](../status/ROADMAP.md), [Engineering](../engineering/README.md) |
| Portfolio, brokerage, ledger continuity | W3 baseline is closed; future changes must preserve shared read-model and acceptance evidence. | [Target End Product](../status/TARGET_END_PRODUCT.md), [Feature Inventory](../status/FEATURE_INVENTORY.md) |
| Accounting, reconciliation, and close | W4 baseline is closed; new close/report work should be maintenance, hardening, or explicitly new-wave scope. | [Governance Fund Ops Blueprint](../plans/governance-fund-ops-blueprint.md) |
| Reporting, evidence, and governed outputs | Report-pack and evidence-workbench support are active; broad Evidence OS language remains positioning unless backed by current evidence. | [Evidence-Backed Investment Operations Plan](../plans/evidence-backed-investment-operations-plan.md) |
| Operator UI | Browser workstation and WPF desktop are both active; shared contracts/read models should carry behavior before either surface composes it. | [Engineering](../engineering/README.md), [Operators](../operators/README.md) |

## Product Status Language

Use conservative language:

- `Done` means the relevant registry/status source and acceptance evidence agree.
- `Closed baseline` means future changes must preserve the accepted evidence, not reopen the wave casually.
- `Support evidence` means a route, DTO, fixture, API, page, or test exists, but the product capability may still require broader acceptance.
- `Planned` means do not describe the capability as delivered.
- `Positioning` means commercial grouping language only until shared contracts, workflows, evidence retention, and governed outputs are accepted.

Do not treat a support slice, route, DTO, WPF page, browser panel, or generated fixture as a wave exit unless the matching acceptance evidence is named.

## Market Positioning Rule

Historical competitive analyses are source material, not current positioning. Before using competitor comparisons, business-model claims, or gap rankings in active product docs, revalidate them against current Meridian evidence and the roadmap registry. The stable product framing is Meridian as a self-hosted evidence-backed investment operations platform; market comparisons should clarify that framing, not replace it.

## Data Quality Claim Rule

Data-quality claims must point to current provider validation evidence, quality reports, or generated status artifacts. Historical data-quality evaluations can explain the dimensions to inspect, such as completeness, gaps, sequence integrity, anomaly detection, latency, freshness, and cross-provider comparison, but they are not proof that the current checkout or deployment satisfies those dimensions.

## Roadmap And Status Sources

Use these sources in order:

1. [Roadmap Registry](../roadmap/README.md)
2. [Generated Roadmap Summary](../roadmap/generated/ROADMAP_SUMMARY.md)
3. [Current Direction and Status](../plans/current-direction-and-status.md)
4. [Project Roadmap](../status/ROADMAP.md)
5. [Program State](../status/PROGRAM_STATE.md)
6. [Feature Inventory](../status/FEATURE_INVENTORY.md)
7. [Target End Product](../status/TARGET_END_PRODUCT.md)

Hand-authored status summaries should not invent a separate source of truth. When registry output and old prose disagree, fix the registry/generator path or explicitly demote the old prose as source material.

## Out Of Scope

Meridian has no active mobile development lane. Do not create native iOS/Android clients, MAUI clients, React Native clients, Flutter clients, or mobile-first workflows. Responsive browser validation is allowed only for the browser workstation.

Live-trading claims remain out of scope until W6 acceptance exists. Read-only, paper-first, and fail-closed controls should remain the default posture.

## Canonical Design Artifact

- [Meridian Design Document (Draft v1.0)](meridian-design-document.md)

## Legacy Source Material

The pages below remain source material during migration. New stakeholder-facing links should prefer this page unless the detailed source is required.

- [Current Direction and Status](../plans/current-direction-and-status.md)
- [Evidence-Backed Investment Operations Plan](../plans/evidence-backed-investment-operations-plan.md)
- [Target End Product](../status/TARGET_END_PRODUCT.md)
- [Feature Inventory](../status/FEATURE_INVENTORY.md)
- [Project Roadmap](../status/ROADMAP.md)
- [Meridian Design Document (Draft v1.0)](meridian-design-document.md)
