# AI Assistant Resources

This document is the master index for AI guidance in the Meridian repository. It now treats repository orientation as a first-class step before specialist task guidance.

---

## Quick Start

| Task | Start Here | Deep Dive |
|------|-----------|-----------|
| Any task | [`CLAUDE.md`](https://github.com/rodoHasArrived/Meridian/blob/main/CLAUDE.md) | Root commands, conventions, and architecture |
| Fast repo orientation | [`navigation/README.md`](navigation/README.md) | [`generated/repo-navigation.md`](generated/repo-navigation.md) |
| Before any change | [`ai-known-errors.md`](ai-known-errors.md) | Prevention checklists |
| GitHub or Claude agents | [`agents/README.md`](agents/README.md) | [`.github/agents/`](https://github.com/rodoHasArrived/Meridian/blob/main/.github/agents), [`.claude/agents/`](https://github.com/rodoHasArrived/Meridian/blob/main/.claude/agents) |
| Portable skills | [`skills/README.md`](skills/README.md) | [`.claude/skills/`](https://github.com/rodoHasArrived/Meridian/blob/main/.claude/skills) |
| Codex repo-local skills | [`.codex/skills/README.md`](https://github.com/rodoHasArrived/Meridian/blob/main/.codex/skills/README.md) | [`.codex/skills/`](https://github.com/rodoHasArrived/Meridian/tree/main/.codex/skills) |
| Copilot setup | [`copilot/instructions.md`](copilot/instructions.md) | [`.github/copilot-instructions.md`](https://github.com/rodoHasArrived/Meridian/blob/main/.github/copilot-instructions.md) |

---

## Navigation First

For large-repo tasks, assistants should orient in this order:

1. Read [`navigation/README.md`](navigation/README.md) for the routing workflow.
2. Read the generated repo map in [`generated/repo-navigation.md`](generated/repo-navigation.md).
3. If MCP is available, use the repo-navigation resources and tools instead of broad recursive searching.
4. Only then move into specialist guides such as provider, storage, testing, WPF, or documentation instructions.

The generated navigation artifacts are the canonical orientation surface:

| Artifact | Purpose |
|----------|---------|
| [`generated/repo-navigation.json`](generated/repo-navigation.json) | Machine-readable repo map for MCP tools/resources and other AI surfaces |
| [`generated/repo-navigation.md`](generated/repo-navigation.md) | Human-readable digest of subsystems, routes, symbols, and dependency highlights |
| [`navigation/README.md`](navigation/README.md) | Hand-authored routing workflow and usage guidance |

---

## Resource Hierarchy

### Tier 1: Root Context

| File | Purpose |
|------|---------|
| [`CLAUDE.md`](https://github.com/rodoHasArrived/Meridian/blob/main/CLAUDE.md) | Master project context, architecture, commands, and conventions |
| [`ai-known-errors.md`](ai-known-errors.md) | Canonical registry of recurring AI mistakes and prevention checklists |

### Tier 2: Navigation and Routing

| File | Purpose |
|------|---------|
| [`navigation/README.md`](navigation/README.md) | How assistants should orient and route work across Meridian |
| [`generated/repo-navigation.md`](generated/repo-navigation.md) | Generated subsystem, route, symbol, and dependency digest |
| [`generated/repo-navigation.json`](generated/repo-navigation.json) | Canonical machine-readable navigation dataset |

### Tier 3: Specialized Guides

Located mainly in `docs/ai/claude/`.

| File | Domain |
|------|--------|
| [`claude/CLAUDE.providers.md`](claude/CLAUDE.providers.md) | Provider implementation |
| [`claude/CLAUDE.storage.md`](claude/CLAUDE.storage.md) | Storage system |
| [`claude/CLAUDE.fsharp.md`](claude/CLAUDE.fsharp.md) | F# interop |
| [`claude/CLAUDE.testing.md`](claude/CLAUDE.testing.md) | Testing |
| [`claude/CLAUDE.actions.md`](claude/CLAUDE.actions.md) | CI/CD workflows |
| [`claude/CLAUDE.repo-updater.md`](claude/CLAUDE.repo-updater.md) | Audit tooling |

### Tier 4: Agents, Skills, and Prompts

| Resource | Purpose |
|----------|---------|
| [`agents/README.md`](agents/README.md) | GitHub and Claude agent overview, including navigation agents |
| [`skills/README.md`](skills/README.md) | Portable Agent Skills overview |
| [`prompts/README.md`](prompts/README.md) | Prompt catalog and usage guide |
| [`.codex/skills/README.md`](https://github.com/rodoHasArrived/Meridian/blob/main/.codex/skills/README.md) | Codex repo-local skill catalog |

### Tier 5: Auto-Applied Instructions

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
| Repo orientation | [`generated/repo-navigation.json`](generated/repo-navigation.json) |
| Agent catalog | [`agents/README.md`](agents/README.md) |
| Portable skill catalog | [`skills/README.md`](skills/README.md) |
| Codex skill catalog | [`.codex/skills/README.md`](https://github.com/rodoHasArrived/Meridian/blob/main/.codex/skills/README.md) |
| Project overview & conventions | [`CLAUDE.md`](https://github.com/rodoHasArrived/Meridian/blob/main/CLAUDE.md) |
| Error prevention | [`ai-known-errors.md`](ai-known-errors.md) |

### Adding a New AI Resource

1. Put the file in the correct tool-owned directory when required.
2. Update the corresponding navigation entry in `docs/ai/*/README.md`.
3. Regenerate [`generated/repo-navigation.json`](generated/repo-navigation.json) and [`generated/repo-navigation.md`](generated/repo-navigation.md) if routing truth changed.
4. Cross-link related resources when the new file changes workflow or ownership.

---

*Last Updated: 2026-03-31*
