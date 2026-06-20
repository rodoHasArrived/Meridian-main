---
id: repo:ai-guidance
tier: repo
scope: repo
file: .codex/memory/repo/ai-guidance.md
tags:
  - ai-guidance
  - codex
  - workflow
  - skills
load_when:
  skills:
    - meridian-docs
    - meridian-implementation-assurance
    - meridian-codex-skill-builder
  paths:
    - AGENTS.md
    - docs/ai/**
    - .codex/**
    - build/scripts/docs/**
    - make/ai.mk
  intents:
    - ai-guidance
    - ai-tooling
    - documentation
    - skill-routing
  branches: []
  tags:
    - ai-guidance
    - codex
    - skills
confidence: high
freshness: fresh
source_refs:
  - AGENTS.md
  - .codex/AGENTS.md
  - .codex/skills/_shared/project-context.md
  - .codex/skills/_shared/codex-execution-contract.md
  - docs/ai/assistant-workflow-contract.md
  - docs/ai/codex/README.md
  - docs/ai/codex/quickstart.md
review_after: 2026-09-19
invalidates_when:
  - Codex skill routing changes.
  - Shared AI workflow contract changes.
  - Codex quickstart or execution contract changes.
---

# Repository AI Guidance Memory

Use this memory when changing Codex skills, AI workflow guidance, prompt routing, or memory itself.

- Treat root `AGENTS.md`, `CLAUDE.md`, `.codex/skills/_shared/project-context.md`, and
  `.codex/skills/_shared/codex-execution-contract.md` as the Codex-loaded development baseline.
- Keep `.codex/skills/` as the canonical repo-local Codex skill set, and choose the narrowest skill
  lane that matches the user's request.
- Put Codex-specific docs under `docs/ai/codex/` when they belong in canonical documentation rather
  than working memory.
- When shared development, validation, workflow, prompt, skill, or agent rules change, inspect the
  mirrored assistant surfaces named by the canonical Codex guidance before deciding whether they
  need edits.
- Before editing `src/**`, read the nearest source README, identify the module in
  `docs/source/data/source-modules.yml`, and update source README or registry records when behavior,
  validation, ownership, diagrams, or TODO scope changes.
- Do not hand-edit generated roadmap, source, or AI docs. Update the registry data or generator
  inputs and rerun the narrow generator when generated output must change.
- Keep memory claims conservative: if a fact is uncertain or temporary, store it in session, task,
  or branch memory instead of promoting it to `repo/`.
