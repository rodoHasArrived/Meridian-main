# Paper Replay Reliability Dashboard

_Auto-generated from canonical JSON payload._
_Generated: 2026-06-19T01:10:40.761310+00:00_
Data sources: `docs/plans/paper-trading-cockpit-reliability-sprint.md`, `docs/status/FEATURE_INVENTORY.md`, `src/Meridian.Execution/Services/PaperSessionPersistenceService.cs`, `src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs`, `tests/Meridian.Tests/Ui/WorkstationEndpointsTests.cs`


Tracks whether paper-session replay verification, stale-readiness detection, and shared trading readiness evidence remain wired through docs, services, and tests.

## Summary

| Metric | Value |
| --- | ---: |
| Score | 46.7% |
| Passed checks | 3 |
| Gap checks | 3 |
| Missing evidence sources | 0 |
| Missing expected terms | 7 |

## Evidence Checks

| Category | Check | Status | Score | Evidence | Missing |
| --- | --- | --- | ---: | --- | --- |
| Acceptance Contract | Reliability sprint records replay evidence and stale-readiness semantics | Gap | 0/3 | `docs/plans/paper-trading-cockpit-reliability-sprint.md` | terms: `replay verification`, `paper-replay-stale`, `fill, order, or ledger-entry counts` |
| Acceptance Contract | Shared trading readiness endpoint remains the replay acceptance lane | Gap | 0/3 | `docs/plans/paper-trading-cockpit-reliability-sprint.md`, `src/Meridian.Ui.Shared/Services/TradingOperatorReadinessService.cs` | terms: `/api/workstation/trading/readiness` |
| API Surface | Execution endpoints expose replay verification for sessions | Pass | 2/2 | `src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs` | - |
| Durability | Paper session persistence can verify replay from durable fills, orders, and ledger state | Pass | 3/3 | `src/Meridian.Execution/Services/PaperSessionPersistenceService.cs` | - |
| Status | Feature inventory describes paper cockpit replay and stale-coverage posture | Gap | 0/2 | `docs/status/FEATURE_INVENTORY.md` | terms: `Paper-trading cockpit`, `replay-audit metadata`, `stale-coverage detection` |
| Validation | Workstation endpoint tests cover replay readiness and operator inbox routing | Pass | 2/2 | `tests/Meridian.Tests/Ui/WorkstationEndpointsTests.cs` | - |

## Follow-up Queue

- **Reliability sprint records replay evidence and stale-readiness semantics**: Refresh the reliability sprint plan with current replay verification semantics.
- **Shared trading readiness endpoint remains the replay acceptance lane**: Route replay posture through the shared trading readiness service and document it.
- **Feature inventory describes paper cockpit replay and stale-coverage posture**: Update the feature inventory when replay reliability semantics change.

---

_This dashboard is auto-generated. Do not edit manually._
