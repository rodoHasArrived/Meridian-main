# Blueprint Mode — Pipeline Position

This document describes where Blueprint Mode fits in the Meridian ideation-to-implementation pipeline,
the inputs and outputs of each stage, and the handoff contracts between stages.

---

## Full Pipeline Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    Meridian Ideation-to-Implementation Pipeline               │
└─────────────────────────────────────────────────────────────────────────┘

Stage 1: Brainstorm (meridian-brainstorm)
  Skill: .claude/skills/meridian-brainstorm/SKILL.md
  Input:  User prompt / pain point / persona / domain
  Output: Idea cards — narrative ideas with effort/audience/impact/depends-on table
  ↓

Stage 2: Idea Evaluator  [optional / ad-hoc]
  Input:  Idea cards from Stage 1
  Output: Scored, tiered ideas (Tier 1 = high value, Tier 3 = park for later)
  ↓

Stage 3: Roadmap Builder  [optional / ad-hoc]
  Input:  Scored ideas from Stage 2
  Output: Phased plan — Phase 1 (foundation), Phase 2 (features), Phase 3 (advanced)
         Ideas assigned to phases by dependencies and value
  ↓

Stage 4: Blueprint Mode (meridian-blueprint)   ◄── THIS SKILL
  Skill: .claude/skills/meridian-blueprint/SKILL.md
  Input:  One idea card from Stage 1, one Roadmap phase item, or a user-specified idea
  Output: Complete technical design document (Steps 1–9)
  ↓

Stage 5: Implementation  [developer]
  Input:  Blueprint from Stage 4
  Output: Working code (C# / F# / XAML)
  ↓

Stage 6: Code Review (meridian-code-review)
  Skill: .claude/skills/meridian-code-review/SKILL.md
  Input:  Implemented code
  Output: Compliance report — MVVM, ADR, hot-path, error handling, test quality
  ↓

Stage 7: Test Writing (meridian-test-writer)
  Skill: .claude/skills/meridian-test-writer/SKILL.md
  Input:  Implemented code + test plan from Blueprint Step 7
  Output: xUnit test files with FluentAssertions, complete coverage per plan
```

---

## Stage 4 (Blueprint Mode) — Inputs

### Required

| Input | Source | Description |
|-------|--------|-------------|
| `idea` | Stage 1 idea card, Roadmap phase item, or user prompt | The single feature to blueprint |

### Optional

| Input | Source | Description |
|-------|--------|-------------|
| `idea_context` | Stage 2 evaluator output | Scores, tier assignment, competitive analysis |
| `constraints` | User specification | `must_not_break`, `target_sprint`, `team_size` |
| `depth` | User specification | `full` (default), `spike`, or `interface-only` |
| `--json` | User flag | Also produce `blueprint.json` summary |

---

## Stage 4 (Blueprint Mode) — Outputs

### Always produced

| Output | Format | Description |
|--------|--------|-------------|
| Blueprint document | Markdown (Steps 1–9) | Complete technical design |
| Implementation checklist | Markdown task list | Ordered, sprint-sized tasks |
| Test plan | Markdown table | Named tests with verification and setup |

### Optionally produced

| Output | Format | Trigger |
|--------|--------|---------|
| `blueprint.json` | JSON | When `--json` flag is passed |
| GitHub issue | Via MCP | When user requests issue creation |

---

## Stage 4 → Stage 5 Handoff Contract

The blueprint document constitutes the contract between Blueprint Mode and the implementing
developer. The developer should be able to open the blueprint and immediately:

1. Know what to build (scope, interfaces, component design)
2. Know how to build it (data flow, concurrency model, error handling)
3. Know what the XAML should look like (if applicable)
4. Know what tests to write (test plan with named tests)
5. Know what tasks to create in their sprint (implementation checklist)

**The developer should NOT need to:**
- Decide on interface names or method signatures
- Decide on namespaces or DI lifetimes
- Decide on concurrency or error handling strategy
- Make up test names or coverage targets

If any of these are missing from the blueprint, the blueprint is incomplete.

---

## Stage 4 → Stage 6 Handoff Contract

The blueprint defines the contracts that `meridian-code-review` validates against. Specifically:

| Blueprint Section | Code Review Validation |
|-------------------|----------------------|
| Step 3 (Interface contracts) | Lens 6: `[ImplementsAdr]` attribute compliance |
| Step 4 (Component design) | Lens 1: MVVM compliance; Lens 5: Provider SDK compliance |
| Step 5 (Data flow) | Lens 2: Hot-path performance; Lens 3: Error handling |
| Step 7 (Test plan) | Lens 4: Test code quality and coverage |

When code review finds a deviation from the blueprint's interface contracts, it should cite
the blueprint's Step 3 definition as the reference.

---

## Stage 4 → Stage 7 Handoff Contract

The blueprint's **Step 7: Test Plan** is the direct input to `meridian-test-writer`. The test plan
defines:

- Which tests to write (by name and what they verify)
- Which test double strategy to use
- What new test infrastructure is needed

`meridian-test-writer` translates the test plan table rows into actual xUnit test methods with
FluentAssertions and the correct mock/stub setup.

---

## Stage Bypass Rules

Blueprint Mode can be invoked directly without prior Brainstorm/Roadmap stages when:

1. The user specifies a well-defined feature directly ("Blueprint the WAL repair command")
2. The idea is already in the Roadmap and doesn't need ideation scoring
3. The idea is a bug fix with a clear technical approach (use `depth=interface-only` for bug
   fixes that require API changes)

Blueprint Mode should **not** be invoked when:

1. The idea is still exploratory ("I want to do something with data quality") — use brainstorm
   first
2. The user wants to compare multiple approaches — use brainstorm (Architecture mode) first
3. The idea is a multi-feature epic — decompose in Roadmap Builder first, then blueprint each
   phase item separately

---

## Skill Cross-References

| Stage | Skill | GitHub Agent |
|-------|-------|-------------|
| Brainstorm | `.claude/skills/meridian-brainstorm/SKILL.md` | `.github/agents/meridian-brainstorm-agent.md` |
| Blueprint | `.claude/skills/meridian-blueprint/SKILL.md` | `.github/agents/meridian-blueprint-agent.md` |
| Code Review | `.claude/skills/meridian-code-review/SKILL.md` | `.github/agents/code-review-agent.md` |
| Provider Build | `.claude/skills/meridian-provider-builder/SKILL.md` | `.github/agents/meridian-provider-builder-agent.md` |
| Test Writing | `.claude/skills/meridian-test-writer/SKILL.md` | `.github/agents/meridian-test-writer-agent.md` |

---

*Last Updated: 2026-03-17*
