# Project Status Documentation

**Last Reviewed:** 2026-05-21
**Current Delivery Theme:** Executing the DK1/DK2 implementation program on top of the closed Wave 1 trust gate while advancing the Wave 2-4 path to evidence-backed investment operations across cockpit hardening, shared-model continuity, accounting, reconciliation, and governed report productization

This folder contains the repository's active status, roadmap, readiness, and reporting surfaces. Use it with [../plans/current-direction-and-status.md](../plans/current-direction-and-status.md) for the consolidated planning interpretation and [../plans/README.md](../plans/README.md) for the detailed active blueprint set.

## Status marker legend

- `active`: hand-authored current-truth guidance.
- `generated`: automation-owned output; regenerate instead of manual edits.
- `historical`: archived snapshot; reference only.

## What Lives Here

This folder mixes two kinds of documents:

- hand-authored strategy and status docs that should guide decisions
- generated reports that summarize documentation, coverage, TODO, or validation state

If a file says it is auto-generated, regenerate it instead of editing it manually.

## Hand-Authored Source Of Truth

| Document | Status | Description |
| --- | --- | --- |
| [PROGRAM_STATE.md](PROGRAM_STATE.md) | `active` | Canonical wave status labels, ownership/escalation metadata (primary owner, backup owner, SLA, dependency owners), target dates, and evidence links reused by status docs |
| [ROADMAP_COMBINED.md](ROADMAP_COMBINED.md) | `active` | Short stakeholder-facing snapshot that combines roadmap, opportunities, and target-state direction |
| [ROADMAP.md](ROADMAP.md) | `active` | Primary wave-structured delivery roadmap |
| [OPPORTUNITY_SCAN.md](OPPORTUNITY_SCAN.md) | `active` | Prioritized repo-grounded opportunities that sit alongside the roadmap |
| [TARGET_END_PRODUCT.md](TARGET_END_PRODUCT.md) | `active` | Concise description of Meridian's intended finished product |
| [FEATURE_INVENTORY.md](FEATURE_INVENTORY.md) | `active` | Current-vs-target capability inventory across platform and product areas |
| [provider-validation-matrix.md](provider-validation-matrix.md) | `active` | Evidence-backed provider readiness matrix used by readiness docs |
| [provider-capability-matrix.md](provider-capability-matrix.md) | `active` | Canonical adapter capability/readiness matrix for provider follow-up ownership |
| [contract-compatibility-matrix.md](contract-compatibility-matrix.md) | `active` | Compatibility, deprecation, and migration policy for workstation/strategy/ledger contracts |
| [production-status.md](production-status.md) | `active` | Current production and pilot-readiness caveats |
| [kernel-readiness-dashboard.md](kernel-readiness-dashboard.md) | `active` | Single hand-authored DK program status dashboard for subsystem readiness, gate state, and rollback posture |
| [IMPROVEMENTS.md](IMPROVEMENTS.md) | `active` | Tracked implementation themes and recommended focus areas |
| [FULL_IMPLEMENTATION_TODO.md](FULL_IMPLEMENTATION_TODO.md) | `active` | Normalized broader implementation backlog |
| [EVALUATIONS_AND_AUDITS.md](EVALUATIONS_AND_AUDITS.md) | `active` | Consolidated index of evaluations and audits |
| [evidence/](evidence/) | `active` | DK1/Wave evidence runbooks, thresholds, rationale maps, and evidence packet/templates |

Backlog split: [`IMPROVEMENTS.md`](IMPROVEMENTS.md) tracks prioritized themes and focus areas, while [`FULL_IMPLEMENTATION_TODO.md`](FULL_IMPLEMENTATION_TODO.md) tracks the normalized flat execution backlog.

## Generated Status Reports

| Document | Status | Description |
| --- | --- | --- |
| [CHANGELOG.md](CHANGELOG.md) | `generated` | Generated repository/doc snapshot summary |
| [TODO.md](TODO.md) | `generated` | Informational TODO/FIXME aggregation from source comments |
| [doc-health-dashboard.md](doc-health-dashboard.md) | `generated` | Documentation health report |
| [ROADMAP_SUMMARY.md](ROADMAP_SUMMARY.md) | `generated` | Generated roadmap summary (from `build/scripts/docs/render-roadmap-docs.py`) |
| [coverage-report.md](coverage-report.md) | `generated` | Documentation coverage summary |
| [metrics-dashboard.md](metrics-dashboard.md) | `generated` | Documentation metrics dashboard |
| [docs-automation-summary.md](docs-automation-summary.md) | `generated` | Latest docs automation run summary |
| [program-state-summary.md](program-state-summary.md) | `generated` | Generated wave escalation summary used by roadmap/readiness reporting |
| [api-docs-report.md](api-docs-report.md) | `generated` | API docs validation summary |
| [example-validation.md](example-validation.md) | `generated` | Code-block validation output |
| [link-repair-report.md](link-repair-report.md) | `generated` | Internal link audit output |
| [rules-report.md](rules-report.md) | `generated` | Documentation rules-engine output |
| [badge-sync-report.md](badge-sync-report.md) | `generated` | README badge synchronization report |
| [workflow-drift-report.md](workflow-drift-report.md) | `generated` | Workflow command/artifact drift and governance report |

Machine-readable sidecars that remain active in this folder:

- `docs-automation-summary.json` - automation run summary consumed by docs tooling
- `program-state-summary.json` - generated wave ownership/escalation metadata consumed by roadmap/readiness reporting
- `workflow-validation-summary.json` - workflow command + artifact governance validation summary consumed by CI/doc tooling
- `workflow-manifest.json` - canonical workflow ownership/refresh-policy manifest used to generate workflow docs and drift checks

Artifact ownership and refresh policy routing lives in [`../generated/workflow-command-reference.md`](../generated/workflow-command-reference.md), generated from `workflow-manifest.json`. Each workflow entry now declares owner lane, refresh trigger, retention policy, and canonical output roots used by drift checks.

## Canonical topic map

| Topic | Canonical current-truth doc (`active`) | Supporting docs and status |
| --- | --- | --- |
| Roadmap direction | [ROADMAP.md](ROADMAP.md) | [ROADMAP_COMBINED.md](ROADMAP_COMBINED.md) (`active` stakeholder snapshot), [ROADMAP_SUMMARY.md](ROADMAP_SUMMARY.md) (`generated`) |
| Program state | [PROGRAM_STATE.md](PROGRAM_STATE.md) | [program-state-summary.md](program-state-summary.md) (`generated`) |
| Provider status/readiness | [provider-validation-matrix.md](provider-validation-matrix.md) | [provider-capability-matrix.md](provider-capability-matrix.md) (`active` capability follow-up) |
| Readiness and production posture | [kernel-readiness-dashboard.md](kernel-readiness-dashboard.md) | [production-status.md](production-status.md) (`active` caveat posture), generated dashboards in `docs/status/*dashboard*.md` (`generated`) |

Historical snapshots stay under `archive/docs/` and are linked from this index as `historical` references.

## Dashboard Evidence Reference

For dashboard purpose, required evidence inputs, `.md` + `.json` output paths, blocker triage, and stale-evidence handling guidance, see [Documentation Automation - Status Dashboard Evidence Surfaces](../development/documentation-automation.md#status-dashboard-evidence-surfaces).

For orphan-doc cleanup driven by the health dashboard output, use [Orphaned Doc Triage Index](../operations/orphaned-doc-triage-index.md) as the current link-recovery intake list.

The golden-path pilot dashboard is generated as validation evidence, not hand-authored status
prose. Run `PilotAcceptanceHarnessTests`, then
`build/scripts/docs/generate-pilot-readiness-dashboard.py` to emit
`artifacts/pilot-acceptance/latest/pilot-readiness-dashboard.md` and its JSON sidecar; the
`Golden Path Validation` workflow uploads those files as `pilot-acceptance-evidence`.

## Archived Snapshots

These dated snapshots remain useful for history, but they no longer act as active status guidance:

- [Documentation triage (2026-03-21)](../../archive/docs/summaries/DOCUMENTATION_TRIAGE_2026_03_21.md)
- [Now / Next / Later roadmap (2026-03-25)](../../archive/docs/summaries/ROADMAP_NOW_NEXT_LATER_2026_03_25.md)
- [Broker phase promotion checklist template](../../archive/docs/summaries/broker-phase-promotion-checklist-template.md)
- [Dead code inventory](../../archive/docs/summaries/dead-code-inventory.md)
- [IBKR provider inventory](../../archive/docs/summaries/ibkr-provider-inventory.md)
- [Provider adapters closure summary](../../archive/docs/summaries/provider-adapters-closure-summary.md)
- [Provider capability inventory](../../archive/docs/summaries/provider-capability-inventory.md)
- [Provider core hardening notes](../../archive/docs/summaries/provider-core-hardening-notes.md)
- [Provider failover hardening notes](../../archive/docs/summaries/provider-failover-hardening.md)
- [Provider test gap baseline](../../archive/docs/summaries/provider-test-gap-baseline.md)
- [Provider test minimums](../../archive/docs/summaries/provider-test-minimums.md)
- [Health dashboard snapshot (2026-05-04)](../../archive/docs/summaries/health-dashboard-2026-05-04.md)
- [Kernel parity placeholder snapshot (2026-04-20)](../../archive/docs/summaries/KERNEL_PARITY_STATUS-placeholder-2026-04-20.md)

## Recommended Reading Order

1. [../plans/current-direction-and-status.md](../plans/current-direction-and-status.md)
2. [ROADMAP_COMBINED.md](ROADMAP_COMBINED.md)
3. [ROADMAP.md](ROADMAP.md)
4. [kernel-readiness-dashboard.md](kernel-readiness-dashboard.md)
5. [production-status.md](production-status.md)
6. [OPPORTUNITY_SCAN.md](OPPORTUNITY_SCAN.md)
7. [TARGET_END_PRODUCT.md](TARGET_END_PRODUCT.md)
8. [../plans/README.md](../plans/README.md)
9. [FEATURE_INVENTORY.md](FEATURE_INVENTORY.md)
10. [provider-validation-matrix.md](provider-validation-matrix.md)
11. [contract-compatibility-matrix.md](contract-compatibility-matrix.md)
12. [IMPROVEMENTS.md](IMPROVEMENTS.md)

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
- **Workstation state:** The web dashboard is the active operator UI lane, now with support evidence for Overview Today panel, Price Alerts, Strategy Designer route-action safety for GET versus POST backend actions, Covered Call, Quant Notebook helpers, Trading Recent Fills detail state, full-console readiness checkpoints, provider-routing refresh posture in Settings/Data, abort-backed Reporting exports, and Meridian Design System reference workbench/tokenized-color work; WPF remains support evidence for workflow briefing, provider/activity triage, canonical launch/deep-link, single-instance forwarding, and automation; cockpit hardening, shared-model continuity, durable reconciliation, and governed-report acceptance still remain
- **Governance state:** Security Master is a delivered baseline and governance is now in active productization on top of it, with operations-continuity close-lane APIs now available as support evidence for broker intake, Security Master resolution, ledger posting, reconciliation, approval, close, and governed reopen
- **Provider state:** The active Wave 1 gate is closed around Alpaca, Robinhood, Yahoo, checkpoint reliability, and Parquet proof; broader provider inventory remains deferred outside that closure claim
- **Documentation state:** Status and plan navigation now starts with the consolidated current-direction document, then the canonical roadmap, production-status posture, and subordinate execution plans; structured roadmap/source registries, generated source README coverage, and stale-doc/hash validation now control documentation freshness; generated TODO scans should exclude repo-local diagnostic tool caches rather than treating `.tools/.store` package documentation as project work
- **Archive state:** Deprecated prior release checklists, UI notes, and dated snapshots should live under `archive/docs/` and stay out of the active status tree
- **DK program state:** Active implementation window (2026-04-20 to 2026-06-26) with weekly dashboard updates and subsystem milestones

## Related Documentation

- [Plans Overview](../plans/README.md)
- [Current Direction and Status](../plans/current-direction-and-status.md)
- [Evidence-Backed Investment Operations Plan](../plans/evidence-backed-investment-operations-plan.md)
- [Trading Workstation Migration Blueprint](../plans/trading-workstation-migration-blueprint.md)
- [Governance and Fund Operations Blueprint](../plans/governance-fund-ops-blueprint.md)
- [Backtest Studio Unification Blueprint](../plans/backtest-studio-unification-blueprint.md)
- [Architecture Overview](../architecture/overview.md)
- [Main Documentation Index](../README.md)

- [`workstation-governance-state-model.md`](workstation-governance-state-model.md): canonical workstation governance state model, transition table, and v1 additive compatibility expectations.
