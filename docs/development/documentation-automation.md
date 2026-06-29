# Documentation Automation Guide

> User guide for the documentation automation system in Meridian.

## Overview

The documentation automation system keeps project documentation accurate and up-to-date through
Python scripts in `build/scripts/docs/`. The `documentation.yml` workflow now re-runs the tracked
documentation and diagram refresh path on documentation-facing changes, while local runs remain the
fastest way to inspect and review generated diffs before you commit them.

### What It Does

| Feature | Description |
| --------- | ------------- |
| **UI Diagram Refresh** | Regenerates WPF UI implementation diagrams and the WPF screen development tracker from live source files before rendering committed SVG artifacts |
| **Structure Generation** | Auto-generates repository structure docs from the file tree |
| **README Tree Sync** | Updates markdown tree markers in README and AI-facing docs when the tree sync tool is run locally |
| **Provider Registry** | Extracts provider metadata from `[DataSource]` attributes |
| **ADR Indexing** | Builds an index of Architecture Decision Records |
| **AI Instruction Sync** | Keeps CLAUDE.md, Copilot instructions, and agent files in sync |
| **TODO Scanning** | Finds TODO/FIXME/HACK comments and generates tracking docs |
| **Health Dashboard** | Computes documentation quality metrics and health score |
| **Link Repair** | Detects and auto-fixes broken internal links |
| **Code Example Validation** | Validates syntax of code examples in docs |
| **Documentation Coverage** | Measures what percentage of code is documented |
| **Changelog Generation** | Generates changelogs from conventional commit messages |
| **Custom Rules Engine** | Enforces project-specific documentation rules |
| **Automatic TODO Issue Creation** | Converts untracked TODO/FIXME items into GitHub issues automatically |
| **Local Orchestration Runner** | Runs documentation automation profiles from one command with JSON/Markdown summaries |

## Diagram Automation

WPF UI implementation diagrams and the WPF screen development tracker are generated from source code without hand-maintained drift instead of being maintained by hand:

```bash
npm run generate-diagrams
```

`make generate-diagrams` delegates to the same package script. Keep docs and Makefile targets on
that canonical entrypoint instead of calling the Node script path directly.

That command updates `docs/diagrams/ui-navigation-map.dot`, `docs/diagrams/ui-implementation-flow.dot`, and `docs/status/wpf-screen-development-tracker.md` from these inputs:

- `src/Meridian.Wpf/App.xaml.cs`
- `src/Meridian.Wpf/MainWindow.xaml.cs`
- `src/Meridian.Wpf/Services/NavigationService.cs`
- `src/Meridian.Wpf/Views/MainPage.xaml`
- `src/Meridian.Wpf/Views/MainPage.xaml.cs`
- `src/Meridian.Wpf/Views/Pages.cs`
- `docs/screenshots/desktop/README.md`
- `tests/Meridian.Wpf.Tests/**/*.cs`

Run the diagram generation command before committing diagram source changes so committed UI diagrams
stay synchronized as the implementation evolves.

Use the targeted shortcut when only the generated screen tracker needs a refresh:

```bash
npm run generate-wpf-screen-tracker
```

## Running Documentation Automation

Run the relevant documentation script locally and review the diff before committing. The
`documentation.yml` workflow verifies the same tracked refresh path in CI, but local runs are still
the best place to inspect generated changes before they reach a pull request.

## README Tree Sync

The repository uses the GitHub Marketplace action [`RavelloH/readme-tree`](https://github.com/RavelloH/readme-tree) to keep embedded repository trees current in contributor-facing and AI-facing markdown files.

### Managed Markdown Files

- `README.md`
- `docs/ai/README.md`
- `docs/ai/claude/CLAUDE.structure.md`

### Marker Format

Add the following markers anywhere a generated tree should appear:

```md
<!-- readme-tree start -->
<!-- readme-tree end -->
```

When the tree sync tool is run, it refreshes the content between those markers. Review generated
changes before committing them.

## Manual Dispatch Options

When triggering `documentation.yml` manually from the Actions UI:

| Input | Default | Description |
| ------- | --------- | ------------- |
| `refresh_all` | false | Run the full documentation and diagram refresh path even if you are only sanity-checking the workflow manually |

## Job Execution Flow

```text
validate-docs
    |
    +-- rules engine
    +-- example validation
    +-- AI inventory / handoff / contract drift checks

regenerate-docs
    |
    +-- docs automation profile (core)
    +-- Mermaid refresh
    +-- WPF UI diagram refresh
    +-- PlantUML render
    +-- workflow inventory refresh
    +-- dashboard delta gate
    +-- git diff / whitespace checks
```

The workflow is intentionally small: one validation job and one regeneration/gating job.

## Python Scripts

### Core Scripts

#### `scan-todos.py`

Scans the codebase for explicit `TODO:`, `FIXME:`, `HACK:`, and `NOTE:` annotations.
The scanner skips generated outputs, TODO artifacts, template scaffolds, and `.claude/worktrees/` duplicates so the report stays actionable.

```bash
# Basic scan
python3 build/scripts/docs/scan-todos.py --output docs/status/TODO.md

# JSON output
python3 build/scripts/docs/scan-todos.py --json-output results.json

# Exclude NOTEs
python3 build/scripts/docs/scan-todos.py --include-notes false
```

#### `generate-structure-docs.py`

Generates repository structure documentation. The tree intentionally skips local build/runtime
artifacts, caches, backups, logs, and symlinked entries so committed generated docs describe the
checkout structure rather than machine-specific working files.

```bash
# Full structure
python3 build/scripts/docs/generate-structure-docs.py --output docs/generated/repository-structure.md

# Provider registry only
python3 build/scripts/docs/generate-structure-docs.py --providers-only --output docs/generated/provider-registry.md

# Workflows overview
python3 build/scripts/docs/generate-structure-docs.py --workflows-only --output docs/generated/workflows-overview.md
```

#### `update-claude-md.py`

Syncs the Repository Structure section across AI instruction files.

```bash
# Update CLAUDE.md
python3 build/scripts/docs/update-claude-md.py --claude-md CLAUDE.md --structure-source docs/generated/repository-structure.md

# Dry run
python3 build/scripts/docs/update-claude-md.py --dry-run
```

### Expansion Scripts

#### `generate-health-dashboard.py`

Generates a documentation health score and metrics dashboard.

```bash
python3 build/scripts/docs/generate-health-dashboard.py \
  --output docs/status/doc-health-dashboard.md \
  --json-output docs-health.json
```

#### `repair-links.py`

Detects and optionally auto-fixes broken internal links in documentation.

```bash
# Report only
python3 build/scripts/docs/repair-links.py --output docs/status/link-repair-report.md

# Auto-fix broken links
python3 build/scripts/docs/repair-links.py --auto-fix --output docs/status/link-repair-report.md
```

#### `validate-examples.py`

Validates code examples (Python, JSON, bash, C#) found in markdown files.

```bash
python3 build/scripts/docs/validate-examples.py --output docs/status/example-validation.md
```

#### `check-ai-inventory.py`

Scans AI assistant assets and fails when the catalog indexes drift from the tracked files. It
covers `.codex/`, `.claude/`, `.github/agents`, `.github/prompts`, `.github/instructions`,
AI-related GitHub Actions workflows, `docs/ai/`, and MCP prompt/resource/tool surfaces.

```bash
python3 build/scripts/docs/check-ai-inventory.py --summary

python3 build/scripts/docs/check-ai-inventory.py \
  --output docs/status/ai-inventory-report.md \
  --json-output docs/status/ai-inventory-report.json
```

#### `check-ai-handoff.py`

Validates shared AI handoff guidance discoverability across AI systems and requires host instructions
to reference `docs/ai/agent-handoff-checklist.md` when multi-agent workflows are in scope.
In strict mode, it also validates the required handoff packet fields used for lane transitions.

`check-ai-handoff-strict` is the orchestration alias used in default documentation profiles
(`core`, `full`) and CI.

```bash
python3 build/scripts/docs/check-ai-handoff.py \
  --output docs/status/ai-handoff-checklist-report.md \
  --json-output docs/status/ai-handoff-checklist-report.json

python3 build/scripts/docs/check-ai-handoff.py --strict

python3 build/scripts/docs/run-docs-automation.py --scripts check-ai-handoff-strict

```

#### `generate-coverage.py`

Measures documentation coverage of public APIs, providers, and configuration.

```bash
python3 build/scripts/docs/generate-coverage.py --output docs/status/coverage-report.md
```

#### `generate-changelog.py`

Generates a changelog from conventional commit messages.

```bash
python3 build/scripts/docs/generate-changelog.py --output docs/status/CHANGELOG.md --recent 50
```

#### `rules-engine.py`

Enforces custom documentation validation rules defined in YAML.

```bash
python3 build/scripts/docs/rules-engine.py \
  --rules build/rules/doc-rules.yaml \
  --output docs/status/rules-report.md
```

#### `create-todo-issues.py` _(new)_

Creates GitHub issues for untracked TODO items discovered by `scan-todos.py`.

**Features:**

- Validates scan-todos JSON structure with clear error messages
- Handles network failures and HTTP errors gracefully
- Caps issue titles at 120 characters for better readability
- Returns structured outcome (created/existing/dry-run)
- Optional `--output-json` for machine-readable summaries
- Prevents duplicate issues by searching for existing markers
- Uses issue refs, derived priority, and local line context from scan output when composing issue bodies

```bash
# 1) Generate scan JSON
python3 build/scripts/docs/scan-todos.py \
  --output docs/status/TODO.md \
  --json-output docs/status/todo-scan-results.json

# 2) Create issues for untracked TODOs (dry run)
python3 build/scripts/docs/create-todo-issues.py \
  --scan-json docs/status/todo-scan-results.json \
  --repo owner/repo \
  --dry-run

# 3) Create real issues with JSON summary
python3 build/scripts/docs/create-todo-issues.py \
  --scan-json docs/status/todo-scan-results.json \
  --repo owner/repo \
  --max-issues 25 \
  --output-json docs/status/todo-issue-creation-summary.json
```

**Output JSON Structure:**

```json
{
  "created": 5,
  "existing": 3,
  "dry_run": 0,
  "skipped": 2,
  "total_untracked": 10,
  "issues": [
    {"status": "created", "number": 123, "file": "src/Example.cs", "line": 45},
    {"status": "existing", "number": 100, "file": "src/Other.cs", "line": 22}
  ]
}
```

#### `run-docs-automation.py` _(new)_

Runs documentation tooling as a single orchestrated command with profile support.

**Features:**

- Orchestrates multiple documentation scripts in sequence
- Validates prerequisites (e.g., scan-todos required for --auto-create-todos)
- Coordinates JSON output paths for downstream automation
- Skips issue creation if scan-todos fails
- Produces machine-readable JSON and human-readable Markdown summaries

```bash
# Plan what would run for quick profile
python3 build/scripts/docs/run-docs-automation.py --profile quick --dry-run

# Run full automation and write machine + human summaries
python3 build/scripts/docs/run-docs-automation.py \
  --profile full \
  --json-output docs/status/docs-automation-summary.json \
  --summary-output docs/status/docs-automation-summary.md

# Run an explicit subset of scripts
python3 build/scripts/docs/run-docs-automation.py \
  --scripts scan-todos,validate-examples,repair-links

# Run core checks and automatically create GitHub issues for untracked TODOs
python3 build/scripts/docs/run-docs-automation.py \
  --profile core \
  --auto-create-todos \
  --todo-repo owner/repo \
  --todo-max-issues 25
```

**Important:** When using `--auto-create-todos`, the runner requires `scan-todos` in the selected scripts. It automatically adds `--json-output` to scan-todos and skips issue creation if the scan fails.

### Orchestration Profiles

| Profile | Included Scripts | Best For |
| -------- | ------------------ | ---------- |
| `quick` | `scan-todos`, `validate-examples`, `repair-links`, `check-ai-inventory`, `check-ai-handoff`, `generate-workflow-manifest` | Fast local verification before commits |
| `core` _(default)_ | `scan-todos`, `generate-structure-docs`, `generate-health-dashboard`, `validate-examples`, `check-ai-inventory`, `check-ai-handoff-strict`, `generate-coverage`, `generate-workflow-manifest` | Day-to-day documentation maintenance |
| `full` | All documented scripts, including changelog + rules engine | Scheduled runs and release prep |

The runner exits non-zero if any script fails (unless `--continue-on-error` is set), making it CI-friendly for preflight checks and local automation.

When `--auto-create-todos` is enabled:

1. The runner validates that `scan-todos` is in the selected scripts
2. It automatically adds `--json-output docs/status/todo-scan-results.json` to scan-todos
3. If scan-todos fails, issue creation is skipped with a clear error message
4. On success, it calls `create-todo-issues.py` with `--output-json docs/status/todo-issue-creation-summary.json`


### Dashboard JSON Source-of-Truth

The dashboard generators (`generate-health-dashboard.py` and `generate-metrics-dashboard.py`) use **JSON as the canonical stage**:

1. The script computes metrics and writes canonical JSON (`--json-output`).
2. Markdown rendering (`--output`) loads that JSON payload and renders deterministically from it.
3. Automation should always pass both flags for dashboards, so Markdown cannot drift from the machine-readable payload.
4. Required evidence inputs are validated; missing evidence causes a non-zero exit to surface automation drift early.

## Status Dashboard Evidence Surfaces

Use the dashboards below as the canonical generated status surfaces for readiness evidence triage. Each dashboard includes both a Markdown operator view (`.md`) and machine-readable sidecar (`.json`) path so automation and human review stay aligned.

### Dashboard Catalog

| Dashboard | Purpose | Required evidence inputs | Outputs |
| --- | --- | --- | --- |
| **Docs automation run summary** | Consolidated pass/fail view across all selected docs scripts, plus per-script duration and failure details. | `run-docs-automation.py` execution metadata for selected profile/scripts; downstream script result files produced during the run. | `docs/status/docs-automation-summary.md` and `docs/status/docs-automation-summary.json` |
| **Wave 1 provider validation status** | Tracks provider gate posture (Alpaca/Robinhood/Yahoo), checkpoint reliability, and DK1 packet readiness handoff. | `artifacts/provider-validation/_automation/<yyyy-mm-dd>/wave1-validation-summary.json`; `artifacts/provider-validation/_automation/<yyyy-mm-dd>/dk1-pilot-parity-packet.json`; optional signed `dk1-operator-signoff.json` packet binding. | `docs/status/provider-validation-matrix.md` and `docs/status/provider-validation-matrix.json` |
| **Replay verification readiness** | Confirms active paper-session replay verification freshness used by trading readiness claims. | Execution replay verification outputs (for example `GET /api/execution/sessions/{sessionId}/replay` evidence captures and execution-audit artifacts referenced by readiness workflows). | `docs/status/kernel-readiness-dashboard.md` and `docs/status/kernel-readiness-dashboard.json` |
| **Reconciliation and operator inbox state** | Summarizes unresolved/open/in-review reconciliation cases and account-scoped operator work-item routing. | Reconciliation case state exports plus workstation operator inbox/readiness evidence (`/api/workstation/operator/inbox`, `/api/workstation/trading/readiness`). | `docs/status/program-state-summary.md` and `docs/status/program-state-summary.json` |
| **Report-pack and contract compatibility posture** | Governs report-pack and contract drift risk before releases, including compatibility/deprecation checks. | Report-pack generation/validation outputs; contract checks (for example `scripts/check_contract_compatibility_gate.py` outputs and generated contract review packet artifacts). | `docs/status/contract-compatibility-matrix.md` and `docs/status/contract-compatibility-matrix.json` |

### Required Evidence References Per Dashboard

- **Docs automation run summary**
  - Must reference the exact profile or explicit `--scripts` set used for the run.
  - Must retain script-level failure detail when blockers are present.
  - Must link any dependent artifacts that failed generation (for example `workflow-drift-report.md`).
- **Wave 1 provider validation status**
  - Must include current run-date summary artifact and DK1 packet path under `artifacts/provider-validation/_automation/<yyyy-mm-dd>/`.
  - Must include signed operator sign-off binding when claiming `ready-for-operator-review` or stronger state.
  - Must flag stale packets whose timestamp predates the latest validation run.
- **Replay verification readiness**
  - Must reference the session identifier and replay verification timestamp used by readiness conclusions.
  - Must mark replay evidence stale when fill/order/ledger counters diverge from latest verification audit.
  - Must link remediation action (rerun replay verification) before readiness can return to green.
- **Reconciliation and operator inbox state**
  - Must include counts by reconciliation case state (`open`, `in-review`, `resolved`) and account scope if applicable.
  - Must reference operator inbox/readiness endpoint captures used for routing and sign-off claims.
  - Must classify blockers by owning workflow lane (trading, accounting, or shared operations).
- **Report-pack and contract compatibility posture**
  - Must include latest contract compatibility gate output and packet artifact references.
  - Must call out any report-pack generation failures, missing sections, or schema drift.
  - Must identify blocking compatibility changes requiring migration/deprecation action before release.

### Operator Guidance: Blockers and Stale Evidence

- Treat any dashboard blocker as **actionable** until an updated evidence artifact is generated and linked.
- Treat dated evidence as **stale** when it predates the latest related workflow run, packet, or replay audit.
- For stale evidence, rerun the narrowest supporting workflow first (provider validation, replay verification, reconciliation export, or contract check) and then regenerate docs automation summaries.
- Do not claim readiness from a dashboard whose `.md` and `.json` sidecars disagree; rerun automation and resolve drift before sign-off.
- The CI dashboard-delta gate only fails on new severe blocker or contract-drift counts when the previous dashboard baseline is readable; if the push baseline is unavailable, the run records that gap and skips the severe-regression failure instead of comparing against an all-zero baseline.
- When a dashboard remains blocked after rerun, escalate with the failing command, artifact path, and owning lane in the summary report.

### Artifact Generation Commands (Full + Targeted)

```bash
# Full docs automation profile with explicit status outputs
python3 build/scripts/docs/run-docs-automation.py \
  --profile full \
  --json-output docs/status/docs-automation-summary.json \
  --summary-output docs/status/docs-automation-summary.md

# Targeted generation for status surfaces (explicit scripts mode)
python3 build/scripts/docs/run-docs-automation.py \
  --scripts generate-health-dashboard,generate-coverage,generate-workflow-manifest,rules-engine \
  --json-output docs/status/docs-automation-summary.json \
  --summary-output docs/status/docs-automation-summary.md

# Targeted governance/evidence checks with contract compatibility verification
python3 build/scripts/docs/run-docs-automation.py \
  --scripts scan-todos,validate-examples,check-ai-inventory \
  --continue-on-error \
  --json-output docs/status/docs-automation-summary.json \
  --summary-output docs/status/docs-automation-summary.md
python3 scripts/check_contract_compatibility_gate.py --base origin/main --head HEAD
```

## Custom Rules

Documentation rules are defined in `build/rules/doc-rules.yaml`. See [Adding Custom Rules](adding-custom-rules.md) for details.

## Generated Output Files

| File | Generator | Purpose |
| ------ | ----------- | --------- |
| `docs/generated/repository-structure.md` | generate-structure-docs.py | Repository file tree |
| `docs/generated/provider-registry.md` | generate-structure-docs.py | Data provider catalog |
| `docs/generated/workflows-overview.md` | generate-structure-docs.py | CI/CD workflow summary |
| `docs/generated/adr-index.md` | workflow inline | ADR index table |
| `docs/generated/configuration-schema.md` | workflow inline | Config options from appsettings |
| `docs/generated/project-context.md` | DocGenerator (C#) | Key interfaces and services |
| `docs/status/TODO.md` | scan-todos.py | TODO tracking |
| `docs/status/todo-scan-results.json` | scan-todos.py | Machine-readable TODO scan results used for auto issue creation |
| `docs/status/todo-issue-creation-summary.json` | create-todo-issues.py | Machine-readable issue creation summary with status counts and issue numbers |
| `docs/status/doc-health-dashboard.md` | generate-health-dashboard.py | Health metrics rendered from canonical JSON |
| `docs/status/doc-health-dashboard.json` | generate-health-dashboard.py | Canonical health dashboard payload |
| `docs/status/wpf-screen-development-tracker.md` | npm run generate-wpf-screen-tracker | WPF screen Gantt chart and automated per-screen TODO checklist |
| `docs/status/wpf-screen-development-tracker.json` | npm run generate-wpf-screen-tracker | Machine-readable WPF screen tracker payload |
| `docs/status/link-repair-report.md` | repair-links.py | Broken link report |
| `docs/status/example-validation.md` | validate-examples.py | Code example validation |
| `docs/status/metrics-dashboard.md` | generate-metrics-dashboard.py | Build/test metrics rendered from canonical JSON |
| `docs/status/metrics-dashboard.json` | generate-metrics-dashboard.py | Canonical metrics dashboard payload |
| `docs/status/ai-inventory-report.md` | check-ai-inventory.py | AI assistant asset catalog drift report |
| `docs/status/ai-inventory-report.json` | check-ai-inventory.py | Machine-readable AI inventory and drift findings |
| `docs/status/ai-handoff-checklist-report.json` | check-ai-handoff.py / `check-ai-handoff-strict` | Machine-readable handoff discoverability and schema checks |
| `docs/status/ai-handoff-checklist-report.md` | check-ai-handoff.py / `check-ai-handoff-strict` | AI handoff checklist discoverability and policy alignment |
| `docs/status/coverage-report.md` | generate-coverage.py | Documentation coverage |
| `docs/status/rules-report.md` | rules-engine.py | Rule validation results |
| `docs/status/docs-automation-summary.md` | run-docs-automation.py | Human-readable automation run summary with status table and failure details |
| `docs/status/docs-automation-summary.json` | run-docs-automation.py | Machine-readable automation run summary with script execution metadata |
| `docs/generated/workflow-command-reference.md` | generate-workflow-manifest.py | Generated command snippets sourced from canonical workflow manifest |
| `docs/status/workflow-validation-summary.json` | generate-workflow-manifest.py | CI-consumable command manifest summary + missing target/script findings |
| `docs/status/workflow-drift-report.md` | generate-workflow-manifest.py | Human-readable drift report comparing declared workflows to actual targets/scripts |

All generated files include an "auto-generated" notice and should not be edited manually.

## Canonical Workflow Inventory Source Of Truth

Use `docs/generated/workflows-overview.md` as the authoritative GitHub Actions inventory. It is generated from `.github/workflows/*.yml` and `.github/workflows/*.yaml` files on disk. Do not hard-code explicit numeric workflow totals in docs under `README.md`, `archive/docs/developer/`, or `docs/development/`.

Workflow command governance uses separate generated artifacts:

1. `docs/status/workflow-manifest.json` — canonical declared command manifest.
2. `docs/generated/workflow-command-reference.md` — generated human-readable workflow command reference.
3. `docs/status/workflow-validation-summary.json` — generated machine-readable command validation summary.
4. `docs/status/workflow-drift-report.md` — generated drift surface for missing declared targets/scripts.

When GitHub Actions workflow files change:

- Run `python3 build/scripts/docs/generate-structure-docs.py --workflows-only --output docs/generated/workflows-overview.md`.
- Reference `docs/generated/workflows-overview.md` in docs instead of restating workflow counts or inventory tables manually.

When workflow command governance changes:

- Update `docs/status/workflow-manifest.json`.
- Run `python3 build/scripts/docs/generate-workflow-manifest.py`.

## Troubleshooting

### Workflow skipped all jobs

The `detect-changes` job uses path-based filtering. If your changes don't match any watched paths, jobs will be skipped. Use `workflow_dispatch` with `update_all=true` to force a full run.

### Script fails with encoding errors

All scripts handle encoding gracefully with `errors='replace'`. If you encounter persistent encoding issues, check for binary files with `.md` extensions.

### Generated files show no changes

The workflow only commits when actual content changes are detected. Timestamp-only changes in generated files are expected and intentional.

### Link repair reports false positives

The link repair script only checks internal (relative) links. Links to anchors require the target heading to exist exactly as referenced. Case-sensitive heading IDs may cause false positives.

### Need to run multiple doc checks locally like CI

Use the orchestrator:

```bash
python3 build/scripts/docs/run-docs-automation.py --profile core --summary-output docs/status/docs-automation-summary.md
```

If one script is flaky but you still want aggregate output, add `--continue-on-error` and inspect the Failures section in the summary markdown.

### Want TODOs automatically added to the project tracker

Use automatic TODO issue creation with the orchestrator:

```bash
python3 build/scripts/docs/run-docs-automation.py \
  --profile core \
  --auto-create-todos \
  --todo-repo owner/repo \
  --todo-max-issues 25
```

This creates GitHub issues (label: `auto-todo`) for TODO items that do not already reference an existing issue.

---

_This guide is part of the documentation automation system._

<!-- BEGIN AUTO-GENERATED: WORKFLOW-MANIFEST-DEV -->
### Workflow Manifest Snapshot (Generated)

Use this generated snapshot when validating docs automation and workflow drift.

| Workflow ID | Prerequisites | Validation checks |
| --- | --- | --- |
| `docs-automation-core` | Python 3.11+ available on PATH; Repository dependencies installed via make install | docs automation summary exists; workflow drift report exists |
| `desktop-screenshot-catalog` | Windows build host with PowerShell; Release WPF build available when using -SkipBuild | screenshot workflow artifacts produced |
| `provider-validation-wave1` | Provider credentials configured for Wave 1 adapters; PowerShell available | provider validation summary exists |
| `operator-inbox-route-validation` | dotnet SDK with Windows targeting packs; PowerShell available | operator inbox route artifact directory exists |
| `provider-validation-evidence-bundle` | Provider credentials configured for Wave 1 adapters; PowerShell available | provider evidence bundle output exists |
| `ibapi-smoke-build` | dotnet SDK with Windows targeting packs; PowerShell available | IBAPI smoke build script exists |
| `wpf-route-validation-position-blotter` | dotnet SDK with Windows targeting packs; PowerShell available | position blotter route artifact directory exists |
| `wpf-dev-loop-validation` | dotnet SDK with Windows targeting packs; PowerShell available | WPF dev-loop validation artifacts produced |
| `targeted-test` | Branch pushed to GitHub; GitHub CLI authenticated or manual Actions UI access; Curated Targeted Test mode selected; Repo-relative .NET test project under tests/ and specific filter when mode=dotnet-filtered; windows-latest runner when running WPF or desktop modes | Targeted Test workflow exists; Targeted Test validates curated modes |
| `robinhood-options-smoke` | dotnet SDK with Windows targeting packs; PowerShell available | Robinhood options smoke artifacts produced |
| `web-screenshot-capture` | Node.js 24 available on PATH; npm dependencies installed for src/Meridian.Ui/dashboard | web screenshot output directory exists |

_Generated by `python3 build/scripts/docs/generate-workflow-manifest.py`._
<!-- END AUTO-GENERATED: WORKFLOW-MANIFEST-DEV -->
