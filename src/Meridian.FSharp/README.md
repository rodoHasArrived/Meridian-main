---
doc_type: source-readme
doc_schema: meridian.source-readme
doc_schema_version: "1.0.0"
module_id: SRC-FSHARP
path: src/Meridian.FSharp
status: active
owner_lane: Strategy Analytics
last_reviewed: 2026-08-04
---

# src/Meridian.FSharp

## Purpose

FSharp contains deterministic functional models, calculations, and workflow support used by research, operations, governance, and domain logic.

## Layer responsibility

This layer should keep functional domain calculations reusable and testable from C# application and workflow code.

## Key folders and files

- `Meridian.FSharp.fsproj` - main F# project boundary.
- `Operations/OperationsContinuityRules.fs` - pure status precedence rules for Operations Continuity.
- `Operations/ReportPackValidationRules.fs` - data-driven report-pack validation rules.
- `Operations/SensitiveActionPolicy.fs` - pure sensitive-action approval and segregation-of-duties policy.
- `Operations/TradingReadinessRules.fs` - trading readiness and evidence-completeness scoring.
- Functional models and calculation modules.

## Important workflows

Use this module for deterministic business-rule kernels shared by strategy, trading, accounting, operations, and governed reporting workflows. Keep C# services responsible for orchestration, DI, storage, logging, endpoint composition, and UI-facing DTO assembly.

## API contract notes

- Security Master swap legs retain optional identifiers, pay/receive directions, spreads, index
  fixings, notionals, payment frequencies, and day-count conventions through serialization, plus
  the principal-exchange flag. Legacy legs may omit these terms. Supplied notionals must be
  positive and directions must be Pay or Receive; missing economics remain unresolved.
- A swap leg may also carry a `NotionalSchedule` (dated notional steps for an amortizing or
  accreting leg; positive, strictly ascending, within the swap's effective and maturity dates) and
  a `ResetSchedule` (strictly ascending reset dates with the fixing once observed). Both are empty
  for legs that do not track them and serialize as `notionalSchedule` / `resetSchedule` arrays.
- The bond economic definition carries the record's issue date, payment frequency, call flag and
  first call date, and issuer name in its maturity, coupon, call, and issuer modules, so a replay
  from economic terms alone keeps a callable bond callable.
- `assetSpecificTermsJson` stamps flat terms with the shared terms-format envelope for every kind
  except `CustomAsset`, whose profile owns its own document shape. The stamp is the effective write
  version: the class's write version, raised to the highest version among the versioned terms the
  record holds, a declared `scheduleConventions` block or any declared call or put entry (ADR-024,
  readable before writable). Bond, CertificateOfDeposit and StructuredCredit therefore write in three
  tiers: with nothing declared, at the write version with none of the readable-only keys
  (`scheduleConventions`, `callSchedule`, `putSchedule`); with conventions only, at the block's
  version with the `scheduleConventions` key; with any call or put entry, at the schedules' version
  (3 on Bond and CertificateOfDeposit, 4 on StructuredCredit) with the block key and both
  `callSchedule` and `putSchedule`.
- `BondTerms`, `CertificateOfDepositTerms`, `StructuredCreditTerms` and `SwapLeg` carry a
  `ScheduleConventions` record (business-day convention, accrual adjustment, payment calendars,
  payment lag, roll, end-of-month rule, regular dates, direction, stub policy, coupon rate unit and
  coupon cash basis); `ScheduleConventions.empty` is the undeclared value. It is decoded only from
  rows stamped at or above the block's readable version, and serialized only from that version
  upward: `null` when nothing is declared, otherwise all twelve members in alphabetical order (on a
  swap, on every leg).
  A declared business-day convention or payment calendar fills the economic `Accrual` module of a
  bond, CertificateOfDeposit or StructuredCredit: the convention verbatim and the payment calendars
  as one joint calendar id, with no fallback to the common terms' holiday calendar.
- `BondTerms`, `CertificateOfDepositTerms` and `StructuredCreditTerms` also carry a
  `CallPutSchedules` record: `Calls` (`CallScheduleTerm`) and `Puts` (`PutScheduleTerm`), in stored
  order; `CallPutSchedules.empty` is the undeclared value, and `SwapLeg` carries none. A
  `CallScheduleTerm` has a required `CallDate` and eleven optional members (window end, exercise
  style and frequency, price, price basis and type, make-whole spread, partial redemption, minimum
  and maximum notice days, notice day type); a `PutScheduleTerm` has a required `PutDate` and the
  eight that apply to a holder put. Text members are kept verbatim, and their meanings are frozen in
  ADR-025. Both lists are decoded only from rows stamped at or above the schedules' version, and
  serialized only from that version upward: `[]` when none is declared, otherwise one object per
  entry in stored order carrying every member in alphabetical order, with `null` for an unset one.
  The C# write-mode mapping never decodes them and refuses populated ones in a flat payload, so only
  the read-mode mapping of a flat row stamped at or above the schedules' version fills them.
- The economic `Call` module of a bond, CertificateOfDeposit or StructuredCredit is filled under one
  rule, frozen with the schedules (ADR-025). On a bond and a CertificateOfDeposit only the class's
  scalar call terms feed `IsCallable` and `FirstCallDate` (bond `IsCallable` and `CallDate`,
  CertificateOfDeposit `CallableDate`), and `CallPrice` stays `None`. Declared entries feed
  `CallSchedule`, `PutSchedule`, `IsPuttable` and `MakeWholeSpreadBps`, sorted stably by date. An
  entry's price is carried only for a `Fixed` price on a `PercentOfPar` basis (an absent type or
  basis counts as each), `IsParCall` marks a carried price of exactly 100, `CallType` is `MakeWhole`
  for a make-whole entry and otherwise the exercise style, and `MakeWholeSpreadBps` is the spread of
  the earliest make-whole entry that states one. StructuredCredit has no scalar call terms: it gets a
  `Call` module only when it declares a call or put, with `IsCallable` true when it declares a call,
  and `FirstCallDate` and `CallPrice` left `None`; `NonCallPeriodEnd` never feeds it. The fill drops
  window end dates, exercise frequency, every put's exercise style and a make-whole call's, partial
  redemption, the notice members, price basis and type beyond the price gate and `CallType`, every
  make-whole spread but that one (including any on an entry that is not make-whole), stored order,
  and the price of an entry outside the gate (a make-whole floor or an accreted-value price), so a
  consumer that needs the full terms reads the domain record's `CallPutSchedules`, not the economic
  module.
- `StructuredProductTerms.FactorScheduleReference` carries the StructuredCredit record's free-text
  `factorSchedule` pointer as evidence lineage. The legacy upgrade no longer writes that pointer into
  the coupon payment frequency or the floating-rate reset frequency; both read `None` for
  structured credit until a typed term supplies them.
- `SecurityMasterLegacyUpgrade.proposeStructuredCreditReclassification` builds the report-first
  StructuredCredit proposal for a legacy securitised row (ADR-022): a Bond in a securitised
  subclass, a CustomAsset whose `category` names a securitised product, or an envelope-less legacy
  custom row read back as OtherSecurity. It carries over par, coupon or index, maturity, seniority
  and the family the label implies; every required term the source lacks and every ambiguous label
  (`MortgageBacked`, `MBS`) is a blocker, so `ProposedTerms` exists only when nothing blocks. A bond
  that declares schedule conventions is blocked with `structured_credit_schedule_conventions_unwritable`,
  because they are a StructuredCredit terms version 3 term this node reads but cannot write, and one
  that declares a call or put entry is blocked with `structured_credit_call_put_schedules_unwritable`,
  because the schedules are StructuredCredit terms version 4 terms.
- `IdentifierKind` includes `LoanX` and `Clip` for syndicated-loan identifiers; interop maps
  them to the matching C# `SecurityIdentifierKind` values.
- `SecurityTermModules` no longer carries `AccountingElections` or `Pci`. Amortization method,
  yield updates, cash-flow source and PCI designation are lot- and account-level accounting
  choices and live on the lot, not in security economics.
- C# callers should enter through C#-friendly interop wrappers and should not depend on internal F# domain types.
- Security Master identifier interop retains optional provider/source metadata for standard
  identifiers. For `ProviderSymbol`, the provider namespace carried by the identifier kind remains
  authoritative and contradictory provider metadata is rejected as structured validation.
- Operations kernels should remain pure and deterministic: inputs in, decisions/issues/statuses out.
- Trading readiness evaluates every supplied acceptance gate, including newly introduced and
  duplicate IDs. Any blocked gate blocks the aggregate; all supplied gates must be ready for
  ready status. Unsupported or missing statuses require review, and empty input remains unknown.
  Evidence summaries use the same status classification.
- Stream-oriented helpers should receive ordered, bounded inputs. Page large storage/backfill batches before crossing into F#, and keep `mergeStreams` / `bufferByTime` on timestamp-ordered streams so they can avoid whole-batch sorting and grouping.
- Sensitive-action policy evaluates explicit guardrails such as MFA, dual approval, privileged roles, and segregation-of-duties before C# services write audit or workflow state.

## Diagrams

See `DIA-ASSURANCE-LOOP` in `docs/source/data/diagram-index.yml`.

## Roadmap traceability

<!-- source-roadmap-traceability:begin module=SRC-FSHARP -->
| Roadmap item | Title |
| --- | --- |
| `W3-CONT-001` | Research to paper continuity |
| `W6-BTSTUDIO-001` | Backtesting studio evidence loop |
| `W10-PERF-001` | Portfolio and investor return measurement |
<!-- source-roadmap-traceability:end -->

## TODO checklist

<!-- source-todos:begin module=SRC-FSHARP -->
- No registry-backed TODOs are open for this module.
<!-- source-todos:end -->

## Validation

```bash
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj /p:FSharpTestSlice=MarketData --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj /p:FSharpTestSlice=Domain --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
dotnet test tests/Meridian.FSharp.Tests/Meridian.FSharp.Tests.fsproj /p:FSharpTestSlice=Operations --logger "console;verbosity=normal" /p:EnableWindowsTargeting=true /p:NodeReuse=false
```

Use `/p:FSharpTestSlice=All` only when validating cross-slice F# changes.

## Change rules

Keep F# interop contracts explicit and covered by tests before changing C# consumers.

## Related docs

- `docs/ai/claude/CLAUDE.fsharp.md`
- `docs/source/generated/source-module-index.md`

`StructuredProductTerms.FactorScheduleEntries` retains dated factors in the typed economic model.
The StructuredCredit upgrade carries its canonical factor history into that module; the application
adapter emits nonempty history alongside the retained `FactorScheduleReference`. Empty histories
leave existing economic JSON bytes unchanged, while economic-only replay retains additive history
that a flat projection cannot express. Legacy passthrough remains a compatibility/replay concern,
not an accounting or reporting authority.
