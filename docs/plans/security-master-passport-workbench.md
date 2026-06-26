# Security Master Passport Workbench — Governed Write Blueprint

> **Status:** Proposed (blueprint) · **Created:** 2026-06-26 · **Owner lane:** Data Confidence and Validation / Accounting and Ledger
> **Roadmap anchors:** W5-MASSET-001 (multi-asset reference-data workbench — named next feature slice), W4-RPT-001 (governed report-pack restatement lifecycle)
> **Depth:** full blueprint · **Source:** brainstorm 2026-06-26 (write-first reframe)

## Summary

Promote the Security Master passport from a read-only coverage view into a **governed write
surface** where fund operators author, correct, version, and approve instrument reference data —
entered from the existing Portfolio multi-asset coverage panel and the Data/Security-Master detail
flow (no new route). The write substrate already exists (event-sourced master with optimistic
concurrency, bitemporal effective-dating, a wired approval gate, conflict assessments, and a
downstream-impact model); this blueprint adds three seams:

1. **Conflict-authority policy** — a deterministic default winner so operator overrides become
   auditable *deviations*, not silent swaps.
2. **Unified governed write path** — one command service emitting operator-origin, effective-dated
   events through the existing `ISecurityMasterEventStore`.
3. **Restatement propagation** — a closed period is never silently changed; closed-period edits
   become governed restatement *proposals* (W4-RPT-001), open-period edits propagate immediately.

## Breaking Change Assessment

**No breaking interface changes — all additive.**

| Surface | Change | Migration |
| --- | --- | --- |
| `ISecurityMasterWorkbenchQueryService` | Add sibling `ISecurityMasterWorkbenchCommandService` (new file); query service untouched | None |
| `ReportingWorkflowService.Restate(...)` | Add `ProposeRestatement(...)` (new method); existing `Restate` signature unchanged | None |
| `UiApiRoutes` | Add new route constants (additive) | None |

The workbench command service **wraps** the existing fragmented write routes
(`SecurityMasterAmend`, `SecurityMasterOperatorOverrides`, `SecurityMasterConflictResolve`) rather
than replacing them — they remain valid lower-level seams.

## Scope

**In scope**
- `ISecurityMasterWorkbenchCommandService` unifying `UpdateSecurityField`, `ResolveSourceConflict`,
  `SubmitForApproval`, `PublishRevision` over the existing `ISecurityMasterEventStore` and the
  existing `OperationsContinuityWorkflowService` approval pipeline.
- `ISecurityMasterConflictAuthorityPolicy` — deterministic default winner + override-evidence rule.
- Effective-dated write path reusing existing bitemporal fields (`EffectiveFrom`/`EffectiveTo`,
  `Provenance.AsOf`, `asOf` queries).
- Restatement-propagation seam: domain event on publish + closed-period detector routing into
  W4-RPT-001 + UFL projection rebuild + coverage invalidation.
- Browser passport editor + WPF parity over the same shared command DTOs.

**Out of scope**
- New corporate-action authoring UI (`SecurityMasterCorporateActionCommandService` already exists;
  the workbench links to it).
- A new persistence engine (`PostgresSecurityMasterEventStore` + `AtomicFileWriter` JSONL stay).
- Provider-side ingest changes (Polygon/EDGAR untouched).
- Asset-class-scoped UFL replay (`IUflProjectionRebuilder` Phase-0 shared replay used as-is).

**Assumptions (verify before coding)**
- `OperationsApprovalPolicyMatrixService` already contains a `SecurityMaster` gate row
  (`OperationsGateKeyDto.SecurityMaster`, event `security-master-override-approved`,
  `RequiredDistinctApprovals:1`, `RequiresIndependentReviewer:true`). Reused; a distinct
  `security-master-field-edit` key is a one-row matrix addition if wanted.
- "Closed period" state is readable via `FundAccountCloseReadinessService` / period-lock posture
  already surfaced in `MultiAssetCoverageReadService`.

## Architectural Overview

```
Portfolio coverage panel ─┐         ┌─ security-details-tracker.tsx (draft/save/sync, EXISTS)
 (buildMultiAssetCoverage)│         │
Data/Security-Master ─────┼─► Passport Editor (TSX) ──► POST /api/security-master/workbench/*
 detail flow              │         │                            │
WPF SecurityMaster page ──┘         └─ WPF: SecurityPassportEditorViewModel
                                                                 │
                          WorkstationEndpoints.SecurityMasterWorkbench.cs  (NEW partial)
                                                                 │
                                    ┌────────────────────────────▼───────────────────────────┐
                                    │  ISecurityMasterWorkbenchCommandService  (NEW)           │
                                    │   UpdateSecurityField / ResolveSourceConflict /          │
                                    │   SubmitForApproval / PublishRevision                    │
                                    └───┬───────────────┬──────────────────┬─────────────────┬─┘
                                        │               │                  │                 │
                ISecurityMasterConflict │   ISecurityMasterEventStore      │  Operations-    │  ISecurityMasterRevision
                AuthorityPolicy (NEW)   │   .AppendAsync(id,                │  Continuity     │  PublishedHandler[] (NEW)
                                        │     expectedVersion, events)      │  WorkflowService│     ├─ UflProjectionRebuildHandler
                                        │   (EXISTS, optimistic concr.)     │  (EXISTS)       │     ├─ CoverageInvalidationHandler
                                        │                                   │                 │     └─ ClosedPeriodRestatementProposalHandler
                                        ▼                                   ▼                 ▼           │
                              SecurityMasterEventEnvelope (Origin=Operator,        ReportingWorkflowService
                               EffectiveFrom, Provenance{AsOf,UpdatedBy,Reason})   .ProposeRestatement(...) (NEW)
```

### Design Decisions

**D1 — Conflict authority is a deterministic policy; operator overrides are events (Unknown #1).**
`ISecurityMasterConflictAuthorityPolicy` computes the default winner from a precedence ladder
(golden-copy source rule → source-system rank → freshness `AsOf` → confidence score), all raw
inputs already present on `SecurityMasterConflictAssessmentDto` and
`InstrumentPassportProviderConfidenceDto.ConfidenceScore`. Operators may override only by emitting a
`SourceConflictResolved` event carrying `chosenWinner`, `reason`, `actor`. The deviation between
policy-winner and operator-choice is the audited artifact.
- *Alternatives:* free-form edit (no default, no auditable deviation); hard-coded precedence (not
  fund-configurable). Both rejected.

**D2 — Reuse the existing bitemporal event store; add an operator-origin event type (Unknown #2).**
`SecurityMasterRecord` already has `EffectiveFrom`/`EffectiveTo`; `AppendAsync` already takes
`expectedVersion`; queries already accept `asOfUtc`. No new store. `UpdateSecurityFieldAsync` builds
a `SecurityMasterEventEnvelope { Origin=Operator, EffectiveFrom, Provenance{AsOf=now,
UpdatedBy=actor, Reason=justification} }` and appends with the caller's `expectedVersion`.
- *Consequence:* a stale token throws → HTTP 409 → UI refetch. No bespoke locking.

**D3 — Publish a domain event; closed-period edits route to restatement, never silent mutation (Unknown #3).**
`PublishRevisionAsync` invokes ordered `IEnumerable<ISecurityMasterRevisionPublishedHandler>` after
the durable append: UFL rebuild → coverage invalidation → `ClosedPeriodRestatementProposalHandler`,
which checks `SecurityMasterDownstreamImpactDto.ReportPackExposureCount` against period-lock state
and, for closed periods, calls `ReportingWorkflowService.ProposeRestatement(...)` (enqueues a
`ReportPackRestatementMetadataDto` with changed-line evidence for human approval) rather than
mutating posted numbers.
- *Rationale:* preserves the W4-RPT-001 invariant that restatements are human-approved and
  evidence-backed; the security-master edit only *proposes*. Synchronous direct `Restate()` rejected
  (requires human `actor`/`approver`, `EnsureHumanOrigin`).

## Interface & API Contracts

### New interfaces (`Meridian.Application.SecurityMaster`)

```csharp
/// <summary>Governed write surface for the Security Master Passport Workbench.
/// Wraps the existing event store + approval pipeline; never bypasses optimistic concurrency.</summary>
public interface ISecurityMasterWorkbenchCommandService
{
    Task<SecurityMasterEditResultDto> UpdateSecurityFieldAsync(
        UpdateSecurityFieldRequest request, CancellationToken ct = default);

    Task<SecurityMasterConflictResolutionDto> ResolveSourceConflictAsync(
        ResolveSourceConflictRequest request, CancellationToken ct = default);

    Task<SecurityMasterEditResultDto> SubmitForApprovalAsync(
        SubmitSecurityMasterRevisionRequest request, CancellationToken ct = default);

    Task<SecurityMasterPublishResultDto> PublishRevisionAsync(
        PublishSecurityMasterRevisionRequest request, CancellationToken ct = default);
}

/// <summary>Deterministic default-winner computation + the evidence an override must carry.</summary>
public interface ISecurityMasterConflictAuthorityPolicy
{
    SecurityMasterConflictAuthorityDecision Evaluate(
        SecurityMasterConflictAssessmentDto assessment,
        IReadOnlyList<InstrumentPassportProviderConfidenceDto> providerConfidence);
}

/// <summary>Side-effect handler invoked after a revision is durably published. MUST be idempotent.</summary>
public interface ISecurityMasterRevisionPublishedHandler
{
    int Order { get; }  // lower runs first; restatement proposal runs last
    Task HandleAsync(SecurityMasterRevisionPublishedEvent evt, CancellationToken ct = default);
}
```

### New contract DTOs (`Meridian.Contracts.Workstation`)

```csharp
public enum SecurityMasterEditOrigin { Provider = 0, Operator = 1 }
public enum SecurityMasterRevisionStateDto { Draft = 0, Submitted = 1, Approved = 2, Published = 3, Rejected = 4 }

public sealed record UpdateSecurityFieldRequest(
    Guid SecurityId, long ExpectedVersion, string FieldPath, string? NewValue,
    DateTimeOffset EffectiveFrom, string Actor, string Justification,
    string? SourceRecordId = null, string? FundProfileId = null, string? CorrelationId = null);

public sealed record ResolveSourceConflictRequest(
    Guid SecurityId, Guid ConflictId, long ExpectedVersion, string ChosenWinnerSource,
    string Actor, string Reason, bool AcknowledgePolicyDeviation = false, string? CorrelationId = null);

public sealed record SubmitSecurityMasterRevisionRequest(
    Guid SecurityId, Guid RevisionId, string Actor, string? Note, string? FundProfileId = null);

public sealed record PublishSecurityMasterRevisionRequest(
    Guid SecurityId, Guid RevisionId, string Actor, string ApproverActor, string? CorrelationId = null);

public sealed record SecurityMasterEditResultDto(
    Guid SecurityId, Guid RevisionId, long NewVersion,
    SecurityMasterRevisionStateDto State, SecurityMasterChangeHistoryItemDto ChangeEntry);

public sealed record SecurityMasterConflictResolutionDto(
    Guid ConflictId, string PolicyWinnerSource, string ChosenWinnerSource,
    bool IsPolicyDeviation, string Reason, long NewVersion);

public sealed record SecurityMasterPublishResultDto(
    Guid SecurityId, Guid RevisionId, long NewVersion, bool TriggeredRestatementProposal,
    Guid? RestatementProposalId, IReadOnlyList<string> InvalidatedProjections);

public sealed record SecurityMasterConflictAuthorityDecision(
    string PolicyWinnerSource, string Rule, string Rationale, bool IsBulkEligible);

public sealed record SecurityMasterRevisionPublishedEvent(
    Guid SecurityId, Guid RevisionId, long Version, DateTimeOffset EffectiveFrom,
    IReadOnlyList<string> ChangedFields, SecurityMasterDownstreamImpactDto DownstreamImpact,
    string Actor, string? CorrelationId);
```

### Additive modification (`ReportingWorkflowService`)

```csharp
/// <summary>Enqueues a restatement proposal for a closed period impacted by an upstream
/// reference-data change. Does NOT post; a human approves via the existing Restate() path.</summary>
public ReportPackRestatementProposalDto ProposeRestatement(
    Guid reportId, string triggerSource, string reasonCode,
    Guid priorVersionReportId, IReadOnlyList<ReportPackChangedLineDto> changedLines);
```

### REST API surface (new `UiApiRoutes` constants)

```
POST /api/security-master/{securityId:guid}/workbench/field
  Body: UpdateSecurityFieldRequest → 200: SecurityMasterEditResultDto
  409: { "error": "version-conflict", "currentVersion": <long> }
  422: { "error": "justification-required" }

POST /api/security-master/{securityId:guid}/workbench/resolve-conflict
  Body: ResolveSourceConflictRequest → 200: SecurityMasterConflictResolutionDto
  422: { "error": "policy-deviation-unacknowledged" }

POST /api/security-master/{securityId:guid}/workbench/submit
  Body: SubmitSecurityMasterRevisionRequest → 200: SecurityMasterEditResultDto

POST /api/security-master/{securityId:guid}/workbench/publish
  Body: PublishSecurityMasterRevisionRequest → 200: SecurityMasterPublishResultDto
  403: { "error": "approval-required" }
```

## Component Design

### `SecurityMasterWorkbenchCommandService`
- **Namespace:** `Meridian.Application.SecurityMaster` · **Lifetime:** Scoped
- **Dependencies:** `ISecurityMasterEventStore`, `ISecurityMasterConflictAuthorityPolicy`,
  `ISecurityMasterWorkbenchQueryService`, `IOperationsContinuityWorkflowService`,
  `IEnumerable<ISecurityMasterRevisionPublishedHandler>`, `ILogger<…>`
- **Responsibilities:** validate justification on operator origin; build operator-authored
  envelope; append with `expectedVersion`; map store concurrency failure → `ConcurrencyException`
  (→ 409); drive Submit/Approve through `OperationsContinuityWorkflowService`; on publish, append +
  fan out to ordered handlers.
- **Concurrency:** all mutation via `AppendAsync(securityId, expectedVersion, …)`; no in-process
  locks; stale token throws before any side effect.
- **Errors:** empty justification on operator origin → 422; version mismatch → 409; handler failure
  during publish → logged, append already durable, handlers idempotent so retry-safe.

### `SecurityMasterConflictAuthorityPolicy`
- **Lifetime:** Singleton (pure). Precedence: golden-copy rule (`TrustPosture.GoldenCopyRule`) →
  configured source rank → `AsOf` freshness → `ConfidenceScore`. `IsBulkEligible` = assessment
  eligible AND decision matches `RecommendedWinner`. `IOptionsMonitor<SecurityMasterWorkbenchOptions>`.

### `ClosedPeriodRestatementProposalHandler` (`Order = 100`)
- **Dependencies:** `FundAccountCloseReadinessService`, `ReportingWorkflowService`, `ILogger<…>`.
  From `evt.DownstreamImpact`, enumerate report-pack links; closed/locked + reported-line affected →
  `ProposeRestatement(...)` with `ReportPackChangedLineDto`s built from `evt.ChangedFields` +
  evidence links. Open periods → no-op.

### `UflProjectionRebuildHandler` (`Order = 10`) / `CoverageInvalidationHandler` (`Order = 20`)
- Thin, idempotent: `IUflProjectionRebuilder.RebuildAsync(assetClass, ct)`; evict
  `MultiAssetCoverageReadService` cache key for the impacted fund/asset class.

### Configuration

```csharp
public sealed class SecurityMasterWorkbenchOptions
{
    public const string SectionName = "SecurityMasterWorkbench";
    public List<string> SourcePrecedence { get; init; } = ["GoldenCopy", "Edgar", "Polygon", "Operator"];
    public bool RequireIndependentReviewer { get; init; } = true;
    public int MaxBulkResolveBatch { get; init; } = 200;
}
```

## Data Flow

### A. Open-period operator field edit (happy path)
1. Operator edits a field in the passport editor; `security-details-tracker.tsx` holds the draft,
   posts `UpdateSecurityFieldRequest` with `ExpectedVersion` from the loaded passport.
2. Endpoint → `UpdateSecurityFieldAsync`: validates non-empty `Justification`.
3. Builds `SecurityMasterEventEnvelope { Origin=Operator, EffectiveFrom, Provenance{...} }`.
4. `AppendAsync(securityId, expectedVersion, [envelope])` → version N+1. State = `Draft`.
5. Submit → `OperationsContinuityWorkflowService.SubmitForApprovalAsync` (gate `SecurityMaster`).
   State = `Submitted`.
6. Independent reviewer approves → `Approved`.
7. Publish → `PublishRevisionAsync`: durable append, then handlers by `Order`: UFL rebuild →
   coverage evict → closed-period check (open ⇒ no-op).
8. Returns `{ TriggeredRestatementProposal=false, InvalidatedProjections=[...] }`; Portfolio row
   flips Review-required → Trusted on refetch.

### B. Closed-period edit (restatement path)
1–6 as above, but `EffectiveFrom` falls in a locked period.
7. `ClosedPeriodRestatementProposalHandler` sees `ReportPackExposureCount > 0` + locked → calls
   `ProposeRestatement(...)`.
8. Returns `{ TriggeredRestatementProposal=true, RestatementProposalId=… }`. **No posted number is
   mutated**; report pack shows a pending restatement for human approval via existing `Restate()`.

### C. Conflict resolution (policy + override)
1. Source Conflicts tab shows `PolicyWinnerSource` (from `Evaluate`) vs `ChallengerValue`.
2. Accept policy winner → `ChosenWinnerSource == policyWinner` → append `SourceConflictResolved`;
   `IsPolicyDeviation=false`.
3. Override → must set `AcknowledgePolicyDeviation=true`, else **422**; event records policy winner +
   chosen winner + reason.
4. Bulk: only `IsBulkEligible && chosen==policyWinner` auto-apply via existing
   `BulkResolveSecurityMasterConflictsAsync`; deviations fall back to per-row.

### D. Concurrency error path
- Two operators load version N. A publishes → N+1. B submits `ExpectedVersion=N` →
  `ConcurrencyException` → **409 { currentVersion: N+1 }** → UI reload banner + refetch. No lost write.

## UI Design

### Browser — `security-passport-editor.tsx` (extends `security-details-tracker.tsx`)
```
Passport detail (entered from coverage panel row / Data→Security-Master)
├── Header: symbol · AssetClass · TrustPosture dot (Blocked/Review/Trusted) · Version chip
├── Tabs: [Identity] [Economics/Terms] [Corporate Actions↗] [Venues] [Classification] [Source Conflicts] [History]
├── Field row: Label · <EditableValue> · OriginBadge(Provider|Operator) · EvidenceLink · "Edit"
│      └── Edit → inline editor: NewValue, EffectiveFrom (date), Justification (required) → Save = draft
├── Source Conflicts tab: dense grid
│      Columns: Field · Winner(policy) · Challenger · Source · ImpactSeverity · [Accept] [Override…]
│      Override… → modal requiring Reason + Acknowledge-deviation checkbox
│      Toolbar: [Bulk-resolve eligible]
└── Footer action bar: [Save Draft] [Submit for Approval] [Publish] (Publish disabled until Approved)
```
- State badges: Draft grey, Submitted amber, Approved blue, Published green, Rejected red.
- `ExpectedVersion` captured at load; 409 → non-destructive reload banner. Justification gates Save
  client-side for UX only; server re-validates (no client-local readiness rule).

### WPF — `SecurityPassportEditorViewModel : BindableBase` + `SecurityPassportEditorPage.xaml`
- TabControl mirroring browser tabs; `DataGrid` of `PassportFieldViewModel`; action bar with
  `AsyncRelayCommand`s (`SubmitCommand` CanExecute=HasDraft, `PublishCommand` CanExecute=IsApproved).
  Published-state changes marshalled via `Application.Current.Dispatcher.Invoke`. Same DTOs — no
  WPF-local rules.

## Test Plan

**Principle:** mock at the `ISecurityMasterEventStore` / `IOperationsContinuityWorkflowService` /
handler boundaries; assert event envelopes and governance transitions. xUnit + FluentAssertions.

### Unit — `SecurityMasterWorkbenchCommandService`
| Test | Verifies |
| --- | --- |
| `UpdateSecurityField_OperatorOriginNoJustification_Throws` | 422 invariant: operator edits require justification |
| `UpdateSecurityField_AppendsOperatorOriginEnvelopeWithEffectiveFrom` | Origin=Operator, EffectiveFrom + Provenance set |
| `UpdateSecurityField_StaleExpectedVersion_ThrowsConcurrency` | optimistic concurrency → 409 |
| `UpdateSecurityField_MissingProviderData_StaysReviewRequired` | never fabricates completeness |
| `Submit_RoutesThroughOperationsApprovalGate` | governance reuse |
| `Publish_OpenPeriod_RunsHandlersNoRestatement` | open-period propagation |
| `Publish_ClosedPeriodWithReportExposure_ProposesRestatement` | Unknown #3 invariant |
| `Publish_HandlerThrows_AppendStillDurable_Idempotent` | retry safety |

### Unit — `SecurityMasterConflictAuthorityPolicy`
| Test | Verifies |
| --- | --- |
| `Evaluate_GoldenCopyRuleWins_OverRankAndFreshness` | precedence order |
| `Evaluate_TieBrokenByFreshnessThenConfidence` | AsOf then ConfidenceScore |
| `Evaluate_BulkEligibleOnlyWhenMatchesRecommendedWinner` | bulk gating |

### Unit — `ResolveSourceConflict`
| Test | Verifies |
| --- | --- |
| `Resolve_ChosenEqualsPolicy_NoDeviation` | `IsPolicyDeviation=false` |
| `Resolve_OverrideWithoutAck_Throws422` | deviation must be acknowledged |
| `Resolve_OverrideWithAck_RecordsDeviationReason` | deviation is the audited artifact |

### Unit — `ClosedPeriodRestatementProposalHandler`
| Test | Verifies |
| --- | --- |
| `Handle_OpenPeriod_NoProposal` | no-op on open |
| `Handle_ClosedPeriod_BuildsChangedLinesWithEvidence` | proposal carries changed-line evidence |

### Unit — `SecurityPassportEditorViewModel`
| Test | Verifies |
| --- | --- |
| `PublishCommand_DisabledUntilApproved` | CanExecute gating |
| `OnVersionConflict_RaisesReloadState` | 409 → non-destructive reload |

### Integration (may defer past first sprint)
| Test | Verifies |
| --- | --- |
| `WorkstationSecurityMasterWorkbenchEndpoints_FullLifecycle` | field→submit→approve→publish; 409 on stale version |
| `ClosedPeriodEdit_EndToEnd_CreatesRestatementProposal_NoLedgerMutation` | the headline guarantee |

### Test infrastructure
- `FakeSecurityMasterEventStore` (in-memory, version-aware, throws `ConcurrencyException` on mismatch).
- `RecordingRevisionPublishedHandler` to assert order + idempotency.
- Reuse `tests/fixtures/security-instrument-explorer-parity.json`; add
  `security-master-workbench-lifecycle.json`.

## Implementation Checklist

**Estimated effort:** Medium–High (~2.5–3 weeks, 1 dev).
**Suggested branch:** `codex/security-master-passport-workbench`.
**Suggested PR sequence:** PR1 contracts+command service+policy, PR2 restatement propagation,
PR3 browser UI, PR4 WPF parity.

### Phase 1 — Contracts & policy
- [ ] Add DTOs to `Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs`.
- [ ] Register them in the source-generated JSON context (ADR-014).
- [ ] Add route constants to `UiApiRoutes.cs`.
- [ ] `ISecurityMasterConflictAuthorityPolicy` + impl + `SecurityMasterWorkbenchOptions`.

### Phase 2 — Command service
- [ ] `ISecurityMasterWorkbenchCommandService` + impl over `ISecurityMasterEventStore`.
- [ ] Wire Submit/Approve through `OperationsContinuityWorkflowService`; confirm/add `SecurityMaster` matrix row.
- [ ] Map `ConcurrencyException` → 409 in endpoint.

### Phase 3 — Propagation
- [ ] `ISecurityMasterRevisionPublishedHandler` + `UflProjectionRebuildHandler`,
      `CoverageInvalidationHandler`, `ClosedPeriodRestatementProposalHandler`.
- [ ] Add `ReportingWorkflowService.ProposeRestatement(...)` + `ReportPackRestatementProposalDto`.
- [ ] DI registration (ordered handler collection).

### Phase 4 — Endpoints + UI
- [ ] New partial `WorkstationEndpoints.SecurityMasterWorkbench.cs` mapping the 4 routes.
- [ ] Browser `security-passport-editor.tsx` extending `security-details-tracker.tsx`; entry point
      from `buildMultiAssetCoveragePanel()` row.
- [ ] WPF `SecurityPassportEditorViewModel` + `SecurityPassportEditorPage.xaml`.

### Phase 5 — Tests
- [ ] All unit tests above (~22) green; ≥80% on new code.
- [ ] Integration tests (or explicitly deferred with a tracking note).

### Phase 6 — Wrap-up
- [ ] `appsettings.json` `SecurityMasterWorkbench` section defaults.
- [ ] ADR check: governed write over an event-sourced aggregate + restatement propagation likely
      warrants an ADR amendment or new ADR ("ADR-017 Reference-data governed edit + restatement propagation").
- [ ] XML doc comments on all public interfaces.
- [ ] Update nearest source READMEs + roadmap registry note on W5-MASSET-001 (RISK-AI-DOC-SKIP-001).
- [ ] PR checklist: no `.Result`/`.Wait()`, structured logging, `CancellationToken` throughout, no
      client-local readiness rules.

## Open Questions

| # | Question | Owner | Impact if unresolved |
| --- | --- | --- | --- |
| 1 | Separate `security-master-field-edit` approval policy key, or reuse `security-master-override-approved`? | Product/Governance | One-row matrix add vs reuse; low risk either way |
| 2 | Exact "closed period" predicate — `FundAccountCloseReadinessService` authoritative, or a distinct ledger period-lock service? | Implementer | Wrong predicate → closed-period edit could slip through as open (the invariant we must not miss) |
| 3 | Should `ProposeRestatement` auto-select impacted `reportId`(s) or surface candidates? | Product | Recommend surface-candidates for v1 |
| 4 | Asset-class-scoped UFL rebuild needed, or Phase-0 shared replay acceptable for edit latency? | Implementer | Shared replay may be slow on large masters; acceptable for v1 |

## Risks

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| Closed-period detection misclassifies a locked period as open → silent mutation of a closed book | Low | High | Default-deny: indeterminate period state ⇒ treat as closed and propose restatement; integration test |
| Handler partial failure leaves projections inconsistent after committed append | Med | Med | Idempotent handlers + publish retry; `InvalidatedProjections` reports partial success |
| Operator override fatigue erodes provenance value | Med | Med | Require `Reason` on deviation; surface deviation rate in trust posture; bulk-resolve only non-deviating |
| Scope creep into corporate-action authoring / new persistence | Med | Med | Explicit Out-of-Scope; link to existing services |

## Grounding References (real source)

- `src/Meridian.Application/SecurityMaster/ISecurityMasterWorkbenchQueryService.cs`
- `src/Meridian.Application/SecurityMaster/SecurityMasterCorporateActionCommandService.cs`
- `src/Meridian.Storage/SecurityMaster/ISecurityMasterEventStore.cs`
- `src/Meridian.Contracts/Workstation/SecurityMasterTrustWorkbenchDtos.cs`
- `src/Meridian.Ui.Shared/Services/MultiAssetCoverageReadService.cs`
- `src/Meridian.Ui.Shared/Services/ReportingWorkflowService.cs` (`Restate`)
- `src/Meridian.Application/SecurityMaster/IUflProjectionRebuilder.cs`
- `src/Meridian.FinancialOperations/OperationsContinuity/OperationsContinuityWorkflowService.cs`
- `src/Meridian.FinancialOperations/OperationsContinuity/OperationsApprovalPolicyMatrixService.cs`
- `src/Meridian.Storage/Archival/AtomicFileWriter.cs`
- `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.tsx`
- `src/Meridian.Ui/dashboard/src/screens/portfolio-screen.view-model.ts` (`buildMultiAssetCoveragePanel`)
