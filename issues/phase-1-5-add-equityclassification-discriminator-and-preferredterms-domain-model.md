## Overview
Extend the domain model to support equity classification (common, preferred, convertible) as the foundation for Phase 1.5.

## Acceptance Criteria
- [ ] Add `EquityClassification` discriminated union to `SecurityMaster.fs`
- [ ] Add `PreferredTerms` record with dividend rate, type (Fixed/Floating/Cumulative), redemption, and callable parameters
- [ ] Add `ConvertibleTerms` record with underlying security reference, conversion ratio, and date windows
- [ ] Add `LiquidationPreference` union (Pari, Senior, Subordinated)
- [ ] Update `EquityTerms` to include optional `Classification` field
- [ ] Add unit tests validating term constraints (e.g., redemption date >= callable date)
- [ ] Document naming conventions in CLAUDE.domain-naming.md: `PrefShrDef`, `ConvPrefDef`, `DivTr`, `RedTr`, `CallTr`, `ConvTr`

## Definition of Done
- All types follow F# domain naming standard
- Constraints are validated at type-level or in aggregate constructor
- Changes are backward-compatible with existing common equity flows
- Unit tests cover happy path and edge cases

## Related
- UFL Equity Target-State Package V2 (Section 2.2, 11: Expansion Strategy)
- Reference: https://github.com/rodoHasArrived/Meridian-main/blob/main/docs/plans/ufl-equity-target-state-v2.md