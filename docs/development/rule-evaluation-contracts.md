# Rule Evaluation Contract Layer

**Owner:** Core Team  
**Scope:** Shared decision-envelope schema across C# kernels, with F# migration compatibility  
**Last Reviewed:** 2026-04-20  
**Review Cadence:** When schema, kernel versions, or rule-evaluation boundaries change

## Purpose

Meridian kernels for data quality, provider trust, promotion, export, reconciliation, and provider degradation now share one decision envelope shape:

- `Score`
- `Reasons`
- `Trace`

The shared contracts live in `src/Meridian.Contracts/RuleEvaluation/DecisionContracts.cs`.

## Common Envelope Schema

### `DecisionResult<TScore>`

| Field | Required | Notes |
|---|---|---|
| `Score` | Yes | Kernel-specific score type (`double`, `decimal`, enum-backed, etc.). |
| `Reasons` | Yes | Ordered list of `DecisionReason` entries. Use an empty list when no rule fired. |
| `Trace` | Yes | `DecisionTrace` metadata for rollout safety and diagnostics. |

### `DecisionReason`

| Field | Required | Notes |
|---|---|---|
| `RuleId` | Yes | Stable identifier for the exact rule implementation. |
| `Weight` | Yes | Weighted contribution (penalty or bonus). |
| `ReasonCode` | Yes | Machine-friendly short code for automation. |
| `HumanExplanation` | Yes | Human-readable explanation for logs/UI/operator review. |
| `Severity` | No (recommended) | Defaults to `Info`; set explicitly when rule importance matters. |
| `EvidenceRefs` | No (recommended) | References to evidence IDs, metric labels, timestamps, or source records. |

### `DecisionTrace`

| Field | Required | Notes |
|---|---|---|
| `SchemaVersion` | Yes | Version of the envelope schema (`major.minor.patch` string). |
| `KernelVersion` | Yes | Version identifier for the scoring kernel implementation. |
| `EvaluatedAt` | Yes | UTC timestamp for deterministic audit trails. |
| `CorrelationId` | No | Request/job/run correlation key when available. |
| `Metadata` | No | Additional key/value trace data (provider, run id, capability, etc.). |

## Kernel Output Requirements

All kernels must return `DecisionResult<TScore>` and follow the matrix below.

| Kernel boundary | Required fields | Optional fields |
|---|---|---|
| Data quality | `Score`, `Reasons[*].RuleId`, `Reasons[*].Weight`, `Reasons[*].ReasonCode`, `Reasons[*].HumanExplanation`, `Trace.SchemaVersion`, `Trace.KernelVersion`, `Trace.EvaluatedAt` | `Reasons[*].Severity`, `Reasons[*].EvidenceRefs`, `Trace.CorrelationId`, `Trace.Metadata` |
| Provider trust | Same required set as above | Same optional set as above |
| Promotion | Same required set as above | Same optional set as above |
| Export | Same required set as above | Same optional set as above |
| Reconciliation | Same required set as above | Same optional set as above |
| Provider degradation | Same required set as above | Same optional set as above |

## Rollout Notes

1. **Schema safety:** increment `SchemaVersion` when envelope shape changes.
2. **Kernel safety:** increment `KernelVersion` when scoring behavior or rule logic changes.
3. **Compatibility:** C# call sites should consume the envelope now; F# kernels can migrate incrementally while preserving the same output contract.
