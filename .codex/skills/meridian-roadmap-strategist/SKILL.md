---
name: meridian-roadmap-strategist
description: Create, refresh, and reconcile Meridian roadmap, delivery-plan, opportunity-map, and target-state documents. Use when the user asks for a roadmap, roadmap update, phased plan, delivery waves, opportunity analysis, product-direction summary, remaining-work summary, or a clear statement of Meridian's intended finished product.
---

# Meridian Roadmap Strategist

Turn Meridian status, plan, and codebase signals into a roadmap another teammate can use to prioritize work.

Read `../_shared/project-context.md` and `../_shared/codex-execution-contract.md` first. Read
`references/roadmap-source-map.md` before deciding what is complete, what is still open, what new
opportunities exist, or what the end-state product should be.

## Use When

Use this skill when the requested artifact is a roadmap, delivery wave, opportunity map, target
state, product-direction reconciliation, or remaining-work summary.

Trigger examples:

- "Refresh the roadmap for the browser workstation pivot."
- "What remains before paper trading cockpit readiness?"
- "Reconcile these plan files into next delivery waves."

## Do Not Use When

Use `meridian-brainstorm` for unconstrained idea generation, `meridian-blueprint` for one selected
technical design, and `meridian-implementation-assurance` for building or certifying work.

Non-trigger examples:

- "Give me ten product ideas."
- "Design the data flow for this selected feature."
- "Implement the highest-priority roadmap item."

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

## Handoffs

- Hand off to `meridian-brainstorm` when the user wants more candidate opportunities before committing waves.
- Hand off to `meridian-blueprint` when one roadmap item needs a technical spec.
- Hand off to `meridian-implementation-assurance` when roadmap updates require implementation evidence or AI/docs catalog synchronization.

## Validation

- Ground status claims in current repo docs, generated status artifacts, and nearby implementation evidence.
- Use exact dates when comparing plan snapshots or current status.
- Run docs or AI inventory checks when changing AI-facing roadmap guidance or skill catalogs.

## Source Rules

- Prefer repository-grounded documents over memory.
- Treat `docs/status/ROADMAP.md` as the primary active roadmap unless the user asks for a new artifact.
- Treat `docs/plans/web-ui-development-pivot.md` as the browser-workstation implementation-direction companion to the roadmap.
- Cross-check roadmap claims against nearby status, plan, audit, and architecture documents before marking work complete.
- Distinguish shipping work from aspirational ideas.
- Call out dependencies, blockers, and optional items explicitly.
- Keep mobile development out of scope unless the roadmap or user explicitly reopens it.

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
- why new operator-facing UI should land in the browser workstation while WPF remains retained support
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

## Output Standards

- Separate complete, partial, planned, optional, and blocked work.
- Tie every recommended wave to user value, dependency, and evidence.
- Keep implementation claims conservative unless backed by tests, generated artifacts, or active docs.
