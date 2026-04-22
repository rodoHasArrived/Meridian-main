# Roadmap Learning Log

Running log of roadmap items studied on the `claude/continue-roadmap-learning-*` branches. Each entry captures what was read, what the current code evidence actually says, and what the next learning session should pick up.

Source of truth for wave status is [`docs/status/PROGRAM_STATE.md`](../../status/PROGRAM_STATE.md); this file only records what was learned, not what is planned.

---

## Entry 1 — Wave 2: Web paper-trading cockpit completion

**Studied on:** 2026-04-22
**Branch:** `claude/continue-roadmap-learning-Jr1rw`
**Why this is "next":** Wave 1 is repo-closed (Done). Wave 2 is the earliest wave still `In Progress` in `PROGRAM_STATE.md`, owned by Trading Workstation, with target 2026-05-29.

### Primary sources read

- [`docs/status/ROADMAP.md`](../../status/ROADMAP.md) §"Wave 2: Web paper-trading cockpit completion" and §"Release Gates"
- [`docs/plans/paper-trading-cockpit-reliability-sprint.md`](../../plans/paper-trading-cockpit-reliability-sprint.md) (the full sprint blueprint)

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
3. No single aggregated cockpit-readiness endpoint has been added; `trading-screen.tsx` still composes sessions, controls, replay, and promotion state client-side via separate routes.

### Takeaways for future work on this branch

- The Wave 2 seam inventory is essentially complete; the sprint is now in its "prove it" phase, not its "build it" phase.
- The highest-value small fix surfaced by this learning pass is the missing `IPromotionRecordStore` registration in `UiServer.cs`. That would unblock the promotion-traceability gate at runtime without any contract changes.
- Next learning session should pick up Wave 3 (Shared Platform Interop, In Progress, target 2026-06-26) using [`docs/plans/brokerage-portfolio-sync-blueprint.md`](../../plans/brokerage-portfolio-sync-blueprint.md) as the primary reference, and verify how far the shared run/portfolio/ledger DTOs in `src/Meridian.Contracts/Workstation/` have been wired into WPF consumers.
