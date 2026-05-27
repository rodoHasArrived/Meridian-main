# Wave 2 Acceptance Gate Checklist

**Last Updated:** 2026-05-08
**Target Exit Date:** 2026-05-29

This document defines the exact pass/fail criteria for each Wave 2 acceptance gate.

---

## Gate 1: Replay Confidence ✓ TESTABLE

**Requirement:** Operators can verify a selected paper session and see explicit evidence of whether replay matches current state.

### Pass Criteria:
- [ ] `PaperSessionPersistenceService.VerifyReplayAsync()` returns `PaperSessionReplayVerificationDto` with:
  - [ ] `SessionId` (stable reference to session)
  - [ ] `IsConsistent` (boolean: match or mismatch)
  - [ ] `VerifiedFilledCount` (explicit count of fills found)
  - [ ] `VerifiedOrderCount` (explicit count of orders found)
  - [ ] `VerifiedLedgerEntriesCount` (explicit count of ledger entries)
  - [ ] `LastVerifiedAt` (timestamp of verification)
  - [ ] `MismatchReasons` (array of human-readable reasons if `!IsConsistent`)
  - [ ] `VerificationAuditId` (stable audit reference)

- [ ] When `IsConsistent == true`:
  - [ ] All counts match expected values
  - [ ] MismatchReasons array is empty
  - [ ] Cockpit shows "Replay Verified ✓"

- [ ] When `IsConsistent == false`:
  - [ ] MismatchReasons is NOT empty (must have explicit reason)
  - [ ] Each reason is actionable (e.g., "Expected 3 fills, found 1")
  - [ ] Cockpit shows "Replay Verification Failed" with reasons
  - [ ] Operator can see what specifically doesn't match

- [ ] Staleness detection:
  - [ ] If fills/orders added after last verification, `IsReplayCoverageStale()` returns true
  - [ ] Cockpit shows warning about stale coverage

### Test Coverage:
```
ReplayConfidenceGate_OperatorCanVerifySessionWithExplicitEvidence
ReplayConfidenceGate_ShowsExplicitMismatchReasonsWhenReplayFails
ReplayConfidenceGate_OperatorReadinessShowsReplayAsBlockingGateWhenMissing
```

### Status: **🔄 Partially Implemented**
- ✅ `PaperSessionPersistenceService.VerifyReplayAsync()` exists
- ✅ Core replay comparison logic is present
- ⚠️ Need to verify explicit mismatch reasons are returned
- ⚠️ Need to verify staleness detection works

---

## Gate 2: Session Persistence ✓ TESTABLE

**Requirement:** A paper session can be created, restored after restart, verified, and closed without losing symbol scope, order history, or ledger continuity.

### Pass Criteria:
- [ ] Session creation persists to durable store:
  - [ ] `CreateSessionAsync()` records: SessionId, StrategyId, StrategyName, InitialCash, Symbols, CreatedAt
  - [ ] Store is called before returning (not fire-and-forget)

- [ ] Session restore after restart:
  - [ ] `InitialiseAsync()` loads all sessions from store
  - [ ] Portfolio state reconstructed by replaying fill log in order
  - [ ] OrderHistory fully restored (all orders visible)
  - [ ] FillHistory fully restored (all fills visible)
  - [ ] Symbols list intact

- [ ] Portfolio consistency:
  - [ ] Cash balance after fills = InitialCash - (sum of fill costs)
  - [ ] Positions correctly aggregated from fills
  - [ ] Long/short quantities correct for each symbol

- [ ] Ledger reconstruction:
  - [ ] Ledger entries reconstructed from persisted journal
  - [ ] Entries accessible via `LedgerReadService`
  - [ ] Trial balance matches portfolio state

- [ ] Order history durability:
  - [ ] Order status, timestamps, and fill references persist
  - [ ] Orders can be retrieved after restart
  - [ ] Order-fill relationships intact

### Test Coverage:
```
SessionPersistenceGate_SessionSurvivesRestartWithFullHistory
SessionPersistenceGate_PortfolioStateConsistentAfterRestart
SessionPersistenceGate_LedgerContinuityAfterRestart
```

### Status: **🔄 Partially Implemented**
- ✅ `PaperSessionPersistenceService.InitialiseAsync()` exists
- ✅ Portfolio replay from fills is implemented
- ⚠️ Store integration needs verification
- ⚠️ Ledger reconstruction needs testing

---

## Gate 3: Risk Auditability ✓ TESTABLE

**Requirement:** Every material order/control outcome is explainable by audited evidence visible from the cockpit.

### Pass Criteria:
- [ ] Execution audit trail captures all control decisions:
  - [ ] Category (Order, Override, Circuit, Manual)
  - [ ] Action (Submitted, Rejected, Approved, etc.)
  - [ ] Outcome (Accepted, RejectedByControl, Approved, Denied)
  - [ ] Actor (who decided: risk-engine, operator, algo, etc.)
  - [ ] Reason (why: position-limit-exceeded, buying-power-constraint, etc.)
  - [ ] Scope (what: symbol, quantity, order ID)
  - [ ] AuditId (stable unique reference)
  - [ ] Timestamp (when decision was made)

- [ ] Control evidence must be complete:
  - [ ] All required fields populated (no nulls)
  - [ ] Actor field non-empty (who made the decision)
  - [ ] Reason field non-empty (why)
  - [ ] Scope field non-empty (what scope did it apply to)

- [ ] Cockpit visibility:
  - [ ] `TradingOperatorReadinessService` returns `Controls` object with:
    - [ ] `RecentEvidence[]` (list of audit entries)
    - [ ] `IsExplained` flag on each evidence item
    - [ ] `MissingFields[]` if incomplete
    - [ ] `CircuitBreakerOpen` status
    - [ ] `ManualOverrideCount`
    - [ ] `UnexplainedEvidenceCount`
    - [ ] `ExplainabilityWarnings[]`

- [ ] Work items generated for unexplained evidence:
  - [ ] `execution-evidence-incomplete` work item created
  - [ ] Tone is Warning (not silent failure)
  - [ ] References the audit ID of the unexplained entry

- [ ] Manual override includes:
  - [ ] Actor (fund-manager, risk-ops, etc.)
  - [ ] Reason (client-request, market-condition, etc.)
  - [ ] Override ID (stable reference)
  - [ ] Timestamp

### Test Coverage:
```
RiskAuditabilityGate_ControlOutcomeExplainableFromCockpit
RiskAuditabilityGate_ManualOverrideIncludesActor
RiskAuditabilityGate_ReadinessExposesAuditControlState
```

### Status: **🔄 Partially Implemented**
- ✅ `ExecutionAuditTrailService` exists
- ✅ Audit recording is wired
- ⚠️ Need to verify all required fields are populated
- ⚠️ Need to verify cockpit exposes evidence properly

---

## Gate 4: Promotion Traceability ✓ TESTABLE

**Requirement:** Every promotion decision yields one durable trace chain from source run to target run with operator, rationale, override, decision state, and audit reference.

### Pass Criteria:
- [ ] Approval decision record includes:
  - [ ] RunId (source backtest run)
  - [ ] OperatorId (who approved)
  - [ ] Reason (approval rationale - why is it approved)
  - [ ] Decision = `PromotionDecision.Approved`
  - [ ] DecidedAt (timestamp)
  - [ ] AuditReference (stable audit ID)
  - [ ] ManualOverrideId (if override used)

- [ ] Rejection decision record includes:
  - [ ] RunId
  - [ ] OperatorId
  - [ ] Reason (rejection rationale - why rejected)
  - [ ] Decision = `PromotionDecision.Rejected`
  - [ ] DecidedAt
  - [ ] AuditReference
  - [ ] (No paper session created on reject)

- [ ] Durable storage:
  - [ ] `PromotionService.ApprovePromotionAsync()` persists approval durably
  - [ ] `PromotionService.RejectPromotionAsync()` persists rejection durably
  - [ ] Decisions survive process restart

- [ ] History recovery:
  - [ ] `GetPromotionHistoryAsync(runId)` returns all prior decisions for that run
  - [ ] Decisions returned in chronological order
  - [ ] Multiple decisions form complete decision chain

- [ ] Readiness integration:
  - [ ] `TradingOperatorReadinessDto` includes promotion state
  - [ ] `Promotion` object has:
    - [ ] `Decision` (Approved, Rejected, Pending)
    - [ ] `DecisionAuditId` (links to audit trail)
    - [ ] `LastDecisionAt` (timestamp)
    - [ ] `PendingApprovalItems[]` if waiting for approval

- [ ] Work items for promotion state:
  - [ ] `promotion-decision-missing` if no decision yet
  - [ ] Work item includes run ID for navigation
  - [ ] Work item references audit ID

### Test Coverage:
```
PromotionTraceabilityGate_ApprovalHasCompleteAuditChain
PromotionTraceabilityGate_RejectionIncludesRationale
PromotionTraceabilityGate_HistoryIsRecoverable
PromotionTraceabilityGate_ReadinessRequiresCompleteDecisionChain
```

### Status: **🔄 Partially Implemented**
- ✅ `PromotionService` exists
- ✅ Approval/rejection endpoints wired
- ⚠️ Need to verify durable storage
- ⚠️ Need to verify history recovery works

---

## Operator Inbox Integration ✓ TESTABLE

**Requirement:** Operator work items aggregate from all readiness gates and enable navigation to concrete workflows.

### Pass Criteria:
- [ ] Work item aggregation:
  - [ ] Paper session work items
  - [ ] Replay verification work items
  - [ ] Risk/control work items
  - [ ] Promotion work items
  - [ ] All in one `/api/workstation/operator/inbox` endpoint

- [ ] Stable work item IDs:
  - [ ] IDs are deterministic (same call = same IDs)
  - [ ] No random churn (repeated calls show same IDs)
  - [ ] Format: lowercase-kebab-case (e.g., `paper-session-missing`, `replay-mismatch-SESSION-123`)

- [ ] Tone levels reflect actual blockage:
  - [ ] Critical = blocks paper operation
  - [ ] Warning = review required before operation
  - [ ] Ready = no action needed

- [ ] Work item content:
  - [ ] WorkItemId (stable, scoped reference)
  - [ ] Title (brief action: "Paper Session Missing")
  - [ ] Description (context: "Start or restore a paper session...")
  - [ ] Kind (PaperReplay, ExecutionControl, etc.)
  - [ ] Tone (Critical, Warning)
  - [ ] Scope (session ID, order ID, etc., if applicable)
  - [ ] AuditReference (links to audit trail if available)

- [ ] Status aggregation:
  - [ ] OverallStatus = Blocked if ANY Critical items
  - [ ] OverallStatus = ReviewRequired if only Warning items
  - [ ] OverallStatus = Ready if no Critical/Warning items
  - [ ] ReadyForPaperOperation boolean reflects OverallStatus

- [ ] Bounded to actionable items only:
  - [ ] Ready items do NOT generate work items
  - [ ] Only Critical/Warning items appear in inbox
  - [ ] No duplicate work items for same issue

### Test Coverage:
```
OperatorInbox_AggregatesReadinessWorkItems
OperatorInbox_PaperSessionMissingIsBlockingWorkItem
OperatorInbox_ReplayVerificationRequiredShowsAsWarning
OperatorInbox_WorkItemsHaveNavigationHints
OperatorInbox_WorkItemIDsAreStableAcrossCalls
OperatorInbox_NoRandomIDChurn
OperatorInbox_BoundedToActionableItems
OperatorInbox_OverallStatusReflectsBlockingGates
```

### Status: **🔄 Partially Implemented**
- ✅ `/api/workstation/operator/inbox` endpoint exists
- ✅ `TradingOperatorReadinessDto.WorkItems` structure exists
- ⚠️ Need to verify all gates contribute work items
- ⚠️ Need to verify ID stability

---

## End-to-End Wave 2 Acceptance Signal

**All gates pass when:**

1. ✅ Replay Confidence: Operators see explicit verification evidence
2. ✅ Session Persistence: Sessions survive restart
3. ✅ Risk Auditability: Control decisions are explained
4. ✅ Promotion Traceability: Approvals are durable and traceable
5. ✅ Operator Inbox: Work items enable triage and navigation

**Wave 2 Exit Statement:**
> "A strategy researched in backtest can be promoted to paper trading through one connected workstation workflow, with positions and fills visible through shared contracts."

---

## Implementation Status Summary

| Gate | Status | Blocker | Next Action |
|------|--------|---------|------------|
| Replay Confidence | 🟡 Partial | Verify explicit mismatch reasons | Run `ReplayConfidenceGate_*` tests |
| Session Persistence | 🟡 Partial | Verify store integration | Run `SessionPersistenceGate_*` tests |
| Risk Auditability | 🟡 Partial | Verify complete audit record | Run `RiskAuditabilityGate_*` tests |
| Promotion Traceability | 🟡 Partial | Verify durable storage | Run `PromotionTraceabilityGate_*` tests |
| Operator Inbox | 🟡 Partial | Verify ID stability | Run `OperatorInbox_*` tests |

---

## How to Use This Checklist

1. **For developers:** Run the failing tests to understand what's missing
2. **For reviewers:** Use this checklist to validate PR completeness
3. **For QA:** Use this as the acceptance test spec
4. **For stakeholders:** This is the definition of Wave 2 Done

---

## Related Documents

- [`docs/testing/WAVE2_ACCEPTANCE_TESTS.md`](WAVE2_ACCEPTANCE_TESTS.md) - Full test suite documentation
- [`docs/plans/paper-trading-cockpit-reliability-sprint.md`](../plans/paper-trading-cockpit-reliability-sprint.md) - Original gate definitions
- [`docs/status/ROADMAP.md`](../status/ROADMAP.md) - Wave 2 roadmap context
