# Status Reports and Evidence

**Status:** automation-owned-support
**Owner:** core-team
**Reviewed:** 2026-09-23

This folder holds generated repository reports, compatibility entry points, and retained evidence
contracts. Delivery status comes from the [roadmap registry](../roadmap/README.md); release readiness
comes from the [implementation and readiness tracker](../product/implementation-todo-list.md).
The current registry records production readiness as **blocked**. A regenerated dashboard, a
registered screen, or a text-presence score does not certify a release.

Start with the [documentation front door](../README.md), [Product](../product/README.md),
[generated documentation policy](../generated/README.md), and [ownership contract](../documentation-ownership.md).

## Report Inventory and Owners

Generated Markdown and JSON companions are read-only outputs. Update the inputs or generator and
rerun it from the repository root. Paths in the command column are relative to that root.

| Reports | Owning command or source |
| --- | --- |
| [Roadmap summary](ROADMAP_SUMMARY.md) | `python build/scripts/docs/render-roadmap-docs.py --summary`; compatibility copy of the [canonical summary](../roadmap/generated/ROADMAP_SUMMARY.md). |
| [Program state](program-state-summary.md) and [JSON](program-state-summary.json) | `python scripts/generate_program_state_summary.py`; same roadmap/program registries. |
| [TODO scan](TODO.md) | `python build/scripts/docs/scan-todos.py --output docs/status/TODO.md --json-output docs/status/todo-scan-results.json`; scan JSON is ignored local output. |
| [Changelog](CHANGELOG.md) | `python build/scripts/docs/generate-changelog.py --output docs/status/CHANGELOG.md --recent 50`; Git history, not a release certification. |
| [Documentation health](doc-health-dashboard.md) and [JSON](doc-health-dashboard.json) | `generate-health-dashboard.py` in the core automation profile. |
| [Automation summary](docs-automation-summary.md) and [JSON](docs-automation-summary.json) | `run-docs-automation.py --profile core`; results cover the listed scripts only. |
| [Coverage](coverage-report.md) | `generate-coverage.py`; documentation/source coverage inventory, not runtime test coverage. |
| [API documentation](api-docs-report.md) | `validate-api-docs.py`. |
| [Documentation rules](rules-report.md) | `rules-engine.py --rules build/rules/doc-rules.yaml`. |
| [Examples](example-validation.md) | `validate-examples.py`. |
| [Link review](link-repair-report.md) | `repair-links.py`; reports repository link debt unless explicitly run with `--docs-dir docs/status`. |
| [Badge synchronization](badge-sync-report.md) | `sync-readme-badges.py`; a badge edit report, not CI execution evidence. |
| [AI inventory](ai-inventory-report.md) and [JSON](ai-inventory-report.json) | `check-ai-inventory.py`. |
| [AI handoff checks](ai-handoff-checklist-report.md) and [JSON](ai-handoff-checklist-report.json) | `check-ai-handoff.py --strict`. |
| [Prompt routing](prompt-route-lint-report.json), [handoff packet](ai-handoff-packet.md), and [packet JSON](ai-handoff-packet.json) | `prompt-route-linter.py` then `handoff-packet-generator.py`; the core profile generates a routing/schema fixture, not actual task telemetry. |
| [API contract coverage](api-contract-coverage-dashboard.md) and [JSON](api-contract-coverage-dashboard.json) | `generate-api-contract-coverage-dashboard.py`; static API/test-reference coverage. |
| [Paper replay reliability](paper-replay-reliability-dashboard.md) and [JSON](paper-replay-reliability-dashboard.json) | `generate-paper-replay-reliability-dashboard.py`; static text/presence checks. |
| [Evidence continuity](evidence-continuity-dashboard.md) and [JSON](evidence-continuity-dashboard.json) | `generate-evidence-continuity-dashboard.py`; static text/presence checks. |
| [Governance readiness](governance-readiness-dashboard.md) and [JSON](governance-readiness-dashboard.json) | `generate-governance-readiness-dashboard.py`; static text/presence checks. |
| [Pilot readiness](pilot-readiness-dashboard.md) and [JSON](pilot-readiness-dashboard.json) | `generate-pilot-readiness-dashboard.py`; also requires run-specific, ignored pilot-acceptance evidence. Opt in explicitly; absence remains a gap. |
| [UI route wiring](ui-route-wiring-report.md) and [JSON](ui-route-wiring-report.json) | `python build/scripts/docs/generate-ui-route-wiring-report.py`; static wiring scan. |
| [WPF screen tracker](wpf-screen-development-tracker.md) and [JSON](wpf-screen-development-tracker.json) | `npm run generate-wpf-screen-tracker`; registry/source/test-reference/screenshot inventory, not proof of executed UI tests. |
| [Workflow manifest](workflow-manifest.json), [drift report](workflow-drift-report.md), and [validation summary](workflow-validation-summary.json) | `python build/scripts/docs/generate-workflow-manifest.py`; workflow declarations and file inventory. |

Unless a full path is shown, Python report owners above live in `build/scripts/docs/`. Some dashboard
generators deliberately emit `1970-01-01T00:00:00+00:00` as a stable timestamp for reproducible output.
That value is neither an execution date nor a claim that evidence is current. Verify the registry
snapshot date, Git revision, and actual run artifacts when assessing freshness.

## Retained Contracts and Evidence

These are reviewed source documents or inputs, not generated report output:

| File | Purpose and review boundary |
| --- | --- |
| [Run-contract schema](run-contract.schema.json) | Retained JSON schema for provider/workflow evidence payloads. Change only with its consumers; validate as JSON Schema. |
| [Cockpit acceptance matrix](workstation-cockpit-acceptance-matrix.json) | Reviewable route/test/screenshot mapping. Validate with `python scripts/dev/validate_workstation_cockpit_acceptance_matrix.py --matrix docs/status/workstation-cockpit-acceptance-matrix.json --summary`. Runtime artifact paths are capture requirements, not stored pass evidence. |
| [DK1 parity runbook](evidence/dk1-pilot-parity-runbook.md) | Active run-date packet and human sign-off procedure. |
| [DK1 baseline thresholds](evidence/dk1-baseline-trust-thresholds.md) | Retained pilot defaults and FP/FN review contract; recalibrate before broader use. |
| [DK1 trust rationale](evidence/dk1-trust-rationale-mapping.md) | Operator reason-code explanation contract. |
| [Wave 2 evidence packet](evidence/wave2-cockpit-evidence-packet.md) | Historical May 2026 test/sign-off snapshot, retained for existing evidence references. |
| [Wave 4 evidence template](evidence/wave4-evidence-template.md) | Capture template; no test execution or current approval is implied. |

## Compatibility Entry Points

Retain these paths while generators, validators, checklists, or guidance consume them:

| Compatibility path | Current source |
| --- | --- |
| [ROADMAP.md](ROADMAP.md) | [Roadmap registry](../roadmap/README.md) |
| [FEATURE_INVENTORY.md](FEATURE_INVENTORY.md) | [Product](../product/README.md) and [roadmap register](../roadmap/generated/roadmap-register.md) |
| [provider-validation-matrix.md](provider-validation-matrix.md) | [Canonical provider validation matrix](../reference/provider-validation-matrix.md) |
| [contract-compatibility-matrix.md](contract-compatibility-matrix.md) | [Canonical contract compatibility matrix](../reference/contract-compatibility-matrix.md) |
| [kernel-readiness-dashboard.md](kernel-readiness-dashboard.md) | [Program state](program-state-summary.md), [readiness tracker](../product/implementation-todo-list.md), and [DK1 procedure](evidence/dk1-pilot-parity-runbook.md) |

The former archive destinations for these redirects are absent from the current tree. Git history
preserves earlier versions; this index links only to current files.

## Retired in the September 2026 Review

- The unreferenced accounting productization checklist moved to the
  [historical summary archive](../../archive/docs/summaries/accounting-productization-checklist.md).
  Use the canonical readiness tracker and roadmap registry for current work.
- Removed `metrics-dashboard.md`: the April snapshot reported 0% from zero measured runs, and
  the current generator still contains placeholder collectors. Its output contract remains in the
  automation tooling; implement real collectors and supply run evidence before publishing another
  metrics report. Missing data must not be interpreted as failed builds or tests.
- Removed the empty `slo-reports/` placeholder, which had no reports and linked to a missing template.
  Current operating guidance starts at [Operators](../operators/README.md).
- One-off logs, scan JSON, and runtime evidence belong in ignored artifacts unless a maintained
  workflow explicitly requires a tracked contract or report.

## Refresh and Verify

```bash
python -m pip install --requirement build/scripts/docs/requirements.txt
python build/scripts/docs/render-roadmap-docs.py --summary
python scripts/generate_program_state_summary.py
python build/scripts/docs/generate-ui-route-wiring-report.py
npm ci --no-fund --no-audit
npm run generate-wpf-screen-tracker
python build/scripts/docs/run-docs-automation.py --profile core --summary-output docs/status/docs-automation-summary.md --json-output docs/status/docs-automation-summary.json
python build/scripts/docs/generate-structure-docs.py --workflows-only
python scripts/check_program_state_consistency.py
python build/scripts/docs/validate-docs-structure.py --summary
git diff --check
```

Refresh the other reports through their listed generators when their inputs change. The core profile
deliberately excludes the local pilot acceptance artifact; use `--include-pilot-readiness` only when
reviewing that evidence. Review reported gaps and command failures rather than changing delivery
states or acceptance claims to make dashboards green.
