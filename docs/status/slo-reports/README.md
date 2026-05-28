# Monthly SLO Reports

Store one report per month using:

- Filename: `YYYY-MM-slo-review.md`
- Template: [`docs/operations/slo-review-template.md`](../../operations/slo-review-template.md)

## Publication Workflow

1. Collect metrics for the monthly UTC window.
2. Fill out the template sections (compliance, burn rate, incidents, actions, launch gate).
3. Commit the report in this folder.

## Manual Collection Commands

Run these against a running local or production-representative host and store outputs under `artifacts/slo/YYYY-MM/`.

```bash
mkdir -p artifacts/slo/YYYY-MM
curl -sS http://localhost:8080/metrics > artifacts/slo/YYYY-MM/metrics.prom
curl -sS http://localhost:8080/status > artifacts/slo/YYYY-MM/status.json
curl -sS http://localhost:8080/healthz > artifacts/slo/YYYY-MM/healthz.txt
```

For incident linkage, export P1/P2 incident IDs and postmortem references from your incident tracker and copy into the monthly report.
