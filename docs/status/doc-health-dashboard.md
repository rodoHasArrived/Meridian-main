# Documentation Health Dashboard

_Auto-generated from canonical JSON payload._
_Generated: 1970-01-01T00:00:00+00:00_
Data sources: `repo markdown (*.md)`, `file modification metadata`


> Auto-generated documentation health report. Do not edit manually.
> Last updated: 1970-01-01T00:00:00+00:00

## Overall Health Score

```text
  [########################------] 81/100
  Rating: Good
```

## Summary

| Metric | Value |
| -------- | ------- |
| Total documentation files | 1858 |
| Total lines | 290,976 |
| Average file size (lines) | 156.6 |
| Orphaned files | 760 |
| Files without headings | 207 |
| Stale files (>90 days) | 0 |
| TODO/FIXME markers | 689 |
| **Health score** | **81/100** |

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
- ... and 192 more

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
- `.agents/skills/meridian-simulated-user-panel/agents/grader.md`
- `.agents/skills/meridian-simulated-user-panel/evals/golden/eval-01-welcome-onboarding-design-partner.md`
- `.agents/skills/meridian-simulated-user-panel/evals/golden/eval-02-provider-onboarding-release-gate.md`
- `.agents/skills/meridian-simulated-user-panel/evals/golden/eval-03-fund-ledger-controls-review.md`
- `.agents/skills/meridian-simulated-user-panel/evals/golden/eval-04-analysis-export-power-user-review.md`
- `.agents/skills/meridian-simulated-user-panel/evals/golden/eval-05-research-promotion-roadmap-review.md`
- `.agents/skills/meridian-simulated-user-panel/evals/golden/eval-06-provider-health-usability-lab.md`
- `.agents/skills/meridian-simulated-user-panel/references/artifact-bundles.md`
- `.agents/skills/meridian-simulated-user-panel/references/personas.md`
- ... and 740 more

## Trend

<!-- Trend data will be appended by CI when historical snapshots are available. -->

| Date | Score | Files | Orphans | Stale |
| ------ | ------- | ------- | --------- | ------- |
| 1970-01-01 | 81 | 1858 | 760 | 0 |

---

_This file is auto-generated. Do not edit manually._
