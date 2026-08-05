# Documentation Automation Scripts

This directory contains Python scripts for automating documentation tasks in the Meridian project.

## Table of Contents

- [Core Scripts](#core-scripts)
- [Expansion Scripts](#expansion-scripts)
- [Enhanced Scripts](#enhanced-scripts)
- [Quick Reference](#quick-reference)
- [Development](#development)
- [Readiness and Coverage Dashboards](#readiness-and-coverage-dashboards)

## Core Scripts

### common.py

Shared helper module for documentation automation. It provides repo-root resolution,
stable path normalization, Markdown table rendering, generated headers, manifest
writing, safe file writes, lightweight YAML loading, and finding output helpers.

### validate-roadmap-registry.py

Validates `docs/roadmap/data/*.yml` schema headers, duplicate IDs, stage-gate
references, source-module references, required fields, and evidence requirements
for accepted or done items.

```bash
python3 build/scripts/docs/validate-roadmap-registry.py --summary
```

### render-roadmap-docs.py

Renders deterministic roadmap Markdown views and a generated manifest from
`docs/roadmap/data/*.yml`.

```bash
python3 build/scripts/docs/render-roadmap-docs.py --summary
```

### validate-source-readmes.py

Validates `docs/source/data/source-modules.yml`,
`docs/source/data/source-readme-coverage.yml`, and registered `src/**/README.md`
front matter, required sections, and generated block markers.

```bash
python3 build/scripts/docs/validate-source-readmes.py --summary
```

### sync-source-readmes.py

Creates missing source READMEs from `docs/source/data/source-modules.yml` so new modules can be
bootstrapped programmatically. Existing READMEs are preserved. Use `--tree` to discover nested
source folders under registered modules; `tree_roots` and ignored paths come from
`docs/source/data/source-readme-ignore.yml`. Use `--stale-only` after running
`mark-stale-docs.py --write` to limit README creation to modules already marked as needing review.

```bash
python3 build/scripts/docs/sync-source-readmes.py --summary
python3 build/scripts/docs/sync-source-readmes.py --create-missing --summary
python3 build/scripts/docs/sync-source-readmes.py --tree --max-depth 2 --summary
python3 build/scripts/docs/sync-source-readmes.py --tree --create-missing --max-depth 2 --summary
python3 build/scripts/docs/sync-source-readmes.py --create-missing --stale-only --summary
```

### render-source-docs.py

Renders deterministic source module views under `docs/source/generated/` and
updates only marked generated blocks in registered source READMEs. `--stale-only` limits README
block updates to modules listed in `docs/source/generated/stale-docs.json`.

```bash
python3 build/scripts/docs/render-source-docs.py --summary
python3 build/scripts/docs/render-source-docs.py --stale-only --summary
```

### scan-source-todos.py

Checks registry-backed TODO alignment for registered source modules.

```bash
python3 build/scripts/docs/scan-source-todos.py --summary
```

### validate-doc-hashes.py

Validates hash alignment for generated roadmap/source manifests and registered source modules. The
hash manifest records source-tree hashes plus README hashes for each registered module, so code
changes can intentionally fail the check until documentation has been reviewed and the manifest is
refreshed. Use `--write-module` when only specific stale modules have been reviewed; reserve the
broad `--write` refresh for a full accepted-baseline review.

```bash
python3 build/scripts/docs/validate-doc-hashes.py --summary
python3 build/scripts/docs/validate-doc-hashes.py --write-module SRC-HOST --summary
python3 build/scripts/docs/validate-doc-hashes.py --write --summary
```

### mark-stale-docs.py

Compares current registered source module hashes to
`docs/source/generated/source-hash-manifest.json` and writes a deterministic stale-doc report
without refreshing the accepted hash baseline. This lets agents and maintainers update only
outdated module READMEs before accepting a new hash baseline.

```bash
python3 build/scripts/docs/mark-stale-docs.py --write --summary
python3 build/scripts/docs/mark-stale-docs.py --write --fail-on-stale --summary
```

### render-roadmap-diagrams.py and render-source-diagrams.py

Render deterministic Mermaid diagram sources from roadmap and source registries.

```bash
python3 build/scripts/docs/render-roadmap-diagrams.py --summary
python3 build/scripts/docs/render-source-diagrams.py --summary
```

### add-todos.py (NEW)

Interactive tool to help developers add well-formatted TODO comments to the codebase.

**Features:**

- Interactive prompts for TODO details
- Automatic comment style detection
- GitHub issue integration
- Assignee tagging
- Priority classification
- Template generation

```bash
# Interactive mode (recommended)
python3 add-todos.py --interactive

# Command-line mode
python3 add-todos.py \
  --file src/MyProject/MyFile.cs \
  --description "Implement retry logic" \
  --issue 123 \
  --assignee alice

# Show templates
python3 add-todos.py --template

# Dry run
python3 add-todos.py \
  --file src/MyProject/MyFile.cs \
  --description "Add validation" \
  --dry-run
```

### scan-todos.py (Enhanced)

Scans codebase for TODO/FIXME/HACK/NOTE comments with enhanced tracking.

**New features:**

- Assignee detection via @username
- Age tracking via git history
- Stale item detection (>90 days)
- Unassigned item tracking

```bash
# Full scan with all features
python3 scan-todos.py --output docs/status/TODO.md

# JSON output
python3 scan-todos.py --json-output todo-results.json

# Exclude NOTEs
python3 scan-todos.py --include-notes false
```

### generate-structure-docs.py

Generates repository structure documentation. The tree skips local build/runtime artifacts, caches,
backups, logs, and symlinked entries so generated output stays tied to repository structure rather
than machine-specific working files.

```bash
python3 generate-structure-docs.py --output docs/generated/repository-structure.md
```

### generate-ai-navigation.py

Generates the AI repo-navigation dataset used by docs, MCP tools/resources, and navigation agents.

```bash
python3 generate-ai-navigation.py \
  --json-output docs/ai/generated/repo-navigation.json \
  --markdown-output docs/ai/generated/repo-navigation.md \
  --recent-changes-output docs/ai/generated/recent-changes.md \
  --summary
```

### check-ai-navigation-freshness.py

Fails when `docs/ai/generated/repo-navigation.json` gets older than the allowed threshold.

```bash
python3 check-ai-navigation-freshness.py --max-age-days 14
```

### prompt-route-linter.py

Validates deterministic prompt-routing rules and optionally classifies a prompt into lane, skill,
mode, model route, validation-floor, telemetry, and escalation recommendations. By default it writes
the canonical route artifact to `docs/status/prompt-route-lint-report.json`.

```bash
python3 prompt-route-linter.py --summary
python3 prompt-route-linter.py --prompt "review this PR for regressions"
python3 prompt-route-linter.py --prompt-file prompt.txt --json-output docs/status/prompt-route-lint-report.json
```

### handoff-packet-generator.py

Generates a structured handoff packet from prompt-route-linter output, changed files, validation
entries, route outcome, model route, and telemetry so lane transitions stay deterministic and
compact. By default it writes `docs/status/ai-handoff-packet.md` and
`docs/status/ai-handoff-packet.json`.

```bash
python3 handoff-packet-generator.py --summary --route-json docs/status/prompt-route-lint-report.json
python3 handoff-packet-generator.py \
  --route-json docs/status/prompt-route-lint-report.json \
  --scope "Implement pilot step 2 handoff packet generation" \
  --next-lane "implementation-assurance" \
  --model "gpt-4.1" --input-tokens 1200 --output-tokens 400 --estimated-cost-usd 0.03 --latency-ms 850 \
  --validation "python build/scripts/docs/prompt-route-linter.py --summary::pass"
```

### check-handoff-packet-schema.py

Validates generated handoff packet schema and enforces route-declared telemetry completeness for
high-risk routes (`Deep Review`, `provider`, `governance`).

```bash
python3 check-handoff-packet-schema.py --packet-json docs/status/ai-handoff-packet.json --summary
```

### check-validation-floor.py

CI guard that enforces route-declared validation-floor script evidence for AI/docs-related changes.
It reads `run-docs-automation.py` JSON summary output and fails when required scripts are missing
or not successful.

```bash
python3 check-validation-floor.py --summary-json docs/status/docs-automation-summary.json --summary
python3 check-validation-floor.py --summary-json docs/status/docs-automation-summary.json --route-json docs/status/prompt-route-lint-report.json --summary
```

### check-mode-escalation.py

Enforces route-declared escalation triggers when cross-lane, policy-touching, failed-validation, or
high-risk model-route conditions indicate under-scoped execution.

```bash
python3 check-mode-escalation.py --route-json docs/status/prompt-route-lint-report.json --summary-json docs/status/docs-automation-summary.json --summary
```

### check-ai-routing-parity.py

Checks host documentation references so routing semantics remain consistent across Codex/shared AI
surfaces.

```bash
python3 check-ai-routing-parity.py --summary
```

### update-claude-md.py

Syncs Repository Structure section across AI instruction files.

```bash
python3 update-claude-md.py --claude-md CLAUDE.md
```

## Expansion Scripts

### generate-metrics-dashboard.py (NEW)

Tracks build, test, and workflow execution metrics over time.

**Features:**

- Workflow success/failure rates
- Test execution statistics
- Build timing trends
- Historical performance tracking
- Regression detection

```bash
# Generate metrics for last 30 days
python3 generate-metrics-dashboard.py \
  --output docs/status/metrics-dashboard.md \
  --days 30

# With JSON output
python3 generate-metrics-dashboard.py \
  --json-output metrics.json \
  --summary
```

**Output includes:**

- Overall success rates
- Per-workflow metrics table
- Test pass rates
- Build success rates
- Recommendations for improvement

### validate-api-docs.py (NEW)

Validates that API documentation matches actual endpoint implementations.

**Features:**

- Extracts HTTP endpoints from C# source
- Cross-references with API documentation
- Identifies undocumented endpoints
- Finds deprecated documentation
- Validates HTTP methods match
- Coverage percentage calculation

```bash
# Generate validation report
python3 validate-api-docs.py \
  --output docs/status/api-validation.md

# Check coverage
python3 validate-api-docs.py --summary

# Custom API docs file
python3 validate-api-docs.py \
  --api-docs docs/reference/custom-api.md
```

**Output includes:**

- Documentation coverage percentage
- Undocumented endpoints table
- Deprecated documentation table
- Actionable recommendations

### sync-readme-badges.py (NEW)

Updates README.md badges with current project metrics.

**Features:**

- Version badge from Directory.Build.props
- Test count from test files
- Coverage from reports
- Build status from workflows
- Automatic color coding
- Dry-run mode

```bash
# Preview changes without updating
python3 sync-readme-badges.py --dry-run

# Update badges
python3 sync-readme-badges.py --readme README.md

# Generate report of changes
python3 sync-readme-badges.py \
  --output badge-sync-report.md \
  --summary
```

**Badge types:**

- Version (from Directory.Build.props)
- Tests (count from test files)
- Coverage (from coverage reports)
- Build Status (from GitHub Actions)
- License
- .NET Version

## Enhanced Scripts

### generate-health-dashboard.py

Generates documentation health metrics with scoring.

```bash
python3 generate-health-dashboard.py \
  --output docs/status/doc-health-dashboard.md \
  --json-output health.json
```

### repair-links.py

Detects and optionally auto-fixes broken internal links. The default output path is
`docs/status/link-repair-report.md`; pass `--output` to write to an alternate location such as
[`.artifacts/link-repair-report.md`](../../../.artifacts/link-repair-report.md).

```bash
# Report only
python3 repair-links.py --output link-repair-report.md

# Auto-fix
python3 repair-links.py --auto-fix
```

### validate-examples.py

Validates syntax of code examples in markdown files.

```bash
python3 validate-examples.py --output example-validation.md
```

### check-ai-inventory.py

Detects catalog drift across AI assistant entrypoints, Codex and Claude configuration, Copilot
instructions, `.codex/`, `.claude/`, `.github/`, `docs/ai/`, and MCP prompt/resource/tool surfaces.
Reports use a portable repository identity so generated Markdown or JSON does not record a local
absolute checkout path.

```bash
python3 check-ai-inventory.py --summary
python3 check-ai-inventory.py \
  --output docs/status/ai-inventory-report.md \
  --json-output docs/status/ai-inventory-report.json
```

### validate-agent-definitions.py

Validates every Claude agent definition under `.claude/agents/` (recursively) against the host's
tool vocabulary and frontmatter schema. It exists because a declaration naming tools the host
cannot resolve does not produce a *reduced* grant but an **empty** one — the host refuses to launch
the subagent — and until 2026-08 all thirteen definitions were in exactly that state with no check
opening them.

Per file it verifies that the frontmatter parses with no duplicate keys, that `name` matches the
filename, that `description` is present and non-empty, that every frontmatter key is one the host
supports and carries a value of the right shape, and that every `tools` / `disallowedTools` entry
resolves to a known built-in or a valid MCP pattern.

Both the field allowlist and the permission-mode set are checked against the "Supported frontmatter
fields" table at <https://code.claude.com/docs/en/sub-agents> and pinned by tests that assert a
literal set. That is deliberate: each set *rejects* anything it omits, so an incomplete one blocks
legitimate work, and a test that iterates the constant passes no matter what the constant leaves
out. Re-derive from that table when the host adds a field rather than appending one name at a time.

MCP entries accept `mcp__server` and `mcp__server__tool`, with the tool segment treated as a glob so
`*` may appear anywhere in it — `mcp__github__*`, `mcp__github__get_*`, `mcp__github__*_issue`. The
**server** segment stays glob-free, because an allow rule has to name a specific configured server;
the all-server `mcp__*` is therefore valid only in `disallowedTools`, where it is honoured rather
than skipped with a warning.

An allow-list naming *only* MCP entries is normally an error, since which servers exist is a
property of the host session and the grant would resolve to nothing without them. Declaring
`mcpServers` lifts that: it makes the servers a property of the definition, so `tools: mcp__playwright`
alongside an `mcpServers` entry is a grant that resolves.

Parenthesised scopes such as `Bash(git diff:*)` are supported, and coverage between a deny and an
allow is evaluated as a **glob**, not a prefix test. Wildcards may appear anywhere, so
`Bash(git * main)` and `Bash(* install)` behave as documented rather than being mistaken for exact
commands.

For `Bash` and `PowerShell`, a trailing `:*` is an equivalent spelling of a trailing wildcard —
`Bash(ls:*)` matches what `Bash(ls *)` matches. The permission dialog writes the space-separated
form when you choose "Yes, don't ask again"; `:*` is the alternative suffix, and it is only
recognised at the end, so the colon in `Bash(git:* push)` is literal. Both trailing forms carry the
documented word boundary, which is why `Bash(git:*)` cancels `Bash(git push:*)` but leaves
`Bash(gitfoo:*)` alone — the same rule that makes `Bash(ls *)` match `ls -la` but not `lsof`.

On every other tool `param:value` is a **parameter** match rather than a command prefix, so
`WebFetch(domain:*)` cancels `WebFetch(domain:example.com)` and `Agent(model:*)` cancels
`Agent(model:opus)`. Treating that colon as a command separator would have left those denies
matching nothing. Parameter matching cannot target a tool's primary content field, which is exactly
why the `:*` command alias is confined to the two shell tools.

The checks fail closed by design: a YAML sequence, an empty or punctuation-only value, an
MCP-only allow-list, a misspelled permission key, an unterminated scalar, and a missing or empty
agent directory are all errors rather than silent passes. The validator requires PyYAML at runtime.
Before running the commands below, install it with
`python3 -m pip install --requirement build/scripts/docs/requirements.txt`.

```bash
python3 build/scripts/docs/validate-agent-definitions.py
python3 -m unittest tests/scripts/test_validate_agent_definitions.py
```

Runs in the `verify_docs` lane of `scripts/ci.sh` and in the docs-automation profiles that include
`validate-docs-structure`.

### check-ai-handoff.py

Checks that required host-level AI guidance references shared orchestration guidance:
`docs/ai/agent-handoff-checklist.md`, `docs/ai/parallel-task-manifest-template.md`,
and `docs/ai/work-modes.md`.

```bash
python3 check-ai-handoff.py \
  --output docs/status/ai-handoff-checklist-report.md \
  --json-output docs/status/ai-handoff-checklist-report.json
```

For a second-tier schema gate, pass `--strict` to require all required packet fields in
`## 3) Required Handoff Packet`:

```bash
python3 check-ai-handoff.py --strict
```

Or run the strict automation alias through the docs runner:

```bash
python3 run-docs-automation.py --scripts check-ai-handoff-strict
```

### check-ai-contract-drift.py

Validates that provider mirror policy files stay byte-aligned to the canonical
`docs/ai/contract-policy.json`.

```bash
python3 check-ai-contract-drift.py \
  --canonical docs/ai/contract-policy.json \
  --mirror docs/ai/copilot/contract-policy.mirror.json \
  --mirror docs/ai/claude/contract-policy.mirror.json
```

### generate-coverage.py

Measures documentation coverage of code constructs.

```bash
python3 generate-coverage.py --output coverage-report.md
```

### generate-changelog.py

Generates changelog from git commit history using Conventional Commits.

```bash
# Last 50 commits
python3 generate-changelog.py --output CHANGELOG.md

# Since date
python3 generate-changelog.py --since 2024-01-01 --recent 100
```

### rules-engine.py

Validates documentation against custom rules from YAML config.

```bash
python3 rules-engine.py \
  --rules build/rules/doc-rules.yaml \
  --output rules-report.md
```

### generate-prompts.py

Auto-generates AI assistant prompts from workflow run results.

```bash
python3 generate-prompts.py \
  --workflow test-matrix \
  --run-id 12345 \
  --output .github/prompts/
```

## Quick Reference

### Common Flags

All scripts support these common flags:

| Flag | Description |
| ------ | ----------- |
| `--root`, `-r` | Repository root directory (default: current) |
| `--output`, `-o` | Output file for Markdown report |
| `--json-output`, `-j` | Output file for JSON data |
| `--summary`, `-s` | Print summary to stdout |
| `--help`, `-h` | Show help message |

### Integration with CI/CD

These scripts are integrated into the `.github/workflows/documentation.yml` workflow:

1. **validate-docs** job - Runs `rules-engine.py`, `validate-examples.py`,
   `check-ai-inventory.py`, `check-ai-handoff.py`, and `check-ai-contract-drift.py`
2. **regenerate-docs** job - Runs `run-docs-automation.py --profile core`,
   refreshes Mermaid diagram sources, regenerates WPF UI diagrams, renders PlantUML artifacts,
   and refreshes workflow inventory outputs
3. **Dashboard diff gate** - Compares the current documentation health dashboard against the
   previous committed baseline and only blocks severe regressions when a baseline is available

New scripts can be added by following the patterns in the workflow file.

## Development

### Script Template

New scripts should follow this structure:

```python
#!/usr/bin/env python3
"""
Brief description.

Usage:
    python3 script.py --output report.md
"""

import argparse
import sys
from datetime import datetime, timezone
from pathlib import Path

# Constants
EXCLUDE_DIRS = {'.git', 'bin', 'obj', '__pycache__'}

def main(argv=None):
    parser = argparse.ArgumentParser(description='...')
    parser.add_argument('--output', '-o', type=Path, help='...')
    parser.add_argument('--summary', '-s', action='store_true', help='...')
    args = parser.parse_args(argv)
    
    # Implementation
    
    return 0

if __name__ == '__main__':
    sys.exit(main())
```

### Conventions

**Required:**

- Shebang: `#!/usr/bin/env python3`
- Module docstring with usage examples
- Type hints on functions
- `--output` for file output
- `--summary` for CI summary
- Only stdlib dependencies
- Return 0 on success, 1 on error

**Recommended:**

- `--json-output` for machine-readable output
- Auto-generated notice in markdown output
- Timestamp in output headers
- Graceful encoding error handling
- Logging to stderr, summaries to stdout

### Testing Scripts

```bash
# Test all scripts parse arguments
for script in *.py; do
    python3 "$script" --help > /dev/null && echo "OK: $script" || echo "FAIL: $script"
done

# Test execution
python3 script.py --output /tmp/test.md
cat /tmp/test.md
```

### Adding to Workflow

1. Add script to `build/scripts/docs/`
2. Test locally with `--help` and `--summary`
3. Add or update the relevant step in `.github/workflows/documentation.yml`
4. Update `docs/guides/documentation-automation.md`
5. Update this README

## Contributing

For detailed development guidelines, see:

- `docs/guides/documentation-automation.md` - User guide for the automation system
- `docs/guides/expanding-scripts.md` - Developer guide for adding new scripts

## Support

If you encounter issues with these scripts:

1. Check script help: `python3 script.py --help`
2. Review script docstring for usage examples
3. Check `.github/workflows/documentation.yml` for integration examples
4. See `docs/guides/documentation-automation.md` for troubleshooting

---

_This directory is part of the Meridian documentation automation system._

### Readiness and Coverage Dashboards

The following generators emit canonical JSON first, render Markdown from that payload, and support
`--summary` for one-line CLI diagnostics.

```bash
python3 generate-pilot-readiness-dashboard.py \
  --output docs/status/pilot-readiness-dashboard.md \
  --json-output docs/status/pilot-readiness-dashboard.json \
  --summary

python3 generate-paper-replay-reliability-dashboard.py \
  --output docs/status/paper-replay-reliability-dashboard.md \
  --json-output docs/status/paper-replay-reliability-dashboard.json \
  --summary

python3 generate-evidence-continuity-dashboard.py \
  --output docs/status/evidence-continuity-dashboard.md \
  --json-output docs/status/evidence-continuity-dashboard.json \
  --summary

python3 generate-governance-readiness-dashboard.py \
  --output docs/status/governance-readiness-dashboard.md \
  --json-output docs/status/governance-readiness-dashboard.json \
  --summary

python3 generate-api-contract-coverage-dashboard.py \
  --output docs/status/api-contract-coverage-dashboard.md \
  --json-output docs/status/api-contract-coverage-dashboard.json \
  --summary
```

**Expected outputs:**

- `docs/status/pilot-readiness-dashboard.md` + `.json`
- `docs/status/paper-replay-reliability-dashboard.md` + `.json`
- `docs/status/evidence-continuity-dashboard.md` + `.json`
- `docs/status/governance-readiness-dashboard.md` + `.json`
- `docs/status/api-contract-coverage-dashboard.md` + `.json`

The pilot readiness dashboard derives readiness from the artifact stage-gate details and evidence
graph, including required golden-path stage coverage and self-edge checks. Do not treat top-level
artifact counters as sufficient evidence when the stage details disagree.
