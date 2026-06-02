---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-BACKTESTING
path: src/Meridian.Backtesting
status: active
owner_lane: Strategy and Research
last_reviewed: 2026-05-20
---

# src/Meridian.Backtesting

## Purpose

Backtesting contains runtime support for historical strategy simulation and replay-oriented research workflows.

## Layer responsibility

This layer should keep simulation behavior isolated from live execution while producing evidence that can flow into research and paper validation.

## Key folders and files

- `Meridian.Backtesting.csproj` - backtesting runtime project boundary.
- Runtime and replay implementation files for historical simulation.

## Important workflows

Use this module for strategy backtests, simulation runtime behavior, and backtesting evidence.
Backtest Studio engines must return canonical SDK `BacktestResult` instances so native Meridian and
external engine runs can flow through the same strategy-run repository, comparison, diff, and
portfolio drill-in surfaces.

## API / contract notes

- `BacktestRequest.OrderBookQueueAheadFraction` feeds `OrderBookFillModel` when order-book
  execution is selected. The model infers a bounded queue-ahead quantity from each visible L2
  level, reducing fillable depth without retaining per-order book state across large replay windows.
- `BacktestRequest.MaxParticipationRate` caps per-bar participation for bar-midpoint and
  market-impact fills. A value of `0` preserves the historical full-fill behavior; positive values
  leave oversized market-impact orders working across bars when partial fills are allowed.
- `MeridianNativeBacktestStudioEngine` stamps native engine output through
  `CanonicalBacktestResultNormalizer.FromNative`, matching the metadata contract used by
  QuantConnect Lean imports.

## Benchmarks and performance

- `MultiSymbolMergeEnumerator` uses a heap for multi-symbol replay and a single-stream fast path
  for one-symbol runs, avoiding per-event heap churn on large historical windows while preserving
  cancellation and enumerator disposal.
- Corporate-action adjustment in `BacktestEngine` adjusts historical bars one event at a time after
  cached Security Master action lookup, so mixed bar/trade/depth streams do not buffer replay
  windows before yielding downstream events.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-BACKTESTING -->
| Roadmap item | Title |
| --- | --- |
| `W3-CONT-001` | Research to paper continuity |
| `W6-BTSTUDIO-001` | Backtesting studio evidence loop |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-BACKTESTING -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.Backtesting.Tests/Meridian.Backtesting.Tests.csproj --logger "console;verbosity=normal"
```

## Change rules

Keep backtesting deterministic and separate from live broker actions.

## Related docs

- `docs/source/generated/source-roadmap-traceability.md`
- `docs/plans/waves-2-4-operator-readiness-addendum.md`
