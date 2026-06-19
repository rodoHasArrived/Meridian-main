---
name: meridian-blueprint
description: Create implementation-ready technical blueprints for Meridian features, refactors, and workflow changes. Use when the user asks for a blueprint, technical design, architecture plan, interface sketch, spike plan, migration design, or a code-ready spec for Meridian, especially for browser workstation flows, WPF desktop shell, providers, pipelines, orchestration services, or cross-project interfaces.
---

# Meridian Blueprint

Turn one Meridian idea into a design another engineer can implement without making core architectural decisions from scratch.

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before naming
types, interfaces, files, or commands. Read
[`references/blueprint-patterns.md`](references/blueprint-patterns.md) when the request needs
naming conventions, DI patterns, ADR reminders, or MVVM examples.

## Use When

Use this skill when the user has one selected Meridian idea, refactor, or workflow and needs a
decision-complete implementation design.

Trigger examples:

- "Blueprint the provider readiness cockpit."
- "Design the interfaces for this reconciliation workflow."
- "Turn candidate #1 into a code-ready technical spec."

## Do Not Use When

Use `meridian-brainstorm` when the user still wants options, `meridian-roadmap-strategist` when the
work is delivery sequencing, and `meridian-implementation-assurance` when implementation or proof is
the requested outcome.

Non-trigger examples:

- "Give me five ideas for provider management."
- "Refresh the six-week roadmap."
- "Implement and verify this already-approved design."

## Workflow

1. Restate the feature or refactor in one sentence.
2. Confirm scope boundaries: what is in, what is deliberately out, and what assumptions are being made.
3. Ground the design in existing Meridian abstractions and real paths before inventing new ones.
4. Name the public-facing types first: interfaces, orchestrators, view models, options, endpoints, contracts, and storage/read models.
5. Describe data flow, lifecycle, and failure modes in the actual order the system will execute them.
6. Call out testing strategy and validation commands that fit the touched layers.
7. Flag breaking changes explicitly and describe a migration path.

## Handoffs

- Hand off from `meridian-brainstorm` only after one idea is selected.
- Hand off to `meridian-test-writer` when the blueprint needs scenario-level test design.
- Hand off to `meridian-implementation-assurance` after the user asks to build or certify the blueprint.

## Validation

- Validate the blueprint against current repo paths, contracts, and docs before naming new types.
- Include targeted build, test, dashboard, or docs commands appropriate to the design.
- For AI/tooling blueprints, include `check-codex-skills.py`, `check-ai-inventory.py`, and mirror-policy checks where relevant.

## Output Shape

Prefer this structure unless the user asks for something narrower:

```md
## Summary
## Scope
## Architecture
## Interfaces and Models
## Data Flow
## Edge Cases and Risks
## Test Plan
## Open Questions
```

## Meridian-Specific Rules

- Reuse existing contracts before proposing new ones.
- Keep provider, storage, execution, and UI responsibilities in their current layers.
- For workflow-centric UI changes, default to `src/Meridian.Ui/dashboard/` and the `/workstation/` route for new browser-surface work. For WPF desktop work, place it in `src/Meridian.Wpf/`. Align visible operator navigation with `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`; treat legacy `Research`, `Data Operations`, and `Governance` names as compatibility aliases.
- For browser workstation work, keep route strings, visible labels, disabled reasons, empty states, and live-region status in view-model/catalog seams rather than scattering them through React components.
- For WPF desktop work, keep code-behind minimal and put behavior in `BindableBase` view models or services.
- Do not propose mobile applications, native mobile clients, or mobile-first workflows unless the roadmap or user explicitly reopens that lane.
- For pipeline or storage work, mention WAL, channel policy, and JSON source generation when relevant.
- For provider or execution changes, cite the concrete contracts being extended.

## Depth Modes

- Full blueprint: complete design with interfaces, flow, tests, and rollout notes.
- Spike blueprint: focus on the riskiest unknowns, experiments, and exit criteria.
- Interface-first blueprint: lock the public surface and leave internals intentionally thin.

## Quality Bar

- Use real Meridian namespaces, not placeholder names.
- Keep the design decision-complete but concise.
- Prefer behavior-level grouping over file-by-file churn lists.
- If the request is underspecified, make the minimum safe assumptions and label them clearly.

## Automation Scripts

- `scripts/blueprint_output_check.py` validates the expected receipt and blueprint section shape.
- `scripts/run_evals.py` runs deterministic dry-run eval fixtures for the skill package.
- `scripts/score_eval.py` scores blueprint outputs against a compact rubric.

## Output Standards

- State the selected goal, assumptions, and out-of-scope items.
- Name public interfaces, contracts, endpoints, options, and read models when they are part of the design.
- Describe data flow, failure modes, and validation commands clearly enough for another engineer to implement.
- Mark open questions only when the decision cannot be derived from repository evidence or the user's request.
