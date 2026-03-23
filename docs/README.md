# Meridian Documentation

**Version:** 1.7.1
**Last Updated:** 2026-03-22

This index is the main entry point for Meridian documentation. The current documentation set is synchronized around two connected product tracks inside a broader comprehensive fund-management objective:

- a workflow-centric front-office workstation for research, implementation, and trade management
- a governance and fund-operations expansion for middle- and back-office workflows built on Security Master, portfolio, ledger, reconciliation, and UFL asset-package foundations

## Quick Start

- **New users** -> [Getting Started Guide](getting-started/README.md)
- **Developers** -> [Repository Organization Guide](development/repository-organization-guide.md)
- **Trading workstation migration** -> [Trading Workstation Migration Blueprint](plans/trading-workstation-migration-blueprint.md)
- **Governance and fund operations expansion** -> [Governance and Fund Operations Blueprint](plans/governance-fund-ops-blueprint.md)
- **Direct lending target state** -> [UFL Direct Lending Target-State Package V2](plans/ufl-direct-lending-target-state-v2.md)
- **Direct lending execution roadmap** -> [UFL Direct Lending Implementation Roadmap](plans/ufl-direct-lending-implementation-roadmap.md)
- **UFL asset packages** -> [UFL Supported Asset Packages](plans/ufl-supported-assets-index.md)
- **Operators** -> [Operator Runbook](operations/operator-runbook.md)

## Documentation Zones

| Zone | Folders | Audience |
|------|---------|----------|
| Product | `getting-started/`, `providers/`, `operations/` | Users and operators |
| Engineering | `architecture/`, `adr/`, `development/`, `integrations/`, `reference/`, `diagrams/` | Developers |
| Governance | `status/`, `plans/`, `evaluations/`, `audits/`, `security/` | Core team and stakeholders |

`generated/` contains auto-generated files and should not be edited manually.

## By Audience

### Users and operators

- [Getting Started](getting-started/README.md)
- [Help and FAQ](HELP.md)
- [Provider Setup Guides](providers/README.md)
- [Backfill Guide](providers/backfill-guide.md)
- [Operator Runbook](operations/operator-runbook.md)
- [Deployment Guide](operations/deployment.md)
- [Service Level Objectives](operations/service-level-objectives.md)

### Developers

- [Repository Organization Guide](development/repository-organization-guide.md)
- [Provider Implementation Guide](development/provider-implementation.md)
- [WPF Implementation Notes](development/wpf-implementation-notes.md)
- [Desktop Testing Guide](development/desktop-testing-guide.md)
- [F# Decision Rule](development/fsharp-decision-rule.md)
- [Language Strategy](integrations/language-strategy.md)
- [Documentation Contribution Guide](development/documentation-contribution-guide.md)

### Architecture and design

- [Architecture Overview](architecture/overview.md)
- [Layer Boundaries](architecture/layer-boundaries.md)
- [Desktop Layers](architecture/desktop-layers.md)
- [Storage Design](architecture/storage-design.md)
- [Deterministic Canonicalization](architecture/deterministic-canonicalization.md)
- [Trading Workstation Migration Blueprint](plans/trading-workstation-migration-blueprint.md)
- [Governance and Fund Operations Blueprint](plans/governance-fund-ops-blueprint.md)
- [UFL Supported Asset Packages](plans/ufl-supported-assets-index.md)
- [UFL Direct Lending Target-State Package V2](plans/ufl-direct-lending-target-state-v2.md)
- [UFL Direct Lending Implementation Roadmap](plans/ufl-direct-lending-implementation-roadmap.md)
- [Fund Management Product Vision and Capability Matrix](plans/fund-management-product-vision-and-capability-matrix.md)
- [Fund Management Module Implementation Backlog](plans/fund-management-module-implementation-backlog.md)
- [Fund Management PR-Sequenced Execution Roadmap](plans/fund-management-pr-sequenced-roadmap.md)
- [ADRs](adr/README.md)

### Status and planning

- [Project Roadmap](status/ROADMAP.md)
- [Feature Inventory](status/FEATURE_INVENTORY.md)
- [Improvements Tracker](status/IMPROVEMENTS.md)
- [Production Status](status/production-status.md)
- [Status Docs Index](status/README.md)
- [Evaluations and Audits Summary](status/EVALUATIONS_AND_AUDITS.md)

## Current Planning Source of Truth

These documents are the reconciled, living planning set. Historical or superseded material has moved to the archive so you can trust the links below when making decisions.

Use these documents together when planning implementation:

1. [status/ROADMAP.md](status/ROADMAP.md) for delivery waves and target state
2. [plans/fund-management-product-vision-and-capability-matrix.md](plans/fund-management-product-vision-and-capability-matrix.md) for the formal product vision and phased capability view
3. [plans/fund-management-module-implementation-backlog.md](plans/fund-management-module-implementation-backlog.md) for project-by-project backlog mapping and file anchors
4. [plans/fund-management-pr-sequenced-roadmap.md](plans/fund-management-pr-sequenced-roadmap.md) for dependency-aware PR slices and parallel implementation lanes
5. [plans/trading-workstation-migration-blueprint.md](plans/trading-workstation-migration-blueprint.md) for workstation structure
6. [plans/governance-fund-ops-blueprint.md](plans/governance-fund-ops-blueprint.md) for Security Master, multi-ledger, cash-flow, reconciliation, and reporting
7. [plans/ufl-supported-assets-index.md](plans/ufl-supported-assets-index.md) for the supported UFL asset packages across the current security-master model
8. [plans/ufl-direct-lending-target-state-v2.md](plans/ufl-direct-lending-target-state-v2.md) for the direct-lending specialization of the governance and fund-ops target state
9. [plans/ufl-direct-lending-implementation-roadmap.md](plans/ufl-direct-lending-implementation-roadmap.md) for the dependency-aware path from the current slice to the full direct-lending target state
10. [status/FEATURE_INVENTORY.md](status/FEATURE_INVENTORY.md) for capability status
11. [status/FULL_IMPLEMENTATION_TODO_2026_03_20.md](status/FULL_IMPLEMENTATION_TODO_2026_03_20.md) for the normalized non-assembly implementation backlog
12. [status/IMPROVEMENTS.md](status/IMPROVEMENTS.md) for tracked implementation themes

## Reference

- [API Reference](reference/api-reference.md)
- [Data Dictionary](reference/data-dictionary.md)
- [Environment Variables](reference/environment-variables.md)
- [Dependencies Reference](DEPENDENCIES.md)
- [Generated Documentation](generated/README.md)

## Archive

Historical, deprecated, and superseded documentation now lives outside the active docs tree in [`https://github.com/rodoHasArrived/Meridian/blob/main/archive/docs`](https://github.com/rodoHasArrived/Meridian/blob/main/archive/docs).

- [Archive index](../archive/docs/INDEX.md)
- [Archive overview](../archive/docs/README.md)

## Documentation Maintenance

- [Documentation triage 2026-03-21](status/DOCUMENTATION_TRIAGE_2026_03_21.md)
- [Project roadmap refresh 2026-03-22](status/ROADMAP.md)

## Maintenance Checklist

When documentation changes in a PR:

1. Update this index if navigation changes.
2. Update the touched document dates.
3. Keep `ROADMAP.md`, `FEATURE_INVENTORY.md`, `IMPROVEMENTS.md`, and the relevant blueprint docs synchronized around the full fund-management target state.
4. Avoid editing generated docs by hand.
