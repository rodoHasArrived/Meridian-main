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

Every generated roadmap view — the Mermaid diagram, `ROADMAP_SUMMARY.md`, and `roadmap-register.md`
— shares one ordering: wave first, then rank within the wave. Rank comes from `sequence` when an
item declares it, and otherwise from the identifier's trailing number.

Set `sequence` whenever a wave's adopted order is not the order its identifiers imply — for example
a wave that numbers per area, where every row ends in `001` and the trailing number carries no rank.
Setting it on a row is an ordinary data update. `sequence` must be an integer of 1 or greater;
validation rejects anything else rather than letting a generated view order itself against the
adopted rank.

Introducing the optional field was a one-time minor schema change, so the registry now declares
`meridian.roadmap-items@1.1.0` whether or not any row sets `sequence` — the contract changed the
moment the property became accepted. Setting it on a row afterwards is an ordinary data update and
bumps nothing. See [schema-versioning.md](schema-versioning.md).
