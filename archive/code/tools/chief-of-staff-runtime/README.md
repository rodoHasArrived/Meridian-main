# Meridian Chief of Staff Runtime (ADK scaffold)

This folder contains an additive scaffold for an out-of-process Chief of Staff (CoS) runtime.

## Goals

- Keep CoS orchestration out of the main .NET host.
- Execute intent-to-evidence orchestration over existing Meridian MCP/API surfaces.
- Return one shared contract (`markdown + structured JSON + actions`) consumed by WPF and dashboard clients.
- Emit trace records suitable for evidence retention.

## Current scaffold

- `runtime.py` defines the workflow node boundaries:
  - `IntentAnalysisNode`
  - `ContextAssemblyNode`
  - `EvidenceAggregationNode`
  - `RecommendationSynthesisNode`
  - `DecisionPreparationNode`
  - `TraceEmissionNode`
- `execute` function composes the node flow and returns a runtime response envelope.

## Integration boundary

The .NET host calls this runtime through `IChiefOfStaffRuntimeClient` at:

- `POST /execute`
- `GET /health`

The scaffold intentionally does **not** mutate Meridian ledger/reconciliation source-of-truth state.
