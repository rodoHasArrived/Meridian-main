# Roadmap Governance

## Phase scope markers

Roadmap/source-doc pull requests must declare a phase (`PR0`…`PR9`) through one of these channels:

1. **Workflow dispatch input** (`phase`) when manually running `roadmap-source-docs.yml`.
2. **PR label** formatted as `phase:PRx` (example: `phase:PR3`).
3. **PR body marker** comment: `<!-- phase:PRx -->`.

The CI scope gate (`tools/roadmap/enforce_phase_scope.py`) resolves declarations in this order:

1. Explicit `--phase` (internal/manual override)
2. Workflow dispatch input
3. PR labels
4. PR body marker

If no declaration is found, CI fails with:

```text
No phase declaration found. Provide --phase/--dispatch-phase, a 'phase:PRx' label, or a PR body marker like '<!-- phase:PR2 -->'.
```

## Examples

### PR body marker

```markdown
## Summary

Adds roadmap schema notes for the next planning slice.

<!-- phase:PR1 -->
```

### Label marker

Apply a label such as:

```text
phase:PR2
```

### Workflow dispatch

When triggering **Roadmap Source Docs** manually, set input:

```text
phase=PR4
```

## Failure output

When files fall outside declared phase scope, CI emits a diff-style report:

```text
::error::Phase scope gate failed for PR1 (source: labels).
--- allowed-patterns
+++ docs/**
+++ tools/roadmap/**
+++ .github/workflows/roadmap-source-docs.yml
--- violating-files
- src/Meridian.Application/Commands/RunbookCommands.cs

Hint: widen phase marker only when roadmap governance allows it.
```

Use the report to either:

- Move out-of-scope changes into a higher-phase PR, or
- Re-declare the PR with an approved higher phase marker.

## Schema evolution policy

Roadmap registry/rendering validators treat unknown top-level fields as errors and enforce explicit required structural fields.

- **Major version change (`vN` -> `vN+1`)**: required when renaming/removing required fields, tightening enum value sets, or changing field semantics in a way that breaks existing items.
- **Minor-compatible change (same major file)**: additive optional fields are allowed only through controlled extension objects (`extensions` with `x-*` keys) or explicitly added optional schema properties.
- **Migration trigger**: if validator output introduces new "missing required field" or "unexpected field" failures on existing data, publish a migration update in the same PR (data update and changelog/governance note) before enabling the stricter rule in automation.
- **Renderer/registry contract**: IDs, ownership, evidence posture, exit criteria, links, and timestamps are treated as structural and must remain explicitly modeled in schema-required fields for deterministic rendering and tracking.
