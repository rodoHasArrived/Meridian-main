# Codex Skill Package Checklist

Use this checklist when creating or auditing Meridian repo-local Codex skills.

## Required Surfaces

- `.codex/skills/<skill>/SKILL.md` with `name` and `description` frontmatter only.
- `agents/openai.yaml` with `display_name`, `short_description`, and a `default_prompt` that names
  `$<skill>`.
- Required sections: `Use When`, `Do Not Use When`, `Workflow`, `Handoffs`, `Validation`, and
  `Output Standards`.
- Trigger and non-trigger examples.
- Links to `../_shared/project-context.md` and `../_shared/codex-execution-contract.md`.
- Skill-owned `scripts/`, `references/`, and `evals/` when the workflow has deterministic checks or
  reusable detail.
- `.codex/agents/<skill>.toml` plus `.codex/config.toml` registration when the skill should be a
  specialist profile.
- `.codex/skills/README.md`, `docs/ai/codex/README.md`, `docs/ai/skills/README.md`, and
  `docs/ai/codex/prompt-route-rules.json` entries when discovery changes.

## Eval Policy

- Deterministic dry-run fixtures are the default proof lane.
- Live `codex exec` runs must be opt-in with `--live-run` and should warn that they require an
  isolated worktree or scratch clone.
- Keep eval assertions tied to visible outputs such as JSON fields, script exit codes, catalog
  entries, or route matches.

## Output Checks

- Report package coverage by surface, not just files changed.
- Distinguish Codex-only updates from mirrored host-neutral skill behavior.
- Run the package audit, owning skill evals, Codex skill check, AI inventory check, and route linter.
