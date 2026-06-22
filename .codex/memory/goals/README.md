---
id: codex-memory-goals
scope: .codex/memory/goals/
tier: task
tags:
  - codex
  - memory
  - goals
confidence: high
freshness: fresh
source_refs:
  - docs/ai/codex/memory-system.md
  - .codex/skills/_shared/codex-execution-contract.md
review_after: 2026-09-19
---

# Goal Inventories

Use this folder for YAML inventories that let Codex resume very long goals after compaction,
interruption, or thread continuation.

Goal inventories are not indexed memory entries. They should track the objective, current status,
active task descriptor, progress items, evidence references, next actions, open questions, and
promotion candidates. Keep durable guidance in indexed Markdown memory; keep the goal file focused
on progress toward the active objective.
