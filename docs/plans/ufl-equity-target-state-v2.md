# Updated Content for ufl-equity-target-state-v2

**Last Updated:** 2026-04-06
**Status:** active
**Reviewed:** 2026-04-06 | **Phase 1.5 Added:** 2026-04-01

> **Naming standard:** All new F# types and DTOs in this package must follow the
> [Domain Naming Standard](../ai/claude/CLAUDE.domain-naming.md).
> For equities: common share definition → `ComShrDef`; preferred share definition → `PrefShrDef`;
> convertible preferred → `ConvPrefDef`; convertible common → `ConvComDef`; voting/ownership trait → `OwnTr`;
> income/dividend trait → `DivTr`; redemption trait → `RedTr`; callable trait → `CallTr`; convertibility trait → `ConvTr`;
> boolean fields → `HasVoting: bool`, `IsRestricted: bool`, `IsCumulative: bool`, `IsCallable: bool`, `IsConvertible: bool`.

### 2.4 Preferred and Convertible Domain Shapes

**Preferred Equity Terms:**

```fsharp
type DividendType =
    | Fixed
    | Floating
    | Cumulative

type ParticipationTerms = {
    ParticipatesInCommonDividends: bool
    AdditionalDividendThreshold: decimal option
}

type LiquidationPreference =
    | Pari  // Pari passu with common
    | Senior of multiple: decimal  // Senior with specified multiple
    | Subordinated

type PreferredTerms = {
    DividendRate: decimal option
    DividendType: DividendType
    RedemptionPrice: decimal option
    RedemptionDate: DateOnly option
    CallableDate: DateOnly option
    ParticipationTerms: ParticipationTerms option
    LiquidationPreference: LiquidationPreference
}

type PreferredEquityProjection = {
    SecurityId: SecurityId
    DividendSchedule: (DateOnly * decimal) list
    CallableWindow: (DateOnly * decimal) option
    RedemptionWindow: (DateOnly * decimal) option
    CurrentYield: decimal option
    IsCumulative: bool
}
```

**Convertible Equity Terms:**

```fsharp
type ConvertibleTerms = {
    UnderlyingSecurityId: SecurityId
    ConversionRatio: decimal
    ConversionPrice: decimal option
    ConversionStartDate: DateOnly option
    ConversionEndDate: DateOnly option
}

type ConversionProjection = {
    SecurityId: SecurityId
    UnderlyingSecurityId: SecurityId
    ConversionRatio: decimal
    ConversionPrice: decimal option
    ConversionParity: decimal
    IsInTheMoney: bool
    ConversionDeadline: DateOnly option
}
```

**Updated Equity Classification:**

```fsharp
type EquityClassification =
    | Common
    | Preferred of PreferredTerms
    | Convertible of ConvertibleTerms
    | ConvertiblePreferred of PreferredTerms * ConvertibleTerms
    | Warrant
    | Right
    | Other of string
```

Update `EquityTerms` to include classification:

```fsharp
type EquityTerms = {
    ShareClass: string option
    Classification: EquityClassification
}
```

### 2.4A Legacy Compatibility and Interop Guardrails

- `ConvertiblePreferred` must remain distinct in the legacy `SecurityEconomicDefinition` projection. Use `Classification.TypeName = "ConvertiblePreferredEquity"` instead of collapsing to plain preferred equity.
- `SecurityMasterSnapshotWrapper.AssetSpecificTermsJson` must emit both `preferredTerms` and `convertibleTerms` for convertible preferred securities, alongside `shareClass`, `votingRightsCat`, and `classification`.
- `DateOnly` values in wrapper JSON continue to serialize as ISO-8601 strings; regression tests should assert that wire format directly.

### 3.4 Equity-Type-Specific Domain Events

Current repo status as of 2026-04-06: `PreferredTermsAmended` and `ConversionTermsAmended` are now implemented in the Security Master domain and preserved in persisted Security Master event history. Execution events remain planned.

- `PreferredTermsAmended` (dividend rate, redemption terms, callable terms)
- `ConversionTermsAmended` (underlying security, conversion ratio, date windows)
- `ConversionTriggered` (converted shares, effective date)
- `RedemptionExecuted` (redemption price, date, shares)
- `CallExercised` (call price, date, shares)
- `DividendPaymentProcessed` (payment date, amount per share)

### 5.5 Preferred Equity Service module

- owns dividend-schedule projections, redemption validation, and callable-window tracking
- owns preferred-term amendments and dividend-payment recording
- exposes queries for preferred terms, dividend schedules, and yields

### 5.6 Convertible Service module

- owns conversion-parity projections and conversion-eligibility validation
- owns conversion-execution workflows and underlying-security dependencies
- exposes queries for conversion terms, parity, and deadline tracking

### 6.6 Create Preferred Equity

1. create canonical security with `Preferred` classification
2. persist `SecurityCreated` with preferred terms (dividend, redemption, callable)
3. build dividend schedule projection
4. calculate initial yield
5. expose through trading-profile and reference APIs

### 6.7 Amend Preferred Terms

1. validate new dividend rate, redemption, and callable parameters
2. persist `PreferredTermsAmended` event
3. rebuild dividend schedule projection
4. recalculate yield
5. publish outbox event for downstream consumers

### 6.8 Execute Conversion

1. validate conversion eligibility (within date window, valid underlying security)
2. calculate converted shares: `shares × conversion_ratio`
3. record `ConversionTriggered` event
4. update `equity_corporate_action_execution` table
5. rebuild conversion parity projection
6. publish outbox event for position/accounting systems

### 6.9 Execute Redemption

1. validate redemption-date window and share count
2. record `RedemptionExecuted` event with price and date
3. update `equity_corporate_action_execution` table
4. if full redemption: rebuild lifecycle to mark terminated
5. publish outbox event for downstream

### 6.10 Exercise Call

1. validate call-date window and share count
2. record `CallExercised` event with price and date
3. update `equity_corporate_action_execution` table
4. if full call: rebuild lifecycle to mark terminated
5. publish outbox event for downstream

### 7.1B Phase 1.5 goal

Deliver preferred and convertible equity support with dividend schedules, conversion parity, and execution workflows.

### 7.1B Phase 1.5 implementation order

1. Extend `EquityTerms` with `EquityClassification` discriminator
2. Add `PreferredTerms` and `ConvertibleTerms` domain shapes
3. Add new equity-type-specific domain events
4. Create projection tables: `equity_preferred_terms`, `equity_convertible_terms`, `equity_dividend_schedule`, `equity_corporate_action_execution`
5. Implement `DividendScheduleProjectionBuilder` service
6. Implement `ConversionParityProjectionBuilder` service
7. Extend `IEquityReferenceService` with preferred and convertible queries
8. Expose new API endpoints for preferred and convertible data
9. Implement `IConversionExecutionService` and `IRedemptionExecutionService`
10. Add deterministic tests for all preferred and convertible workflows
11. Implement workstation governance views for preferred equity lifecycle

### 7.1B Phase 1.5 exit criteria

- Preferred and convertible equities can be created with full term definitions
- Dividend schedules are projected and queryable
- Conversion parity and eligibility are calculated and exposed
- Conversion, redemption, and call execution workflows are tested and operational
- Governance UI surfaces support preferred equity term management and execution
- Phase 1.5 documentation is complete and up-to-date

### 8.4 Preferred Equity Endpoints

Current repo status as of 2026-04-07: `GET /api/security-master/equities/{securityId}/preferred-terms` is implemented as a typed current-terms endpoint, and `PATCH /api/security-master/equities/{securityId}/preferred-terms` now reuses the existing Security Master amend/event flow for preferred-term updates. Dividend schedule and current-yield endpoints remain planned.

- `GET /api/security-master/equities/{securityId}/preferred-terms` → `PreferredEquityProjection`
- `GET /api/security-master/equities/{securityId}/dividend-schedule?fromDate=X&toDate=Y` → `DividendScheduleRow[]`
- `GET /api/security-master/equities/{securityId}/current-yield` → `{ yieldPercent: decimal }`
- `PATCH /api/security-master/equities/{securityId}/preferred-terms` → update dividend/redemption/callable terms

### 8.5 Convertible Equity Endpoints

Current repo status as of 2026-04-07: `GET /api/security-master/equities/{securityId}/conversion-terms` is implemented as a typed current-terms endpoint via the active Security Master query seam. Price-derived parity, callable-window, and redemption-term endpoints remain planned.

- `GET /api/security-master/equities/{securityId}/conversion-parity` → `ConversionProjection`
- `GET /api/security-master/equities/{securityId}/callable-windows` → `CallableWindow[]`
- `GET /api/security-master/equities/{securityId}/redemption-terms` → `RedemptionTerms`
- `POST /api/security-master/equities/{securityId}/convert` → execute conversion
- `POST /api/security-master/equities/{securityId}/redeem` → execute redemption
- `POST /api/security-master/equities/{securityId}/call` → exercise call

### 4.3 Preferred and Convertible Storage

Additional table groups for Phase 1.5:

- `equity_preferred_terms` - current preferred terms snapshot (security_id, dividend_rate, dividend_type, redemption_price, redemption_date, callable_date, is_cumulative, as_of)
- `equity_convertible_terms` - current convertible terms snapshot (security_id, underlying_security_id, conversion_ratio, conversion_price, conversion_start_date, conversion_end_date, as_of)
- `equity_dividend_schedule` - projected dividend payments (security_id, payment_date, amount, record_date, ex_date, created_at)
- `equity_corporate_action_execution` - conversion/redemption/call history (execution_id, security_id, action_type, executed_date, detail_json, source_event_id)

Index strategy:
- `equity_preferred_terms`: index on `(security_id, as_of)` for rebuild-safe slicing
- `equity_convertible_terms`: index on `(security_id, as_of)` and `(underlying_security_id)` for parity lookups
- `equity_dividend_schedule`: index on `(security_id, payment_date)` and `(ex_date)` for schedule queries
- `equity_corporate_action_execution`: index on `(security_id, executed_date)` for execution history

### 10. Implementation Roadmap

### Phase 1 Tickets (Core Equity)
1. Add equity DTOs and query contracts.
2. Add equity trading-profile projection storage.
3. Add alias-resolution projection and rebuild path.
4. Implement `IEquityReferenceService`.
5. Expose equity reference endpoints.
6. Add deterministic alias-resolution tests.
7. Add lifecycle projection model for listed/suspended/delisted states.
8. Implement corporate-action import record storage.
9. Add rebuild orchestration for corporate actions.
10. Add workstation governance views for equity lifecycle inspection.

### Phase 1.5 Tickets (Preferred & Convertible Equity)
1. Add EquityClassification discriminator and PreferredTerms domain model *(implemented 2026-04-06)*
2. Extend event model for preferred and convertible equity mutations *(implemented 2026-04-06 for `PreferredTermsAmended` and `ConversionTermsAmended`)*
3. Add dividend schedule and conversion parity projection storage
4. Implement dividend schedule projection builder
5. Implement conversion parity projection builder
6. Extend IEquityReferenceService with preferred and convertible lookups *(active repo seam uses `ISecurityMasterQueryService`; initial preferred/conversion term lookups implemented 2026-04-06)*
7. Add API endpoints for preferred and convertible equity queries *(preferred-terms GET/PATCH and conversion-terms GET implemented by 2026-04-07; schedule/yield/parity endpoints remain planned)*
8. Implement conversion execution workflow
9. Implement redemption/call execution workflow
10. Add deterministic tests for preferred and convertible flows
11. Update target-state documentation with preferred/convertible expansion
12. Add workstation governance views for preferred equity lifecycle
