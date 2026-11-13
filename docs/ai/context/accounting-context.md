# Accounting Context

**Status:** active AI context pack  
**Owner:** core-team  
**Reviewed:** 2026-11-12

## Meridian Accounting Rules

- Accounting workflows are double-entry only.
- Journal entries are immutable after posting.
- Corrections occur through reversal and rebook, not in-place mutation.
- Every accounting entry requires source, effective date, posting date, approval status, and retained evidence or explicit operator rationale.
- Every posted entry must balance debits and credits.
- Balances must reconcile to source evidence, external statements, or approved operational explanations.
- Accounting UI must expose validation state, source, approval state, and audit trail before commit.
- Generated code must not silently create accounting records from unverified market data.
- Ledger writes must fail closed when required source evidence, reviewer state, period posture, idempotency key, or version/concurrency guard is missing.
- Governed production stores reject standalone tax-lot saves even with complete acquisition facts. Journal-backed mutations use `IAtomicTaxLotJournalStore`; retained legacy facts use reviewed backfill. Direct saves remain an explicit compatibility-store operation.
- Supported canonical corporate actions bind reviewed predecessor/successor identities and commit lot mutations with the journal in one transaction. Retain original acquisition facts separately from current carrying basis and preserve source-action lineage after disposal; see the [lot convergence blueprint](../../engineering/blueprints/security-lot-convergence-blueprint.md) for treatments and remaining gates.
- New successor postings must certify the full immutable predecessor ancestry, resolving stable source-action identity from retained source evidence even when historical lineage or explicit stable-ID fields are absent. A later action or disposal cannot authorize repeating an earlier action; missing or contradictory receipts block posting.
- Atomic successors require the canonical `AssetAccounting.CorporateAction` source type and complete approved typed context. Corporate-action corrections, reversal/rebook intents, corrected-batch/source-journal metadata and closing-entry posting remain outside this bounded successor path.
- Migration 042 owns successor posting. Forward migration 043 permits an approved amortization reversal to restore the latest original mutation's retained null prior basis in the same scope/date; exact snapshot and inverse-journal validation, new version/batch requirements and ordinary removal refusal remain enforced.
- Payment-related work starts as payment intent, cash expectation, approval evidence, bank confirmation, ledger intent, reconciliation, and report linkage. Full live payment execution remains deferred unless roadmap evidence reopens it.

## AI Usage

External GL provider work also follows [External GL Providers](../../operators/external-gl-providers.md).
Xero and NetSuite credentialed imports remain external evidence. Their provider-owned
export checks require current connection/import scope and exact retained human control
references; a certified review artifact still cannot post externally.

Load this context before generating or reviewing code for ledgers, journal entries, capital accounts, close workflows, reconciliation postings, reports with accounting balances, or audit evidence involving accounting records.

Consolidation work loads [Intercompany Consolidation](../../domain/intercompany-consolidation.md).
The exact book policy retains v1 behavior or selects v2 stable-role/N-entity matching, v3 governed
whole-trial-balance FX translation, or v4 separately enabled income elimination. Only posted reviewed
eliminations enter actual consolidated figures. Legacy summed views remain gross. New book controls
are enforced immediately; existing books warn until audited migration activation. Missing historical
lifecycle, agreement, account or currency evidence is unresolved, never inferred from current state.
Load [Intercompany Agreements and Entity Lifecycle](../../domain/intercompany-agreements-and-entity-lifecycle.md)
and the [implementation sequence](../../engineering/plans/intercompany-entity-controls.md) before
changing these shared seams. Preserve independently reviewed legacy-attribution sidecars, exact
source digests and retained v1 evidence. File-based local/demo state does not establish PostgreSQL
transactional authority or Windows desktop verification.

## Review Checklist

Close-plan preparation captures reusable task configuration and explicit date rules into immutable
template versions. Preview resolves an existing target ledger book and period, exposes owner and
accounting-policy changes, and blocks unresolved mappings. Creation requires a current retained
preview and an idempotency key, creates a fresh workflow and evidence requirements, and retains
template and creation provenance. Completions, approvals, reviewed evidence, journal references,
and period locks remain attached to the source period. Never implement rollover as a serialized
copy of an executed workflow or calculate authoritative due dates in a workstation client.

- Does the change preserve double-entry balance?
- Are posted records immutable?
- Is reversal/rebook supported for corrections?
- Are effective date and posting date both represented where needed?
- Is approval state explicit?
- Is source evidence retained or referenced?
- Are period locks, idempotency, and stale-version/concurrency concerns represented where postings can affect balances?
- Can an auditor explain how the balance was produced?
