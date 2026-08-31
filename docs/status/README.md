# Status And Reporting Source-Material Index

**Status:** automation-owned-support
**Owner:** core-team
**Reviewed:** 2026-07-19

This supporting folder contains automation-owned reports and compatibility artifacts still consumed
by documentation, readiness, and workflow tooling. It is not the durable roadmap source.
Stakeholder interpretation starts at [Product](../product/README.md), and roadmap truth lives in
the [Roadmap Registry](../roadmap/README.md).

## Canonical Interpretation Paths

- [Documentation Front Door](../README.md)
- [Product Documentation](../product/README.md)
- [Roadmap Registry](../roadmap/README.md)
- [Generated Documentation](../generated/README.md)
- [Documentation Ownership Contract](../documentation-ownership.md)

## Controlled Migration Inputs

| Source item | Migration target |
| --- | --- |
| `ROADMAP_COMBINED.md`, `TARGET_END_PRODUCT.md`, `IMPROVEMENTS.md`, `FULL_IMPLEMENTATION_TODO.md`, `OPPORTUNITY_SCAN.md`, `PROGRAM_STATE.md`, `production-status.md`, `BROKER_PHASE_*`, most `provider-*` and `workstation-*` status snapshots | Migrated to `archive/docs/status/*`; the archive-stub replacements formerly kept in `docs/status/*` have been removed |
| `ROADMAP.md`, `FEATURE_INVENTORY.md`, `provider-validation-matrix.md`, `contract-compatibility-matrix.md`, `kernel-readiness-dashboard.md` | Migrated to `archive/docs/status/*`; stubs retained at these paths because dashboard generators, checklist tooling, skill manifests, or installer validation still consume them |
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
  - `ui-route-wiring-report.md`
  - `wpf-screen-development-tracker.md`
- Include generated JSON artifacts as read-only outputs unless generator contracts change:
  - `workflow-validation-summary.json`
  - `program-state-summary.json`
  - `docs-automation-summary.json`
  - `doc-health-dashboard.json`
  - `wpf-screen-development-tracker.json`
  - `workflow-manifest.json`
  - `todo-scan-results.json`
  - `ui-route-wiring-report.json`
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
