# Operational Evidence Context

**Status:** active AI context pack
**Owner:** core-team
**Reviewed:** 2026-10-30

## Purpose

Use this context before generating or reviewing code, tests, UI, reports, or documentation that
changes source evidence, strategy definitions and runs, order intent, execution reports,
normalized records, reconciliation, exception casework, ledger or capital-account impact, close
readiness, report provenance, delivery records, or audit support.

Use customer-neutral scope first: organization, entity, portfolio, account, book, period,
strategy, strategy run, order, execution report, operational event, evidence, approval, journal,
report, and audit trail. Fund, investor,
capital-account, and fund-event scope are specializations for fund/private-capital workflows, not
requirements for every customer.

Strategy formulation, order management, and governed execution are explicit objectives alongside
financial operations. The [strategy, order, and execution dictionary](../../domain/strategy-order-execution.md)
owns their business meanings. The [expanded-objective tracker](../../product/implementation-todo-list.md#expanded-strategy-and-order-management-objectives)
owns remaining work; a scope decision is not implementation, acceptance, or live authority.

## Meridian Evidence Rules

- Meridian should prove operational records, not just display them.
- Source evidence must retain enough identity to reconstruct origin, receipt time, source hash or version, provider/file/API context, and review state.
- Normalized records must remain traceable back to source evidence and mapping or extraction version.
- Strategy runs retain exact definition and input versions, parameters, data and runtime assumptions, mode, results, and applicable account scope; dependent promotion review binds those same operands.
- Orders retain their operator or strategy origin, authenticated account authority, pre-trade evidence, durable command identity, and broker observations. Submission, amendment, or cancellation intent cannot stand in for confirmed broker state.
- Uncertain dispatches, replacements, and partial fills remain explicit and recoverable. Duplicate or out-of-order reports cannot duplicate portfolio or accounting effects; recovery reconciles retained outcomes before new dispatch.
- Reconciliation breaks need owner, age, due date or SLA posture, materiality, root cause, supporting evidence, approval state, and blocked outputs where applicable.
- Exceptions that affect strategy admission, orders, execution, close, reporting, ledger, operating accounts, capital accounts, books, or delivery must fail closed until evidence and review state satisfy the relevant policy.
- Report lines should link to approved source records, calculations, ledger or capital-account impact, package version, delivery evidence, approval, and restatement history where applicable.
- Audit evidence must be retained as a manifest or support packet that can explain what changed, why it changed, who approved it, and where it was used.

## Active Proof Chain

| Workflow | Proof path |
| --- | --- |
| Financial operations | Retained source, normalization, validation, reconciliation, exception disposition, posting candidate, approved journal, close, report line, delivery, and audit evidence |
| Strategy formulation and validation | Versioned definition and inputs, validation, run evidence, review, paper evidence, and authorized promotion |
| Order management and execution | Authorized operator or strategy intent, account and risk checks, broker-confirmed lifecycle and fills, recovery, and portfolio/accounting handoff |

Capital-account impact joins the financial path where applicable. These paths connect through
stable identities and retained versions; a close is not required for every strategy experiment,
and a broker fill does not itself approve a journal or report.

## AI Usage

Load this context before work on:

- Operational Evidence Graph features.
- Operational Event Command Spine and fund-event command-center flows.
- strategy definition, run comparison, promotion lineage, order management, and execution recovery.
- close cockpit, exception queues, and reconciliation casework.
- report-line provenance, governed report packages, exports, and restatement workflows.
- evidence vault, request lists, audit support, tax support, and retained support packages.
- AI-assisted extraction, matching, summarization, variance explanation, or draft preparation.

AI-generated strategy logic or order suggestions remain reviewed drafts. They cannot approve
their own work or grant execution authority. Deterministic execution of an approved strategy
still uses supported runtime isolation and the current promotion, account, credential, pre-trade
risk, and live operator-control gates. Preserve existing run/account/readiness admission until a
broader manual OMS authority contract is explicitly designed and reviewed; never fabricate a run
to satisfy it. The [execution context](README.md#execution-boundary-context) routes to the owning
source contracts.

## Review Checklist

For the first posted amount workflow, use the [W10 amount provenance acceptance record](../../testing/w10-amount-provenance.md).
Its shared `ledger-amount` subject binds immutable journal/line/side IDs to authenticated tenant/company
and retained fund/book/period scope; names, symbols, and general navigation links cannot establish proof.

- Can the user trace the record from source evidence to output?
- Does the workflow retain evidence before the record is approved, posted, delivered, or reported?
- Are unresolved exceptions close-blocking or release-blocking when they should be?
- Are approval, scope, and segregation-of-duties concerns explicit?
- Are browser and WPF surfaces using shared identifiers, status definitions, read models, and endpoint contracts?
- Are strategy versions, order intents, broker observations, and financial posting authority distinct and joined by exact evidence identities?
- Does any model-generated suggestion remain a reviewed draft, while approved deterministic execution preserves its separate authority and control gates?
