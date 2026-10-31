# Strategy, Orders, and Execution

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-30

## Purpose and Scope

Strategy formulation, order management, and governed strategy execution are explicit Meridian
product objectives alongside financial operations. `DEC-TRADING-STRATEGY-DIRECTION-001` adopts
this direction; `W13-STRAT-001` and `W13-OMS-001` own its planned delivery and acceptance.
The [design charter](../product/meridian-design-document.md#227-strategy-and-order-management-direction)
defines the product boundary, and the [roadmap registry](../roadmap/README.md) owns status.

These are shared domain requirements over existing strategy, execution, risk, and accounting
services. They do not establish that every formulation route, broker, account, instrument,
order type, or operating mode is supported. Historical W6/W7 and W9 acceptance remains bounded
to its original evidence.

## Concepts and Relationships

| Concept | Meaning | Required relationship |
| --- | --- | --- |
| Strategy definition / retained artifact | Versioned rules, universe, parameters, data dependencies, execution modes, and risk limits. | A run resolves the exact reviewed version and inputs, not the current editable UI document. |
| Strategy run | One retained evaluation or execution in a declared mode and owner scope. | Carries data and engine/model versions, results, lifecycle, and parent/child lineage; account scope is explicit where needed. |
| Promotion decision | Independent authorization for a specific strategy/run to advance under policy. | Binds the reviewed definition, input/evidence versions, permitted mode, and applicable account; it is separate from order admission. |
| Order intent | A discretionary instruction or strategy-generated decision requesting a scoped order. | Retains source/origin, exact requested terms, actor/account authority, risk evidence, and durable command identity. Strategy lineage applies only when that is the actual origin. |
| Order | The OMS record of admitted intent and its observed lifecycle. | Retains client and broker identities, amendments, replacement relationships, cancellation requests, and reconciled outcomes. |
| Execution report / fill | An external broker observation or explicitly identified simulator observation. | Retains event identity, order association, quantities, prices, costs, and timestamps for lifecycle and economic reconciliation. |

These terms describe responsibilities. They do not require a new parallel class hierarchy or a
replacement for existing retained definitions, `StrategyRunEntry`, Designer documents, order
contracts, or broker report envelopes.

## Ownership and Authority

| Owner | Authority |
| --- | --- |
| Strategy services | Formulation, validation/run evidence, promotion and strategy lifecycle. |
| Risk and execution controls | Current admission policy, limits, reservations, readiness, and operator control state. |
| Order Management System and gateways | Authoritative order coordination, routing, retained broker outcomes, replacement identity, cancellation, and recovery. |
| Portfolio and financial operations | Controlled economic effects, reconciliation, accounting candidates and approval. |
| Ledger and storage | Durable posted journals, balance/period guards, idempotency and correction lineage. |
| Browser and WPF | Shared command/read-model consumers that expose the same permissions, lifecycle, blockers, and evidence. |

An order request is not evidence that a broker accepted or filled it. A requested cancellation
is not evidence that an order is canceled. A replacement ID is not a confirmed replacement
relationship until the owning workflow retains the required broker observations. Uncertainty
must remain visible together with unresolved working-order or exposure risk.

Strategy review does not authorize arbitrary live trading. Promotion, broker/account authority,
connection ownership, current risk/control state, readiness, and runtime support remain separate
checks at the authoritative command boundary. An OMS objective also does not authorize treasury
payment release; that remains a separate product and financial-control boundary.

## Connected Operator Journeys

1. **Formulate and validate.** Retain a supported strategy definition, parameters, universe, data
   snapshot/fingerprint, engine and cost assumptions, results, and limitations. A successful script
   process with printed metrics is not necessarily a captured canonical backtest result.
2. **Review and promote.** Bind the independent decision to the exact source run and retained
   inputs. Backtest, paper, and live evidence remain distinguishable; changing approved operands
   requires renewed review.
3. **Admit and manage orders.** Operator tickets and strategy decisions use the same OMS and
   gateway seams. Validate the actual origin, account, instrument, order terms, risk and current
   authority before submission; unsupported combinations fail closed.
4. **Observe and recover.** Retain acknowledgments, partial and complete fills, rejections,
   expirations, amendments, cancellation outcomes, reconnects, and restart recovery. Reconcile
   uncertain results before another attempt can create a duplicate effect.
5. **Reconcile and account.** Connect retained broker orders, fills, positions, and cash evidence
   to portfolio and governed financial effects. The existing accounting handoff owns retry and
   acceptance; the OMS cannot mutate posted journals or create a competing ledger.

Accounting workflows may start from external statements or other operational events. Research
experiments may end with retained analysis. These journeys connect where the supported workflow
requires them; neither creates a mandatory origin for all platform activity.

## Current Foundations and Delivery Limits

| Existing source | Current boundary to preserve |
| --- | --- |
| [Execution module](../../src/Meridian.Execution/README.md) | Existing OMS, risk, gateways, broker reports, recovery and durable accounting handoff are foundations for expanded delivery. Current non-paper admission requires retained strategy/run, account and live-readiness authority. |
| [Strategies module](../../src/Meridian.Strategies/README.md) | Retained run identity, promotion, lifecycle and the approved-revision Designer activation subset already exist. Their evidence does not prove every authoring route is composed end to end. |
| [Designer live source](../../src/Meridian.Strategies/Live/Designer/DesignerDocumentLiveSource.cs) | Preserve the approved-revision binding, supported closed-expression execution grammar, and visible refusal/deferred states for unsupported or changed definitions. |
| [Strategy Engine foundation](../architecture/strategy-engine-foundation.md) | The pre-run validation seam's `LiveDisabled` posture remains current behavior until an independently reviewed implementation changes it. |
| [Backtesting module](../../src/Meridian.Backtesting/README.md) | The bounded Covered Call path is host-composed; broader orchestration and generic Designer result capture have separate activation work. |
| [QuantScript module](../../src/Meridian.QuantScript/README.md) | Worker isolation is a foundation; production/customer scripting requires the hardened-profile certification tracked by `PRD-012`. |

The generic Designer compiler currently emits diagnostic output, while the workstation run route
requires exactly one captured `BacktestResult`. Expanded formulation must close that real result
and orchestration gap; it must not fabricate results or treat the already-supported live adapter
as proof that the full journey works. See
[compiler](../../src/Meridian.Strategies/Services/StrategyDesignService.cs) and
[workstation route](../../src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.Strategy.cs).

A standalone manual OMS journey needs an explicit intent/account-authority design. It must use a
legitimate existing authorization context or a separately reviewed equivalent contract; it cannot
invent a strategy run or remove the current non-paper admission guard to make a ticket submit.

## Acceptance Cases

Use deterministic synthetic institutions, accounts, strategy inputs, broker reports, and failure
scenarios for ordinary development under the [solo-developer workflow](../ai/solo-developer-workflow.md).
Record the verified scope and use independent AI critique when the changed financial, persistence,
or authorization risk warrants it. Live credentials and a human review organization are not
prerequisites to a routine PR; AI critique cannot substitute for a required human product approval.

| Case | Required evidence |
| --- | --- |
| Definition, parameters, account or source changes after review | Changed operands invalidate affected approval; the renewed review identifies exact versions. |
| Duplicate, delayed or out-of-order broker reports | The retained order state and fill/economic handoff remain correct without duplicate consequences. |
| Timeout, lost acknowledgment, restart or reconnect | Durable identity and reconciliation recover the known result; unresolved dispatch remains explicit. |
| Amendment, pending replacement, cancel-all or kill switch | Broker-confirmed results and per-order unresolved exposure remain inspectable; a requested action cannot masquerade as completion. |
| Missing scope, unsupported order/runtime mode, stale readiness or failed risk | The authoritative operation refuses the action with a recoverable, visible reason. |
| Synthetic or paper rehearsal | Retained expected outcomes and failure cases prove the declared simulation and workflow scope; they do not establish live compatibility. |
| Separately authorized broker-backed operation | A live-support claim names the tested configuration and retains broker evidence, owner authorization, and applicable product approvals; untested configurations remain explicit. |

The [implementation tracker](../product/implementation-todo-list.md#expanded-strategy-and-order-management-objectives)
links the W13 work to existing hardening tasks. Product-scope adoption does not change delivery
ranks, accept those tasks, certify a release, or authorize broker activity.
