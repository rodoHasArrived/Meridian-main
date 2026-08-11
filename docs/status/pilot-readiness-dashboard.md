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
| Generated | 2026-08-11T07:42:54.1827097+00:00 |
| Stages ready | 8/8 |
| All stages ready | True |
| Evidence graph edges | 16 |
| Evidence graph self-edges | 0 |
| Dataset evidence | `dataset/pilot/golden-aapl-2026-04-11` |
| Paper session | `PAPER-20260811-18209106` |
| Portfolio evidence | `pilot-strategy-72f4315-paper-portfolio` |
| Ledger evidence | `pilot-strategy-72f4315-paper-ledger` |
| Ledger artifact refs | 2 |
| Report pack | `adhoc-pilot-acceptance-tenant-audit-evidence-package-20260811074253794-20260411` |

### Ledger Artifact Refs

| Kind | Route | Path | Hash |
| --- | --- | --- | --- |
| ledger-journal | `/api/workstation/runs/pilot-paper-5d5be8418cf846c48db65d39a2898d08/ledger/journal` | - | - |
| ledger-trial-balance | `/api/workstation/runs/pilot-paper-5d5be8418cf846c48db65d39a2898d08/ledger/trial-balance` | - | - |

### Stage Gates

| Stage | W2-W4 claims | Status | Evidence | Validation |
| --- | --- | --- | --- | --- |
| Trusted provider and dataset evidence | W2, W3, W4 | Ready | `provider-evidence/dk1/unit-ready`, `dataset/pilot/golden-aapl-2026-04-11` | DK1 packet fixture and dataset references seeded by PilotAcceptanceHarnessTests. |
| Strategy run evidence retained | W3 | Ready | `pilot-backtest-a8944d5799f8445f81c784d80037d3a2`, `dataset/pilot/golden-aapl-2026-04-11` | Strategy briefing returned the retained backtest run and dataset evidence. |
| Baseline and candidate run comparison | W3 | Ready | `pilot-backtest-a8944d5799f8445f81c784d80037d3a2`, `pilot-paper-5d5be8418cf846c48db65d39a2898d08` | Shared run comparison endpoint accepted the baseline and paper run IDs. |
| Paper promotion approval audit | W2, W3 | Ready | `pilot-backtest-a8944d5799f8445f81c784d80037d3a2`, `8596ae97bfe84c4f84bd965c7738f467` | PromotionService approved the backtest run with the required checklist. |
| Paper session replay verification | W2 | Ready | `PAPER-20260811-18209106`, `audit-aaf53ca40d584e9390f2aecb52861d80` | PaperSessionPersistenceService replay verification returned consistent counts. |
| Portfolio and ledger continuity | W3, W4 | Ready | `pilot-paper-5d5be8418cf846c48db65d39a2898d08`, `pilot-strategy-72f4315-paper-portfolio`, `pilot-strategy-72f4315-paper-ledger` | Run continuity detail confirmed portfolio, ledger, and reconciliation coverage. |
| Reconciliation run casework | W3, W4 | Ready | `882e0fa3d708449b96a75a7e45eac438`, `pilot-paper-5d5be8418cf846c48db65d39a2898d08` | Reconciliation run endpoint retained run-scoped reconciliation detail. |
| Governed report pack lineage | W4 | Ready | `casework/882e0fa3d708449b96a75a7e45eac438`, `close-checklist/bf5128d8-5615-49d7-a2e0-7651fd8fd7ec/2026-04-11`, `reporting-approval/adhoc-pilot-acceptance-tenant-audit-evidence-package-20260811074253794-20260411/pilot-reporting-RunApproved-6`, +2 more | W4 acceptance passed with reconciliation casework, close checklist, report approval, publication, restatement readiness, and linked evidence-vault support. |

### Evidence Graph

| From | Relationship | To |
| --- | --- | --- |
| `provider-evidence/dk1/unit-ready` | supports-dataset | `dataset/pilot/golden-aapl-2026-04-11` |
| `dataset/pilot/golden-aapl-2026-04-11` | feeds-run | `pilot-backtest-a8944d5799f8445f81c784d80037d3a2` |
| `pilot-backtest-a8944d5799f8445f81c784d80037d3a2` | compared-to | `pilot-paper-5d5be8418cf846c48db65d39a2898d08` |
| `pilot-backtest-a8944d5799f8445f81c784d80037d3a2` | approved-by | `8596ae97bfe84c4f84bd965c7738f467` |
| `8596ae97bfe84c4f84bd965c7738f467` | promotes-to-session | `PAPER-20260811-18209106` |
| `PAPER-20260811-18209106` | verified-by | `audit-aaf53ca40d584e9390f2aecb52861d80` |
| `pilot-paper-5d5be8418cf846c48db65d39a2898d08` | produces-portfolio | `pilot-strategy-72f4315-paper-portfolio` |
| `pilot-paper-5d5be8418cf846c48db65d39a2898d08` | books-ledger | `pilot-strategy-72f4315-paper-ledger` |
| `pilot-strategy-72f4315-paper-portfolio` | checked-against | `pilot-strategy-72f4315-paper-ledger` |
| `pilot-strategy-72f4315-paper-ledger` | reconciled-by | `882e0fa3d708449b96a75a7e45eac438` |
| `pilot-backtest-a8944d5799f8445f81c784d80037d3a2` | summarized-by | `adhoc-pilot-acceptance-tenant-audit-evidence-package-20260811074253794-20260411` |
| `pilot-paper-5d5be8418cf846c48db65d39a2898d08` | summarized-by | `adhoc-pilot-acceptance-tenant-audit-evidence-package-20260811074253794-20260411` |
| `882e0fa3d708449b96a75a7e45eac438` | summarized-by | `adhoc-pilot-acceptance-tenant-audit-evidence-package-20260811074253794-20260411` |
| `casework/882e0fa3d708449b96a75a7e45eac438` | closes-into | `close-checklist/bf5128d8-5615-49d7-a2e0-7651fd8fd7ec/2026-04-11` |
| `close-checklist/bf5128d8-5615-49d7-a2e0-7651fd8fd7ec/2026-04-11` | approved-by | `reporting-approval/adhoc-pilot-acceptance-tenant-audit-evidence-package-20260811074253794-20260411` |
| `reporting-approval/adhoc-pilot-acceptance-tenant-audit-evidence-package-20260811074253794-20260411` | published-by | `reporting-release/adhoc-pilot-acceptance-tenant-audit-evidence-package-20260811074253794-20260411` |

### Artifact Follow-up

No stage blockers were recorded in the latest pilot artifact.

---

_This dashboard is auto-generated. Do not edit manually._
