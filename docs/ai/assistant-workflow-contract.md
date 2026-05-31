# Provider-Agnostic AI Development Contract

This contract is the shared operating standard for AI-assisted development in Meridian. It applies
to Codex, Claude, GitHub Copilot, MCP clients, reusable prompt templates, CI prompt generation, and
manual assistant sessions, including local AI maintenance tooling used by automations.

Use this file when updating assistant-specific instructions so the project keeps one common rule
set instead of drifting into conflicting provider-specific guidance.

---

## Supported AI Systems

The current repository evidence supports these AI surfaces:

| System or surface | Repository assets | Primary role |
| --- | --- | --- |
| Root assistant compatibility | `AGENTS.md`, `CLAUDE.md` | Root-level project context and compatibility for agents that read conventional files |
| Codex | `.codex/config.toml`, `.codex/environments/`, `.codex/agents/`, `.codex/AGENTS.md`, `.codex/skills/`, `.codex/skills/*/agents/openai.yaml`, `.codex/prompts/`, `.codex/checklists/`, `tools/codex/` | Repo-local agent profiles, specialist skills, OpenAI/Codex metadata, environment entrypoints, desktop prompts, validation checklists, and Codex PowerShell scanners/generators |
| Agent Skills-compatible hosts | `.agents/skills/`, `.agents/skills/_shared/project-context.md`, `.agents/skills/*/agents/openai.yaml` | Portable `open-agent-skills-v1` packages and host-neutral skill metadata |
| Claude / Claude Code | `.claude/settings.json`, `.claude/settings.local.json`, `.claude/agents/`, `.claude/skills/`, `.claude/plugins/` | Claude agent definitions, portable skill packages, checked-in plugin packages, hooks, permissions, and model selection |
| GitHub Copilot | `.github/copilot-instructions.md`, `.github/instructions/`, `.github/agents/`, `.github/prompts/` | Repository-wide coding-agent guidance, path instructions, agents, and reusable prompts |
| MCP-compatible clients | `src/Meridian.Mcp/`, `docs/ai/navigation/README.md`, `docs/ai/generated/repo-navigation.json` | Tool, prompt, resource, and navigation access for any MCP client |
| Workflow guidance | `.github/workflows/README.md`, `docs/development/github-actions-summary.md`, `docs/development/github-actions-testing.md` | Current build, test, publish, and maintenance workflow guidance |
| Reusable prompt templates | `.github/prompts/`, `docs/prompts/`, `docs/ai/prompts/README.md` | Model-agnostic prompts for Copilot Chat, Claude Code, ChatGPT, automation runs, and manual assistant sessions |
| Local AI maintenance tooling | `scripts/ai/`, `tools/codex/`, `make/ai.mk` | Provider-agnostic maintenance lanes, local AI setup/cleanup helpers, and Codex-specific quality scans |
| Shared AI documentation | `docs/ai/`, `.codex/skills/_shared/project-context.md`, `.claude/skills/_shared/project-context.md`, `.agents/skills/_shared/project-context.md` | Human-readable indexes, routing rules, known-error prevention, and shared project grounding |

The AI inventory checker also watches optional IDE/provider assistant entrypoints such as
`.cursor/`, `.windsurf/`, `.continue/`, `.cline/`, `.roo/`, `.cursorrules`, `.windsurfrules`,
`.clinerules`, `.roomodes`, `GEMINI.md`, and `.gemini/`. No tracked Cursor, Windsurf, Continue,
Cline, Roo, or Gemini instruction surface was found during the 2026-05-25 scan. Add one only when
there is an actual tooling need, then list the exact entrypoint in this contract and
`docs/ai/README.md`.

---

## Universal Execution Flow

Every assistant and automation should use the same high-level flow:

1. **Read the request literally.** Restate the desired outcome and identify acceptance criteria.
2. **Orient before broad search.** Start with `docs/ai/navigation/README.md` and
   `docs/ai/generated/repo-navigation.md`. If MCP is available, prefer the repo-navigation tools
   and resources before broad recursive search.
3. **Load the nearest specialist surface.** Use the relevant Codex skill, Claude skill or agent,
   Copilot agent, prompt template, path instruction, or MCP tool based on the routed subsystem.
   If the task crosses multiple subsystems, requires an approval gate or operator sign-off, or
   needs a structured briefing with trace/evidence retention, use the repository docs, skills,
   prompts, and scripts that currently own that workflow instead of inventing a new surface.
4. **Use a shared handoff format.** For multi-agent or multi-phase work, use
   [`agent-handoff-checklist.md`](agent-handoff-checklist.md) as the required compact handoff packet
   between specialist lanes.
5. **Declare mode and parallel ownership up front.** Select a mode from [`work-modes.md`](work-modes.md)
   and, for parallel lanes, initialize [`parallel-task-manifest-template.md`](parallel-task-manifest-template.md)
   before implementation so ownership and context boundaries stay explicit.
6. **Preserve architecture boundaries.** Follow the current shared-contract-first operator UI framing,
   keep visible navigation to `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`,
   `Data`, and `Settings`, and treat legacy `Research`, `Data Operations`, and `Governance`
   WPF names as legacy workspace aliases rather than new root workspaces.
   **No mobile development lane:** do not create mobile applications, mobile-specific product
   surfaces, native iOS/Android clients, MAUI clients, React Native clients, Flutter clients, or
   mobile-first workflows. Existing responsive browser checks may continue only as validation for
   the browser workstation.
7. **Make the smallest safe change.** Avoid speculative rewrites, fake providers, unused agents,
   broad cleanup, and unrelated formatting churn.
8. **Validate narrowly first.** Run the smallest build, test, docs, or skill-validation command
   that covers the touched surface; expand only when the change risk justifies it.
9. **Synchronize docs and AI catalogs.** When a behavior, workflow, prompt, skill, or agent changes,
   update the nearest `docs/ai/*/README.md` index and any mirrored host surfaces that teach the
   same workflow.
10. **Report evidence.** Summaries must include what changed, why, affected files, validation
   commands, and any residual risks.

## Source Documentation And Roadmap Sync

When editing `src/**`, assistants must:

1. Read the nearest `src/**/README.md`.
2. Read `docs/architecture/module-map.md` before changing dependencies or boundaries.
3. Identify the module ID from `docs/source/data/source-modules.yml`.
4. Link meaningful feature, workflow, or behavior changes to a roadmap item ID.
5. Update the nearest source README when behavior, workflow, validation command, module boundary,
   diagram, or TODO scope changes.
6. Update `docs/source/data/source-modules.yml` when module ownership, validation, roadmap mapping,
   layer, diagram, or README path changes.
7. Update `docs/source/data/source-todos.yml` for module-local follow-up.
8. Update `docs/source/data/diagram-index.yml` when adding or replacing diagrams.
9. Run `python3 build/scripts/docs/mark-stale-docs.py --write --summary` when registered source
   code changes, then target `--stale-only` README sync/render commands when only outdated docs
   should be updated.
10. Never hand-edit generated docs outside approved generated blocks.
11. Run the narrowest validation command and report the result.

---

## Safety Rules Shared By All Providers

- Do not add or expose secrets, API keys, tokens, paid-service credentials, or local-only absolute
  paths.
- Do not remove AI system support unless the obsolete surface is verified and documented.
- Do not introduce new AI providers, tools, agents, models, or dependencies without repository
  evidence that they are needed.
- Do not pursue mobile app development unless a future roadmap change explicitly creates and
  documents a mobile product lane; keep operator UI work on the active browser workstation and WPF
  desktop surfaces.
- Do not duplicate long rule sets across provider-specific files. Link to the shared source of
  truth and keep host-specific files focused on host mechanics.
- Do not embed full repository trees in host-specific guidance. Link to
  `docs/ai/generated/repo-navigation.*` or `docs/generated/repository-structure.md` instead.
- Do not mix AI orchestration, tool logic, prompt management, or knowledge indexing into WPF views.
  Put that logic in services, agents, scripts, utilities, configuration, or documentation.
- Respect existing worktree changes. Treat unrelated edits as user-owned unless explicitly told to
  revert them.
- For generated AI artifacts, update the generator or source input when one exists; do not hand-edit
  generated outputs unless the workflow explicitly allows it.

---

## Source-Of-Truth Map

| Topic | Source of truth | Mirrors or consumers |
| --- | --- | --- |
| Documentation front door and ownership | `docs/README.md`, `docs/documentation-ownership.md`, `docs/documentation-inventory.md` | Root `README.md`, `AGENTS.md`, `CLAUDE.md`, assistant indexes |
| Project framing, commands, and architecture | `docs/start/README.md`, `docs/product/README.md`, `docs/engineering/README.md`, `CLAUDE.md`, `.codex/skills/_shared/project-context.md`, `.claude/skills/_shared/project-context.md`, `.agents/skills/_shared/project-context.md` | `AGENTS.md`, Copilot instructions, skills, agents |
| Repo routing and subsystem ownership | `docs/ai/generated/repo-navigation.json`, `docs/ai/generated/recent-changes.md`, `docs/ai/navigation/README.md` | MCP navigation resources/tools, generated markdown, navigation agents and skills |
| Codex task startup and proof routing | `docs/ai/codex/quickstart.md`, `docs/ai/codex/route-cards.md` | Root `AGENTS.md`, `.codex/skills/README.md`, Codex specialist skills |
| Roadmap and source documentation truth | `docs/roadmap/data/*.yml`, `docs/source/data/*.yml`, registered `src/**/README.md` | Generated roadmap/source docs, source README blocks, AI source sync rules |
| Source documentation staleness | `docs/source/generated/stale-docs.json`, `docs/source/generated/source-hash-manifest.json` | Stale-only README sync/render commands, source-doc hash validation |
| Known AI mistakes | `docs/ai/ai-known-errors.md` | Copilot instructions, Claude/Codex skills, manual or local docs intake |
| Codex skill catalog | `.codex/skills/README.md`, `docs/ai/skills/README.md` | Codex UI metadata in `agents/openai.yaml` |
| Codex agent profiles | `.codex/agents/*.toml`, `docs/ai/codex/README.md`, `docs/ai/agents/README.md` | Codex specialist profile routing and task entrypoints |
| Codex prompts and validation checklists | `.codex/prompts/`, `.codex/checklists/`, `docs/ai/codex/README.md` | Desktop implementation prompts, MVVM/resource/safe-refactor checklists, and Codex workflow guidance |
| Agent Skills-compatible package catalog | `.agents/skills/`, `docs/ai/skills/README.md` | Host-neutral portable Agent Skill packages and `agents/openai.yaml` metadata |
| Claude agent and skill catalog | `.claude/agents/`, `.claude/skills/`, `docs/ai/agents/README.md`, `docs/ai/skills/README.md` | Portable skill packages and Claude settings |
| Claude plugin packages | `.claude/plugins/csharp-dotnet-development/`, `.claude/plugins/frontend-web-dev/` | Plugin manifests, plugin-contributed agents, plugin-contributed skills, `docs/ai/agents/README.md`, and `docs/ai/skills/README.md` |
| Copilot agents, prompts, and path rules | `.github/agents/`, `.github/prompts/`, `.github/instructions/`, `.github/copilot-instructions.md` | `docs/ai/agents/README.md`, `docs/ai/prompts/README.md`, `docs/ai/instructions/README.md` |
| Provider-agnostic prompt docs | `docs/prompts/automation-prompts.md`, `docs/prompts/repo-maintenance-prompts.md`, `docs/prompts/roadmap-source-docs-implementation-prompt.md` | `docs/prompts/README.md`, `docs/ai/prompts/README.md`, automation prompts, repo-maintenance prompts, and source-doc update prompts |
| MCP tools, prompts, and resources | `src/Meridian.Mcp/` | `docs/ai/navigation/README.md`, generated repo-navigation artifacts |
| AI prompt generation and evaluation | `build/scripts/docs/generate-prompts.py`, skill `evals/` folders, `.codex/skills/*/scripts/run_evals.py` | CI-derived prompt files, local eval reports, and archived workflow notes |
| Local AI maintenance scripts and Codex tools | `scripts/ai/*.sh`, `tools/codex/*.ps1`, `make/ai.mk` | `make ai-maintenance-*`, `make ai-audit*`, Codex desktop quality reports, and local AI setup/cleanup lanes |
| Assistant entrypoints and provider config | `AGENTS.md`, `CLAUDE.md`, `.codex/config.toml`, `.codex/environments/`, `.claude/settings.json`, `.claude/settings.local.json`, `.github/copilot-instructions.md` | AI inventory drift checker, root shims, provider-specific startup/config flows |
| Optional IDE/provider assistant entrypoints | `build/scripts/docs/check-ai-inventory.py`, this contract, `docs/ai/README.md` | Cursor, Windsurf, Continue, Cline, Roo, or Gemini files only when a real repo usage path is added |

---

## Alignment Checklist

Use this checklist when changing any AI-related asset:

- [ ] Identify which systems are affected: Codex, Claude, Copilot, MCP, prompts, workflows, docs,
      or root compatibility files.
- [ ] Confirm whether the change is shared policy, host-specific mechanics, or generated content.
- [ ] Update the shared source first when the rule is provider-agnostic.
- [ ] Update only the provider-specific files that need host mechanics or discoverability links.
- [ ] Keep shared project context mirrored across `.codex/skills/_shared/project-context.md`,
      `.claude/skills/_shared/project-context.md`, and `.agents/skills/_shared/project-context.md`
      when current project framing changes.
- [ ] Use the shared handoff format in
      [`agent-handoff-checklist.md`](agent-handoff-checklist.md) whenever a task transitions across
      specialist agents, host surfaces, or validation gates.
- [ ] Choose a work mode from [`work-modes.md`](work-modes.md) before implementation and document any mode escalation.
- [ ] For parallel lanes, initialize [`parallel-task-manifest-template.md`](parallel-task-manifest-template.md)
      and track lane ownership, inspected files, validation plans, and merge risks.
- [ ] Keep all assistant surfaces aligned to the current operator taxonomy: browser dashboard and
      WPF desktop both consume shared contracts, and visible root workspaces remain limited to `Trading`,
      `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`.
- [ ] Keep the **No mobile development lane** policy mirrored in the root assistant entrypoints,
      Copilot guide, and shared Codex/Claude/Agent Skills project-context files so mobile clients are not
      proposed by one assistant while another follows the browser-workstation plan.
- [ ] Keep host-specific guides compact; route broad repository layout questions to generated
      navigation or structure artifacts instead of copying tree snapshots into assistant docs.
- [ ] Keep documentation routing aligned with the rebuilt audience paths: `docs/start/`,
      `docs/product/`, `docs/engineering/`, `docs/operators/`, `docs/ai/`, `docs/roadmap/`,
      `docs/source/`, `docs/reference/`, and `docs/generated/`.
- [ ] Keep root `AGENTS.md` as a compact compatibility shim. Put command catalogs, route cards, and
      proof matrices in maintained docs such as `docs/HELP.md`, `docs/developer/build-test-run.md`,
      and `docs/ai/codex/quickstart.md`.
- [ ] Keep `.codex/agents/*.toml` documented in the Codex and agent indexes when Codex specialist
      profile routing changes.
- [ ] Keep `agents/openai.yaml` aligned with the corresponding Codex or Claude skill when skill
      descriptions or default prompts change.
- [ ] Keep checked-in Claude plugin packages under `.claude/plugins/` indexed in the agent and
      skill docs when plugin manifests, plugin agents, or plugin skills are added, renamed, or
      removed.
- [ ] Keep `docs/prompts/*.md` listed in `docs/prompts/README.md` and `docs/ai/prompts/README.md`
      when prompt or automation guidance changes.
- [ ] Keep `scripts/ai/` and `tools/codex/` documented when local AI maintenance scripts or
      Codex quality tools are added, renamed, or removed.
- [ ] Update `docs/ai/README.md` plus the nearest `docs/ai/*/README.md` index for discoverability.
- [ ] Regenerate `docs/ai/generated/repo-navigation.*` only when routing truth, projects, symbols,
      or authoritative docs change.
- [ ] Keep generated AI inventory reports portable; they must not include local absolute repository
      paths, secrets, or machine-only identifiers.
- [ ] Keep active AI docs from linking to retired GitHub Actions workflow paths. Point to current
      local scripts or `archive/docs/workflows/legacy-github-actions-2026-05-18.md` instead.
- [ ] Keep canonical GitHub documentation links pointed at `rodoHasArrived/Meridian-main`; historical
      issue or workflow-run evidence links may retain their original repository if they are evidence.
- [ ] Run targeted validation and record the command result.

---

## Recommended Validation

Choose the narrowest command that matches the touched surface:

```bash
python3 build/scripts/docs/check-ai-inventory.py --summary
python3 build/scripts/docs/validate-skill-packages.py
python3 build/scripts/docs/mark-stale-docs.py --write --summary
python3 .codex/skills/meridian-implementation-assurance/scripts/run_evals.py --all --dry-run
python3 build/scripts/docs/run-docs-automation.py --profile quick --dry-run
python3 build/scripts/docs/generate-ai-navigation.py --json-output docs/ai/generated/repo-navigation.json --markdown-output docs/ai/generated/repo-navigation.md --recent-changes-output docs/ai/generated/recent-changes.md --summary
python3 build/scripts/docs/check-ai-navigation-freshness.py --max-age-days 14
dotnet build src/Meridian.Mcp/Meridian.Mcp.csproj -c Release
```

For documentation-only updates, a link/readability check plus `git diff --check` is usually enough
unless the change affects generated docs, skill packages, prompt generation, or MCP behavior.

---

## Adding A New AI Surface

Before adding support for a new assistant, IDE, model provider, or automation:

1. Confirm the repo has an actual usage path, not just a speculative provider name.
2. Prefer a small host-specific shim that points to this shared contract and existing `docs/ai/`
   indexes.
3. Avoid copying the full project rules into the new surface.
4. Add the new surface to the supported systems table in this file.
5. Link it from `docs/ai/README.md` and the nearest specialized index.
6. Add validation steps for the new surface if it has executable checks.

---

_Last Updated: 2026-05-28_

## Machine-checkable synchronization contract

Canonical policy file: `docs/ai/contract-policy.json`.

Path-specific mirrors that must stay byte-identical to the canonical policy:

- `docs/ai/copilot/contract-policy.mirror.json`
- `docs/ai/claude/contract-policy.mirror.json`

CI runs `build/scripts/docs/check-ai-contract-drift.py` and fails if any mirror drifts from the canonical policy file.
