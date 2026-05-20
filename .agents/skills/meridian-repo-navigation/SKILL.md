---
name: meridian-repo-navigation
description: Orient an AI quickly inside the Meridian repository before deeper implementation work. Use when the task starts with "where should I look", "what subsystem owns this", "route me to the right files", or when a large-repo task needs fast grounding before specialist skills take over.
license: See repository LICENSE
compatibility: >
  Portable Agent Skill package for Agent Skills-compatible hosts. Reads generated repo-navigation
  references and optional MCP navigation resources when the host exposes them.
metadata:
  owner: meridian-ai
  version: "1.0"
  spec: open-agent-skills-v1
---

# Meridian Repo Navigation

Use this skill first when the main problem is orientation inside Meridian's size and structure rather than implementation details.

Read these in order:
1. `../_shared/project-context.md`
2. `../../../docs/ai/generated/repo-navigation.md`
3. `../../../docs/ai/navigation/README.md`

If MCP access is available, prefer the generated navigation resources/tools before broad codebase searching:
- `mdc://repo-navigation/quick-start`
- `mdc://repo-navigation/catalog`
- `find-subsystem`
- `route-task`
- `find-entrypoints`
- `find-related-projects`
- `find-authoritative-docs`

## Roles

### `repo-orienter`
- Classify the task into the closest subsystem.
- Name the first 2-3 projects, contracts, and docs to inspect.
- Keep the answer orientation-first, not implementation-first.

### `task-router`
- Translate natural-language requests like provider bug, WPF issue, storage regression, or MCP tool work into the right route from the generated repo map.
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

## Meridian Rules

- Start with the generated repo map before broad recursive searching.
- Prefer subsystem-level routing over file-by-file wandering.
- Use the shared project context to keep terminology and commands consistent.
- If multiple subsystems are involved, name the primary owner first and the cross-project edges second.
