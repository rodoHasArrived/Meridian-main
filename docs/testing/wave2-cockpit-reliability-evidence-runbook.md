# Wave 2 Cockpit Reliability Evidence Runbook

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-10-05
**Original scope:** 2026-05-18 reliability sprint
**Scope:** Paper-trading cockpit reliability sprint execution evidence for replay, session continuity,
risk/control explainability, and promotion traceability.

## Purpose

This runbook provides a repeatable operator/developer sequence to collect one date-stamped Wave 2
evidence packet that proves:

1. replay can move `verified -> stale -> re-verified`,
2. session state survives restart with order/ledger continuity,
3. risk/control evidence remains explainable, and
4. promotion decisions remain trace-complete and durable.

## Prerequisites and execution context

Use the SDK in [global.json](../../global.json), Bash or PowerShell, and the repository root for
the test command. For the manual sequence, prepare a designated non-production paper workspace,
a reviewed backtest, and an authenticated operator session with the necessary execution and
read permissions. Follow [operator preflight](../operators/preflight-checklist.md) to start the
host in one terminal and authenticate requests in another. Retain the same tenant, account, and
session scope across each request; a bare API key does not supply an operator session.

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
   - stale replay work item `paper-replay-stale-{normalizedSessionId}` exists (`normalizedSessionId`
     is lower-cased and non-alphanumeric separators are collapsed to `-`).
5. Re-run replay verification for the same session and capture the new audit ID.
6. Re-check readiness and confirm:
   - replay gate returns to `Ready`,
   - stale replay work item is cleared.
7. Query operator inbox (`GET /api/workstation/operator/inbox`) and confirm severity/tone alignment
   with readiness blockers plus actionable routing/sign-off detail for each blocking item.

## Evidence Packet Contents

For each run date (`YYYY-MM-DD`), archive:

- focused test output log,
- replay audit IDs before and after stale recovery,
- readiness payload snapshots for stale and recovered states,
- operator-inbox snapshot for blocker alignment and routing/sign-off detail evidence,
- short narrative stating whether all four Wave 2 reliability gates are pass/review/blocked.

## Expected results and recovery

The focused tests must execute and pass; record skips or missing tests as incomplete evidence.
The replay sequence must show the stale blocker appearing and then clearing after verification.
Capture restart/restore continuity and promotion/risk evidence separately before declaring all
four gates satisfied; replay recovery alone does not prove them.

For 401/403 responses, repair the operator login, role, or scope using preflight. For a failed
replay or restart, retain the session ID, logs, readiness payload, and audit references before
retrying. Reopen the same paper session and diagnose the mismatch; do not replace the evidence
with a new session or remove a blocker by disabling its gate. Mark unresolved criteria blocked.
