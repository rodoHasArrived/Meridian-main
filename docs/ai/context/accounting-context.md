# Accounting Context

**Status:** active AI context pack  
**Owner:** core-team  
**Reviewed:** 2026-06-16

## Meridian Accounting Rules

- Accounting workflows are double-entry only.
- Journal entries are immutable after posting.
- Corrections occur through reversal and rebook, not in-place mutation.
- Every accounting entry requires source, effective date, posting date, approval status, and retained evidence or explicit operator rationale.
- Every posted entry must balance debits and credits.
- Balances must reconcile to source evidence, external statements, or approved operational explanations.
- Accounting UI must expose validation state, source, approval state, and audit trail before commit.
- Generated code must not silently create accounting records from unverified market data.

## AI Usage

Load this context before generating or reviewing code for ledgers, journal entries, capital accounts, close workflows, reconciliation postings, reports with accounting balances, or audit evidence involving accounting records.

## Review Checklist

- Does the change preserve double-entry balance?
- Are posted records immutable?
- Is reversal/rebook supported for corrections?
- Are effective date and posting date both represented where needed?
- Is approval state explicit?
- Is source evidence retained or referenced?
- Can an auditor explain how the balance was produced?
