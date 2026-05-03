# Documentation Health Dashboard

_Auto-generated from canonical JSON payload._
_Generated: 2026-05-03T08:31:49.995088+00:00_
Data sources: `repo markdown (*.md)`, `git commit metadata`


> Auto-generated documentation health report. Do not edit manually.
> Last updated: 2026-05-03T08:31:49.989100+00:00

## Overall Health Score

```text
  [###########################---] 91/100
  Rating: Excellent
```

## Summary

| Metric | Value |
| -------- | ------- |
| Total documentation files | 426 |
| Total lines | 132,627 |
| Average file size (lines) | 311.3 |
| Orphaned files | 99 |
| Files without headings | 0 |
| Stale files (>90 days) | 0 |
| TODO/FIXME markers | 204 |
| **Health score** | **91/100** |

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
- `.claude/skills/meridian-implementation-assurance/references/evaluation-harness.md`
- `.claude/skills/meridian-roadmap-strategist/references/roadmap-source-map.md`
- `.claude/skills/meridian-simulated-user-panel/agents/grader.md`
- `.claude/skills/meridian-simulated-user-panel/evals/golden/eval-01-welcome-onboarding-design-partner.md`
- `.claude/skills/meridian-simulated-user-panel/evals/golden/eval-02-provider-onboarding-release-gate.md`
- `.claude/skills/meridian-simulated-user-panel/evals/golden/eval-03-fund-ledger-controls-review.md`
- `.claude/skills/meridian-simulated-user-panel/evals/golden/eval-04-analysis-export-power-user-review.md`
- `.claude/skills/meridian-simulated-user-panel/evals/golden/eval-05-research-promotion-roadmap-review.md`
- `.claude/skills/meridian-simulated-user-panel/evals/golden/eval-06-provider-health-usability-lab.md`
- `.claude/skills/meridian-simulated-user-panel/references/artifact-bundles.md`
- `.claude/skills/meridian-simulated-user-panel/references/personas.md`
- `.claude/skills/meridian-simulated-user-panel/references/review-contract.md`
- `.claude/skills/meridian-simulated-user-panel/references/review-modes.md`
- `.claude/skills/meridian-simulated-user-panel/references/sample-prompts.md`
- `.codex/skills/meridian-archive-organizer/fixtures/superseded-adr/docs/generated/repository-structure.md`
- `.codex/skills/meridian-archive-organizer/references/archive-placement-guide.md`
- `.codex/skills/meridian-archive-organizer/references/evaluation-harness.md`
- `.codex/skills/meridian-blueprint/references/blueprint-patterns.md`
- `.codex/skills/meridian-brainstorm/references/competitive-landscape.md`
- ... and 79 more

## Trend

<!-- Trend data will be appended by CI when historical snapshots are available. -->

| Date | Score | Files | Orphans | Stale |
| ------ | ------- | ------- | --------- | ------- |
| 2026-05-03 | 91 | 426 | 99 | 0 |

---

_This file is auto-generated. Do not edit manually._
