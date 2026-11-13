# Adding a Security Master Asset Class

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-11-13
**Audience:** contributors and agents adding a first-class Security Master kind

This is the checklist for adding a first-class asset class, or for giving a governed CustomAsset
profile a first-class home it can be promoted to. It lists every file a new class touches today and
the tests that fail when one is missed. The canonical-home rules are in
[ADR-022](../adr/022-canonical-asset-class-homes.md); the terms format and version rules are in
[ADR-024](../adr/024-terms-payload-format-and-version-policy.md).

Use `CommitmentGuarantee` or `PrivateFundInterest` as the worked example: search the repository for
the class name and compare each hit with this list.

## 1. F# domain (`src/Meridian.FSharp/`)

| File | What to add |
| --- | --- |
| `Domain/SecurityMaster.fs` | The `<Class>Terms` record, the `SecurityKind` case, the classification row (asset class, family, sub-type, issuer type, derivative flag) and the kind-name arm. |
| `Domain/SecurityClassification.fs` | The `SecuritySubType` case and its label; an `AssetFamily` only when no existing family fits. |
| `Domain/SecurityKindPersistence.fs` | The kind's persistence arms, including the issue-date arm. |
| `Domain/SecurityMasterCommands.fs` | The kind's command invariants (`validateKindInvariants`), each with a stable error code. |
| `Domain/SecurityMasterLegacyUpgrade.fs` | The kind's arm in the legacy-upgrade match, so a replayed record keeps its terms. |
| `Interop.SecurityMaster.fs` | The C# facade arm for the kind. |
| `Domain/SecurityEconomicDefinition.fs`, `Domain/SecurityTermModules.fs` | Only when the class needs a term module the existing modules do not cover. |

A missing match arm is an incomplete-match warning (FS0025), not a build error, so read the F#
build warnings for each file above. Run
`dotnet test tests/Meridian.FSharp.Tests -c Release /p:EnableWindowsTargeting=true` after this step.

## 2. C# contracts (`src/Meridian.Contracts/SecurityMaster/`)

| File | What to add |
| --- | --- |
| `SecurityAssetClassCatalog.cs` | The `SecurityAssetClassDescriptor`: asset pack ids, lot unit, cash-flow behaviour, `ProjectionKind`, preferred identifier kinds, Asset Operations capabilities, aliases, accounting class, `SupportsProfileBackedTerms`, `ClassificationLabels` and, for a promotion target, `ReclassifyingProfileIds`. |
| `SecurityAssetTermsSchema.cs` | The class's fields in `FieldsByAssetClass`. Declaration order is the required-field refusal order on read. A field added after version 1 needs `.Since(n)` and, when required, `ReadsAbsentAs(...)`. List the class in `ReadsProfileResolvedTerms` when it reads profile-resolved terms. |
| `SecurityAssetTermsCodecBindings.cs` | The flat codec bindings for the class's members, when the class uses the schema-bound codec. |
| `SecurityAssetSpecificTermsUpcasterChain.cs`, `SecurityRateFieldUnits.cs` | Rate units and any key renames the class's terms carry. |
| `ClassificationLabelReader.cs`, `SecurityAssetPackRegistry*.cs` | The classification label and the pack's readiness specification. |

## 3. Application codec and validation (`src/Meridian.Application/SecurityMaster/`)

| File | What to add |
| --- | --- |
| `SecurityMasterMapping.cs` | The decode arm in `ToSecurityKind`, and the class in `SchemaBoundDecodeClasses` when it decodes through the schema-bound reader. |
| `Validation/AssetClassValidatorRegistry.cs` | The class's semantic rules. Schema rules are generated from the schema. |

## 4. Storage (`src/Meridian.Storage/SecurityMaster/`)

| File | What to add |
| --- | --- |
| `SecurityTermsProjectionRegistry.cs` | The `SecurityTermsProjectionDescriptor`: table, columns, child and satellite tables. The registry writes the projection and `RegistryProjectionReadService.cs` reads it; no hand-written writer or reader is needed. |
| `Migrations/<ordinal>_<slug>.sql` | The projection table. Scaffold the ordinal with `python build/scripts/schema-control.py new-migration --migration-set security-master --name <slug>`, never reuse a retired ordinal, and never edit an applied migration. See [PostgreSQL schema control](README.md#postgresql-schema-control). |

Regenerate the schema-control outputs (`database/manifest/**`, `docs/generated/database/**`) as the
schema-control guide describes.

## 5. Fixtures (`tests/fixtures/security-master/`)

| Fixture | What to add |
| --- | --- |
| `terms-versions/<Class>.json` | The class's terms-version manifest. |
| `terms-golden/security-terms-write-golden.json` | The class's write golden. A mismatch writes a `.received.json` file next to it; review it before you replace the golden. |
| `replay-corpus/` | A replay case when the class has a legacy or economic-terms history. |
| `src/Meridian.Ui/dashboard/src/test/fixtures/security-asset-term-form-parity.json` | The browser term-form parity row. |

## 6. A promotion target for a CustomAsset profile

When the class is where a governed profile's records should live:

- Declare the profile id in the class's `ReclassifyingProfileIds`. A profile id belongs to one
  class; a second claim fails at type initialization.
- Set `SupportsProfileBackedTerms: true` and give the class a registry descriptor.
  `CustomAssetPromotionRoundTripTests.EveryReclassifyingProfile_TargetsAProfileBackedClassWithARegistryProjection`
  fails otherwise.
- Records already pinned to the profile stay `CustomAsset` until an operator promotes them. A write
  without a terms patch keeps the stored `CustomAsset` class. Any write that carries the pinned
  envelope (including an approved field edit or a term-block patch) promotes the record, so repair
  and promotion are one amendment. Operators use the
  [CustomAsset promotion runbook](../operators/security-master-custom-asset-promotion.md).

## 7. Tests that check the list

Run these after the change:

```bash
dotnet test tests/Meridian.Tests -c Release /p:EnableWindowsTargeting=true --filter "FullyQualifiedName~SecurityAssetClassOperationalWiringTests|FullyQualifiedName~SecurityAssetClassRegistryCoverageTests|FullyQualifiedName~SecurityAssetClassSurfaceParityTests|FullyQualifiedName~SecurityAssetClassCatalogTests|FullyQualifiedName~SecurityTermsProjectionRegistryTests|FullyQualifiedName~SecurityAssetTermsSchema|FullyQualifiedName~SecurityMasterRequiredReadOrderTests|FullyQualifiedName~SecurityAssetClassTermsHistoryTests|FullyQualifiedName~SecurityTermsWriteGoldenTests|FullyQualifiedName~SecurityAssetClassLiteralRatchetTests|FullyQualifiedName~CustomAssetPromotion"
dotnet test tests/Meridian.FSharp.Tests -c Release /p:EnableWindowsTargeting=true
npm --prefix src/Meridian.Ui/dashboard run test
```

The PostgreSQL goldens (`RegistryMigratedProjectionGoldenTests`, `ProjectionFanOutPostgresTests`)
are in the `Integration` category. Run them against Docker locally or through the manual
`targeted-test.yml` workflow with `mode=dotnet-filtered`.
