### Phase 1.5.1: Add EquityClassification discriminator and PreferredTerms domain model

**Type:** Feature  
**Complexity:** Medium  
**Dependencies:** None (foundation)

**Description:**  
Extend the domain model to support equity classification (common, preferred, convertible) as the foundation for Phase 1.5.

**Acceptance Criteria:**  
- [ ] Add `EquityClassification` discriminated union to `src/Meridian.FSharp/Domain/SecurityMaster.fs`  
- [ ] Add `PreferredTerms` record with:  
  - `DividendRate: decimal option`  
  - `DividendType: DividendType` (Fixed, Floating, Cumulative)  
  - `RedemptionPrice: decimal option`  
  - `RedemptionDate: DateOnly option`  
  - `CallableDate: DateOnly option`  
  - `ParticipationTerms: ParticipationTerms option`  
  - `LiquidationPreference: LiquidationPreference`  
- [ ] Add `ConvertibleTerms` record with underlying, ratio, price, and date windows  
- [ ] Add `LiquidationPreference` union (Pari, Senior of decimal, Subordinated)  
- [ ] Update `EquityTerms` to include optional `Classification` field  
- [ ] Add unit tests validating term constraints  
- [ ] Update CLAUDE.domain-naming.md with naming conventions

**Definition of Done:**  
- All types follow F# domain naming standard  
- Constraints validated at type-level  
- Backward-compatible with existing common equity flows  
- Unit tests cover happy path and edge cases

**Reference:** https://github.com/rodoHasArrived/Meridian-main/blob/main/docs/plans/ufl-equity-target-state-v2.md