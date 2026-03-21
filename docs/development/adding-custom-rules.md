# Adding Custom Documentation Rules

> Guide for creating and maintaining documentation validation rules.

## Overview

The documentation rules engine (`build/scripts/docs/rules-engine.py`) enforces project-specific conventions through a YAML configuration file at `build/rules/doc-rules.yaml`.

## Rule Structure

Each rule has the following fields:

```yaml
rules:
  - name: "Human-readable rule name"
    applies_to: "glob/pattern/*.md"
    severity: error|warning|info
    # One of the following check types:
    must_contain: "required text or regex"
    must_not_contain: "forbidden text or regex"
    must_match: "regex pattern"
    required_sections:
      - "Section Heading 1"
      - "Section Heading 2"
```

## Fields Reference

| Field | Required | Description |
|-------|----------|-------------|
| `name` | Yes | Descriptive name shown in reports |
| `applies_to` | Yes | Glob pattern for target files (relative to repo root) |
| `severity` | Yes | `error`, `warning`, or `info` |
| `must_contain` | No | String/regex that must appear in the file |
| `must_not_contain` | No | String/regex that must NOT appear in the file |
| `must_match` | No | Regex that at least one line must match |
| `required_sections` | No | List of markdown heading texts that must exist |

Only one check type per rule. To apply multiple checks to the same files, create multiple rules.

## Severity Levels

| Level | CI Impact | Use For |
|-------|-----------|---------|
| `error` | Fail validation | Missing required content, broken standards |
| `warning` | Report only | Best practice violations, style issues |
| `info` | Report only | Suggestions, minor improvements |

## Examples

### Ensure ADRs have required metadata

```yaml
- name: "ADR has status field"
  applies_to: "docs/adr/*.md"
  must_contain: "Status:"
  severity: error
```

### Prevent hardcoded URLs in docs

```yaml
- name: "No hardcoded localhost URLs"
  applies_to: "docs/**/*.md"
  must_not_contain: "http://localhost"
  severity: info
```

### Require specific sections in setup guides

```yaml
- name: "Provider setup has prerequisites"
  applies_to: "docs/providers/*-setup.md"
  required_sections:
    - "Prerequisites"
    - "Configuration"
    - "Troubleshooting"
  severity: warning
```

### Ensure generated files have notice

```yaml
- name: "Generated docs marked as auto-generated"
  applies_to: "docs/generated/*.md"
  must_contain: "auto-generated"
  severity: warning
```

## Running Locally

```bash
# Run with default rules
python3 build/scripts/docs/rules-engine.py \
  --rules build/rules/doc-rules.yaml \
  --output docs/status/rules-report.md

# Print summary to stdout
python3 build/scripts/docs/rules-engine.py \
  --rules build/rules/doc-rules.yaml \
  --summary
```

## Adding New Rules

1. Edit `build/rules/doc-rules.yaml`
2. Add your rule following the structure above
3. Test locally: `python3 build/scripts/docs/rules-engine.py --rules build/rules/doc-rules.yaml --summary`
4. Commit the updated rules file
5. The workflow will enforce the new rule on the next run

## Glob Pattern Tips

| Pattern | Matches |
|---------|---------|
| `docs/**/*.md` | All markdown files under docs/ (recursive) |
| `docs/adr/*.md` | Only direct children of docs/adr/ |
| `*.md` | Root-level markdown files only |
| `README.md` | Only the root README |
| `src/**/*.cs` | All C# source files |

---

*This guide is part of the documentation automation system.*
