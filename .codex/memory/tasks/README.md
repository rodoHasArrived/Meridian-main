---
id: codex-memory-tasks
scope: .codex/memory/tasks/
tier: task
tags:
  - codex
  - memory
  - tasks
confidence: high
freshness: 2026-06-19
source_refs:
  - docs/ai/codex/quickstart.md
  - .codex/skills/_shared/codex-execution-contract.md
review_after: 2026-09-19
---

# Task Memory

Use this folder for two task-scoped memory surfaces:

- `*.yml` task descriptors that route the current Codex task by `task_id`, intent, selected skill,
  work mode, branch, planned paths, and explicit memory tags.
- Indexed Markdown entries that help future agents continue recurring or active work but are not
  stable repository facts.

Task descriptors are not indexed memory entries and must not contain durable guidance by
themselves. Promote a Markdown entry to `repo/` only after it is backed by canonical documentation
and is expected to remain durable.
