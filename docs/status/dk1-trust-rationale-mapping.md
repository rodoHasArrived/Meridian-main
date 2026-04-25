# DK1 Operator Trust Rationale Mapping

**Last Updated:** 2026-04-24  
**Owners:** Data Operations + Trading  
**Scope:** Operator-facing trust rationale for DK1 alerts (signal source, reason code, recommended action)

---

## Purpose

Define the required explainability contract for DK1 trust alerts so every alert has:

1. **Signal source** (where the signal came from)
2. **Reason code** (why the trust state changed)
3. **Recommended action** (what operator should do next)

## Trust rationale map

| Signal source | Reason code | Operator-facing meaning | Recommended action |
|---|---|---|---|
| Provider baseline health snapshot | `HEALTHY_BASELINE` | Provider is within the current DK1 trust baseline and has no active alert. | Continue monitoring provider health; no DK1 action is required. |
| Provider quote/trade stream health telemetry | `PROVIDER_STREAM_DEGRADED` | Live stream reliability dropped below expected baseline. | Verify provider connectivity + entitlements, then monitor for recovery before promotion decisions. |
| Provider reconnect monitor | `RECONNECT_INSTABILITY` | Frequent reconnects indicate unstable session quality. | Keep run in observation mode; require stable reconnect window before trusting parity-sensitive outputs. |
| Error-rate monitor | `ERROR_RATE_SPIKE` | Provider/API errors exceeded acceptable rate for the active window. | Inspect recent provider errors and suppress downstream trust claims until error rate normalizes. |
| Latency monitor | `LATENCY_REGRESSION` | Data latency moved outside calibrated operational range. | Delay operator promotion actions; review latency trend and compare against baseline window. |
| Cross-provider parity comparator | `PARITY_DRIFT_DETECTED` | Provider outputs diverge materially from the pilot parity baseline. | Re-run parity packet and treat results as non-promotable until drift is explained or corrected. |
| Missing data completeness checker | `DATA_COMPLETENESS_GAP` | Expected records/events were missing for the scenario window. | Trigger targeted backfill/replay and block trust sign-off for impacted symbols/windows. |
| Calibration freshness validator | `CALIBRATION_STALE` | Trust thresholds are based on outdated calibration snapshot. | Run calibration refresh and do not approve threshold-sensitive decisions until freshness is restored. |

---

## Evidence and reference links

- Kernel readiness dashboard DK1 explainability gate: [`kernel-readiness-dashboard.md#dk1---data-quality--provider-trust`](./kernel-readiness-dashboard.md#dk1---data-quality--provider-trust)
- Provider validation evidence source: [`provider-validation-matrix.md`](./provider-validation-matrix.md)
- Calibration workflow reference: [`../operations/provider-degradation-calibration.md`](../operations/provider-degradation-calibration.md)

## Minimum operator UX requirement

For every trust alert in pilot operations, the UI/API payload must expose:

- `signalSource`
- `reasonCode`
- `recommendedAction`

Alerts missing any field are treated as **explainability failures** for DK1 gate review.

## Current implementation surface

- `GET /api/workstation/data-operations` provider rows expose `trustScore`, `signalSource`, `reasonCode`, `recommendedAction`, and `gateImpact` for provider metrics loaded from `_status/providers.json`.
- The WPF Data Operations provider queue includes the DK1 signal source, reason code, and recommended action in its visible provider-health detail when provider routing is degraded or disconnected.
