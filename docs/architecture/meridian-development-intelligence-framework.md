# Meridian Development Intelligence Framework (MDIF)

**Status:** active guidance  
**Owner:** core-team  
**Reviewed:** 2026-06-16

## Purpose

The Meridian Development Intelligence Framework (MDIF) makes future AI-assisted development sessions understand Meridian as consistently as a senior architect would. It provides a compact, repeatable context spine for code, tests, migrations, UI components, services, reports, and documentation.

MDIF is not a product feature embedded into Meridian. It is a repository governance and generation framework that keeps a growing fund-operations platform architecturally coherent.

The framework is intentionally scoped to Meridian's current product direction: an operational proof layer for fund, portfolio, accounting, reconciliation, close, reporting, delivery, and audit workflows. It should make agents better at preserving Meridian's shared contracts and proof chains, not encourage broad autonomous AI features inside the product.

## Meridian-Specific Operating Thesis

AI-assisted work in Meridian should optimize for one outcome: every generated artifact should make it easier to prove an operational record from source evidence to governed output.

The active proof chain is:

```text
source evidence
-> normalized record
-> validation
-> reconciliation
-> exception resolution
-> journal / ledger impact
-> capital account impact
-> close package
-> report line
-> delivery record
-> audit evidence
```

MDIF should reject or defer generated work that does not strengthen that chain unless the roadmap registry explicitly moves the capability into scope.

## Layer Model

| Layer | Repository location | Purpose |
| --- | --- | --- |
| Project Constitution | `docs/architecture/` | Canonical vision, architecture, domain model, standards, security, and audit principles. |
| Domain Dictionary | `docs/domain/` | Shared business vocabulary, relationships, examples, and expansion notes. |
| Decision Records | `docs/adr/` and architecture indexes | Historical and current rationale for irreversible architecture choices. |
| AI Context Packs | `docs/ai/context/` | Task-specific compact context that agents can load instead of long prompts. |
| Feature Specifications | `features/<FeatureName>/` | Implementation-ready feature packs with requirements, workflows, UI, impacts, and acceptance tests. |
| Code Generation Templates | `templates/` | Reusable patterns for entities, services, pages, APIs, repositories, and tests. |
| AI Prompt Library | `docs/ai/prompts/` | Stable prompts that compose architecture, domain, and template context. |
| AI Review Framework | `docs/ai/reviews/` or task evidence | Architecture, security, performance, audit, and accounting review packets before merge. |
| Knowledge Exporter | `build/scripts/ai/meridian_context_exporter.py` | Generates machine-readable and Markdown context snapshots for assistants. |

## Artifact Ownership

| Artifact type | Canonical owner | Update rule |
| --- | --- | --- |
| Product scope and deferrals | `docs/product/meridian-design-document.md` and roadmap registry data | Update only when product direction or roadmap evidence changes. |
| Architecture constraints | `docs/architecture/` and `docs/adr/` | Update when a durable boundary, invariant, or tradeoff changes. |
| Business nouns | `docs/domain/` | Add or update before generating broad code around a durable concept. |
| Repeated AI task context | `docs/ai/context/` | Keep compact and rule-oriented; do not duplicate full architecture docs. |
| Generated AI snapshots | `docs/ai/exports/` | Regenerate from source docs; do not hand-edit generated context files. |
| Source implementation truth | `src/**`, nearest source README, and `docs/source/data/*.yml` | Prefer shared services, contracts, and read models before UI-specific forks. |

## First-Priority Artifacts

MDIF starts with five artifacts that should be loaded or referenced before large architectural work:

1. [`meridian-vision.md`](meridian-vision.md) - product scope boundaries and module intent.
2. [`meridian-domain-model.md`](meridian-domain-model.md) - core operational-record entities and relationships.
3. [`ADR-017 Modular Operational Monolith`](../adr/017-modular-operational-monolith.md) - decision rationale for the current growth model.
4. [`Accounting Context`](../ai/context/accounting-context.md) - accounting rules for generated code and reviews.
5. [`Meridian Context Exporter`](../../build/scripts/ai/meridian_context_exporter.py) - generated context snapshot tool.

For operational-record work, pair the accounting context with [`Operational Evidence Context`](../ai/context/operational-evidence-context.md). That context is the compact rule pack for the current proof-layer product wedge.

## Usage Contract for AI Sessions

Before broad implementation, an AI session should:

1. Load the repository navigation workflow in `docs/ai/navigation/README.md` and `docs/ai/generated/repo-navigation.md`.
2. Load the stakeholder product direction in `docs/product/meridian-design-document.md` when the task changes scope, roadmap posture, or operator workflow.
3. Load the MDIF project constitution document most relevant to the task.
4. Load the matching domain dictionary files in `docs/domain/`.
5. Load a focused context pack from `docs/ai/context/`.
6. For `src/**` edits, read the nearest source README and identify the source module in `docs/source/data/source-modules.yml`.
7. Generate or refresh an export with `build/scripts/ai/meridian_context_exporter.py` when a machine-readable snapshot is useful.
8. Validate the change with the narrowest command that covers the touched files.

## Generation Filters

Generated code, tests, UI, and docs should pass these filters before implementation:

- Does it strengthen data confidence, retained evidence, reconciliation, approvals, accounting records, multi-asset operational coverage, governed reports, or audit evidence?
- Does it preserve the active operator navigation model: `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`?
- Does it use shared contracts, endpoint read models, and services before creating separate browser or WPF logic?
- Does it keep AI inside reviewed assistance, extraction, explanation, draft preparation, or discrepancy detection rather than autonomous posting, approval, payment release, or report publication?
- Does it defer mobile apps, full live trading, full payment execution, broad client portals, no-code workflow builders, and unrelated forecasting or enterprise-risk surfaces unless roadmap evidence explicitly reopens them?

## Drift Prevention Rules

- Prefer extending canonical domain and context files over adding one-off prompt text.
- Add a decision record when a change creates a durable architecture constraint.
- Add or update a domain dictionary page when code introduces a new business noun.
- Add a context pack when repeated AI work needs the same compact rules.
- Keep generated exports out of hand-authored source-of-truth docs; regenerate them from the exporter.
- Treat generated suggestions as drafts until they pass source evidence, approval, audit, and narrow validation checks.
