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
invalidates_when:
  - Goal inventory schema or long-goal progress rules change.
---

# Goal Inventories

Use this folder for YAML inventories that let Codex resume very long goals after compaction,
interruption, or thread continuation. Goal inventories track progress; they are not durable memory
entries.

## What Belongs Here

- `*.yml` goal inventories with objective, status, active task descriptor, progress items, evidence
  references, next actions, open questions, and promotion candidates.
- Compact evidence references showing why a progress item is complete, blocked, deferred, or still
  pending.
- Links to task descriptors or canonical docs instead of copied task memory.

## What Must Not Be Stored Here

- Secrets, credentials, tokens, personal data, customer data, proprietary external content, raw logs,
  or large transcripts.
- Stable repository claims that should be indexed under `repo/`.
- Unreviewed speculation, task-local guesses, or broad design instructions without source evidence.
- Durable guidance hidden in progress records; reusable facts require normal memory promotion.

## Expiration And Promotion

- Goal inventories expire when the long-running goal is complete, abandoned, or superseded, or when
  their review date/invalidation condition is reached.
- Use promotion candidates only as a review queue. Promotion requires rewriting the claim into an
  indexed Markdown memory entry with source refs.
- Archive completed or abandoned inventories when their progress trail remains useful for audit;
  otherwise remove obsolete inventories in a focused cleanup.
