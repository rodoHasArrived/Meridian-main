# AI Agent Definitions

This directory indexes AI agent definitions used in the Meridian project. Copilot agent files live in `.github/agents/`; Claude agent files live in `.claude/agents/`.

Both sets should stay aligned around a common routing model: use the generated repo-navigation artifacts first, then hand off to specialist agents.

---

## Orientation Layer

### Repo Navigation Agent

**Copilot file:** [`.github/agents/repo-navigation-agent.md`](https://github.com/rodoHasArrived/Meridian/blob/main/.github/agents/repo-navigation-agent.md)  
**Claude file:** [`.claude/agents/meridian-navigation.md`](../../../.claude/agents/meridian-navigation.md)

Routes large-repo work to the right subsystem, docs, entrypoints, and downstream specialist agents before implementation starts. It owns four roles:

- `repo-orienter` for subsystem classification
- `task-router` for mapping natural-language requests to repo routes
- `execution-tracer` for high-signal entrypoints and dependency edges
- `doc-router` for authoritative docs and guardrails

Primary inputs:

- [`docs/ai/generated/repo-navigation.md`](../generated/repo-navigation.md)
- [`docs/ai/generated/repo-navigation.json`](../generated/repo-navigation.json)
- [`docs/ai/navigation/README.md`](../navigation/README.md)

---

## GitHub Copilot Agents (`.github/agents/`)

| Agent | Purpose |
|------|---------|
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
| `test-writer-agent.md` | Generate Meridian-style tests |

---

## Claude Code Agents (`.claude/agents/`)

| Agent | Purpose |
|------|---------|
| `meridian-blueprint.md` | Blueprint and design specialist |
| `meridian-cleanup.md` | Cleanup specialist |
| `meridian-docs.md` | Documentation specialist |
| `meridian-navigation.md` | Repo navigation and routing specialist |

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
|----------|---------|
| [`../README.md`](../README.md) | Master AI resource index |
| [`../navigation/README.md`](../navigation/README.md) | Navigation workflow guide |
| [`../generated/repo-navigation.md`](../generated/repo-navigation.md) | Generated routing digest |
| [`../skills/README.md`](../skills/README.md) | Portable skill catalog |

---

*Last Updated: 2026-03-31*
