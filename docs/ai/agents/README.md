# AI Agent Definitions

This directory indexes AI agent definitions used in the Meridian project. GitHub Copilot agent
files live in `.github/agents/`; Claude agent files live in `.claude/agents/`; Codex specialist
profiles live in `.codex/agents/`. Repo-local Codex skills live in `.codex/skills/` and provide
the primary current project-scoped workflow surface.
All agent surfaces should follow the shared provider-agnostic workflow in
[`../assistant-workflow-contract.md`](../assistant-workflow-contract.md).

All three surfaces should stay aligned around the same current product framing: Meridian is a
.NET 10 fund-management and trading platform with an active browser-based operator workstation,
active WPF operator work, and visible navigation limited to `Trading`, `Portfolio`, `Accounting`,
`Reporting`, `Strategy`, `Data`, and `Settings` on top of strong provider, storage, execution,
ledger, and MCP foundations.

When an agent lane needs validator, route, handoff-packet, or maintenance-script selection, load
[`../tooling/README.md`](../tooling/README.md) instead of rediscovering command lanes from scratch.

---

## Orientation Layer

### Repo Navigation Agent

**Copilot file:** [`.github/agents/repo-navigation-agent.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.github/agents/repo-navigation-agent.md)

**Claude files:** [`.claude/agents/meridian-navigation.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.claude/agents/meridian-navigation.md),
[`.claude/agents/meridian-repo-navigation.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.claude/agents/meridian-repo-navigation.md)

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
| `meridian-code-review.toml` | Review changes for bugs, regressions, and architecture drift |
| `meridian-docs.toml` | Maintain documentation and AI guidance |
| `meridian-implementation-assurance.toml` | Verify implementation completeness, evidence, docs sync, and guardrails |
| `meridian-navigation.toml` | Route tasks through Meridian repo-navigation context |
| `meridian-provider-builder.toml` | Build or extend ProviderSdk-compliant data providers |
| `meridian-repo-navigation.toml` | Orient large-repo tasks before deeper work |
| `meridian-roadmap-strategist.toml` | Reconcile roadmap, delivery-plan, and target-state docs |
| `meridian-simulated-user-panel.toml` | Run structured simulated-user feedback workflows |
| `meridian-test-writer.toml` | Write scenario-first Meridian tests |
| `meridian-user-panel.toml` | Legacy simulated-user panel alias for compatibility |
| `modular-desktop-mvvm.toml` | Implement modular WPF MVVM workstation changes |
| `performance-resource-review.toml` | Review memory, CPU, I/O, rendering, concurrency, and lifecycle risks |
| `provider-management-workflow.toml` | Build secure provider setup, health, validation, and recovery workflows |
| `research-data-acquisition.toml` | Build research acquisition, preview, validation, lineage, and cleanup workflows |
| `safe-refactoring.toml` | Refactor desktop and shared code incrementally without behavior drift |
| `shared-component-extraction.toml` | Extract repeated desktop patterns into reusable components |
| `workstation-screen-composition.toml` | Compose desktop screens from shared workstation primitives |

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

| Agent | Purpose |
| ------ | --------- |
| [`meridian-archive-organizer.md`](../../../.claude/agents/meridian-archive-organizer.md) | Archive stale files and keep repository structure tidy |
| [`meridian-blueprint.md`](../../../.claude/agents/meridian-blueprint.md) | Blueprint and design specialist |
| [`meridian-cleanup.md`](../../../.claude/agents/meridian-cleanup.md) | Cleanup specialist |
| [`meridian-docs.md`](../../../.claude/agents/meridian-docs.md) | Documentation specialist |
| [`meridian-navigation.md`](../../../.claude/agents/meridian-navigation.md) | Repo navigation and routing specialist |
| [`meridian-repo-navigation.md`](../../../.claude/agents/meridian-repo-navigation.md) | Generated-map-based repo navigation specialist |
| [`meridian-roadmap-strategist.md`](../../../.claude/agents/meridian-roadmap-strategist.md) | Roadmap, delivery-plan, and target-state specialist |
| [`meridian-user-panel.md`](../../../.claude/agents/meridian-user-panel.md) | Manifest-driven user-panel specialist for design-partner, release-gate, and usability-lab reviews |

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
                -> [Multi-domain / gated]  -> CoS Runtime (ADK)      -> Specialist Agent/Skill -> Approval Gate -> Trace/Evidence
```

Use repo navigation first whenever the main problem is "where should I start?" rather than "how do I implement this detail?"

Choose the CoS runtime path when the task crosses multiple subsystems, requires an approval gate
or operator sign-off, or needs a structured briefing with trace/evidence retention. See
`tools/chief-of-staff-runtime/runtime.py` and the operator runbook guidance in `docs/operators/README.md`.

Coordinator agents should assign one narrow concern, a compact file set, and a validation owner to
each specialist lane. Specialist agents should load only the required context for that lane, return
the `required context` vs `optional context` split from
[`../agent-handoff-checklist.md`](../agent-handoff-checklist.md), and record rerun triggers before
handoff so downstream agents do not repeat discovery or validation without cause.

## AI Contract Coverage

- Repo navigation: `../navigation/README.md`, `../generated/repo-navigation.md`
- Agent edit rules: `../assistant-workflow-contract.md`, `.codex/skills/_shared/project-context.md`,
  `.agents/skills/_shared/project-context.md`, `.claude/skills/_shared/project-context.md`
- Generated-file handling: AI and repo-navigation generated artifacts are owned by their generators;
  do not hand-edit docs under `docs/ai/generated/` or generator outputs in `docs/generated/`.
- Agent orchestration: `docs/ai/parallel-task-manifest-template.md`, `docs/ai/agent-handoff-checklist.md`,
  and `.codex/agents/` profiles for cross-lane routing
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
| **Hierarchical** | A coordinator delegates to specialist agents, aggregates evidence, and enforces approval gates | DK1 readiness: provider validation + replay verification + brokerage sync → approval gate → promotion via CoS runtime |

---

## Related Resources

| Resource | Purpose |
| ---------- | --------- |
| [`../README.md`](../README.md) | Master AI resource index |
| [`../assistant-workflow-contract.md`](../assistant-workflow-contract.md) | Provider-agnostic workflow and alignment rules for all assistant surfaces |
| [`../navigation/README.md`](../navigation/README.md) | Navigation workflow guide |
| [`../generated/repo-navigation.md`](../generated/repo-navigation.md) | Generated routing digest |
| [`../skills/README.md`](../skills/README.md) | Skill catalog across Codex and portable packages |
| [`.codex/skills/README.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.codex/skills/README.md) | Current repo-local Codex skills |

---

_Last Updated: 2026-06-03_
