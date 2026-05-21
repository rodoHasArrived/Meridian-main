---
name: meridian-blueprint
description: Create implementation-ready technical blueprints for Meridian features, refactors, and workflow changes. Use when the user asks for a blueprint, technical design, architecture plan, interface sketch, spike plan, migration design, or a code-ready spec for Meridian, especially for browser workstation flows, retained WPF support, providers, pipelines, orchestration services, or cross-project interfaces.
---

# Meridian Blueprint

Turn one Meridian idea into a design another engineer can implement without making core architectural decisions from scratch.

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` before naming
types, interfaces, files, or commands. Read
[`references/blueprint-patterns.md`](references/blueprint-patterns.md) when the request needs
naming conventions, DI patterns, ADR reminders, or MVVM examples.

## Workflow

1. Restate the feature or refactor in one sentence.
2. Confirm scope boundaries: what is in, what is deliberately out, and what assumptions are being made.
3. Ground the design in existing Meridian abstractions and real paths before inventing new ones.
4. Name the public-facing types first: interfaces, orchestrators, view models, options, endpoints, contracts, and storage/read models.
5. Describe data flow, lifecycle, and failure modes in the actual order the system will execute them.
6. Call out testing strategy and validation commands that fit the touched layers.
7. Flag breaking changes explicitly and describe a migration path.

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
- For workflow-centric UI changes, default to `src/Meridian.Ui/dashboard/` and the `/workstation/` route unless the request is explicitly retained-WPF. Align visible operator navigation with `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`; treat legacy `Research`, `Data Operations`, and `Governance` names as compatibility aliases.
- For browser workstation work, keep route strings, visible labels, disabled reasons, empty states, and live-region status in view-model/catalog seams rather than scattering them through React components.
- For retained WPF work, keep code-behind minimal and put behavior in `BindableBase` view models or services.
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
