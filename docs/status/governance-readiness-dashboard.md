# Governance Readiness Dashboard

_Auto-generated from canonical JSON payload._
_Generated: 1970-01-01T00:00:00+00:00_
Data sources: `docs/status/kernel-readiness-dashboard.md`, `docs/status/contract-compatibility-matrix.md`, `docs/status/FEATURE_INVENTORY.md`, `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs`, `tests/Meridian.Tests/Ui/WorkstationEndpointsTests.cs`


Tracks whether DK2 governance, reconciliation, and shared-contract controls have current status evidence, route support, and validation coverage.

## Summary

| Metric | Value |
| --- | ---: |
| Score | 46.7% |
| Passed checks | 3 |
| Gap checks | 3 |
| Missing evidence sources | 0 |
| Missing expected terms | 9 |

## Evidence Checks

| Category | Check | Status | Score | Evidence | Missing |
| --- | --- | --- | ---: | --- | --- |
| Readiness Board | Kernel dashboard tracks reconciliation and governance DK2 readiness | Gap | 0/3 | `docs/status/kernel-readiness-dashboard.md` | terms: `Reconciliation + governance`, `Governance/Fund Ops owner`, `Operator Sign-off` |
| Shared Contracts | Contract compatibility matrix requires review packets and owner decisions | Gap | 0/3 | `docs/status/contract-compatibility-matrix.md` | terms: `Contract review packet`, `Owner decision`, `migration notes` |
| Governance Operations | Feature inventory describes reconciliation calibration and sign-off posture | Gap | 0/2 | `docs/status/FEATURE_INVENTORY.md` | terms: `calibration-summary`, `tolerance-profile posture`, `required sign-off role` |
| Governance Operations | Workstation endpoints expose governance break queue and calibration routes | Pass | 3/3 | `src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.cs` | - |
| Validation | Endpoint tests cover governance break queue and calibration readiness | Pass | 2/2 | `tests/Meridian.Tests/Ui/WorkstationEndpointsTests.cs` | - |
| Status | Provider and contract status dashboards remain present for governance reviews | Pass | 2/2 | `docs/status/provider-validation-matrix.md`, `docs/status/contract-compatibility-matrix.md`, `docs/status/kernel-readiness-dashboard.md` | - |

## Follow-up Queue

- **Kernel dashboard tracks reconciliation and governance DK2 readiness**: Refresh the kernel dashboard governance row before claiming DK2 readiness.
- **Contract compatibility matrix requires review packets and owner decisions**: Record contract-review packet evidence and owner decisions in the matrix.
- **Feature inventory describes reconciliation calibration and sign-off posture**: Update the feature inventory with the current reconciliation governance scope.

---

_This dashboard is auto-generated. Do not edit manually._
