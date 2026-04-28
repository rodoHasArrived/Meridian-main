# Architecture Documentation

**Owner:** Core Team
**Scope:** Engineering
**Review Cadence:** Quarterly or when significant architectural decisions are made

---

## Purpose

This directory contains documentation about the system's design, architectural decisions, and structural patterns. It is the authoritative source for understanding *how* the system is built and *why* key design choices were made.

---

## What Belongs Here

- High-level architecture overviews (C4 context, container, component)
- Layer boundary and dependency rule definitions
- Storage design and data flow explanations
- Desktop application architecture documentation
- Design rationale documents ("Why this architecture?")
- Domain boundary and module responsibility descriptions

## What Does NOT Belong Here

- Step-by-step developer guides → use `development/`
- Provider-specific setup or comparison content → use `providers/`
- Operational runbooks or deployment procedures → use `operations/`
- Architecture Decision Records (those live in `adr/`)

---

## Contents

| Document | Description |
|----------|-------------|
| [Overview](overview.md) | High-level system architecture |
| [Layer Boundaries](layer-boundaries.md) | Project dependency rules and enforcement |
| [Storage Design](storage-design.md) | Tiered storage pipeline and WAL design |
| [Deterministic Canonicalization](deterministic-canonicalization.md) | Data normalization and deduplication |
| [Desktop Layers](desktop-layers.md) | WPF desktop application architecture |
| [Why This Architecture](why-this-architecture.md) | Design rationale and tradeoffs |
| [Provider Management](provider-management.md) | Provider abstraction and failover |
| [Domain Boundaries](domains.md) | Domain model responsibilities |
| [C4 Diagrams Reference](c4-diagrams.md) | C4 views plus the runtime, workstation, Security Master, and fund-ops diagram catalog |
| [Crystallized Storage Format](crystallized-storage-format.md) | Storage format specification |
| [Deterministic Canonicalization](deterministic-canonicalization.md) | Normalization and deduplication rules |
| [Ledger Architecture](ledger-architecture.md) | Ledger, portfolio, and accounting architecture notes |
| [Environment Designer Runtime Projection and WPF Admin Surface](environment-designer-runtime-projection-and-wpf-admin-surface.md) | Draft/publish/rollback architecture for company umbrella environment design |
| [Desktop Layers](desktop-layers.md) | WPF desktop application layering |
| [WPF Shell MVVM](wpf-shell-mvvm.md) | Shell composition and MVVM direction for the desktop client |
| [WPF Workstation Shell UX](wpf-workstation-shell-ux.md) | Shared workstation-shell UX pattern for WPF research, trading, data operations, and governance |
| [Workflow Library](workflow-library.md) | Reusable workstation workflow and action registry architecture |
| [UI Redesign](ui-redesign.md) | Product and information-architecture direction for workstation UX |
| [Trading Workstation Migration Blueprint](../plans/trading-workstation-migration-blueprint.md) | Target run model, workspace IA, and migration phases |
| [MCP Server](layer-boundaries.md#dependency-graph) | MCP tool server — dependency position and boundary rules |

## Related

- [ADRs](../adr/README.md) — Architecture Decision Records (numbered decisions)
- [Diagrams](../diagrams/README.md) — Visual architecture diagrams (C4, DOT, Graphviz)
- [Diagrams / UML](../diagrams/uml/README.md) — UML sequence, state, and activity diagrams

---

*Architecture documentation is hand-authored and reviewed by the core team.*
