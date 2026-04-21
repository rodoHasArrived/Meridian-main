# F# Decision Rule for Meridian

## Purpose

Use F# in this repository only when the candidate logic satisfies a concrete fit test. This replaces the earlier "F# is generally a good fit" guidance with an explicit decision rule.

## Decision Rule

Choose F# for a subsystem only when **all** of the following are true:

1. The core behavior is dominated by **rules-heavy, side-effect-light transforms** rather than host orchestration.
2. The logic can be expressed as a **pure transform over explicit inputs and outputs**.
3. The C#/F# boundary can be kept narrow:
   - **contract and input types in C#**
   - **pure transform module in F#**
   - **orchestration, DI, logging, persistence, and integration in C#**
4. The subsystem can be validated with deterministic unit tests without booting provider connections, storage, or other service infrastructure.
5. The team can name a specific pilot boundary and explain why F# improves correctness or maintainability for that case.

If any of those conditions are false, prefer declarative C# first.

## Prefer C# First When

Stay in C# when the implementation is primarily any of the following:

- Host or integration orchestration.
- Deep dependency injection or service-locator interaction.
- Logging, metrics, retries, scheduling, or lifecycle management.
- Provider SDK adaptation, transport code, storage code, or framework glue.
- Logic whose correctness depends more on collaborating services than on pure rule evaluation.

In short: **use F# for pure decisions; use C# for integration-heavy coordination**.

## What `Pipeline/Transforms.fs` Tells Us

Review of `src/Meridian.FSharp/Pipeline/Transforms.fs` shows two distinct categories.

### Pure transforms

These functions are good examples of F#-friendly logic because they map explicit inputs to outputs without external callbacks or cross-enumeration hidden state:

- `filterBySymbol`, `filterBySymbols`, `filterByTimeRange`
- `filterTrades`, `filterQuotes`, `filterDepth`, `filterIntegrity`
- `enrichQuotes`
- `validateAndFilter`
- `groupBySymbol`
- `mergeStreams`
- `bufferByCount`, `bufferByTime`
- `simpleMovingAverage`, `exponentialMovingAverage`, `rateOfChange`
- `detectGaps`
- `TransformPipeline.create`, `TransformPipeline.add`, `TransformPipeline.run`
- `TransformPipeline.filterSymbol`, `TransformPipeline.filterTime`, `TransformPipeline.validate`, `TransformPipeline.dedupe`, `TransformPipeline.throttle`, `TransformPipeline.sample`

These are good candidates because they are fundamentally selection, validation, scoring, aggregation, normalization, or canonicalization-style operations over in-memory values.

### Stateful or streaming helpers

These functions depend on mutable rolling state, event ordering, or callbacks, so they need more care and should not be treated as automatic F# candidates:

- `enrichWithAggressor` — keeps the last quote while walking a stream.
- `partitionByType` — uses mutable accumulators to build categorized outputs.
- `sampleAtInterval` — tracks the last emitted timestamp.
- `deduplicate` — maintains a `HashSet` of seen sequence numbers.
- `throttleBySymbol` — maintains per-symbol emission state.
- `normalizePrices` — anchors later outputs to the first observed trade.
- `lag` — maintains a rolling queue.
- `TransformPipeline.filterGaps` — invokes an external callback as a side effect.

These helpers can still live in F#, but they should clear a higher bar because they are closer to streaming orchestration than to standalone rule evaluation.

## Good F# Candidates in Meridian

Use F# when the subsystem is primarily one of these:

- **Validation rules** — checking well-formedness, completeness, sequencing, range constraints, or consistency rules.
- **Scoring logic** — deterministic score/rank calculations over explicit inputs.
- **Normalization** — converting provider-specific values into stable internal representations.
- **Canonicalization decisions** — deterministic winner-selection or field-resolution rules that turn multiple acceptable representations into one canonical form.
- **Other side-effect-light transforms** — small rule engines, classification functions, and pure derived-field calculations.

## Required Interop Boundary

Every F# pilot in this repository must document this split:

1. **C# contract/input layer**
   - public contracts
   - request DTOs / domain records used by the host
   - dependency-managed services that gather inputs
2. **F# pure transform layer**
   - no direct DI container access
   - no logging or persistence side effects
   - deterministic functions over explicit inputs
3. **C# orchestration/integration layer**
   - service composition
   - provider calls
   - retries, metrics, logging, and persistence
   - adaptation back into the rest of the application

If that boundary is blurry, the subsystem is not ready for F#.

## Pilot Choice

**Pilot subsystem: trade and quote validation rules in `src/Meridian.FSharp/Validation/`.**

### Why this pilot was chosen

- It is already the clearest rules-heavy subsystem in the repository.
- The core behavior is a deterministic accept/reject decision with structured validation errors.
- It has a natural interop boundary: C# event contracts and pipeline integration, F# validation transforms, C# composition in `FSharpEventValidator`.
- It exercises the kind of logic F# is best at in this codebase: validation, normalization of decisions, and exhaustive rule handling.
- It lets the team evaluate actual maintainability and correctness benefits before expanding F# usage.

### What this means for future adoption

Future F# adoption should be justified by similarity to this pilot:

- rules-heavy
- deterministic
- narrow interop surface
- low service coupling
- measurable improvement in correctness or maintainability

Do **not** expand F# because it is stylistically appealing. Expand it only when the pilot pattern demonstrates better fit than declarative C#.

## Decision-Kernel Expansion Pattern

When extending F# usage beyond the current validation pilot, reuse one implementation pattern:

1. **C# contracts + input shaping**
2. **F# decision kernel** (`inputs -> score/decision + structured reasons`)
3. **C# orchestration** (timers, state, logging, I/O, cancellation, DI wiring)

This keeps the boundary explicit and allows new kernels to inherit the same testability profile as the existing validation interop layer.

## Recommended Expansion Sequence

Apply the decision-kernel pattern in this order.

### 1) Data-quality scoring kernels (highest leverage)

Best-fit boundaries:

- `CompletenessScoreCalculator`
- `GapAnalyzer`
- `SequenceErrorTracker`

Scope guidance:

- Keep rolling timers, state retention, and telemetry/event emission in C#.
- Move deterministic score/severity/classification functions into F# modules.

Why first:

- These scores drive trust signals consumed across Data Operations, monitoring, and downstream readiness gates.

### 2) Provider trust + degradation scoring

Best-fit boundaries:

- `ProviderTrustScoringService` in `src/Meridian.Application/ProviderRouting/ProviderOperationsSupportServices.cs`
- `ProviderDegradationScorer` in `src/Meridian.Application/Monitoring/ProviderDegradationScorer.cs`

Scope guidance:

- Keep provider polling, scheduling cadence, metrics/logging, and side-effect orchestration in C#.
- Move weighted rule evaluation into F# functions that return score plus reason codes.

Why second:

- Central trust/degradation outputs influence routing and operator confidence across multiple surfaces.

### 3) Promotion/governance policy matrix

Current state:

- Promotion already has an F# seam (`BacktestToLivePromoter` via `Interop.PromotionInterop`).

Next boundary:

- Consolidate eligibility and governance decision trees (manual overrides, live-gate policy, approval predicates) into F# decision kernels.
- Keep `PromotionService` and surrounding workflow orchestration in C#.

### 4) Export preflight rule engine

Best-fit boundary:

- `src/Meridian.Storage/Export/ExportValidator.cs`

Scope guidance:

- Keep file-system access, permission probing, and cancellation handling in C#.
- Express deterministic preflight rules in F# evaluators that return structured issue sets.

### 5) Reconciliation break classification

Best-fit boundary:

- Extend rule classification on top of existing ledger-oriented F# foundations in `src/Meridian.FSharp.Ledger/`.

Scope guidance:

- Use exhaustive pattern matching for break-type classification and deterministic reason-code generation.
- Keep queue persistence, review workflows, and external side effects in C# services.

## Prioritization Rationale

- **Highest-leverage start:** data quality + provider trust kernels (steps 1 and 2) because they centralize cross-workspace risk/trust signals.
- **Platform bet:** one shared C#/F# decision-kernel contract reused by promotion, export, and reconciliation instead of subsystem-specific interop styles.
- **Product signal:** better deterministic scoring lineage supports operator explainability and competitive trust narratives without moving integration-heavy services out of C#.
