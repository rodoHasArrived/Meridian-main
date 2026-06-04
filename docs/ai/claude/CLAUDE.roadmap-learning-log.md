# Roadmap Learning Log

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-06-04

Running log of roadmap items studied on the `claude/continue-roadmap-learning-*` branches. Each entry captures what was read, what the current code evidence actually says, and what the next learning session should pick up.

Source-of-truth for active wave status is [`docs/roadmap/README.md`](../../roadmap/README.md) plus the generated roadmap register in [`docs/roadmap/generated/`](../../roadmap/generated/); this file only records what was learned at the time, not current planning commitments.

---

## Entry 1 — Wave 2: Web paper-trading cockpit completion

**Studied on:** 2026-04-22
**Branch:** `claude/continue-roadmap-learning-Jr1rw`
**Why this was "next" on 2026-04-22:** Wave 1 was repo-closed (Done), and Wave 2 was then the earliest wave still treated as in progress. As of the 2026-05-27 evidence slice, W2 is closed as Done; keep this entry as historical learning context, not current roadmap state.

### Primary sources read

- [`docs/roadmap/data/*.yml`](../../roadmap/data/) for wave and gate records for the corresponding scope
- [`docs/roadmap/generated/ROADMAP_SUMMARY.md`](../../roadmap/generated/ROADMAP_SUMMARY.md) for the generated historical summary
- Legacy context: [`archive/docs/plans/paper-trading-cockpit-reliability-sprint.md`](../../../archive/docs/plans/paper-trading-cockpit-reliability-sprint.md)

### Acceptance gate map (sprint → code)

The sprint defines four acceptance gates. Each maps to concrete seams that already exist in the repo:

| Gate | Current seam | File |
| --- | --- | --- |
| Replay confidence | `PaperSessionPersistenceService.VerifyReplayAsync` returning `PaperSessionReplayVerificationDto` with `ComparedFillCount` / `ComparedOrderCount` / `ComparedLedgerEntryCount` / `LastPersistedFillAt` / `VerificationAuditId` | `src/Meridian.Execution/Services/PaperSessionPersistenceService.cs:385-390, 677-682` |
| Session persistence | `PaperSessionPersistenceService.InitialiseAsync` + session endpoints | `src/Meridian.Execution/Services/PaperSessionPersistenceService.cs`, `src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs` |
| Risk auditability | `OrderManagementSystem` + `ExecutionAuditTrailService` + `ExecutionOperatorControlService`; manual-override routes wired | `src/Meridian.Ui.Shared/Endpoints/ExecutionEndpoints.cs:283, 308`, `src/Meridian.Contracts/Api/UiApiRoutes.cs:433-434` |
| Promotion traceability | `PromotionService` + `IPromotionRecordStore` / `JsonlPromotionRecordStore`; `StrategyPromotionRecord` already carries `SourceRunId`, `TargetRunId`, `ApprovalReason`, `ReviewNotes`, `Decision`, `AuditReference`, `ApprovedBy`, `ManualOverrideId` | `src/Meridian.Strategies/Services/PromotionService.cs`, `src/Meridian.Strategies/Storage/IPromotionRecordStore.cs`, `src/Meridian.Strategies/Promotions/BacktestToLivePromoter.cs:96-113` |

Frontend has also caught up: `ApprovePromotionRequest` in `src/Meridian.Ui/dashboard/src/lib/api.ts:108-114` now carries `approvedBy`, `approvalReason`, `reviewNotes`, and optional `manualOverrideId` — matching the sprint's "full operator context" requirement.

### Observed drift from the sprint blueprint

One concrete delivery gap surfaced while cross-checking:

- `PromotionService` has a required `IPromotionRecordStore` constructor dependency (`src/Meridian.Strategies/Services/PromotionService.cs:26, 31, 39`) but no production DI registration binds that interface. `UiServer.cs:110` registers `PromotionService` as a singleton, yet there is no `services.AddSingleton<IPromotionRecordStore, JsonlPromotionRecordStore>()` anywhere under `src/`. The only bindings live in test composition (`tests/Meridian.Tests/Ui/ExecutionWriteEndpointsTests.cs:526`, `tests/Meridian.Tests/Strategies/PromotionServiceTests.cs:214`, `tests/Meridian.Tests/Strategies/PromotionServiceLiveGovernanceTests.cs:256`).
- Runtime impact: any `POST /api/promotion/approve` or `/api/promotion/reject` call against the real host will fail DI resolution when `PromotionService` is resolved by `PromotionEndpoints`. This directly invalidates the Wave 2 "promotion traceability" gate because history cannot be durably appended in production.

This is not a blueprint ambiguity — the sprint explicitly calls out in §"Current State" that `PromotionService.GetPromotionHistory()` was in-memory. The interface and JSONL store have since landed, but the composition wiring step is missing.

### Open sprint questions still unresolved in code

From §"Open Questions" of the sprint blueprint:

1. Replay gate scope: `VerifyReplayAsync` already reports fill/order/ledger compared counts, but there is no place in the cockpit or endpoint contract that *blocks* on order-history or ledger divergence — only the numbers are returned.
2. Where durable promotion records live: the repo chose the strategies layer (`JsonlPromotionRecordStore`), not `ExecutionAuditTrailService`. The sprint left this ambiguous; the code has now taken a position, but it is not reflected back in the sprint doc.
3. Updated 2026-04-25: `GET /api/workstation/trading/readiness` now provides the aggregated cockpit-readiness lane, and `trading-screen.tsx` can consume the shared readiness payload from `/api/workstation/trading` while the focused routes remain available for drill-in and write actions.

### Takeaways for future work on this branch

- Historical 2026-04-22 note: the Wave 2 seam inventory was essentially complete and the sprint was in its proof phase. Current 2026-05-27 status is Done with evidence tracked from the W2 acceptance slice.
- The highest-value small fix surfaced by this learning pass is the missing `IPromotionRecordStore` registration in `UiServer.cs`. That would unblock the promotion-traceability gate at runtime without any contract changes.
- Historical 2026-04-22 follow-up: the next learning session was to pick up Wave 3 shared platform interop. Current 2026-05-27 status is Done for the W3 shared run / portfolio / ledger continuity baseline; W4 close, report, reconciliation, and evidence governance remains the active gate.

### Follow-up: implementations landed on this branch

The two gaps observed above were addressed in the same branch:

1. **`IPromotionRecordStore` DI registration:** added in `src/Meridian/UiServer.cs` (lines around 110-115). `JsonlPromotionRecordStore` is now bound as a singleton with its history file under `{contentRootPath}/data/promotions/promotion-history.jsonl`, mirroring the pattern used by `JsonlFilePaperSessionStore`. Promotion approval and rejection now persist across host restarts.
2. **Desktop status bar reliability:** `StatusBarViewModel` previously had three observable defects — throughput formatting overflowed past 1M ev/s, the dropped-events badge never appeared, and the "Degraded" status check compared two zero defaults. The view model now derives backend status from the real `DropRate` signal, surfaces a per-tick delta of dropped events through the existing badge, and formats throughput across K/M tiers. The XAML adds a backend-status text and tooltip describing the live snapshot. Pure helpers are covered by `StatusBarViewModelTests`.
