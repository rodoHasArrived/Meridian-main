# Architecture Documentation

**Owner:** Core Team
**Scope:** Engineering
**Review Cadence:** Quarterly or when significant architectural decisions are made

## Purpose

This directory contains documentation about the system's design, structural boundaries, and major technical tradeoffs. It is the authoritative source for understanding how Meridian is shaped today across platform, workstation, ledger, provider, and storage concerns.

## What Belongs Here

- High-level architecture overviews
- Layer boundary and dependency rule definitions
- Storage, provider, ledger, and domain-boundary explanations
- Desktop and WPF shell architecture notes
- Design rationale documents

## What Does Not Belong Here

- Step-by-step implementation guides: use `development/`
- Provider setup and comparison content: use `providers/`
- Operator procedures: use `operations/`
- Architectural decisions with formal numbering: use `adr/`

## Contents

| Document | Description |
|----------|-------------|
| [Overview](overview.md) | High-level architecture across web, WPF, MCP, and shared platform seams |
| [Layer Boundaries](layer-boundaries.md) | Project dependency rules and enforcement guidance |
| [Domains](domains.md) | Domain model responsibilities and payload boundaries |
| [Provider Management](provider-management.md) | Provider abstraction, routing, and failover direction |
| [Storage Design](storage-design.md) | Tiered storage pipeline, WAL, and storage organization |
| [Crystallized Storage Format](crystallized-storage-format.md) | Storage format specification |
| [Deterministic Canonicalization](deterministic-canonicalization.md) | Normalization and deduplication rules |
| [Ledger Architecture](ledger-architecture.md) | Ledger, portfolio, and accounting architecture notes |
| [Desktop Layers](desktop-layers.md) | WPF desktop application layering |
| [WPF Shell MVVM](wpf-shell-mvvm.md) | Shell composition and MVVM direction for the desktop client |
| [WPF Workstation Shell UX](wpf-workstation-shell-ux.md) | Shared workstation-shell UX pattern for WPF research, trading, data operations, and governance |
| [UI Redesign](ui-redesign.md) | Product and information-architecture direction for workstation UX |
| [Why This Architecture](why-this-architecture.md) | Rationale and tradeoffs behind the current shape |
| [C4 Diagrams](c4-diagrams.md) | C4 documentation references and diagrams |

## Related

- [ADRs](../adr/README.md)
- [Diagrams](../diagrams/README.md)
- [Plans Overview](../plans/README.md)
- [Trading Workstation Migration Blueprint](../plans/trading-workstation-migration-blueprint.md)
- [Governance and Fund Operations Blueprint](../plans/governance-fund-ops-blueprint.md)
