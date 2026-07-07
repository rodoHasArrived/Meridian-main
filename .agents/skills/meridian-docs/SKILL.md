---
name: meridian-docs
description: Maintain Meridian documentation accurately and conservatively. Use when the user asks to update docs, fix stale docs, reconcile README or AGENTS guidance, add documentation for a changed Meridian workflow, refresh AI instructions, or keep docs aligned with code, commands, plans, provider workflows, packaging, desktop WPF support, browser workstation surfaces, or docs automation.
license: See repository LICENSE
compatibility: >
  Portable Agent Skill package for Agent Skills-compatible hosts. Reads markdown references
  on demand and edits repository documentation when the host permits filesystem writes.
metadata:
  owner: meridian-ai
  version: "1.0"
  spec: open-agent-skills-v1
---

# Meridian Docs

Keep Meridian documentation current, evidence-backed, and scoped to the requested change.

Read `../_shared/project-context.md` before editing.
For docs changes tied to implementation, coordinate with `meridian-implementation-assurance`; for
archived or superseded material, coordinate with `meridian-archive-organizer`.

## Use When

Use this skill for documentation work in this repository, including:

- Updating `README.md`, `AGENTS.md`, `CLAUDE.md`, shared project-context files, or AI guidance.
- Reconciling docs with current commands, scripts, routes, tests, packaging, provider flows, or roadmap status.
- Adding or fixing docs after code changes in public APIs, configuration, provider contracts, storage, UI services, WPF, browser workstation, or automation.
- Repairing stale paths, broken links, command drift, generated-doc guidance, or status/readiness docs.

Trigger examples:

- "Update AGENTS.md with the verified desktop workflow."
- "Fix stale docs after this provider change."
- "Reconcile the README with the current packaging commands."

## Do Not Use When

Use `meridian-blueprint` when the user wants a design, `meridian-code-review` when the user wants
findings only, `meridian-cleanup` when docs cleanup is part of broader source cleanup, and
`meridian-implementation-assurance` when code changes and proof gates are the main outcome.

Non-trigger examples:

- "Design the WPF shell modernization plan."
- "Review this diff for bugs."
- "Implement the provider and update docs."

## Current Product Direction

- Treat `src/Meridian.Ui/dashboard/` and `src/Meridian.Ui/wwwroot/workstation/` as active browser workstation surfaces.
- Treat `src/Meridian.Wpf/` as an active co-equal UI lane whose current focus is web-UI parity; keep WPF docs aligned with shared contracts alongside compatibility, validation, and maintenance guidance.
- Keep `src/Meridian.Ui.Services/` and `src/Meridian.Ui.Shared/` as shared API/read-model support surfaces for the browser workstation and retained WPF compatibility.
- Keep mobile development out of scope unless the user or roadmap explicitly reopens it.

## Workflow

1. Check `git status --short` and preserve unrelated user-owned changes.
2. Identify the authoritative source before editing: code, scripts, Make targets, tests, generated inventories, canonical docs, or the user's explicit instruction.
3. Keep edits minimal and in-place. Do not rewrite broad planning or generated files unless the task asks for it.
4. When guidance is uncertain, add a short TODO with the verification needed instead of inventing a workflow.
5. Update the nearest index or cross-link only when a new doc needs discoverability.
6. Run the narrowest validation command that proves the touched docs.
7. Finalize with files changed, evidence used, validation result, and any remaining uncertainty.

## Handoffs

- Hand off to `meridian-implementation-assurance` when docs edits are part of a code change that needs build/test evidence.
- Hand off to `meridian-archive-organizer` when material is superseded, historical, misplaced, or should move under `archive/`.
- Hand off to `meridian-roadmap-strategist` when the work is delivery sequencing, target-state reconciliation, or roadmap prioritization.
- Hand off to `meridian-repo-navigation` when the user needs to know which subsystem or documentation area owns a topic before editing.

## Routing

- Root shims: keep `AGENTS.md` short and point to canonical sources rather than duplicating large sections.
- Developer workflows: prefer `docs/developer/`, `docs/development/`, and `docs/HELP.md`.
- Operations and packaging: prefer `docs/operations/`.
- Status, readiness, and evidence gates: prefer `docs/status/`.
- Roadmap and planning interpretation: prefer `docs/plans/`.
- Architecture and ownership boundaries: prefer `docs/architecture/`.
- Generated navigation or AI inventory: prefer `docs/ai/` and the matching build scripts.
- Deprecated, superseded, or historical material: move or route through `archive/` with traceable references.

## Validation

Use the smallest applicable check:

- Docs-only markdown edits: `git diff --check -- <path>`.
- `AGENTS.md` edits: `git diff --check -- AGENTS.md`.
- AI guidance or skill changes: run the available AI inventory or skill validation scripts before claiming full validation.
- Docs automation script changes: run Python compile checks, targeted `--summary` output, artifact rendering when relevant, and `run-docs-automation.py --profile core --dry-run`.
- Command guidance: verify against `make/*.mk`, scripts, `--help`, tests, or source command handlers before documenting it as current.

If validation is skipped or blocked, state exactly why.

## Output Standards

- Lead with the documentation change made and the exact files touched.
- Name the repo evidence used: command output, script, source file, test, or canonical doc.
- Report validation commands and pass/fail status exactly.
- Separate unrelated dirty-worktree changes from this task's changes.
- Call out TODOs or uncertainty without presenting them as confirmed current behavior.

## Guardrails

- Keep documentation factual and conservative; distinguish verified facts from inferred direction.
- Do not advertise commands, routes, providers, or workflow steps unless they are supported by current repo evidence or explicitly marked TODO.
- Do not broaden a docs request into code changes unless stale docs reveal a small necessary fix and the user asked to correct issues.
- Do not touch generated docs, screenshots, reports, or archived files unless they are the requested target.
- Preserve canonical names, especially `Meridian Design System`, `src/Meridian.Wpf/`, and `src/Meridian.Ui/dashboard/`.
- Prefer exact paths, commands, routes, and test names over generic prose.
