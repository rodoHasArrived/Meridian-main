# Status And Reporting Source-Material Index

**Status:** controlled-migration
**Owner:** core-team
**Reviewed:** 2026-05-30

This folder is retained for current status surfaces, generated reports, and migration source material. In the rebuilt documentation model, stakeholder-facing interpretation starts at [Product Documentation](../product/README.md), while durable roadmap truth belongs to [Roadmap Registry](../roadmap/README.md) and `docs/roadmap/data/*.yml`.

Use current guidance first:

- [Documentation Front Door](../README.md)
- [Product Documentation](../product/README.md)
- [Roadmap Registry](../roadmap/README.md)
- [Generated Documentation](../generated/README.md)
- [Documentation Ownership Contract](../documentation-ownership.md)
- [Summaries Archive](../../archive/docs/summaries/README.md)

## Current Role In The Rebuild

- Keep generated reports automation-owned; update generators or source data instead of hand-editing emitted output.
- Treat hand-authored status pages as migration inputs unless they are explicitly linked from the rebuilt product or roadmap lanes.
- Collapse duplicate status truth into `docs/product/`, `docs/roadmap/data/*.yml`, generated roadmap views, or archive snapshots.
- Preserve evidence caveats: do not upgrade readiness language unless current evidence proves the claim.

## Controlled Current Status Inputs

| Document | Target lane | Notes |
| --- | --- | --- |
| [ROADMAP.md](ROADMAP.md) | roadmap/product | Wave-structured roadmap input; reconcile durable truth into roadmap registry and product summary. |
| [ROADMAP_COMBINED.md](ROADMAP_COMBINED.md) | product/roadmap | Stakeholder snapshot; extract durable current narrative into product docs. |
| [FEATURE_INVENTORY.md](FEATURE_INVENTORY.md) | product/reference | Capability inventory input; migrate stable lookup facts to product/reference docs. |
| [PROGRAM_STATE.md](PROGRAM_STATE.md) | roadmap/product | Program state input; preserve evidence and ownership caveats. |
| [production-status.md](production-status.md) | product/operators | Readiness caveat input; do not overstate readiness. |
| [provider-validation-matrix.md](provider-validation-matrix.md) | operators/reference | Provider readiness evidence input. |
| [provider-capability-matrix.md](provider-capability-matrix.md) | reference/operators | Adapter capability/readiness lookup input. |
| [contract-compatibility-matrix.md](contract-compatibility-matrix.md) | engineering/reference | Contract compatibility and migration policy input. |
| [kernel-readiness-dashboard.md](kernel-readiness-dashboard.md) | product/operators | DK/readiness status input. |
| [IMPROVEMENTS.md](IMPROVEMENTS.md) | roadmap/product/archive | Improvement themes; reconcile against roadmap registry before keeping active. |
| [FULL_IMPLEMENTATION_TODO.md](FULL_IMPLEMENTATION_TODO.md) | roadmap/archive | Flat backlog; reconcile against roadmap/source TODO registries. |
| [EVALUATIONS_AND_AUDITS.md](EVALUATIONS_AND_AUDITS.md) | archive/product | Consolidated historical index; user-owned edits may exist, so do not migrate without explicit inclusion. |
| [evidence/](evidence/) | product/operators/archive | Evidence runbooks/templates; keep only current evidence workflows active. |

## Generated Status Reports

Generated reports in this folder remain automation-owned. Examples include `CHANGELOG.md`, `TODO.md`, `doc-health-dashboard.md`, `ROADMAP_SUMMARY.md`, `coverage-report.md`, `metrics-dashboard.md`, `docs-automation-summary.md`, `program-state-summary.md`, `api-docs-report.md`, `example-validation.md`, `link-repair-report.md`, `rules-report.md`, `badge-sync-report.md`, and `workflow-drift-report.md`.

Machine-readable sidecars such as `docs-automation-summary.json`, `program-state-summary.json`, `workflow-validation-summary.json`, and `workflow-manifest.json` remain active only when current tooling writes or consumes them.

## Migration Rules

1. Do not create new durable roadmap truth in hand-authored status prose.
2. Update `docs/roadmap/data/*.yml` or roadmap renderers when registry-owned truth changes.
3. Update `docs/product/` when stakeholder-facing interpretation changes.
4. Archive dated snapshots under `archive/docs/summaries/` after replacement links exist.
5. Leave generated reports in place unless the owning generator or workflow is changed.
6. Preserve unrelated dirty changes in this folder as user-owned unless explicitly included.

## High-Traffic Status Redirects

- [broker-phase-promotion-checklist-template.md](broker-phase-promotion-checklist-template.md) → [archive/docs/summaries/broker-phase-promotion-checklist-template.md](../../archive/docs/summaries/broker-phase-promotion-checklist-template.md)
- [dead-code-inventory.md](dead-code-inventory.md) → [archive/docs/summaries/dead-code-inventory.md](../../archive/docs/summaries/dead-code-inventory.md)
- [ibkr-provider-inventory.md](ibkr-provider-inventory.md) → [archive/docs/summaries/ibkr-provider-inventory.md](../../archive/docs/summaries/ibkr-provider-inventory.md)
- [provider-adapters-closure-summary.md](provider-adapters-closure-summary.md) → [archive/docs/summaries/provider-adapters-closure-summary.md](../../archive/docs/summaries/provider-adapters-closure-summary.md)
- [provider-capability-inventory.md](provider-capability-inventory.md) → [archive/docs/summaries/provider-capability-inventory.md](../../archive/docs/summaries/provider-capability-inventory.md)
- [provider-core-hardening-notes.md](provider-core-hardening-notes.md) → [archive/docs/summaries/provider-core-hardening-notes.md](../../archive/docs/summaries/provider-core-hardening-notes.md)
- [provider-failover-hardening.md](provider-failover-hardening.md) → [archive/docs/summaries/provider-failover-hardening.md](../../archive/docs/summaries/provider-failover-hardening.md)
- [provider-test-gap-baseline.md](provider-test-gap-baseline.md) → [archive/docs/summaries/provider-test-gap-baseline.md](../../archive/docs/summaries/provider-test-gap-baseline.md)
- [provider-test-minimums.md](provider-test-minimums.md) → [archive/docs/summaries/provider-test-minimums.md](../../archive/docs/summaries/provider-test-minimums.md)
- [provider-capability-matrix.md](provider-capability-matrix.md) → [archive/docs/summaries/provider-capability-matrix.md](../../archive/docs/summaries/provider-capability-matrix.md)
- [contract-compatibility-matrix.md](contract-compatibility-matrix.md) → [archive/docs/summaries/contract-compatibility-matrix.md](../../archive/docs/summaries/contract-compatibility-matrix.md)
- [provider-validation-evidence-schema.md](provider-validation-evidence-schema.md) → [archive/docs/summaries/provider-validation-evidence-schema.md](../../archive/docs/summaries/provider-validation-evidence-schema.md)
- [provider-validation-matrix.md](provider-validation-matrix.md) → [archive/docs/summaries/provider-validation-matrix.md](../../archive/docs/summaries/provider-validation-matrix.md)
- [provider-integration-status.md](provider-integration-status.md) → [archive/docs/summaries/provider-integration-status.md](../../archive/docs/summaries/provider-integration-status.md)
