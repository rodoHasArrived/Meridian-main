---
id: codex-memory-sessions
scope: .codex/memory/sessions/
tier: session
tags:
  - codex
  - memory
  - sessions
confidence: high
freshness: fresh
source_refs:
  - docs/ai/codex/memory-system.md
  - .codex/skills/_shared/codex-execution-contract.md
review_after: 2026-09-19
invalidates_when:
  - Session-tier scope or promotion rules change.
---

# Session Memory

Use this folder for session-local observations, continuation breadcrumbs, and context that should not
outlive the immediate work unless later promoted with source evidence.

## What Belongs Here

- Temporary observations from the current Codex session that may help after compaction or thread
  continuation.
- Short breadcrumbs about inspected files, skipped routes, local blockers, or validation context for
  the current session.
- Session-scoped Markdown entries that are narrow, dated, and clearly non-canonical.

## What Must Not Be Stored Here

- Stable repository guidance that belongs in `repo/`.
- Secrets, credentials, tokens, personal data, customer data, proprietary external content, raw logs,
  or long command transcripts.
- Speculative conclusions presented as facts, task-local guesses that need no continuation, or
  unrelated worktree notes.
- Content that should instead live in canonical docs, tests, source code, or issue trackers.

## Expiration And Promotion

- Session memory expires at session end, after compaction when no longer needed, or when the active
  work moves to a task, branch, or goal inventory.
- Promote session notes only after verifying them against current repo evidence and deciding whether
  the correct destination is `tasks/`, `branches/`, `goals/`, or `repo/`.
- Delete session notes that were only useful for the completed interaction; archive only when an
  audit trail is genuinely useful.
