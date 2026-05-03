# Documentation Health Dashboard

_Auto-generated from canonical JSON payload._
_Generated: 2026-05-03T11:26:56.436747+00:00_
Data sources: `repo markdown (*.md)`, `git commit metadata`


> Auto-generated documentation health report. Do not edit manually.
> Last updated: 2026-05-03T11:26:56.430879+00:00

## Overall Health Score

```text
  [############################--] 92/100
  Rating: Excellent
```

## Summary

| Metric | Value |
| -------- | ------- |
| Total documentation files | 426 |
| Total lines | 132,655 |
| Average file size (lines) | 311.4 |
| Orphaned files | 80 |
| Files without headings | 0 |
| Stale files (>90 days) | 0 |
| TODO/FIXME markers | 204 |
| **Health score** | **92/100** |

### Score Breakdown

| Component | Weight | Description |
| ----------- | -------- | ------------- |
| Orphan ratio | 30 pts | Fewer orphaned files is better |
| Heading coverage | 25 pts | All files should have at least one heading |
| Freshness | 20 pts | Files updated within the last 90 days |
| TODO density | 15 pts | Lower density of TODO/FIXME markers |
| Average size | 10 pts | Files averaging at least 20 lines |

## Top Priorities for Improvement

### Orphaned Documentation

These files are not linked from any other Markdown file in the repository:

- `.codex/skills/meridian-brainstorm/references/competitive-landscape.md`
- `.codex/skills/meridian-implementation-assurance/references/documentation-routing.md`
- `.codex/skills/meridian-implementation-assurance/references/evaluation-harness.md`
- `.codex/skills/meridian-provider-builder/references/provider-patterns.md`
- `.codex/skills/meridian-roadmap-strategist/references/roadmap-source-map.md`
- `.codex/skills/meridian-simulated-user-panel/references/artifact-bundles.md`
- `.codex/skills/meridian-simulated-user-panel/references/personas.md`
- `.codex/skills/meridian-simulated-user-panel/references/review-contract.md`
- `.codex/skills/meridian-simulated-user-panel/references/review-modes.md`
- `.codex/skills/meridian-test-writer/references/test-patterns.md`
- `AGENTS.md`
- `Meridian Design System/CONTENT_FUNDAMENTALS.md`
- `Meridian Design System/ICONOGRAPHY.md`
- `Meridian Design System/INSPIRATION_BRIEF.md`
- `Meridian Design System/SKILL.md`
- `Meridian Design System/VISUAL_FOUNDATIONS.md`
- `archive/docs/INDEX.md`
- `archive/docs/assessments/ARTIFACT_ACTIONS_DOWNGRADE.md`
- `archive/docs/assessments/CODE_REVIEW_2026-03-16.md`
- `archive/docs/assessments/CONFIG_CONSOLIDATION_REPORT.md`
- ... and 60 more

## Trend

<!-- Trend data will be appended by CI when historical snapshots are available. -->

| Date | Score | Files | Orphans | Stale |
| ------ | ------- | ------- | --------- | ------- |
| 2026-05-03 | 92 | 426 | 80 | 0 |

---

_This file is auto-generated. Do not edit manually._
