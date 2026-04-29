# Meridian Codex Skills

This folder contains Meridian's repo-local Codex skills. These are the primary project-scoped
skills for the current AI workflow and should stay aligned with Meridian's browser-first operator
workstation direction, retained WPF support, and fund-management/trading-platform scope.

## Current Skills

| Skill | Entry Point | Purpose |
|-------|-------------|---------|
| `meridian-archive-organizer` | [`SKILL.md`](meridian-archive-organizer/SKILL.md) | Archive stale code/docs and keep the repo structure tidy |
| `meridian-blueprint` | [`SKILL.md`](meridian-blueprint/SKILL.md) | Create implementation-ready Meridian technical blueprints |
| `meridian-brainstorm` | [`SKILL.md`](meridian-brainstorm/SKILL.md) | Generate Meridian-native product and architecture ideas |
| `meridian-cleanup` | [`SKILL.md`](meridian-cleanup/SKILL.md) | Clean up code and docs without behavior changes |
| `meridian-code-review` | [`SKILL.md`](meridian-code-review/SKILL.md) | Review changes for bugs, regressions, and architecture drift |
| `meridian-implementation-assurance` | [`SKILL.md`](meridian-implementation-assurance/SKILL.md) | Implement and verify changes with explicit evidence |
| `meridian-provider-builder` | [`SKILL.md`](meridian-provider-builder/SKILL.md) | Build and extend provider integrations |
| `meridian-repo-navigation` | [`SKILL.md`](meridian-repo-navigation/SKILL.md) | Orient large-repo tasks before specialist work |
| `meridian-roadmap-strategist` | [`SKILL.md`](meridian-roadmap-strategist/SKILL.md) | Refresh roadmap, delivery-plan, and target-state docs |
| `meridian-simulated-user-panel` | [`SKILL.md`](meridian-simulated-user-panel/SKILL.md) | Run manifest-driven design-partner, release-gate, and usability-lab reviews |
| `meridian-test-writer` | [`SKILL.md`](meridian-test-writer/SKILL.md) | Write scenario-first Meridian tests |

## Shared Resources

- [`_shared/project-context.md`](_shared/project-context.md) — current product framing, solution
  map, key abstractions, and review guardrails

## Maintenance Rules

- Keep each skill's `description` aligned with the current `README.md` and
  `docs/status/ROADMAP.md`, not with older market-data-only phrasing.
- Keep `agents/openai.yaml` synchronized with the skill text so Codex UI metadata stays current.
- Mirror shared workflow changes into the corresponding Claude and GitHub agent surfaces when a
  specialist workflow is meant to stay host-consistent.

## Recommended Flow

1. `meridian-repo-navigation`
2. the relevant specialist skill
3. `meridian-implementation-assurance` when the change needs explicit validation
