# Wave 2 Paper Trading Cockpit Acceptance Tests

**Location:** `tests/Meridian.Tests/Ui/`
- `Wave2PaperTradingCockpitAcceptanceTests.cs` - Core acceptance gate validation
- `Wave2OperatorInboxAcceptanceTests.cs` - Operator inbox integration

**Last Updated:** 2026-05-08
**Status:** Comprehensive test suite for Wave 2 exit criteria

---

## Overview

These tests validate that the paper trading cockpit meets the four acceptance gates required for Wave 2 completion:

1. **Replay Confidence** - Operators can verify paper sessions with explicit evidence
2. **Session Persistence** - Sessions survive restart with full history intact
3. **Risk Auditability** - Control/risk outcomes are explainable from the cockpit
4. **Promotion Traceability** - Promotion decisions have complete audit chains

Plus, operator inbox integration tests validate that work items flow correctly and enable operators to triage workflows.

---

## Running the Tests

### Run all Wave 2 acceptance tests:
```bash
dotnet test tests/Meridian.Tests/Ui/Wave2PaperTradingCockpitAcceptanceTests.cs -c Release
dotnet test tests/Meridian.Tests/Ui/Wave2OperatorInboxAcceptanceTests.cs -c Release
```

### Run a specific acceptance gate:
```bash
# Replay confidence gate only
dotnet test tests/Meridian.Tests/Ui/Wave2PaperTradingCockpitAcceptanceTests.cs -c Release -k ReplayConfidenceGate

# Session persistence gate only
dotnet test tests/Meridian.Tests/Ui/Wave2PaperTradingCockpitAcceptanceTests.cs -c Release -k SessionPersistenceGate

# Risk auditability gate only
dotnet test tests/Meridian.Tests/Ui/Wave2PaperTradingCockpitAcceptanceTests.cs -c Release -k RiskAuditabilityGate

# Promotion traceability gate only
dotnet test tests/Meridian.Tests/Ui/Wave2PaperTradingCockpitAcceptanceTests.cs -c Release -k PromotionTraceabilityGate

# Operator inbox tests
dotnet test tests/Meridian.Tests/Ui/Wave2OperatorInboxAcceptanceTests.cs -c Release -k OperatorInbox
```

### Run with detailed output:
```bash
dotnet test tests/Meridian.Tests/Ui/Wave2PaperTradingCockpitAcceptanceTests.cs -c Release -v detailed
```

---

## Test Suite Structure

### Gate 1: Replay Confidence

**What it validates:**
- Operators can explicitly verify that a paper session's current state matches the replay
- Mismatch results include clear, actionable reasons (not just "pass/fail")
- Replay verification evidence includes counts, timestamps, and detailed mismatch analysis

**Tests:**
- `ReplayConfidenceGate_OperatorCanVerifySessionWithExplicitEvidence` - Happy path with explicit counts
- `ReplayConfidenceGate_ShowsExplicitMismatchReasonsWhenReplayFails` - Failure diagnosis with clear reasons
- `ReplayConfidenceGate_OperatorReadinessShowsReplayAsBlockingGateWhenMissing` - Readiness integration

**Key assertions:**
- Verification includes `VerifiedFilledCount`, `VerifiedOrderCount`, `VerifiedLedgerEntriesCount`
- Mismatch reasons are human-readable and actionable
- `LastVerifiedAt` timestamp is recorded for staleness tracking

### Gate 2: Session Persistence

**What it validates:**
- Paper sessions persist to durable storage
- After a restart (shutdown + reload), all session metadata, order history, fills, and ledger entries are intact
- Portfolio state can be reconstructed from persisted fills
- Ledger entries survive restart and can be audited later

**Tests:**
- `SessionPersistenceGate_SessionSurvivesRestartWithFullHistory` - Full session state survives restart
- `SessionPersistenceGate_PortfolioStateConsistentAfterRestart` - Portfolio snapshot matches post-restart
- `SessionPersistenceGate_LedgerContinuityAfterRestart` - Ledger entries are reconstructable

**Key assertions:**
- Session metadata (StrategyId, InitialCash, Symbols) survives restart
- OrderHistory and FillHistory contain expected entries
- Portfolio positions and cash balance match expected state
- Ledger entries exist and can be retrieved

### Gate 3: Risk Auditability

**What it validates:**
- Every material order/control outcome (reject, override, constraint) is recorded with actor/reason/scope
- Control evidence visible in the cockpit includes who decided, what scope, and why
- Risk evidence is not just summary copy but includes structured audit trail
- Manual overrides include explicit actor and approval reason

**Tests:**
- `RiskAuditabilityGate_ControlOutcomeExplainableFromCockpit` - Control decision has full context
- `RiskAuditabilityGate_ManualOverrideIncludesActor` - Override includes actor and reason
- `RiskAuditabilityGate_ReadinessExposesAuditControlState` - Cockpit readiness surfaces audit evidence

**Key assertions:**
- AuditEntry includes Actor, Reason, Scope, AuditId (stable reference)
- Recent evidence in readiness is not empty when controls exist
- Evidence is marked as "explained" only when all required fields present
- Work items reference audit IDs for navigation

### Gate 4: Promotion Traceability

**What it validates:**
- Every promotion decision (approve/reject) is durable and reconstructable
- Decision includes operator, decision reason/rationale, decision timestamp, and audit reference
- Promotion history can be retrieved after restart
- Decision chain is complete: source run → decision → audit reference

**Tests:**
- `PromotionTraceabilityGate_ApprovalHasCompleteAuditChain` - Approval includes full trace
- `PromotionTraceabilityGate_RejectionIncludesRationale` - Rejection includes complete context
- `PromotionTraceabilityGate_HistoryIsRecoverable` - Multiple decisions form complete history
- `PromotionTraceabilityGate_ReadinessRequiresCompleteDecisionChain` - Readiness gate enforces traceability

**Key assertions:**
- Approval.Decision == PromotionDecision.Approved
- Rejection.Decision == PromotionDecision.Rejected
- All decisions include RunId, OperatorId, Reason, AuditReference
- History retrieval returns multiple decisions in order

### Operator Inbox Integration Tests

**What it validates:**
- Trading readiness work items are aggregated correctly
- Work items have stable IDs (no random churn on repeated calls)
- Tone levels (Critical vs. Warning) reflect actual blockage
- Work items include navigation context (title, description, audit references)
- Overall readiness status reflects the gate states

**Key tests:**
- `OperatorInbox_AggregatesReadinessWorkItems` - Items are collected correctly
- `OperatorInbox_WorkItemIDsAreStableAcrossCalls` - IDs don't change between calls
- `OperatorInbox_NoRandomIDChurn` - IDs are deterministic, not random
- `OperatorInbox_BoundedToActionableItems` - Only blocking/warning items, not every status
- `OperatorInbox_OverallStatusReflectsBlockingGates` - Status correctly reflects gates

### End-to-End Scenarios

**What it validates:**
- Full workflow from backtest approval → paper session → execution → replay verification → readiness
- Operator can diagnose replay failure with explicit information
- All four gates work together coherently

**Tests:**
- `EndToEndScenario_BacktestToPaperWorkflow` - Full workflow integration
- `EndToEndScenario_OperatorCanDiagnoseReplayFailure` - Clear diagnostic output

---

## Expected Test Results

### When all gates pass:
```
Wave2PaperTradingCockpitAcceptanceTests: 16 tests
  ✓ All replay confidence tests pass
  ✓ All session persistence tests pass
  ✓ All risk auditability tests pass
  ✓ All promotion traceability tests pass
  ✓ Both end-to-end scenario tests pass

Wave2OperatorInboxAcceptanceTests: 13 tests
  ✓ All inbox aggregation tests pass
  ✓ All status/tone tests pass
  ✓ All stability/churn tests pass
```

### Typical implementation issues that tests catch:

**Replay Confidence failures:**
- Missing VerificationAuditId (no audit trail)
- MismatchReasons array is empty (no diagnostics)
- LastVerifiedAt is null (staleness tracking broken)

**Session Persistence failures:**
- Sessions empty after GetSessions() (store not wired)
- OrderHistory loses entries (persistence not applied)
- Portfolio positions mismatch original (fill replay broken)

**Risk Auditability failures:**
- AuditEntry missing Actor or Reason (incomplete audit)
- Evidence marked "explained" without all fields (validation broken)
- No work items generated for control events (readiness broken)

**Promotion Traceability failures:**
- History returns empty (store not implemented)
- AuditReference is null/empty (no trace linkage)
- Decision omits OperatorId or Reason (incomplete record)

**Operator Inbox failures:**
- Work item IDs change between calls (random generation)
- No work items appear (aggregation not implemented)
- Tone is "Ready" for blocking states (status logic broken)

---

## Integration with Wave 2 Exit Criteria

These tests directly validate the Wave 2 exit signal from [Roadmap Registry Summary](../roadmap/generated/ROADMAP_SUMMARY.md):

> "A strategy researched in backtest can be promoted to paper trading through one connected workstation workflow, with positions and fills visible through shared contracts."

- ✅ **Backtest promotion** - Promotion traceability gate
- ✅ **Paper trading** - Session persistence gate  
- ✅ **Connected workflow** - End-to-end scenario tests
- ✅ **Visible positions/fills** - Replay confidence gate
- ✅ **Shared contracts** - TradingOperatorReadinessDto integration

---

## Acceptance Gate Mapping

| Gate | Test Class | Primary Tests | Exit Signal |
|------|-----------|---|---|
| Replay Confidence | Wave2PaperTradingCockpitAcceptanceTests | ReplayConfidenceGate_* | Operators can verify paper sessions with explicit mismatch diagnostics |
| Session Persistence | Wave2PaperTradingCockpitAcceptanceTests | SessionPersistenceGate_* | Sessions survive restart with full order/fill/ledger history |
| Risk Auditability | Wave2PaperTradingCockpitAcceptanceTests | RiskAuditabilityGate_* | Control decisions include actor, reason, scope, and stable audit ID |
| Promotion Traceability | Wave2PaperTradingCockpitAcceptanceTests | PromotionTraceabilityGate_* | Promotions are durable with operator, reason, and complete decision chain |
| Operator Inbox | Wave2OperatorInboxAcceptanceTests | OperatorInbox_* | Work items aggregate correctly with stable IDs and actionable tone |

---

## Debugging Test Failures

### Enable detailed logging:
```bash
dotnet test tests/Meridian.Tests/Ui/Wave2PaperTradingCockpitAcceptanceTests.cs \
  -c Release --logger "console;verbosity=detailed" 2>&1 | less
```

### Inspect test artifacts:
Tests create temporary audit trail files in `Path.GetTempPath()/meridian-tests/[TestName]/`. 
These can be inspected for audit content:
```bash
ls -la /tmp/meridian-tests/
cat /tmp/meridian-tests/TestName/audit.jsonl
```

### Run single test:
```bash
dotnet test tests/Meridian.Tests/Ui/Wave2PaperTradingCockpitAcceptanceTests.cs \
  -c Release -k ReplayConfidenceGate_OperatorCanVerifySessionWithExplicitEvidence
```

---

## Maintenance

- Update these tests whenever the TradingOperatorReadinessDto contract changes
- Update when PaperSessionPersistenceService behavior changes
- Add new tests if new acceptance gates are introduced
- Keep test data realistic: use actual symbol names (AAPL, MSFT, etc.)
- Keep test names as the specification: the name IS the requirement

---

## Related Documentation

- [Roadmap Registry Summary](../roadmap/generated/ROADMAP_SUMMARY.md) - Wave 2 exit criteria and sequencing
- [`docs/plans/paper-trading-cockpit-reliability-sprint.md`](../plans/paper-trading-cockpit-reliability-sprint.md) - Detailed acceptance gate definitions
- [`archive/docs/plans/waves-2-4-operator-readiness-addendum.md`](../../archive/docs/plans/waves-2-4-operator-readiness-addendum.md) - Workstream ownership and dependencies
