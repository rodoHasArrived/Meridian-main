# Architecture Documentation

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05
**Scope:** Engineering
**Review Cadence:** Quarterly or when significant architectural decisions are made

---

## Purpose

This directory contains documentation about the system's design, architectural decisions, and structural patterns. It is the authoritative source for understanding _how_ the system is built and _why_ key design choices were made.

Start with [Overview](overview.md) and [Module Map](module-map.md) for system orientation,
[Layer Boundaries](layer-boundaries.md) before changing dependencies, and the
[Domain Dictionary](../domain/README.md) for business terminology. The [source registry](../source/README.md)
and owning source README establish implemented behavior; a specification or dated review alone does
not establish delivery status. Use [Product](../product/README.md) for roadmap and release evidence.

This index's review covers navigation and document scope. Dated reviews retain their own evidence dates.

---

## What Belongs Here

- High-level architecture overviews (C4 context, container, component)
- Layer boundary and dependency rule definitions
- Storage design and data flow explanations
- Desktop application architecture documentation
- Design rationale documents ("Why this architecture?")
- Domain boundary and module responsibility descriptions

## What Does NOT Belong Here

- Step-by-step developer guides → use [Engineering](../engineering/README.md) or
  [Development Guides](../development/README.md)
- Provider setup and recovery → use [Operators](../operators/README.md); provider capability and
  evidence lookup → use [Reference](../reference/README.md)
- Operational runbooks or deployment procedures → use [Operators](../operators/README.md)
- Architecture Decision Records → use [ADRs](../adr/README.md)

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
| [Module Conventions](module-conventions.md) | Capability endpoint groups, tenant-context, scoped-access, tenant-store-factory, registration seams, and the no-new-god-file ratchet |
| [Design Document Adaptation](design-document-adaptation.md) | Executable adaptation contract for the design document's scope, contexts, modules, workspaces, screen inventory, and deferrals |
| [Design Module Conformance](design-module-conformance.md) | Maps the design document's bounded-context modules to current source owners and staged extraction rules |
| [MVVM Guidelines](mvvm-guidelines.md) | Browser workstation and WPF desktop view-model boundaries |
| [Layer Boundaries](layer-boundaries.md) | Project dependency rules and enforcement |
| [Storage Design](storage-design.md) | File-backed market-data archive, tiering, pipeline, and WAL design; operational database ownership is separate |
| [Storage Topology Diagram](diagrams/meridian-storage-topology.mmd) | File-backed and PostgreSQL durable stores, control services, and their consumers |
| [Deterministic Canonicalization](deterministic-canonicalization.md) | Data normalization and deduplication |
| [Desktop Layers](desktop-layers.md) | WPF desktop application architecture |
| [Why This Architecture](why-this-architecture.md) | Design rationale and tradeoffs |
| [Provider Management](provider-management.md) | Provider abstraction and failover |
| [Provider Integration Manifest Runtime](provider-integration-manifest-runtime.md) | No-code provider integration manifests, generic connector runtime, raw payload retention, mapping, validation, quarantine, and certified trading boundary |
| [Market-Data Runtime Contracts](domains.md) | Collector event envelopes, payloads, and source attribution; use the Domain Dictionary for business concepts |
| [Security Master Extensibility Review](security-master-extensibility-review.md) | Dated cross-asset assessment and source-evidence addenda; recheck each finding against current source before acting |
| [Security Master Identifier Conflict Detection](security-master-identifier-conflict-detection.md) | Canonical identifier equality, validity-window overlap, complete claimant-pair detection, and indexed rebuild behavior |
| [Security-Identified Open-Lot Convergence Blueprint](../engineering/blueprints/security-lot-convergence-blueprint.md) | Target contract and staged migration for SecurityId-keyed unit/face lots, acquisition FX, relief, amortization, and corporate-action continuity |
| [C4 Diagrams Reference](c4-diagrams.md) | C4 views plus the runtime, workstation, Security Master, and fund-ops diagram catalog |
| [Crystallized Storage Format](crystallized-storage-format.md) | Storage format specification |
| [Ledger Architecture](ledger-architecture.md) | Ledger, portfolio, Security Master expected accounting, and accounting architecture notes |
| [Event Accounting Architecture](event-accounting-architecture.md) | Event-backed accounting posting commands, immutable journal facts, evidence references, and storage validation |
| [Strategy Builder Integration](strategy-builder-integration.md) | Browser Strategy Builder contracts, JSONL draft storage, QuantScript proof execution, and prototype boundary |
| [Strategy Engine Foundation](strategy-engine-foundation.md) | Shared Strategy Engine definitions, run validation, data dependency policy, evidence manifests, and workstation API surface |
| [Environment Designer Runtime Projection and WPF Admin Surface](environment-designer-runtime-projection-and-wpf-admin-surface.md) | Draft/publish/rollback architecture for company umbrella environment design |
| [WPF Shell MVVM](wpf-shell-mvvm.md) | Shell composition and MVVM direction for the desktop client |
| [WPF Workstation Shell UX](wpf-workstation-shell-ux.md) | WPF workstation shell UX pattern and guidance for WPF workspace shells |
| [Core Extensibility Model](core-extensibility-model.md) | Stable financial operations core objects, configurable tenant layers, governed foundations, and current contract/service seams |
| [Workflow Library](workflow-library.md) | Reusable workstation workflow and action registry architecture |
| [Evidence Workflow Fabric](evidence-workflow-fabric.md) | Cross-workflow evidence packets, lineage, validation, and manifest-only export architecture |
| [Reporting Workstation Model](reporting-workstation-model.md) | Reporting production pipeline, controlled state vocabularies, reporting period, health gates, and change-since-review |
| [Runtime Component State Boundaries](runtime-component-state-boundaries.md) | Stateful host responsibilities, recovery boundaries, and prerequisites for service extraction or scaling |
| [Operator Observability Dashboard](operator-observability-dashboard.md) | Dashboard design targets; deployed thresholds remain owned by the SLO registry and alert rules |
| [Write-Path Invariants](write-path-invariants.md) | Required versioning, idempotency, transaction, and evidence boundaries for operator-critical writes |
| [Workstation Continuity Payload Profile](workstation-continuity-payload-profile.md) | Shared ledger, reconciliation, and strategy continuity contracts and compatibility tests |
| [Stakeholder Product Charter](../product/meridian-design-document.md) | Product-facing strategy and capability model used for current direction framing |
| [Trading Workstation Migration Blueprint (Archived)](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/plans/trading-workstation-migration-blueprint.md) | Historical migration model retained for reference; active architecture execution posture is now under canonical product/engineering documentation |
| [Current Direction and Status (Archived)](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/plans/current-direction-and-status.md) | Historical planning interpretation retained for context; active direction now in `docs/product/` |
| [MCP Server](layer-boundaries.md#dependency-graph) | MCP tool server — dependency position and boundary rules |

Historical UI redesign notes now live in [`../../archive/docs/assessments/ui-redesign.md`](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/assessments/ui-redesign.md).

## Related

- [ADRs](../adr/README.md) — Architecture Decision Records (numbered decisions)
- [Diagrams](../diagrams/README.md) — Visual architecture diagrams (C4, DOT, Graphviz)
- [Diagrams / UML](../diagrams/uml/README.md) — UML sequence, state, and activity diagrams

---

_Architecture documentation is hand-authored and reviewed by the core team._
