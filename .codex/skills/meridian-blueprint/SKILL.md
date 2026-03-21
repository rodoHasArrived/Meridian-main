---
name: meridian-blueprint
description: Create implementation-ready technical blueprints for Meridian features, refactors, and workflow changes. Use when the user asks for a blueprint, technical design, architecture plan, interface sketch, spike plan, migration design, or a code-ready spec for Meridian, especially for WPF, trading workstation migration, providers, pipelines, orchestration services, or cross-project interfaces.
---

# Meridian Blueprint

Turn one Meridian idea into a design another engineer can implement without making core architectural decisions from scratch.

Read `../_shared/project-context.md` before naming types, interfaces, files, or commands. Read `references/blueprint-patterns.md` when the request needs naming conventions, DI patterns, ADR reminders, or MVVM examples.

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
- For workflow-centric UI changes, align with the Research, Trading, Data Operations, and Governance workspaces from `docs/plans/trading-workstation-migration-blueprint.md`.
- For WPF work, keep code-behind minimal and put behavior in `BindableBase` view models or services.
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
