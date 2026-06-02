---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-BACKTESTING-SDK
path: src/Meridian.Backtesting.Sdk
status: active
owner_lane: Strategy and Research
last_reviewed: 2026-05-20
---

# src/Meridian.Backtesting.Sdk

## Purpose

Backtesting SDK defines reusable simulation contracts for strategy and backtesting integrations.

## Layer responsibility

This layer should expose stable backtesting integration seams without owning runtime orchestration details.

## Key folders and files

- `Meridian.Backtesting.Sdk.csproj` - SDK project boundary.
- Strategy and simulation integration contracts.

## Important workflows

Use this module when a backtesting contract must be shared across runtime and strategy code.

## API / contract notes

- `BacktestRequest.OrderBookQueueAheadFraction` configures queue-aware order-book replay. The
  default `0` preserves immediate visible-depth fills; positive values infer that a fraction of
  each executable level is queued ahead of the simulated order.
- `BacktestRequest.MaxParticipationRate` applies to bar-midpoint and market-impact execution. The
  default `0` keeps backward-compatible full-bar fills; positive values cap the filled quantity to
  the configured fraction of traded bar volume and carry the remainder forward when partial fills
  are allowed.
- `CanonicalBacktestResultNormalizer` is the shared result-normalization seam for Backtest Studio
  engines. Native results are marked with full canonical coverage, while QuantConnect Lean imports
  normalize into the same `BacktestResult` storage model with `SummaryOnly` coverage metadata and
  compatibility warnings for missing fill, cash-flow, attribution, and ledger artifacts.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-BACKTESTING-SDK -->
| Roadmap item | Title |
| --- | --- |
| `W6-BTSTUDIO-001` | Backtesting studio evidence loop |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-BACKTESTING-SDK -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet build src/Meridian.Backtesting.Sdk/Meridian.Backtesting.Sdk.csproj /p:EnableWindowsTargeting=true
```

## Change rules

Keep SDK changes backward-compatible unless the roadmap explicitly accepts a breaking migration.

## Related docs

- `docs/architecture/module-map.md`
- `docs/source/generated/source-module-index.md`
