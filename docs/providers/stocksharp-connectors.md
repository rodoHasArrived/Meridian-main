# StockSharp Connector Status

## Purpose

This page captures Meridian's current StockSharp posture so older setup references and operator docs
still resolve to one current source of truth.

## Current Status

- StockSharp remains reference inventory and future validation scope rather than an active Wave 1
  provider-confidence gate.
- Real usage requires `EnableStockSharp=true`, connector-specific packages, and any vendor/runtime
  dependencies required by the selected connector.
- The default repo baseline does not claim that every StockSharp connector is live or validated in
  the current build.

## Runtime Expectations

- Treat StockSharp enablement as a manual, environment-specific validation lane.
- Use committed offline evidence for active provider claims; do not treat StockSharp as closed by
  default CI or replay coverage.
- Confirm any connector-specific desktop or vendor software before advertising a connector as
  operator-ready.

## Related

- [Provider Confidence Baseline](provider-confidence-baseline.md#deferred-provider-inventory)
- [Data Sources Reference](data-sources.md)
- [Operator Runbook](../operations/operator-runbook.md#stocksharp)
