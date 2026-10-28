# AI Context Packs

**Status:** active guidance
**Owner:** core-team
**Reviewed:** 2026-06-16

This directory contains compact Meridian Development Intelligence Framework context packs. Load these after repo navigation and before generating or reviewing code for the matching workflow.

## Packs

| Context pack | Use when |
| --- | --- |
| [Accounting Context](accounting-context.md) | Ledgers, journal entries, capital accounts, close workflows, accounting balances, reversals, approvals, and accounting evidence. |
| [Operational Evidence Context](operational-evidence-context.md) | Source evidence, reconciliation, exception casework, close readiness, report provenance, delivery records, audit support, and proof-layer workflows. |

Keep packs short and rule-oriented. Do not duplicate full architecture or product documents here; link to the canonical source when a task needs deeper context.

## Execution boundary context

For order-entry and pre-trade risk changes, read the
[execution workflow contract](../../../src/Meridian.Execution/README.md#important-workflows) and
[SDK typed-order contract](../../../src/Meridian.Execution.Sdk/README.md#important-workflows).
New submissions require structural validation and gateway-resolved sizing before risk; the
legacy risk-request adapter does not replace gateway authority. Amendments and recovery retain
separate contracts and reservation semantics.
