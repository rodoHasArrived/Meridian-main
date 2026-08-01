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

## Purpose

Execution SDK provides abstractions shared by execution gateways, broker integrations, and execution-facing services.

## Layer responsibility

This layer should hold reusable execution contracts without binding them to one broker, UI, or application workflow.

## Key folders and files

- `Meridian.Execution.Sdk.csproj` - execution SDK project boundary.
- Gateway and execution extension contracts used by runtime implementations.

## Important workflows

Use this module when execution integration contracts need to be shared by multiple execution implementations.
`BrokerageOrderPlacementGate` is the shared pre-submit safety gate for HTTP endpoints and the OMS:
non-paper gateways must have a matching `BrokerageConfiguration`, while the paper gateway keeps
the default paper-first behavior. Gateways that implement `IExecutionGatewayModeProvider` expose
typed paper/simulation/live mode metadata so live-readiness checks do not infer safety posture from
gateway-id strings.
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
`ExecutionLogText.ForLog` renders caller-supplied order text — symbols, and rule reasons that embed
them — for log output. Nothing upstream of the pre-trade gate is required to constrain a symbol
(the Security Master gate is optional), so line breaks and other control characters are replaced
and over-long values truncated before the value reaches a logger. It is a rendering concern only:
the unaltered value still reaches the audit trail, where the serializer escapes it.

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

Keep SDK contracts stable and broker-neutral. Implementation logic belongs in execution or infrastructure projects.

## Related docs

- `docs/architecture/module-map.md`
- `docs/source/generated/source-module-index.md`
