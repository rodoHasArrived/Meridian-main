# Documentation Health Dashboard

_Auto-generated from canonical JSON payload._
_Generated: 1970-01-01T00:00:00+00:00_
Data sources: `repo markdown (*.md)`, `file modification metadata`


> Auto-generated documentation health report. Do not edit manually.
> Last updated: 1970-01-01T00:00:00+00:00

## Overall Health Score

```text
  [#########################-----] 83/100
  Rating: Good
```

## Summary

| Metric | Value |
| -------- | ------- |
| Total documentation files | 540 |
| Total lines | 89,467 |
| Average file size (lines) | 165.7 |
| Orphaned files | 206 |
| Files without headings | 38 |
| Stale files (>90 days) | 0 |
| TODO/FIXME markers | 200 |
| **Health score** | **83/100** |

### Score Breakdown

| Component | Weight | Description |
| ----------- | -------- | ------------- |
| Orphan ratio | 30 pts | Fewer orphaned files is better |
| Heading coverage | 25 pts | All files should have at least one heading |
| Freshness | 20 pts | Files updated within the last 90 days |
| TODO density | 15 pts | Lower density of TODO/FIXME markers |
| Average size | 10 pts | Files averaging at least 20 lines |

## Top Priorities for Improvement

### Files Without Headings

These files lack a Markdown heading, making them harder to navigate:

- `Meridian Design System/SKILL.md`
- `Meridian Design System/components/charts/CandleChart.prompt.md`
- `Meridian Design System/components/charts/ChartCard.prompt.md`
- `Meridian Design System/components/charts/EquityCurve.prompt.md`
- `Meridian Design System/components/core/Badge.prompt.md`
- `Meridian Design System/components/core/Button.prompt.md`
- `Meridian Design System/components/core/Eyebrow.prompt.md`
- `Meridian Design System/components/core/Input.prompt.md`
- `Meridian Design System/components/core/PanelSurface.prompt.md`
- `Meridian Design System/components/core/StatusBanner.prompt.md`
- `Meridian Design System/components/data/DenseDataTable.prompt.md`
- `Meridian Design System/components/data/EntitySummary.prompt.md`
- `Meridian Design System/components/data/KeyValueGrid.prompt.md`
- `Meridian Design System/components/data/MetricCard.prompt.md`
- `Meridian Design System/components/shell/NavRail.prompt.md`
- ... and 23 more

### Orphaned Documentation

These files are not linked from any other Markdown file in the repository:

- `.agents/skills/meridian-archive-organizer/SKILL.md`
- `.agents/skills/meridian-blueprint/SKILL.md`
- `.agents/skills/meridian-brainstorm/SKILL.md`
- `.agents/skills/meridian-browser-workstation/SKILL.md`
- `.agents/skills/meridian-cleanup/SKILL.md`
- `.agents/skills/meridian-docs/SKILL.md`
- `.agents/skills/meridian-implementation-assurance/SKILL.md`
- `.agents/skills/meridian-provider-builder/SKILL.md`
- `.agents/skills/meridian-repo-navigation/SKILL.md`
- `.agents/skills/meridian-roadmap-strategist/SKILL.md`
- `.agents/skills/meridian-simulated-user-panel/SKILL.md`
- `.agents/skills/meridian-test-writer/SKILL.md`
- `AGENTS.md`
- `Meridian Design System/CONTENT_FUNDAMENTALS.md`
- `Meridian Design System/ICONOGRAPHY.md`
- `Meridian Design System/INSPIRATION_BRIEF.md`
- `Meridian Design System/SKILL.md`
- `Meridian Design System/VISUAL_FOUNDATIONS.md`
- `Meridian Design System/components/charts/CandleChart.prompt.md`
- `Meridian Design System/components/charts/ChartCard.prompt.md`
- ... and 186 more

## Trend

<!-- Trend data will be appended by CI when historical snapshots are available. -->

| Date | Score | Files | Orphans | Stale |
| ------ | ------- | ------- | --------- | ------- |
| 1970-01-01 | 83 | 540 | 206 | 0 |

---

_This file is auto-generated. Do not edit manually._
