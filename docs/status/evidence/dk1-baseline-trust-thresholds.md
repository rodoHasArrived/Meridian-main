# DK1 Baseline Trust Thresholds and FP/FN Review Process

**Last Updated:** 2026-04-21  
**Owners:** Data Operations + Provider Reliability + Trading  
**Scope:** Baseline trust thresholds for DK1 pilot and required false-positive/false-negative governance loop

---

## Baseline threshold profile (pilot default)

| Metric | Baseline threshold | Breach interpretation | Immediate gate impact |
|---|---|---|---|
| Composite trust score | `< 0.80` | Aggregate trust below pilot safety floor | Block parity sign-off for affected window |
| Connection stability score | `< 0.75` | Session/connectivity instability | Require operator review before trust restoration |
| Error-rate score | `< 0.85` | Error rate too high for reliable trust claim | Mark window as degraded; no promotion dependency allowed |
| Latency score | `< 0.80` | Latency outside calibrated normal band | Hold decisions dependent on low-latency parity |
| Reconnect score | `< 0.70` | Reconnect behavior materially unstable | Keep run in caution/degraded state |

> Thresholds are baseline defaults for DK1 pilot operations and must be recalibrated using current incident windows before broad rollout.

## Severity bands for operator interpretation

| Composite score range | Severity | Operator posture |
|---|---|---|
| `>= 0.90` | Healthy | Normal operation |
| `0.80 - 0.89` | Watch | Continue operation with focused monitoring |
| `0.70 - 0.79` | Degraded | Restrict parity-sensitive decisions |
| `< 0.70` | Critical | Treat as trust failure; block gate progress |

---

## False-positive / false-negative review process

### Definitions

- **False positive (FP):** Alert raised, but later review confirms no meaningful trust degradation occurred.
- **False negative (FN):** No alert raised, but later evidence confirms trust degradation should have been flagged.

### Review cadence

- Weekly DK1 review: classify all pilot-window alerts as TP/FP/FN/TN.
- Mandatory post-incident review: any critical severity incident triggers same-day FP/FN classification.

### Required evidence packet per review

1. Run-date parity packet from [`dk1-pilot-parity-runbook.md`](./dk1-pilot-parity-runbook.md)
2. Threshold and calibration context from [`../operations/provider-degradation-calibration.md`](../operations/provider-degradation-calibration.md)
3. Alert rationale mapping from [`dk1-trust-rationale-mapping.md`](./dk1-trust-rationale-mapping.md)

### Decision workflow

1. **Classify outcomes** (TP/FP/FN/TN) per alert window.
2. **Quantify impact**
   - FP rate = `FP / (FP + TP)`
   - FN rate = `FN / (FN + TP)`
3. **Apply adjustment rule**
   - If FP rate > 15%, tighten noisy reason-code thresholds only after confirming FN does not worsen.
   - If FN rate > 5%, relax sensitivity guardrails to improve recall at degraded/critical severities.
4. **Document decision**
   - Record changed thresholds, rationale, approvers, and effective date in weekly review notes.
5. **Re-run parity evidence**
   - Execute Wave 1 validation command and attach updated summary artifacts before declaring calibration pass.

### Exit criterion for DK1 calibration gate

DK1 calibration gate can pass only when:

- FP/FN review log is current for the active pilot window.
- Any threshold changes are documented with approvers and dates.
- Post-change parity evidence is linked and reproducible.
