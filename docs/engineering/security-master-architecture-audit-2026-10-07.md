# Security Master Architecture Audit — 2026-10-07 (follow-up)

Follow-up to [the 2026-08-13 audit](security-master-architecture-audit-2026-08-13.md). Method is the
same: static reading of `src/Meridian.Contracts/SecurityMaster/`,
`src/Meridian.Application/SecurityMaster/`, `src/Meridian.Storage/SecurityMaster/`,
`src/Meridian.FSharp/Domain/SecurityMaster*.fs`, the accounting-event adapter, the browser passport
editor, and `tests/Meridian.Tests/SecurityMaster/` at `2bc041f1`. No build or test run was done.
This note re-checks each earlier risk (R1–R8) and adds what is new.

## Verdict

Better than in August, but still **not** clean and extensible across all asset classes. Five of
the eight earlier risks are closed or mostly closed. Most of the work left is in two places: the
profile-backed special case that wraps the domain model, and the read and term-probing layers,
which still follow one hand-written path per asset class.

## Status of the 2026-08-13 risks

| # | Risk | Status | Evidence |
|---|------|--------|----------|
| R1 | Lossy DU / profile-backed special case | **Partly fixed** | `CustomAsset` is now a real DU case (`SecurityMaster.fs:583,624`). The silent discard of the amend patch now throws instead (`SecurityMasterService.cs:1588-1597`). However, `IsProfileBackedCustomAsset` still hard-codes 7 asset-class strings (`:1610-1618`), and `KnownProfileAssetClasses` (`:1630`) is a second map in the service that sits beside the asset-profile registry. `LegacyAssetClass`/`LegacyAssetSpecificTerms` passthrough is still read in 13 files. |
| R2 | Terms schema not enforced | **Fixed** | Mapping, service, and projection registry now consult `SecurityAssetTermsSchema` (`SecurityMasterMapping.cs:453,487`, `SecurityTermsProjectionRegistry.cs:269`), and there are new round-trip and codec tests (`SecurityAssetTermsSchemaRoundTripTests`, `SecurityMasterProjectionCodecTests`). The `CustomAsset` schema now declares `category`/`subType`/`evidenceLinks`. The one gap left: the schema still does not declare element shapes inside arrays (see N2). |
| R3 | Factor schedule has no typed writer | **Fixed** | Typed `FactorScheduleEntry` list on `StructuredCreditTerms` (`SecurityMaster.fs:467-484`), written via `factorScheduleEntries` (`SecurityMasterMapping.cs:348`). |
| R4 | Alias-probing term reader | **Open** | `StructuredCashFlowTermsResolver` still carries about 66 alias entries. `SecurityMasterAccountingEventSourceAdapter` (797 lines) still reads factor schedules on its own path (`:613-619`) and does not use the resolver, so there are still two readers of the same terms. |
| R5 | Provenance is record-level only | **Fixed** | Durable per-field store `ISecurityFieldProvenanceStore` (migrations 027/028), with conflict winners written in the same transaction as the conflict close, plus version-guarded removal. |
| R6 | Operator edits never reach golden record | **Mostly fixed** | `ApprovedFieldEditCanonicalMergeHandler` merges approved `assetSpecificTerms.*` edits into the canonical stream through the amender. But the browser passport editor still takes a free-text path and a free-text value (`security-passport-editor.tsx:332-336`, placeholder `EconomicDefinition.Coupon`). That path is **outside** the merged namespace, so the editor's own example produces only an annotation. |
| R7 | `InvestmentFund` unusable / no validator guard | **Fixed** | Validator registered (`AssetClassValidatorRegistry.cs:286`), `SecurityAssetClassParityGuardTests` added, and a projection backlog guard that can only shrink (`SecurityAssetTermsSchemaTests.cs:44-60`). |
| R8 | Asset-pack registry is prose | **Open (grew)** | `SecurityAssetPackRegistry.cs` grew to 884 lines. It still validates policy prose by substring (`:342,717,727`), and its only consumer is `SecurityMasterOperationalReadinessService`. |

## What's solid

Identifier normalization and temporal identifiers, classification driven by the `AssetClassRegistry`
table, event sourcing with rebuild and optimistic concurrency, the maker-checker revision lifecycle,
schema-family versioning with upcasters, `FaceValueLot` for par instruments, and declarative
validators are all still solid (see the August note). New since August: the schema-checked
**write** registry for projections (`SecurityTermsProjectionRegistry`), field-level provenance, and
merge-on-publish for approved edits. Together these are what a defensible golden record needs.

## New findings

- **N1: Projection reads are still one hand-written store per class.** Writes are now
  table-driven, but 13 `I*ReferenceProjectionStore` / `Postgres*ReferenceProjectionStore` pairs
  (about 1,900 lines of hand-written SQL and row mapping) still exist, each with its own DI
  registration. Adding a projected class means a registry descriptor, DDL, **and** a new
  interface, implementation, and registration. The read side should be generated from the same
  descriptors as the write side.
- **N2: Array element contracts are not declared.** `SecurityTermsProjectionRegistry.cs:39-48`
  documents that child-table element keys (`principalSchedule[].paymentDate`, factor entries,
  covenants, swap legs) are pinned only by per-class decode tests, not by the terms schema. These
  arrays carry the cashflow and factor data, so this is where drift does the most financial damage.
- **N3: Asset-class string literals in core services.** About 36 class-name literals across
  `SecurityMasterService` (17, including bond-specific flat-versus-nested coupon handling at
  `:1091,1296,1342`), `SecurityMasterMapping` (13), and the workbench service. Each one has to be
  updated by hand whenever a class is added.
- **N4: Private-markets projection backlog.** `PrivateFundInterest`, `PrivateCompanyEquity`,
  `RealEstateHolding`, and `CommitmentGuarantee` are ops-capable but can only be queried through
  JSONB. The ratchet keeps the backlog from growing, but it has not shrunk yet.

## Top 5 priorities

1. **Remove the profile-backed special case from `SecurityMasterService`.** Have
   `IsProfileBackedCustomAsset` and `KnownProfileAssetClasses` derive from the asset-profile
   registry (or the `AssetClassRegistry` descriptor) instead of literals, so a new profile-backed
   class needs no change to the service. (R1, N3)
2. **Declare array element shapes in `SecurityAssetTermsSchema`.** Do this for schedules, legs,
   factor entries, and covenants, then make the projection registry validate child-table element
   keys against them. (N2)
3. **Make one term reader the only owner of cashflow and factor terms.** Move
   `SecurityMasterAccountingEventSourceAdapter` onto `StructuredCashFlowTermsResolver`, then reduce
   the alias table to vendor-ingest normalization at the edge, not every read. (R4)
4. **Generate projection reads from the descriptor registry.** Replace the 13 per-class stores with
   a generic descriptor-driven reader and typed query façades, then clear the private-markets
   backlog with descriptors only. (N1, N4)
5. **Drive the browser passport editor from the schema.** Offer only the declared term paths, with
   typed inputs, so an edit always lands in the merged namespace. Also decide whether
   `SecurityAssetPackRegistry` should gate admission or move to `docs/`. (R6, R8)
