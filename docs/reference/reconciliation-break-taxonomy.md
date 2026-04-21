# Reconciliation Break Taxonomy (Versioned)

`Meridian.FSharp.Ledger` now classifies reconciliation breaks through a **closed, versioned model** so governance/UI/API layers can consume stable outputs.

## Taxonomy version

- `reconciliation-break-taxonomy/v1`

## Canonical break classes

- `Timing`
- `Quantity`
- `Price`
- `Instrument`
- `CashFlow`
- `CorporateAction`
- `MappingError`

## Reason codes (v1)

- `TimingOutsideTolerance`
- `SettlementDateMissing`
- `QuantityMismatch`
- `QuantitySignMismatch`
- `PriceMismatch`
- `PriceMissing`
- `InstrumentIdentifierMismatch`
- `InstrumentMissing`
- `CashAmountMismatch`
- `CashCurrencyMismatch`
- `CorporateActionTypeMismatch`
- `CorporateActionFactorMismatch`
- `MappingKeyNotFound`
- `MappingConflict`
- `UnsupportedBreakTypeFallback`
- `NoDeterministicSignalFallback`

## Migration strategy for unknown/new break types

When upstream producers emit break types outside the known taxonomy:

1. The kernel keeps processing (no throw / no null classification).
2. Classification safely falls back to:
   - `BreakClass = MappingError`
   - `ReasonCodes` including `UnsupportedBreakTypeFallback`
3. Outputs remain stable for UI/API consumers while new break categories are evaluated for a future taxonomy version.

## Interop surface

`LedgerInterop.ClassifyBreakFacts` returns stable DTOs:

- `TaxonomyVersion`
- `BreakClass`
- `PrimaryReasonCode`
- `ReasonCodes`
- `IsFallback`

`LedgerInterop.ToBreakRecordClassificationDtos` maps persisted `BreakRecord` rows into governance/API-safe canonical payloads with stable identifiers and classification metadata.

This allows governance experiences to remain compatible while F# internals evolve under explicit version upgrades.
