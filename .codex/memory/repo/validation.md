---
id: repo:validation
tier: repo
scope: repo
file: .codex/memory/repo/validation.md
tags:
  - validation
  - commands
  - ai-tooling
  - codex
load_when:
  skills:
    - meridian-docs
    - meridian-implementation-assurance
  paths:
    - build/scripts/docs/**
    - docs/**
    - .codex/**
    - make/**
  intents:
    - validation
    - documentation
    - ai-tooling
  branches: []
  tags:
    - validation
    - ai-tooling
  task:
    ids: []
    work_modes:
      - implementation
      - validation
    intents:
      - validation
      - ai-tooling
    paths:
      - .codex/memory/**
      - build/scripts/docs/**
confidence: high
freshness: fresh
source_refs:
  - AGENTS.md
  - .codex/AGENTS.md
  - .codex/skills/_shared/project-context.md
  - .codex/skills/_shared/codex-execution-contract.md
  - docs/ai/codex/quickstart.md
  - docs/ai/tooling/README.md
review_after: 2026-09-19
invalidates_when:
  - AI tooling validation commands change.
  - Codex execution contract validation gates change.
  - GitHub-hosted targeted-test workflow input contract changes.
---

# Repository Validation Memory

Use this memory when a task touches Codex guidance, AI docs, docs automation, or validation tooling.

- Run `git status --short` before editing and treat unrelated changes as user-owned.
- Prefer the narrowest validation command that covers the files changed.
- For docs-only Markdown edits, use `git diff --check -- <paths>`.
- For Codex skill, prompt, catalog, checklist, memory, or AI workflow changes, start with the
  deterministic AI checks:
  - `python build/scripts/docs/check-codex-memory.py --summary`
  - `python build/scripts/docs/check-codex-skills.py --summary`
  - `python build/scripts/docs/check-ai-inventory.py --summary`
- For local .NET tests, prefer `python build/python/cli/buildctl.py test --project <project>
  --filter "<filter>" --queue` when agent-triggered validation should avoid parallel collisions.
- If local CPU, memory, disk, restore, dependency, or MSBuild contention blocks reliable proof, use
  GitHub Actions `Targeted Test` after pushing the branch, with a repo-relative test project under
  `tests/` and a scoped `dotnet_filter`.
- GNU Make targets are optional convenience wrappers; use direct `dotnet`, `npm`, `pwsh`, and
  `python` commands when `make` is unavailable.
