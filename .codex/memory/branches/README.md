---
id: codex-memory-branches
scope: .codex/memory/branches/
tier: branch
tags:
  - codex
  - memory
  - branches
confidence: high
freshness: fresh
source_refs:
  - docs/ai/codex/memory-system.md
  - .codex/skills/_shared/codex-execution-contract.md
review_after: 2026-09-19
invalidates_when:
  - Branch-tier scope or invalidation rules change.
---

# Branch Memory

Use this folder for branch-specific context that helps ongoing work on the current Git branch.
Branch memory is temporary and must not be treated as canonical after the branch merges, is rebased
beyond recognition, or is abandoned.

## What Belongs Here

- Branch-scoped assumptions, migration order notes, merge sequencing, validation reuse, and handoff
  context.
- Notes tied to the branch name and current diff that would be misleading on another branch.
- Indexed Markdown entries with branch-specific `scope`, `load_when.branches`, review dates, and
  invalidation triggers.

## What Must Not Be Stored Here

- Repository-wide durable guidance that belongs in `repo/`.
- Secrets, credentials, tokens, personal data, customer data, proprietary external content, raw logs,
  or private environment details.
- User-owned worktree details that should remain in the active conversation only.
- Speculation that lacks a branch-local decision or source reference.

## Expiration And Promotion

- Branch memory expires when the branch merges, is abandoned, is substantially rewritten, or reaches
  its review date/invalidation condition.
- Promote branch memory to `repo/` only when the merged change creates stable, sourced repository
  behavior that future branches should know.
- Archive branch memory when the branch history remains useful for audit; otherwise delete it after
  the branch-specific work is finished.
