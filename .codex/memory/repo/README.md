---
id: codex-memory-repo
scope: .codex/memory/repo/
tier: repo
tags:
  - codex
  - memory
  - repo
confidence: high
freshness: fresh
source_refs:
  - docs/ai/codex/memory-system.md
  - .codex/memory/index.yml
review_after: 2026-09-19
invalidates_when:
  - Codex memory layout or repo-tier promotion rules change.
---

# Repository Memory

Use this folder for durable Meridian repository memory that is stable, sourced, and useful across
future Codex tasks. Repo memory supplements canonical docs, source, tests, scripts, AGENTS.md files,
and selected skills; it never overrides those sources.

## What Belongs Here

- Stable repository conventions that are expensive to rediscover and repeatedly useful.
- Current architecture, validation, AI-guidance, or workflow facts backed by canonical source files.
- Entries listed in `.codex/memory/index.yml` with matching front matter, review dates,
  `source_refs`, confidence, freshness, and invalidation triggers.
- Claims that are expected to remain true beyond the current branch, session, task, or goal.

## What Must Not Be Stored Here

- Speculative observations, task-local guesses, one-off command output, raw logs, or scratch notes.
- Secrets, credentials, tokens, personal data, customer data, proprietary external content, or
  environment-specific private details.
- Branch-only merge notes, session breadcrumbs, unfinished investigation notes, or unsupported
  conclusions.
- Durable instructions that conflict with canonical repository docs, source code, tests, direct user
  instructions, or scoped AGENTS.md guidance.

## Expiration And Promotion

- Every repo entry must define `review_after` and `invalidates_when`; review it before trusting it
  after the review date or when an invalidation condition occurs.
- Promote from `sessions/`, `tasks/`, or `branches/` only after the claim is confirmed by current
  repository evidence and is useful beyond the original scope.
- Do not promote raw notes directly. Rewrite promoted entries as concise, sourced claims and update
  `.codex/memory/index.yml` in the same change.
- Move stale or superseded repo entries to `archive/` when they retain audit value; otherwise remove
  them with a clear replacement in the index or canonical docs.
