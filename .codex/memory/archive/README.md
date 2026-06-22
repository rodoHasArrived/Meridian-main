---
id: codex-memory-archive
scope: .codex/memory/archive/
tier: archive
tags:
  - codex
  - memory
  - archive
confidence: high
freshness: fresh
source_refs:
  - docs/ai/codex/memory-system.md
  - .codex/skills/_shared/codex-execution-contract.md
review_after: 2026-09-19
invalidates_when:
  - Archive-tier audit or loading rules change.
---

# Memory Archive

Use this folder for retired memory entries that should remain auditable but should no longer guide
active Codex work. Archive memory is retained for traceability, not task routing.

## What Belongs Here

- Superseded repo, task, branch, or session memory that still has audit value.
- Retired entries with an archival reason, retirement date, and replacement guidance or canonical
  source reference when one exists.
- Historical context needed to understand why a memory entry stopped loading.

## What Must Not Be Stored Here

- Active guidance that should still route Codex work.
- Secrets, credentials, tokens, personal data, customer data, proprietary external content, raw logs,
  or private environment details.
- Duplicates of canonical docs or generated reports.
- Speculative notes that have no audit value after retirement.

## Expiration And Promotion

- Archived entries do not promote automatically. Restore or re-promote only after a fresh source
  review and index update.
- Archive entries may be deleted when their audit value expires or a replacement canonical source
  fully covers the history.
- Keep archived entries excluded from active loading unless a future audit task explicitly asks for
  retired memory.
