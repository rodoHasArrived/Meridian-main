# Architecture Documentation

**Owner:** Core Team
**Scope:** Engineering
**Review Cadence:** Quarterly or when significant architectural decisions are made

---

## Purpose

This directory contains documentation about the system's design, architectural decisions, and structural patterns. It is the authoritative source for understanding _how_ the system is built and _why_ key design choices were made.

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
| -------- | ----------- |
| [Overview](overview.md) | High-level system architecture |
| [Meridian Development Intelligence Framework](meridian-development-intelligence-framework.md) | AI development intelligence framework for project constitution, domain dictionaries, context packs, templates, reviews, and context exports |
| [Meridian Vision](meridian-vision.md) | Product scope boundaries and long-term module intent for AI-assisted development |
| [Meridian Domain Model](meridian-domain-model.md) | Compact operational-record domain model and invariants for generated code, tests, UI, and reports |
| [Project Structure](project-structure.md) | Maintained repository inventory and cleanup classification |
| [Module Map](module-map.md) | Layer-oriented project map and dependency boundary quick reference |
| [Design Document Adaptation](design-document-adaptation.md) | Executable adaptation contract for the design document's scope, contexts, modules, workspaces, screen inventory, and deferrals |
| [Design Module Conformance](design-module-conformance.md) | Maps the design document's bounded-context modules to current source owners and staged extraction rules |
| [MVVM Guidelines](mvvm-guidelines.md) | Browser workstation and WPF desktop view-model boundaries |
| [Layer Boundaries](layer-boundaries.md) | Project dependency rules and enforcement |
| [Storage Design](storage-design.md) | Tiered storage pipeline and WAL design |
| [Deterministic Canonicalization](deterministic-canonicalization.md) | Data normalization and deduplication |
| [Desktop Layers](desktop-layers.md) | WPF desktop application architecture |
| [Why This Architecture](why-this-architecture.md) | Design rationale and tradeoffs |
| [Provider Management](provider-management.md) | Provider abstraction and failover |
| [Domain Boundaries](domains.md) | Domain model responsibilities |
| [C4 Diagrams Reference](c4-diagrams.md) | C4 views plus the runtime, workstation, Security Master, and fund-ops diagram catalog |
| [Crystallized Storage Format](crystallized-storage-format.md) | Storage format specification |
| [Ledger Architecture](ledger-architecture.md) | Ledger, portfolio, Security Master expected accounting, and accounting architecture notes |
| [Strategy Builder Integration](strategy-builder-integration.md) | Browser Strategy Builder contracts, JSONL draft storage, QuantScript proof execution, and prototype boundary |
| [Strategy Engine Foundation](strategy-engine-foundation.md) | Shared Strategy Engine definitions, run validation, data dependency policy, evidence manifests, and workstation API surface |
| [Environment Designer Runtime Projection and WPF Admin Surface](environment-designer-runtime-projection-and-wpf-admin-surface.md) | Draft/publish/rollback architecture for company umbrella environment design |
| [WPF Shell MVVM](wpf-shell-mvvm.md) | Shell composition and MVVM direction for the desktop client |
| [WPF Workstation Shell UX](wpf-workstation-shell-ux.md) | WPF workstation shell UX pattern and guidance for WPF workspace shells |
| [Core Extensibility Model](core-extensibility-model.md) | Stable financial operations core objects, configurable tenant layers, governed foundations, and current contract/service seams |
| [Workflow Library](workflow-library.md) | Reusable workstation workflow and action registry architecture |
| [Evidence Workflow Fabric](evidence-workflow-fabric.md) | Cross-workflow evidence packets, lineage, validation, and manifest-only export architecture |
| [Stakeholder Product Charter](../product/meridian-design-document.md) | Product-facing strategy and capability model used for current direction framing |
| [Trading Workstation Migration Blueprint (Archived)](../../archive/docs/plans/trading-workstation-migration-blueprint.md) | Historical migration model retained for reference; active architecture execution posture is now under canonical product/engineering documentation |
| [Current Direction and Status (Archived)](../../archive/docs/plans/current-direction-and-status.md) | Historical planning interpretation retained for context; active direction now in `docs/product/` |
| [MCP Server](layer-boundaries.md#dependency-graph) | MCP tool server — dependency position and boundary rules |

Historical UI redesign notes now live in [`../../archive/docs/assessments/ui-redesign.md`](../../archive/docs/assessments/ui-redesign.md).

## Related

- [ADRs](../adr/README.md) — Architecture Decision Records (numbered decisions)
- [Diagrams](../diagrams/README.md) — Visual architecture diagrams (C4, DOT, Graphviz)
- [Diagrams / UML](../diagrams/uml/README.md) — UML sequence, state, and activity diagrams

---

_Architecture documentation is hand-authored and reviewed by the core team._
