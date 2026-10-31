# AI Context Packs

**Status:** active guidance
**Owner:** core-team
**Reviewed:** 2026-10-30

This directory contains compact Meridian Development Intelligence Framework context packs. Load these after repo navigation and before generating or reviewing code for the matching workflow.

## Packs

| Context pack | Use when |
| --- | --- |
| [Accounting Context](accounting-context.md) | Ledgers, journal entries, capital accounts, close workflows, accounting balances, reversals, approvals, and accounting evidence. |
| [Operational Evidence Context](operational-evidence-context.md) | Source evidence, strategy/run lineage, order intent and broker outcomes, reconciliation, exception casework, close readiness, report provenance, delivery, and audit support. |

Keep packs short and rule-oriented. Do not duplicate full architecture or product documents here; link to the canonical source when a task needs deeper context.

The [current charter](../../product/meridian-design-document.md#227-strategy-and-order-management-direction)
includes strategy formulation, order management, and governed execution alongside financial
operations. Load the [strategy, order, and execution dictionary](../../domain/strategy-order-execution.md)
for business meanings and the [expanded-objective tracker](../../product/implementation-todo-list.md#expanded-strategy-and-order-management-objectives)
for remaining work. Earlier baselines and the close package do not exclude these objectives.

## Execution boundary context

For order-entry and pre-trade risk changes, read the
[execution workflow contract](../../../src/Meridian.Execution/README.md#important-workflows) and
[SDK typed-order contract](../../../src/Meridian.Execution.Sdk/README.md#important-workflows).
New submissions require structural validation and gateway-resolved sizing before risk; the
legacy risk-request adapter does not replace gateway authority. Amendments and recovery retain
separate contracts and reservation semantics.

For strategy authoring, run evidence, or promotion changes, also read the
[strategy module workflows](../../../src/Meridian.Strategies/README.md#important-workflows) and
[strategy engine foundation](../../architecture/strategy-engine-foundation.md). Keep exact strategy,
input, mode, and applicable execution-account identity through review and recovery. A pre-run
validation or eligible backtest does not authorize paper promotion or live dispatch.

Model output remains a reviewed suggestion. Approved deterministic strategies execute only
through the supported runtime, account, promotion, risk, credential, and live operator-control
gates. Current live order admission retains its run/account/readiness boundary; a broader manual
OMS path requires an explicit reviewed order-intent contract rather than a fabricated run.
