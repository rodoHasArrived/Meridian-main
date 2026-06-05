# Provider-Agnostic AI Development Contract

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-06-04

This contract is the shared operating standard for AI-assisted development in Meridian. It applies
to Codex, Claude, GitHub Copilot, MCP clients, reusable prompt templates, CI prompt generation, and
manual assistant sessions, including local AI maintenance tooling used by automations.

Canonical AI governance for this rebuild is:

- `docs/ai/assistant-workflow-contract.md` (shared operating contract)
- `docs/documentation-ownership.md` (lane authority and archive boundaries)
- `docs/documentation-inventory.md` (migration classification)
- `.codex/skills/_shared/project-context.md` (Codex shared project context)
- `AGENTS.md`, `CLAUDE.md` (compatibility shims)

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
| AI automation workflows | `.github/workflows/documentation.yml`, `.github/workflows/copilot-setup-steps.yml`, `.github/workflows/README.md`, archived workflow names `prompt-generation.yml`, `reusable-ai-analysis.yml`, `skill-evals.yml` | Checked-in automation lanes for documentation validation and Copilot bootstrap guidance plus documented historical AI workflow names that now route to active local scripts or archive notes |
| Workflow guidance | `.github/workflows/README.md`, `docs/engineering/README.md`, `make/ai.mk` | Current build, test, publish, and maintenance workflow guidance |
| Reusable prompt templates | `.github/prompts/`, `docs/prompts/`, `docs/ai/prompts/README.md` | Model-agnostic prompts for Copilot Chat, Claude Code, ChatGPT, automation runs, and manual assistant sessions |
| Local AI maintenance tooling | `scripts/ai/`, `build/scripts/ai/`, `tools/codex/`, `make/ai.mk` | Provider-agnostic maintenance lanes, local AI setup/cleanup helpers, scoped deterministic edit tooling, and Codex-specific quality scans |
| Shared AI documentation | `docs/ai/`, `.codex/skills/_shared/project-context.md`, `.claude/skills/_shared/project-context.md`, `.agents/skills/_shared/project-context.md` | Human-readable indexes, routing rules, known-error prevention, and shared project grounding |

The AI inventory checker also watches optional IDE/provider assistant entrypoints such as
`.cursor/`, `.windsurf/`, `.continue/`, `.cline/`, `.roo/`, `.cursorrules`, `.windsurfrules`,
`.clinerules`, `.roomodes`, `GEMINI.md`, and `.gemini/`. No tracked Cursor, Windsurf, Continue,
Cline, Roo, or Gemini instruction surface was found during the 2026-05-25 scan. Add one only when
there is an actual tooling need, then list the exact entrypoint in this contract, `docs/ai/README.md`, and the nearest host index.

## AI-Lane Classification for Rebuild Batches

- `canonical`: shared policies and lane indexes under `docs/ai/` that define current behavior.
- `source-material`: historical notes and one-off experiments used only for extraction.
- `generated`: `docs/ai/generated/*` (regenerate, do not hand-edit).
- `archive`: retired AI guidance retained with replacement links from migration stubs.
- `delete-candidate`: only untracked junk or explicitly approved removals after review.

## Engineering- and Documentation-Awareness Rules for Agents

### Repo Navigation

Before behavior changes, run this sequence:

1. Read `docs/ai/navigation/README.md`.
2. Read the canonical generated navigation in `docs/ai/generated/repo-navigation.md`.
3. Read the owning lane entrypoint (`start`, `engineering`, `product`, `operators`, or `reference`).
4. Verify module/registry impact from `docs/source/data/source-modules.yml` and `docs/roadmap/data/*.yml`.

### Generated-File Handling

- Do not hand-edit generated docs under `docs/roadmap/generated/`, `docs/source/generated/`, `docs/ai/generated/`, `docs/generated/`.
- Update generator inputs and rerun generation commands.
- Keep registry files (`docs/source/data/*.yml`, `docs/roadmap/data/*.yml`) as source-of-truth for generated views.

### Agent Orchestration

- Use `docs/ai/parallel-task-manifest-template.md` for parallel lanes.
- Use `agent-handoff-checklist.md` for explicit handoffs between specialist lanes.
- Use `docs/ai/codex/route-cards.md` to anchor multi-system routing.
- For Codex-owned orchestration, use `docs/ai/codex/prompt-route-rules.json` as the route source,
  `docs/status/prompt-route-lint-report.json` as the route artifact, and
  `docs/status/ai-handoff-packet.json` as the handoff artifact.
- Route schema v2 requires `modelRouteId`, validation floor, validation scripts, required telemetry,
  and escalation triggers so routing, handoff, and CI evidence remain connected.
- Coordinators should assign one narrow concern and explicit file ownership per specialist lane, and
  specialists should load only the required context for that scoped lane before asking for more.

### Token and Context Management

Start from lane entrypoints and expand only after ownership boundaries are confirmed.
Keep each pass scoped to one active concern and one proof lane.
For mixed, multi-system work, switch modes with `work-modes.md` before widening context.
Use `tooling/README.md` when the next question is "which script or validator should prove this?"

#### Token/Context Budget Contract

- Budget by concern, not by file count.
- Keep exploratory context to what directly proves the request; defer historical proof until registry-driven lanes require it.
- Use `Standard` mode for routine refactors and AI-index edits; use `Deep-review` mode only when touching cross-lane workflows or generator contracts.
- If a single pass is expected to require more than one generated surface or more than one registry/doc-source domain, start with a short handoff packet and explicit manifest.
- On long sessions, every additional batch must include a handoff note with current objective, inspected scope, changed files, validation owner, and the next narrow proof command.
- Record inspected files per lane before widening scope so another agent does not repeat the same discovery pass without need.
- Every lane handoff must separate `required context` from `optional context` and record whether validation evidence was reused or rerun.
- Treat assumptions as first-class handoff data: list open assumptions explicitly so downstream lanes do not mistake them for validated facts.
- Record a validation owner and rerun triggers in the handoff packet or shared manifest before lane transfer.

### Validation Procedure

Use this lane-specific order for AI-lane or cross-lane work:

1. If the task touches navigation or agent inventories:
   - `python build/scripts/docs/check-ai-inventory.py --summary`
   - `python build/scripts/docs/check-codex-skills.py --summary`
   - `python build/scripts/docs/validate-docs-structure.py --top-level ai --summary` (for narrow AI-doc lifecycle/structure checks)
2. If task changes any AI mirror policy or shared contract files:
   - `python build/scripts/docs/check-ai-contract-drift.py --canonical docs/ai/contract-policy.json --mirror docs/ai/copilot/contract-policy.mirror.json --mirror docs/ai/claude/contract-policy.mirror.json`
3. If task changes handoff, parallel-lane, or work-mode guidance:
   - `python build/scripts/docs/check-ai-handoff.py --strict`
4. If task changes source-aware engineering or roadmap references:
   - `python build/scripts/docs/validate-roadmap-registry.py --summary`
   - `python build/scripts/docs/validate-source-readmes.py --summary`
   - `python build/scripts/docs/validate-doc-hashes.py --summary`
5. After any doc edits:
   - `python build/scripts/docs/repair-links.py --summary`
   - `git diff --check`
6. For generated-file handoffs:
   - Update generator inputs first.
   - Re-run generation lane only for changed generator-owned outputs.
   - Record generated outputs as generated-only in the handoff packet.
7. For Codex route/handoff changes:
   - `python build/scripts/docs/prompt-route-linter.py --summary`
   - `python build/scripts/docs/handoff-packet-generator.py --summary --route-json docs/status/prompt-route-lint-report.json`
   - `python build/scripts/docs/check-handoff-packet-schema.py --packet-json docs/status/ai-handoff-packet.json --summary`
   - `python build/scripts/docs/check-validation-floor.py --summary-json docs/status/docs-automation-summary.json --route-json docs/status/prompt-route-lint-report.json --summary`
   - `python build/scripts/docs/check-mode-escalation.py --route-json docs/status/prompt-route-lint-report.json --summary-json docs/status/docs-automation-summary.json --summary`

### Parallel Development Defaults

When launching parallel AI work:

- Create or update a shared manifest first:
  - `docs/ai/parallel-task-manifest-template.md`
- Assign one coordinator artifact per lane (`assistant-workflow-contract`, `docs/ai/README`, one
  or two canonical lane READMEs).
- Keep each lane scoped to one canonical source surface unless the manifest explicitly approves cross-lane coupling.
- Record lane-owned inspected files and the validation owner before implementation starts.
- Require a short handoff note at each lane transition using:
  - scope
  - inspected files
  - edited files
  - decisions made
  - validation owner
  - validation run + outcome
  - validation reuse decision + rerun triggers
  - required context vs optional context
  - residual risks

### Token and Context Boundary Guardrails

- No lane should expand context until it has satisfied the required preconditions in
  **Repo Navigation** and the owning lane contract.
- Stop once proof is in hand; do not defer validation to a later unrelated pass.
- Prefer this contract plus one specialist surface over broad prose recycling.
- Include `Readme`, `commands`, `validation`, and `risks` in final lane exit notes.

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
   Keep new product work centered on the W1-W5 operational record baseline: data confidence,
   retained source evidence, reconciliation, approvals, accounting records, multi-asset operational
   coverage, and governed reports. Defer Backtesting Studio, live-readiness beyond paper-first
   governance, full payments, forecasting, enterprise risk, client portal, no-code workflow design,
   mobile, and other expansion lanes unless they directly strengthen that workflow.
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

## Rebuild-Native AI Requirements

This contract is the canonical source for AI work during the documentation rebuild.

| Required area | Contracted behavior |
| --- | --- |
| Repo orientation | Use `docs/ai/navigation/README.md` and `docs/ai/generated/repo-navigation.md` before broad edits. |
| Edit rules | Keep source-of-truth updates in the shared contract and lane READMEs; treat old hand-authored docs as migration material unless explicitly canonical. |
| Generated-file handling | Do not edit generated docs in place. Update generator inputs or source data and rerun the owning generator. |
| Agent orchestration | Use `parallel-task-manifest-template.md` for multi-lane work and `agent-handoff-checklist.md` for ownership transitions. |
| Codex route evidence | Use `prompt-route-linter.py` to emit `docs/status/prompt-route-lint-report.json` with lane, skill, mode, `modelRouteId`, validation requirements, telemetry requirements, and escalation triggers. |
| Handoff packet evidence | Use `handoff-packet-generator.py` to emit `docs/status/ai-handoff-packet.json` with scope, changed files, validation evidence, route outcome, telemetry, next lane, and context lists. |
| Parallel workflow | One manifest per parallel batch. One-file-per-lane ownership and explicit merge order in the manifest. |
| Token/context discipline | Keep scope bounded to one lane and one evidence surface per batch; handoff ownership and scope on lane transitions. |
| Validation | Run the narrowest relevant docs/AI checks for touched surfaces and record command outcomes in final notes. |
| Ownership rules | Use `docs/documentation-ownership.md` for canonical lane ownership and archive policy. |

### Requirement-to-Command Mapping

Use this matrix to satisfy rebuild acceptance criteria for AI-lane updates:

| Requirement | Required action |
| --- | --- |
| Repo navigation truth | Re-run `python build/scripts/docs/generate-ai-navigation.py --json-output docs/ai/generated/repo-navigation.json --markdown-output docs/ai/generated/repo-navigation.md --recent-changes-output docs/ai/generated/recent-changes.md --summary` when routing rules/doc surfaces change. |
| AI inventory consistency | Run `python build/scripts/docs/check-ai-inventory.py --summary` and `python build/scripts/docs/check-codex-skills.py --summary` whenever any AI entrypoint, host shim, agent index, or tool mapping changes. |
| Contract drift control | Run `python build/scripts/docs/check-ai-contract-drift.py --canonical docs/ai/contract-policy.json --mirror docs/ai/copilot/contract-policy.mirror.json --mirror docs/ai/claude/contract-policy.mirror.json` when shared policy or host mirrors are edited. |
| Codex route and handoff guardrails | Run `prompt-route-linter.py`, `handoff-packet-generator.py`, `check-handoff-packet-schema.py`, `check-validation-floor.py`, `check-mode-escalation.py`, and `check-ai-routing-parity.py` when Codex routing, handoff, or validation-floor behavior changes. |
| Link and structure hygiene | Run `python build/scripts/docs/repair-links.py --summary` and `python build/scripts/docs/validate-docs-structure.py --top-level ai --summary` for AI-doc edits; use the repo-wide validator only when global docs taxonomy changes are in scope. |
| Archive migration audit | Update migration mapping entries in relevant canonical `docs/*/README.md` files and archive indexes when retiring high-traffic legacy paths. |

Use this section for rebuild planning: every AI request that changes docs or agent behavior must explicitly classify scope as canonical, source-material, generated, or archive and align to this contract.

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
| Project framing, commands, and architecture | `docs/start/README.md`, `docs/product/README.md`, `docs/product/meridian-design-document.md`, `docs/engineering/README.md`, `CLAUDE.md`, `.codex/skills/_shared/project-context.md`, `.claude/skills/_shared/project-context.md`, `.agents/skills/_shared/project-context.md` | `AGENTS.md`, Copilot instructions, skills, agents |
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
| Local AI maintenance scripts and Codex tools | `docs/ai/tooling/README.md`, `scripts/ai/*.sh`, `build/scripts/ai/*.py`, `tools/codex/*.ps1`, `make/ai.mk` | `make ai-maintenance-*`, `make ai-audit*`, Codex desktop quality reports, scoped edit planning/apply lanes, and local AI setup/cleanup lanes |
| Assistant entrypoints and provider config | `AGENTS.md`, `CLAUDE.md`, `.codex/config.toml`, `.codex/environments/`, `.claude/settings.json`, `.claude/settings.local.json`, `.github/copilot-instructions.md` | AI inventory drift checker, root shims, provider-specific startup/config flows |
| AI automation workflows | `.github/workflows/documentation.yml`, `.github/workflows/copilot-setup-steps.yml`, `.github/workflows/README.md`, archived workflow names `prompt-generation.yml`, `reusable-ai-analysis.yml`, `skill-evals.yml` | Shared CI automation surface plus Copilot bootstrap workflow and documented historical workflow names that now route to active local scripts or archive notes |
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
- [ ] Update [`tooling/README.md`](tooling/README.md) when AI script discovery, validation lanes,
      or safe-usage guidance changes.
- [ ] Keep all assistant surfaces aligned to the current operator taxonomy: browser dashboard and
      WPF desktop both consume shared contracts, and visible root workspaces remain limited to `Trading`,
      `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings`.
- [ ] Keep product guidance aligned to the W1-W5 operational record baseline; expansion lanes stay
      deferred unless roadmap data moves them into active scope.
- [ ] Keep the **No mobile development lane** policy mirrored in the root assistant entrypoints,
      Copilot guide, and shared Codex/Claude/Agent Skills project-context files so mobile clients are not
      proposed by one assistant while another follows the browser-workstation plan.
- [ ] Keep host-specific guides compact; route broad repository layout questions to generated
      navigation or structure artifacts instead of copying tree snapshots into assistant docs.
- [ ] Keep documentation routing aligned with the rebuilt audience paths: `docs/start/`,
      `docs/product/`, `docs/engineering/`, `docs/operators/`, `docs/ai/`, `docs/roadmap/`,
      `docs/source/`, `docs/reference/`, and `docs/generated/`.
- [ ] Keep root `AGENTS.md` as a compact compatibility shim. Put command catalogs, route cards, and
      proof matrices in maintained docs such as `docs/HELP.md`, `docs/start/README.md`,
      `docs/engineering/README.md`, and `docs/ai/codex/quickstart.md`.
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

## Repository Navigation Contract

For any non-trivial task, the mandatory orientation chain is:

1. `docs/ai/navigation/README.md`
2. `docs/ai/generated/repo-navigation.md`
3. `docs/ai/README.md`
4. relevant lane README (`start`, `engineering`, `product`, `operators`, or `reference`)

Deviating from this chain is only allowed for very narrow one-file edits with no ownership or
cross-lane impact.

## Agent Orchestration and Parallel Work

- Use `docs/ai/parallel-task-manifest-template.md` before parallel edits.
- Assign one explicit owner per lane:
  - code change
  - docs update
  - validation/runbook lane
- Require a handoff artifact (`agent-handoff-checklist.md`) before ownership transfer across assistants,
  providers, or skill families.
- Do not let parallel lane work overlap in the same file class (source, generated output,
  AI contracts, navigation docs) unless the manifest records merge order and conflict ownership.

## AI Surface Update Checklist (Provider-Agnostic)

When changing AI guidance, agent files, or provider surfaces:

- Update shared policy first: this file.
- Update affected host-specific docs and indexes in the same batch.
- Keep host files as mechanical shims.
- Run:

```bash
python3 build/scripts/docs/check-ai-inventory.py --summary
python3 build/scripts/docs/check-codex-skills.py --summary
python3 build/scripts/docs/check-ai-contract-drift.py --canonical docs/ai/contract-policy.json --mirror docs/ai/copilot/contract-policy.mirror.json --mirror docs/ai/claude/contract-policy.mirror.json
python3 build/scripts/docs/prompt-route-linter.py --summary
python3 build/scripts/docs/check-ai-routing-parity.py --summary
```

If any command fails due to missing optional surfaces, treat that as work-tree evidence and
either add the surface or document it as intentionally unavailable.

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

_Last Updated: 2026-05-31_

## Machine-checkable synchronization contract

Canonical policy file: `docs/ai/contract-policy.json`.

Path-specific mirrors that must stay byte-identical to the canonical policy:

- `docs/ai/copilot/contract-policy.mirror.json`
- `docs/ai/claude/contract-policy.mirror.json`

CI runs `build/scripts/docs/check-ai-contract-drift.py` and fails if any mirror drifts from the canonical policy file.

