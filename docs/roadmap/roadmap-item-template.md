# Roadmap Item Template

Use this template when adding a roadmap item to `docs/roadmap/data/roadmap-items.yml`.

```yaml
- id: W<wave>-<AREA>-<number>
  title: Plain English outcome
  wave: W<wave>
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
