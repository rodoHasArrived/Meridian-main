---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-CONTRACTS
path: src/Meridian.Contracts
status: active
owner_lane: Contract Compatibility
last_reviewed: 2026-05-25
---

# src/Meridian.Contracts

## Purpose

Meridian contracts contains shared DTOs and cross-layer contracts used by host, services,
dashboard, and WPF.

## Layer responsibility

This module owns stable transport payloads, compatibility-safe DTOs, and shared schema objects.
Consumers depend on contracts; contracts should not depend on host, UI, application orchestration,
or provider implementations.

## Key folders and files

- `Workstation/` - workstation and operator workflow DTOs.
- Contract DTO files - shared payloads consumed across host, UI services, desktop, and dashboard.
- Project metadata - serialization and package references for contract consumers.

## Important workflows

Treat additive and breaking changes as cross-module compatibility work. Operations Continuity
workflow DTOs publish the shared broker intake, Security Master, ledger posting, reconciliation,
approval, close, and audit vocabulary consumed by both browser and WPF workstation clients. Keep
returned workflow blocker codes in `OperationsWorkflowContractMatrix.BlockerCodes`, including
ledger journal context-validation failures, so clients can handle command failures without parsing
messages. Close-checklist control approval blockers are part of that shared vocabulary and must
remain contract-owned rather than browser-only or WPF-only state. Close readiness score payloads
are also contract-owned and include server-derived Security Master, position, cash, ledger,
pricing, reconciliation, report, and approval components so UI clients can render readiness without
client-local scoring rules.

Report-pack workflow contracts carry governed publication metadata: sign-off actor, evidence hash,
retained manifest path, retained evidence links, report-line provenance, and restatement evidence
links. Keep these fields shared so browser, WPF, and service tests enforce the same publication and
no-orphan-evidence rules.

Fund-structure contracts include the ledger mapping workbench payload used by accounting and
governance surfaces to show account-to-ledger-group assignment source, unresolved mapping issues,
and recommended operator action without requiring clients to duplicate mapping precedence rules.

Evidence workflow contracts now carry policy-owned SLA/freshness assessments and the Meridian
Assurance Score on packet completeness. Keep provider validation, replay checks, reconciliation,
approval, and report freshness policy output in shared DTOs so browser and WPF clients render the
same cross-workflow readiness signal without local scoring rules.

Brokerage sync activity payloads are fund-account scoped under `Workstation/BrokerageSyncDtos.cs`.
Keep readiness and work-item decisions on `WorkstationBrokerageSyncStatusDto` and reserve
`FundAccountBrokerageSyncActivityDto` for durable account-level evidence, positions, orders, fills,
and cash-transaction details. Provider-ledger reconciliation payloads in the same file are also
fund-account scoped and compare the latest provider projection with Meridian's internal
account-balance snapshot plus Security Master coverage posture. Reconciliation break payloads carry
stable break keys, owner assignment, tolerance, first/last-observed aging, and sign-off state so
controller workflows can treat provider-ledger variances as accounting-grade case records. The
detail contract also carries Security Master confidence passports for provider positions, including
resolution source, confidence score, validation issue codes, and identifier-conflict evidence.

Statement reconciliation payloads live under `Workstation/StatementReconciliationDtos.cs` and keep
source-file evidence, mapping/tolerance profile versions, normalized positions, cash, transactions,
match summaries, breaks, and operator cases in the shared contract lane. Keep these DTOs additive and
transport-safe so browser, WPF, retained evidence, and automation consumers can reconcile custodian
statements without referencing application, UI, or infrastructure types.

Direct lending command result codes distinguish validation failures, missing aggregates,
optimistic concurrency conflicts, and idempotency/command conflicts so persistence stores can return
operator-safe failure reasons without parsing exception text.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-CONTRACTS -->
| Roadmap item | Title |
| --- | --- |
| `W1-DATA-001` | Provider trust gate and data confidence baseline |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W3-CONT-001` | Research to paper continuity |
| `W4-RECON-001` | Portfolio ledger reconciliation readiness |
| `W4-RPT-001` | Governed report pack readiness |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-CONTRACTS -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
```

## Change rules

Prefer additive DTO changes when possible. Update shared compatibility tests and generated docs when
contract shape, blocker vocabulary, or route-visible payloads change.

## Related docs

- `docs/status/contract-compatibility-matrix.md`
- `docs/architecture/module-map.md`
- `docs/source/generated/source-module-index.md`
