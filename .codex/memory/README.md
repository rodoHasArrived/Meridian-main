---
id: codex-memory-root
scope: .codex/memory/
tier: repo
tags:
  - codex
  - memory
  - navigation
confidence: high
freshness: fresh
source_refs:
  - docs/ai/codex/memory-system.md
  - .codex/memory/index.yml
review_after: 2026-09-19
invalidates_when:
  - Codex memory layout changes.
---

# Codex Memory

This directory stores Meridian repo-local Codex memory. Memory supplements canonical docs, source,
tests, scripts, and skill instructions; it never overrides them.

## Layout

- `index.yml` - machine-readable catalog, schema, routing metadata, and memory entries.
- `repo/` - durable repository-level memory backed by current source references.
- `tasks/` - task descriptors (`*.yml`) plus indexed task-scoped Markdown memory for active or recurring work.
- `goals/` - long-running Codex goal inventories that track progress and active task descriptors.
- `branches/` - branch-scoped notes that expire when the branch merges or is abandoned.
- `sessions/` - temporary session notes and continuation breadcrumbs.
- `archive/` - retired memory retained for auditability.

## Maintenance Rules

- Keep the required structure present: `README.md`, `index.yml`, and `repo/`, `tasks/`, `goals/`, `branches/`, `sessions/`, and `archive/` folders, each with its own `README.md`.
- Prefer canonical docs over memory when facts disagree.
- Record stable claims with explicit `source_refs`, review dates, and invalidation triggers.
- Keep temporary findings in `sessions/`, `tasks/`, or `branches/` until promotion is reviewed.
- Use `tasks/*.yml` descriptors to route memory for active Codex tasks; do not treat descriptor
  files as durable memory entries.
- Use `goals/*.yml` inventories for very long Codex goals; keep progress evidence compact and link
  the active task descriptor instead of copying task memory into the goal file.
- Do not store secrets, credentials, tokens, personal data, customer data, proprietary external content, raw logs, or speculative assumptions here.
- Seed `repo/` only with stable, sourced claims; keep task-local guesses and unverified observations in narrower tiers until they expire or are deliberately promoted.
- Run `python build/scripts/docs/check-codex-memory.py --summary` after memory changes.
