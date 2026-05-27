# Roadmap Source Map

Use this file to decide which Meridian sources to read first and how to shape roadmap outputs.

## Source Priority

1. `docs/status/ROADMAP.md`
2. `docs/status/FEATURE_INVENTORY.md`
3. `docs/status/FULL_IMPLEMENTATION_TODO_*.md`
4. `docs/plans/trading-workstation-migration-blueprint.md`
5. `docs/plans/meridian-6-week-roadmap.md`
6. `docs/status/IMPROVEMENTS.md`
7. `docs/status/EVALUATIONS_AND_AUDITS.md`
8. directly relevant files in `src/` or `tests/` when a roadmap claim depends on current implementation

## Typical Tasks

### Refresh the active roadmap

- Confirm the document date and repository snapshot.
- Preserve completed work history, but move attention to remaining waves.
- Re-check open items against newer plan or status docs.
- Update the target-state wording if the product direction has shifted.

### Create a new time-boxed roadmap

- Start from the active roadmap, not a greenfield list.
- Pull only the items that fit the requested horizon.
- Keep non-goals explicit.
- Put enabling work ahead of surface polish.

### Suggest opportunities

- Prefer opportunities that remove ambiguity, unblock a dependency, or improve operator trust.
- Highlight whether the opportunity is product-facing, technical, or enabling.
- Avoid listing ideas that have no clear place in Meridian's current direction.

### State the end product

- Describe the finished Meridian experience as a workflow-centric trading workstation.
- Explain how research, strategy runs, portfolio, ledger, paper trading, and live operations fit together.
- Distinguish mandatory end-state capabilities from optional expansions such as scale-out or specialized advanced tooling.

## Useful Framing

- "What is complete?" should be factual and conservative.
- "What remains?" should focus on material delivery gaps, not every possible improvement.
- "Opportunities" should be additive and prioritized, not a duplicate of the backlog.
- "End product" should read like a product vision grounded in the roadmap, not marketing copy.
