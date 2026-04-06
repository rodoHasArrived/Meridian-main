# CLAUDE.domain-naming.md — Meridian Domain Naming Standard

> **Scope:** All F# and C# code in `Meridian.FSharp`, `Meridian.Contracts`, `Meridian.Application`,
> and any layer that models financial instruments, security master, reference data, or market
> structure entities.
>
> **Authority:** This document supersedes ad-hoc naming in the files it covers. When a name in
> existing code conflicts with this standard, prefer the standard when writing *new* adjacent code;
> rename only when the inconsistency causes real confusion and the change is isolated to the domain
> layer.
>
> **Last Updated:** 2026-03-26

---

## 1. Objectives

Names in the financial domain layer must be:

- **Predictable** — a domain engineer can guess the name without looking it up.
- **Compact** — as short as possible without losing unambiguous meaning.
- **Consistent** — one naming pattern per concept class across every module.
- **Stable** — stable enough that they can become API routes, table names, and serialised keys
  without churn.
- **Mappable** — names should predict their storage column, JSON field, and DTO root.

---

## 2. General Principles

1. Prefer **consistency** over personal preference.
2. Prefer **domain meaning** over generic software vocabulary.
3. Use the **same root word everywhere** for the same business concept.
4. Keep names **as short as possible** without losing clarity.
5. Do **not invent alternate synonyms** for an existing concept.
6. Avoid decorative or redundant words: `Data`, `Info`, `Object`, `Item`, `Thing`, `Record`,
   `Model` — unless they distinguish a real concept.
7. A name must signal its **role**: entity, value object, union/category, trait, definition,
   relationship, identifier, or service.
8. New names must **fit the existing taxonomy** before introducing new vocabulary.

---

## 3. Casing Rules

| Context | Convention | Examples |
|---------|-----------|---------|
| F# types, modules, DU cases | `PascalCase` | `SecurityId`, `AssetClass`, `BondDef` |
| F# local values, parameters, helpers | `camelCase` | `secId`, `assetClass`, `validateTerms` |
| C# types, interfaces, methods | `PascalCase` | `SecurityId`, `ISecurityMasterService` |
| C# private fields | `_camelCase` prefix | `_securityId`, `_terms` |
| Domain acronyms (2–4 letters, standard codes) | `ALLCAPS` | `ISIN`, `CUSIP`, `FIGI`, `LEI`, `MIC` |
| Longer compound abbreviations | `PascalCase` | `CorpActId`, `OptChainId`, `BusDayConv` |

**Do not mix** `snake_case`, `kebab-case`, and `PascalCase` in source identifiers (only at external
schema boundaries, e.g. SQL column names or JSON wire format).

---

## 4. Vocabulary Root Words

### 4.1 Meridian-Established Roots (do not abbreviate these in type names)

Meridian's existing code uses **full words** for primary entity and type names. These are
established vocabulary — do not introduce abbreviated aliases next to them:

| Full Word | Meaning | Example Type Names |
|-----------|---------|-------------------|
| `Security` | Financial instrument | `SecurityId`, `SecurityMaster`, `SecurityClassification` |
| `Identifier` | An external/internal code for a security | `IdentifierKind`, `Identifier` |
| `AssetClass` | Top-level instrument class | `AssetClass`, `AssetFamily` |
| `Corporate Action` | CorpAct in compound names | `CorpActId`, `CorpActEvent`, `CorpActLnk` |
| `Option` | Opt in compound names | `OptChainId`, `OptDef` |
| `Future` | Fut in compound names | `FutDef` |
| `Exchange` | Exch in compound names | `ExchId`, `SecExchLnk` |
| `Issuer` | Iss in compound names | `IssId`, `SecIssLnk` |

### 4.2 Approved Abbreviations in Compound Names

Use these abbreviations **only in compound names** where the full word would make a name
excessively long (> ~20 characters):

| Abbreviation | Expands To | Use Case |
|---|---|---|
| `CorpAct` | CorporateAction | `CorpActId`, `CorpActEvent`, `CorpActLnk` |
| `Opt` | Option | `OptChainId`, `OptDef`, `OptRight` |
| `Fut` | Future | `FutDef`, `FutExpiry` |
| `Fx` | ForeignExchange | `FxDef`, `FxSpotRate` |
| `Exch` | Exchange | `ExchId`, `SecExchLnk` |
| `Iss` | Issuer | `IssId`, `SecIssLnk` |
| `Cpty` | Counterparty | `CptyId`, `NetAgrCptyLnk` |
| `Pref` | Preferred | `PrefShrDef`, `ConvPrefDef` |
| `Shr` | Share | `PrefShrDef`, `ComShrDef` |
| `Conv` | Convertible/Conversion | `ConvTr`, `ConvRatio`, `ConvPrefDef` |
| `Div` | Dividend | `DivRate`, `DivDt` |
| `Red` | Redemption | `RedTr`, `RedPx`, `RedTerms` |
| `Sen` | Seniority | `SenTr`, `SenCat` |
| `Own` | Ownership | `OwnTr` |
| `Inc` | Income | `IncTr` |
| `List` | Listing | `ListTr`, `ListedExp` |
| `Lnk` | Link (relationship) | `SecIssLnk`, `SecExchLnk` |
| `Def` | Definition (term sheet) | `BondDef`, `EquityDef`, `OptDef` |
| `Tr` | Trait (cross-cutting) | `OwnTr`, `IncTr`, `ConvTr` |
| `Ccy` | Currency | `CcyCode`, `PayCcy` |
| `Ctry` | Country | `RiskCtry`, `CtryCode` |
| `DayCnt` | DayCount | `DayCntConv` |
| `BusDayConv` | BusinessDayConvention | `BusDayConv` |

**Never create parallel abbreviations.** If `Security` is the established root, never introduce
`Sec` as a synonym type name beside it (`SecurityId` and `SecId` cannot both exist).

---

## 5. Concept Classes and Required Patterns

### 5.1 Identifiers — suffix `Id`

All identifier types **must end in `Id`**. Identifier fields must also end in `Id`.

| ✅ Correct | ❌ Incorrect |
|---|---|
| `SecurityId` | `SecurityIdentifier`, `SecurityKey`, `SecurityCode` |
| `CorpActId` | `CorporateActionIdentifier`, `CorpActKey` |
| `OptChainId` | `OptionChainId`, `OptionChainIdentifier` |
| `ExchId` | `ExchangeId`, `ExchangeIdentifier` |

Meridian already uses `SecurityId of Guid` as a single-case F# discriminated union — follow this
exact pattern for all new domain identifier types:

```fsharp
// Correct — single-case DU prevents Guid confusion
type SecurityId = SecurityId of Guid
type CorpActId  = CorpActId  of Guid
type OptChainId = OptChainId of Guid
```

### 5.2 Entity Types — short singular noun

A **durable business object** with its own identity and lifecycle.

- Use a short, singular noun.
- Do **not** suffix with `Entity` (unless needed to distinguish a framework artifact).
- Entity field names use full semantic nouns for major fields.

```fsharp
// Entity types
type Security  = { ... }
type Issuer    = { ... }
type Exchange  = { ... }
type CorpAct   = { ... }   // CorporateAction is too long in compound usage
type OptChain  = { ... }   // Option chain
```

### 5.3 Value Objects — singular noun, no `Id` unless it IS an identifier

```fsharp
type Identifier   = { Kind: IdentifierKind; Value: string; IsPrimary: bool; ... }
type Rating       = { Agency: string; Grade: string; Outlook: string option }
type MaturityTerms  = { MaturityDate: DateOnly option; IssueDate: DateOnly option; ... }
```

### 5.4 Definition Records — suffix `Def`

**Concrete payloads defining the terms of an instrument subtype** (think "term sheet"). These
capture subtype-specific fields and are distinct from general classification.

```fsharp
type BondDef    = { Coupon: CouponTerms; Maturity: MaturityTerms; ... }
type EquityDef  = { ShareClass: string option; VotingRights: string option; ... }
type OptDef     = { Underlying: SecurityId; Strike: decimal; ExpiryDt: DateOnly; ... }
type FutDef     = { Underlying: SecurityId; ExpiryDt: DateOnly; NotionalAmt: decimal option }
type FxDef      = { BaseCcy: string; QuoteCcy: string; Tenor: string option }
```

**Do not use** `Details`, `Info`, `Attributes`, `Spec`, or `Terms` as a substitute for `Def`
unless there is a distinct conceptual reason (e.g. `MaturityTerms` is a value-object sub-record
*within* a `BondDef`, not the definition record itself).

**Existing Meridian types and their `Def` equivalents going forward:**

| Existing (keep for now) | New `Def` form for future types |
|---|---|
| `CouponTerms` | Sub-record within `BondDef` (keep) |
| `MaturityTerms` | Sub-record within `BondDef` (keep) |
| `EconomicCallTerms` | Sub-record within `BondDef` (keep) |
| `SecurityTermModules` | Container record (keep) |
| New bond instrument type | `BondDef` |
| New equity instrument type | `EquityDef` / `ComShrDef` / `PrefShrDef` |
| New option instrument type | `OptDef` |
| New future instrument type | `FutDef` |

### 5.5 Category / Taxonomy Unions — marker suffix

Closed sets used for classification. Apply the correct semantic suffix:

| Suffix | Semantics | Examples |
|---|---|---|
| `Class` | Top-level instrument class | `AssetClass` |
| `Family` | Sub-class grouping | `AssetFamily` |
| `SubType` | Concrete instrument subtype | `SecuritySubType` |
| `Kind` | Operational/mechanical type | `IdentifierKind`, `StructureNodeKind` |
| `Cat` | Business category/subcategory | `IssueCat`, `VenueCat`, `NetAgrCat` |
| `Stat` | Status | `SecurityStat`, `CorpActStat` |
| `Role` | Relationship role | `OwnershipRole` |
| `Style` | Exercise/settlement/processing style | `ExerciseStyle`, `SettleStyle` |
| `Right` | Option right | `OptRight` |

```fsharp
// Existing — already correctly named
type AssetClass   = Equity | FixedIncome | Fund | ...
type AssetFamily  = Sovereign | CorporateDebt | CommonEquity | ...
type SecuritySubType = TreasuryBill | CommonShare | OptionContract | ...
type IdentifierKind  = Ticker | Isin | Cusip | Sedol | Figi | ...

// New types should follow the same pattern
type VenueCat     = Exchange | OtcMarket | DarkPool | Mtf
type IssueCat     = Sovereign | Corporate | Municipal | Securitized
type CorpActStat  = Announced | Confirmed | Settled | Cancelled
type ExerciseStyle = American | European | Bermuda
type OptRight      = Call | Put
```

Do **not** use multiple competing suffixes for the same conceptual layer.

### 5.6 Trait Records — suffix `Tr`

Cross-cutting behavioral or economic characteristics that apply to multiple instrument categories.

```fsharp
type OwnTr  = { HasVoting: bool; IsRestricted: bool; OwnershipCap: decimal option }
type IncTr  = { IsIncomeProducing: bool; DivRate: decimal option; PayFreq: string option }
type ConvTr = { IsConvertible: bool; ConvRatio: decimal option; ConvPx: decimal option }
type RedTr  = { IsRedeemable: bool; FirstRedDt: DateOnly option; RedPx: decimal option }
type SenTr  = { SeniorityLevel: int; IsSeniorSecured: bool }
type ListTr = { IsListed: bool; PrimaryExchId: ExchId option; ListedExp: DateOnly option }
```

Boolean fields in trait records **must** begin with `Is` or `Has`:

```fsharp
// ✅ Correct
IsConvertible: bool
HasVoting: bool
IsRedeemable: bool

// ❌ Incorrect
Convertible: bool
VotingRights: bool  // (if bool — use string when it is a category value)
Redeemable: bool
```

### 5.7 Link / Relationship Records — suffix `Lnk`

Many-to-many or role-based association types. Name pattern: `LeftRightLnk` in business reading order.

```fsharp
type SecIssLnk  = { SecurityId: SecurityId; IssuerId: IssId; Role: IssuanceRole }
type SecExchLnk = { SecurityId: SecurityId; ExchId: ExchId; IsPrimary: bool; ListDt: DateOnly option }
type CorpActSecLnk = { CorpActId: CorpActId; SecurityId: SecurityId; Role: CorpActSecRole }
```

### 5.8 Aggregates and Root Containers — singular noun

```fsharp
// ✅ Correct
type SecMaster  = { ... }   // bounded-context root
type CorpActBook = { ... }  // aggregate container

// ❌ Incorrect
type SecurityManager = ...    (behavior word, not structure)
type SecurityContainer = ...  (generic)
```

### 5.9 Modules and Namespaces — domain nouns or domain actions

```fsharp
// ✅ Correct
module SecurityValidation = ...
module CorpActRules = ...
module SecurityClassification = ...  // matches type name

// ❌ Incorrect
module Helpers = ...
module SecurityUtils = ...
module Misc = ...
```

---

## 6. Field Naming Rules

| Field Type | Rule | Examples |
|---|---|---|
| Boolean | Begin with `Is` or `Has` | `IsPrimary`, `IsCallable`, `HasVoting`, `IsBullet` |
| Date (calendar day) | End with `Dt` or `Date` (prefer `Dt` in new code) | `MaturityDt`, `IssueDt`, `ExpiryDt`, `FirstCallDt` |
| Instant / timestamp | End with `At` or use `AsOf` | `RecordedAt`, `AsOfUtc`, `CreatedAt` |
| Monetary amount | End with `Amt` | `GrossAmt`, `NetAmt`, `NotionalAmt`, `FaceAmt` |
| Rate | End with `Rate` | `DivRate`, `FixedRate`, `CpnRate`, `FloorRate`, `CapRate` |
| Price | End with `Px` | `CallPx`, `ConvPx`, `RedPx`, `IssuePx` |
| Ratio | End with `Ratio` | `ConvRatio`, `ExRatio` |
| Count / frequency | Use `Count` or `Freq` | `PayFreq`, `CpnFreq`, `SettleLagDays` |
| Currency code | `Ccy` suffix or `CcyCode` | `PayCcy`, `SettleCcy`, `BaseCcy` |
| Country code | `Ctry` suffix | `RiskCtry`, `DomicileCtry` |
| Spread (basis points) | End with `Bps` | `SpreadBps`, `OasBps` |

**Do not** use `Val` unless the concept is truly generic and no better noun exists. Do not mix date
suffixes (`Date`, `Dt`, `EffectiveDate`, `EffDt`) within the same domain layer — use `Dt` in new
F# code.

---

## 7. F#-Specific Patterns

### 7.1 Identifier Types — single-case discriminated union

```fsharp
// ✅ Always use single-case DU, not a type alias
type SecurityId = SecurityId of Guid  // ✅
type SecurityId = Guid                // ❌ (type alias loses type safety)
```

### 7.2 Category Unions — `[<RequireQualifiedAccess>]`

```fsharp
[<RequireQualifiedAccess>]
type AssetClass =
    | Equity
    | FixedIncome
    | Fund
    | ...
```

This prevents bare `Equity` or `FixedIncome` from polluting the value namespace.

### 7.3 Type + Module Pattern

Pair every significant record type with a matching module of the same name:

```fsharp
type SecurityId = SecurityId of Guid

[<RequireQualifiedAccess>]
module SecurityId =
    let value (SecurityId id) = id
    let create raw = SecurityId raw
    let isValid (SecurityId id) = id <> Guid.Empty
```

For value object modules where the type and module would clash, use the type name as module name:

```fsharp
type Identifier = { Kind: IdentifierKind; Value: string; ... }

[<RequireQualifiedAccess>]
module Identifier =
    let isActiveAt asOf (id: Identifier) = ...
    let normalizeValue (value: string) = ...
```

### 7.4 Module Function Naming Vocabulary

| Function role | Naming convention | Examples |
|---|---|---|
| Construct/build | `create`, `make`, or named smart ctor | `createBond`, `makeOptDef` |
| Normalize/canonicalize | `normalize`, `normalizeX` | `normalize`, `normalizeValue` |
| Query/predicate | short verb or `isX`/`hasX` | `isActiveAt`, `isValid`, `hasCoupon` |
| String representation | `asString`, `kindName`, `className` | `assetClassName`, `kindName` |
| Transform/copy-with | `withX`, `mapX` | `withNormalizedCoreFields`, `mapTerms` |
| Validation | `validate`, `validateX` | `validateCommonTerms`, `validateIdentifiers` |

### 7.5 Discriminated Union Case Naming

- DU case names must be **nouns or noun phrases**, not verbs.
- If a payload type ends in `Def`, the union case should reuse the same root:

```fsharp
// ✅ Correct
type InstrumentTerms =
    | BondDef    of BondDef
    | EquityDef  of EquityDef
    | OptDef     of OptDef
    | FutDef     of FutDef

// ❌ Incorrect
type InstrumentTerms =
    | CreateBond of BondDef      // verb in case name
    | EquityData of EquityDef    // wrong suffix
```

---

## 8. C#-Specific Patterns

### 8.1 Domain DTOs in `Meridian.Contracts`

- Record types use PascalCase throughout.
- Id fields follow the `XxxId` pattern (e.g. `SecurityId`, `IssuerId`).
- No `Base` suffix unless there is a true C# inheritance boundary.

```csharp
// ✅ Correct
public sealed record SecurityDto(
    Guid SecurityId,
    string DisplayName,
    string AssetClass,
    DateTimeOffset EffectiveFrom);

// ❌ Incorrect
public sealed record SecurityDataModel(...);
public sealed record SecurityInfoRecord(...);
```

### 8.2 Service and Interface Naming

```csharp
ISecurityMasterService      // ✅  domain service interface
ISecurityMasterQueryService // ✅  read-side segregation
SecurityMasterService       // ✅  implementation
SecurityProcessor           // ❌  (vague behavior noun)
SecurityManager             // ❌  (manager = god class smell)
```

### 8.3 Exception Types

All domain exceptions must derive from `MeridianException`. Name with the subject + `Exception`:

```csharp
SecurityMasterException   // ✅
ValidationException       // ✅  (already defined in Core)
CorpActException          // ✅  (hypothetical)
GenericSecurityError      // ❌
```

---

## 9. Anti-Patterns to Reject

| Anti-Pattern | Why | Fix |
|---|---|---|
| `SecurityDataModel` | Redundant `Data` + `Model` | `Security` or `SecurityDto` |
| `CounterPartyEntity` | Wrong casing + redundant `Entity` | `Counterparty` or `CptyId` |
| `PreferredShareAttributes` | `Attributes` is meaningless | `PrefShrDef` |
| `ExchangeLinkTable` | `Table` encodes storage concern | `SecExchLnk` |
| `AssetTypeInfo` | `Info` is empty | `AssetClass` (it IS a class, not info about it) |
| `SecurityRecordModel` | `Record` + `Model` double decorations | `Security` |
| `Type` suffix without category meaning | Ambiguous | Use `Class`, `Kind`, `Cat`, `Stat` as appropriate |
| `secIdentifier` and `SecurityId` coexisting | Synonym leakage | Pick one: `SecurityId` |
| Boolean field `Convertible: bool` | Missing Is/Has | `IsConvertible: bool` |
| `MaturityDate` in new F# code | Inconsistent with `Dt` convention | `MaturityDt: DateOnly option` |
| `Db`, `Api`, `Json`, `Ef` prefix on domain types | Transport concern in domain name | Move to adapter layer |

---

## 10. Naming Decision Checklist

When creating a new name, answer these questions in order:

1. What **business concept** is this?
2. Is it an entity, value object, category union, trait, definition, link, or aggregate?
3. What **repository-standard root word** already exists for this concept? (Check sections 4–5.)
4. What **mandatory suffix/pattern** applies? (`Id`, `Def`, `Tr`, `Lnk`, `Stat`, `Kind`, etc.)
5. Can the name be **shortened** without losing clarity? (Apply abbreviations from section 4.2 only.)
6. Does it match **nearby peer names** exactly in style?

If two candidate names are both correct, choose the one that:
1. matches the repository standard more closely,
2. is shorter,
3. remains obvious to a financially literate engineer reading the code for the first time.

---

## 11. Quick Reference Table

### Good vs. Bad

| ✅ Good | ❌ Bad |
|---|---|
| `SecurityId` | `SecurityIdentifier`, `secKey`, `secCode` |
| `BondDef` | `BondData`, `BondInfo`, `BondAttributes` |
| `AssetClass` | `AssetTypeInfo`, `AssetTypeData` |
| `IdentifierKind` | `IdentifierType`, `IdentifierCategory` (inconsistent with `Kind` standard) |
| `IsCallable: bool` | `Callable: bool`, `CallableFlag: bool` |
| `MaturityDt: DateOnly option` | `MaturityDate`, `MtrDt`, `EffectiveMaturityDate` |
| `CpnRate: decimal option` | `CouponRateValue`, `CpnRateData` |
| `ConvPx: decimal option` | `ConversionPrice`, `ConvertiblePriceVal` |
| `SecIssLnk` | `SecurityIssuerLinkTable`, `SecurityToIssuerAssociation` |
| `OwnTr` | `OwnershipFeatures`, `OwnershipData`, `OwnershipFlags` |
| `CorpActStat` | `CorporateActionStatus`, `CorpActStateType` |

---

*See also:*
- [`CLAUDE.fsharp.md`](CLAUDE.fsharp.md) — F# domain module patterns and interop rules
- [`CLAUDE.providers.md`](CLAUDE.providers.md) — provider adapter naming (different conventions)
- [`../_shared/project-context.md`](../../generated/project-context.md) — codebase statistics and key abstractions
- [`../../../docs/plans/ufl-supported-assets-index.md`](../../plans/ufl-supported-assets-index.md) — UFL asset package index
