# Accounting Context

**Status:** active AI context pack  
**Owner:** core-team  
**Reviewed:** 2026-10-07

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
The first slice uses two direct 100% entities in one currency; only posted reviewed eliminations
enter actual consolidated figures. The existing summed ledger/WPF views remain gross.

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
