# Design Document Adaptation

**Status:** active
**Owner:** Core Team
**Reviewed:** 2026-10-30

This document is the executable adaptation contract for the
[Meridian Design Document, Version 1.6](../product/meridian-design-document.md). It is broader than the physical
module list: it covers roadmap/status evidence, bounded contexts, recommended modules, root
workspaces, MVP screen inventory, expansion lanes, shared UI surfaces, and the modular-monolith rule.

The machine-readable contract is [`design-document-adaptation.yml`](design-document-adaptation.yml).
Validate it with:

```bash
python build/scripts/docs/validate-design-document-adaptation.py --summary
python -m unittest tests/scripts/test_validate_design_document_adaptation.py
```

## Adaptation Scope

Meridian adapts the design document through these enforceable surfaces:

| Design-doc area | Project adaptation |
| --- | --- |
| W1-W5 financial foundation | The bounded operational-record baseline retains data confidence, reconciliation, approvals, accounting records, retained evidence, multi-asset operational coverage, and governed reports. Its financial-package sequence does not rank or defer strategy or OMS development. |
| Root operator navigation | Browser and WPF surfaces expose `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings` as the root workspaces. |
| Compatibility groupings | `Research`, `Data Operations`, and `Governance` remain aliases or internal groupings, not new root workspaces. |
| Recommended modules | The twelve design modules exist physically under `src/` and expose bounded-context descriptors. |
| Bounded contexts | Financial MVP and deferred expansion contexts retain their mapped design modules. Adopted strategy and OMS direction maps to existing runtime projects, with planned W13 outcomes and separate acceptance. |
| Research & Analytics | Section 22.7 and `W13-STRAT-001` adopt strategy formulation, validation, promotion, and execution as planned product direction over existing strategy, backtesting, SDK, and QuantScript projects. |
| Trading & Execution | Section 22.7 and `W13-OMS-001` adopt governed order management over the existing execution, SDK, risk, strategy, and backtesting seams. `W11-EXEC-001` continues to track ownership enforcement; this mapping does not complete it. |
| MVP screen inventory | The design-doc screen inventory maps to browser and active WPF workstation evidence paths. |
| Module facets | Each design module declares the required `Domain model`, `Application services`, `Contracts / APIs`, `Infrastructure`, `UI components`, and `Tests` facets. |
| Shared UI direction | Browser and WPF are two active co-equal shared-first operator surface lanes over `Ui.Shared` and `Ui.Services`; WPF's current focus is web-UI parity (`W8-WPF-PARITY-001`). |
| No mobile lane | Mobile applications and mobile-first workflows remain out of scope. |

## Relationship To Module Conformance

[Design Module Conformance](design-module-conformance.md) proves the physical module structure:

```text
Meridian.Platform
Meridian.Identity
Meridian.Entities
Meridian.DataIntegration
Meridian.ReferenceData
Meridian.Instruments
Meridian.PortfolioRecords
Meridian.FinancialOperations
Meridian.Workflow
Meridian.Audit
Meridian.Reporting
Meridian.Documents
```

This document proves the larger design-doc adaptation contract around that structure. The two
validators should be run together when architecture, source registry, module, or operator workspace
scope changes.

The twelve-module target is a bounded physical-conformance inventory. Research & Analytics uses
`src/Meridian.Strategies/`, `src/Meridian.Backtesting/`, `src/Meridian.Backtesting.Sdk/`, and
`src/Meridian.QuantScript/`. Trading & Execution uses `src/Meridian.Execution/`,
`src/Meridian.Execution.Sdk/`, `src/Meridian.Risk/` for pre-trade enforcement, and the existing
strategy/backtesting seams. These shared runtime projects retain their distinct record and policy
responsibilities; listing them does not transfer accounting authority or create a new module.

The [scope-adoption decision](../roadmap/data/decision-log.yml) records
`DEC-TRADING-STRATEGY-DIRECTION-001`. Both W13 outcomes remain `planned` with `planned_evidence`
in the [roadmap registry](../roadmap/data/roadmap-items.yml). Existing W2/W3/W6/W7 and accepted
W9 evidence keeps its original scope. The financial MVP is not a development ceiling, and a
planned strategy or OMS outcome is not an enabled live capability. Supported account/intent
authority, broker readiness, independent review, and release certification retain their own gates.

## Implementation Rule

The design document recommends a modular monolith with strict bounded-context boundaries. In the
current repository that means:

1. New work names its bounded context, then selects its mapped design module or existing runtime
   project; do not invent a physical module to stand in for planned ownership work.
2. Shared contracts and read models remain in published seams until a bounded context can own them
   without breaking browser, WPF, host, and test consumers.
3. Modules may read another module through published APIs, views, or events.
4. Modules must not directly write another module's owned records.
5. Evaluate expansion against the current design charter, roadmap evidence, source, and explicit
   user direction. Preserve operational-record invariants without treating the W1-W5 baseline as
   a development ceiling.

## Validation Contract

The adaptation validator checks that:

- the design document exists and still mentions the expected modules, contexts, root workspaces, and
  screen inventory;
- the adaptation names Version 1.6 and its Section 22.7 direction;
- every recommended module has a physical project and source registry entry;
- every module descriptor exposes its bounded context and required facets;
- the financial baseline remains bounded, the two adopted W13 contexts remain planned with exact
  roadmap/decision references and existing runtime owners, and unrelated deferred contexts stay
  deferred;
- the browser and WPF workspace catalogs preserve the seven root workspaces;
- no mobile development lane is enabled in the adaptation contract;
- every screen-inventory evidence path exists.

This gives the project a repeatable answer to "does `src/` still conform to the design document?"
rather than relying on a one-time manual audit.

## Related Docs

- [Design Module Conformance](design-module-conformance.md)
- [Module Map](module-map.md)
- [Project Structure](project-structure.md)
- [Domain Boundaries](domains.md)
- [Meridian Design Document](../product/meridian-design-document.md)
