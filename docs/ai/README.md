# AI Assistant Resources

This document is the master index for AI guidance in the Meridian repository. It now treats repository orientation as a first-class step before specialist task guidance.

---

## Quick Start

| Task | Start Here | Deep Dive |
| --- | --- | --- |
| Any task | [`CLAUDE.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/CLAUDE.md) | Root commands, conventions, and architecture |
| Any AI system or automation | [`assistant-workflow-contract.md`](assistant-workflow-contract.md) | Shared provider-agnostic workflow, safety rules, and alignment checklist |
| Multi-agent or multi-phase work | [`agent-handoff-checklist.md`](agent-handoff-checklist.md) | Required handoff packet format and token-efficient context boundaries |
| Parallel multi-lane execution | [`parallel-task-manifest-template.md`](parallel-task-manifest-template.md) | Shared lane-ownership manifest to prevent duplicate discovery and overlapping edits |
| Context budget sizing | [`work-modes.md`](work-modes.md) | Lightweight/Standard/Deep-review mode selection and escalation rules |
| Fast repo orientation | [`navigation/README.md`](navigation/README.md) | [`generated/repo-navigation.md`](generated/repo-navigation.md) |
| Source/roadmap documentation sync | [`../source/README.md`](../source/README.md), [`../roadmap/README.md`](../roadmap/README.md) | `build/scripts/docs/validate-source-readmes.py`, `build/scripts/docs/validate-roadmap-registry.py` |
| Before any change | [`ai-known-errors.md`](ai-known-errors.md) | Prevention checklists |
| GitHub or Claude agents | [`agents/README.md`](agents/README.md) | [`.github/agents/`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.github/agents), [`.claude/agents/`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.claude/agents) |
| Portable skills | [`skills/README.md`](skills/README.md) | [`.agents/skills/`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.agents/skills), [`.claude/skills/`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.claude/skills) |
| Codex repo-local skills and profiles | [`codex/README.md`](codex/README.md) | [`.codex/agents/`](https://github.com/rodoHasArrived/Meridian-main/tree/main/.codex/agents), [`.codex/skills/README.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.codex/skills/README.md), [`.codex/skills/`](https://github.com/rodoHasArrived/Meridian-main/tree/main/.codex/skills) |
| Fast Codex task startup | [`codex/quickstart.md`](codex/quickstart.md) | [`codex/route-cards.md`](codex/route-cards.md) |
| Copilot setup | [`copilot/instructions.md`](copilot/instructions.md) | [`.github/copilot-instructions.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.github/copilot-instructions.md) |

---

## Navigation First

For large-repo tasks, assistants should orient in this order:

1. Read [`navigation/README.md`](navigation/README.md) for the routing workflow.
2. Read the generated repo map in [`generated/repo-navigation.md`](generated/repo-navigation.md).
3. If MCP is available, use the repo-navigation resources and tools instead of broad recursive searching.
4. If the task spans multiple agents or validation phases, use [`agent-handoff-checklist.md`](agent-handoff-checklist.md)
   before switching context or specialist lanes.
5. If two or more lanes run in parallel, initialize [`parallel-task-manifest-template.md`](parallel-task-manifest-template.md)
   and record inspected files per lane.
6. Select [`work-modes.md`](work-modes.md) level before implementation and escalate when risk grows.
7. Only then move into specialist guides such as provider, storage, testing, WPF, or documentation instructions.

The generated navigation artifacts are the canonical orientation surface:

| Artifact | Purpose |
| --- | --- |
| [`generated/repo-navigation.json`](generated/repo-navigation.json) | Machine-readable repo map for MCP tools/resources and other AI surfaces |
| [`generated/repo-navigation.md`](generated/repo-navigation.md) | Human-readable digest of subsystems, routes, symbols, and dependency highlights |
| [`generated/recent-changes.md`](generated/recent-changes.md) | Rolling source-file change digest for sprint-level debugging context |
| [`navigation/README.md`](navigation/README.md) | Hand-authored routing workflow and usage guidance |

For source edits, assistants must also read the nearest registered source README and use
`docs/source/data/source-modules.yml` to identify module ownership, roadmap traceability, diagrams,
TODOs, and validation commands.

---

## Resource Hierarchy

### Tier 1: Root Context

| File | Purpose |
| --- | --- |
| [`CLAUDE.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/CLAUDE.md) | Master project context, architecture, commands, and conventions |
| [`assistant-workflow-contract.md`](assistant-workflow-contract.md) | Shared provider-agnostic execution flow, safety rules, system inventory, and alignment checklist |
| [`ai-known-errors.md`](ai-known-errors.md) | Canonical registry of recurring AI mistakes and prevention checklists |

Validate AI catalog consistency with:

```bash
python3 build/scripts/docs/check-codex-skills.py --summary
python3 build/scripts/docs/check-ai-inventory.py --summary
```

The checker inventories root assistant entrypoints, Codex configuration, Codex agent profiles,
Codex desktop prompts, Codex validation checklists, Codex `AGENTS.md`, Codex quality tools under
`tools/codex/`, Claude configuration, checked-in Claude plugin packages under `.claude/plugins/`, Agent Skills-compatible
packages and shared context, Copilot instructions, agents, skills, prompts, repository prompt
documentation under `docs/prompts/`, local AI maintenance scripts under `scripts/ai/`, AI workflow
files, `docs/ai/`, MCP prompt/resource/tool surfaces, optional IDE/provider assistant entrypoints for
Cursor, Windsurf, Continue, Cline, Roo, and Gemini if they are introduced, and stale canonical
GitHub documentation links. It also flags active AI docs that still point at retired GitHub Actions
workflow paths. Its Markdown and JSON reports are safe to share because they use a portable
repository identity instead of local absolute paths.

### Tier 2: Navigation and Routing

| File | Purpose |
| --- | --- |
| [`navigation/README.md`](navigation/README.md) | How assistants should orient and route work across Meridian |
| [`generated/repo-navigation.md`](generated/repo-navigation.md) | Generated subsystem, route, symbol, and dependency digest |
| [`generated/repo-navigation.json`](generated/repo-navigation.json) | Canonical machine-readable navigation dataset |
| [`generated/recent-changes.md`](generated/recent-changes.md) | Generated rolling source-file change summary |

### Tier 3: Specialized Guides

Located mainly in `docs/ai/claude/`.

| File | Domain |
| --- | --- |
| [`claude/CLAUDE.providers.md`](claude/CLAUDE.providers.md) | Provider implementation |
| [`claude/CLAUDE.storage.md`](claude/CLAUDE.storage.md) | Storage system |
| [`claude/CLAUDE.fsharp.md`](claude/CLAUDE.fsharp.md) | F# interop |
| [`claude/CLAUDE.testing.md`](claude/CLAUDE.testing.md) | Testing |
| [`claude/CLAUDE.actions.md`](claude/CLAUDE.actions.md) | CI/CD workflows |
| [`claude/CLAUDE.repo-updater.md`](claude/CLAUDE.repo-updater.md) | Audit tooling |

### Tier 4: Agents, Skills, and Prompts

| Resource | Purpose |
| --- | --- |
| [`agents/README.md`](agents/README.md) | GitHub and Claude agent overview, including navigation agents |
| [`skills/README.md`](skills/README.md) | Portable Agent Skills overview for `.agents/skills/` and `.claude/skills/` |
| [`codex/README.md`](codex/README.md) | Codex repo-local skill workflow, execution gates, and validation tooling |
| [`codex/quickstart.md`](codex/quickstart.md) | Compact Codex startup checklist, proof matrix, read budget, and dirty-worktree protocol |
| [`codex/route-cards.md`](codex/route-cards.md) | Subsystem cards for Codex routing after generated navigation identifies ownership |
| [`prompts/README.md`](prompts/README.md) | Prompt catalog and usage guide |
| [`../prompts/README.md`](../prompts/README.md) | Provider-agnostic prompt and automation guidance inventory |
| `scripts/ai/`, `tools/codex/` | Local AI maintenance scripts and Codex quality tools |
| [`.claude/plugins/`](../../.claude/plugins) | Checked-in Claude plugin packages with plugin manifests, agents, and skills |
| [`.codex/agents/`](https://github.com/rodoHasArrived/Meridian-main/tree/main/.codex/agents) | Codex specialist agent-profile TOML files |
| [`.codex/skills/README.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.codex/skills/README.md) | Codex repo-local skill catalog |

### Tier 5: Auto-Applied Instructions

| Resource | Purpose |
| --- | --- |
| [`instructions/README.md`](instructions/README.md) | Index for path-specific instruction files |
| [`.github/instructions/`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.github/instructions) | Auto-applied Copilot instruction files |
| [`.github/copilot-instructions.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.github/copilot-instructions.md) | Repository-wide Copilot guidance |

---

## Maintenance Notes

### Canonical Sources

| Topic | Canonical Source |
| --- | --- |
| Cross-provider AI workflow | [`assistant-workflow-contract.md`](assistant-workflow-contract.md) |
| Repo orientation | [`generated/repo-navigation.json`](generated/repo-navigation.json) |
| Agent catalog | [`agents/README.md`](agents/README.md) |
| Portable skill catalog | [`skills/README.md`](skills/README.md), [`.agents/skills/`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.agents/skills) |
| Codex skill catalog | [`codex/README.md`](codex/README.md), [`.codex/skills/README.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.codex/skills/README.md) |
| Claude plugin packages | [`skills/README.md`](skills/README.md), [`agents/README.md`](agents/README.md), [`.claude/plugins/`](https://github.com/rodoHasArrived/Meridian-main/tree/main/.claude/plugins) |
| Project overview & conventions | [`CLAUDE.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/CLAUDE.md) |
| Error prevention | [`ai-known-errors.md`](ai-known-errors.md) |
| AI inventory drift checks | [`assistant-workflow-contract.md`](assistant-workflow-contract.md), `build/scripts/docs/check-ai-inventory.py` |
| Codex agent profiles | [`codex/README.md`](codex/README.md), [`agents/README.md`](agents/README.md), [`.codex/agents/`](https://github.com/rodoHasArrived/Meridian-main/tree/main/.codex/agents) |
| Model routing policy | [`model-routing-policy.json`](model-routing-policy.json) |
| Roadmap/source registry sync | [`../roadmap/README.md`](../roadmap/README.md), [`../source/README.md`](../source/README.md) |
| Prompt and automation guidance | [`prompts/README.md`](prompts/README.md), [`../prompts/README.md`](../prompts/README.md) |
| Local AI maintenance tooling | `scripts/ai/`, `tools/codex/`, `make/ai.mk` |

Model-routing behavior is docs-only controlled by `model-routing-policy.json`; no alternate mirror files, templates, or env-based fallback file paths are used at runtime.

### Adding a New AI Resource

1. Put the file in the correct tool-owned directory when required.
2. Update the corresponding navigation entry in `docs/ai/*/README.md`, including
   [`agents/README.md`](agents/README.md) for new agent definitions or Codex agent profiles.
3. Update [`assistant-workflow-contract.md`](assistant-workflow-contract.md) when the new resource
   adds or removes an AI system, provider, automation, or cross-provider rule.
4. Run `python3 build/scripts/docs/check-ai-inventory.py --summary` and fix any catalog drift.
5. Regenerate [`generated/repo-navigation.json`](generated/repo-navigation.json), [`generated/repo-navigation.md`](generated/repo-navigation.md), and [`generated/recent-changes.md`](generated/recent-changes.md) if routing truth or recent-change context changed.
6. Cross-link related resources when the new file changes workflow or ownership.

### Optional IDE Or Provider Surfaces

`build/scripts/docs/check-ai-inventory.py` watches common assistant entrypoints that are not
currently supported by tracked files: Cursor, Windsurf, Continue, Cline, Roo, and Gemini. If one of
those files is added, document the exact entrypoint in this index and
[`assistant-workflow-contract.md`](assistant-workflow-contract.md) instead of relying on a
tool-specific rule file to carry shared Meridian policy.

---

_Last Updated: 2026-05-28_

## Drift failure remediation

1. Run TODO scan and validation:
   - `python3 build/scripts/docs/scan-todos.py --json-output docs/status/todo-scan-results.json`
   - `python3 build/scripts/docs/validate-todo-registry.py --scan-json docs/status/todo-scan-results.json --registry docs/source/todo-registry.json --enforce-prefix docs/source/`
2. For missing IDs, add a new entry in `docs/source/todo-registry.json` and update the TODO comment with `TODO-ID` and `OWNER` metadata.
3. Run AI contract drift check:
   - `python3 build/scripts/docs/check-ai-contract-drift.py --canonical docs/ai/contract-policy.json --mirror docs/ai/copilot/contract-policy.mirror.json --mirror docs/ai/claude/contract-policy.mirror.json`
4. If drift exists, copy canonical policy content into each mirror path and rerun checks.
