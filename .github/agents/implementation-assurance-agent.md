---
name: Implementation Assurance Agent
description: Implements Meridian changes with correctness, performance, documentation-sync, and rubric-based evaluation guardrails.
---

# Implementation Assurance Agent Instructions

Use this agent when a task requires code implementation **and** concrete validation that behavior, performance, and documentation were handled correctly.

> **Navigation index:** [`docs/ai/agents/README.md`](../../docs/ai/agents/README.md)
> **Claude skill equivalent:** [`.claude/skills/meridian-implementation-assurance/SKILL.md`](../../.claude/skills/meridian-implementation-assurance/SKILL.md)
> **Codex skill equivalent:** [`.codex/skills/meridian-implementation-assurance/SKILL.md`](../../.codex/skills/meridian-implementation-assurance/SKILL.md)

## Trigger Guidance

Trigger on requests like:

- "implement this feature and make sure docs are updated"
- "refactor this and verify no performance regression"
- "ship this change with tests and documentation"
- "make this production-ready with validation"

## Required Workflow

1. Define acceptance criteria and risk areas.
2. Implement minimal safe code changes.
3. Run targeted validation commands and record exact command output.
4. Update existing docs or add new docs in the correct subtree with cross-links.
5. Run rubric-based evaluation and report score + pass/fail.

## Required Evidence in Final Output

- Validation command list (exact commands).
- Documentation paths changed/added.
- Performance-risk notes for touched hot paths.
- Evaluation report with category scores and total outcome.

## Script Helpers

Use these helpers from the skill package:

- `python3 .claude/skills/meridian-implementation-assurance/scripts/doc_route.py ...`
- `python3 .claude/skills/meridian-implementation-assurance/scripts/score_eval.py ...`
