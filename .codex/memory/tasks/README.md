---
id: codex-memory-tasks
scope: .codex/memory/tasks/
tier: task
tags:
  - codex
  - memory
  - tasks
confidence: high
freshness: fresh
source_refs:
  - docs/ai/codex/memory-system.md
  - .codex/skills/_shared/codex-execution-contract.md
review_after: 2026-09-19
invalidates_when:
  - Task descriptor schema or task-tier routing rules change.
---

# Task Memory

Use this folder for task-scoped memory that helps a named Codex task load only the context it needs.
Task memory is narrower than repo memory and must not become durable guidance without promotion
review.

## What Belongs Here

- `*.yml` task descriptors that route an active Codex task by `task_id`, intent, selected skill,
  work mode, branch, planned paths, and explicit memory tags.
- Indexed Markdown entries for recurring or active task context that is useful while the named task
  remains open.
- Compact promotion candidates that point to source evidence for later review.

## What Must Not Be Stored Here

- Stable repository guidance that already qualifies for `repo/`.
- Secrets, credentials, tokens, personal data, customer data, proprietary external content, or raw
  logs.
- Broad assumptions from unrelated tasks, speculative design notes, or unverified conclusions.
- Durable facts embedded only in task descriptor YAML files; descriptors are routing inputs, not
  indexed memory entries.

## Expiration And Promotion

- Task descriptors expire when the task closes, the prompt family stops recurring, or their review
  date/invalidation condition is reached.
- Promote task Markdown entries to `repo/` only when current canonical sources support the claim and
  the guidance will remain useful after the task ends.
- Move task entries with audit value to `archive/` after completion when they should not continue to
  guide active work.
- Delete short-lived task descriptors when they no longer route active or recurring work.
