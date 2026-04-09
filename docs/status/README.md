# Project Status Documentation

**Last Reviewed:** 2026-04-05
**Current Delivery Theme:** Converging research, trading, data-operations, and governance workflows into one operator-ready platform

This folder contains the repository's active status, roadmap, readiness, and reporting surfaces. Use it with [../plans/README.md](../plans/README.md) when you need both the current status snapshot and the active blueprint set.

## What Lives Here

This folder mixes two kinds of documents:

- hand-authored strategy and status docs that should guide decisions
- generated reports that summarize documentation, coverage, TODO, or validation state

If a file says it is auto-generated, regenerate it instead of editing it manually.

## Hand-Authored Source Of Truth

| Document | Description |
|----------|-------------|
| [ROADMAP_COMBINED.md](ROADMAP_COMBINED.md) | Short stakeholder-facing snapshot that combines roadmap, opportunities, and target-state direction |
| [ROADMAP.md](ROADMAP.md) | Primary wave-structured delivery roadmap |
| [OPPORTUNITY_SCAN.md](OPPORTUNITY_SCAN.md) | Prioritized repo-grounded opportunities that sit alongside the roadmap |
| [TARGET_END_PRODUCT.md](TARGET_END_PRODUCT.md) | Concise description of Meridian's intended finished product |
| [FEATURE_INVENTORY.md](FEATURE_INVENTORY.md) | Current-vs-target capability inventory across platform and product areas |
| [provider-validation-matrix.md](provider-validation-matrix.md) | Evidence-backed provider readiness matrix used by readiness docs |
| [production-status.md](production-status.md) | Current production and pilot-readiness caveats |
| [IMPROVEMENTS.md](IMPROVEMENTS.md) | Tracked implementation themes and recommended focus areas |
| [FULL_IMPLEMENTATION_TODO_2026_03_20.md](FULL_IMPLEMENTATION_TODO_2026_03_20.md) | Normalized broader implementation backlog |
| [EVALUATIONS_AND_AUDITS.md](EVALUATIONS_AND_AUDITS.md) | Consolidated index of evaluations and audits |

## Generated Status Reports

| Document | Description |
|----------|-------------|
| [CHANGELOG.md](CHANGELOG.md) | Generated repository/doc snapshot summary |
| [TODO.md](TODO.md) | TODO/FIXME aggregation from source comments |
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

- [Documentation triage (2026-03-21)](../../archive/docs/summaries/DOCUMENTATION_TRIAGE_2026_03_21.md)
- [Now / Next / Later roadmap snapshot (2026-03-25)](../../archive/docs/summaries/ROADMAP_NOW_NEXT_LATER_2026_03_25.md)

## Recommended Reading Order

1. [ROADMAP_COMBINED.md](ROADMAP_COMBINED.md)
2. [ROADMAP.md](ROADMAP.md)
3. [OPPORTUNITY_SCAN.md](OPPORTUNITY_SCAN.md)
4. [TARGET_END_PRODUCT.md](TARGET_END_PRODUCT.md)
5. [../plans/README.md](../plans/README.md)
6. [FEATURE_INVENTORY.md](FEATURE_INVENTORY.md)
7. [provider-validation-matrix.md](provider-validation-matrix.md)
8. [production-status.md](production-status.md)
9. [IMPROVEMENTS.md](IMPROVEMENTS.md)

## Current Status Summary

- **Platform state:** Development / pilot-ready baseline with strong ingestion, storage, replay, and export foundations
- **Workstation state:** Web and WPF both expose meaningful workspace flows, but workflow hardening and parity work remain
- **Governance state:** Security Master, ledger, reconciliation, and reporting seams exist and are moving from visibility to productization
- **Provider state:** Breadth is strong, but readiness still depends on concrete validation evidence across key providers
- **Documentation state:** Status and plan navigation now centers the current roadmap, opportunity scan, and target-state narrative

## Related Documentation

- [Plans Overview](../plans/README.md)
- [Trading Workstation Migration Blueprint](../plans/trading-workstation-migration-blueprint.md)
- [Governance and Fund Operations Blueprint](../plans/governance-fund-ops-blueprint.md)
- [Backtest Studio Unification Blueprint](../plans/backtest-studio-unification-blueprint.md)
- [Architecture Overview](../architecture/overview.md)
- [Main Documentation Index](../README.md)
