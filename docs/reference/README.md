# Reference Documentation

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-07-15

This is the canonical lookup lane for Meridian APIs, environment variables, CLI/configuration surfaces, schemas, provider capability matrices, glossary-style definitions, and stable product/reference tables.

## What Is Canonical Here

- **Canonical**: reference contracts, matrices, schemas, and environment/config lookup docs.
- **Source-Material**: strategy rationale and historical analysis used for extraction only.
- **Generated**: matrix/rule outputs produced by registry or generation scripts (do not hand-edit).
- **Archive**: retired lookup snapshots moved to `archive/docs/` with replacement reasons.

Reference docs answer "what exists and what shape does it have?" For procedures, use [Start](../start/README.md), [Engineering](../engineering/README.md), or [Operators](../operators/README.md).

Reference content here must stay schema/procedure-neutral and claim-stable. Procedures and operational policy live in [Operators](../operators/README.md) or [Start](../start/README.md).

## Find A Specific Contract

| Question | Reference |
| --- | --- |
| Which environment variable or JSON field does the host actually read? | [Environment Variables](environment-variables.md) and [Appsettings Schema](appsettings-schema.md); CLI validation and host readiness are separate checks. |
| What does stored market data contain? | [Data Dictionary](data-dictionary.md#event-container) and [JSON serialization](data-dictionary.md#json-serialization); distinguish runtime events, compatibility DTOs, and endpoint-specific JSON options. |
| How do I authenticate a script or handle a stale mutation? | [API Authentication](api-reference.md#authentication), [Conflict Contract](api-conflict-contract.md), and [Verified Operation Outcomes](verified-operation-outcomes.md). |
| Is an adapter implemented, and is it approved for this use? | [Generated Adapter Readiness](../source/generated/adapter-readiness-matrix.md) for implementation; [Provider Validation](provider-validation-matrix.md) for evidence requirements. |
| What enables the real IB runtime? | [Interactive Brokers API Compatibility](interactive-brokers-api-compatibility.md); default builds and official vendor builds have different connectivity guarantees. |
| How are startup, persistence, and shutdown controlled? | [Lifecycle Control Plane](lifecycle-control-plane.md), [Ledger Journal Store](ledger-journal-store.md), and [Accounting Report Packs](accounting-report-packs.md). |

## Canonical Lookup Areas

| Area | Canonical or migration source | Notes |
| --- | --- | --- |
| HTTP and local API contracts | [API Reference](api-reference.md), [API Conflict Contract](api-conflict-contract.md), [Verified Operation Outcomes](verified-operation-outcomes.md), [Lifecycle Control Plane](lifecycle-control-plane.md), [OMS/EMS Integration](oms-ems-integration.md), [Accounting Configuration](accounting-configuration.md), [Governed Accounting Reporting](accounting-report-packs.md) | Keep route, request, response, idempotency, and storage-shape details here. |
| Data fields and normalization | [Data Dictionary](data-dictionary.md), [Data Uniformity](data-uniformity.md), [Reconciliation Break Taxonomy](reconciliation-break-taxonomy.md) | Stable field/type definitions and cross-provider terminology. |
| Environment and config | [Environment Variables](environment-variables.md), [Appsettings Schema Reference](appsettings-schema.md), [EDGAR Reference Data](edgar-reference-data.md) | Lookup details only; setup procedures belong in operators/start docs. |
| Provider capability and readiness lookup | [Provider Capability Matrix](provider-capability-matrix.md), [Provider Validation Matrix](provider-validation-matrix.md), [IB API Compatibility](interactive-brokers-api-compatibility.md) | Keep capability/evidence scope separate from runtime registration; provider setup belongs in Operators. |
| Adapter implementation readiness | [Generated Adapter Readiness Matrix](../source/generated/adapter-readiness-matrix.md), [Adapter Readiness Registry](../source/data/adapter-readiness.yml) | Source-owned implementation inventory and targeted evidence; live-provider validation and operator sign-off remain separate gates. |
| Contract compatibility and schemas | [Contract Compatibility Matrix](contract-compatibility-matrix.md), [Provider Validation Evidence Schema](provider-validation-evidence-schema.md), [Provider Validation Matrix](provider-validation-matrix.md) | Keep generated or matrix-owned content in place until moved through a focused migration. |
| UFL and asset profiles | [UFL Supported Assets Index](ufl-supported-assets-index.md), [UFL Capability Model](ufl-capability-model.md), [UFL Conformance Matrix](ufl-conformance-matrix.md) | Capability definitions and historical/target-state profiles; the conformance rows are a dated assessment, not current delivery status. |
| Ledger, accounting, and reporting contracts | [Ledger Journal Store](ledger-journal-store.md), [Export Preflight Rules](export-preflight-rules.md), [Accounting Configuration](accounting-configuration.md), [Accounting Report Packs](accounting-report-packs.md) | Stable contracts and artifact layouts. |
| Database schema | [Database Schema](database-schema.md) | Schema-control entrypoint for authoritative SQL migrations, generated `pg_catalog` manifests, DTO/data-object diagrams, policies, and dependency maps. |
| Strategy contracts | [Backtest Preflight and Stage Telemetry](backtest-preflight-and-stage-telemetry.md), [Strategy Briefing Workflow](strategy-briefing-workflow.md), [Strategy Promotion History](strategy-promotion-history.md) | DTOs, persistence fields, and workflow contract shapes. |
| Dependencies and open-source notices | [Open Source References](open-source-references.md), [Dependencies](../DEPENDENCIES.md) | Package inventory and third-party acknowledgement lookup. |

## Provider Lookup Rule

Provider-selection claims belong in capability and validation matrices, not dated evaluations. Backfill provider guidance should distinguish procedure, lookup, and implementation ownership:

- Procedures for credentials, setup, repair, and fallback operation belong in [Operators](../operators/README.md).
- Capability, data-type, coverage, rate-limit, and validation lookup belongs in this reference lane or generated/matrix-owned status files.
- Provider orchestration and fallback logic belongs in engineering/source docs and current source README files.
- Adapter-family implementation readiness is owned by the [source registry](../source/data/adapter-readiness.yml)
  and its [generated matrix](../source/generated/adapter-readiness-matrix.md); do not maintain a duplicate readiness inventory here.

Do not treat old rate-limit, entitlement, or data-quality statements as current without checking live provider documentation or current validation evidence.

## Storage And Streaming Lookup Rule

Stable lookup details for storage and streaming should live in reference, generated API/source views, or current source READMEs. Use dated evaluations only for rationale. Active lookup claims for file formats, WAL behavior, retention tiers, provider ID ranges, SLO thresholds, dedupe keys, schema versions, or alert names must be backed by current source, generated output, or validation evidence.

## Active Reference Files

| Document | Description |
| --- | --- |
| [API Reference](api-reference.md) | HTTP API endpoints, request/response schemas. |
| [API Conflict Contract](api-conflict-contract.md) | Canonical 409 body, `ExpectedVersion` request field, and per-family conflict migration state. |
| [Backtest Preflight and Stage Telemetry](backtest-preflight-and-stage-telemetry.md) | Backtest trust-gate DTOs/service contract and stage-aware progress telemetry fields. |
| [Brand Assets](brand-assets.md) | Brand asset reference material. |
| [Data Dictionary](data-dictionary.md) | Data field definitions and types. |
| [Data Uniformity](data-uniformity.md) | Cross-provider consistency guidelines. |
| [Database Schema](database-schema.md) | Maintained PostgreSQL schema-control map with generated ER diagrams, public contract-object diagrams, migration inventory, policy results, and dependency metadata. |
| [EDGAR Reference Data](edgar-reference-data.md) | EDGAR filer, ticker association, XBRL fact, filing-derived security data, CLI, API, and local storage reference. |
| [Contract Compatibility Matrix](contract-compatibility-matrix.md) | Shared contract/versioning baseline for workstation routes, DTOs, services, and migration behavior. |
| [Provider Capability Matrix](provider-capability-matrix.md) | Adapter coverage and readiness states by capability surface. |
| [Interactive Brokers and Alpaca Capability Expansion Review](broker-provider-capability-expansion-review.md) | Evidence-based provider expansion opportunities, priorities, and safety boundaries. |
| [Interactive Brokers API Compatibility](interactive-brokers-api-compatibility.md) | Official SDK inputs, opt-in runtime build, release evidence, and compatibility limits. |
| [Provider Validation Matrix](provider-validation-matrix.md) | Wave 1 provider gate matrix and promotion evidence criteria. |
| [Provider Validation Evidence Schema](provider-validation-evidence-schema.md) | Required validation packet artifacts and schema fields for provider trust decisions. |
| [Environment Variables](environment-variables.md) | Credential and configuration reference. |
| [Appsettings Schema](appsettings-schema.md) | Configuration fields, explicit environment aliases, and validation scope. |
| [Lifecycle Control Plane](lifecycle-control-plane.md) | Service supervision, local PostgreSQL lifecycle, readiness, and shutdown contract. |
| [Appsettings Schema Reference](appsettings-schema.md) | `appsettings.sample.json` and schema quick map for high-impact runtime sections. |
| [Export Preflight Rules](export-preflight-rules.md) | Export validation rule engine, rule IDs, and reuse pattern. |
| [Accounting Configuration](accounting-configuration.md) | Browser-first accounting setup DTOs, local API routes, non-posting preview behavior, and action audit seam. |
| [Governed Accounting Reporting](accounting-report-packs.md) | Certified point-in-time runs, maker-checker lifecycle, immutable artifacts, schedules, grants, delivery jobs, and provider receipt contracts. |
| [Ledger Journal Store](ledger-journal-store.md) | Postgres journal-entry, accounting-period, migration, and DI contract. |
| [OMS/EMS Integration](oms-ems-integration.md) | Versioned integration endpoint contracts, idempotent ingest semantics, adapter diagnostics, Excel sync policy, signing, and runbook steps. |
| [Open Source References](open-source-references.md) | Third-party library acknowledgements. |
| [Reconciliation Break Taxonomy](reconciliation-break-taxonomy.md) | Versioned canonical break classes and reason codes used by ledger reconciliation. |
| [Strategy Briefing Workflow](strategy-briefing-workflow.md) | Shared Strategy workspace briefing contracts, endpoint, and shell binding flow. |
| [Strategy Promotion History Persistence](strategy-promotion-history.md) | Durable promotion decision chain fields and JSONL-backed history behavior. |
| [Verified Operation Outcomes](verified-operation-outcomes.md) | Four-state terminal receipt, postcondition, evidence, artifact, replay, and case-history contract. |

## Historical Snapshots And Compatibility Pages

| Page | How to use it |
| --- | --- |
| [Provider Integration Snapshot](provider-integration-status.md) | Retains the 2026-05-20 board and evidence dates. Its registration gaps and sprint queue are historical; use the generated adapter matrix for current implementation. |
| [UFL Conformance Matrix](ufl-conformance-matrix.md) | Retains the 2026-05-29 maturity assessment; revalidate source/tests before making a current coverage claim. |
| [Design Review Memo](design-review-memo.md) | Design history; follow its current-source pointers for planning. |
| [Research Briefing](research-briefing-workflow.md) and [Governance Report Packs](governance-report-packs.md) | Redirects to the current Strategy and Accounting contracts; retained identifiers are compatibility surfaces. |

## Migration Rules

- Do not put step-by-step setup guides here; use [Operators](../operators/README.md) or [Start](../start/README.md).
- Do not put architecture narrative here; use [Engineering](../engineering/README.md) and route to architecture source material from there.
- Do not hand-edit generated API or registry output; update the generator, registry, or source input.
- When a legacy matrix or target-state design becomes stable lookup material, migrate it here or add a redirect from the legacy path.
- Keep reference pages precise, short, and schema-oriented; move historical rationale to `archive/docs/` after extraction.
- Keep generated reference outputs under generator ownership; update generator inputs and rerun generation.

## High-Traffic Reference Migration Index

Use this index for stable lookup claims that still appear in legacy locations:

- `docs/roadmap/data/*.yml` → [Data Dictionary](data-dictionary.md), [Reconciliation Break Taxonomy](reconciliation-break-taxonomy.md) for canonical lookup fields.
- [`archive/docs/status/IMPROVEMENTS.md`](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/status/IMPROVEMENTS.md) → historical context only; prefer [Provider Capability Matrix](provider-capability-matrix.md) and [Provider Validation Matrix](provider-validation-matrix.md) for stable lookup claims.
- `docs/status/provider-validation-matrix.md` → [Provider Validation Matrix](provider-validation-matrix.md) as canonical evidence lookup.
- archived `docs/providers/provider-comparison.md` → [Provider Capability Matrix](provider-capability-matrix.md), then [archive copy](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/providers/provider-comparison.md) for historical context.
- archived `docs/providers/provider-confidence-baseline.md` → [Provider Capability Matrix](provider-capability-matrix.md), [Provider Validation Matrix](provider-validation-matrix.md), then [archive copy](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/providers/provider-confidence-baseline.md) for historical context.
- archived `docs/providers/README.md` → [Provider Integration Status](provider-integration-status.md), then [archive copy](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/providers/README.md).
- Legacy strategy/status handoff notes in legacy docs → [Strategy Promotion History](strategy-promotion-history.md) and [Backtest Preflight and Stage Telemetry](backtest-preflight-and-stage-telemetry.md) only after schema stability is validated.
- `docs/status/provider-capability-matrix.md` → [Provider Capability Matrix](provider-capability-matrix.md)
- `docs/status/provider-integration-status.md` → [Provider Integration Status](provider-integration-status.md)
- `docs/status/provider-validation-matrix.md` → [Provider Validation Matrix](provider-validation-matrix.md)
- `docs/status/provider-validation-evidence-schema.md` → [Provider Validation Evidence Schema](provider-validation-evidence-schema.md)
- `docs/status/contract-compatibility-matrix.md` → [Contract Compatibility Matrix](contract-compatibility-matrix.md)
- `docs/status/provider-capability-inventory.md` → [Provider Capability Matrix](provider-capability-matrix.md)
- `docs/status/provider-adapters-closure-summary.md` → [Provider Integration Status](provider-integration-status.md)
- `docs/status/provider-core-hardening-notes.md` → [Provider Integration Status](provider-integration-status.md)
- `docs/status/provider-failover-hardening.md` → [Provider Integration Status](provider-integration-status.md)
- `docs/status/provider-test-gap-baseline.md` → [Provider Validation Matrix](provider-validation-matrix.md), [Provider Validation Evidence Schema](provider-validation-evidence-schema.md)
- `docs/status/provider-test-minimums.md` → [Provider Validation Matrix](provider-validation-matrix.md)
- `docs/status/ibkr-provider-inventory.md` → [Provider Integration Status](provider-integration-status.md), [Provider Capability Matrix](provider-capability-matrix.md)
- `docs/status/IMPROVEMENTS.md`, `docs/status/EVALUATIONS_AND_AUDITS.md`, `docs/status/FEATURE_INVENTORY.md`, `docs/status/TODO.md` → [Provider Capability Matrix](provider-capability-matrix.md), [Design Review Memo](design-review-memo.md), or [Reconciliation Break Taxonomy](reconciliation-break-taxonomy.md), depending on claim type; historical copies in [archive/docs/reference](https://github.com/rodoHasArrived/Meridian-main/tree/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/reference).

If a replacement page is still missing for a high-traffic legacy route, keep a short redirect stub at the old location and track it in `archive/docs/reference/README.md` until canonical replacement is in place. The 2026-09-11 archive cleanup (`982eea2d`) removed both indexes listed below; recreate them with the next archived reference batch. The links show their last content.

### Archive destination for legacy reference material

When a legacy reference document is retired, move it under `archive/docs/reference/` and keep migration context in:

- [Reference Archive](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/reference/README.md)
- [Archive README](https://github.com/rodoHasArrived/Meridian-main/blob/8a420730765d99de02c2ac4e9ba6cea062987f9b/archive/docs/README.md)

## Ownership and Governance Alignment

- Primary reference contracts: [documentation-ownership.md](../documentation-ownership.md)
- Canonical lookup ownership:
  - API, contract, schema, and field-shape contracts should point to active source modules in `docs/source/data/source-modules.yml` where behavior is implemented.
  - Provider capability and integration status claims should match `docs/reference/provider-capability-matrix.md` + `docs/reference/provider-integration-status.md` + current evidence packet updates in `docs/reference/provider-validation-*`.
  - Cross-cutting generated/automation output belongs under `docs/generated/README.md`, never hand-edited.

## Generated References

Generated reference output remains script-owned. Start with [Generated Documentation](../generated/README.md) before editing any generated page or report.

## Reference Evidence Contracts

- `docs/reference/provider-validation-matrix.md` and `docs/reference/provider-integration-status.md` are tied to current provider validation evidence.
- `docs/reference/provider-validation-evidence-schema.md` defines required fields and report shape for evidence packets.
- Any reference updates to API/contract fields must include corresponding source or registry proof; do not propagate stale snapshots into this lane.
