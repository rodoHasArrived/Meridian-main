# AI Assistant Resources

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-06-16

This document is the master index for AI guidance in the Meridian repository. It now treats repository orientation as a first-class step before specialist task guidance.

---

## Rebuild Contract Envelope

For every AI/systems task in this repository, use this canonical order:

1. `docs/ai/navigation/README.md`
2. `docs/ai/generated/repo-navigation.md`
3. `docs/ai/assistant-workflow-contract.md`
4. MDIF sources (`docs/architecture/meridian-development-intelligence-framework.md`, `docs/domain/README.md`, and `docs/ai/context/README.md`) for broad generation, domain modeling, workflow design, or architecture-sensitive work
5. `docs/engineering/README.md` or `docs/product/README.md` / `docs/operators/README.md` as applicable
6. `docs/documentation-ownership.md` for archive/generation ownership
7. `docs/documentation-inventory.md` to update migration state and phase notes
8. For memory-aware Codex tasks, inspect `.codex/memory/index.yml`; route named scopes through
   `.codex/memory/tasks/<task-id>.yml`; route long-running work through
   `.codex/memory/goals/<goal-id>.yml`; load only selected entries; emit compact receipts; and keep
   canonical docs, source, tests, scripts, scoped `AGENTS.md`, and selected skills authoritative
   over memory.
9. Targeted validation command set (`check-*` and structure checks), including
   `python build/scripts/docs/check-codex-memory.py --summary` after Codex memory changes

Classify AI/doc changes in this rebuild model:

- `canonical`: canonical lanes in `docs/ai/` and host shims (CLAUDE/Codex/agent docs)
- `source-material`: historical notes or experiments used for extraction only
- `generated`: `docs/ai/generated/*` (regenerate, do not hand-edit)
- `archive`: retired guidance with replacement index entries under `archive/docs/`

## Quick Start

| Task | Start Here | Deep Dive |
| --- | --- | --- |
| Engineering/agent work | [`engineering/README.md`](../engineering/README.md) | [`docs`](.) lane map, module map, and source/roadmap registry |
| Stakeholder/product planning | [`product/README.md`](../product/README.md) | [`meridian-design-document.md`](../product/meridian-design-document.md) and roadmap-facing status alignment |
| Any task | [`CLAUDE.md`](../../CLAUDE.md) | Root commands, conventions, and architecture |
| Any AI system or automation | [`assistant-workflow-contract.md`](assistant-workflow-contract.md) | Shared provider-agnostic workflow, safety rules, and alignment checklist |
| Rebuild-era AI requirements | [`assistant-workflow-contract.md`](assistant-workflow-contract.md) | Contracted requirements for repo navigation, generated-doc handling, orchestration, token/context discipline, and validation |
| Multi-agent or multi-phase work | [`agent-handoff-checklist.md`](agent-handoff-checklist.md) | Required handoff packet format and token-efficient context boundaries |
| Parallel multi-lane execution | [`parallel-task-manifest-template.md`](parallel-task-manifest-template.md) | Shared lane-ownership manifest to prevent duplicate discovery and overlapping edits |
| Concurrent implementation memory | [`working-memory.md`](working-memory.md) | Task-local ledger for claims, inspected files, assumptions, merge order, drift, and validation reuse |
| Context budget sizing | [`work-modes.md`](work-modes.md) | Lightweight/Standard/Deep-review mode selection and escalation rules |
| Compact context packets | [`agent-handoff-checklist.md`](agent-handoff-checklist.md), [`parallel-task-manifest-template.md`](parallel-task-manifest-template.md), [`work-modes.md`](work-modes.md) | Required vs optional context split, validation-reuse tracking, and summarized evidence budgets |
| AI tooling and validators | [`tooling/README.md`](tooling/README.md) | Script/tool selection, safe usage notes, and narrow proof lanes |
| Local contention-aware .NET validation | [`tooling/README.md`](tooling/README.md) | `buildctl test` local lock, active-process checks, isolated outputs, and `.ai/validation-runs` evidence |
| Hosted targeted testing | [GitHub workflows README](../../.github/workflows/README.md) | Manual `Targeted Test` workflow with curated modes for .NET, browser, docs/source, WPF, route, and desktop-smoke validation |
| Fast repo orientation | [`navigation/README.md`](navigation/README.md) | [`generated/repo-navigation.md`](generated/repo-navigation.md) |
| Source/roadmap documentation sync | [`../source/README.md`](../source/README.md), [`../roadmap/README.md`](../roadmap/README.md) | `build/scripts/docs/validate-source-readmes.py`, `build/scripts/docs/validate-roadmap-registry.py` |
| Before any change | [`ai-known-errors.md`](ai-known-errors.md) | Prevention checklists |
| GitHub or Claude agents | [`agents/README.md`](agents/README.md) | [`.github/agents/`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.github/agents), [`.claude/agents/`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.claude/agents) |
| Portable skills | [`skills/README.md`](skills/README.md) | [`.agents/skills/`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.agents/skills), [`.claude/skills/`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.claude/skills) |
| Codex repo-local skills and profiles | [`codex/README.md`](codex/README.md) | [`.codex/agents/`](https://github.com/rodoHasArrived/Meridian-main/tree/main/.codex/agents), [`.codex/skills/README.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.codex/skills/README.md), [`.codex/skills/`](https://github.com/rodoHasArrived/Meridian-main/tree/main/.codex/skills) |
| Fast Codex task startup | [`codex/quickstart.md`](codex/quickstart.md) | [`codex/advanced-configuration.md`](codex/advanced-configuration.md), [`codex/route-cards.md`](codex/route-cards.md) |
| Codex memory contract | [`codex/memory-system.md`](codex/memory-system.md) | [`.codex/memory/index.yml`](../../.codex/memory/index.yml), task descriptors under `.codex/memory/tasks/*.yml`, goal inventories under `.codex/memory/goals/*.yml`, `python build/scripts/docs/check-codex-memory.py --summary` |
| MDIF context packs | [`context/README.md`](context/README.md) | [`../architecture/meridian-development-intelligence-framework.md`](../architecture/meridian-development-intelligence-framework.md), [`../domain/README.md`](../domain/README.md), and [`exports/README.md`](exports/README.md) |
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
6. For concurrent implementation or a dirty codebase, maintain task-local working memory with
   [`working-memory.md`](working-memory.md) before lanes edit or reuse validation.
7. Select [`work-modes.md`](work-modes.md) level before implementation and escalate when risk grows.
8. Only then move into specialist guides such as provider, storage, testing, WPF, or documentation instructions.

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

## AI Edit Contract (Required)

Before changing any AI/helping-agent guidance:

- Read and update `assistant-workflow-contract.md` first when shared behavior changes.
- Keep host-specific edits (`claude/`, `codex/`, `copilot/`, `agents/`, `skills/`) strictly aligned to the shared contract.
- Do not hand-edit generated AI outputs under `docs/ai/generated/*`; re-run generation command lanes.
- Update this index when adding/removing AI surfaces, entrypoints, checks, or ownership.
- For parallel work, initialize a manifest (`parallel-task-manifest-template.md`) and record lane transitions in `agent-handoff-checklist.md`.
- For concurrent implementation, keep a working-memory ledger (`working-memory.md`) for active
  claims, inspected files, assumptions, merge risks, and validation reuse.
- For lane transitions, include required context, optional context, validation reuse, and unresolved assumptions in the handoff packet.
- End every AI surface edit with matching validation outputs and a short residual-risk note.

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
python build/scripts/docs/check-codex-memory.py --summary
python build/scripts/docs/check-codex-memory.py --task .codex/memory/tasks/example.yml --receipt --summary
python build/scripts/docs/check-codex-memory.py --goal .codex/memory/goals/example.yml --receipt --summary
python -m unittest build.scripts.docs.tests.test_check_codex_memory
python3 build/scripts/docs/check-codex-skills.py --summary
python3 build/scripts/docs/check-ai-inventory.py --summary
```

The memory checker validates `.codex/memory/index.yml`, sourced Markdown entries, task descriptors,
goal inventories, review dates, routing metadata, and unindexed memory files. The inventory checker inventories root assistant entrypoints, Codex configuration, Codex agent profiles,
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
| [`codex/memory-system.md`](codex/memory-system.md) | Codex repo-local memory tiers, routing, promotion, and validation contract |
| [`working-memory.md`](working-memory.md) | Task-local coordination ledger for concurrent AI implementation work |
| [`codex/advanced-configuration.md`](codex/advanced-configuration.md) | Advanced Codex local-client configuration patterns for profiles, providers, project config, hooks, telemetry, notifications, history, and TUI behavior |
| [`codex/route-cards.md`](codex/route-cards.md) | Subsystem cards for Codex routing after generated navigation identifies ownership |
| [`prompts/README.md`](prompts/README.md) | Prompt catalog and usage guide |
| [`../prompts/README.md`](../prompts/README.md) | Provider-agnostic prompt and automation guidance inventory |
| [`.codex/memory/index.yml`](../../.codex/memory/index.yml) | Codex repo-local memory catalog and selective loading metadata |
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
| Local AI maintenance tooling | [`tooling/README.md`](tooling/README.md), `scripts/ai/`, `build/scripts/ai/`, `tools/codex/`, `make/ai.mk` |

Model-routing behavior is docs-only controlled by `model-routing-policy.json`; use `routingRules`, `modelClasses`, and `telemetrySignals` as the canonical policy sections. No alternate mirror files, templates, or env-based fallback file paths are used at runtime.

## AI Contract Coverage Checklist

- Repo navigation: [`navigation/README.md`](navigation/README.md), [`generated/repo-navigation.md`](generated/repo-navigation.md)
- MDIF grounding: [`../architecture/meridian-development-intelligence-framework.md`](../architecture/meridian-development-intelligence-framework.md), [`../domain/README.md`](../domain/README.md), [`context/README.md`](context/README.md)
- Agent edit rules: [`assistant-workflow-contract.md`](assistant-workflow-contract.md), `.codex/skills/_shared/project-context.md`
- Generated-file handling: this contract plus `validate-docs-structure`, `generate-ai-navigation`, and `check-ai-contract-drift` command lanes
- Agent orchestration: [`parallel-task-manifest-template.md`](parallel-task-manifest-template.md), [`agent-handoff-checklist.md`](agent-handoff-checklist.md), [check-ai-contract-drift command](assistant-workflow-contract.md)
- Working memory: [`working-memory.md`](working-memory.md) for active claims, inspected files,
  assumptions, codebase drift, merge order, and validation reuse
- Codex repo-local memory: [`codex/memory-system.md`](codex/memory-system.md) and
  `.codex/memory/index.yml`; validate with `check-codex-memory`
- Token/context management: [`work-modes.md`](work-modes.md), [`tooling/README.md`](tooling/README.md), and one-lane handoff packets in handoff manifest
- Compact evidence transfer: require `required context` vs `optional context` separation plus validation-reuse notes in handoff packets
- Validation procedures: `check-codex-memory`, `check-ai-inventory`, `check-codex-skills`, `check-ai-handoff --strict`, `check-ai-contract-drift`
- Ownership rules: [`../documentation-ownership.md`](../documentation-ownership.md) and this lane’s `assistant-workflow-contract.md`

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

## Legacy Path Migration (High-Traffic)

The following high-traffic files are source material or historical entrypoints only and now point to canonical lanes:

- `docs/AGENTS.md` shim → `docs/README.md`
- `archive/docs/developer/setup.md` and `archive/docs/developer/build-test-run.md` → [start/](../start/README.md) and [engineering/](../engineering/README.md)
- `archive/docs/developer/` and `docs/development/` guidance files → [engineering/README.md](../engineering/README.md)
- `docs/ops`-style quickstart notes and one-off operator snapshots → [operators/README.md](../operators/README.md)
- archived `docs/providers/*` setup, matrix, and provider-reference pages → [operators/provider-*.md](../operators/README.md) and [reference/provider-*.md](../reference/README.md)

---

_Last Updated: 2026-06-16_

## Drift failure remediation

1. Run TODO scan and validation:
   - `python3 build/scripts/docs/scan-todos.py --json-output docs/status/todo-scan-results.json`
   - `python3 build/scripts/docs/validate-todo-registry.py --scan-json docs/status/todo-scan-results.json --registry docs/source/todo-registry.json --enforce-prefix docs/source/`
2. For missing IDs, add a new entry in `docs/source/todo-registry.json` and update the TODO comment with `TODO-ID` and `OWNER` metadata.
3. Run AI contract drift check:
   - `python3 build/scripts/docs/check-ai-contract-drift.py --canonical docs/ai/contract-policy.json --mirror docs/ai/copilot/contract-policy.mirror.json --mirror docs/ai/claude/contract-policy.mirror.json`
4. If drift exists, copy canonical policy content into each mirror path and rerun checks.
