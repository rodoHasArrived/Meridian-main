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
| Total documentation files | 1762 |
| Total lines | 264,758 |
| Average file size (lines) | 150.3 |
| Orphaned files | 706 |
| Files without headings | 97 |
| Stale files (>90 days) | 0 |
| TODO/FIXME markers | 681 |
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

- `.tmp/meridian-design-system-attachment-bb32f6f3f418450f8fae4f8c63223d4b/SKILL.md`
- `.tmp/meridian-design-system-attachment-bb32f6f3f418450f8fae4f8c63223d4b/components/charts/CandleChart.prompt.md`
- `.tmp/meridian-design-system-attachment-bb32f6f3f418450f8fae4f8c63223d4b/components/charts/ChartCard.prompt.md`
- `.tmp/meridian-design-system-attachment-bb32f6f3f418450f8fae4f8c63223d4b/components/charts/EquityCurve.prompt.md`
- `.tmp/meridian-design-system-attachment-bb32f6f3f418450f8fae4f8c63223d4b/components/core/Badge.prompt.md`
- `.tmp/meridian-design-system-attachment-bb32f6f3f418450f8fae4f8c63223d4b/components/core/Button.prompt.md`
- `.tmp/meridian-design-system-attachment-bb32f6f3f418450f8fae4f8c63223d4b/components/core/Eyebrow.prompt.md`
- `.tmp/meridian-design-system-attachment-bb32f6f3f418450f8fae4f8c63223d4b/components/core/Input.prompt.md`
- `.tmp/meridian-design-system-attachment-bb32f6f3f418450f8fae4f8c63223d4b/components/core/PanelSurface.prompt.md`
- `.tmp/meridian-design-system-attachment-bb32f6f3f418450f8fae4f8c63223d4b/components/core/StatusBanner.prompt.md`
- `.tmp/meridian-design-system-attachment-bb32f6f3f418450f8fae4f8c63223d4b/components/data/DenseDataTable.prompt.md`
- `.tmp/meridian-design-system-attachment-bb32f6f3f418450f8fae4f8c63223d4b/components/data/EntitySummary.prompt.md`
- `.tmp/meridian-design-system-attachment-bb32f6f3f418450f8fae4f8c63223d4b/components/data/KeyValueGrid.prompt.md`
- `.tmp/meridian-design-system-attachment-bb32f6f3f418450f8fae4f8c63223d4b/components/data/MetricCard.prompt.md`
- `.tmp/meridian-design-system-attachment-bb32f6f3f418450f8fae4f8c63223d4b/components/shell/NavRail.prompt.md`
- ... and 82 more

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
- `.tmp/gh-run-28153181504/wpf-dev-validation-469/20260625-070953/wpf-dev-test-validation.md`
- `.tmp/gh-run-28268782582/windows-desktop-validation-1499/20260626-222901/wpf-dev-test-validation.md`
- `.tmp/meridian-design-system-attachment-bb32f6f3f418450f8fae4f8c63223d4b/SKILL.md`
- `.tmp/meridian-design-system-attachment-bb32f6f3f418450f8fae4f8c63223d4b/components/charts/CandleChart.prompt.md`
- `.tmp/meridian-design-system-attachment-bb32f6f3f418450f8fae4f8c63223d4b/components/charts/ChartCard.prompt.md`
- `.tmp/meridian-design-system-attachment-bb32f6f3f418450f8fae4f8c63223d4b/components/charts/EquityCurve.prompt.md`
- `.tmp/meridian-design-system-attachment-bb32f6f3f418450f8fae4f8c63223d4b/components/core/Badge.prompt.md`
- `.tmp/meridian-design-system-attachment-bb32f6f3f418450f8fae4f8c63223d4b/components/core/Button.prompt.md`
- ... and 686 more

## Trend

<!-- Trend data will be appended by CI when historical snapshots are available. -->

| Date | Score | Files | Orphans | Stale |
| ------ | ------- | ------- | --------- | ------- |
| 1970-01-01 | 83 | 1762 | 706 | 0 |

---

_This file is auto-generated. Do not edit manually._
