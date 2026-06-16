# Meridian Development Intelligence Framework (MDIF)

**Status:** active guidance  
**Owner:** core-team  
**Reviewed:** 2026-06-16

## Purpose

The Meridian Development Intelligence Framework (MDIF) makes future AI-assisted development sessions understand Meridian as consistently as a senior architect would. It provides a compact, repeatable context spine for code, tests, migrations, UI components, services, reports, and documentation.

MDIF is not a product feature embedded into Meridian. It is a repository governance and generation framework that keeps a growing fund-operations platform architecturally coherent.

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

## First-Priority Artifacts

MDIF starts with five artifacts that should be loaded or referenced before large architectural work:

1. [`meridian-vision.md`](meridian-vision.md) — product scope boundaries and module intent.
2. [`meridian-domain-model.md`](meridian-domain-model.md) — core operational-record entities and relationships.
3. [`ADR-017 Modular Operational Monolith`](../adr/017-modular-operational-monolith.md) — decision rationale for the current growth model.
4. [`Accounting Context`](../ai/context/accounting-context.md) — accounting rules for generated code and reviews.
5. [`Meridian Context Exporter`](../../build/scripts/ai/meridian_context_exporter.py) — generated context snapshot tool.

## Usage Contract for AI Sessions

Before broad implementation, an AI session should:

1. Load the repository navigation workflow in `docs/ai/navigation/README.md` and `docs/ai/generated/repo-navigation.md`.
2. Load the MDIF project constitution document most relevant to the task.
3. Load the matching domain dictionary files in `docs/domain/`.
4. Load a focused context pack from `docs/ai/context/`.
5. Generate or refresh an export with `build/scripts/ai/meridian_context_exporter.py` when a machine-readable snapshot is useful.
6. Validate the change with the narrowest command that covers the touched files.

## Self-Maintenance Contract

MDIF stays useful only when source-of-truth docs, exports, and checks move together:

1. Update the hand-authored source first: `docs/architecture/meridian-*.md`, `docs/domain/*.md`, `docs/ai/context/*.md`, or `docs/adr/*.md`.
2. Regenerate snapshots with `make ai-mdif-context` or `python3 build/scripts/ai/meridian_context_exporter.py --summary`.
3. Verify snapshots with `make ai-mdif-context-check` or `python3 build/scripts/ai/meridian_context_exporter.py --check --summary`. The check compares a source digest over MDIF architecture docs, domain dictionary pages, context packs, ADRs, and current project files.
4. Run `python3 build/scripts/docs/validate-docs-structure.py --top-level domain --summary` when domain pages change.
5. Run Codex/AI inventory checks when any Codex-facing or AI index changes.

Generated exports are useful session inputs, not source-of-truth files. If the check reports a stale digest, update the hand-authored source or rerun the exporter rather than editing the generated JSON or Markdown by hand.

## Drift Prevention Rules

- Prefer extending canonical domain and context files over adding one-off prompt text.
- Add a decision record when a change creates a durable architecture constraint.
- Add or update a domain dictionary page when code introduces a new business noun.
- Add a context pack when repeated AI work needs the same compact rules.
- Keep generated exports out of hand-authored source-of-truth docs; regenerate them from the exporter.
