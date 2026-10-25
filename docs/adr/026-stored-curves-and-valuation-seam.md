# ADR-026: Stored Curves and the Valuation Seam

**Status:** Proposed
**Date:** 2026-10-25
**Owner:** core-team
**Reviewed:** 2026-10-25
**Deciders:** core-team (review pending)
**Supersedes:** —
**Superseded by:** —

## Context

The Security Master extensibility review recorded finding G4: no interface describes "value this
security as of this date with these market inputs", and the only curve seam,
`ISecurityForwardCurveProvider`, had one implementation, `InMemorySecurityForwardCurveProvider`,
a concrete class in Contracts that nothing in production published to. Every deployed host therefore
projected floating legs flat forward, the cash-flow projection read curves at the wall clock rather
than the projection date, and nothing discounted a projected cash flow anywhere in the tree.

Two pricing lanes exist and never meet: the Security Master's hierarchy-selected, receipted golden
copy (keyed by security, bitemporal) and the ledger's symbol-keyed marks under
`ValuationFreshnessPolicy`. Neither can value a swap or a floating-rate note from a curve. The
pricing lane already shows the durable patterns a curve needs: bitemporal reads on `as_of` and
`recorded_at`, immutable receipts with reject-mutation triggers, and governance events appended in
the same transaction as the state change.

The [domain model](../architecture/meridian-domain-model.md) places economic interpretation
downstream of the Security Master and asks for a `docs/domain/` page and a decision record before
a durable concept is built around.

## Decision

1. **Curves live in `security_master`, under the governance log.** Stored curves are two tables in
   the `security-master` migration set: `security_curves` (write-once identity: key, kind, currency,
   index, day count, interpolation, source; migration 052) and `security_curve_versions`
   (append-only, immutable versions with economic `as_of`, knowledge `recorded_at`, `sourced_at`,
   actor, points as jsonb and a canonical content hash). Update, delete and truncate are refused by
   trigger. Every definition and publication appends to `security_governance_events` on a new
   `Curves` stream (migration 053) in the same transaction; `ISecurityGovernanceEventReader`
   gains `GetForStreamAsync` to read streams whose subject is not a security.
2. **Pure curve rules and arithmetic sit in Contracts.** `SecurityCurveRules` normalizes definitions
   and versions, computes the content hash, interpolates (`ValueAt`), discounts
   (`DiscountFactorAt`) and adapts a Forward version to the existing `SecurityForwardCurve`
   (`ToForwardCurve`), as `DayCountConventions` and the schedule kernel already keep shared
   arithmetic below the owners that interpret terms (ADR-025 rationale). The store contract
   `ISecurityCurveStore` sits beside its DTOs in Contracts, as `IOtcContractStore` and
   `ISecurityUnderlyingGraphStore` do, so endpoints and tests can substitute it.
3. **The existing forward-curve seam is adapted, not replaced.** `ISecurityForwardCurveProvider`
   and `SecurityForwardCurve` are unchanged. `StoredSecurityForwardCurveProvider` in Application
   reads the one Forward definition for an index at its latest version as of the projection date and
   carries the curve key, version and hash prefix into `SecurityForwardCurve.Source`, so every leg's
   `RateSource` names the stored content. The in-memory provider becomes a test double.
   `ISecurityMasterCashFlowService.GetProjectionAsOfAsync` projects as of a requested date and reads
   curves as of that date; `GetProjectionAsync` is that method at `UtcNow`.
4. **Valuation is an interface with models dispatched on a catalog fact.** `ISecurityValuationService`
   takes a `SecurityValuationRequest` (security, economic as-of, knowledge cutoff, account scope,
   quantity, pinned curves, model override) and returns a `SecurityValuationResult` (price with unit,
   present value, market value, observed as-of, observability, the inputs the value was built from,
   diagnostics and blockers). `SecurityAssetClassDescriptor.DefaultValuationModel` names the model a
   class is valued with when a request names none: `QuotedPrice` by default, `DiscountedCashFlow` for
   Bond and Swap. The dispatcher reads the fact, never the class name.
5. **Two models.** `QuotedPriceValuationModel` composes the golden-copy as-of read and reports its
   staleness verdict without re-deciding it. `DiscountedCashFlowValuationModel` composes the
   base-scenario projection as of the valuation date with a stored Discount curve, discounting the
   net schedule and refusing to extrapolate past the curve. Market values use the F# market-value
   functions.
6. **Model outputs never post.** A valuation is a rebuildable view over retained inputs (curve version
   ids and hashes, the selection receipt, the terms version). It does not become a raw price, a
   golden copy, a close-price line, a mark or a ledger entry; when blocked it carries no number.
7. **Ambiguity and missing inputs fail closed.** Several Forward curves for an index leave a leg flat
   forward with a warning; several Discount curves in a currency, a missing curve, a payment beyond
   the last pillar, a currency mismatch or a blocked projection each block the valuation.

## Implementation Links

| Component | Location | Purpose |
|-----------|----------|---------|
| Curve contract and rules | `src/Meridian.Contracts/SecurityMaster/SecurityCurves.cs` | DTOs, requests, `ISecurityCurveStore`, `SecurityCurveRules` |
| Valuation contract | `src/Meridian.Contracts/SecurityMaster/SecurityValuation.cs` | Request, result, `ISecurityValuationModel`, `ISecurityValuationService` |
| Catalog fact | `src/Meridian.Contracts/SecurityMaster/SecurityAssetClassCatalog.cs` | `DefaultValuationModel` on the descriptor |
| Store | `src/Meridian.Storage/SecurityMaster/PostgresSecurityCurveStore.cs` | Write-once definitions, append-only versions, governance events |
| Migrations | `src/Meridian.Storage/SecurityMaster/Migrations/052_security_curves.sql`, `src/Meridian.Storage/SecurityMaster/Migrations/053_security_governance_events_curves_stream.sql` | Tables, triggers, the `Curves` stream |
| Governance reader | `src/Meridian.Storage/SecurityMaster/SecurityGovernanceEventLog.cs` | `Curves` stream, `GetForStreamAsync` |
| Forward provider | `src/Meridian.Application/SecurityMaster/Curves/StoredSecurityForwardCurveProvider.cs` | Store-backed `ISecurityForwardCurveProvider` |
| Dispatcher and models | `src/Meridian.Application/SecurityMaster/Valuation/SecurityValuationService.cs`, `src/Meridian.Application/SecurityMaster/Valuation/QuotedPriceValuationModel.cs`, `src/Meridian.Application/SecurityMaster/Valuation/DiscountedCashFlowValuationModel.cs` | Catalog-fact dispatch and the two models |
| Projection as-of | `src/Meridian.Application/SecurityMaster/SecurityMasterCashFlowService.cs` | `GetProjectionAsOfAsync` |
| Endpoints | `src/Meridian.Ui.Shared/Endpoints/SecurityMasterEndpoints.Curves.cs` | Curve and valuation routes, mapped only when the services are registered |
| Tests | `tests/Meridian.Tests/SecurityMaster/SecurityCurveRulesTests.cs`, `tests/Meridian.Tests/SecurityMaster/DiscountedCashFlowValuationModelTests.cs`, `tests/Meridian.Tests/SecurityMaster/SecurityCurvesPostgresTests.cs`, `tests/Meridian.Tests/Ui/SecurityMasterCurveEndpointsTests.cs` | Rules, golden cases, PostgreSQL behaviour, routes |

Every component above is implemented in the first increment. Not yet landed: a persisted valuation
receipt, wiring into the ledger mark lane (`IMarkPriceSource`, the fair-value level mapping),
per-request pinning of a forward version, a knowledge-time cutoff for curves inside the projection,
an index master, interpolation beyond `Linear`, a definition flag admitting discount factors above
1, the clean price and accrued interest split, non-base scenarios, and curve ingest from vendors.
Decision acceptance alone does not certify a release; the extensibility review's resolution pass
records the validation that ran.

## Rationale

Placing curves in `security_master` costs one migration pair and reuses the governance log, the
bitemporal read pattern and the immutability triggers the pricing lane proved. A new schema would
need a full migration-set declaration, a runner, manifests and either its own append-only log or a
cross-schema reference, for no benefit in a first increment.

Keeping curve arithmetic in Contracts follows the two precedents ADR-025 names: `DayCountConventions`
is the single day-count engine the F# lane delegates to, and the schedule kernel is pure and shared
below the owners. Arithmetic is shared; what a value means stays with the model in Application.

Dispatching on a catalog fact rather than a class name keeps the asset-class comparison ratchet
honest and lets a class opt into a model by declaration. A rule derived from `CashFlowBehavior` was
rejected for this increment because it would have moved factor-amortizing and financing structures
onto discounted cash flow before their semantics were decided.

Adapting `ISecurityForwardCurveProvider` rather than replacing it keeps every consumer compiling and
behaving; the only observable change for a host is that stored curves now reach the projection.

## Alternatives Considered

### Alternative 1: A new `market_data` schema and migration set

**Pros:**
- Clean separation of market inputs from reference data.
- Room for vendor ingest tables later.

**Cons:**
- Needs a migration-set declaration, reservations entry, runner, connection variable and
  regenerated manifests before any curve can be stored.
- Governance events would need a second log or a cross-schema reference.

**Why rejected:** the cost buys nothing in a first increment; the pricing lane already keeps market
observations in `security_master`.

### Alternative 2: Write model values as raw prices through the pricing hierarchy

**Pros:**
- Golden copy, receipts, close sets and approvals would apply unchanged.

**Cons:**
- `security_raw_prices` has no evidence or receipt column, so a model value would lose its lineage.
- A model output would silently compete with observed prices under the same hierarchy walk.

**Why rejected:** a model value is a view over retained inputs, not an observation; whether it may
enter a close set is a separate decision for the pricing lane.

### Alternative 3: A second curve type and interpolation

**Pros:**
- A richer curve shape (currency, day count, compounding) on the projection seam from day one.

**Cons:**
- Every consumer of `SecurityForwardCurve` and `RateSource` would change.
- Two interpolations for the same curve could disagree.

**Why rejected:** the stored version adapts to the existing type, so one arithmetic serves both.

## Consequences

### Positive

- Floating legs can project from stored, versioned curves with the curve's identity and hash in the
  projection's provenance.
- A security can be valued as of a date with a declared model and a result that names every input.
- Curve writes are governed and replayable; a valuation is reproducible from retained versions.

### Negative

- Hosts that pinned the in-process provider in tests must use the moved test double.
- A Discount curve must cover every payment date or the valuation blocks; operators must publish
  curves long enough for the instruments they value.

### Neutral

- One public Contracts type (`InMemorySecurityForwardCurveProvider`) moved to tests, refreshing the
  generated contracts manifest.
- `ISecurityMasterCashFlowService` and `ISecurityGovernanceEventReader` each gain one method.

## Compliance

### Code Contracts

```csharp
public interface ISecurityCurveStore
{
    Task<SecurityCurveDefinitionDto> DefineAsync(DefineSecurityCurveRequest request, CancellationToken ct = default);
    Task<SecurityCurveVersionDto> PublishVersionAsync(PublishSecurityCurveVersionRequest request, CancellationToken ct = default);
    Task<SecurityCurveVersionDto?> GetLatestVersionAsOfAsync(Guid curveId, DateOnly asOf, DateTimeOffset? knownAt = null, CancellationToken ct = default);
}

public interface ISecurityValuationService
{
    Task<SecurityValuationResult?> ValueAsync(SecurityValuationRequest request, CancellationToken ct = default);
}
```

### Runtime Verification

- `SecurityCurveRulesTests` pins every rule in the domain page and the `ValueAt` equivalence with
  `SecurityForwardCurve.RateAt`.
- `SecurityCurvesPostgresTests` proves append-only versions, bitemporal reads, idempotent republish,
  refused mutation (SQL state 23514), the `Curves` stream and the golden swap through the real
  stores.
- `DiscountedCashFlowValuationModelTests` and `QuotedPriceValuationModelTests` pin the golden
  numbers and every blocker; `SecurityValuationServiceTests` pins catalog-fact dispatch.
- `SecurityAssetClassBehaviorFactsTests.DefaultValuationModel_DiscountedCashFlow_IsBondAndSwap` and
  the asset-class comparison ratchet hold the fact.
- Verify paths in the Implementation Links table from the repository root:

```bash
dotnet run --project build/dotnet/DocGenerator/DocGenerator.csproj -- verify-adrs --adr-dir docs/adr --src-dir src
```

This command verifies referenced files and directories exist; it does not validate behavior.

## References

- [Market Curve](../domain/market-curve.md)
- [ADR-025: Contractual Schedule Kernel and Business-Day Calendar Seam](025-contractual-schedule-kernel.md)
- [Security Master extensibility review](../architecture/security-master-extensibility-review.md) (finding G4)
- [Meridian domain model](../architecture/meridian-domain-model.md)

---

*Last Updated: 2026-10-25*
