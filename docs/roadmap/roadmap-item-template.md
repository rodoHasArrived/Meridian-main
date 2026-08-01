# Roadmap Item Template

Use this template when adding a roadmap item to `docs/roadmap/data/roadmap-items.yml`.

```yaml
- id: W<wave>-<AREA>-<number>
  title: Plain English outcome
  wave: W<wave>
  sequence: 1  # optional; rank within the wave for generated views
  stage_gates:
    - StageGateId
  workspace:
    - Trading
  owner_lane: Owning lane
  status: planned
  health: green
  priority: medium
  evidence_posture: planned_evidence
  current_summary: One sentence explaining why this matters now.
  exit_criteria:
    - Observable acceptance criterion.
  source_modules:
    - SRC-MODULE-ID
  diagrams:
    - DIA-DIAGRAM-ID
  last_reviewed: YYYY-MM-DD
```

Checklist items should name implementation evidence, validation commands, and user value. Avoid generic "finish feature" wording.

## Ordering within a wave

Generated views order a wave by `sequence` when items declare it, and otherwise by the identifier's
trailing number. Set `sequence` whenever a wave's adopted order is not the order its identifiers
imply — for example a wave that numbers per area, where every row ends in `001` and the trailing
number carries no rank. Adding it to any item in a wave is a minor schema change; see
[schema-versioning.md](schema-versioning.md).
