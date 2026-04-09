# AI Navigation Workflow

This guide explains how assistants should orient inside Meridian before they start making changes or doing deep tracing.

---

## Purpose

Meridian is large enough that broad recursive searching creates avoidable cost, misses authoritative docs, and increases architecture mistakes. The navigation layer solves that by publishing the same routing truth for every AI surface:

- generated markdown for humans and prompt-driven tools
- generated JSON for MCP resources/tools and programmatic consumers
- lightweight agent and skill wrappers that consume the same generated artifacts

---

## Canonical Artifacts

| Artifact | Use |
|----------|-----|
| [`../generated/repo-navigation.json`](../generated/repo-navigation.json) | Canonical machine-readable repo map |
| [`../generated/repo-navigation.md`](../generated/repo-navigation.md) | Human-readable routing digest |
| [`build/scripts/docs/generate-ai-navigation.py`](https://github.com/rodoHasArrived/Meridian-main/blob/main/build/scripts/docs/generate-ai-navigation.py) | Generator that refreshes both artifacts |

The generator is the source of truth. Do not manually edit the generated files.

---

## Routing Order

1. Identify the likely subsystem with the generated repo map or the MCP `find-subsystem` tool.
2. Use `route-task` to map the natural-language request to a high-signal route.
3. Read the authoritative docs for that route.
4. Inspect the first entrypoints and contracts named by the route.
5. Hand off to the relevant specialist skill or agent.

This keeps orientation-first work separate from deeper implementation or tracing.

---

## MCP Surface

If MCP is available, prefer these navigation resources/tools:

| Type | Name | Purpose |
|------|------|---------|
| Resource | `mdc://repo-navigation/catalog` | Full generated navigation dataset |
| Resource | `mdc://repo-navigation/quick-start` | Condensed orientation view |
| Tool | `find-subsystem` | Match a topic to a subsystem |
| Tool | `route-task` | Route a natural-language task to projects, docs, and specialist guidance |
| Tool | `find-entrypoints` | Find key contracts, shell files, and coordinator files |
| Tool | `find-related-projects` | Surface cross-project edges and neighboring projects |
| Tool | `find-authoritative-docs` | Return the docs that should be read first |

---

## AI Surface Mapping

| Surface | Navigation layer |
|---------|------------------|
| Codex | [`.codex/skills/meridian-repo-navigation/SKILL.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.codex/skills/meridian-repo-navigation/SKILL.md) |
| Copilot | [`.github/agents/repo-navigation-agent.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.github/agents/repo-navigation-agent.md) |
| Claude | [`.claude/agents/meridian-navigation.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/.claude/agents/meridian-navigation.md) |
| MCP clients | `RepoNavigationResources` and `RepoNavigationTools` in `src/Meridian.McpServer/` |

---

## Refreshing the Repo Map

Run:

```bash
python3 build/scripts/docs/generate-ai-navigation.py \
  --json-output docs/ai/generated/repo-navigation.json \
  --markdown-output docs/ai/generated/repo-navigation.md \
  --summary
```

Refresh the repo map whenever:

- projects are added, moved, or renamed
- authoritative docs change meaningfully
- new high-signal contracts or task routes should become discoverable
- new AI navigation agents or skills are added

---

## Decision Rule

If the user’s question is mainly about finding the right owner, files, docs, or starting point, use navigation first. If the task is already scoped to a concrete subsystem and file set, skip directly to the specialist guidance.

---

*Last Updated: 2026-03-31*
