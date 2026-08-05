# Security Master Architecture Review

**Scope:** Security Master schema, models, and extension points across equities, options, bonds,
loans, cash, funds, derivatives, and structured products, assessed against institutional-finance
reference-data requirements.

**Method:** source review of `src/Meridian.FSharp/Domain/SecurityMaster*.fs`,
`src/Meridian.FSharp/Interop.SecurityMaster.fs`, `src/Meridian.Contracts/SecurityMaster/`,
`src/Meridian.Application/SecurityMaster/`, `src/Meridian.Storage/SecurityMaster/`,
`src/Meridian.ReferenceData/SecurityMaster/`, `src/Meridian.Ui.Shared/Services/`, and
`tests/Meridian.Tests/SecurityMaster/`. No code was changed. Findings are evidence-linked; no
build or test run was performed as part of this review.

---

## Verdict

The Security Master is **not** a pile of asset-specific special cases. The classification,
capability, validation, and identifier lanes are genuinely data-driven and defended by tests, and
several of the registries carry comments showing they were consolidated deliberately after real
drift incidents. That is a stronger foundation than most reference-data layers reach.

The risk is concentrated in one place, and it is structural rather than cosmetic:

> **The write-side domain records are narrower than the read-side typed models, and the gap is
> bridged by tolerant alias-probing that yields nulls instead of errors.**

Schedules and legs — the parts of an instrument that actually drive accrual, paydown, and
amortization — are the fields where this gap bites. A security can be created and amended through
the canonical path, pass every validator, and still be economically unusable downstream, silently.

---

## What is solid

**Asset-class classification is single-sourced.** `AssetClassRegistry`
(`SecurityMaster.fs:604-660`) holds one descriptor table plus one `keyOf` arm per class; asset-class
name, the derivative predicate, and the canonical `SecurityClassification` all derive from it rather
than being re-encoded per call site. `keyOf` is the only `SecurityKind` match used for
classification, and the table is locked to it by tests.

**Capability metadata is declarative.** `SecurityAssetClassCatalog` carries 27 descriptors
(cashflow-schedule default, face-value-lot usage, create-workflow support, preferred identifier
kinds, `AmortizesTowardPar`, `RequiresMaturity`) behind a non-throwing `GetOrDefault` that degrades
unknown input to an `Unknown` descriptor instead of failing reads.

**The term-field contract is a table, not three hand-maintained copies.**
`SecurityAssetTermsSchema` declares key/type/required/aliases per asset class, and its header
documents exactly why: the serialize side, the deserialize side, and the relational decoders had
already drifted once (the nested `coupon` object the serializer never wrote, landing null bond
coupon columns). Backed by `SecurityAssetTermsSchemaTests` and `SecurityMasterProjectionCodecTests`.

**Validation is rule objects, not switches.** `AssetClassValidatorRegistry` composes
`JsonAssetClassValidator` instances from `FieldRule` / `DateOrderRule` primitives with stable
`SM_*` codes, resolved by asset class through a dictionary.

**Identifier resolution is institutional-grade.** 17 identifier kinds with real checksum validation
— ISIN mod-10, CUSIP, SEDOL, LEI (ISO 7064 mod-97), FIGI — plus normalization, a normalized lookup
table (migration `016`), time-bounded identifiers (`ValidFrom`/`ValidTo`), scoped aliases, and a
historical symbol timeline resolver for post-corporate-action ticker reuse.

**Mixed-version read tolerance is a deliberate discipline.** `Unknown` members on
`SecurityIdentifierKind`, `SecurityStatusDto`, and `SecurityAliasScope` plus
`SecurityMasterEnumReads.ParseOrFallback` keep rows written by a newer node readable, while the
strict write mapping rejects `Unknown` so an unrecognized value is never silently re-persisted.

**Schema versioning learned from a real leak.** `AssetSpecificTermsSchema` and `EconomicTermsSchema`
were split into separate families precisely because an economic-terms version had leaked into
asset-specific-terms acceptance checks; `SecurityMasterSchemaVersions` remains only as a
back-compat facade. An upcaster chain exists with dedicated tests.

**Governance and auditability are real.** Event-sourced core with snapshots and a rebuild
orchestrator; `ISecurityMasterRevisionStore` enforces atomic Draft → Submitted → Approved →
Published transitions with the submitting workflow id durably bound so approval can be restricted
to the same lane; conflict detection emits typed `IdentifierAmbiguity` / `EconomicTermMismatch` /
`CommonTermMismatch` rows with resolution attribution captured atomically.

**Coverage is tested per asset class.** `SecurityMasterAssetClassSupportTests` exercises create
across 17 classes including the alternative-asset set.

---

## What is at risk

### R1 — Schedules and legs are written narrower than they are read (highest severity) — *addressed*

> **Status:** fixed in this branch. `StructuredCreditTerms.FactorSchedule` is now a typed
> `FactorScheduleEntry list` and `SwapLeg` carries the full per-leg economics; both serialize into
> the shapes their readers accept, the schedule now also reaches the economic-terms paydown reader,
> and `SecurityAssetTermsSchema` declares the element shapes so the codec tests can catch this class
> of drift. The analysis below is retained as the record of what was wrong and why.


The read side models a full economic shape. The write side does not produce it. Nothing fails.

**Factor schedules.** `StructuredCreditTerms.FactorSchedule` is `string option`
(`SecurityMaster.fs`), serialized as a JSON string (`Interop.SecurityMaster.fs:277-286`), and
declared `String` in `SecurityAssetTermsSchema.cs:244`. But
`StructuredCashFlowTermsResolver.ReadFactorSchedule` accepts only `JsonValueKind.Array`, and
`SecurityMasterAccountingEventSourceAdapter.EnumerateFactorScheduleArrays` likewise requires an
array. Consequence: a CLO / CMO / MBS tranche written through the canonical domain path can never
carry a usable factor schedule. `StructuredCashFlowTerms.FactorAsOf` falls back to the scalar
`CurrentFactor`, so the position amortizes off a single static factor. Worse,
`SecurityMasterAccountingEventService` sets `RequiresFactorSchedule` when `currentFactor < 1`, then
raises a **High**-severity coverage break and `continue`s past paydown-event generation — so these
securities are permanently blocked from paydown accounting by a condition the write path cannot
satisfy.

**Swap legs.** F# `SwapLeg` has four fields (`LegType`, `Currency`, `Index`, `FixedRate`). The
read-side `StructuredCashFlowLeg` has eleven — `Notional`, `PaymentFrequency`,
`DayCountConvention`, `SpreadBps`, `CurrentIndexRate`, `ExchangesPrincipal`, `Direction`, and
others. A swap written through the domain yields legs with no notional, no frequency, and no day
count, which is to say no computable payment. `Currency` is carried on the write side and has
nowhere to land on the read side.

This is the same defect class `SecurityAssetTermsSchema` was built to prevent. It escaped because
the schema table declares only the top-level field type; for `Array` and `Object` fields its own
remarks state that "their inner shapes are not enumerated here" — which is exactly where these two
defects live. `DirectLoanTerms.PrincipalSchedule` is a correctly typed `PrincipalPaymentEntry list`,
so the fix has an in-repo pattern to copy.

### R2 — Read tolerance has leaked into the write path

`SecurityMasterMapping.ToSecurityKind` ends in `_ => SecurityKind.NewOtherSecurity(...)`
(`SecurityMasterMapping.cs:399`). The inline comment justifies it correctly — for reads. But the
same mapping sits on the amend path: `AmendTermsInternalAsync` → `ToRecord(currentProjection)` →
domain → `CreateProjectionFromResult`. For an asset class this node's switch does not know, the
record is relabeled `OtherSecurity` and its terms collapse to the five-field `OtherSecurity` shape
*before the new event envelope is written*. Read tolerance becomes write-side data loss.

The rescue is narrow and hardcoded: `GetProfileBackedAssetClassOverride` restores the class and raw
terms only for a whitelist of seven class-name strings, and only when a `customProfileId` is present
(`SecurityMasterService.cs:386-393`). `CustomAsset` — the profile-backed extensibility escape hatch
— has no `SecurityKind` case at all (`SecurityMasterMapping.cs:307` folds it into `OtherSecurity`),
so its `profileFields` survive only via that post-hoc projection override, never through the domain.
Profile-backed records therefore also bypass domain validation entirely.

### R3 — The governed passport editor does not amend the golden record

`SecurityMasterWorkbenchCommandService.UpdateSecurityFieldAsync` writes an operator-override overlay
— a `Dictionary<string, string>` — and explicitly does not append to the economic event stream
(documented at lines 92-96, with a sound reason: the stream is replayed verbatim and a partial
payload would clobber the economic definition).

The gap is what happens next. Overrides are read by exactly one consumer, `SecurityValidationService`.
Nothing in the projection, economic-definition, cash-flow, amortization, or accounting-event path
reads them. An operator correcting a bond's day count or a pool factor through the governed editor
changes what validation sees and nothing that amortization computes. The overlay is also untyped and
never validated against `SecurityAssetTermsSchema`. Today the workflow is neither an annotation
layer nor an amendment path — it has the approval ceremony of the latter and the reach of the
former.

### R4 — Field-level provenance is modeled but not persisted

`SecurityFieldProvenance` exists, with a `ForField` projection that stamps record-level provenance
onto a field path — but it has **zero consumers** outside its own definition file, and there is no
field-provenance table. Provenance is one `{sourceSystem, sourceRecordId, asOf, updatedBy, reason}`
document per record version. "Which vendor asserted this security's maturity, as of when, and with
what confidence" is not answerable from the golden record. Conflict detection produces field-level
mismatch rows, so the raw material exists — it is just never durably attributed. For a golden-record
security master this is normally a hard requirement, not a nice-to-have.

### R5 — The relational projection lane is hand-written and covers 11 of 26 classes

Eleven migrations (`005`–`015`), eleven store interfaces, eleven Postgres implementations, eleven
row records, eleven DI registrations, and an entry in the writer registry at
`PostgresSecurityMasterStore.cs:45-55`. Adding one asset class to this lane means roughly six new
artifacts and a migration.

Two consequences. First, every alternative asset — `DirectLoan`, `StructuredCredit`,
`PrivateFundInterest`, `PrivateCompanyEquity`, `RealEstateHolding`, `CommitmentGuarantee`,
`InvestmentFund`, `Repo`, `CashSweep`, `TreasuryBill`, `CommercialPaper`, `Cfd`, `Warrant`,
`CustomAsset` — has no relational projection, so it is queryable only through the JSON terms blob.
Second, only three of the eleven stores have a consumer (`Meridian.Instruments` Bond/Option/Equity
projection services); the other eight are registered, written, and never read.

### R6 — Adding an asset class touches roughly ten hand-maintained registries

`SecurityKind` DU + terms record → `AssetClassRegistry.keyOf` + descriptors → `Interop` serialize
arm → `SecurityMasterMapping` deserialize arm → `SecurityAssetTermsSchema` →
`SecurityAssetClassCatalog` → `AssetClassValidatorRegistry` → `InstrumentTypeDescriptorCatalog` →
`SecurityKindMapping` → `SecurityAssetPackRegistry` → `SecurityMasterOperationalReadinessService`
coverage table → optional projection artifacts.

Several pairs are locked together by tests, which is why this is a maintenance cost rather than a
correctness bug today. But test-locking is the mitigation, not the fix: no table is *derived* from
another, so the count only grows.

### R7 — `SecurityAssetPackRegistry` is prose-as-data, not an executable extension point

802 lines describing lifecycle events, valuation methods, journal templates, admission policy, and
reporting taxonomy — all as strings, validated for internal consistency, consumed only by
`SecurityMasterOperationalReadinessService` (a readiness report) and by tests. It documents the
intended asset-pack contract well. It does not drive dispatch, so declaring a new pack still
requires every edit in R6. The registry reads as a design intent that was never wired to the
runtime.

### R8 — Opaque strings where structure is required

Beyond `FactorSchedule` (R1): `RealEstateHoldingTerms.DebtStack`, `PrivateFundInterestTerms.Lockup`,
`CommitmentGuaranteeTerms.Collateral`, `PrivateCompanyEquityTerms.TransferRestrictions`, and
`Covenant.Threshold` are all free-text. Covenant thresholds in particular cannot be tested against
a reported value, which makes covenant monitoring on direct loans a manual read rather than a
computable check.

---

## Top priorities

**1 — Close the write/read term-shape gap (R1).** *Done in this branch.*
`StructuredCreditTerms.FactorSchedule` is a typed entry list mirroring `PrincipalPaymentEntry`, and
`SwapLeg` carries the fields `StructuredCashFlowLeg` models. The schedule also flows into the
economic-terms `structuredProduct` block as `{asOfDate, priorFactor, currentFactor}` transitions,
which is what the accounting-event adapter actually reads — fixing only the asset-terms side would
have left the paydown coverage gate unsatisfiable. `SecurityAssetTermsSchema` now declares element
shapes for economically meaningful arrays, and `SecurityMasterTermScheduleCodecTests` measures the
serializer against those declarations end-to-end.

**2 — Separate read tolerance from write tolerance (R2).** Keep the `_ =>` fallback on the read
path; on the amend path, either preserve the original class and terms verbatim or fail loudly. Give
`CustomAsset` a first-class `SecurityKind` case carrying the profile envelope, and retire the
seven-name whitelist in `SecurityMasterService`. This is the single change that most improves
extensibility to new asset classes, because today a class the node does not know is not merely
unsupported — it is destructive on amend.

**3 — Resolve the operator-override contract (R3).** Decide whether approved overrides are
authoritative. If yes, merge them into the economic read model with typing and
`SecurityAssetTermsSchema` validation. If no, make the passport editor amend the record through the
governed revision path. The current middle state gives operators approval ceremony without economic
effect, which is the worst of both for auditability.

**4 — Persist field-level provenance (R4).** Give `SecurityFieldProvenance` a durable store keyed by
`(securityId, fieldPath, version)`, written on amend from the same seam that already detects field
conflicts. Without it the platform cannot answer basic golden-record lineage questions during audit.

**5 — Generalize or retire the relational projection lane (R5).** Either generate the per-class
tables and decoders from `SecurityAssetTermsSchema` so coverage follows the schema automatically, or
delete the eight unread stores and keep the three that have consumers. Maintaining eleven
hand-written lanes for a surface that serves three is cost without return.

R6, R7, and R8 are follow-on work that becomes much cheaper once 1, 2, and 5 land: a schema that
describes nested shapes and a domain that carries every class faithfully are the preconditions for
deriving the remaining registries rather than hand-maintaining them.

---

*Prepared as an architecture assessment. No source changes were made; every finding above cites the
file it was read from so it can be re-verified independently.*
