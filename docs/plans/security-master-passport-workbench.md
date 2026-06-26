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
- `OperationsApprovalPolicyMatrixService` already contains the `operations-continuity.security-master-override`
  row (gate `OperationsGateKeyDto.SecurityMaster`, audit `security-master-override-approved`,
  `RequiredDistinctApprovals:1`, `RequiresIndependentReviewer:true`). Per Q1: conflict resolution
  reuses it; field edits get a new `operations-continuity.security-master-field-edit` row (data-only
  add via `UpsertRuleAsync`, audit `security-master-field-edit-approved`).
- "Closed period" state is the **ledger accounting period status** (`LedgerAccountingPeriod.Status`),
  read via `ILedgerJournalStore.GetPeriodAsync`/`ListPeriodsAsync` and authoritative per Q2 —
  not `FundAccountCloseReadinessService` (readiness only).

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
                                        │     expectedVersion, events)      │  WorkflowService│     ├─ SecurityProjectionRebuildHandler (per-security)
                                        │   (EXISTS, optimistic concr.)     │  (EXISTS)       │     ├─ CoverageInvalidationHandler
                                        │                                   │                 │     └─ PeriodAwarePropagationHandler (ledger period status)
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

**D3 — Publish a domain event; the ledger accounting-period status is the lock authority; edits route by tri-state period status, never silent mutation (Unknown #3 — RESOLVED).**
`PublishRevisionAsync` invokes ordered `IEnumerable<ISecurityMasterRevisionPublishedHandler>` after
the durable append: **per-security projection rebuild** (`SecurityProjectionRebuildHandler`, which
calls `SecurityMasterAggregateRebuilder.RebuildAsync(securityId)` — NOT a full UFL replay, see Q4)
→ coverage invalidation → `PeriodAwarePropagationHandler`. The
**single authoritative** answer to "is the period covering `EffectiveFrom` locked?" is the **ledger
accounting period status** (`LedgerAccountingPeriod.Status`, enum `LedgerPeriodStatusDto` ∈ `Open` /
`SoftClosed` / `HardClosed`), enforced at post-time by `LedgerPeriodPostingGuard.Validate(...)` in
`Meridian.Storage.Ledger`. `FundAccountCloseReadinessService` (readiness score) and the
OperationsContinuity close-package are **derivative/informational, not the gate**. The handler reads
period status per affected ledger book and routes by the matrix below:

| Period status (covering `EffectiveFrom`) | Posting path | Restatement proposal |
| --- | --- | --- |
| `Open` | immediate propagation (UFL rebuild + coverage invalidate) | none |
| `SoftClosed` | downstream ledger effect posts as a **governed `Adjustment`** carrying the workbench approval as `LedgerAdjustmentApprovalMetadataDto` (satisfies `LedgerPeriodPostingGuard`'s soft-closed adjustment rule) | only if a **published** report pack consumed the changed line |
| `HardClosed` | **NO posting** | **mandatory** — `ReportingWorkflowService.ProposeRestatement(...)` with changed-line evidence |
| indeterminate / period missing / unrecognized status | treated as `HardClosed` (**default-deny**) | mandatory |

- *Rationale:* the ledger posting guard is the hardest gate (rejects posts into closed periods at
  the storage layer), so aligning the handler to the *same* authority guarantees the workbench can
  never produce a state the ledger would have rejected. Tri-state matters: `SoftClosed` already
  permits *approved* adjustments, and the workbench approval gate produces exactly that approval
  metadata — so soft-closed edits post as governed adjustments rather than blocked. `HardClosed`
  and the default-deny fallback preserve the W4-RPT-001 invariant that restatements are
  human-approved and evidence-backed; the security-master edit only *proposes*. Synchronous direct
  `Restate()` rejected (requires human `actor`/`approver`, `EnsureHumanOrigin`).

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

/// <summary>Authoritative read of accounting-period lock status for a date. Backed by the ledger
/// accounting period (LedgerAccountingPeriod.Status), the same source LedgerPeriodPostingGuard
/// enforces at post-time. Default-deny: returns HardClosed when the covering period is missing or
/// its status is unrecognized.</summary>
public interface ILedgerPeriodLockReader
{
    /// <summary>Resolves the period covering <paramref name="date"/> for the ledger book and returns
    /// its lock status; HardClosed when indeterminate.</summary>
    Task<LedgerPeriodStatusDto> GetPeriodStatusAsync(
        Guid ledgerBookId, DateOnly date, CancellationToken ct = default);
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
    Guid SecurityId, Guid RevisionId, long NewVersion,
    bool RestatementRequired,                                  // true when a hard-closed period with
                                                              // report exposure is affected (Q3 default-deny)
    IReadOnlyList<RestatementCandidateDto> RestatementCandidates,  // operator picks which to restate
    IReadOnlyList<string> InvalidatedProjections);

public sealed record RestatementCandidateDto(
    Guid ReportId, Guid PriorVersionReportId, string PeriodLabel, string Summary,
    IReadOnlyList<ReportPackChangedLineDto> ChangedLines);

public sealed record SecurityMasterConflictAuthorityDecision(
    string PolicyWinnerSource, string Rule, string Rationale, bool IsBulkEligible);

public sealed record SecurityMasterRevisionPublishedEvent(
    Guid SecurityId, Guid RevisionId, long Version, DateTimeOffset EffectiveFrom,
    IReadOnlyList<string> ChangedFields, SecurityMasterDownstreamImpactDto DownstreamImpact,
    IReadOnlyList<Guid> AffectedLedgerBookIds,   // resolved by the command service at publish time;
                                                 // the period-aware handler checks each book's lock status
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
  (→ 409); drive Submit/Approve through `OperationsContinuityWorkflowService` using the
  **`security-master-field-edit`** policy key for field edits and the existing
  **`security-master-override`** key for conflict resolutions (Q1); resolve `evt.AffectedLedgerBookIds`
  from the passport/impact read at publish time; on publish, append + fan out to ordered handlers and
  return any `RestatementCandidateDto`s.
- **Concurrency:** all mutation via `AppendAsync(securityId, expectedVersion, …)`; no in-process
  locks; stale token throws before any side effect.
- **Errors:** empty justification on operator origin → 422; version mismatch → 409; handler failure
  during publish → logged, append already durable, handlers idempotent so retry-safe.

### `SecurityMasterConflictAuthorityPolicy`
- **Lifetime:** Singleton (pure). Precedence: golden-copy rule (`TrustPosture.GoldenCopyRule`) →
  configured source rank → `AsOf` freshness → `ConfidenceScore`. `IsBulkEligible` = assessment
  eligible AND decision matches `RecommendedWinner`. `IOptionsMonitor<SecurityMasterWorkbenchOptions>`.

### `PeriodAwarePropagationHandler` (`Order = 100`) — formerly `ClosedPeriodRestatementProposalHandler`
- **Dependencies:** `ILedgerPeriodLockReader` (**authoritative** period status),
  `IGovernedLedgerAdjustmentPoster` (soft-closed adjustment posting carrying approval metadata —
  wraps the ledger posting path), `IRestatementCandidateResolver` (resolves affected report packs
  into `RestatementCandidateDto`s — see Q3; backed by the report-usage projection, surfacing
  candidates rather than auto-restating), `ILogger<…>`.
- **Logic:** for each affected ledger book in `evt.AffectedLedgerBookIds`, call
  `ILedgerPeriodLockReader.GetPeriodStatusAsync(ledgerBookId, DateOnly.FromDateTime(evt.EffectiveFrom.UtcDateTime))`
  and route by the D3 matrix: `Open` → no-op (lower-`Order` handlers already propagated);
  `SoftClosed` → post the downstream effect as a governed `Adjustment` carrying the workbench
  approval as `LedgerAdjustmentApprovalMetadataDto`, plus restatement candidates only if a published
  report pack consumed the line; `HardClosed` (or indeterminate/default-deny) → **no posting**, and
  `IRestatementCandidateResolver` resolves `RestatementCandidateDto`s (with `ReportPackChangedLineDto`s
  built from `evt.ChangedFields` + evidence links) onto the publish result. If exposure exists but no
  candidate resolves, set `RestatementRequired=true` and emit a manual "locate affected packs" task —
  never silently complete. The operator approves each candidate via the existing
  `ReportingWorkflowService.Restate(...)` path.
- **Why the authority change:** the previous draft depended on `FundAccountCloseReadinessService`,
  which reports *readiness to close*, not *lock state* — using it as the gate would let a hard-closed
  book be silently mutated. The ledger period status is the same authority `LedgerPeriodPostingGuard`
  enforces, so the workbench can never produce a state the ledger would reject.

### `SecurityProjectionRebuildHandler` (`Order = 10`) / `CoverageInvalidationHandler` (`Order = 20`)
- Thin, idempotent. `SecurityProjectionRebuildHandler` rebuilds only the edited security via
  `SecurityMasterAggregateRebuilder.RebuildAsync(evt.SecurityId, …)` (snapshot + tail events — O(events
  since snapshot for one stream), the per-edit hot path; see Q4). It does **not** call
  `IUflProjectionRebuilder.RebuildAsync(assetClass)` — that is a full shared-cache replay reserved for
  ingest/maintenance and triggered async/debounced only on structural reclassification.
  `CoverageInvalidationHandler` evicts the `MultiAssetCoverageReadService` cache key for the impacted
  fund/asset class.

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
7. Publish → `PublishRevisionAsync`: durable append, then handlers by `Order`: per-security
   projection rebuild (`SecurityMasterAggregateRebuilder.RebuildAsync(securityId)`) → coverage evict
   → period-status check (open ⇒ no-op).
8. Returns `{ RestatementRequired=false, RestatementCandidates=[], InvalidatedProjections=[...] }`;
   Portfolio row flips Review-required → Trusted on refetch.

### B. Closed-period edit (restatement path)
1–6 as above, but `EffectiveFrom` falls in a locked period.
7. `PeriodAwarePropagationHandler` reads the authoritative ledger period status for each affected
   book; `HardClosed` + report exposure → `IRestatementCandidateResolver` resolves
   `RestatementCandidateDto`s (or sets `RestatementRequired=true` if none resolve). **No posting.**
8. Returns `{ RestatementRequired=true, RestatementCandidates=[…] }`. **No posted number is mutated**;
   the operator picks candidates and approves each via the existing `Restate()` path.

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

### Unit — `PeriodAwarePropagationHandler`
| Test | Verifies |
| --- | --- |
| `Handle_OpenPeriod_NoProposalNoAdjustment` | no-op on open (lower-Order handlers already propagated) |
| `Handle_SoftClosedPeriod_PostsGovernedAdjustmentWithApproval` | soft-closed → governed `Adjustment` carrying approval metadata, not blocked |
| `Handle_HardClosedPeriod_ProposesRestatementNoPosting` | hard-closed → restatement proposal, evidence-backed, **no ledger posting** |
| `Handle_IndeterminatePeriod_DefaultDenyTreatsAsHardClosed` | missing/unrecognized status ⇒ restatement proposal (default-deny) |

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
- [ ] `ISecurityMasterRevisionPublishedHandler` + `SecurityProjectionRebuildHandler`
      (per-security `SecurityMasterAggregateRebuilder.RebuildAsync(securityId)` — NOT full UFL replay, Q4),
      `CoverageInvalidationHandler`, `PeriodAwarePropagationHandler`.
- [ ] `ILedgerPeriodLockReader` over `ILedgerJournalStore` (authoritative period status, default-deny — Q2).
- [ ] `IGovernedLedgerAdjustmentPoster` (soft-closed adjustment) + `IRestatementCandidateResolver`
      (surfaces candidates — Q3); add `ReportingWorkflowService.ProposeRestatement(...)` for the
      operator-approved restatement step.
- [ ] `SecurityMasterRevisionPublishedEvent.AffectedLedgerBookIds` resolved at publish time.
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
| 1 | ~~Separate `security-master-field-edit` approval policy key, or reuse `security-master-override-approved`?~~ **RESOLVED 2026-06-26:** split by action. Conflict resolution **reuses** the existing `operations-continuity.security-master-override` row (it genuinely *is* an override). Field edits get a **new** `operations-continuity.security-master-field-edit` row — same governance defaults (gate `SecurityMaster`, `RequiredDistinctApprovals:1`, `RequiresIndependentReviewer:true`, `RequiresReportPack:false`) but a distinct `AuditEventType` (`security-master-field-edit-approved`) and Route. Data-only change via `UpsertRuleAsync` (JSON-persisted); no code change. Distinct audit type preserves the audit trail and allows future divergence. | Resolved | — |
| 2 | ~~Exact "closed period" predicate — `FundAccountCloseReadinessService` authoritative, or a distinct ledger period-lock service?~~ **RESOLVED 2026-06-26:** the **ledger accounting period status** (`LedgerAccountingPeriod.Status` ∈ `Open`/`SoftClosed`/`HardClosed`, enum `LedgerPeriodStatusDto`) is the sole authority, enforced at post-time by `LedgerPeriodPostingGuard.Validate(...)` and read via `ILedgerJournalStore.GetPeriodAsync`/`ListPeriodsAsync`. `FundAccountCloseReadinessService` is readiness-only; the close-package is derivative. See D3 routing matrix + new `ILedgerPeriodLockReader`. | Resolved | — |
| 3 | ~~Should `ProposeRestatement` auto-select impacted `reportId`(s) or surface candidates?~~ **RESOLVED 2026-06-26:** **surface candidates** for v1. No durable `security → reportId` index exists — `ReportPackExposureCount` is computed at runtime by rendering a report and matching rows; impact `Links` carry summaries, not Guids. Auto-select would require new schema. The handler attaches resolved `RestatementCandidateDto`s to `SecurityMasterPublishResultDto`; the operator picks which to restate via the existing `Restate()` path. **Default-deny:** if the period is hard-closed with report exposure but no candidate resolves, publish records `RestatementRequired=true` (blocks "done") and surfaces a manual "locate affected packs" task — never a silent completion. *Follow-up (Phase 2):* add `Guid ReportPackId` to `SecurityMasterImpactLinkDto` + a persistent provenance index keyed by security to enable later auto-select. | Resolved | — |
| 4 | ~~Asset-class-scoped UFL rebuild needed, or Phase-0 shared replay acceptable for edit latency?~~ **RESOLVED 2026-06-26:** neither — rebuild only the **edited security** via `SecurityMasterAggregateRebuilder.RebuildAsync(securityId, …)` (snapshot + tail events for one stream). `IUflProjectionRebuilder.RebuildAsync(assetClass)` ignores the asset class and does a **full shared-cache replay** (Phase-0), unacceptable per edit. Broad UFL replay stays a maintenance/ingest operation, triggered async/debounced only when a structural change (e.g. asset-class reclassification) requires it. | Resolved | — |

## Risks

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| Closed-period detection misclassifies a locked period as open → silent mutation of a closed book | Low | High | **Resolved by D3**: gate on the authoritative ledger period status (same source `LedgerPeriodPostingGuard` enforces), not readiness. Default-deny: missing/unrecognized status ⇒ treat as `HardClosed` and propose restatement; integration test asserts no ledger mutation |
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
- `src/Meridian.Storage/Ledger/LedgerPeriodPostingGuard.cs` (authoritative period-lock enforcement — Q2)
- `src/Meridian.Storage/Ledger/ILedgerJournalStore.cs` (`LedgerAccountingPeriod`, `GetPeriodAsync`, `ListPeriodsAsync`)
- `src/Meridian.Contracts/Ledger/LedgerBookDtos.cs` (`LedgerPeriodStatusDto { Open, SoftClosed, HardClosed }`)
- `src/Meridian.FinancialOperations/OperationsContinuity/OperationsContinuityWorkflowService.cs`
- `src/Meridian.FinancialOperations/OperationsContinuity/OperationsApprovalPolicyMatrixService.cs`
- `src/Meridian.Storage/Archival/AtomicFileWriter.cs`
- `src/Meridian.Ui/dashboard/src/components/meridian/security-details-tracker.tsx`
- `src/Meridian.Ui/dashboard/src/screens/portfolio-screen.view-model.ts` (`buildMultiAssetCoveragePanel`)
