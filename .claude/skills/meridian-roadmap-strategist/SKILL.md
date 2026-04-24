---
name: meridian-roadmap-strategist
description: Create, refresh, and reconcile Meridian roadmap, delivery-plan, opportunity-map, and target-state documents. Use when the user asks for a roadmap, roadmap update, phased plan, delivery waves, opportunity analysis, product-direction summary, remaining-work summary, or a clear statement of Meridian's intended finished product.
---

# Meridian Roadmap Strategist

Turn Meridian status, plan, and codebase signals into a roadmap another teammate can use to prioritize work.

Read `../_shared/project-context.md` first. Read `references/roadmap-source-map.md` before deciding what is complete, what is still open, what new opportunities exist, or what the end-state product should be.

## Workflow

1. Identify the requested artifact:
   - master roadmap refresh
   - time-boxed roadmap
   - opportunity scan
   - end-state product summary
   - combined roadmap plus opportunities plus target-state narrative
2. Ground the work in current repository evidence before writing conclusions.
3. Separate facts into four buckets: complete, partial, planned, and optional.
4. Reconcile conflicts across roadmap, plan, audit, and status documents instead of repeating them blindly.
5. Turn gaps into prioritized opportunities with a reason each item matters now.
6. State the end product clearly: what Meridian becomes for the user when the roadmap is finished.
7. Use exact dates when refreshing status documents or comparing planning snapshots.

## Source Rules

- Prefer repository-grounded documents over memory.
- Treat `docs/status/ROADMAP.md` as the primary active roadmap unless the user asks for a new artifact.
- Cross-check roadmap claims against nearby status, plan, audit, and architecture documents before marking work complete.
- Distinguish shipping work from aspirational ideas.
- Call out dependencies, blockers, and optional items explicitly.

## Opportunity Rules

When suggesting opportunities, prefer one of these categories:

- workflow completion
- operator UX
- provider readiness
- architecture simplification
- reliability and observability
- testing and validation
- flagship product capabilities

For each opportunity, explain:

- the gap
- the user or operator value
- the dependency it unlocks
- whether it belongs in the critical path, a later wave, or an optional track

## End-State Rules

When the user wants the final product outcome, describe Meridian in product terms, not only task lists.

Cover these areas when relevant:

- the operator workflow Meridian supports end to end
- the major workspaces or product surfaces
- how research, backtesting, paper trading, live trading, portfolio, and ledger experiences connect
- what is first-class versus supporting infrastructure
- what optional capabilities remain optional

## Output Shapes

Prefer one of these structures:

```md
## Summary
## Current State
## What Is Complete
## What Remains
## Opportunities
## Target End Product
## Recommended Next Waves
## Risks and Dependencies
```

For a shorter artifact, use:

```md
## Snapshot
## Top Opportunities
## End State
## Next Steps
```

## Quality Bar

- Keep roadmap language concrete and repo-grounded.
- Prefer delivery waves and dependency-aware sequencing over flat backlogs.
- Mark assumptions when evidence is incomplete.
- Avoid inflating completion status.
- Keep the target-state narrative crisp enough that a stakeholder can repeat it in one paragraph.
