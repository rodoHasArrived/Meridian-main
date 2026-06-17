# Documentation Health Dashboard

_Auto-generated from canonical JSON payload._
_Generated: 2026-06-17T01:13:05.377622+00:00_
Data sources: `repo markdown (*.md)`, `file modification metadata`


> Auto-generated documentation health report. Do not edit manually.
> Last updated: 2026-06-17T01:13:05.350614+00:00

## Overall Health Score

```text
  [#########################-----] 83/100
  Rating: Good
```

## Summary

| Metric | Value |
| -------- | ------- |
| Total documentation files | 600 |
| Total lines | 86,402 |
| Average file size (lines) | 144.0 |
| Orphaned files | 241 |
| Files without headings | 21 |
| Stale files (>90 days) | 0 |
| TODO/FIXME markers | 238 |
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

- `benchmarks/results/20260521_111257/batchserialization/results/Meridian.Benchmarks.BatchSerializationBenchmarks-report-github.md`
- `benchmarks/results/20260521_111257/canonicalizing/results/Meridian.Benchmarks.CanonicalizingPublisherBenchmarks-report-github.md`
- `benchmarks/results/20260521_111257/composite/results/Meridian.Benchmarks.CompositeSinkBenchmarks-report-github.md`
- `benchmarks/results/20260521_111257/wal/results/Meridian.Benchmarks.WalChecksumBenchmarks-report-github.md`
- `benchmarks/results/20260521_180245/results/Meridian.Benchmarks.CanonicalizingPublisherBenchmarks-report-github.md`
- `benchmarks/results/20260521_180245/results/Meridian.Benchmarks.CanonicalizingPublisherThroughputBenchmarks-report-github.md`
- `benchmarks/results/20260521_180245/results/Meridian.Benchmarks.CompositeSinkBenchmarks-report-github.md`
- `benchmarks/results/20260521_180245/results/Meridian.Benchmarks.DepthCollectorBenchmarks-report-github.md`
- `benchmarks/results/20260521_180245/results/Meridian.Benchmarks.EndToEndPipelineBenchmarks-report-github.md`
- `benchmarks/results/20260521_180245/results/Meridian.Benchmarks.TradeCollectorBenchmarks-report-github.md`
- `benchmarks/results/20260521_180245/results/Meridian.Benchmarks.WalChecksumBenchmarks-report-github.md`
- `benchmarks/results/20260521_181635/results/Meridian.Benchmarks.EventPipelineBenchmarks-report-github.md`
- `benchmarks/results/20260521_181812/results/Meridian.Benchmarks.EventBufferBenchmarks-report-github.md`
- `benchmarks/results/20260521_182021/results/Meridian.Benchmarks.BatchSerializationBenchmarks-report-github.md`
- `benchmarks/results/20260521_182313/results/Meridian.Benchmarks.JsonSerializationBenchmarks-report-github.md`
- ... and 6 more

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
- `Meridian Design System/BRAND_GUIDELINES.md`
- `Meridian Design System/CONTENT_FUNDAMENTALS.md`
- `Meridian Design System/ICONOGRAPHY.md`
- `Meridian Design System/INSPIRATION_BRIEF.md`
- `Meridian Design System/SKILL.md`
- `Meridian Design System/VISUAL_FOUNDATIONS.md`
- `benchmarks/BOTTLENECK_REPORT.md`
- ... and 221 more

## Trend

<!-- Trend data will be appended by CI when historical snapshots are available. -->

| Date | Score | Files | Orphans | Stale |
| ------ | ------- | ------- | --------- | ------- |
| 2026-06-17 | 83 | 600 | 241 | 0 |

---

_This file is auto-generated. Do not edit manually._
