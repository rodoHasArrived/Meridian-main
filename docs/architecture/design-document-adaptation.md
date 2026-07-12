# Design Document Adaptation

**Status:** Active
**Owner:** Core Team
**Reviewed:** 2026-06-04

This document is the executable adaptation contract for the
[Meridian Design Document](../product/meridian-design-document.md). It is broader than the physical
module list: it covers roadmap/status evidence, bounded contexts, recommended modules, root
workspaces, MVP screen inventory, expansion lanes, shared UI surfaces, and the modular-monolith rule.

The machine-readable contract is [`design-document-adaptation.yml`](design-document-adaptation.yml).
Validate it with:

```bash
python build/scripts/docs/validate-design-document-adaptation.py --summary
```

## Adaptation Scope

Meridian adapts the design document through these enforceable surfaces:

| Design-doc area | Project adaptation |
| --- | --- |
| W1-W5 product baseline | The active scope remains the operational record baseline: data confidence, reconciliation, approvals, accounting records, retained evidence, multi-asset operational coverage, and governed reports. |
| Root operator navigation | Browser and WPF surfaces expose `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings` as the root workspaces. |
| Compatibility groupings | `Research`, `Data Operations`, and `Governance` remain aliases or internal groupings, not new root workspaces. |
| Recommended modules | The twelve design modules exist physically under `src/` and expose bounded-context descriptors. |
| Bounded contexts | MVP and later contexts map to physical design modules as current source, roadmap, and user direction support them. |
| MVP screen inventory | The design-doc screen inventory maps to current browser evidence paths and retained WPF compatibility evidence. |
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

## Implementation Rule

The design document recommends a modular monolith with strict bounded-context boundaries. In the
current repository that means:

1. New work names the owning design module first.
2. Shared contracts and read models remain in published seams until a bounded context can own them
   without breaking browser, WPF, host, and test consumers.
3. Modules may read another module through published APIs, views, or events.
4. Modules must not directly write another module's owned records.
5. Expansion contexts stay explicitly deferred unless they strengthen the W1-W5 operational record
   workflow.

## Validation Contract

The adaptation validator checks that:

- the design document exists and still mentions the expected modules, contexts, root workspaces, and
  screen inventory;
- every recommended module has a physical project and source registry entry;
- every module descriptor exposes its bounded context and required facets;
- MVP and later bounded contexts are classified consistently with the design document;
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
