# ADR-034: Bitemporal Security Master Terms

**Status:** Proposed
**Date:** 2026-11-25
**Owner:** core-team
**Reviewed:** 2026-11-25
**Deciders:** repository owner (chose full valid-time terms on 2026-11-25; acceptance of this ADR pending)
**Supersedes:** —
**Superseded by:** —

## Context

The Security Master records terms on one time axis: recording time. A term amendment keeps the record's
original `EffectiveFrom` (`SecurityMasterCommands.fs`, amend). The request's effective date only expires
identifiers and becomes `Provenance.AsOf`. A back-dated coupon correction therefore silently became the
coupon for the whole life of the security. Only "the full definition as recorded at T" could be
reconstructed, never "the coupon that applied on date D".

The 2026-11-24 architecture review (item E1, PR #3662 Phase 7) offered two scopes: record the intended
effective date (7a), or full valid-time terms. The repository owner chose full valid-time terms.

Three facts bound the design:

1. Economic events are full-state assertions of the security's economic definition (ADR-027), and the
   event log is append-only (migration 059).
2. Every event already carries its recording time (`event_timestamp`).
3. The projection row and every current consumer read "latest recorded" terms.

## Decision

1. **Valid-from is recorded on every economic event.**
   - Create, amend and deactivate write the request's intended effective instant into the event
     metadata under `validFrom` (`SecurityMasterEventPayloads.ValidFromMetadataKey`). For deactivation
     this is `EffectiveTo`.
   - Events written before this ADR carry no key, and read their state's own `EffectiveFrom`.
2. **Semantics.**
   - An event asserts its full economic state from its valid-from until the next later valid-from.
   - "Terms valid at V, as known at T" is the event recorded by T with the latest valid-from not
     after V.
   - When two events have equal valid-from, the later recording wins. A back-dated correction recorded
     later therefore governs the dates it covers, and history is never rewritten.
3. **The read path** is `SecurityMasterValidTimeQuery.GetAsync(securityId, validAt, knownAt)`, served at
   `GET /api/security-master/{securityId}/terms-as-of?validAt=&knownAt=`.
4. **Consumers migrate one by one.**
   - The projection row stays "latest recorded" so that existing readers keep their behaviour.
   - A consumer that needs period-correct economics reads through the valid-time query for the
     period it computes.
   - Cash-flow generation, accrual and restatement routing are the first candidates. Each migration is
     its own change, with its own receipts and tests.

## Implementation Links

| Component | Location | Purpose |
|-----------|----------|---------|
| Metadata key | `src/Meridian.Contracts/SecurityMaster/SecurityEvents.cs` | `ValidFromMetadataKey` |
| Writers | `src/Meridian.Application/SecurityMaster/SecurityMasterMapping.cs`, `src/Meridian.Application/SecurityMaster/SecurityMasterService.cs`, `src/Meridian.Application/SecurityMaster/SecurityMasterService.Creation.cs`, `src/Meridian.Application/SecurityMaster/SecurityMasterService.Deactivation.cs` | Stamp `validFrom` on create, amend and deactivate |
| Read path | `src/Meridian.Application/SecurityMaster/SecurityMasterValidTimeQuery.cs`, `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.Merge.cs` | Bitemporal read and route |
| Tests | `tests/Meridian.Tests/SecurityMaster/SecurityMasterValidTimePostgresTests.cs` | A back-dated correction applies from its valid-from and is invisible before it was recorded |
| Shared rule | `src/Meridian.Contracts/SecurityMaster/SecurityTermsValidTime.cs` | `ISecurityTermsValidTimeReader`, the valid-time assignment rule and economic equality, shared by every reader |
| Cash-flow consumer | `src/Meridian.Application/SecurityMaster/SecurityMasterCashFlowService.ValidTime.cs` | Period-correct calculated schedules |
| Accrual consumer | `src/Meridian.Strategies/Services/SecurityMasterAccountingEventSourceAdapter.cs`, `src/Meridian.Strategies/Services/SecurityMasterAccountingEventService.cs` | Accrual reads the terms valid at period end |
| Restatement consumer | `src/Meridian.Application/SecurityMaster/IPeriodAwareRestatementResolver.cs`, `src/Meridian.Ui.Shared/Services/ReportPackRestatementCandidateResolver.cs` | Restatement routing over the window a term change governs |
| Change history | `src/Meridian.Ui.Shared/Services/SecurityMasterWorkbenchQueryService.History.cs`, `src/Meridian.Wpf/Models/SecurityMasterChangeHistoryRow.cs`, `src/Meridian.Ui/dashboard/src/screens/accounting-screen.security-master-detail-panels.tsx` | Valid time in both workstations' change history |
| Consumer tests | `tests/Meridian.Tests/SecurityMaster/ValidTime/`, `tests/Meridian.Tests/SecurityMaster/SecurityMasterCashFlowValidTimeTests.cs`, `tests/Meridian.Tests/Strategies/SecurityMasterAccountingValidTimeTests.cs`, `src/Meridian.Ui/dashboard/src/screens/security-master-change-history.test.tsx`, `tests/Meridian.Wpf.Tests/Models/SecurityMasterChangeHistoryRowTests.cs` | Each migrated consumer |

**Implemented on 2026-11-25:** the valid-from stamp, the read path and its route.

**Consumers migrated on 2026-11-25** (each opts in only when the host composes the PostgreSQL event
store, which registers `ISecurityTermsValidTimeReader`; other hosts keep "latest recorded" reads):

1. **Cash-flow generation.** `SecurityMasterCashFlowService.GetProjectionAsOfAsync` composes a calculated
   schedule from the security's valid-time periods.
   - Each row takes the terms valid at its accrual start: the accrual start, else the unadjusted start,
     else the payment date. A change in the middle of a period applies from the next period.
   - Rows before the first valid-from use the first period's terms.
   - Consecutive periods with equal economics merge, so an identifier-only amendment changes nothing.
   - A composed projection lists its `TermsPeriods`. A single economic period returns exactly the
     previous projection.
   - Fail closed: if one period's terms are blocked, or the leg set differs between periods, the projection
     is blocked with `ValidTimeCompositionUnsupported` rather than mixing terms silently.
   - The receipt-capturing context path (`CaptureProjectionAsync` / `ReplayProjectionAsync`) is unchanged,
     because a receipt pins the terms it was computed from.
2. **Accrual.** `SecurityMasterAccountingEventSourceAdapter` reads the terms valid at the end of the
   reconciliation period.
   - When those economics equal the latest recorded economics, it reads the projection row exactly as before.
   - If different economics took effect inside the period, `SecurityMasterAccountingEventService` raises
     `SM_TERMS_CHANGED_IN_PERIOD` (Medium). The single-terms accrual does not split the period.
3. **Restatement routing.** `PeriodAwareRestatementResolver` bounds a published term change by the next
   later valid-from.
   - The decision carries `GovernedFrom` and `GovernedUntil`, and the persisted change-outbox decision
     records both.
   - Report-pack candidates whose period lies entirely on or after `GovernedUntil` are dropped. This fails
     open on period labels it cannot parse.
   - Period-lock routing still keys on the effective date.
   - The window is applied only when an economic event asserts the change at exactly its effective instant.
     Corporate actions and overlay-only edits stay open-ended.
4. **Change history.** The workbench change history carries `ValidFromUtc`, `ValidToUtc` and
   `SupersededByStreamVersion`. The browser Security Master change-history panel and the WPF History grid
   show Valid from and Valid to: an end date, "open", or "superseded by vN".

**Still proposed:** reporting consumers, and splitting one accrual period across two sets of terms.

## Rationale

- Full-state events already exist, so valid time needs one metadata field and a read rule. It does not
  need a new event family or a second store.
- Keeping the projection on "latest recorded" makes adoption incremental: no consumer changes
  behaviour until it opts in.

## Alternatives Considered

### Alternative 1: Record the intended effective date only (Phase 7a)

**Pros:**
- Smallest change.

**Cons:**
- No read path, so the correction still cannot be applied to the period it covers.

**Why rejected:** the owner chose full valid time.

### Alternative 2: Per-field valid-time intervals

**Pros:**
- Precise per field. A back-dated coupon fix does not carry the rest of the state with it.

**Cons:**
- Requires a field-level event model alongside the full-state model ADR-027 consolidated on.
- Every consumer would need to recompose state.

**Why rejected:** contradicts ADR-027's single full-state model. The full-state rule is documented in
the Decision section, including how a later valid-from bounds an earlier correction.

## Consequences

### Positive

- Back-dated corrections are period-correct for any reader that asks.
- "As known at" reads support audit and reproducibility.

### Negative

- A full-state correction applies only until the next later valid-from. A correction that must also
  change later periods needs a later-dated amendment too.
- Valid-time reads replay the stream. That is cheap for single securities, and the cost is
  documented for bulk use.

### Neutral

- Existing events read their own `EffectiveFrom` as valid-from.

## Compliance

### Runtime Verification

- `SecurityMasterValidTimePostgresTests` covers the valid-from boundary and the as-known-at boundary.
- Verify the paths in the Implementation Links table:

```bash
dotnet run --project build/dotnet/DocGenerator/DocGenerator.csproj -- verify-adrs --adr-dir docs/adr --src-dir src
```

## References

- [ADR-027: Single Economic Model Consolidation](027-single-economic-model-consolidation.md)
- Security Master architecture review 2026-11-24, item E1

---

*Last Updated: 2026-11-25 (consumer migrations)*
