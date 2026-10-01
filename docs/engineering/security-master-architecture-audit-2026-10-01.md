# Security Master Architecture Audit — 2026-10-01 (follow-up)

Follow-up to the [2026-08-13 audit](security-master-architecture-audit-2026-08-13.md). This pass
re-checks each 2026-08-13 finding (R1–R8) against current source at `e648c22e`, then records what
has come up since. Method: I read the source directly. I did not run a build or any tests. Line
references are to the files as they stand at that commit.

---

## Verdict

**Not yet clean across all asset classes, but much better.** Five of the eight 2026-08-13 findings
are closed or mostly closed. The term layer, which was the weak point, now has an enforced schema
and typed factor schedules. What remains falls into three groups:

- Asset-class special cases are still hard-coded in the service layer.
- Two projection mechanisms run side by side.
- Lot models above the ledger are keyed by symbol and use integer quantities, which blocks
  par, fractional, and multiplier-bearing instruments outside Security Master.

---

## Status of 2026-08-13 findings

| # | Finding | Status | Evidence |
|---|---|---|---|
| R1 | Profile-backed case not first-class; amend patch discarded | **Partly closed** | `SecurityKind.CustomAsset` is now a real DU case (`SecurityMaster.fs:583,624`). The silent patch discard now throws instead (`SecurityMasterService.cs:~1590`). The hard-coded class list `IsProfileBackedCustomAsset` (8 strings) and the `KnownProfileAssetClasses` / `AssetClassMetadataKeywords` maps are still in the service (`SecurityMasterService.cs:1606-1650`). |
| R2 | `SecurityAssetTermsSchema` is documentation only | **Closed** | It is now used by `SecurityAssetTermsFieldEditValidator`, `SecurityTermsProjectionRegistry`, the mapping/service/store, and the round-trip and codec tests (`SecurityAssetTermsSchemaRoundTripTests`, `SecurityMasterProjectionCodecTests`). |
| R3 | Factor schedule is a free-text string with no writer | **Mostly closed** | Typed `FactorScheduleEntry` list with `[0,1]` / ordering / maturity validation (`SecurityMaster.fs:467-484`, `SecurityMasterCommands.fs:239-259`). The legacy `FactorSchedule: string option` field is kept, and the accounting adapter still reads `profileFields.factorSchedule` from legacy terms (`SecurityMasterAccountingEventSourceAdapter.cs:427-442`). |
| R4 | Term reading done by probing aliases | **Open** | `StructuredCashFlowTermsResolver.cs:15-59` still holds about 30 hand-written alias arrays that are not derived from the schema. |
| R5 | Field-level provenance is synthesized | **Mostly closed** | `ISecurityFieldProvenanceStore` (migrations 027/028) is written by the amend seam and the workbench, and `ForField()` has no remaining callers. `Confidence` is still never populated (`PostgresSecurityMasterConflictService.cs:320`). |
| R6 | Operator edits never reach the golden record | **Partly closed** | `ApprovedFieldEditCanonicalMergeHandler` merges approved `assetSpecificTerms.*` edits into the canonical stream. Every other path is still an annotation only. See N3. |
| R7 | `InvestmentFund` has no validator; no catalog-vs-validator guard | **Closed** | Validator added (`AssetClassValidatorRegistry.cs:286`). Guard test added: `ValidatorRegistry_CoversExactlyTheCatalogAssetClasses` (`SecurityAssetClassParityGuardTests.cs:22`). |
| R8 | `SecurityAssetPackRegistry` is prose compiled into C# | **Partly closed** | English substring inference was replaced by a token switch (`SecurityAssetPackRegistry.cs:487`). All packs still share one `ContractSchema` / `ValidationRules` instance (`:404,416`). Validation still checks policy prose for the substring `"core ledger"` (`:342,727`). The only consumer is the readiness report, so the registry blocks nothing. |

---

## What's solid

- **Identity and identifier resolution:** check-digit normalization, temporal identifier windows,
  scoped aliases, uniqueness on the normalized primary identifier (migration 032), and conflict
  detection.
- **Table-driven classification:** `AssetClassRegistry`.
- **Event sourcing:** a snapshot-and-rebuild core, with optimistic concurrency and corporate-action
  amendment chains folded on read.
- **Governance:** draft → submit → approve → publish, override approvals, and asset-profile
  versioning and rollback.
- **Schema versioning:** separate payload families, a chain that upcasts payloads when they are
  read, and a promoted `schema_version` column.
- **Ratchet guards:** catalog-vs-validator parity, plus catalog-vs-projection parity with a
  shrink-only backlog and a frozen ceiling (`SecurityAssetTermsSchemaTests.cs:24-60`). Coverage
  gaps can no longer drift unnoticed.
- **Par lots:** `FaceValueLot` (par basis, booked factor, amortized basis).
- **Pricing:** dated price history and immutable price-selection receipts (migrations 034/035).

---

## New or carried-forward risks

### N1 — Lot models outside Security Master cannot represent most asset classes

There are four parallel lot types, and they have no shared contract:

| Lot type | Key | Quantity |
|---|---|---|
| `OpenLot` (`Backtesting.Sdk/OpenLot.cs:8-11`) | `string Symbol` | `long Quantity` |
| `TaxLot` (`Execution.Sdk/TaxLot.cs:16-19`) | `string Symbol` | `long Quantity` |
| `LedgerTaxLot` (`Ledger/LedgerTaxLot.cs:52`) | `Guid? SecurityId` (nullable) | decimal |
| `FaceValueLot` | `SecurityId` | face value |

The two SDK lots have no Security Master identity, no contract multiplier, and no factor. They also
cannot hold fractional quantities, so fractional equities, crypto, bond face, and option/future
multipliers cannot be represented in the paper-trading or backtest lanes. Nothing ties these lots to
Security Master terms, so a symbol rename or reuse breaks lot continuity.

### N2 — Two projection mechanisms side by side

`SecurityTermsProjectionRegistry` is the generic mechanism: it is driven by descriptors and checked
against the schema. So far it covers only `DirectLoan` and `StructuredCredit` (`:171,206`).

The 11 earlier classes still use one hand-written `I*ReferenceProjectionStore` plus
`Postgres*ReferenceProjectionStore` pair each, with one migration per class (005–015). Each pair is
its own place for drift.

The backlog of four private-markets classes (`PrivateFundInterest`, `PrivateCompanyEquity`,
`RealEstateHolding`, `CommitmentGuarantee`) still has no relational projection. These are the
institutional differentiators.

### N3 — The browser editor steers operators toward a path that does not merge

The browser passport editor still takes a free-text path and a free-text value. Its placeholder is
`EconomicDefinition.Coupon` (`security-passport-editor.tsx:332,336`), and that path is outside the
`assetSpecificTerms.*` namespace the merge handler applies. An edit made the way the UI suggests is
approved and published but stays an annotation. The browser lane still has no edit form driven by
`SecurityAssetTermsSchema`.

### N4 — The generic workbench service hard-codes class-specific term paths

`SecurityMasterWorkbenchCommandService.cs:1113-1118` hard-codes `par`, `issueDate`, `maturity`, and
`principalSchedule`, plus checks across fields that are specific to bonds. The four main
orchestration files are each 1.2k–1.9k lines:

- `SecurityMasterService.cs`
- `SecurityMasterWorkbenchCommandService.cs`
- `SecurityMasterMapping.cs`
- `PostgresSecurityMasterStore.cs`

Rules that span fields belong in `AssetClassValidatorRegistry` next to the existing
`FieldRule` / `DateOrderRule`. They should not live in the workbench.

---

## Top 5 priorities

1. **Move the profile→class resolution into data.** Put `IsProfileBackedCustomAsset`,
   `KnownProfileAssetClasses`, and `AssetClassMetadataKeywords` into the asset-profile catalog
   (the profile declares the class it resolves to). Add a guard test so that every catalog class
   with a profile is resolvable. This removes the last service-layer list of asset classes. (R1)
2. **Unify lots on a cross-asset lot contract.** Key it on `SecurityId`, use a decimal quantity,
   and carry the quantity basis (units, face, or contracts × multiplier) resolved from Security
   Master terms. Then adapt `OpenLot` and `TaxLot` to it. This is the main blocker for options,
   futures, bonds, and crypto in the execution and backtest lanes. (N1)
3. **Converge on `SecurityTermsProjectionRegistry`.** Migrate the 11 hand-written projection stores
   onto descriptors, then clear the four-class private-markets backlog. (N2)
4. **Make the editor schema-driven and fix the placeholder now.** Build the browser field-edit form
   from `SecurityAssetTermsSchema`. Either reject edit paths that will not merge, or label them
   "annotation" when they are submitted. (N3, R6)
5. **Generate term aliases from the schema and retire legacy factor fields.** Declare vendor
   aliases on the schema field, derive `StructuredCashFlowTermsResolver` from them, and remove the
   `FactorSchedule` string and the `profileFields.factorSchedule` read once legacy rows are
   upcast. (R3, R4)

Lower priority:

- Either make `SecurityAssetPackRegistry` gate admission (one `ContractSchema` per pack, bound to
  `SecurityAssetTermsSchema`) or move it to docs.
- Populate provenance `Confidence` from the conflict-authority policy.
