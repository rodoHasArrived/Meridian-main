# Wave 1 Validation Summary

- Generated: 2026-04-17T02:07:07.4905416Z
- Configuration: Release
- Overall result: failed

| Step | Kind | Status | Duration (s) | Log |
|---|---|---|---:|---|
| Meridian.Tests build | build | passed | 103.5 | `artifacts/provider-validation/_automation/2026-04-16/meridian-tests-build.log` |
| Polygon replay and parsing | test | passed | 9.58 | `artifacts/provider-validation/_automation/2026-04-16/polygon-replay-and-parsing.log` |
| Robinhood supported surface | test | failed | 6.67 | `artifacts/provider-validation/_automation/2026-04-16/robinhood-supported-surface.log` |
| Interactive Brokers guidance and version bounds | test | passed | 3.35 | `artifacts/provider-validation/_automation/2026-04-16/interactive-brokers-guidance-and-version-bounds.log` |
| NYSE shared lifecycle and bounded runtime seams | test | passed | 3.15 | `artifacts/provider-validation/_automation/2026-04-16/nyse-shared-lifecycle-and-bounded-runtime-seams.log` |
| StockSharp validated adapter baseline | test | passed | 3.41 | `artifacts/provider-validation/_automation/2026-04-16/stocksharp-validated-adapter-baseline.log` |
| Checkpoint reliability and gap handling | test | passed | 5.1 | `artifacts/provider-validation/_automation/2026-04-16/checkpoint-reliability-and-gap-handling.log` |
| Parquet sink and conversion | test | passed | 3.93 | `artifacts/provider-validation/_automation/2026-04-16/parquet-sink-and-conversion.log` |
| IBApi compile-only smoke build | script | passed | 50.92 | `artifacts/provider-validation/_automation/2026-04-16/ibapi-compile-only-smoke-build.log` |
