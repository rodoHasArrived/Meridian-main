# Meridian Codex Skills

This repository includes project-local Codex skills under this folder.

Current skills:

| Skill | Entry Point | Purpose |
|-------|-------------|---------|
| `meridian-blueprint` | [`SKILL.md`](meridian-blueprint/SKILL.md) | Create implementation-ready technical blueprints |
| `meridian-brainstorm` | [`SKILL.md`](meridian-brainstorm/SKILL.md) | Generate Meridian-specific ideas and directions |
| `meridian-cleanup` | [`SKILL.md`](meridian-cleanup/SKILL.md) | Clean up code and docs without behavior changes |
| `meridian-code-review` | [`SKILL.md`](meridian-code-review/SKILL.md) | Review changes with Meridian’s architecture lenses |
| `meridian-implementation-assurance` | [`SKILL.md`](meridian-implementation-assurance/SKILL.md) | Validate completed work with explicit evidence |
| `meridian-provider-builder` | [`SKILL.md`](meridian-provider-builder/SKILL.md) | Build and extend providers |
| `meridian-repo-navigation` | [`SKILL.md`](meridian-repo-navigation/SKILL.md) | Orient large-repo tasks before specialist work |
| `meridian-roadmap-strategist` | [`SKILL.md`](meridian-roadmap-strategist/SKILL.md) | Build and update roadmap documents |
| `meridian-test-writer` | [`SKILL.md`](meridian-test-writer/SKILL.md) | Write Meridian-style tests |

**Shared resources:** [`_shared/project-context.md`](_shared/project-context.md) — canonical project statistics, paths, and ADR anchors used by all skills.

Recommended order for large-repo tasks:

1. `meridian-repo-navigation`
2. the relevant specialist skill
3. `meridian-implementation-assurance` when the change needs explicit validation
