# Event Accounting Patterns

Use this reference when event-based accounting work needs outside rationale. Treat these notes as
supporting background; Meridian's MDIF, accounting context, module map, and source code remain
authoritative.

## Research Anchors

- Martin Fowler describes event sourcing as storing changes as events and highlights audit-log and
  replay benefits: https://martinfowler.com/eaaDev/EventSourcing.html
- Fowler's Accounting Entry pattern notes that event sourcing fits accounting when accounting
  entries link back to the domain event that caused them: https://martinfowler.com/eaaDev/AccountingEntry.html
- Microsoft Azure Architecture Center describes event sourcing as append-only event storage that can
  regenerate materialized views by replay: https://learn.microsoft.com/en-us/azure/architecture/patterns/event-sourcing
- Square's Books article describes immutable double-entry ledger design around accounts, journal
  entries, and line items: https://developer.squareup.com/blog/books-an-immutable-double-entry-accounting-database-service/
- Modern Treasury's immutability guidance emphasizes that double-entry ledgers should enforce
  append-only correction patterns rather than in-place mutation:
  https://www.moderntreasury.com/journal/enforcing-immutability-in-your-double-entry-ledger

## Meridian Interpretation

- The domain event is the reviewed operational fact, not a UI click or transient integration message.
- The event log and evidence chain explain why a record exists; journal entries are accounting
  consequences that must be balanced, approved, and immutable after posting.
- Read models and balances are projections. They must be rebuildable from posted accounting facts and
  retained evidence, with explicit projection versioning when calculation logic changes.
- Replay is an audit and recovery tool, not permission to rewrite posted accounting history.
  Corrections must append reversal/rebook, amendment, or restatement facts.
- Event-driven integration does not remove accounting controls. Missing source evidence, approval,
  idempotency, version, or period state should fail closed before posting.

## Design Checklist

- Event identity: stable event id, tenant/fund/book/period scope, event type, effective date,
  source evidence id, and operator/reviewer rationale.
- Causality: correlation id, causation id, command id, idempotency key, source version, and replay
  batch id where applicable.
- Posting controls: approval state, segregation-of-duties gate, period lock posture, stale-version
  guard, materiality policy, and blocked-output links.
- Accounting impact: balanced debit/credit lines, account ids, currency, amount precision,
  reversal/rebook path, and projection version.
- Evidence graph: source document or provider record, normalized record, validation result,
  reconciliation state, journal entry, capital account impact, report line, delivery record, and
  audit packet.
