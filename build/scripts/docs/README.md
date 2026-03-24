# Documentation Automation Scripts

This directory contains Python scripts for automating documentation tasks in the Meridian project.

## Table of Contents

- [Core Scripts](#core-scripts)
- [Expansion Scripts](#expansion-scripts)
- [Enhanced Scripts](#enhanced-scripts)
- [Quick Reference](#quick-reference)
- [Development](#development)

## Core Scripts

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

Generates repository structure documentation.

```bash
python3 generate-structure-docs.py --output docs/generated/repository-structure.md
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
  --output docs/status/health-dashboard.md \
  --json-output health.json
```

### repair-links.py

Detects and optionally auto-fixes broken internal links.

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
|------|-------------|
| `--root`, `-r` | Repository root directory (default: current) |
| `--output`, `-o` | Output file for Markdown report |
| `--json-output`, `-j` | Output file for JSON data |
| `--summary`, `-s` | Print summary to stdout |
| `--help`, `-h` | Show help message |

### Integration with CI/CD

These scripts are integrated into the `.github/workflows/documentation.yml` workflow:

1. **validate-docs** job - Runs rules-engine.py
2. **regenerate-docs** job - Runs generate-structure-docs.py and update-claude-md.py
3. **scan-todos** job - Runs scan-todos.py
4. **link-repair** job - Runs repair-links.py
5. **coverage-report** job - Runs generate-coverage.py
6. **validate-examples** job - Runs validate-examples.py
7. **generate-changelog** job - Runs generate-changelog.py

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
3. Add job to `.github/workflows/documentation.yml`
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

*This directory is part of the Meridian documentation automation system.*
