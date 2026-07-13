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
| Generated | 2026-07-13T23:36:15.0764703+00:00 |
| Stages ready | 8/8 |
| All stages ready | True |
| Evidence graph edges | 16 |
| Evidence graph self-edges | 0 |
| Dataset evidence | `dataset/pilot/golden-aapl-2026-04-11` |
| Paper session | `PAPER-20260713-aa220301` |
| Portfolio evidence | `pilot-strategy-4aaae27-paper-portfolio` |
| Ledger evidence | `pilot-strategy-4aaae27-paper-ledger` |
| Ledger artifact refs | 2 |
| Report pack | `a258484e-2b58-4109-bcfc-91594484ff3f` |

### Ledger Artifact Refs

| Kind | Route | Path | Hash |
| --- | --- | --- | --- |
| ledger-journal | `/api/workstation/runs/pilot-paper-011e65f483b7458b827ba528713c1621/ledger/journal` | - | - |
| ledger-trial-balance | `/api/workstation/runs/pilot-paper-011e65f483b7458b827ba528713c1621/ledger/trial-balance` | - | - |

### Stage Gates

| Stage | W2-W4 claims | Status | Evidence | Validation |
| --- | --- | --- | --- | --- |
| Trusted provider and dataset evidence | W2, W3, W4 | Ready | `provider-evidence/dk1/unit-ready`, `dataset/pilot/golden-aapl-2026-04-11` | DK1 packet fixture and dataset references seeded by PilotAcceptanceHarnessTests. |
| Strategy run evidence retained | W3 | Ready | `pilot-backtest-2430da60a7384c43b9c6bcab5b820484`, `dataset/pilot/golden-aapl-2026-04-11` | Strategy briefing returned the retained backtest run and dataset evidence. |
| Baseline and candidate run comparison | W3 | Ready | `pilot-backtest-2430da60a7384c43b9c6bcab5b820484`, `pilot-paper-011e65f483b7458b827ba528713c1621` | Shared run comparison endpoint accepted the baseline and paper run IDs. |
| Paper promotion approval audit | W2, W3 | Ready | `pilot-backtest-2430da60a7384c43b9c6bcab5b820484`, `ef273021435f4fbb807588af55c3b579` | PromotionService approved the backtest run with the required checklist. |
| Paper session replay verification | W2 | Ready | `PAPER-20260713-aa220301`, `audit-398af8daa8d44149bbd152f5e0e39c23` | PaperSessionPersistenceService replay verification returned consistent counts. |
| Portfolio and ledger continuity | W3, W4 | Ready | `pilot-paper-011e65f483b7458b827ba528713c1621`, `pilot-strategy-4aaae27-paper-portfolio`, `pilot-strategy-4aaae27-paper-ledger` | Run continuity detail confirmed portfolio, ledger, and reconciliation coverage. |
| Reconciliation run casework | W3, W4 | Ready | `080f650419a84edb8f2be33af14493d2`, `pilot-paper-011e65f483b7458b827ba528713c1621` | Reconciliation run endpoint retained run-scoped reconciliation detail. |
| Governed report pack lineage | W4 | Ready | `casework/080f650419a84edb8f2be33af14493d2`, `close-checklist/d53d57c3-1c3b-48eb-a222-283c82fde88a/2026-04-11`, `approval/fe26fe27-db09-4ac1-b706-188632c75952/20260713233615`, +2 more | W4 acceptance passed with reconciliation casework, close checklist, report approval, publication, restatement readiness, and linked evidence-vault support. |

### Evidence Graph

| From | Relationship | To |
| --- | --- | --- |
| `provider-evidence/dk1/unit-ready` | supports-dataset | `dataset/pilot/golden-aapl-2026-04-11` |
| `dataset/pilot/golden-aapl-2026-04-11` | feeds-run | `pilot-backtest-2430da60a7384c43b9c6bcab5b820484` |
| `pilot-backtest-2430da60a7384c43b9c6bcab5b820484` | compared-to | `pilot-paper-011e65f483b7458b827ba528713c1621` |
| `pilot-backtest-2430da60a7384c43b9c6bcab5b820484` | approved-by | `ef273021435f4fbb807588af55c3b579` |
| `ef273021435f4fbb807588af55c3b579` | promotes-to-session | `PAPER-20260713-aa220301` |
| `PAPER-20260713-aa220301` | verified-by | `audit-398af8daa8d44149bbd152f5e0e39c23` |
| `pilot-paper-011e65f483b7458b827ba528713c1621` | produces-portfolio | `pilot-strategy-4aaae27-paper-portfolio` |
| `pilot-paper-011e65f483b7458b827ba528713c1621` | books-ledger | `pilot-strategy-4aaae27-paper-ledger` |
| `pilot-strategy-4aaae27-paper-portfolio` | checked-against | `pilot-strategy-4aaae27-paper-ledger` |
| `pilot-strategy-4aaae27-paper-ledger` | reconciled-by | `080f650419a84edb8f2be33af14493d2` |
| `pilot-backtest-2430da60a7384c43b9c6bcab5b820484` | summarized-by | `a258484e-2b58-4109-bcfc-91594484ff3f` |
| `pilot-paper-011e65f483b7458b827ba528713c1621` | summarized-by | `a258484e-2b58-4109-bcfc-91594484ff3f` |
| `080f650419a84edb8f2be33af14493d2` | summarized-by | `a258484e-2b58-4109-bcfc-91594484ff3f` |
| `casework/080f650419a84edb8f2be33af14493d2` | closes-into | `close-checklist/d53d57c3-1c3b-48eb-a222-283c82fde88a/2026-04-11` |
| `close-checklist/d53d57c3-1c3b-48eb-a222-283c82fde88a/2026-04-11` | approved-by | `approval/fe26fe27-db09-4ac1-b706-188632c75952` |
| `approval/fe26fe27-db09-4ac1-b706-188632c75952` | published-by | `publication/fe26fe27-db09-4ac1-b706-188632c75952` |

### Artifact Follow-up

No stage blockers were recorded in the latest pilot artifact.

---

_This dashboard is auto-generated. Do not edit manually._
