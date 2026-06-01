# Repo Maintenance Prompts

Meridian has several AI guidance hosts. This page explains where each active
maintenance prompt belongs and how to avoid duplicate guidance.

## Active Guidance Map

| Surface | Role |
| --- | --- |
| `AGENTS.md` | Short compatibility shim for agents that look for root instructions |
| `CLAUDE.md` | High-signal Claude-compatible repository guide |
| `.codex/skills/_shared/project-context.md` | Codex skill grounding and current project context |
| `.agents/skills/_shared/project-context.md` | Agent skill grounding mirror |
| `.github/agents/*.md` | GitHub/Copilot agent entrypoints |
| `.github/instructions/*.md` | Auto-applied Copilot instruction files |
| `.github/copilot-instructions.md` | Repository-wide Copilot guidance |
| `.agents/skills/*/SKILL.md` | Agent skill packages |
| `.codex/skills/*/SKILL.md` | Codex repo-local skill packages |
| `docs/ai/` | AI resource index, generated navigation, prompt, instruction, and skill docs |

## Consolidation Rules

- Put durable project facts in `.codex/skills/_shared/project-context.md` and
  mirror them to `.agents/skills/_shared/project-context.md` when both hosts
  need the same context.
- Keep `AGENTS.md` and `CLAUDE.md` short. Link to maintained docs instead of
  pasting generated repository trees.
- Put developer workflow commands in `archive/docs/developer/`; point prompts there.
- Put archive and cleanup rules in `docs/operations/cleanup-and-maintenance.md`;
  point prompts there.
- Put design guidance in `docs/engineering/README.md` under the UI and
  workstation design rules lane, and point prompts there instead of creating
  screen-specific design notes.
- For historical design-system details, use
  `archive/docs/design/design-system-usage.md` with an explicit reason and a
  temporary replacement pointer if needed.
- Archive obsolete maintenance notes under `archive/docs/` with a short reason
  and replacement link.

## Current Path Rule

Use repository-relative paths in committed prompt guidance. When an automation
run needs a concrete machine path, keep it in local run notes or automation
memory instead of active prompt docs.
