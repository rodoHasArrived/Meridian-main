# Documentation Health Dashboard

_Auto-generated from canonical JSON payload._
_Generated: 2026-06-16T06:27:08.531105+00:00_
Data sources: `repo markdown (*.md)`, `file modification metadata`


> Auto-generated documentation health report. Do not edit manually.
> Last updated: 2026-06-16T06:27:08.341098+00:00

## Overall Health Score

```text
  [#########################-----] 82/100
  Rating: Good
```

## Summary

| Metric | Value |
| -------- | ------- |
| Total documentation files | 714 |
| Total lines | 102,480 |
| Average file size (lines) | 143.5 |
| Orphaned files | 293 |
| Files without headings | 22 |
| Stale files (>90 days) | 69 |
| TODO/FIXME markers | 231 |
| **Health score** | **82/100** |

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

- `.nuget/packages/newtonsoft.json/13.0.4/LICENSE.md`
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
- ... and 7 more

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
- `.nuget/packages/communitytoolkit.highperformance/8.4.0/License.md`
- `.nuget/packages/farmer/1.9.26/readme.md`
- `.nuget/packages/microsoft.aspnetcore.openapi/9.0.15/PACKAGE.md`
- `.nuget/packages/microsoft.bcl.asyncinterfaces/10.0.3/PACKAGE.md`
- `.nuget/packages/microsoft.bcl.asyncinterfaces/8.0.0/PACKAGE.md`
- `.nuget/packages/microsoft.codeanalysis.analyzers/5.3.0-2.25625.1/documentation/readme.md`
- `.nuget/packages/microsoft.codecoverage/18.5.1/PACKAGE.md`
- `.nuget/packages/microsoft.extensions.caching.abstractions/10.0.5/PACKAGE.md`
- ... and 273 more

### Stale Documentation

These files have not been updated in over 90 days:

- `.nuget/packages/azure.core.amqp/1.3.1/CHANGELOG.md`
- `.nuget/packages/azure.core.amqp/1.3.1/README.md`
- `.nuget/packages/azure.core/1.38.0/CHANGELOG.md`
- `.nuget/packages/azure.core/1.38.0/README.md`
- `.nuget/packages/azure.core/1.46.2/CHANGELOG.md`
- `.nuget/packages/azure.core/1.46.2/README.md`
- `.nuget/packages/azure.identity/1.11.4/CHANGELOG.md`
- `.nuget/packages/azure.identity/1.11.4/README.md`
- `.nuget/packages/azure.messaging.servicebus/7.20.1/CHANGELOG.md`
- `.nuget/packages/azure.messaging.servicebus/7.20.1/README.md`
- `.nuget/packages/communitytoolkit.highperformance/8.4.0/License.md`
- `.nuget/packages/farmer/1.9.26/readme.md`
- `.nuget/packages/fluentvalidation/12.1.1/README.md`
- `.nuget/packages/fsharpplus/1.9.1/README.md`
- `.nuget/packages/fstoolkit.errorhandling/5.2.0/README.md`
- `.nuget/packages/microsoft.bcl.asyncinterfaces/10.0.3/PACKAGE.md`
- `.nuget/packages/microsoft.bcl.asyncinterfaces/8.0.0/PACKAGE.md`
- `.nuget/packages/microsoft.codeanalysis.analyzers/5.3.0-2.25625.1/documentation/Microsoft.CodeAnalysis.Analyzers.md`
- `.nuget/packages/microsoft.codeanalysis.analyzers/5.3.0-2.25625.1/documentation/readme.md`
- `.nuget/packages/microsoft.codecoverage/18.5.1/PACKAGE.md`
- ... and 49 more

## Trend

<!-- Trend data will be appended by CI when historical snapshots are available. -->

| Date | Score | Files | Orphans | Stale |
| ------ | ------- | ------- | --------- | ------- |
| 2026-06-16 | 82 | 714 | 293 | 69 |

---

_This file is auto-generated. Do not edit manually._
