# Project Status Documentation

**Last Reviewed:** 2026-04-26
**Current Delivery Theme:** Executing the DK1/DK2 implementation program on top of the closed Wave 1 trust gate while advancing the Wave 2-4 path to core operator-readiness across cockpit hardening, shared-model continuity, and governance productization

This folder contains the repository's active status, roadmap, readiness, and reporting surfaces. Use it with [../plans/README.md](../plans/README.md) when you need both the current status snapshot and the active blueprint set.

## What Lives Here

This folder mixes two kinds of documents:

- hand-authored strategy and status docs that should guide decisions
- generated reports that summarize documentation, coverage, TODO, or validation state

If a file says it is auto-generated, regenerate it instead of editing it manually.

## Hand-Authored Source Of Truth

| Document | Description |
| --- | --- |
| [PROGRAM_STATE.md](PROGRAM_STATE.md) | Canonical wave status labels, owners, target dates, and evidence links reused by status docs |
| [ROADMAP_COMBINED.md](ROADMAP_COMBINED.md) | Short stakeholder-facing snapshot that combines roadmap, opportunities, and target-state direction |
| [ROADMAP.md](ROADMAP.md) | Primary wave-structured delivery roadmap |
| [OPPORTUNITY_SCAN.md](OPPORTUNITY_SCAN.md) | Prioritized repo-grounded opportunities that sit alongside the roadmap |
| [TARGET_END_PRODUCT.md](TARGET_END_PRODUCT.md) | Concise description of Meridian's intended finished product |
| [FEATURE_INVENTORY.md](FEATURE_INVENTORY.md) | Current-vs-target capability inventory across platform and product areas |
| [provider-validation-matrix.md](provider-validation-matrix.md) | Evidence-backed provider readiness matrix used by readiness docs |
| [contract-compatibility-matrix.md](contract-compatibility-matrix.md) | Compatibility, deprecation, and migration policy for workstation/strategy/ledger contracts |
| [production-status.md](production-status.md) | Current production and pilot-readiness caveats |
| [kernel-readiness-dashboard.md](kernel-readiness-dashboard.md) | Single hand-authored DK program status dashboard for subsystem readiness, gate state, and rollback posture |
| [IMPROVEMENTS.md](IMPROVEMENTS.md) | Tracked implementation themes and recommended focus areas |
| [FULL_IMPLEMENTATION_TODO_2026_03_20.md](FULL_IMPLEMENTATION_TODO_2026_03_20.md) | Normalized broader implementation backlog |
| [EVALUATIONS_AND_AUDITS.md](EVALUATIONS_AND_AUDITS.md) | Consolidated index of evaluations and audits |
| [wave4-evidence-template.md](wave4-evidence-template.md) | Deterministic template and seeded scenarios for Wave 4 governance evidence capture |

## Compatibility Views

| Document | Description |
| --- | --- |
| [ROADMAP_NOW_NEXT_LATER_2026_03_25.md](ROADMAP_NOW_NEXT_LATER_2026_03_25.md) | Refreshed Now / Next / Later compatibility view for the canonical roadmap |

## Generated Status Reports

| Document | Description |
| --- | --- |
| [CHANGELOG.md](CHANGELOG.md) | Generated repository/doc snapshot summary |
| [TODO.md](TODO.md) | Informational TODO/FIXME aggregation from source comments |
| [health-dashboard.md](health-dashboard.md) | Documentation health report |
| [coverage-report.md](coverage-report.md) | Documentation coverage summary |
| [metrics-dashboard.md](metrics-dashboard.md) | Documentation metrics dashboard |
| [docs-automation-summary.md](docs-automation-summary.md) | Latest docs automation run summary |
| [api-docs-report.md](api-docs-report.md) | API docs validation summary |
| [example-validation.md](example-validation.md) | Code-block validation output |
| [link-repair-report.md](link-repair-report.md) | Internal link audit output |
| [rules-report.md](rules-report.md) | Documentation rules-engine output |
| [badge-sync-report.md](badge-sync-report.md) | README badge synchronization report |

Machine-readable sidecars that remain active in this folder:

- `docs-automation-summary.json` - automation run summary consumed by docs tooling

## Archived Snapshots

These dated snapshots remain useful for history, but they no longer act as active status guidance:

- [Documentation triage (2026-03-21)](DOCUMENTATION_TRIAGE_2026_03_21.md)

## Recommended Reading Order

1. [ROADMAP_COMBINED.md](ROADMAP_COMBINED.md)
2. [ROADMAP.md](ROADMAP.md)
3. [kernel-readiness-dashboard.md](kernel-readiness-dashboard.md)
4. [production-status.md](production-status.md)
5. [OPPORTUNITY_SCAN.md](OPPORTUNITY_SCAN.md)
6. [TARGET_END_PRODUCT.md](TARGET_END_PRODUCT.md)
7. [../plans/README.md](../plans/README.md)
8. [FEATURE_INVENTORY.md](FEATURE_INVENTORY.md)
9. [provider-validation-matrix.md](provider-validation-matrix.md)
10. [contract-compatibility-matrix.md](contract-compatibility-matrix.md)
11. [IMPROVEMENTS.md](IMPROVEMENTS.md)

## Contributor Checklist (Required Headings)

When authoring or editing these doc categories, include the required section headers so docs lint passes:

- **Runbooks** (for example `docs/operations/operator-runbook.md`):
  - `## Troubleshooting`
- **Provider setup guides** (`docs/providers/*-setup.md`):
  - `## Prerequisites`
  - `## Configuration`

## Current Status Summary

- **Platform state:** Development / pilot-ready baseline with strong ingestion, storage, replay, and export foundations
- **Core delivery path:** Waves 1-4 define the core operator-ready baseline; Waves 5-6 deepen the product afterward
- **Workstation state:** WPF is the primary operator shell and retained local API/web surfaces support the same workspace contracts, but cockpit hardening and shared-model continuity still remain
- **Governance state:** Security Master is a delivered baseline and governance is now in active productization on top of it
- **Provider state:** The active Wave 1 gate is closed around Alpaca, Robinhood, Yahoo, checkpoint reliability, and Parquet proof; broader provider inventory remains deferred outside that closure claim
- **Documentation state:** Status and plan navigation now centers the canonical roadmap, production-status posture, and subordinate execution plans
- **DK program state:** Active implementation window (2026-04-20 to 2026-06-26) with weekly dashboard updates and subsystem milestones

## Related Documentation

- [Plans Overview](../plans/README.md)
- [Trading Workstation Migration Blueprint](../plans/trading-workstation-migration-blueprint.md)
- [Governance and Fund Operations Blueprint](../plans/governance-fund-ops-blueprint.md)
- [Backtest Studio Unification Blueprint](../plans/backtest-studio-unification-blueprint.md)
- [Architecture Overview](../architecture/overview.md)
- [Main Documentation Index](../README.md)
