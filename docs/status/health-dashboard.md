# Documentation Health Dashboard

_Auto-generated from canonical JSON payload._
_Generated: 2026-04-30T05:51:06.190920+00:00_
Data sources: `repo markdown (*.md)`, `git commit metadata`


> Auto-generated documentation health report. Do not edit manually.
> Last updated: 2026-04-30T05:51:06.186222+00:00

## Overall Health Score

```text
  [###########################---] 90/100
  Rating: Excellent
```

## Summary

| Metric | Value |
| -------- | ------- |
| Total documentation files | 419 |
| Total lines | 139,307 |
| Average file size (lines) | 332.5 |
| Orphaned files | 114 |
| Files without headings | 0 |
| Stale files (>90 days) | 0 |
| TODO/FIXME markers | 217 |
| **Health score** | **90/100** |

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

- `.artifacts/link-repair-report.md`
- `.claude/agents/meridian-archive-organizer.md`
- `.claude/agents/meridian-blueprint.md`
- `.claude/agents/meridian-cleanup.md`
- `.claude/agents/meridian-docs.md`
- `.claude/agents/meridian-navigation.md`
- `.claude/agents/meridian-repo-navigation.md`
- `.claude/agents/meridian-roadmap-strategist.md`
- `.claude/skills/meridian-archive-organizer/fixtures/superseded-adr/docs/generated/repository-structure.md`
- `.claude/skills/meridian-archive-organizer/references/archive-placement-guide.md`
- `.claude/skills/meridian-archive-organizer/references/evaluation-harness.md`
- `.claude/skills/meridian-blueprint/references/blueprint-patterns.md`
- `.claude/skills/meridian-blueprint/references/pipeline-position.md`
- `.claude/skills/meridian-brainstorm/SKILL.md`
- `.claude/skills/meridian-brainstorm/references/competitive-landscape.md`
- `.claude/skills/meridian-brainstorm/references/idea-dimensions.md`
- `.claude/skills/meridian-code-review/agents/grader.md`
- `.claude/skills/meridian-code-review/references/architecture.md`
- `.claude/skills/meridian-code-review/references/schemas.md`
- `.claude/skills/meridian-implementation-assurance/references/documentation-routing.md`
- ... and 94 more

## Trend

<!-- Trend data will be appended by CI when historical snapshots are available. -->

| Date | Score | Files | Orphans | Stale |
| ------ | ------- | ------- | --------- | ------- |
| 2026-04-30 | 90 | 419 | 114 | 0 |

---

_This file is auto-generated. Do not edit manually._
