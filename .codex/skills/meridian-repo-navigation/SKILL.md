---
name: meridian-repo-navigation
description: Orient an AI quickly inside the Meridian repository before deeper implementation work. Use when the task starts with "where should I look", "what subsystem owns this", "route me to the right files", or when a large-repo task needs fast grounding before specialist skills take over.
---

# Meridian Repo Navigation

Use this skill first when the main problem is orientation inside Meridian's size and structure rather than implementation details.

Read these in order:
1. `../_shared/project-context.md`
2. `../_shared/codex-execution-contract.md`
3. `../../../docs/ai/generated/repo-navigation.md`
4. `../../../docs/ai/navigation/README.md`

If MCP access is available, prefer the generated navigation resources/tools before broad codebase searching:
- `mdc://repo-navigation/quick-start`
- `mdc://repo-navigation/catalog`
- `find-subsystem`
- `route-task`
- `find-entrypoints`
- `find-related-projects`
- `find-authoritative-docs`

## Use When

Use this skill when the user needs orientation, ownership, entrypoints, authoritative docs, or a
route to the next specialist before deeper work starts.

Trigger examples:

- "Where should I look for trading readiness?"
- "Route this bug to the right subsystem."
- "Which files own provider health?"

## Do Not Use When

Use the relevant specialist skill when the subsystem is already clear and the user asks for design,
implementation, review, tests, roadmap work, or archive action.

Non-trigger examples:

- "Implement the selected dashboard fix."
- "Write tests for the provider."
- "Blueprint the reconciliation cockpit."

## Roles

### `repo-orienter`
- Classify the task into the closest subsystem.
- Name the first 2-3 projects, contracts, and docs to inspect.
- Keep the answer orientation-first, not implementation-first.

### `task-router`
- Translate natural-language requests like browser workstation issue, provider bug, retained WPF issue, storage regression, or MCP tool work into the right route from the generated repo map.
- Recommend the next specialist skill or agent once the subsystem is identified.

### `execution-tracer`
- For follow-up exploration, point at the likely entrypoints and dependency edges that explain execution flow.
- Stay high-signal; do not dump exhaustive symbol lists.

### `doc-router`
- Point the AI to the authoritative docs and guardrails before changes start.
- Prefer AI guides, migration blueprints, and developer guides over generic README scanning.

## Workflow

1. Match the task to a route in `docs/ai/generated/repo-navigation.md` or the MCP navigation tools.
2. Confirm the owning subsystem, start projects, and key contracts.
3. Read the authoritative docs for that route.
4. Hand off to the specialist skill or agent only after orientation is complete.

## Handoffs

- Route first, then exit; do not keep driving implementation from this skill.
- Hand off to `meridian-blueprint`, `meridian-implementation-assurance`, `meridian-code-review`, `meridian-test-writer`, or another specialist based on the routed task phase.
- If multiple subsystems are involved, name the primary owner and the cross-project edges before handing off.

## Validation

- Validate routing against generated repo navigation, authoritative docs, and nearby project structure.
- Use targeted `Select-String`, `rg --files`, or direct file reads to confirm route claims when generated docs are ambiguous.
- Avoid full repo scans unless targeted routing cannot resolve ownership.

## Meridian Rules

- Start with the generated repo map before broad recursive searching.
- Route new operator-facing UI requests to `src/Meridian.Ui/dashboard/` and `/workstation/` first unless the user explicitly asks for retained WPF.
- Prefer subsystem-level routing over file-by-file wandering.
- Use the shared project context to keep terminology and commands consistent.
- If multiple subsystems are involved, name the primary owner first and the cross-project edges second.

## Output Standards

- Name the subsystem, first files, first docs, likely tests, and recommended next skill.
- Keep orientation concise and avoid implementation plans unless the user asks for planning.
- Distinguish verified routes from inferred routes when evidence is incomplete.
