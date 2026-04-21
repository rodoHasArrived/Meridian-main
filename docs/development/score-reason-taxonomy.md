# Shared score explanation reason taxonomy

This document defines the canonical reason codes used by scoring kernels when they emit deterministic "why" artifacts.

## Canonical reason codes

| Code | Meaning | Typical producer |
|---|---|---|
| `LATENCY_SPIKE` | Latency distribution moved above acceptable range and increased degradation score. | Provider latency/degradation kernels |
| `GAP_BURST` | Gap frequency or duration increased and reduced quality score. | Data-quality gap kernels |
| `SEQUENCE_DUPLICATE` | Duplicate/out-of-order sequence behavior affected score. | Sequence-validation kernels |
| `MANUAL_OVERRIDE` | Operator/action policy overrode normal scoring behavior. | Workstation/manual-control flows |
| `CONNECTION_UNSTABLE` | Disconnects/missed heartbeats increased degradation score. | Provider degradation kernel |
| `ERROR_RATE_ELEVATED` | Error rate exceeded threshold and degraded score. | Provider degradation kernel |
| `RECONNECT_BURST` | Reconnect frequency indicates unstable transport behavior. | Provider degradation kernel |

## Deterministic emission requirements

All scoring kernels should:

1. Emit reasons in descending order by absolute contribution magnitude.
2. Emit reason `contribution` as the signed component impact on final score.
3. Emit stable reason code strings (no localized/user-generated text in code field).
4. Emit no-op reasons only when they are material (`|contribution| > 0`).

The authoritative machine-readable registry is maintained at `config/score-reason-registry.json`.
