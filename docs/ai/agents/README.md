# AI Agent Definitions

This directory indexes AI agent definitions used in the Meridian project. GitHub Copilot agent
files live in `.github/agents/`; Claude agent files live in `.claude/agents/`. Repo-local Codex
skills live in `.codex/skills/` and provide the primary current project-scoped workflow surface.
All agent surfaces should follow the shared provider-agnostic workflow in
[`../assistant-workflow-contract.md`](../assistant-workflow-contract.md).

All three surfaces should stay aligned around the same current product framing: Meridian is a
.NET 9 fund-management and trading platform with an active browser-based operator workstation,
retained WPF support, and visible navigation limited to `Trading`, `Portfolio`, `Accounting`,
`Reporting`, `Strategy`, `Data`, and `Settings` on top of strong provider, storage, execution,
ledger, and MCP foundations.

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
| `simulated-user-panel-agent.md` | Run manifest-driven owner-minded user panels across design-partner, release-gate, and usability-lab modes |
| `test-writer-agent.md` | Generate Meridian-style tests |

---

## Claude Code Agents (`.claude/agents/`)

| Agent | Purpose |
| ------ | --------- |
| `meridian-archive-organizer.md` | Archive stale files and keep repository structure tidy |
| `meridian-blueprint.md` | Blueprint and design specialist |
| `meridian-cleanup.md` | Cleanup specialist |
| `meridian-docs.md` | Documentation specialist |
| `meridian-navigation.md` | Repo navigation and routing specialist |
| `meridian-repo-navigation.md` | Generated-map-based repo navigation specialist |
| `meridian-roadmap-strategist.md` | Roadmap, delivery-plan, and target-state specialist |
| `meridian-user-panel.md` | Manifest-driven user-panel specialist for design-partner, release-gate, and usability-lab reviews |

---

## Pipeline Position

The intended routing flow is:

```text
Repo Navigation -> Specialist Agent/Skill -> Implementation -> Review -> Testing/Assurance
```

Use repo navigation first whenever the main problem is “where should I start?” rather than “how do I implement this detail?”

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

_Last Updated: 2026-04-29_
