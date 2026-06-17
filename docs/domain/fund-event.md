# Fund Event

**Status:** active guidance
**Owner:** core-team
**Reviewed:** 2026-06-16

## Definition

A Fund Event is an end-to-end private-capital operating object that captures a material fund activity and its evidence, workflow, accounting, capital-account, reporting, delivery, tax, and audit impact.

Fund events include closings, subscription packets, capital calls, contribution receipts, distributions, investments, valuations, fee or expense allocations, tax requests, audit requests, amendments, dissolutions, and wind-down support.

## Relationships

- Belongs to a tenant, organization, fund, legal entity, book, period, and event type where applicable.
- References source documents, provider files, bank or custodian evidence, operator rationale, and approval records.
- Can produce or explain transactions, cash expectations, journal entries, capital-account activity, reconciliation cases, report lines, delivery records, tax support, and audit evidence.
- Can block close, reporting, delivery, or ledger posting until required evidence and approvals are complete.

## Business Rules

- A fund event is not complete merely because accounting output exists.
- Completion requires reconstructable evidence, workflow state, approval state, ledger or capital impact, report usage, delivery posture, and audit history where those dimensions apply.
- Material fund events require retained source evidence or an explicit operator rationale.
- Corrections should preserve original event history and add reversal, amendment, restatement, or reapproval evidence rather than overwriting prior state.
- AI assistance may draft classifications, summaries, suggested matches, or missing-evidence flags, but cannot approve, post, release, publish, or erase event evidence.

## Examples

- Capital call with notice package, investor allocations, expected cash movements, contribution receipts, journal evidence, and delivery log.
- Distribution with approval chain, bank evidence, capital-account impact, report-line usage, and audit support.
- Valuation event with source marks, reviewer state, stale-mark resolution, ledger impact, NAV support, and reporting provenance.

## Future Expansion Notes

Fund events should become the operating spine for private-capital workflows. Future work should connect event templates, scoped authority, reusable evidence requirements, close blockers, capital-account roll-forward checks, tax support packages, audit request lists, and report-line provenance without coupling event state to one UI surface.
