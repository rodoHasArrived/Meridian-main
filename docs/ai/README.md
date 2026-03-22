# AI Assistant Resources

This document is the **master index** for AI assistant guidance in the Meridian repository. It maps the major AI-related resources, explains how they fit together, and provides a simple reading order by task type.

---

## Quick Start: Which File Do I Read?

| Task | Start Here | Deep Dive |
|------|-----------|-----------|
| Any task | [`CLAUDE.md`](https://github.com/rodoHasArrived/Meridian/blob/main/CLAUDE.md) | This file for the full resource map |
| Before any change | [`ai-known-errors.md`](ai-known-errors.md) | Prevention checklists |
| GitHub agent work | [`agents/README.md`](agents/README.md) | [`.github/agents/`](https://github.com/rodoHasArrived/Meridian/blob/main/.github/agents) |
| Agent Skills work | [`skills/README.md`](skills/README.md) | [`.claude/skills/`](https://github.com/rodoHasArrived/Meridian/blob/main/.claude/skills) |
| Copilot setup | [`copilot/instructions.md`](copilot/instructions.md) | [`.github/copilot-instructions.md`](https://github.com/rodoHasArrived/Meridian/blob/main/.github/copilot-instructions.md) |
| Prompt-driven work | [`prompts/README.md`](prompts/README.md) | [`.github/prompts/`](https://github.com/rodoHasArrived/Meridian/blob/main/.github/prompts) |
| Documentation edits | [`instructions/README.md`](instructions/README.md) | [`agents/README.md`](agents/README.md) |

---

## Directory Structure

The repository tree below is maintained automatically by the README Tree Sync workflow so the AI index stays aligned with the live repo layout.

<!-- readme-tree start -->
<!-- readme-tree end -->

**Tool-owned locations** that are part of the AI guidance surface:

```text
.github/
├── agents/                      # GitHub Copilot agent definitions
├── instructions/                # Path-specific Copilot rules
├── prompts/                     # Copilot prompt templates
└── copilot-instructions.md      # Repository-wide Copilot instructions

.claude/
├── agents/                      # Claude Code agent definitions
└── skills/                      # Portable Agent Skills packages, references, evals, and scripts
```

---

## Resource Hierarchy

### Tier 1: Root Context

| File | Purpose |
|------|---------|
| [`CLAUDE.md`](https://github.com/rodoHasArrived/Meridian/blob/main/CLAUDE.md) | Master project context, architecture, commands, and conventions |
| [`ai-known-errors.md`](ai-known-errors.md) | Canonical registry of recurring AI mistakes and prevention checklists |

### Tier 2: Specialized Guides

Located in `docs/ai/claude/`.

| File | Domain |
|------|--------|
| [`CLAUDE.providers.md`](claude/CLAUDE.providers.md) | Provider implementation |
| [`CLAUDE.storage.md`](claude/CLAUDE.storage.md) | Storage system |
| [`CLAUDE.fsharp.md`](claude/CLAUDE.fsharp.md) | F# interop |
| [`CLAUDE.testing.md`](claude/CLAUDE.testing.md) | Testing |
| [`CLAUDE.actions.md`](claude/CLAUDE.actions.md) | CI/CD workflows |
| [`CLAUDE.repo-updater.md`](claude/CLAUDE.repo-updater.md) | Audit tooling |

### Tier 3: Agents, Skills, and Prompts

| Resource | Purpose |
|----------|---------|
| [`agents/README.md`](agents/README.md) | GitHub and Claude agent overview |
| [`skills/README.md`](skills/README.md) | Portable Agent Skills overview |
| [`prompts/README.md`](prompts/README.md) | Prompt catalog and usage guide |
| [`.github/agents/`](https://github.com/rodoHasArrived/Meridian/blob/main/.github/agents) | GitHub Copilot agent definition files |
| [`.claude/skills/`](https://github.com/rodoHasArrived/Meridian/blob/main/.claude/skills) | Portable Agent Skills packages, references, evals, and scripts |

### Tier 4: Auto-Applied Instructions

| Resource | Purpose |
|----------|---------|
| [`instructions/README.md`](instructions/README.md) | Index for path-specific instruction files |
| [`.github/instructions/`](https://github.com/rodoHasArrived/Meridian/blob/main/.github/instructions) | Auto-applied Copilot instruction files |
| [`.github/copilot-instructions.md`](https://github.com/rodoHasArrived/Meridian/blob/main/.github/copilot-instructions.md) | Repository-wide Copilot guidance |

---

## Maintenance Notes

### Canonical Sources

| Topic | Canonical Source |
|-------|------------------|
| Code review lenses | [`.github/agents/code-review-agent.md`](https://github.com/rodoHasArrived/Meridian/blob/main/.github/agentscode-review-agent.md) |
| Agent catalog | [`agents/README.md`](agents/README.md) |
| Skill catalog | [`skills/README.md`](skills/README.md) |
| Prompt catalog | [`prompts/README.md`](prompts/README.md) |
| Project overview & conventions | [`CLAUDE.md`](https://github.com/rodoHasArrived/Meridian/blob/main/CLAUDE.md) |
| Error prevention | [`ai-known-errors.md`](ai-known-errors.md) |

### Adding a New AI Resource

1. Put the file in the correct tool-owned directory (`.github/` or `.claude/`) when required.
2. Add or update the corresponding navigation entry in `docs/ai/*/README.md`.
3. Cross-link related resources when the new file changes workflow or ownership.
4. Update this master index if the new resource changes how contributors should navigate the AI docs.

---

*Last Updated: 2026-03-20*
