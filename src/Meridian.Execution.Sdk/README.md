---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-EXECUTION-SDK
path: src/Meridian.Execution.Sdk
status: active
owner_lane: Execution and Fund Accounts
last_reviewed: 2026-07-05
---

# src/Meridian.Execution.Sdk

`CanonicalTaxLotAdapter` admits only long execution and canonical lots. Positive quantity alone
cannot make an explicitly short canonical acquisition compatible with the long consumer. Short
production cutover still requires governed posting and consumer parity evidence.

`BrokeragePortfolioSnapshotDto.IsComplete` defaults to false until a provider establishes complete
account and holding evidence. `IBrokerageConnectionState` supplies a credential-free connection
epoch and verified account/environment scope, so cached execution evidence cannot survive a
connection switch. `IBrokerageOrderRecoveryGateway` reads a retained client identity without
submitting or treating a missing broker response as permission to retry.

## Shared close and lot convergence

`CanonicalTaxLotAdapter` validates legacy execution lot identity, quantity, acquisition date, and basis against a supplied retained canonical lot. Symbols remain display evidence. The adapter refuses shorts pending an explicit direction-model cutover and does not synthesize identity or FX facts.

## Purpose

Execution SDK provides abstractions shared by execution gateways, broker integrations, and execution-facing services.

## Layer responsibility

This layer should hold reusable execution contracts without binding them to one broker, UI, or application workflow.

## Key folders and files

- `Meridian.Execution.Sdk.csproj` - execution SDK project boundary.
- Gateway and execution extension contracts used by runtime implementations.

## Important workflows

New OMS submissions follow `OrderRequest` → `ValidatedOrder` → `GatewayResolvedOrder` before
pre-trade risk. `ValidatedOrder.TryCreate` freezes metadata and legs and returns structured
field errors for blank symbols, undefined order enums, missing price limbs, or conflicting /
non-positive trailing specifications. Closed pricing variants retain required prices; auxiliary
paper price hints remain available. Quantity, leg, and instrument-specific price policies remain
with the existing gateway and risk checks; zero or negative prices are not universally rejected.
`GatewayResolvedOrder.TryResolve` asks the active gateway for sizing authority and represents
quantity, broker-native notional, or face value priced as percentage of par as distinct cases.
`ToRiskRequest` is the compatibility boundary for existing risk rules: it stamps resolved sizing
on a copy, never on the submitted gateway request. Neither typed object permits construction with
arbitrary caller-asserted sizing. Amendments and recovery retain their existing contracts.

Use this module when execution integration contracts need to be shared by multiple execution implementations.
`BrokerageOrderPlacementGate` is the shared pre-submit safety gate for HTTP endpoints and the OMS:
non-paper gateways must have a matching `BrokerageConfiguration`, while the paper gateway keeps
the default paper-first behavior. Gateways that implement `IExecutionGatewayModeProvider` expose
typed paper/simulation/live mode metadata so live-readiness checks do not infer safety posture from
gateway-id strings.
Gateways whose providers keep client and broker order IDs in separate lookup namespaces implement
`IExplicitOrderCancellationGateway`. Callers label the namespace with
`OrderCancellationIdentifier`; identifier shape is never used to guess whether a value is a broker
ID, because a UUID-shaped client ID can otherwise target an unrelated broker order.
`IExplicitOrderModificationGateway` applies the same namespace discipline to amendments.
Each gateway's amendment path has one send boundary immediately before the call that can reach the
venue. Any failure or cancellation before it (identity resolution, local validation, an operation
conflict, a read-only lookup) raises `BrokerageRequestNotSentException` and discards local attempt
state; the OMS releases reservations and rejects the amendment as never dispatched. Anything after
the boundary stays dispatch-uncertain.
`ExecutionReportType.CancelRejected` and `ModificationRejected` describe refused operations;
their `OrderStatus` describes the actual order and does not imply terminal rejection. Report
consumers retain cumulative fills and original client identity across broker replacements.
`ModificationPending` retains an uncertain replacement attempt for reconciliation. Durable pending
amendments preserve original and proposed terms, broker identities, and provider recovery evidence
until authoritative reconciliation selects an outcome. Gateways implementing
`IPreassignedReplacementReferenceGateway` honor `OrderModification.ReplacementClientOrderId`, and
`PendingOrderAmendment.PriorChainBrokerOrderIds` names the replacement chain's earlier members. Reported order terms are explicitly marked
as evidence; provisional prices and quantities cannot silently become confirmed terms. Snapshot
averages set `FillPriceIsCumulativeAverage`; the OMS derives the executed increment's price before
publishing accounting fills. Optional replacement and average fields preserve legacy payloads
when absent.
`IBrokerageOrderRecoveryGateway` adds authoritative lookup by retained client ID, including terminal
orders. A null result means unresolved state and cannot authorize resubmission.
`IBrokerageConnectionState` exposes an opaque connection generation and verified account/environment
scope so synchronization and durable recovery revoke evidence when the active connection changes.
Brokerage activity fill snapshots can carry explicit provider-reported realized P&L when a broker
or custodian supplies it; callers should leave the field null rather than infer it from fill
notional. Activity snapshots can also carry provider corporate-action/factor events such as
splits, dividends, amortization, paydowns, and factor updates when the upstream feed supplies
account-scoped evidence.
`OrderRequest.FundAccountId` carries account scope for W7 live-order validation so shared
readiness gates can verify broker sync and open-order reconciliation against the same governed
account that the operator selected. `ExecutionOrderMetadataPolicy` treats broker-account routing,
manual override identifiers, asset class routing, and live-readiness evidence references as
server-owned metadata. Endpoint callers may name a run for validation, but retained live-readiness
evidence must be supplied by server-side execution gates and is stripped before broker submission if
a caller attempts to provide it.
`INotionalOrderSizingGateway` is the opt-in marker for gateways that route the broker-native
notional metadata dollar amount in place of `OrderRequest.Quantity` — Alpaca alone today. Every
rail that measures an order's economic size reads that metadata through `BrokerNotionalMetadata`,
so a gateway that routes quantity must not implement it: the OMS refuses such orders rather than
measuring one size while the broker routes another. `BrokerNotionalMetadata` consults only the
first non-blank alias, matching the gateway's own precedence, so a value the gateway cannot use
means the order is quantity-sized rather than falling through to a later alias.
Because another broker can use the same asset-class label with different unit semantics,
`IFaceValueOrderSizingGateway` makes the active gateway resolve the actual route. The OMS then
carries that server-owned fact through `GatewayResolvedOrder`, the legacy risk adapter's
`OrderSizingMetadata`, and `OrderState`; risk and working
reserves value those orders as `abs(quantity) * price / 100`, and broker-notional metadata does not
override routed face value.
`IPosition.ExactQuantity` carries the unrounded signed size beside the whole-share `Quantity`;
fund-ownership attribution is decimal, so deriving an unattributed remainder from the rounded value
invents a contribution the book never held.

`TaxLot` quantity is `decimal` and carries `UnitKind` (default `Shares`) and `Multiplier`
(default 1) through `IUnitizedLot`, matching backtesting and ledger lots.

## Diagrams

See `DIA-PAPER-SESSION-REPLAY` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-EXECUTION-SDK -->
| Roadmap item | Title |
| --- | --- |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W7-LIVE-001` | Live-readiness governance |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-EXECUTION-SDK -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.Execution.Sdk/Meridian.Execution.Sdk.csproj /p:EnableWindowsTargeting=true
```

## Change rules

Keep SDK contracts stable and broker-neutral. Validated/resolved order factories construct shared
contracts and query broker-neutral capabilities; routing, policy orchestration, and lifecycle
implementation belong in execution or infrastructure projects.

## Related docs

- `docs/architecture/module-map.md`
- `docs/source/generated/source-module-index.md`
