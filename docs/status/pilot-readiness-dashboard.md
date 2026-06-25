# Pilot Readiness Dashboard

_Auto-generated from canonical JSON payload._
_Generated: 1970-01-01T00:00:00+00:00_
Data sources: `docs/status/provider-validation-matrix.md`, `docs/status/evidence/dk1-pilot-parity-runbook.md`, `docs/status/kernel-readiness-dashboard.md`, `artifacts/pilot-acceptance/latest/pilot-readiness.json`, `scripts/dev/*dk1*`, `tests/scripts/test_*dk1*`


Tracks whether DK1 pilot evidence, packet-bound operator sign-off, the trading readiness handoff, and local golden-path acceptance artifact remain present and synchronized.

## Summary

| Metric | Value |
| --- | ---: |
| Score | 58.8% |
| Passed checks | 4 |
| Gap checks | 3 |
| Missing evidence sources | 0 |
| Missing expected terms | 9 |

## Evidence Checks

| Category | Check | Status | Score | Evidence | Missing |
| --- | --- | --- | ---: | --- | --- |
| Golden Path Evidence | Pilot acceptance artifact proves all eight golden-path stage gates | Pass | 4/4 | `artifacts/pilot-acceptance/latest/pilot-readiness.json` | - |
| Provider Evidence | Pilot provider matrix covers Alpaca, Robinhood, Yahoo, and Wave 1 status | Gap | 0/2 | `docs/status/provider-validation-matrix.md` | terms: `Alpaca`, `Robinhood`, `Yahoo`, `Wave 1` |
| Provider Evidence | DK1 parity runbook names generated packet and run-date artifact requirements | Pass | 2/2 | `docs/status/evidence/dk1-pilot-parity-runbook.md` | - |
| Operator Sign-off | Kernel dashboard records signed packet-bound DK1 operator sign-off | Gap | 0/3 | `docs/status/kernel-readiness-dashboard.md` | terms: `operatorSignoff.status=signed`, `operatorSignoff.validForDk1Exit=true`, `ready-for-operator-review` |
| Automation | Provider validation, packet generation, and sign-off scripts are present | Pass | 2/2 | `scripts/dev/run-wave1-provider-validation.ps1`, `scripts/dev/generate-dk1-pilot-parity-packet.ps1`, `scripts/dev/prepare-dk1-operator-signoff.ps1` | - |
| Automation | DK1 packet and sign-off scripts have focused regression tests | Pass | 2/2 | `tests/scripts/test_generate_dk1_pilot_parity_packet.py`, `tests/scripts/test_prepare_dk1_operator_signoff.py` | - |
| Trading Readiness | Pilot posture is consumed by the shared trading readiness lane | Gap | 0/2 | `docs/plans/paper-trading-cockpit-reliability-sprint.md`, `src/Meridian.Ui.Shared/Services/Dk1TrustGateReadinessService.cs` | terms: `/api/workstation/trading/readiness`, `ProviderTrustGate` |

## Follow-up Queue

- **Pilot provider matrix covers Alpaca, Robinhood, Yahoo, and Wave 1 status**: Refresh the provider validation matrix before claiming DK1 pilot readiness.
- **Kernel dashboard records signed packet-bound DK1 operator sign-off**: Update the kernel dashboard with the current signed, packet-bound DK1 evidence.
- **Pilot posture is consumed by the shared trading readiness lane**: Keep the DK1 trust-gate handoff wired into the shared trading readiness contract.

## Pilot Acceptance Artifact

| Field | Value |
| --- | --- |
| Status | loaded |
| Path | `artifacts/pilot-acceptance/latest/pilot-readiness.json` |
| Generated | 2026-06-21T07:00:10.091633+00:00 |
| Stages ready | 8/8 |
| All stages ready | True |
| Evidence graph edges | 16 |
| Evidence graph self-edges | 0 |
| Dataset evidence | `dataset/pilot/golden-aapl-2026-04-11` |
| Paper session | `PAPER-20260621-a3a03b40` |
| Portfolio evidence | `pilot-strategy-3e2efe1-paper-portfolio` |
| Ledger evidence | `pilot-strategy-3e2efe1-paper-ledger` |
| Ledger artifact refs | 2 |
| Report pack | `77572824-41a6-44d0-b6c2-2c13a9cdda0d` |

### Ledger Artifact Refs

| Kind | Route | Path | Hash |
| --- | --- | --- | --- |
| ledger-journal | `/api/workstation/runs/pilot-paper-fece6a6e65244958bac75e30823cdd32/ledger/journal` | - | - |
| ledger-trial-balance | `/api/workstation/runs/pilot-paper-fece6a6e65244958bac75e30823cdd32/ledger/trial-balance` | - | - |

### Stage Gates

| Stage | W2-W4 claims | Status | Evidence | Validation |
| --- | --- | --- | --- | --- |
| Trusted provider and dataset evidence | W2, W3, W4 | Ready | `provider-evidence/dk1/unit-ready`, `dataset/pilot/golden-aapl-2026-04-11` | DK1 packet fixture and dataset references seeded by PilotAcceptanceHarnessTests. |
| Strategy run evidence retained | W3 | Ready | `pilot-backtest-fe171408a4ad40f7a6286d03e2cf175e`, `dataset/pilot/golden-aapl-2026-04-11` | Strategy briefing returned the retained backtest run and dataset evidence. |
| Baseline and candidate run comparison | W3 | Ready | `pilot-backtest-fe171408a4ad40f7a6286d03e2cf175e`, `pilot-paper-fece6a6e65244958bac75e30823cdd32` | Shared run comparison endpoint accepted the baseline and paper run IDs. |
| Paper promotion approval audit | W2, W3 | Ready | `pilot-backtest-fe171408a4ad40f7a6286d03e2cf175e`, `95000fd2846f4562917f2f2fb6a323c5` | PromotionService approved the backtest run with the required checklist. |
| Paper session replay verification | W2 | Ready | `PAPER-20260621-a3a03b40`, `audit-3dbfa192dc8a4e458b0cb988d55a8910` | PaperSessionPersistenceService replay verification returned consistent counts. |
| Portfolio and ledger continuity | W3, W4 | Ready | `pilot-paper-fece6a6e65244958bac75e30823cdd32`, `pilot-strategy-3e2efe1-paper-portfolio`, `pilot-strategy-3e2efe1-paper-ledger` | Run continuity detail confirmed portfolio, ledger, and reconciliation coverage. |
| Reconciliation run casework | W3, W4 | Ready | `bf9dab8631de42b4a5c1b2a9cd109761`, `pilot-paper-fece6a6e65244958bac75e30823cdd32` | Reconciliation run endpoint retained run-scoped reconciliation detail. |
| Governed report pack lineage | W4 | Ready | `casework/bf9dab8631de42b4a5c1b2a9cd109761`, `close-checklist/fe9f62aa-909b-471d-a3dc-e580db55326f/2026-04-11`, `approval/47d70b8e-a301-4cd6-8eb1-4e76c33f3eed/20260621070010`, +2 more | W4 acceptance passed with reconciliation casework, close checklist, report approval, publication, restatement readiness, and linked evidence-vault support. |

### Evidence Graph

| From | Relationship | To |
| --- | --- | --- |
| `provider-evidence/dk1/unit-ready` | supports-dataset | `dataset/pilot/golden-aapl-2026-04-11` |
| `dataset/pilot/golden-aapl-2026-04-11` | feeds-run | `pilot-backtest-fe171408a4ad40f7a6286d03e2cf175e` |
| `pilot-backtest-fe171408a4ad40f7a6286d03e2cf175e` | compared-to | `pilot-paper-fece6a6e65244958bac75e30823cdd32` |
| `pilot-backtest-fe171408a4ad40f7a6286d03e2cf175e` | approved-by | `95000fd2846f4562917f2f2fb6a323c5` |
| `95000fd2846f4562917f2f2fb6a323c5` | promotes-to-session | `PAPER-20260621-a3a03b40` |
| `PAPER-20260621-a3a03b40` | verified-by | `audit-3dbfa192dc8a4e458b0cb988d55a8910` |
| `pilot-paper-fece6a6e65244958bac75e30823cdd32` | produces-portfolio | `pilot-strategy-3e2efe1-paper-portfolio` |
| `pilot-paper-fece6a6e65244958bac75e30823cdd32` | books-ledger | `pilot-strategy-3e2efe1-paper-ledger` |
| `pilot-strategy-3e2efe1-paper-portfolio` | checked-against | `pilot-strategy-3e2efe1-paper-ledger` |
| `pilot-strategy-3e2efe1-paper-ledger` | reconciled-by | `bf9dab8631de42b4a5c1b2a9cd109761` |
| `pilot-backtest-fe171408a4ad40f7a6286d03e2cf175e` | summarized-by | `77572824-41a6-44d0-b6c2-2c13a9cdda0d` |
| `pilot-paper-fece6a6e65244958bac75e30823cdd32` | summarized-by | `77572824-41a6-44d0-b6c2-2c13a9cdda0d` |
| `bf9dab8631de42b4a5c1b2a9cd109761` | summarized-by | `77572824-41a6-44d0-b6c2-2c13a9cdda0d` |
| `casework/bf9dab8631de42b4a5c1b2a9cd109761` | closes-into | `close-checklist/fe9f62aa-909b-471d-a3dc-e580db55326f/2026-04-11` |
| `close-checklist/fe9f62aa-909b-471d-a3dc-e580db55326f/2026-04-11` | approved-by | `approval/47d70b8e-a301-4cd6-8eb1-4e76c33f3eed` |
| `approval/47d70b8e-a301-4cd6-8eb1-4e76c33f3eed` | published-by | `publication/47d70b8e-a301-4cd6-8eb1-4e76c33f3eed` |

### Artifact Follow-up

No stage blockers were recorded in the latest pilot artifact.

---

_This dashboard is auto-generated. Do not edit manually._
