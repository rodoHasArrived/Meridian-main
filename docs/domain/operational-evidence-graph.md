# Operational Evidence Graph

**Status:** active guidance
**Owner:** core-team
**Reviewed:** 2026-06-16

## Definition

The Operational Evidence Graph is the connected proof chain that explains how Meridian turns source evidence into trusted operational outcomes.

It links source documents, provider records, normalized records, validation results, reconciliation cases, ledger and capital-account impact, close readiness, report lines, delivery records, and audit evidence.

## Relationships

- Connects source evidence to normalized transactions, positions, balances, fund events, journals, capital-account activity, reports, and delivery records.
- References validation rules, mapping versions, extraction confidence, reviewer state, reconciliation matches, exceptions, approvals, period locks, and restatement history.
- Feeds operator proof panels, audit timelines, report-line provenance, close readiness, and evidence manifests.

## Business Rules

- The graph should answer what happened, why it happened, whether it can be trusted, and where it was approved, recorded, reported, delivered, or blocked.
- Each graph edge must represent a meaningful operational relationship, not a decorative visual connection.
- Missing evidence, unresolved breaks, stale approvals, period locks, or blocked outputs should be visible as graph state.
- Graph-backed UI should let operators drill from an output back to source evidence and forward to downstream usage.
- Generated code must not invent proof relationships that cannot be backed by retained records or explicit operator rationale.

## Examples

- A report line links to calculation inputs, approved transactions, journal entries, reconciliation cases, source files, package approval, delivery evidence, and restatement history.
- A capital account balance links to capital calls, contributions, allocations, distributions, journals, statement packages, and audit support.
- A close blocker links to a reconciliation break, owner, SLA state, materiality, evidence packet, blocked report package, and reviewer decision.

## Future Expansion Notes

The first implementation can be a structured record tree or proof panel. It does not need a visually elaborate network. Future work can add richer graph traversal, cross-record search, evidence manifests, saved proof views, and graph-backed audit exports once the underlying contracts and read models are stable.
