# Documentation Automation Guide

> User guide for the documentation automation system in Meridian.

## Overview

The documentation automation system keeps project documentation accurate and up-to-date through a unified GitHub Actions workflow (`.github/workflows/documentation.yml`) and a suite of Python scripts in `build/scripts/docs/`.

### What It Does

| Feature | Description |
| --------- | ------------- |
| **UI Diagram Refresh** | Regenerates WPF UI implementation diagrams from live source files before rendering committed SVG artifacts |
| **Structure Generation** | Auto-generates repository structure docs from the file tree |
| **README Tree Sync** | Updates markdown tree markers in README and AI-facing docs on every push to `main` |
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

WPF UI implementation diagrams are generated from source code without hand-maintained drift instead of being maintained by hand:

```bash
npm run generate-diagrams
```

That command updates `docs/diagrams/ui-navigation-map.dot` and `docs/diagrams/ui-implementation-flow.dot` from these inputs:

- `src/Meridian.Wpf/App.xaml.cs`
- `src/Meridian.Wpf/MainWindow.xaml.cs`
- `src/Meridian.Wpf/Services/NavigationService.cs`
- `src/Meridian.Wpf/Views/MainPage.xaml`
- `src/Meridian.Wpf/Views/MainPage.xaml.cs`
- `src/Meridian.Wpf/Views/Pages.cs`

The `update-diagrams.yml` workflow now listens for those files, so the committed UI diagrams stay synchronized as the desktop implementation evolves.

## Workflow Triggers

The workflow runs automatically on:

- **Push to main** when documentation-related files change
- **Pull requests** to main with doc changes
- **Weekly schedule** (Monday 3 AM UTC) for full regeneration
- **Manual dispatch** via GitHub Actions UI with configurable options
- **Issue events** for AI Known Error intake

The repository also has a dedicated `readme-tree.yml` workflow that runs on every push to `main` and refreshes markdown files containing `<!-- readme-tree start -->` / `<!-- readme-tree end -->` markers.

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

On each push to `main`, `.github/workflows/readme-tree.yml` refreshes the content between those markers and commits the updated markdown back to the branch.

## Manual Dispatch Options

When triggering manually via Actions UI:

| Input | Default | Description |
| ------- | --------- | ------------- |
| `regenerate` | false | Force regenerate all docs |
| `update_all` | false | Update all auto-generated outputs |
| `dry_run` | true | Show changes without committing |
| `create_pr` | false | Create PR instead of direct commit |
| `scan_todos` | true | Run TODO scanning |
| `create_issues` | false | Create GitHub issues for untracked TODOs |
| `include_notes` | true | Include NOTE comments in TODO scan |
| `use_copilot` | true | Use AI for triage recommendations |
| `run_expansion` | true | Run expansion features |

## Job Execution Flow

```text
detect-changes (always runs first)
    |
    +-- validate-docs      (parallel)
    +-- regenerate-docs    (parallel)
    +-- scan-todos         (parallel)
    +-- validate-examples  (parallel)
    +-- generate-changelog (parallel)
    |
    +-- link-repair        (after regenerate-docs)
    +-- coverage-report    (after regenerate-docs)
    |
    +-- create-todo-issues (after scan-todos, manual only)
    |
    +-- report             (final, always runs)
```

Key optimization: `validate-docs`, `regenerate-docs`, and `scan-todos` all run in parallel after change detection completes, reducing total execution time.

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

Generates repository structure documentation.

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
  --output docs/status/health-dashboard.md \
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
| `quick` | `scan-todos`, `validate-examples`, `repair-links` | Fast local verification before commits |
| `core` _(default)_ | `scan-todos`, `generate-structure-docs`, `generate-health-dashboard`, `validate-examples`, `generate-coverage` | Day-to-day documentation maintenance |
| `full` | All documented scripts, including changelog + rules engine | Scheduled runs and release prep |

The runner exits non-zero if any script fails (unless `--continue-on-error` is set), making it CI-friendly for preflight checks and local automation.

When `--auto-create-todos` is enabled:

1. The runner validates that `scan-todos` is in the selected scripts
2. It automatically adds `--json-output docs/status/todo-scan-results.json` to scan-todos
3. If scan-todos fails, issue creation is skipped with a clear error message
4. On success, it calls `create-todo-issues.py` with `--output-json docs/status/todo-issue-creation-summary.json`

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
| `docs/status/health-dashboard.md` | generate-health-dashboard.py | Health metrics |
| `docs/status/link-repair-report.md` | repair-links.py | Broken link report |
| `docs/status/example-validation.md` | validate-examples.py | Code example validation |
| `docs/status/coverage-report.md` | generate-coverage.py | Documentation coverage |
| `docs/status/rules-report.md` | rules-engine.py | Rule validation results |
| `docs/status/docs-automation-summary.md` | run-docs-automation.py | Human-readable automation run summary with status table and failure details |
| `docs/status/docs-automation-summary.json` | run-docs-automation.py | Machine-readable automation run summary with script execution metadata |

All generated files include an "auto-generated" notice and should not be edited manually.

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
