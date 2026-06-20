# Evidence Continuity Dashboard

_Auto-generated from canonical JSON payload._
_Generated: 1970-01-01T00:00:00+00:00_
Data sources: `docs/status/evidence/dk1-pilot-parity-runbook.md`, `docs/status/kernel-readiness-dashboard.md`, `docs/status/contract-compatibility-matrix.md`, `scripts/dev/generate-dk1-pilot-parity-packet.ps1`, `scripts/generate_contract_review_packet.py`


Tracks whether DK1 and shared-contract evidence remain packet-bound, date-scoped, test-covered, and connected to current readiness dashboards.

## Summary

| Metric | Value |
| --- | ---: |
| Score | 85.7% |
| Passed checks | 5 |
| Gap checks | 1 |
| Missing evidence sources | 0 |
| Missing expected terms | 3 |

## Evidence Checks

| Category | Check | Status | Score | Evidence | Missing |
| --- | --- | --- | ---: | --- | --- |
| DK1 Evidence | DK1 runbook requires fresh date-stamped evidence artifacts | Pass | 3/3 | `docs/status/evidence/dk1-pilot-parity-runbook.md` | - |
| DK1 Evidence | Kernel dashboard links the active DK1 packet and sign-off evidence | Gap | 0/2 | `docs/status/kernel-readiness-dashboard.md` | terms: `artifacts/provider-validation/_automation/2026-04-27`, `dk1-pilot-parity-packet.json`, `dk1-operator-signoff.json` |
| Automation | DK1 packet generator and sign-off preparer are present | Pass | 2/2 | `scripts/dev/generate-dk1-pilot-parity-packet.ps1`, `scripts/dev/prepare-dk1-operator-signoff.ps1` | - |
| Automation | DK1 evidence packet tests guard packet identity and sign-off validation | Pass | 2/2 | `tests/scripts/test_generate_dk1_pilot_parity_packet.py`, `tests/scripts/test_prepare_dk1_operator_signoff.py` | - |
| Shared Contracts | Shared-contract review packet has a repeatable generator and owner-decision trail | Pass | 3/3 | `docs/status/contract-compatibility-matrix.md`, `scripts/generate_contract_review_packet.py` | - |
| Shared Contracts | Contract packet and compatibility gate scripts have regression tests | Pass | 2/2 | `tests/scripts/test_generate_contract_review_packet.py`, `tests/scripts/test_check_contract_compatibility_gate.py` | - |

## Follow-up Queue

- **Kernel dashboard links the active DK1 packet and sign-off evidence**: Update the readiness dashboard when the current DK1 evidence packet changes.

---

_This dashboard is auto-generated. Do not edit manually._
