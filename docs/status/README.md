# Status And Reporting Source-Material Index

**Status:** controlled-migration
**Owner:** core-team
**Reviewed:** 2026-05-31

This folder remains a migration source for status snapshots and generated reports. In the rebuilt model, stakeholder interpretation starts at [`docs/product/README.md`](../product/README.md) and roadmap truth in [`docs/roadmap/README.md`](../roadmap/README.md).

## Canonical Interpretation Paths

- [Documentation Front Door](../README.md)
- [Product Documentation](../product/README.md)
- [Roadmap Registry](../roadmap/README.md)
- [Generated Documentation](../generated/README.md)
- [Documentation Ownership Contract](../documentation-ownership.md)

## Controlled Migration Inputs

| Source item | Migration target |
| --- | --- |
| `ROADMAP.md`, `ROADMAP_COMBINED.md`, `FEATURE_INVENTORY.md`, `TARGET_END_PRODUCT.md`, `IMPROVEMENTS.md`, `FULL_IMPLEMENTATION_TODO.md`, `OPPORTUNITY_SCAN.md`, `PROGRAM_STATE.md`, `production-status.md`, `BROKER_PHASE_*`, `provider-*`, `workstation-*` status snapshots | All migrated to `archive/docs/status/*` with archive-stub replacements in `docs/status/*` |
| Additional status source files with historical copies (e.g., `contract-compatibility-matrix`, `provider-capability-matrix`, etc.) | Same-named files under `archive/docs/status` |
| `evidence/` | Current evidence packets continue in `status/evidence/` until replaced by canonical workflows |

## Generated Outputs

- Keep generated report owners unchanged unless generator contracts change:
  - `ROADMAP_SUMMARY.md`
  - `program-state-summary.md`
  - `api-docs-report.md`
  - `badge-sync-report.md`
  - `CHANGELOG.md`
  - `coverage-report.md`
  - `doc-health-dashboard.md`
  - `docs-automation-summary.md`
  - `link-repair-report.md`
  - `metrics-dashboard.md`
  - `rules-report.md`
  - `TODO.md`
  - `wpf-screen-development-tracker.md`
- Include generated JSON artifacts as read-only outputs unless generator contracts change:
  - `workflow-validation-summary.json`
  - `program-state-summary.json`
  - `docs-automation-summary.json`
  - `doc-health-dashboard.json`
  - `wpf-screen-development-tracker.json`
  - `workflow-manifest.json`
  - `todo-scan-results.json`
  - `workstation-cockpit-acceptance-matrix.json`
  - `run-contract.schema.json`

Do not hand-edit generated outputs, including companion JSON artifacts.

- Keep one-off workflow logs as automation artifacts:
  - `governance-workflow-check.log`

## High-Traffic Redirects

See the full migration index in [`../../archive/docs/status/README.md`](../../archive/docs/status/README.md).
- `provider-validation-matrix.md` → [`../reference/provider-validation-matrix.md`](../reference/provider-validation-matrix.md)
- `provider-capability-matrix.md` → [`../reference/provider-capability-matrix.md`](../reference/provider-capability-matrix.md)
- `provider-validation-evidence-schema.md` → [`../reference/provider-validation-evidence-schema.md`](../reference/provider-validation-evidence-schema.md)
- `provider-integration-status.md` → [`../reference/provider-integration-status.md`](../reference/provider-integration-status.md)
- `ROADMAP_SUMMARY.md` → [`../roadmap/generated/ROADMAP_SUMMARY.md`](../roadmap/generated/ROADMAP_SUMMARY.md)

## Migration Rule

Do not place durable roadmap truth in hand-authored status pages. Update `docs/roadmap/data/*.yml` and regenerated roadmap views when status state changes.

