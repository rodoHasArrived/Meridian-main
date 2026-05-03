# Provider-Agnostic AI Development Contract

This contract is the shared operating standard for AI-assisted development in Meridian. It applies
to Codex, Claude, GitHub Copilot, MCP clients, reusable prompt templates, CI prompt generation, and
manual assistant sessions.

Use this file when updating assistant-specific instructions so the project keeps one common rule
set instead of drifting into conflicting provider-specific guidance.

---

## Supported AI Systems

The current repository evidence supports these AI surfaces:

| System or surface | Repository assets | Primary role |
| --- | --- | --- |
| Root assistant compatibility | `AGENTS.md`, `CLAUDE.md` | Root-level project context and compatibility for agents that read conventional files |
| Codex | `.codex/config.toml`, `.codex/environments/`, `.codex/skills/`, `.codex/skills/*/agents/openai.yaml` | Repo-local specialist skills, OpenAI/Codex metadata, and environment entrypoints |
| Claude / Claude Code | `.claude/settings.json`, `.claude/settings.local.json`, `.claude/agents/`, `.claude/skills/` | Claude agent definitions, portable skill packages, hooks, permissions, and model selection |
| GitHub Copilot | `.github/copilot-instructions.md`, `.github/instructions/`, `.github/agents/`, `.github/prompts/`, `.github/workflows/copilot-*` | Repository-wide coding-agent guidance, path instructions, agents, prompts, and setup workflows |
| MCP-compatible clients | `src/Meridian.Mcp/`, `src/Meridian.McpServer/`, `docs/ai/navigation/README.md`, `docs/ai/generated/repo-navigation.json` | Tool, prompt, resource, and navigation access for any MCP client |
| AI automation workflows | `.github/workflows/prompt-generation.yml`, `.github/workflows/reusable-ai-analysis.yml`, `.github/workflows/skill-evals.yml`, `.github/workflows/documentation.yml` | CI-generated prompts, AI analysis, skill evaluation, and known-error intake |
| Reusable prompt templates | `.github/prompts/`, `docs/ai/prompts/README.md` | Model-agnostic prompts for Copilot Chat, Claude Code, ChatGPT, and manual assistant sessions |
| Shared AI documentation | `docs/ai/`, `.codex/skills/_shared/project-context.md`, `.claude/skills/_shared/project-context.md` | Human-readable indexes, routing rules, known-error prevention, and shared project grounding |

No tracked `.cursor/`, `.windsurf/`, `.continue/`, `.cline/`, `.roo/`, or Gemini-specific
instruction surface was found during the 2026-04-28 scan. Add one only when there is an actual
tooling need and link it from `docs/ai/README.md`.

---

## Universal Execution Flow

Every assistant and automation should use the same high-level flow:

1. **Read the request literally.** Restate the desired outcome and identify acceptance criteria.
2. **Orient before broad search.** Start with `docs/ai/navigation/README.md` and
   `docs/ai/generated/repo-navigation.md`. If MCP is available, prefer the repo-navigation tools
   and resources before broad recursive search.
3. **Load the nearest specialist surface.** Use the relevant Codex skill, Claude skill or agent,
   Copilot agent, prompt template, path instruction, or MCP tool based on the routed subsystem.
4. **Preserve architecture boundaries.** Follow the current browser-first operator UI framing,
   keep visible navigation to `Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`,
   `Data`, and `Settings`, and treat legacy `Research`, `Data Operations`, and `Governance`
   WPF names as retained compatibility aliases rather than new root workspaces.
5. **Make the smallest safe change.** Avoid speculative rewrites, fake providers, unused agents,
   broad cleanup, and unrelated formatting churn.
6. **Validate narrowly first.** Run the smallest build, test, docs, or skill-validation command
   that covers the touched surface; expand only when the change risk justifies it.
7. **Synchronize docs and AI catalogs.** When a behavior, workflow, prompt, skill, or agent changes,
   update the nearest `docs/ai/*/README.md` index and any mirrored host surfaces that teach the
   same workflow.
8. **Report evidence.** Summaries must include what changed, why, affected files, validation
   commands, and any residual risks.

---

## Safety Rules Shared By All Providers

- Do not add or expose secrets, API keys, tokens, paid-service credentials, or local-only absolute
  paths.
- Do not remove AI system support unless the obsolete surface is verified and documented.
- Do not introduce new AI providers, tools, agents, models, or dependencies without repository
  evidence that they are needed.
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
| Project framing, commands, and architecture | `CLAUDE.md`, `.codex/skills/_shared/project-context.md`, `.claude/skills/_shared/project-context.md` | `AGENTS.md`, Copilot instructions, skills, agents |
| Repo routing and subsystem ownership | `docs/ai/generated/repo-navigation.json`, `docs/ai/navigation/README.md` | MCP navigation resources/tools, generated markdown, navigation agents and skills |
| Known AI mistakes | `docs/ai/ai-known-errors.md` | Copilot instructions, Claude/Codex skills, documentation workflow intake |
| Codex skill catalog | `.codex/skills/README.md`, `docs/ai/skills/README.md` | Codex UI metadata in `agents/openai.yaml` |
| Claude agent and skill catalog | `.claude/agents/`, `.claude/skills/`, `docs/ai/agents/README.md`, `docs/ai/skills/README.md` | Portable skill packages and Claude settings |
| Copilot agents, prompts, and path rules | `.github/agents/`, `.github/prompts/`, `.github/instructions/`, `.github/copilot-instructions.md` | `docs/ai/agents/README.md`, `docs/ai/prompts/README.md`, `docs/ai/instructions/README.md` |
| MCP tools, prompts, and resources | `src/Meridian.Mcp/`, `src/Meridian.McpServer/` | `docs/ai/navigation/README.md`, generated repo-navigation artifacts |
| AI prompt generation and evaluation | `.github/workflows/prompt-generation.yml`, `.github/workflows/skill-evals.yml`, skill `evals/` folders | Generated prompt files and eval reports |
| Assistant entrypoints and provider config | `AGENTS.md`, `CLAUDE.md`, `.codex/config.toml`, `.codex/environments/`, `.claude/settings.json`, `.claude/settings.local.json`, `.github/copilot-instructions.md` | AI inventory drift checker, root shims, provider-specific startup/config flows |

---

## Alignment Checklist

Use this checklist when changing any AI-related asset:

- [ ] Identify which systems are affected: Codex, Claude, Copilot, MCP, prompts, workflows, docs,
      or root compatibility files.
- [ ] Confirm whether the change is shared policy, host-specific mechanics, or generated content.
- [ ] Update the shared source first when the rule is provider-agnostic.
- [ ] Update only the provider-specific files that need host mechanics or discoverability links.
- [ ] Keep shared project context mirrored between `.codex/skills/_shared/project-context.md` and
      `.claude/skills/_shared/project-context.md` when current project framing changes.
- [ ] Keep all assistant surfaces aligned to the current operator taxonomy: browser dashboard first,
      retained WPF support only when needed, and visible root workspaces limited to `Trading`,
      `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`.
- [ ] Keep host-specific guides compact; route broad repository layout questions to generated
      navigation or structure artifacts instead of copying tree snapshots into assistant docs.
- [ ] Keep `agents/openai.yaml` aligned with the corresponding Codex or Claude skill when skill
      descriptions or default prompts change.
- [ ] Update `docs/ai/README.md` plus the nearest `docs/ai/*/README.md` index for discoverability.
- [ ] Regenerate `docs/ai/generated/repo-navigation.*` only when routing truth, projects, symbols,
      or authoritative docs change.
- [ ] Keep generated AI inventory reports portable; they must not include local absolute repository
      paths, secrets, or machine-only identifiers.
- [ ] Keep canonical GitHub documentation links pointed at `rodoHasArrived/Meridian-main`; historical
      issue or workflow-run evidence links may retain their original repository if they are evidence.
- [ ] Run targeted validation and record the command result.

---

## Recommended Validation

Choose the narrowest command that matches the touched surface:

```bash
python3 build/scripts/docs/check-ai-inventory.py --summary
python3 build/scripts/docs/validate-skill-packages.py
python3 .codex/skills/meridian-implementation-assurance/scripts/run_evals.py --all --dry-run
python3 build/scripts/docs/run-docs-automation.py --profile quick --dry-run
python3 build/scripts/docs/generate-ai-navigation.py --json-output docs/ai/generated/repo-navigation.json --markdown-output docs/ai/generated/repo-navigation.md --summary
dotnet build src/Meridian.McpServer/Meridian.McpServer.csproj -c Release
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

_Last Updated: 2026-04-29_
