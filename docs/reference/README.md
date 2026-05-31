# Reference Documentation

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-05-30

This is the canonical lookup lane for Meridian APIs, environment variables, CLI/configuration surfaces, schemas, provider capability matrices, glossary-style definitions, and stable product/reference tables.

Reference docs answer "what exists and what shape does it have?" For procedures, use [Start](../start/README.md), [Engineering](../engineering/README.md), or [Operators](../operators/README.md).

## Canonical Lookup Areas

| Area | Canonical or migration source | Notes |
| --- | --- | --- |
| HTTP and local API contracts | [API Reference](api-reference.md), [OMS/EMS Integration](oms-ems-integration.md), [Governance Report Packs](governance-report-packs.md) | Keep route, request, response, idempotency, and storage-shape details here or in generated API docs. |
| Data fields and normalization | [Data Dictionary](data-dictionary.md), [Data Uniformity](data-uniformity.md), [Reconciliation Break Taxonomy](reconciliation-break-taxonomy.md) | Stable field/type definitions and cross-provider terminology. |
| Environment and config | [Environment Variables](environment-variables.md), [EDGAR Reference Data](edgar-reference-data.md) | Lookup details only; setup procedures belong in operators/start docs. |
| Provider capability and readiness lookup | [Provider Capability Matrix](provider-capability-matrix.md), [Provider Validation Matrix](provider-validation-matrix.md), [Provider Integration Status](provider-integration-status.md), [Provider Comparison](../providers/provider-comparison.md) | Canonical lookup in this lane; keep procedure and rollout details in operators. |
| Contract compatibility and schemas | [Contract Compatibility Matrix](contract-compatibility-matrix.md), [Provider Validation Evidence Schema](provider-validation-evidence-schema.md), [Workstation Cockpit Acceptance Matrix](../status/workstation-cockpit-acceptance-matrix.md) | Keep generated or matrix-owned content in place until moved through a focused migration. |
| UFL and asset profiles | [UFL Supported Assets Index](ufl-supported-assets-index.md), [UFL Capability Model](ufl-capability-model.md), [UFL Conformance Matrix](ufl-conformance-matrix.md) | Target-state lookup material now lives in reference. |
| Ledger, accounting, and reporting contracts | [Ledger Journal Store](ledger-journal-store.md), [Export Preflight Rules](export-preflight-rules.md), [Governance Report Packs](governance-report-packs.md) | Stable contracts and artifact layouts. |
| Strategy and research contracts | [Backtest Preflight and Stage Telemetry](backtest-preflight-and-stage-telemetry.md), [Research Briefing Workflow](research-briefing-workflow.md), [Strategy Promotion History](strategy-promotion-history.md) | DTOs, persistence fields, and workflow contract shapes. |
| Dependencies and open-source notices | [Open Source References](open-source-references.md), [Dependencies](../DEPENDENCIES.md) | Package inventory and third-party acknowledgement lookup. |

## Provider Lookup Rule

Provider-selection claims belong in capability and validation matrices, not dated evaluations. Backfill provider guidance should distinguish procedure, lookup, and implementation ownership:

- Procedures for credentials, setup, repair, and fallback operation belong in [Operators](../operators/README.md).
- Capability, data-type, coverage, rate-limit, and validation lookup belongs in this reference lane or generated/matrix-owned status files.
- Provider orchestration and fallback logic belongs in engineering/source docs and current source README files.

Do not treat old rate-limit, entitlement, or data-quality statements as current without checking live provider documentation or current validation evidence.

## Storage And Streaming Lookup Rule

Stable lookup details for storage and streaming should live in reference, generated API/source views, or current source READMEs. Use dated evaluations only for rationale. Active lookup claims for file formats, WAL behavior, retention tiers, provider ID ranges, SLO thresholds, dedupe keys, schema versions, or alert names must be backed by current source, generated output, or validation evidence.

## Active Reference Files

| Document | Description |
| --- | --- |
| [API Reference](api-reference.md) | HTTP API endpoints, request/response schemas. |
| [Backtest Preflight and Stage Telemetry](backtest-preflight-and-stage-telemetry.md) | Backtest trust-gate DTOs/service contract and stage-aware progress telemetry fields. |
| [Brand Assets](brand-assets.md) | Brand asset reference material. |
| [Data Dictionary](data-dictionary.md) | Data field definitions and types. |
| [Data Uniformity](data-uniformity.md) | Cross-provider consistency guidelines. |
| [Design Review Memo](design-review-memo.md) | Key design constraints and decisions retained as reference material. |
| [EDGAR Reference Data](edgar-reference-data.md) | EDGAR filer, ticker association, XBRL fact, filing-derived security data, CLI, API, and local storage reference. |
| [Contract Compatibility Matrix](contract-compatibility-matrix.md) | Shared contract/versioning baseline for workstation routes, DTOs, services, and migration behavior. |
| [Provider Capability Matrix](provider-capability-matrix.md) | Adapter coverage and readiness states by capability surface. |
| [Provider Integration Status](provider-integration-status.md) | Per-provider phase, DI/factory posture, blockers, and evidence refresh cadence. |
| [Provider Validation Matrix](provider-validation-matrix.md) | Wave 1 provider gate matrix and promotion evidence criteria. |
| [Provider Validation Evidence Schema](provider-validation-evidence-schema.md) | Required validation packet artifacts and schema fields for provider trust decisions. |
| [Environment Variables](environment-variables.md) | Credential and configuration reference. |
| [Export Preflight Rules](export-preflight-rules.md) | Export validation rule engine, rule IDs, and reuse pattern. |
| [Governance Report Packs](governance-report-packs.md) | Local-first fund-ops report-pack artifact routes, contracts, and storage layout. |
| [Ledger Journal Store](ledger-journal-store.md) | Postgres journal-entry, accounting-period, migration, and DI contract. |
| [OMS/EMS Integration](oms-ems-integration.md) | Versioned integration endpoint contracts, idempotent ingest semantics, adapter diagnostics, Excel sync policy, signing, and runbook steps. |
| [Open Source References](open-source-references.md) | Third-party library acknowledgements. |
| [Reconciliation Break Taxonomy](reconciliation-break-taxonomy.md) | Versioned canonical break classes and reason codes used by ledger reconciliation. |
| [Research Briefing Workflow](research-briefing-workflow.md) | Shared Research workspace briefing contracts, endpoint, and shell binding flow. |
| [Strategy Promotion History Persistence](strategy-promotion-history.md) | Durable promotion decision chain fields and JSONL-backed history behavior. |

## Migration Rules

- Do not put step-by-step setup guides here; use [Operators](../operators/README.md) or [Start](../start/README.md).
- Do not put architecture narrative here; use [Engineering](../engineering/README.md) and route to architecture source material from there.
- Do not hand-edit generated API or registry output; update the generator, registry, or source input.
- When a legacy matrix or target-state design becomes stable lookup material, migrate it here or add a redirect from the legacy path.
- Keep reference pages precise, short, and schema-oriented; move historical rationale to `archive/docs/` after extraction.

## Generated References

Generated reference output remains script-owned. Start with [Generated Documentation](../generated/README.md) before editing any generated page or report.
