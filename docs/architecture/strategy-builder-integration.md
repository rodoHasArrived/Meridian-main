# Strategy Builder Integration

## Scope

The browser Strategy workspace now treats `stock-strategy-build` as a product prototype and
requirements source, not as a vendored application. Meridian owns the runtime contract, storage,
validation, execution, and governance flow.

The first integrated surface is `/workstation/strategy/designer`.

## Imported Concepts

The pinned prototype baseline is `rodoHasArrived/stock-strategy-build` commit
`2826afc0dc5ca4590bf0860871acd45933088bad`. The v1 Meridian work imports these concepts:

- Cell-based strategy documents: visual, formula, code, governance, and options-payoff cells.
- AMX-style field catalog: mapped fields stay enabled when Meridian has a canonical source;
  unmapped AMX fields stay visible but disabled with an operator-facing reason.
- Formula autocomplete vocabulary: suggestions come from the enabled field catalog.
- Transitions and loop guards: backward or loop transitions require a bounded iteration count and
  rationale before execution.
- Run trace and governance proof: validation, cell compilation, generated QuantScript, dataset
  fingerprint, and review-packet handoffs are explicit response fields.
- Templates: equities momentum, investment-grade income, and options payoff are represented as
  Meridian design templates.

Prototype implementation details such as Spark KV, browser `localStorage`, Monaco, AG Grid,
Recharts, Phosphor icons, and JavaScript backtest execution are intentionally excluded from the v1
runtime.

## Runtime Boundary

`Meridian.Contracts.Workstation` defines the shared DTOs:

- `StrategyDesignDocument`, `StrategyDesignCell`, and `StrategyDesignTransition`
- `StrategyDesignTemplate` and `StrategyDesignFieldCatalogItem`
- `StrategyDesignValidationResult`, `StrategyDesignPreviewResult`, and
  `StrategyDesignRunBacktestResponse`

`Meridian.Strategies` owns design validation and durability:

- `StrategyDesignService` normalizes documents, validates fields and transitions, builds preview
  rows, emits trace entries, and compiles QuantScript source.
- `IStrategyDesignRepository` persists drafts.
- `JsonlStrategyDesignRepository` appends draft versions to
  `data/strategies/designer/strategy-design-drafts.jsonl`, using `AtomicFileWriter`.

`Meridian.Ui.Shared` exposes the browser workstation contract under
`/api/workstation/strategy/designer/*`:

- `GET templates`
- `GET field-catalog`
- `GET drafts`
- `GET drafts/{documentId}`
- `POST drafts`
- `POST validate`
- `POST preview`
- `POST run-backtest`

Draft mutation and backtest execution require `ManageStrategies`. Backtest execution also requires
Quant Lab to be enabled; otherwise the endpoint returns `503`.

## Execution Flow

The designer does not execute the prototype JavaScript runner. A valid document compiles into
QuantScript and flows through the existing `IScriptRunner` seam. Successful proof runs record a
normal `StrategyRunEntry` with:

- `RunType.Backtest`
- `Engine = QuantScript`
- `DatasetReference` from the design document
- `FeedReference = strategy-designer:v1`
- `datasetFingerprint`, `designerDocumentId`, and cell count in the parameter set

The response carries promotion and run-review handoff routes so successful proof runs can continue
through the existing promotion and trading-readiness workflow.

## Browser Surface

The dashboard route uses the existing browser stack: React 18, lucide icons, local UI primitives,
`MetricCard`, `DenseDataTable`, and existing SVG chart patterns. It does not introduce the
prototype dependency stack.

The workbench includes:

- field catalog rail
- dense cell canvas
- selected-cell inspector
- template gallery
- transition map
- run trace and backtest proof panel
- embedded options payoff template panel preserving the previous option-leg payoff math

View-model code owns labels, disabled reasons, selected state, validation copy, route actions, and
live-region text.
