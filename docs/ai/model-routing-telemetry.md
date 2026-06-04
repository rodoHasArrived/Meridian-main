# AI Model Routing and Cost Telemetry Basis

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-06-04

This document is the evidence surface for `docs/ai/model-routing-policy.json` and its `routingRules`, `modelClasses`, and `telemetrySignals` sections.

## Source of Truth (docs-only)

`docs/ai/model-routing-policy.json` is the only runtime policy source for model routing behavior.
Application runtime and operational checks must use this file directly; no alternate mirrors,
generated artifacts, environment-sourced paths, or fallback policy files are permitted.
It is intentionally compact and source-of-truth for two things:

- what telemetry signals the route validation should collect per task,
- how teams report token/cost behavior for model-routing decisions.

## Required Signals

Each routing event should capture these fields at handoff boundaries:

| Signal | Purpose | Source |
| --- | --- | --- |
| `route_id` | identifies the policy rule chosen | routing orchestrator |
| `selected_model` | exact model class/instance used | orchestrator or host |
| `input_tokens` | input token consumption | provider usage payload |
| `output_tokens` | output token consumption | provider usage payload |
| `estimated_cost_usd` | token-to-cost estimate in USD | policy cost calculator |
| `latency_ms` | end-to-end completion time | request timing trace |
| `error_class` | structured error/fallback path classification | runtime logs |
| `handoff_path` | packet or handoff artifact path | coordinator handoff |

## Signal Quality Requirements

- Signal capture must preserve the full run context for rerunability.
- Cost estimates must be computed from the current policy token-rate baseline.
- `estimated_cost_usd` should be non-null for every completed request and zero for non-failed short-circuit paths.
- `latency_ms` should be captured when available and should not include local file IO outside the model call window.

## Evidence Packet Requirements

For any strict model-routing decision handoff, record at least:

- `Model routing decision`
- `Model class selected`
- `Cost estimate`
- `Escalation trigger`
- `Telemetry signal capture`
- `Fallback path selected` (when downgrade/upgrade is used)

The corresponding checklist section is expected in `docs/ai/agent-handoff-checklist.md`.

## Context Efficiency Defaults

The policy `contextEfficiency` block carries shared output and context budget defaults so agents do
not rediscover token-saving rules on every task:

- `summaryLinesMax` — soft ceiling on handoff summary length before trimming detail.
- `rawLogPolicy` — summarize command output; attach full logs only when diagnosis needs them.
- `reuseValidationWhenUnchanged` — reuse prior validation evidence when touched files are unchanged.
- `preferReferencesOverFullFileDumps` — link required files instead of pasting whole files.
- `loadContextFirst` — shared navigation files to read before recursive search.
- `handoffTemplate` — the parallel task manifest template used for lane handoffs.

## Escalation Signals

Routes may escalate to a higher-cost class when one or more of these are true:

- complexity threshold exceeded,
- a safety-critical marker is raised,
- estimated per-task cost would exceed the route budget,
- provider/provider-bridge errors indicate routing instability.

Escalation must produce a short follow-on handoff packet and preserve the original route context.

## Policy Lifecycle

- Refresh this document when:
  - tokens/cost formulas change,
  - provider telemetry fields change,
  - routing classes are added/retired,
  - escalation rules change.
- Keep generated examples and command outputs aligned with the latest validated policy artifact.
