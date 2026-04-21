# Documentation Health Dashboard

> Auto-generated documentation health report. Do not edit manually.
> Last updated: 2026-04-21T17:14:29.280065+00:00

## Overall Health Score

```
  [##########################----] 88/100
  Rating: Good
```

## Summary

| Metric | Value |
|--------|-------|
| Total documentation files | 401 |
| Total lines | 140,052 |
| Average file size (lines) | 349.3 |
| Orphaned files | 128 |
| Files without headings | 0 |
| Stale files (>90 days) | 0 |
| TODO/FIXME markers | 185 |
| **Health score** | **88/100** |

### Score Breakdown

| Component | Weight | Description |
|-----------|--------|-------------|
| Orphan ratio | 30 pts | Fewer orphaned files is better |
| Heading coverage | 25 pts | All files should have at least one heading |
| Freshness | 20 pts | Files updated within the last 90 days |
| TODO density | 15 pts | Lower density of TODO/FIXME markers |
| Average size | 10 pts | Files averaging at least 20 lines |

## Top Priorities for Improvement

### Orphaned Documentation

These files are not linked from any other Markdown file in the repository:

- `.artifacts/link-repair-report.md`
- `.claude/agents/meridian-blueprint.md`
- `.claude/agents/meridian-cleanup.md`
- `.claude/agents/meridian-docs.md`
- `.claude/agents/meridian-navigation.md`
- `.claude/skills/meridian-blueprint/references/blueprint-patterns.md`
- `.claude/skills/meridian-blueprint/references/pipeline-position.md`
- `.claude/skills/meridian-brainstorm/SKILL.md`
- `.claude/skills/meridian-brainstorm/references/competitive-landscape.md`
- `.claude/skills/meridian-brainstorm/references/idea-dimensions.md`
- `.claude/skills/meridian-code-review/agents/grader.md`
- `.claude/skills/meridian-code-review/references/architecture.md`
- `.claude/skills/meridian-code-review/references/schemas.md`
- `.claude/skills/meridian-implementation-assurance/references/documentation-routing.md`
- `.claude/skills/meridian-implementation-assurance/references/evaluation-harness.md`
- `.claude/skills/meridian-provider-builder/SKILL.md`
- `.claude/skills/meridian-simulated-user-panel/SKILL.md`
- `.claude/skills/meridian-simulated-user-panel/agents/grader.md`
- `.claude/skills/meridian-simulated-user-panel/evals/golden/eval-01-welcome-onboarding-design-partner.md`
- `.claude/skills/meridian-simulated-user-panel/evals/golden/eval-02-provider-onboarding-release-gate.md`
- ... and 108 more

## Trend

<!-- Trend data will be appended by CI when historical snapshots are available. -->

| Date | Score | Files | Orphans | Stale |
|------|-------|-------|---------|-------|
| 2026-04-21 | 88 | 401 | 128 | 0 |

---

*This file is auto-generated. Do not edit manually.*
