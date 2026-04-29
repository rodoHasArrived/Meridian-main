# AI Repository Updater Guide

This document explains how an AI agent with shell access should use the `ai-repo-updater.py` script to systematically analyse and improve the Meridian repository.

## Overview

The `build/scripts/ai-repo-updater.py` script is a purpose-built toolkit that gives an AI agent structured, machine-readable insight into the repository's health. It replaces ad-hoc file searching with deterministic auditors that check for known convention violations, documentation gaps, test coverage holes, and CI/CD issues.

**Location:** `build/scripts/ai-repo-updater.py`

## Workflow

Follow this loop when asked to "update", "improve", or "audit" the repository:

```
1. AUDIT   → Discover what needs fixing
2. PLAN    → Prioritise findings by severity
3. FIX     → Implement changes (one category at a time)
4. VERIFY  → Confirm the build and tests still pass
5. REPEAT  → Re-audit until clean or time-boxed
```

### Step 1 — Audit

Run the full audit to get a prioritised improvement plan:

```bash
python3 build/scripts/ai-repo-updater.py audit
```

The output is JSON with three sections:
- **summary** — counts by severity (critical, warning, info, suggestion)
- **improvement_plan** — grouped findings sorted by worst severity
- **findings** — every individual finding with file, line, message, and fix hint

For targeted work, use a sub-auditor:

```bash
python3 build/scripts/ai-repo-updater.py audit-code      # C#/F# conventions
python3 build/scripts/ai-repo-updater.py audit-docs       # Documentation quality
python3 build/scripts/ai-repo-updater.py audit-tests      # Test coverage gaps
python3 build/scripts/ai-repo-updater.py audit-config     # CI/CD and config issues
python3 build/scripts/ai-repo-updater.py audit-providers  # Provider completeness
```

Save results to a file for later reference:

```bash
python3 build/scripts/ai-repo-updater.py audit --json-output /tmp/audit.json
```

### Step 2 — Review Known Errors

Before making changes, check what mistakes prior AI agents have made:

```bash
python3 build/scripts/ai-repo-updater.py known-errors
```

This parses `docs/ai/ai-known-errors.md` and returns structured entries. Cross-reference your planned changes against these entries to avoid repeating known mistakes.

### Step 3 — Fix

Work through findings by category, starting with `critical` severity:

1. Read the finding's `file` and `line`.
2. Apply the `fix_hint`.
3. Mark the category done before moving to the next.

**Rules while fixing:**
- Follow all conventions in `CLAUDE.md` (sealed classes, CancellationToken, structured logging, etc.).
- Never introduce Version attributes on PackageReference (Central Package Management).
- If you fix an AI-caused regression, add an entry to `docs/ai/ai-known-errors.md`.

### Step 4 — Verify

After each batch of fixes, confirm nothing is broken:

```bash
python3 build/scripts/ai-repo-updater.py verify
```

This runs `dotnet build`, `dotnet test`, and `dotnet format --verify-no-changes`. The output is JSON with pass/fail per step and `overall_ok: true|false`.

If verify fails, fix the issue before continuing.

### Step 5 — Generate Report

Create a human-readable markdown report:

```bash
python3 build/scripts/ai-repo-updater.py report --output docs/generated/improvement-report.md
```

### Step 6 — Summarise Changes

Before committing, generate a diff summary:

```bash
python3 build/scripts/ai-repo-updater.py diff-summary
```

Use the output to draft a clear commit message describing what changed and why.

## Commands Reference

| Command | Purpose | Output |
|---------|---------|--------|
| `audit` | Full repository audit (all analysers) | JSON with findings + plan |
| `audit-code` | C#/F# convention violations | JSON |
| `audit-docs` | Documentation quality analysis | JSON |
| `audit-tests` | Test coverage gap detection | JSON |
| `audit-config` | CI/CD and configuration issues | JSON |
| `audit-providers` | Provider implementation completeness | JSON |
| `verify` | Build + test + lint validation | JSON with pass/fail |
| `report` | Generate markdown improvement report | Markdown file |
| `known-errors` | Load known AI error entries | JSON |
| `diff-summary` | Summarise uncommitted git changes | JSON |

## Common Flags

| Flag | Short | Purpose |
|------|-------|---------|
| `--root PATH` | `-r` | Override repository root |
| `--output PATH` | `-o` | Write markdown output |
| `--json-output PATH` | `-j` | Write JSON output |
| `--summary` | `-s` | Print summary to stdout |

## What Each Auditor Checks

### Code Auditor (`audit-code`)
- Async methods missing `CancellationToken` parameter
- String interpolation in logger calls (should use structured parameters)
- Direct `new HttpClient()` instantiation (should use `IHttpClientFactory`)
- Blocking async with `.Result` or `.Wait()` (deadlock risk)
- `Task.Run` used for I/O-bound operations
- Public classes not marked `sealed`

### Documentation Auditor (`audit-docs`)
- Broken internal markdown links
- Stub documentation files (fewer than 3 content lines)
- Outdated "Last Updated" timestamps
- ADR files missing required sections (Status, Context, Decision, Consequences)

### Test Auditor (`audit-tests`)
- Important classes (Services, Providers, Clients, etc.) without corresponding test classes

### Configuration Auditor (`audit-config`)
- Hardcoded secrets in workflow files
- Deprecated GitHub Action versions
- `PackageReference` with `Version=` attribute (CPM violation)

### Provider Auditor (`audit-providers`)
- Provider classes missing `[ImplementsAdr]` attribute
- Provider classes missing `[DataSource]` attribute

## Example Session

```bash
# 1. Start with a full audit
python3 build/scripts/ai-repo-updater.py audit --json-output /tmp/audit.json

# 2. Check known errors to avoid
python3 build/scripts/ai-repo-updater.py known-errors

# 3. Fix critical findings first, then warnings
# ... (make edits based on findings) ...

# 4. Verify changes
python3 build/scripts/ai-repo-updater.py verify

# 5. Re-audit to confirm improvements
python3 build/scripts/ai-repo-updater.py audit-code --summary

# 6. Generate report and summarise
python3 build/scripts/ai-repo-updater.py report --output docs/generated/improvement-report.md
python3 build/scripts/ai-repo-updater.py diff-summary
```

## Integration with Makefile

The script is available via make targets:

```bash
make ai-audit            # Full audit
make ai-audit-code       # Code conventions only
make ai-audit-docs       # Documentation only
make ai-verify           # Build + test + lint
make ai-report           # Generate improvement report
```

## Related Resources

- **Master AI index:** [`docs/ai/README.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/docs/ai/README.md)
- **Root context:** [`CLAUDE.md`](https://github.com/rodoHasArrived/Meridian-main/blob/main/CLAUDE.md) § AI Repository Updater
- **Error prevention:** [`docs/ai/ai-known-errors.md`](../ai-known-errors.md)

---

*Last Updated: 2026-03-16*
