---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-APP
path: src/Meridian.Application
status: active
owner_lane: Runtime Host
last_reviewed: 2026-05-28
---

# src/Meridian.Application

## Purpose

Meridian application layer contains use cases, orchestration services, commands, and workflow
coordination.

## Layer responsibility

This module owns application workflows that coordinate providers, storage, execution, ledger,
reporting, and UI-facing services through contracts. Keep transport, persistence implementation,
and UI presentation concerns in their owning layers.

## Key folders and files

- `Commands/` - CLI command handlers and operator workflows.
- `Reconciliation/` - statement intake, canonical matching, materiality-aware break
  classification, recommended actions, and case creation gates.
- `OperationsContinuity/` - account-period continuity aggregate, command transitions, audit
  timeline, and server-derived gate status for broker, Security Master, ledger, reconciliation,
  and approval close lanes. Approval and close commands enforce shared close-checklist control
  approvals before the workflow can become ready for close or close against a report pack. Close
  readiness is scored server-side across Security Master, position, cash, ledger, pricing,
  reconciliation, report, and approval components.
- `FundStructure/` - organization, fund, portfolio, account, ledger-group, cash-flow, and ledger
  mapping workbench orchestration. Ledger mapping resolution stays server-side and reuses
  fund-structure assignments before falling back to account ledger references.
- `Reconciliation/` - statement intake, validation, matching, case creation, and reconciliation
  orchestration. Statement validation returns structured issue DTOs so operator workflows can
  distinguish hard blockers from policy-controlled soft issues before import.
- `Services/` - application use cases and orchestration services.
- `Reconciliation/` - canonical reconciliation matching, statement tolerance profile models, and profile-provider seams that stamp tolerance profile/version/rule evidence on runs and match explanations.
- `Composition/` - application feature registration and service wiring.
- `Reconciliation/` - statement reconciliation workflows, external statement mapping profiles, case intake, and match orchestration.

## Important workflows

Use this module when changing command behavior, workflow orchestration, feature registration, or
application service contracts consumed by host and UI surfaces.

## API contract notes

- Broker/custodian reconciliation now uses canonical feed descriptors, normalized position/cash/transaction records, deterministic exact/tolerance/heuristic match decisions, and an append-only decision journal for unresolved breaks and resolution history.

- Statement reconciliation tolerance profiles are versioned in `Reconciliation/`; match outputs that use a tolerance must carry the tolerance profile ID/version and the exact tolerance rule ID that allowed the match.
- Options-chain provider IDs are normalized with trim plus invariant lowercase before deduplication,
  health lookup, fallback detection, logging, and metrics.
- `StatementMatchingEngine` accepts normalized statement positions, cash balances, transactions,
  internal portfolio/cash/ledger views, and a tolerance profile. It emits deterministic exact,
  tolerance, candidate, and unmatched results with rule IDs, confidence, side-specific evidence
  references, variance, tolerance, and operator explanations.
- Statement reconciliation classifies broker and custodian breaks before case creation; only
  material unresolved breaks are promoted into casework, with severity and recommended action
  stored in the classification result.
- Statement reconciliation imports return typed normalized collections for positions, cash balances, transactions, security references, and source-row references. The import path keeps legacy canonical rows in adapter infrastructure only while application orchestration consumes the typed result shape.
- Statement reconciliation completion creates evidence links that bind run IDs, source hashes, broker/custodian and account-period metadata, mapping and tolerance profile versions, validation and match summaries, break/case identifiers, actors, and timestamps.
- Statement reconciliation imports return typed normalized collections for positions, cash balances, transactions, security references, and source-row references. The import path keeps legacy canonical rows in adapter infrastructure only while application orchestration consumes the typed result shape. File-backed statement repositories persist run manifests, validation issues, normalized entities, match results, breaks, and case links under `reconciliation/statement-runs/{runId}/` using atomic JSON writes.
- Options-chain provider IDs are normalized with trim plus invariant lowercase before deduplication, health lookup, fallback detection, logging, and metrics.
- Statement validation checks source accessibility, account/profile references, duplicate imports,
  invariant date/decimal parsing, currency/activity/security resolution, and statement-period
  alignment through application-layer DTOs rather than relying only on exceptions. Statement run manifests retain source file hashes plus mapping/tolerance profile versions for reproducibility, and raw broker files are referenced by source path or approved evidence URI instead of being blindly copied into repository storage.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-APP -->
| Roadmap item | Title |
| --- | --- |
| `W2-TRD-001` | Paper trading cockpit reliability |
| `W2-PROMO-001` | Paper promotion evidence and operator acceptance |
| `W3-CONT-001` | Research to paper continuity |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-APP -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "Category!=Integration" --logger "console;verbosity=normal"
```

## Change rules

Keep orchestration here. Do not leak transport/UI concerns into this layer or add direct
infrastructure details when an abstraction already exists.

## Related docs

- `docs/architecture/module-map.md`
- `docs/developer/build-test-run.md`
- `docs/source/generated/source-module-index.md`
