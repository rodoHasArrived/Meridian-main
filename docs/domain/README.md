# Domain Dictionary

**Status:** active guidance  
**Owner:** core-team  
**Reviewed:** 2026-10-28

This directory contains Meridian's domain dictionary for AI-assisted and human development.
Start with the [Meridian Domain Model](../architecture/meridian-domain-model.md) for shared scope,
relationships, and invariants; use these pages for individual business concepts. The
[MDIF framework](../architecture/meridian-development-intelligence-framework.md) explains how the
dictionary fits architecture decisions and AI context packs.

Business rules and future expansion notes express domain requirements, not delivery status.
Use each page's implementation references and the [roadmap](../roadmap/README.md) to establish
what is implemented; the [readiness tracker](../product/implementation-todo-list.md) owns release
follow-up.

Start with:

- [Brokerage Account Snapshot](brokerage-account-snapshot.md)
- [Corporate Action Case](corporate-action-case.md)
- [Fund Event](fund-event.md)
- [Operational Evidence Graph](operational-evidence-graph.md)
- [Account Oversight Evidence](account-oversight-evidence.md)
- [Account and Book Scopes](account-and-book-scopes.md)
- [Intercompany Consolidation](intercompany-consolidation.md)
- [Recurring Journal](recurring-journal.md)
- [Security](security.md)
- [Contractual Schedule](contractual-schedule.md)
- [Contractual Cash-Flow Projection](contractual-cash-flow-projection.md)
- [Business-Day Calendar](business-day-calendar.md)
- [Market Curve](market-curve.md)
- [Credit Support](credit-support.md)

When new code introduces a durable business concept, add or update the matching dictionary page before broad code generation.

Security Master extensions: [OTC contract lifecycle](otc-contract-lifecycle.md) and
[asset-pack admission](security-asset-pack-admission.md).
