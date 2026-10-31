# Meridian Development Intelligence Framework (MDIF)

**Status:** active guidance  
**Owner:** core-team  
**Reviewed:** 2026-10-30

## Purpose

The Meridian Development Intelligence Framework (MDIF) makes future AI-assisted development sessions understand Meridian as consistently as a senior architect would. It provides a compact, repeatable context spine for code, tests, migrations, UI components, services, reports, and documentation.

MDIF is not a product feature embedded into Meridian. It is a repository governance and generation framework that keeps the operational-finance and trading platform architecturally coherent.

The framework follows the current charter's objectives: financial operations, strategy formulation
and validation, order management, and governed strategy execution. It helps agents preserve shared
contracts, record ownership, and evidence through those workflows. Expanded objectives do not
authorize autonomous model output to approve, dispatch orders, post journals, or release reports.

## Meridian-Specific Operating Thesis

Meridian should be modeled as a customer-neutral financial-operations and trading platform. Fund operations are
an important specialization, but generated architecture should start from neutral scope concepts:
organization, entity, portfolio, account, book, period, strategy, strategy run, order, execution
report, operational event, evidence, approval, journal, report, and audit trail.

AI-assisted work should strengthen a declared operator outcome and make its inputs, decisions,
authority, results, and recovery reconstructable. A financial close is not a prerequisite for all
strategy or OMS work; neither a strategy run nor an order fill establishes accounting acceptance.

The connected proof paths are:

| Workflow | Evidence and authority to preserve |
| --- | --- |
| Financial operations | Retained source, normalized record, validation, reconciliation, exception disposition, approved journal, close package, report line, delivery, and audit evidence; capital-account impact where applicable |
| Strategy formulation and validation | Exact strategy version, parameters, data and runtime assumptions, reproducible run results, paper evidence, and authorized promotion |
| Order management and execution | Operator or strategy intent, account and pre-trade authority, durable command identity, broker-confirmed state, fills, recovery, and accounting handoff |

Use the current design charter, roadmap registry, source evidence, and explicit user direction to
evaluate expansion. Prior baselines describe accepted evidence; they are not development ceilings.

## Layer Model

| Layer | Repository location | Purpose |
| --- | --- | --- |
| Project Constitution | `docs/architecture/` | Canonical vision, architecture, domain model, standards, security, and audit principles. |
| Domain Dictionary | `docs/domain/` | Shared business vocabulary, relationships, examples, and expansion notes. |
| Decision Records | `docs/adr/` and architecture indexes | Historical and current rationale for irreversible architecture choices. |
| AI Context Packs | `docs/ai/context/` | Task-specific compact context that agents can load instead of long prompts. |
| Feature Specifications | [Plans and Blueprints Register](../engineering/blueprints/README.md) | Current homes for requirements, workflows, UI impacts, and acceptance plans. A root `features/<FeatureName>/` convention is a proposed MDIF layer, not an existing repository requirement. |
| Code Generation Templates | [Examples](../examples/README.md) and owning source guides | Reuse current templates and verified patterns. A root `templates/` generation library is a proposed MDIF layer, not an existing implementation. |
| AI Prompt Library | [Prompt index](../ai/prompts/README.md), `.github/prompts/`, and `docs/prompts/` | The AI prompt directory is an index; actual Copilot prompts and provider-agnostic guidance live in their maintained homes. |
| AI Review Framework | Task or pull-request evidence and the owning documentation lane | Architecture, security, performance, audit, and accounting review packets before merge. There is no current `docs/ai/reviews/` directory. |
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
For strategy and OMS work, also load the
[strategy, order, and execution dictionary](../domain/strategy-order-execution.md), the
[execution boundary context](../ai/context/README.md#execution-boundary-context), and the relevant
source-module guidance. The [expanded-objective tracker](../product/implementation-todo-list.md#expanded-strategy-and-order-management-objectives)
owns follow-up; generated artifacts must not invent delivery status or live authority.

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

- Does it strengthen a declared financial-operations, strategy-formulation, order-management, or governed-execution outcome while preserving the applicable evidence and control boundaries?
- Does it avoid requiring fund scope unless the workflow is explicitly fund, investor, or private-capital specific?
- Does it preserve the active operator navigation model: `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`?
- Does it use shared contracts, endpoint read models, and services before creating separate browser or WPF logic?
- Does it keep model output inside reviewed assistance and prevent it from granting approval, live order authority, posting, payment release, or report publication?
- Does deterministic strategy execution retain the approved version, account authority, promotion, runtime-isolation, pre-trade risk, and live operator-control gates rather than treating a model suggestion or successful backtest as permission to trade?
- Does it distinguish requested order actions from broker-confirmed outcomes and preserve uncertainty, recovery, and the canonical accounting handoff?
- Does it keep mobile development out of scope and evaluate other expansion through the current
  charter, roadmap, source evidence, and explicit user direction while preserving the operational
  proof chain?

## Drift Prevention Rules

- Prefer extending canonical domain and context files over adding one-off prompt text.
- Add a decision record when a change creates a durable architecture constraint.
- Add or update a domain dictionary page when code introduces a new business noun.
- Add a context pack when repeated AI work needs the same compact rules.
- Keep generated exports out of hand-authored source-of-truth docs; regenerate them from the exporter.
- Treat generated suggestions as drafts until they pass source evidence, approval, audit, and narrow validation checks.
