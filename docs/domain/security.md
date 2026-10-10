# Security

**Status:** active guidance  
**Owner:** core-team  
**Reviewed:** 2026-06-16

## Definition

A Security represents a financial instrument, investable asset, or fund interest that can appear in positions, transactions, valuations, reconciliations, and reports.

## Relationships

- Belongs to or references an issuer, obligor, counterparty, or fund sponsor when applicable.
- Can have positions across multiple portfolios.
- Can be referenced by transactions, prices, corporate actions, expected cash flows, and reconciliation evidence.
- Can flow into accounting events when valuation, income, realized gain/loss, or capital allocation rules require it.

## Business Rules

- A Security must have stable identity separate from provider-specific symbols.
- Provider symbols, CUSIPs, ISINs, tickers, and local IDs are identifiers for matching, not the canonical security itself.
- Security type drives required attributes, valuation rules, and reporting treatment.
- Security master changes that affect accounting or reconciliation require retained evidence and review state.

## Examples

- Public equity share
- Bond
- Private loan
- Derivative contract
- Fund interest

## Future Expansion Notes

Future additions should support richer issuer hierarchies, multi-identifier matching, corporate actions, expected cash-flow models, private-asset attributes, and fund-interest look-through behavior without coupling provider ingestion directly to accounting records.

## Price and projected-cash-flow evidence

A raw price observation has security, source, effective timestamp, recorded timestamp and explicit
quote units. Golden-copy selection uses both economic and knowledge cutoffs with a retained
hierarchy version. An untyped legacy quote or an assumed class-level par value is insufficient
valuation evidence.

An absent coupon or fixing is unresolved economics, distinct from contractual zero. A normalized
per-100 schedule is analysis only until actual principal/notional is retained. Bills and discount
commercial paper pay principal rather than coupon interest; carrying-value accretion is separate.

Price-selection receipts retain an immutable result, hierarchy and comparison snapshot under
security/account identity. Replay by receipt preserves what was actually evaluated even when a
transaction with an earlier recorded timestamp commits later. Timestamp cutoffs alone are
eligibility predicates, not durable certification or a commit-consistent snapshot.

## Governed edit concurrency

PostgreSQL-backed workbench edits use a durable per-security generation, separate from the canonical
economic event-stream version. Migration `036_security_master_workbench_generations.sql` creates
that fence even when no overlay exists. A command reads the generation, advances it with a conditional
update, validates against the current overlay, and commits the overlay and draft together. A concurrent
command that read the predecessor generation gets a conflict; there is no automatic retry of stale
validation or review assumptions. Reload and explicitly retry so validation runs against the winner.
The canonical stream version is still checked under its existing stream lock before overlay mutation.

Approval scans and decisions, guarded generic patches, submission, discard, and the final
approval-to-published transition use the same database boundary. Revision-store calls outside the
workbench also participate. The stores must share one configured database and schema; mixed durable
and in-memory participants are rejected. PostgreSQL instances do not rely on the process-local gate.
A failed draft insert or cancellation rolls back values, review audit, draft and generation together.
Standalone edit provenance remains best-effort after commit; provenance written during discard joins
the transaction. Parallel read-model composition serializes commands on the enlisted connection.

This does not make approval workflow persistence or downstream publish handlers part of a distributed
transaction. Their existing reconciliation and idempotent retry contracts still apply. It does not
implement the remaining typed-schedule, payload-family, effective-history or shared-economics work.

Deployment requires human schema/storage review and a coordinated writer rollout: drain old writer
instances, apply migration 036 through the normal migration runner, then start the updated instances.
Mixed old/new writers are not covered, because old binaries do not acquire the generation fence.
The additive table can remain during a binary rollback, but the multi-instance guarantee is then lost.
No production migration or operator acceptance is implied by this change.

`SecurityMasterGovernedConcurrencyPostgresTests` forces two independent services to overlap at the
PostgreSQL lock, then verifies incompatible-date rejection, approval exclusion during patch/draft
creation, SQL-failure rollback, cancellation rollback, concurrent reads and composition guards.
The service-backed GitHub Actions integration lane is the required database proof; skipped local
PostgreSQL tests do not satisfy it.
