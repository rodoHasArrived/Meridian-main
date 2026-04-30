# Preflight Checklist

This checklist maps 1:1 to the pre-production checklist in `docs/status/production-status.md` and is designed for pilot go/no-go reviews.

## Checklist Mapping

| Production-status item | Operational verification evidence |
|---|---|
| Configure real provider credentials and validate operator startup paths | Credential inventory completed; `--quick-check`, `--test-connectivity`, and `--dry-run` logs attached. |
| Complete remaining provider-confidence hardening for Polygon, StockSharp, IB, and optional NYSE | Provider hardening tracker updated; connector-specific smoke tests passed. |
| Validate brokerage gateway adapters (Alpaca, IB, StockSharp) against live vendor surfaces | Adapter validation report with timestamps and environment labels (paper/live) archived. |
| Build paper-trading cockpit in web dashboard wired to brokerage gateways | Paper cockpit walkthrough recorded; create/close paper session evidence captured. |
| Finish the evidence-backed operator workflow beyond the first shared run baseline | Operator acceptance checklist completed for `Data`, `Strategy`, `Trading`, `Portfolio`, `Accounting`, and `Reporting` surfaces across the trusted-data -> governed-report path. |
| Productize Security Master for operator use | Security Master read/write path validation complete; sample entity lifecycle exercised. |
| Implement multi-ledger, trial-balance, and cash-flow governance views | Governance view screenshots/reports attached; validation scenarios executed. |
| Implement reconciliation workflows and break-review UX | Reconciliation run created, reviewed, and resolved in pilot environment. |
| Implement report generation and governed export/report-pack flows | Export/report-pack generation tested with retention and audit metadata checks. |
| Validate end-to-end observability and operator diagnostics against the final product surfaces | Metrics, traces, logs, and alert-routing verification completed for all pilot-critical flows. |

## Usage

1. Copy this page into release artifacts for each pilot wave.
2. Attach objective evidence links for every row.
3. Mark launch decision only when all rows have passing evidence.

## Source of truth

- [Production status pre-production checklist](../status/production-status.md#pre-production-checklist)
