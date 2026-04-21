# Provider degradation kernel calibration workflow

This workflow adds an **offline calibration gate** before promoting new provider-degradation kernel weights.

## 1) Calibration input dataset format

Use a JSON file with this shape:

```json
{
  "datasetId": "incidents-2026-q1",
  "generatedAt": "2026-04-20T00:00:00Z",
  "source": "historical-provider-incidents",
  "windows": [
    {
      "provider": "Polygon",
      "windowStart": "2026-01-04T14:30:00Z",
      "windowEnd": "2026-01-04T14:35:00Z",
      "observedSeverity": "Critical",
      "connectionScore": 1.0,
      "latencyScore": 0.8,
      "errorRateScore": 0.9,
      "reconnectScore": 0.7,
      "eventsObserved": 40,
      "alertVolumeObserved": 6
    }
  ]
}
```

Each `windows[]` entry should represent one historical replay window captured from provider and data-quality incidents.

## 2) Offline calibration runner

Run:

```bash
Meridian --calibrate-provider-degradation \
  --calibration-input ./incidents-2026-q1.json \
  --candidate-kernel-version kernel-v2 \
  --baseline-kernel-version kernel-v1
```

The runner replays historical windows, computes composite scores for baseline and candidate kernels, and produces precision/recall metrics for severity thresholds (Minor/Moderate/Major/Critical).

## 3) Snapshot persistence

Calibration snapshots are persisted to:

- `dataRoot/calibration/provider-degradation/*.json`

Each snapshot contains:

- UTC timestamp (`createdAt`)
- `baselineKernelVersion`
- `candidateKernelVersion`
- per-severity precision/recall
- expected alert-volume delta
- `calibrationPass`

## 4) Freshness policy gates

Governance checks enforce:

- snapshot age <= policy `MaxSnapshotAge`
- snapshot candidate version must match promotion candidate
- minimum precision/recall thresholds at required severity (default: `Critical`)

If any check fails, promotion is blocked with explicit blocking reasons.

## 5) Dashboard/report output

The calibration command generates a markdown report (`latest-report.md`) comparing:

- baseline vs candidate precision/recall
- threshold values used per severity
- expected alert count baseline vs candidate
- expected alert-volume percent change

This report is suitable for governance review packets and dashboard ingestion.

## 6) Governance workflow “calibration pass” check

Promotion decisions for kernel weights are now tied to an explicit `calibrationPass` gate in `KernelWeightGovernanceWorkflowService`.

The workflow returns:

- `calibrationPass`
- `freshnessPass`
- `approved`
- explicit `blockingReasons`

A candidate kernel is promotable only when `approved == true`.
