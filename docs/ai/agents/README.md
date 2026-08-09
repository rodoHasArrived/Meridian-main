# AI Agent Definitions

This directory indexes AI agent definitions used in the Meridian project. GitHub Copilot agent
files live in `.github/agents/`; Claude agent files live in `.claude/agents/`; Codex specialist
profiles live in `.codex/agents/`. Repo-local Codex skills live in `.codex/skills/` and provide
the primary current project-scoped workflow surface.
All agent surfaces should follow the shared provider-agnostic workflow in
[`../assistant-workflow-contract.md`](../assistant-workflow-contract.md).

All three surfaces should stay aligned around the same current product framing: Meridian is a
.NET 10 operational-finance and trading platform with two active co-equal operator UI lanes — a
browser-based operator workstation and the reactivated WPF desktop workstation (current focus:
web-UI parity over shared contracts) — and visible navigation limited to
`Trading`, `Portfolio`, `Accounting`, `Reporting`, `Strategy`, `Data`, and `Settings` on top of
strong provider, storage, execution, ledger, and MCP foundations.

When an agent lane needs validator, route, handoff-packet, or maintenance-script selection, load
[`../tooling/README.md`](../tooling/README.md) instead of rediscovering command lanes from scratch.
When an agent, prompt, skill, rubric, or graph-memory workflow is being improved from feedback,
use the Codex self-improving loop in [`../codex/self-improving-agents.md`](../codex/self-improving-agents.md)
and keep promotion evidence tied to the owning skill or agent profile.

---

## Orientation Layer

### Repo Navigation Agent

**Copilot file:** [`.github/agents/repo-navigation-agent.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.github/agents/repo-navigation-agent.md)

**Claude file:** [`.claude/agents/meridian-repo-navigation.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.claude/agents/meridian-repo-navigation.md)

Routes large-repo work to the right subsystem, docs, entrypoints, and downstream specialist agents before implementation starts. It owns four roles:

- `repo-orienter` for subsystem classification
- `task-router` for mapping natural-language requests to repo routes
- `execution-tracer` for high-signal entrypoints and dependency edges
- `doc-router` for authoritative docs and guardrails

Primary inputs:

- [`docs/ai/generated/repo-navigation.md`](../generated/repo-navigation.md)
- [`docs/ai/generated/repo-navigation.json`](../generated/repo-navigation.json)
- [`docs/ai/navigation/README.md`](../navigation/README.md)
- [`.codex/skills/_shared/project-context.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.codex/skills/_shared/project-context.md)

---

## Codex Agent Profiles (`.codex/agents/`)

Codex TOML profiles route recurring specialist work to compact, provider-specific entrypoints while
the shared policy remains in [`../assistant-workflow-contract.md`](../assistant-workflow-contract.md)
and [`../codex/README.md`](../codex/README.md).
The former `meridian-navigation` and `meridian-user-panel` compatibility aliases were pruned; use
`meridian-repo-navigation` and `meridian-simulated-user-panel` for those lanes.
Each profile must define `name`, `description`, and `developer_instructions`; it can also include
supported Codex `config.toml` keys such as `model`, `model_reasoning_effort`, `sandbox_mode`,
`mcp_servers`, and `skills.config` for lane-specific overrides. Keep shared project profiles
conservative and put secrets, provider auth, notifications, telemetry, and personal model choices
in user-level Codex config.

| Profile | Purpose |
| ------ | --------- |
| `dense-data-grid-inspector-panel.toml` | Build scalable dense grids, stable selection, and inspector panels |
| `desktop-test-generation.toml` | Generate focused WPF desktop workflow tests |
| `diagnostics-audit-timeline.toml` | Build diagnostics panels, audit timelines, evidence trails, and recovery surfaces |
| `meridian-archive-organizer.toml` | Archive stale files and preserve repository structure evidence |
| `meridian-blueprint.toml` | Create implementation-ready technical designs |
| `meridian-brainstorm.toml` | Generate Meridian-native product and architecture ideas |
| `meridian-browser-workstation.toml` | Route and implement browser workstation TypeScript/React tasks |
| `meridian-cleanup.toml` | Clean up code and docs without behavior changes |
| `meridian-code-architecture.toml` | Check architecture conformance, module boundaries, dependencies, and ADR/source-doc alignment |
| `meridian-code-review.toml` | Review changes for bugs, regressions, and architecture drift |
| `meridian-contract-governance.toml` | Trace shared contract impact across services, UI surfaces, tests, and docs |
| `meridian-accounting-posting-controls.toml` | Review accounting posting gates, approval, period locks, idempotency, and reversal/rebook safeguards |
| `meridian-event-accounting-architecture.toml` | Design event-based accounting architecture and evidence-backed ledger controls |
| `meridian-ledger-projection-replay-review.toml` | Review ledger projection, replay ordering, rebuild, versioning, and report handoff risk |
| `meridian-codex-skill-builder.toml` | Package Codex skills with scripts, evals, profiles, catalogs, and route coverage |
| `meridian-docs.toml` | Maintain documentation and AI guidance |
| `meridian-feasibility-sketcher.toml` | Add lightweight seams, dependency, validation, and size cards to brainstormed ideas |
| `meridian-idea-critic.toml` | Critique brainstorm output for weak anchors, duplication, hidden cost, and wrong-lane routing |
| `meridian-idea-to-blueprint-router.toml` | Route brainstorm candidates to blueprint, roadmap, docs, review, or no action |
| `meridian-implementation-assurance.toml` | Verify implementation completeness, evidence, docs sync, and guardrails |
| `meridian-opportunity-scout.toml` | Find repo-grounded opportunity areas before brainstorm generation |
| `meridian-persona-signal-scout.toml` | Extract Persona Matrix pressure points before persona-sensitive ideation |
| `meridian-provider-builder.toml` | Build or extend ProviderSdk-compliant data providers |
| `meridian-repo-navigation.toml` | Orient large-repo tasks before deeper work |
| `meridian-roadmap-strategist.toml` | Reconcile roadmap, delivery-plan, and target-state docs |
| `meridian-simulated-user-panel.toml` | Run evidence-led canonical Persona Matrix panels with fail-closed release gates |
| `meridian-test-writer.toml` | Write scenario-first Meridian tests |
| `modular-desktop-mvvm.toml` | Implement modular WPF MVVM workstation changes |
| `performance-resource-review.toml` | Review memory, CPU, I/O, rendering, concurrency, and lifecycle risks |
| `provider-management-workflow.toml` | Build secure provider setup, health, validation, and recovery workflows |
| `research-data-acquisition.toml` | Build research acquisition, preview, validation, lineage, and cleanup workflows |
| `safe-refactoring.toml` | Refactor desktop and shared code incrementally without behavior drift |
| `shared-component-extraction.toml` | Extract repeated desktop patterns into reusable components |
| `workstation-screen-composition.toml` | Compose desktop screens from shared workstation primitives |

### Brainstorm companion profiles

These Codex-only profiles support `meridian-brainstorm` without replacing it or creating duplicate
roadmap, blueprint, or user-panel brainstorm lanes. Use the full chain when ideation needs both
grounding and post-processing:

```text
meridian-opportunity-scout -> meridian-persona-signal-scout -> meridian-brainstorm
  -> meridian-idea-critic -> meridian-feasibility-sketcher -> meridian-idea-to-blueprint-router
```

| Profile | Output boundary |
| ------ | --------------- |
| `meridian-opportunity-scout` | Repo anchors, opportunity gaps, and avoid-list only |
| `meridian-persona-signal-scout` | Persona pressure points from the current Persona Matrix only |
| `meridian-idea-critic` | Keep, refine, reject, or reroute decisions for existing ideas |
| `meridian-feasibility-sketcher` | Lightweight feasibility cards, not class-level blueprint details |
| `meridian-idea-to-blueprint-router` | One recommended next lane per candidate and required input for that lane |

Do not add a separate `meridian-competitive-pattern-adapter` in this layer. Competitive signals
belong in `meridian-opportunity-scout` and the existing `meridian-brainstorm` competitive reference,
then advance through the same critic, feasibility, and router gates as other ideas.

### Persona Matrix user-testing profiles

The `meridian-user-testing-*` Codex profiles simulate one role at a time from the Persona Matrix in
[`../../product/meridian-design-document.md`](../../product/meridian-design-document.md). Use them
for focused single-persona user testing, and use `meridian-simulated-user-panel` for multi-persona
panels. Each profile carries the persona's matrix facts plus domain experience, familiar programs,
preferences, and testing pressure points. These are simulations, not recruited-user research;
results must label that limitation and distinguish verified evidence, inference, and missing proof.
Independent persona agents are optional and require an explicit request for independent voices.

| Profile | Persona category |
| ------ | --------- |
| `meridian-user-testing-financial-operations-professional.toml` | Primary Operator |
| `meridian-user-testing-investment-accountant.toml` | Primary Operator |
| `meridian-user-testing-reconciliation-analyst.toml` | Primary Operator |
| `meridian-user-testing-fund-accountant.toml` | Primary Operator |
| `meridian-user-testing-operations-manager.toml` | Primary Operator / Manager |
| `meridian-user-testing-data-operations-analyst.toml` | Primary Operator |
| `meridian-user-testing-treasury-operations-specialist.toml` | Primary Operator |
| `meridian-user-testing-reporting-analyst.toml` | Primary Operator |
| `meridian-user-testing-portfolio-manager.toml` | Investment User |
| `meridian-user-testing-investment-analyst.toml` | Investment User |
| `meridian-user-testing-quantitative-researcher.toml` | Investment User |
| `meridian-user-testing-trader.toml` | Investment User |
| `meridian-user-testing-risk-manager.toml` | Governance / Investment User |
| `meridian-user-testing-cfo.toml` | Executive |
| `meridian-user-testing-cio.toml` | Executive |
| `meridian-user-testing-controller.toml` | Governance |
| `meridian-user-testing-compliance-officer.toml` | Governance |
| `meridian-user-testing-fund-investor-lp.toml` | Stakeholder |
| `meridian-user-testing-ria-client.toml` | Stakeholder |
| `meridian-user-testing-family-beneficiary.toml` | Stakeholder |
| `meridian-user-testing-trustee.toml` | Stakeholder |
| `meridian-user-testing-auditor.toml` | External / Governance |
| `meridian-user-testing-system-administrator.toml` | Administration |
| `meridian-user-testing-security-administrator.toml` | Administration |
| `meridian-user-testing-integration-administrator.toml` | Administration |

---

## GitHub Copilot Agents (`.github/agents/`)

| Agent | Purpose |
| ------ | --------- |
| `adr-generator.agent.md` | Create ADRs in `docs/adr/` |
| `blueprint-agent.md` | Produce implementation-ready technical designs |
| `brainstorm-agent.md` | Generate high-value ideas and refactoring directions |
| `bug-fix-agent.md` | Reproduce, isolate, fix, and regression-test bugs |
| `cleanup-agent.md` | Remove dead code and safe anti-patterns |
| `code-review-agent.md` | Apply the canonical 7-lens review framework |
| `documentation-agent.md` | Keep docs and AI guidance current |
| `implementation-assurance-agent.md` | Verify completed work against requirements and evidence |
| `performance-agent.md` | Optimize measured bottlenecks |
| `provider-builder-agent.md` | Build and extend providers |
| `repo-navigation-agent.md` | Orient large-repo tasks before deeper work |
| `software-engineer-agent-v1.agent.md` | General-purpose production software engineering execution agent |
| `simulated-user-panel-agent.md` | Run manifest-driven owner-minded user panels across design-partner, release-gate, and usability-lab modes |
| `test-writer-agent.md` | Generate Meridian-style tests |

---

## Claude Code Agents (`.claude/agents/`)

Each agent is a lightweight entrypoint that routes to its portable skill package in
`.claude/skills/`. The set mirrors the meridian-* Codex profiles in `.codex/agents/`;
the former `meridian-navigation` and `meridian-user-panel` aliases were pruned here too.

| Agent | Purpose |
| ------ | --------- |
| [`meridian-archive-organizer.md`](../../../.claude/agents/meridian-archive-organizer.md) | Archive stale files and keep repository structure tidy |
| [`meridian-blueprint.md`](../../../.claude/agents/meridian-blueprint.md) | Blueprint and design specialist |
| [`meridian-brainstorm.md`](../../../.claude/agents/meridian-brainstorm.md) | Generate Meridian-native product and architecture ideas |
| [`meridian-browser-workstation.md`](../../../.claude/agents/meridian-browser-workstation.md) | Route and implement browser workstation TypeScript/React tasks |
| [`meridian-cleanup.md`](../../../.claude/agents/meridian-cleanup.md) | Cleanup specialist |
| [`meridian-code-review.md`](../../../.claude/agents/meridian-code-review.md) | Review changes for bugs, regressions, and architecture drift. **Findings only, and holds no command tool** — scope it to named files or supply the diff; it cannot derive one from a commit hash, branch, or worktree |
| [`meridian-docs.md`](../../../.claude/agents/meridian-docs.md) | Documentation specialist |
| [`meridian-implementation-assurance.md`](../../../.claude/agents/meridian-implementation-assurance.md) | Verify implementation completeness, evidence, docs sync, and guardrails |
| [`meridian-provider-builder.md`](../../../.claude/agents/meridian-provider-builder.md) | Build or extend ProviderSdk-compliant data providers |
| [`meridian-repo-navigation.md`](../../../.claude/agents/meridian-repo-navigation.md) | Generated-map-based repo navigation specialist. Declares a **deny-list**, so it uses the session's navigation MCP tools when present rather than being confined to filesystem search |
| [`meridian-roadmap-strategist.md`](../../../.claude/agents/meridian-roadmap-strategist.md) | Roadmap, delivery-plan, and target-state specialist |
| [`meridian-simulated-user-panel.md`](../../../.claude/agents/meridian-simulated-user-panel.md) | Evidence-led Persona Matrix specialist for design-partner, usability-lab, and fail-closed release-gate reviews. Independent persona voices need the **parent session** to launch the workers — a subagent may not be able to nest, and the panel must say so rather than collapse to one voice |
| [`meridian-test-writer.md`](../../../.claude/agents/meridian-test-writer.md) | Write scenario-first Meridian tests |

---

## Claude Plugin Agents (`.claude/plugins/*/agents/`)

Checked-in Claude plugin agents are provider-specific helpers and must still follow the shared
Meridian workflow contract when used in this repository.

| Plugin | Agent | Purpose |
| ------ | ----- | ------- |
| `csharp-dotnet-development` | [`.claude/plugins/csharp-dotnet-development/agents/expert-dotnet-software-engineer.md`](../../../.claude/plugins/csharp-dotnet-development/agents/expert-dotnet-software-engineer.md) | General .NET software engineering guidance from the plugin package |
| `frontend-web-dev` | [`.claude/plugins/frontend-web-dev/agents/electron-angular-native.md`](../../../.claude/plugins/frontend-web-dev/agents/electron-angular-native.md) | General Electron/Angular/native integration review guidance from the plugin package |
| `frontend-web-dev` | [`.claude/plugins/frontend-web-dev/agents/expert-react-frontend-engineer.md`](../../../.claude/plugins/frontend-web-dev/agents/expert-react-frontend-engineer.md) | General React/TypeScript frontend guidance from the plugin package |

---

## Pipeline Position

The intended routing flow is:

```text
Repo Navigation -> [Single-domain task]    -> Specialist Agent/Skill -> Implementation -> Review -> Testing/Assurance
                -> [Multi-domain / gated]  -> Coordinator Escalation -> Specialist Agent/Skill -> Approval Gate -> Trace/Evidence
```

Use repo navigation first whenever the main problem is "where should I start?" rather than "how do I implement this detail?"

Choose coordinator escalation for multi-domain work, explicit approval-gated changes, or operator-facing
briefings that need trace/evidence retention. Use the shared handoff packet workflow plus the route and
validation evidence required by [`../assistant-workflow-contract.md`](../assistant-workflow-contract.md);
record required/context boundaries and
rerun triggers before lane transition.

Coordinator agents should assign one narrow concern, a compact file set, and a validation owner to
each specialist lane. Specialist agents should load only the required context for that lane, return
the `required context` vs `optional context` split from
[`../agent-handoff-checklist.md`](../agent-handoff-checklist.md), and record rerun triggers before
handoff so downstream agents do not repeat discovery or validation without cause.
When local validation is blocked by machine capacity, restore failures, or MSBuild locks, agents
should use GitHub Actions `Targeted Test` on the pushed branch as the hosted proof lane before
retrying broad local scripts.

## AI Contract Coverage

- Repo navigation: `../navigation/README.md`, `../generated/repo-navigation.md`
- Agent edit rules: `../assistant-workflow-contract.md`, `.codex/skills/_shared/project-context.md`,
  `.agents/skills/_shared/project-context.md`, `.claude/skills/_shared/project-context.md`
- Generated-file handling: AI and repo-navigation generated artifacts are owned by their generators;
  do not hand-edit docs under `docs/ai/generated/` or generator outputs in `docs/generated/`.
- Agent orchestration: `docs/ai/parallel-task-manifest-template.md`, `docs/ai/agent-handoff-checklist.md`,
  and `.codex/agents/` profiles for cross-lane routing
- Agent improvement: `docs/ai/codex/self-improving-agents.md` for baseline, feedback, eval,
  promotion, and graph/retrieval guardrails
- Tooling and validators: prefer [`../tooling/README.md`](../tooling/README.md) for script choice,
  route artifacts, and maintenance lanes instead of copying command catalogs into agent prompts
- Parallel workflow: keep lane scopes disjoint in the manifest and record handoff expectations before merge
- Token/context management: choose a mode in `docs/ai/work-modes.md` and summarize context scope in handoff packets
- Validation: `python build/scripts/docs/check-ai-inventory.py --summary`, `python build/scripts/docs/check-codex-skills.py --summary`,
  `python build/scripts/docs/validate-skill-packages.py`, `python build/scripts/docs/check-ai-handoff.py --strict`,
  and `python build/scripts/docs/check-ai-contract-drift.py ...` when surfaces change
- Documentation ownership: `../documentation-ownership.md` and `../assistant-workflow-contract.md`

### Agent Design Pattern Selection

| Pattern | When to use | Meridian example |
| --- | --- | --- |
| **Parallel** | Subtasks are independent — no output dependency between them | Code review + security scan simultaneously; investigating separate subsystems concurrently |
| **Sequential** | Each step's output feeds the next | Repo Navigation → Specialist Implementation → Code Review → Assurance (default single-domain lane) |
| **Hierarchical** | A coordinator delegates to specialist agents, aggregates evidence, and enforces approval gates | DK1 readiness: provider validation + replay verification + brokerage sync → approval gate → evidence packet handoff |

---

## Related Resources

| Resource | Purpose |
| ---------- | --------- |
| [`../README.md`](../README.md) | Master AI resource index |
| [`../assistant-workflow-contract.md`](../assistant-workflow-contract.md) | Provider-agnostic workflow and alignment rules for all assistant surfaces |
| [`../codex/self-improving-agents.md`](../codex/self-improving-agents.md) | Baseline-to-eval promotion loop for improving agents, prompts, skills, and agent retrieval memory |
| [`../navigation/README.md`](../navigation/README.md) | Navigation workflow guide |
| [`../generated/repo-navigation.md`](../generated/repo-navigation.md) | Generated routing digest |
| [`../skills/README.md`](../skills/README.md) | Skill catalog across Codex and portable packages |
| [`.codex/skills/README.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.codex/skills/README.md) | Current repo-local Codex skills |

---

_Last Updated: 2026-06-20_
