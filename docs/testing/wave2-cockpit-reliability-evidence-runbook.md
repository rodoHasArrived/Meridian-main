# Wave 2 Cockpit Reliability Evidence Runbook

**Last Reviewed:** 2026-05-18  
**Scope:** Paper-trading cockpit reliability sprint execution evidence for replay, session continuity,
risk/control explainability, and promotion traceability.

## Purpose

This runbook provides a repeatable operator/developer sequence to collect one date-stamped Wave 2
evidence packet that proves:

1. replay can move `verified -> stale -> re-verified`,
2. session state survives restart with order/ledger continuity,
3. risk/control evidence remains explainable, and
4. promotion decisions remain trace-complete and durable.

## Required Test Evidence (Automated)

Run the focused reliability slice:

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj --filter "FullyQualifiedName~SessionContinuity_CreateRestartVerifyClose_PreservesScopeAndHistoryAcrossFlow|FullyQualifiedName~MapWorkstationEndpoints_TradingReadiness_ShouldRequireReplayRefreshWhenSessionChangesAfterVerification|FullyQualifiedName~MapWorkstationEndpoints_TradingReadiness_ShouldFlagUnexplainedRiskControlAuditEvidence|FullyQualifiedName~MapWorkstationEndpoints_TradingReadiness_ShouldNotUseStalePromotionHistoryForLatestRun"
```

This command covers:

- session `create -> restart/restore -> verify -> close`,
- replay stale detection and recovery gate behavior,
- risk/control explainability enforcement (`actor/scope/reason`),
- promotion trace gate audit-reference continuity.

## Operator Evidence Sequence (Manual/API)

1. Start local host and create a paper session from a reviewed backtest.
2. Verify replay once (`GET /api/execution/sessions/{sessionId}/replay`) and capture the audit ID.
3. Add one new order/fill event without re-verifying replay.
4. Check readiness (`GET /api/workstation/trading/readiness`) and confirm:
   - replay gate falls to `ReviewRequired`,
   - stale replay work item `paper-replay-stale-{sessionId}` exists.
5. Re-run replay verification for the same session and capture the new audit ID.
6. Re-check readiness and confirm:
   - replay gate returns to `Ready`,
   - stale replay work item is cleared.
7. Query operator inbox (`GET /api/workstation/operator/inbox`) and confirm severity/tone alignment
   with readiness blockers.

## Evidence Packet Contents

For each run date (`YYYY-MM-DD`), archive:

- focused test output log,
- replay audit IDs before and after stale recovery,
- readiness payload snapshots for stale and recovered states,
- operator-inbox snapshot for blocker alignment,
- short narrative stating whether all four Wave 2 reliability gates are pass/review/blocked.
