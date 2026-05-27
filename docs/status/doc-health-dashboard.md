# Documentation Health Dashboard

_Auto-generated from canonical JSON payload._
_Generated: 2026-05-21T03:18:07.772234+00:00_
Data sources: `repo markdown (*.md)`, `file modification metadata`


> Auto-generated documentation health report. Do not edit manually.
> Last updated: 2026-05-21T03:18:07.753616+00:00

## Overall Health Score

```text
  [###########################---] 89/100
  Rating: Good
```

## Summary

| Metric | Value |
| -------- | ------- |
| Total documentation files | 409 |
| Total lines | 100,073 |
| Average file size (lines) | 244.7 |
| Orphaned files | 86 |
| Files without headings | 0 |
| Stale files (>90 days) | 0 |
| TODO/FIXME markers | 285 |
| **Health score** | **89/100** |

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

- `AGENTS.md`
- `Meridian Design System/BRAND_GUIDELINES.md`
- `Meridian Design System/CONTENT_FUNDAMENTALS.md`
- `Meridian Design System/ICONOGRAPHY.md`
- `Meridian Design System/INSPIRATION_BRIEF.md`
- `Meridian Design System/SKILL.md`
- `Meridian Design System/VISUAL_FOUNDATIONS.md`
- `benchmarks/BOTTLENECK_REPORT.md`
- `docs/ai/claude/CLAUDE.api.md`
- `docs/ai/claude/CLAUDE.roadmap-learning-log.md`
- `docs/ai/claude/CLAUDE.structure.md`
- `docs/ai/copilot/ai-sync-workflow.md`
- `docs/architecture/workstation-continuity-payload-profile.md`
- `docs/audits/CODE_REVIEW_2026-03-16.md`
- `docs/audits/workspace-visual-audit-checklist-2026-04-22.md`
- `docs/development/fund-account-traversal.md`
- `docs/development/policies/promotion-policy-matrix.md`
- `docs/development/score-reason-taxonomy.md`
- `docs/development/synthetic-provider-test-harness.md`
- `docs/docfx/api/index.md`
- ... and 66 more

## Trend

<!-- Trend data will be appended by CI when historical snapshots are available. -->

| Date | Score | Files | Orphans | Stale |
| ------ | ------- | ------- | --------- | ------- |
| 2026-05-21 | 89 | 409 | 86 | 0 |

---

_This file is auto-generated. Do not edit manually._
