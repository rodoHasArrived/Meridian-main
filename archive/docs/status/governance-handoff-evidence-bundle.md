# Governance Handoff Evidence Bundle

Date: 2026-05-31
Scope: enforce second-tier AI handoff packet validation in CI

## Before

- `HEAD` CI step used direct `check-ai-handoff.py` execution and did not run the new strict automation alias:
  - `.github/workflows/ci.yml` (from `HEAD`): `python3 build/scripts/docs/check-ai-handoff.py --strict --output docs/status/ai-handoff-checklist-report.md --json-output docs/status/ai-handoff-checklist-report.json`
- Documentation automation runner profiles used non-strict `check-ai-handoff` in `core` and `full`:
  - `build/scripts/docs/run-docs-automation.py` (`PROFILE_CONFIG`) contained `check-ai-handoff` entries.
- Strict handoff check existed only as a manual CLI flag (`--strict`), with no dedicated runner/script entry.
- No governance run log was required by design at the workflow level in this path.

## After

- Added strict alias in docs runner:
  - `build/scripts/docs/run-docs-automation.py` now defines `check-ai-handoff-strict` with `--strict`, JSON output, and markdown output.
- Wired strict alias into profiles:
  - `core` and `full` profiles in `run-docs-automation.py` now include `check-ai-handoff-strict`.
- CI now gates the check through runner alias (required status output):
  - `.github/workflows/ci.yml` runs:
    - `python3 build/scripts/docs/run-docs-automation.py --scripts check-ai-handoff-strict --json-output docs/status/docs-automation-summary.json --summary-output docs/status/docs-automation-summary.md`
- Added schema enforcement in handoff validator:
  - `build/scripts/docs/check-ai-handoff.py` validates required handoff fields in strict mode:
    - `scope`, `inputs loaded`, `changes made`, `validation`, `open risks`, `next lane`, `required context`, `optional context`.
- Added host-target config support with fallback:
  - Optional `--host-targets` argument in `check-ai-handoff.py`.
  - Default path support: `build/scripts/docs/ai-handoff-host-targets.json`.
  - Falls back to built-in host target list if config is missing.

## Validation artifacts (post-change)

- Workflow execution log:
  - `docs/status/governance-workflow-check.log`
- Automation summary artifacts:
  - `docs/status/docs-automation-summary.md`
  - `docs/status/docs-automation-summary.json`
- Handoff report artifacts:
  - `docs/status/ai-handoff-checklist-report.md`
  - `docs/status/ai-handoff-checklist-report.json`

## Snapshot excerpts

- `python3 build/scripts/docs/run-docs-automation.py --scripts check-ai-handoff-strict ...` output:
  - `Selected scripts (1): check-ai-handoff-strict`
  - `Running check-ai-handoff-strict...`
  - `-> success (...)`
  - `Wrote markdown summary: docs/status/docs-automation-summary.md`
  - `Wrote JSON summary: docs/status/docs-automation-summary.json`
- `docs/status/docs-automation-summary.md`:
  - single entry table row for `check-ai-handoff-strict` with status `success`.
- `docs/status/ai-handoff-checklist-report.md`:
  - `# AI Handoff Checklist Compliance: pass`
  - `All required host guidance files reference the shared handoff checklist.`

