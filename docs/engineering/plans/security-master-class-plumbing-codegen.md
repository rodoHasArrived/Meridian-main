# Security Master Class-Plumbing Codegen

**Status:** active
**Owner:** core-team
**Reviewed:** 2026-11-25
**Scope:** Engineering
**Roadmap:** `W11-PACK-002`. It was split from `W11-PACK-001` under `DEC-PACK-SPLIT-001` and switched to generated code under `DEC-PACK-CODEGEN-001`.

This plan records item E2 of the 2026-11-24 Security Master architecture review, which challenged
`W11-PACK-001`. The owner first chose a prototype. The hand-written source stayed authoritative,
and a test held the generator's output equal to it. On 2026-11-25 the owner switched to generated
code: the class declaration is now the source of truth for the mechanical plumbing.

## Problem

Adding a Security Master class takes about 18–20 hand-edited source files. The edits include:

- the F# `SecurityKind` match sites: `AssetClassRegistry.keyOf`, the classification row,
  `SecurityKindPersistence` (two matches), `SecurityMasterLegacyUpgrade` and
  `Interop.SecurityMaster`;
- `SecurityMasterMapping.ToSecurityKind` and `SchemaBoundDecodeClasses`;
- pack-readiness rows, `ClassificationLabelReader.KnownCaseNames` and upcaster-chain entries;
- two classification tables, the F# `AssetClassRegistry` and the C# `SecurityAssetClassCatalog`,
  which are kept equal only by tests.

PR #3736 makes `FS0025` (incomplete match) a build error in
`src/Meridian.FSharp/Meridian.FSharp.fsproj`. That PR is an open draft as of 2026-11-25, so on
`main` an incomplete match is still only a warning. Once it merges, the compiler will report a
missing F# arm. It still will not report a missing C# arm, a missing table row or a missing entry
in a string list.

## Split

| Item | Scope |
| --- | --- |
| `W11-PACK-002` | Generate the mechanical class plumbing from one declaration, built on `SecurityAssetTermsCodecBindings` and `SecurityAssetClassCatalog`. Compiled classes change through code review, not through runtime activation. |
| `W11-PACK-001` | Runtime pack governance (versioned, content-hashed packs with the `Draft → … → Retired` lifecycle) for profile-backed, `CustomAsset`-style classes only. |

## How it works

| File | Role |
| --- | --- |
| `src/Meridian.Contracts/SecurityMaster/SecurityAssetClassDeclarations.cs` | The declaration: one row per compiled class, in F# union order. A row holds only what the catalog and the codec bindings don't already say: the structural classification, the terms type when it isn't `{Name}Terms`, the union-case documentation, and the arms that aren't mechanical (the `Bond` decode wrapper, the StructuredCredit decode note and the profile-backed `CustomAsset`). |
| `tests/Meridian.Tests/SecurityMaster/SecurityClassPlumbingGenerator.cs` | Generates each region from the declaration. Codec arm parameters come from the binding flags (`ReadsResolvedTerms`, `VersionedSchedules`, `WritesConventionsFlag`, `Inputs`), using the same rules as `SecurityFlatCodecGenerator`. Classification cases are resolved against the F# unions by reflection: a label that names a nullary case renders as that case, and any other label renders as `OtherFamily` or `OtherSubType`. |
| `tests/Meridian.Tests/SecurityMaster/SecurityClassPlumbingGenerationTests.cs` | Regenerates every region and fails when the committed text differs. With `MERIDIAN_ACCEPT_GENERATED_CODEC=1` it writes the regions instead, as `SecurityFlatCodecGenerationTests` does for the flat codec. It also checks the declaration against the catalog, the catalog rows and the compiled union, and shows that one new row plus one binding adds an arm to every region. |

The generated code is ordinary committed source between `<generated:id>` and `</generated:id>`
comment markers, so it compiles with the surrounding hand-written code. It is not part of
`buildctl.py regen`; like the flat codec, it is held by the unit-test lane.

### Generated regions

| Region | File |
| --- | --- |
| `security-kind-cases`: `SecurityKind` union cases and their docs | `src/Meridian.FSharp/Domain/SecurityMaster.fs` |
| `asset-class-descriptors`: `AssetClassRegistry` classification rows | same |
| `asset-class-keys`: `AssetClassRegistry.keyOf` arms | same |
| `flat-writer-dispatch`: flat-writer arms for every class except `CustomAsset` | `src/Meridian.FSharp/Interop.SecurityMaster.fs` |
| `decode-dispatch`: `ToSecurityKind` arms | `src/Meridian.Application/SecurityMaster/SecurityMasterMapping.cs` |
| `asset-class-names`: `SecurityAssetClassNames` constants | `src/Meridian.Contracts/SecurityMaster/SecurityAssetClassCatalog.cs` |

The decode method list (`GeneratedDecodeClasses`) and the codec arms are generated separately by
`SecurityFlatCodecGenerator` from the binding table.

### Changing a class

1. Edit or add the row in `SecurityAssetClassDeclarations`. For a new flat class, also add its
   `SecurityAssetTermsCodecBindings` row and its hand-written catalog row.
2. Regenerate:

   ```bash
   MERIDIAN_ACCEPT_GENERATED_CODEC=1 dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --filter "FullyQualifiedName~SecurityClassPlumbingGenerationTests|FullyQualifiedName~SecurityFlatCodecGenerationTests"
   ```

3. Rebuild and run the same filter without the variable. Then make the deliberate semantic edits
   listed below; the compiler and the parity tests point at each one.

### Deliberate semantic edits (not generated)

These sites state what a class means rather than how it is wired, so a declaration row cannot
derive them:

- `SecurityKindPersistence`: the two matches listing which members of each embedded module a flat
  row persists.
- `SecurityMasterLegacyUpgrade.termsFromKind` (economic term modules per class) and the
  `Deposit` and `OtherSecurity` sub-type refinements in `classificationFromKind`.
- The `Interop.SecurityMaster` rate-unit arms and the hand-written `CustomAsset` writer arm.
- `SchemaBoundDecodeClasses`. Its order records the migration cohorts, and a sweep test already
  holds it to the catalog as a set.
- `KnownCaseNames`. It lists the F# classification unions, not the classes, and a test pins it by
  reflection. It changes only when a new nullary family or sub-type case is added.
- The body of each catalog row (lot unit, cash-flow behavior, terms version, aliases, upcaster
  chain) and the pack-readiness rows. These are product facts that belong to the catalog and the
  pack registry.

## Decisions and findings

1. **One classification table is authored.** The F# `AssetClassRegistry` rows are generated from
   the declaration. The C# side has no separate classification table to remove.
2. **Catalog order.** The declaration follows the F# union order, which also orders the registry
   and the name constants. The catalog's rows place `CustomAsset` after `OtherSecurity`. That order
   is public through `SecurityAssetClassCatalog.AssetClasses`, so it is kept as hand-written
   presentation order and checked as a set. It is not generated.
3. **Codec dispatch is derived from the binding flags.** The two declared exceptions are `Bond`,
   which calls a hand-written `DecodeBond` wrapper, and `CustomAsset`, which has no binding. The
   switch to generated code moved the `CustomAsset` decode arm to the end of the region (union
   order). This doesn't change behavior, because the arms match distinct string constants.
4. **Seven of the roughly 18–20 edits are generated** (six regions plus the catalog-row check). The
   rest are the semantic sites above. Adding a class is now one declaration row, one binding row
   and those deliberate edits.

## Validation

```bash
dotnet test tests/Meridian.Tests/Meridian.Tests.csproj -c Release --filter "FullyQualifiedName~SecurityClassPlumbingGenerationTests|FullyQualifiedName~SecurityAssetClassLiteralRatchetTests"
```
