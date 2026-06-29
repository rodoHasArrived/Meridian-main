# Reconciliation Statement Intake — Blueprint

Status: Implementable. Target: a developer starting tomorrow.
Primary file under change: `/home/user/Meridian-main/src/Meridian.FinancialOperations/Reconciliation/StatementReconciliationService.cs`
Validation command: `dotnet test tests/Meridian.Tests -c Release --filter "FullyQualifiedName~Reconciliation" /p:EnableWindowsTargeting=true`

---

## 1. Problem statement & scope

### What's broken (exact gaps)

1. **`local` statements are intake dead-ends.**
   - `StatementReconciliationService.ImportAsync` (`StatementReconciliationService.cs:45-67`) forks on `RequiresCanonicalStatementSchema` (`:118-121`). For `local`, it only emits `StatementSourceRowReference` rows with raw-line snapshots (`:56-66`); `Positions`, `CashBalances`, `Transactions`, `Securities` are all empty (`:66`).
   - `CreateExternalStatementCases` (`:123-141`) returns `new ExternalStatementCaseIntakeResult(importId, kind, path, 0, 0, [])` for `local` (`:127-129`) — local never produces matches or cases.

2. **The source-kind gate is hard-coded.**
   - `ValidateSourceAccess` (`:97-116`) allows only `local`/`broker`/`custodian`/`sample-broker` and throws `NotSupportedException` otherwise (`:106-110`). Onboarding a new custodian requires a code change here plus `RequiresCanonicalStatementSchema` (`:118-121`).

3. **Two divergent canonical parsers.**
   - `ReadNormalizedStatementImportAsync` (`:143-279`) parses by **fixed positional columns** (`CanonicalStatementColumns` at `:10-19` plus hard indices 7-16) and **ignores the mapping profile entirely**.
   - `ReadCanonicalStatementRows` (`:281-354`) is **profile-driven** via `StatementMappedCsvRow`/`BuildColumnMap`. Importing vs. case-creating the same file uses different column semantics.

4. **Mapping profile model can't express the canonical row shape.**
   - `StatementCanonicalField` (`StatementMappingProfiles.cs:5-18`) lacks `MarketValue`, `AccountId`, `ExternalAccountId`, `SecurityId`, `UnresolvedIdentifier`, `ExternalReference` — which is exactly why `ReadNormalizedStatementImportAsync` falls back to positional indices 7-16.
   - The profile has no notion of **source kind**, **delimiter/format**, or **header-required policy**; selection is a string/switch only (`ResolveForSourceKind` `:71-83`).

5. **Lost error context.**
   - `StatementMappingProfileRegistry.Resolve` (`StatementMappingProfiles.cs:60-69`) throws `NotSupportedException` for unknown ids — no list of valid ids, no default fallback path for callers that want one.
   - `StatementValidationService.ValidateSourceFile` (`StatementValidationService.cs:81-100`) returns `null` on an unreadable file, after which row validation is skipped (`:68-71`) — all per-row context is dropped after the single blocker.
   - The **live** HTTP path uses `IStatementReconciliationValidationService.ValidateAsync` (returns plain `string`), which discards all structured issues; the rich `StatementValidationService` (returns `StatementValidationResultDto` with severities + `IsBlocked`) is **not registered in DI**.

### In scope
- Make `local` a first-class, profile-driven parse-to-canonical path producing `StatementPosition`/`StatementCashBalance` (+ `StatementTransaction`) rows.
- Replace the hard-coded source-kind gate with profile-driven admission.
- Unify the two canonical parsers behind one profile-driven helper.
- Extend `StatementCanonicalField` + the profile model (source kind, format/delimiter, header policy).
- Replace null-on-error with a structured parse-result type that preserves per-row context.
- Surface `ListProfiles()` so operators can pick a profile id.

### Out of scope (this lane)
- Rewiring the **HTTP live path** (`IBrokerStatementService`/`CsvBrokerStatementService` + `StatementMatchingService` via `StatementRunWorkflowService`) to call `StatementReconciliationService`. Section 6 specifies the **DTO/endpoint plumbing** (adding `MappingProfileId`/`SourceKind` flow and a profiles-list endpoint) but does **not** swap the workflow's importer — that is a separate, larger lane flagged as a risk.
- Building the `StatementPosition` → `NormalizedStatementPosition` adapter into `StatementMatchingEngine` (the unfilled seam at `StatementMatchingEngine.cs:598-634`). Noted as open question O-3.
- Profile persistence to a store/DB. We make the registry DI-injectable and config-seedable; durable CRUD is a follow-up.

---

## 2. Target design overview

Today `local` short-circuits and the canonical path has two parsers. The target collapses every source kind onto **one profile-driven pipeline**. A source kind is no longer a hard-coded `switch`; it is just a key that resolves to a `StatementMappingProfile`. `local` gets a real profile (or uses any operator-selected one), so its rows parse into the same `StatementPosition`/`StatementCashBalance` records the canonical path already produces and the matcher already consumes.

```
source file (any columns, any delimiter)
        │
        ▼
resolve mapping profile  ── StatementMappingProfileRegistry.ResolveForSourceKind(kind, profileId)
   (profile carries: AcceptedSourceKinds, Format/Delimiter, HeaderPolicy, field mappings, txn codes)
        │
        ▼
admit source            ── ValidateSourceAccess(kind, path, profile)   (gate driven by profile, not hard switch)
        │
        ▼
parse header + rows     ── ParseCanonical(profile, header, line, rowNumber)  (ONE shared helper)
        │                     → StatementParseResult { Rows, Errors }   (NEW: no null-on-error)
        ▼
project to canonical    ── StatementPosition / StatementCashBalance / StatementTransaction
        │                   (+ StatementSourceRowReference for every line, errors included)
        ▼
   ┌─────────────┴─────────────┐
   ▼                           ▼
ImportAsync                CreateExternalStatementCases
→ NormalizedStatementImportResult   → MatchRows(NormalizedStatementRow[])
   (typed lists populated for           → ReconciliationMatchLink + ReconciliationCase
    local AND canonical)                  (local now produces matches/cases)
```

Key invariant: `ImportAsync` and `CreateExternalStatementCases` consume the **same** parse output, so column semantics never diverge again.

---

## 3. Interfaces & types — concrete C# signatures

### 3.1 NEW: structured parse result (replaces null-on-error)

`src/Meridian.FinancialOperations/Reconciliation/StatementMappingProfiles.cs` (or a new `StatementParseResult.cs` in the same folder).

```csharp
// NEW
public enum StatementParseSeverity { Warning, Error }

// NEW — one error keeps full context: which row, which canonical field, the raw line.
public sealed record StatementParseError(
    int SourceRowNumber,
    StatementCanonicalField? Field,   // null for header/file-level problems
    string Code,                      // e.g. "MISSING_REQUIRED", "BAD_DECIMAL", "BAD_HEADER"
    string Message,
    StatementParseSeverity Severity,
    string? RawLine);

// NEW — every line yields either a normalized row or an error (or both: warnings + row).
public sealed record StatementParseResult(
    string ImportId,
    string ProfileId,
    string NormalizedSourceKind,
    string SourcePath,
    IReadOnlyList<NormalizedStatementRow> Rows,           // existing type, StatementReconciliationAggregate.cs:24
    IReadOnlyList<StatementSourceRowReference> SourceRows, // existing, StatementEntities.cs:26
    IReadOnlyList<StatementParseError> Errors)
{
    public bool IsBlocked => Errors.Any(e => e.Severity == StatementParseSeverity.Error);
}
```

Rationale: callers that previously got `null` (`StatementValidationService.ValidateSourceFile`) or a thrown `InvalidDataException` (`ReadCanonicalStatementRows` `:302`) now receive `StatementParseResult` with the full `Errors` list, and choose whether to surface or throw.

### 3.2 CHANGED: extend the canonical-field enum

`StatementMappingProfiles.cs:5-18`. Append the missing fields so positional fallback (`ReadNormalizedStatementImportAsync` indices 7-16) can be deleted.

```csharp
public enum StatementCanonicalField
{
    Account,
    SecurityIdentifier,
    Quantity,
    Price,
    CashAmount,
    ActivityType,
    TradeDate,
    SettlementDate,
    Currency,
    FeesCommission,
    ExternalTransactionId,
    // NEW (additive — append only; do not reorder existing members)
    MarketValue,
    ExternalAccountId,
    SecurityId,
    UnresolvedIdentifier,
    Amount
}
```

`AccountId` from the spec maps to the existing `Account` field; `ExternalReference` maps to existing `ExternalTransactionId`. Only the five above are genuinely new.

### 3.3 CHANGED: profile model carries source kind, format, header policy

`StatementMappingProfiles.cs:29-44`. Add init-only members with safe defaults so all existing positional constructions (`CreateDefaultProfiles` `:88-139`) keep compiling.

```csharp
// NEW supporting enums
public enum StatementSourceFormat { Csv }            // extension point; Csv-only today
public enum StatementHeaderPolicy { CanonicalPrefix, RequiredColumns, None }

public sealed record StatementMappingProfile(
    string ProfileId,
    string DisplayName,
    IReadOnlyList<StatementFieldMapping> FieldMappings,
    IReadOnlyList<StatementTransactionCodeMapping> TransactionCodeMappings)
{
    // NEW — which source kinds this profile admits. Empty => admit any kind that names it explicitly.
    public IReadOnlyList<string> AcceptedSourceKinds { get; init; } = [];

    // NEW
    public StatementSourceFormat Format { get; init; } = StatementSourceFormat.Csv;
    public char Delimiter { get; init; } = ',';

    // NEW — replaces the hard-coded "canonical-csv-v1 must match canonical prefix" special case.
    public StatementHeaderPolicy HeaderPolicy { get; init; } = StatementHeaderPolicy.RequiredColumns;

    // unchanged
    public StatementFieldMapping? FindField(StatementCanonicalField field) =>
        FieldMappings.FirstOrDefault(mapping => mapping.CanonicalField == field);

    public string MapActivityType(string activityType)
    {
        var mapped = TransactionCodeMappings.FirstOrDefault(mapping =>
            string.Equals(mapping.SourceCode, activityType, StringComparison.OrdinalIgnoreCase));
        return mapped?.CanonicalActivityType ?? activityType;
    }
}
```

The canonical-csv-v1 default sets `HeaderPolicy = StatementHeaderPolicy.CanonicalPrefix`; the new `local` profile and `sample-broker` use `RequiredColumns`. This deletes the `profile.ProfileId.Equals(CanonicalCsvV1ProfileId ...)` special case at `StatementReconciliationService.cs:384-388`.

### 3.4 CHANGED: registry — add `local` profile, non-throwing resolve, source-kind admission

`StatementMappingProfiles.cs:46-140`.

```csharp
public sealed class StatementMappingProfileRegistry
{
    public const string CanonicalCsvV1ProfileId = "canonical-csv-v1";
    public const string SampleBrokerCsvV1ProfileId = "sample-broker-csv-v1";
    public const string LocalCsvV1ProfileId = "local-csv-v1";   // NEW

    // unchanged ctor / Defaults / _profiles ...

    // CHANGED — non-throwing variant preserves context (valid id list) instead of bare NotSupportedException.
    public bool TryResolve(string? profileId, out StatementMappingProfile profile)
    {
        var id = string.IsNullOrWhiteSpace(profileId) ? CanonicalCsvV1ProfileId : profileId.Trim();
        return _profiles.TryGetValue(id, out profile!);
    }

    // CHANGED — keep throwing overload for back-compat, but include valid ids in the message.
    public StatementMappingProfile Resolve(string? profileId)
    {
        if (TryResolve(profileId, out var profile)) return profile;
        throw new NotSupportedException(
            $"Statement mapping profile '{profileId}' is not registered. " +
            $"Registered profiles: {string.Join(", ", _profiles.Keys.OrderBy(k => k))}.");
    }

    // CHANGED — source-kind switch replaced by AcceptedSourceKinds lookup, with sensible fallback.
    public StatementMappingProfile ResolveForSourceKind(string normalizedSourceKind, string? profileId = null)
    {
        if (!string.IsNullOrWhiteSpace(profileId)) return Resolve(profileId);

        var byKind = _profiles.Values.FirstOrDefault(p =>
            p.AcceptedSourceKinds.Contains(normalizedSourceKind, StringComparer.OrdinalIgnoreCase));
        if (byKind is not null) return byKind;

        // legacy fallback preserved
        return Resolve(normalizedSourceKind switch
        {
            "sample-broker" => SampleBrokerCsvV1ProfileId,
            "local" => LocalCsvV1ProfileId,
            _ => CanonicalCsvV1ProfileId
        });
    }

    // NEW — admission decision moves out of the service's hard switch.
    public bool IsSourceKindSupported(string normalizedSourceKind) =>
        TryResolve(null, out _) /* always a default exists */ && true;
}
```

`CreateDefaultProfiles` (`:88-139`) gains a third profile:

```csharp
new StatementMappingProfile(
    LocalCsvV1ProfileId,
    "Local CSV v1",
    [
        new(StatementCanonicalField.Account, "account"),
        new(StatementCanonicalField.SecurityIdentifier, "symbol"),
        new(StatementCanonicalField.Quantity, "quantity"),
        new(StatementCanonicalField.Price, "price"),
        new(StatementCanonicalField.CashAmount, "cashAmount"),
        new(StatementCanonicalField.ActivityType, "activityType"),
        new(StatementCanonicalField.TradeDate, "tradeDate"),
        new(StatementCanonicalField.SettlementDate, "settlementDate", Required: false),
        new(StatementCanonicalField.Currency, "currency", Required: false),
        new(StatementCanonicalField.FeesCommission, "feesCommission", Required: false),
        new(StatementCanonicalField.MarketValue, "marketValue", Required: false)
    ],
    [
        new("position", "position"), new("cash", "cash"), new("cashbalance", "cashbalance"),
        new("trade", "trade"), new("fee", "fee"), new("dividend", "dividend"), new("div", "dividend")
    ])
{
    AcceptedSourceKinds = ["local"],
    HeaderPolicy = StatementHeaderPolicy.RequiredColumns
}
```

Existing two profiles get `AcceptedSourceKinds = ["broker", "custodian"]` (canonical-csv-v1, `HeaderPolicy = CanonicalPrefix`) and `["sample-broker"]` (sample-broker-csv-v1, `RequiredColumns`).

### 3.5 CHANGED: `StatementReconciliationService` — methods & new private helpers

Behavioral changes by method:

| Method (line) | Change |
|---|---|
| `ValidateSourceAccess` (`:97-116`) | Drop the four-way `string.Equals` gate (`:106-110`). New signature `ValidateSourceAccess(string sourceKind, string sourcePath, StatementMappingProfile profile)`: still validates non-empty + `File.Exists`; admission is now "a profile resolved." Throws `NotSupportedException` only if `ResolveForSourceKind` finds nothing (cannot happen given default). |
| `RequiresCanonicalStatementSchema` (`:118-121`) | **Delete.** No more canonical-vs-local fork; every kind parses through the profile. |
| `ImportAsync` (`:45-67`) | Resolve profile once, call new `ParseStatement(...)`, then project `StatementParseResult.Rows` into `StatementPosition`/`StatementCashBalance`/`StatementTransaction`/`StatementSecurityReference` for **all** kinds incl. `local`. Remove the raw-only branch (`:54-66`). |
| `CreateExternalStatementCases` (`:123-141`) | Remove the `local` empty-result branch (`:125-130`). Always `ParseStatement` → `MatchRows`. `local` now yields matches/cases. |
| `ReadNormalizedStatementImportAsync` (`:143-279`) | **Replace body** to call the shared `ParseStatement` helper instead of positional `ParseCanonicalStatementLine` (`:233-278`). Delete `ParseCanonicalStatementLine` and the `CanonicalStatementColumns` array (`:10-19`). |
| `ReadCanonicalStatementRows` (`:281-354`) | Refactor to delegate to the shared `ParseStatement` helper (it already has the right shape; extract its per-row body). |
| `ValidateStatementHeader` (`:373-401`) | Drive header check off `profile.HeaderPolicy` instead of the `CanonicalCsvV1ProfileId` equality check (`:384-388`). Return errors into `StatementParseResult.Errors` rather than throwing where the caller wants soft validation; keep a throwing wrapper for hard callers. |

New private helpers (one shared parser used by both import and case-intake):

```csharp
// NEW — the single profile-driven parser. Both ImportAsync and CreateExternalStatementCases call this.
private StatementParseResult ParseStatement(
    string normalizedSourceKind,
    string sourcePath,
    StatementMappingProfile profile)
{
    var content = File.ReadAllText(sourcePath);
    var importId = DeterministicFingerprint.Compute(
        $"{normalizedSourceKind}|{profile.ProfileId}|{sourcePath}|{content}");  // matches existing :286 shape

    var allLines = File.ReadLines(sourcePath).ToArray();
    var errors = new List<StatementParseError>();
    var rows = new List<NormalizedStatementRow>();
    var sourceRows = new List<StatementSourceRowReference>();

    if (allLines.Length == 0)
    {
        errors.Add(new StatementParseError(0, null, "EMPTY_FILE",
            "Statement source file is empty.", StatementParseSeverity.Error, null));
        return new StatementParseResult(importId, profile.ProfileId, normalizedSourceKind, sourcePath, rows, sourceRows, errors);
    }

    var header = allLines[0].Split(profile.Delimiter, StringSplitOptions.TrimEntries);
    ValidateHeader(profile, header, errors);   // appends, never throws

    for (var i = 1; i < allLines.Length; i++)
    {
        var line = allLines[i];
        if (string.IsNullOrWhiteSpace(line)) continue;
        var rowNumber = i; // 1-based data row index, consistent with existing rowNumber semantics

        sourceRows.Add(CreateSourceRowReference(importId, rowNumber, line,
            BuildSnapshot(normalizedSourceKind, sourcePath, profile.ProfileId, line)));

        if (TryParseRow(profile, header, line, rowNumber, importId, normalizedSourceKind, sourcePath,
                out var normalizedRow, errors))
        {
            rows.Add(normalizedRow);
        }
    }

    return new StatementParseResult(importId, profile.ProfileId, normalizedSourceKind, sourcePath, rows, sourceRows, errors);
}

// NEW — per-row mapping; reuses StatementMappedCsvRow but converts throws to StatementParseError.
private bool TryParseRow(
    StatementMappingProfile profile, string[] header, string line, int rowNumber,
    string importId, string normalizedSourceKind, string sourcePath,
    out NormalizedStatementRow row, List<StatementParseError> errors)
{
    row = null!;
    var parts = line.Split(profile.Delimiter, StringSplitOptions.TrimEntries);
    if (parts.Length < header.Length)
    {
        errors.Add(new StatementParseError(rowNumber, null, "COLUMN_COUNT",
            $"Row {rowNumber} has {parts.Length} columns; expected at least {header.Length}.",
            StatementParseSeverity.Error, line));
        return false;
    }

    var mapped = new StatementMappedCsvRow(profile, BuildColumnMap(header, parts));
    try
    {
        // reuse the exact projection currently in ReadCanonicalStatementRows :306-350
        row = ProjectNormalizedRow(mapped, profile, line, rowNumber, importId, normalizedSourceKind, sourcePath);
        return true;
    }
    catch (InvalidDataException ex)   // GetRequired*/parse failures from StatementMappedCsvRow
    {
        errors.Add(new StatementParseError(rowNumber, null, "ROW_PARSE", ex.Message, StatementParseSeverity.Error, line));
        return false;
    }
}
```

`ProjectNormalizedRow` is the lifted body of `ReadCanonicalStatementRows` lines `:306-350` (unchanged logic, now reused by both paths). The canonical→typed projection used by `ImportAsync` (lifted from `ReadNormalizedStatementImportAsync`) maps each `NormalizedStatementRow.Kind` into `StatementPosition`/`StatementCashBalance`/`StatementTransaction`, now reading `MarketValue`/`ExternalAccountId`/`SecurityId`/`UnresolvedIdentifier` from the **profile** (via `mapped.GetOptional(...)`) instead of positional indices.

Public method signatures are **unchanged** (`ValidateAsync`, `ImportAsync`, `ReconcileAsync`, `CreateExternalStatementCasesAsync`, `MatchRows`) — back-compatible. `MatchRows` (`:543-616`), `StatementBreakClassifier`, and `ReconciliationCase` construction are untouched.

---

## 4. Mapping-profile design

### Data model
A `StatementMappingProfile` (3.3) now fully describes how to read a source: `FieldMappings` (column → `StatementCanonicalField`), `TransactionCodeMappings` (source code → canonical activity), `AcceptedSourceKinds`, `Format`/`Delimiter`, and `HeaderPolicy`. This is the unit of "onboard a new custodian by config, not code."

### Storage / registration (mirror existing registry)
- `StatementMappingProfileRegistry` stays the home (`StatementMappingProfiles.cs:46`). `Defaults` static (`:58`) keeps the three built-in profiles.
- Make it **DI-injectable**: register a single `StatementMappingProfileRegistry` in `ReconciliationServiceRegistration.cs` and pass it to `new StatementReconciliationService(registry)`. The ctor already accepts it (`StatementReconciliationService.cs:22-25`), but the service is currently registered via `TryAddSingleton` using the default ctor, so it always falls back to `StatementMappingProfileRegistry.Defaults` today.
- Config seam (additive, follow-up-friendly): the registry ctor already takes `IEnumerable<StatementMappingProfile>` (`:53-56`). A bind from `appsettings`/options can supplement `Defaults` without code changes per custodian. Persistence to a store is out of scope (O-1).

### Default / auto-detect path
- No explicit `profileId` + known source kind → `ResolveForSourceKind` finds the profile whose `AcceptedSourceKinds` contains the kind, else falls back (`sample-broker`→sample, `local`→local, else canonical). This replaces the hard switch at `:71-83`.
- Unknown source kind with no profile → resolves to `canonical-csv-v1` (default) rather than `NotSupportedException`. Onboarding "acme-custodian" needs only a profile with `AcceptedSourceKinds = ["acme-custodian"]`.

### Validation rules (header)
Driven by `profile.HeaderPolicy`:
- `CanonicalPrefix` — first N columns must equal the canonical required prefix (existing logic at `CanonicalCsvHeaderPrefixMatches` `:421-431`). Used by canonical-csv-v1.
- `RequiredColumns` — every `FieldMapping` with `Required = true` must be present (existing logic at `:390-398`). Used by local + sample-broker + any new custodian.
- `None` — skip header column checks (delimiter-positional sources).
- Duplicate-column detection (`EnsureUniqueStatementHeaderColumns` `:403-419`) runs for all policies.

### How this removes the hard-coded switch
- `ValidateSourceAccess` (`:106-110`) four-way `string.Equals` → deleted; admission = "a profile resolved."
- `RequiresCanonicalStatementSchema` (`:118-121`) → deleted; there is no canonical-vs-local fork.
- New kinds need a profile entry only.

---

## 5. Parse path (step by step)

For a local CSV with arbitrary columns, e.g. header `acct,ticker,qty,px,cash,type,td` mapped by a custom `local-acme` profile:

1. **Resolve profile.** `ResolveForSourceKind("local", profileId)`. Operator-selected `profileId` wins; else the kind→profile match (`AcceptedSourceKinds`); else `local-csv-v1`.
2. **Admit source.** `ValidateSourceAccess("local", path, profile)`: non-empty kind/path, `File.Exists`. No hard kind gate.
3. **Compute import id.** `DeterministicFingerprint.Compute($"{kind}|{profile.ProfileId}|{path}|{content}")` — same shape as `:286`, so re-importing an identical file yields the same `ImportId` (idempotency).
4. **Header validation per policy.** `RequiredColumns`: confirm every required mapped `SourceColumn` is present; missing → `StatementParseError("MISSING_REQUIRED", ...)` (accumulated, not thrown). Extra columns are ignored (they fall through `BuildColumnMap` `:433-445` and simply aren't mapped).
5. **Per-row mapping.** For each data line, `BuildColumnMap(header, parts)` → `StatementMappedCsvRow`. `GetRequired*`/`GetOptional` resolve via `profile.FindField(field).SourceColumn` (`StatementMappingProfiles.cs:165-180`). Activity code mapped via `profile.MapActivityType` → `ToStatementRowKind` (`:516-541`) routes to Position/CashBalance/Transaction.
6. **Project to canonical rows.** `position` → `StatementPosition` (Quantity, Price, MarketValue, Account, ExternalAccountId, SecurityId/UnresolvedIdentifier, Currency, TradeDate/SettlementDate); `cash`/`cashbalance` → `StatementCashBalance`; others → `StatementTransaction`. These feed `NormalizedStatementImportResult`. The parallel `NormalizedStatementRow` (Symbol/Quantity/Amount + `RawSnapshot`) feeds `MatchRows`.
7. **Error handling preserves context.** Every line produces a `StatementSourceRowReference` regardless of parse outcome, and parse failures append `StatementParseError` carrying `SourceRowNumber`, `Code`, `Message`, `RawLine`. Nothing returns `null`. Callers:
   - `ImportAsync` includes successfully-parsed typed rows and exposes errors (via a follow-up: surface `StatementParseResult.Errors` into the result; minimally, log structured + throw if `IsBlocked` when called from the strict path).
   - The validation path maps `StatementParseError` → `StatementValidationIssueDto` (severity-mapped), fixing the `ValidateSourceFile`→`null`→skip-rows drop (`StatementValidationService.cs:68-71, 81-100`).
8. **Idempotency / fingerprinting.** Reuse `DeterministicFingerprint.Compute` (lowercase hex SHA256, `:744-751`) everywhere — import id (step 3), per-row fingerprint `$"{importId}|{rowNumber}|{line}"` (matches `:320`), and `CreateSourceRowReference` hash (`:447-452`). No new hashing scheme.

---

## 6. Endpoint / API impact

### DTO
`StatementRunCreateDto` (`StatementReconciliationDtos.cs:297-310`) has no `sourceKind` (`Broker` is the surrogate) and already carries `MappingProfileId`. Add an **optional** explicit source kind without breaking callers:

```csharp
// CHANGED — append optional params only; existing positional callers unaffected.
public sealed record StatementRunCreateDto(
    string Broker,
    string SourceInstitution,
    string FundAccountId,
    string ExternalAccountId,
    DateOnly StatementPeriodStart,
    DateOnly StatementPeriodEnd,
    string SourcePath,
    string OriginalFileName,
    string MappingProfileId,
    string ToleranceProfileId,
    string ImportedBy,
    string? SourceFileHash = null,
    string? Notes = null,
    string? SourceKind = null);   // NEW: explicit kind; null => Broker is used as today
```

### New endpoint: list profiles
So operators can pick a `MappingProfileId` that flows into `StatementRunCreateDto`. Mirror existing route conventions in `WorkstationEndpoints.Reconciliation.cs`:

```
GET ReconciliationStatementMappingProfiles → ListMappingProfiles
  → registry.ListProfiles() projected to a DTO { ProfileId, DisplayName, AcceptedSourceKinds, RequiredColumns }
  → 200 / 501 (when IReconciliationApiService is null, same pattern as :116-129, .Produces(501))
```

Add a method to `IReconciliationApiService` and the concrete `ReconciliationApiService` returning the projection. No mutation permission needed (read-only list). Exact files to edit:
- Interface: `/home/user/Meridian-main/src/Meridian.Ui.Shared/Contracts/Reconciliation/IReconciliationApiService.cs`
- Implementations (two — confirm which the endpoint resolves at runtime and update accordingly; both implement the interface):
  - `/home/user/Meridian-main/src/Meridian.Ui.Services/Services/Reconciliation/ReconciliationApiService.cs`
  - `/home/user/Meridian-main/src/Meridian.Ui.Shared/Services/ReconciliationApiService.cs`
- Endpoint route: `/home/user/Meridian-main/src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.Reconciliation.cs`

### Backward compatibility
- All new DTO members are optional with defaults → existing request bodies still bind.
- `ValidateAsync` / `ImportAsync` / `ReconcileAsync` public signatures unchanged.
- The **live workflow importer is not swapped** in this lane — the run-create endpoint still routes through `StatementRunWorkflowService.CreateAsync` (`StatementRunWorkflowService.cs:16`), which depends on `IBrokerStatementService` (concrete `CsvBrokerStatementService` in `Meridian.Infrastructure/Reconciliation/BrokerStatementInfrastructure.cs`) and `new StatementMatchingService()` (`:22`), both of which ignore mapping profiles. `MappingProfileId` continues to reach the string validator. The profiles-list endpoint and the `StatementReconciliationService` hardening are independently shippable. Wiring the workflow to the profile-driven service is the follow-up (Section 1 out-of-scope, Risk R-1).

---

## 7. Test plan

Project: `tests/Meridian.Tests` (`tests/Meridian.Tests/Meridian.Tests.csproj`).
Folder: `/home/user/Meridian-main/tests/Meridian.Tests/Reconciliation/` — namespace `Meridian.Tests.Reconciliation`.
Style: new suite → `Method_Condition_ExpectedOutcome` + **FluentAssertions** (add `using FluentAssertions;` per file; `using Xunit;` is global). Construct services with `new StatementReconciliationService(...)`. Reuse the `FixturePath` parent-walk helper and inline temp-CSV idiom from `StatementFixtureScenarioTests.cs` / `StatementImportAndMatchingTests.cs`. Thread real `CancellationToken`s.

New file: `LocalStatementIntakeTests.cs` (sealed class `LocalStatementIntakeTests`).

```csharp
using System.Globalization;
using FluentAssertions;
using Meridian.FinancialOperations.Reconciliation;
namespace Meridian.Tests.Reconciliation;

public sealed class LocalStatementIntakeTests
{
    private static string WriteTemp(params string[] lines) { /* Path.Combine(GetTempPath, $"meridian-local-{Guid:N}.csv"); WriteAllLinesAsync */ }
    // ... try/finally File.Delete around each test
}
```

Cases:

1. **`ImportAsync_LocalStatement_ProducesCanonicalPositionsAndCash`** — local CSV (canonical local header: `account,symbol,quantity,price,cashAmount,activityType,tradeDate`) with one `position` row + one `cash` row. Assert `result.Positions.Should().ContainSingle()`, `result.CashBalances.Should().ContainSingle()`, `result.SourceRows.Should().HaveCount(2)`. (Regression-proof against `:66` empty-lists bug.)

2. **`ReconcileAsync_LocalStatement_ProducesMatchesAndCases`** — local file with one in-tolerance position and one break. Assert `result.MatchCount.Should().Be(1)` and the cases count > 0. (Proves the `:127-129` local empty dead-end is gone.)

3. **`ImportAsync_ProfileDrivenCustomColumns_MapsToCanonical`** — write a custom-header CSV (`acct,ticker,qty,px,cash,type,td`) + a custom registry `new StatementMappingProfileRegistry([customLocalProfile, ...defaults])`; `ImportAsync("local", path, ...)` with that registry. Assert positions parse using the mapped columns. (Proves profile-driven local parse.)

4. **`ImportAsync_MissingRequiredColumn_ReportsContextNotNull`** — header missing `quantity`. Assert the operation surfaces a structured error referencing `"quantity"` / row context (via `StatementParseResult.Errors` or `InvalidDataException` whose message names the column) — and that it is **not** silently empty. (Proves error-context preservation; counters `ValidateSourceFile`→null drop.)

5. **`ImportAsync_ExtraUnmappedColumns_AreIgnored`** — header has `account,symbol,quantity,price,cashAmount,activityType,tradeDate,note,trader`. Assert parse succeeds and ignores `note`/`trader`.

6. **`ResolveForSourceKind_UnknownKindNoProfile_FallsBackToCanonical`** — `StatementMappingProfileRegistry.Defaults.ResolveForSourceKind("acme-custodian", null).ProfileId.Should().Be(CanonicalCsvV1ProfileId)`. (Proves the hard `NotSupportedException` gate is replaced by fallback.)

7. **`Resolve_UnknownProfileId_ThrowsWithRegisteredIdsListed`** — `act.Should().Throw<NotSupportedException>().WithMessage("*canonical-csv-v1*")`. (Proves lost-context message fix at `:60-69`.)

8. **`ImportAsync_SameFileTwice_ProducesSameImportId`** — call `ImportAsync` twice on the same temp file; `r1.ImportId.Should().Be(r2.ImportId)`. (Idempotency via `DeterministicFingerprint`.)

9. **Cancellation** — `using var cts = new CancellationTokenSource(); cts.Cancel();` then `await act.Should().ThrowAsync<OperationCanceledException>()` for `ImportAsync` (observes cancellation per `:48`).

Regression (existing canonical/broker path must still pass): add to the existing canonical suite (or a new `CanonicalStatementIntakeRegressionTests.cs`):

10. **`ImportAsync_BrokerCanonicalFixture_StillReconciles`** — reuse `FixturePath("statement-clean-reconciles.csv")` with `sourceKind = "broker"`; assert positions/cash parse and reconcile exactly as today.
11. **`ReconcileAsync_SampleBrokerProfile_StillMatches`** — inline sample-broker CSV (`BrokerAccount,Ticker,Units,ExecutionPrice,NetCash,TxnCode,TradeDate,...`) with `SampleBrokerCsvV1ProfileId`; assert match/case counts unchanged. (Confirms the parser-unification refactor preserved profile-driven broker behavior.)
12. **`ImportAndCaseIntake_ProduceConsistentColumns`** — parse the same broker file via `ImportAsync` and `CreateExternalStatementCasesAsync`; assert the position quantities/symbols agree. (Locks in the unified-parser invariant; would have failed under the old two-parser divergence.)

Run: `dotnet test tests/Meridian.Tests -c Release --filter "FullyQualifiedName~Reconciliation" /p:EnableWindowsTargeting=true`.

---

## 8. Implementation checklist (ordered, small, reviewable)

1. **Add `StatementParseResult` + `StatementParseError` + `StatementParseSeverity`** (new file in `Reconciliation/`). No callers yet. Compile.
2. **Extend `StatementCanonicalField`** with `MarketValue, ExternalAccountId, SecurityId, UnresolvedIdentifier, Amount` (append only). Compile.
3. **Extend `StatementMappingProfile`** with `AcceptedSourceKinds`, `Format`, `Delimiter`, `HeaderPolicy` (init-only defaults) + `StatementSourceFormat`/`StatementHeaderPolicy` enums. Compile (defaults keep existing constructions valid).
4. **Registry: add `LocalCsvV1ProfileId` + local profile; set `AcceptedSourceKinds`/`HeaderPolicy` on all three defaults; add `TryResolve`; improve `Resolve` message; rewrite `ResolveForSourceKind` to use `AcceptedSourceKinds`.** Unit test 6 & 7 pass.
5. **Extract `ProjectNormalizedRow`** from `ReadCanonicalStatementRows` body (`:306-350`) — pure refactor, behavior identical. Run existing recon tests (regression cases 10-11 baseline).
6. **Introduce `ParseStatement` + `TryParseRow` + `ValidateHeader(profile, header, errors)`** returning `StatementParseResult`; route `ReadCanonicalStatementRows` through it. Existing tests still green.
7. **Delete `RequiresCanonicalStatementSchema`; rewrite `ValidateSourceAccess` to profile-driven admission; remove the four-way kind gate.**
8. **Rewrite `ImportAsync` and `CreateExternalStatementCases`** to call `ParseStatement` for all kinds (remove local raw-only branch `:54-66` and local empty-result branch `:125-130`); project typed rows from the shared parser. Delete `ParseCanonicalStatementLine` (`:233-278`) and `CanonicalStatementColumns` (`:10-19`).
9. **Switch `ValidateStatementHeader` to `profile.HeaderPolicy`** (remove the `CanonicalCsvV1ProfileId` special case `:384-388`).
10. **Write `LocalStatementIntakeTests` (cases 1-5, 8, 9)** and regression cases 10-12. Run targeted filter.
11. **DI:** register `StatementMappingProfileRegistry` in `ReconciliationServiceRegistration.cs` and inject into `StatementReconciliationService`.
12. **DTO + endpoint:** add optional `SourceKind` to `StatementRunCreateDto`; add `GET ReconciliationStatementMappingProfiles` route + `IReconciliationApiService`/`ReconciliationApiService` method + projection DTO.
13. **(Optional, same PR or follow-up) Validation bridge:** map `StatementParseError` → `StatementValidationIssueDto`; ensure `StatementValidationService` no longer drops rows after an unreadable-file blocker.
14. **Docs:** update reconciliation docs to note profile-driven local intake + the new profiles endpoint (CLAUDE.md rule 4).
15. **Validate:** `dotnet test tests/Meridian.Tests -c Release --filter "FullyQualifiedName~Reconciliation" /p:EnableWindowsTargeting=true`, then `bash scripts/ci.sh` for PR readiness.

---

## 9. Risks & open questions

- **R-1 (live path untouched):** The run-create endpoint still imports via `StatementRunWorkflowService.CreateAsync` (`:16`) → `IBrokerStatementService`/`CsvBrokerStatementService` (`Meridian.Infrastructure/Reconciliation/BrokerStatementInfrastructure.cs`) + `new StatementMatchingService()` (`:22`), which ignore mapping profiles. This blueprint hardens `StatementReconciliationService` and the DTO/endpoint surface but does **not** make the live HTTP run use the profile-driven parser. Operators selecting a `MappingProfileId` still won't change live parsing until the workflow is rewired. Flag explicitly in the PR.
- **R-2 (enum ordering):** `StatementCanonicalField` must be **append-only**. If any persisted artifact serializes it by integer, reordering breaks reads. Verified safe: it is serialized **by name** — `ToCanonicalSnapshot` (`StatementMappingProfiles.cs:189`) uses `mapping.CanonicalField.ToString()` as a dictionary key, which is name-based. Appending members is therefore safe.
- **R-3 (header-policy regression):** canonical-csv-v1 currently hard-requires the exact canonical prefix (`:367-370, :384-388`). Moving that to `HeaderPolicy = CanonicalPrefix` must reproduce the existing `CanonicalCsvHeaderPrefixMatches` logic exactly, or `statement-invalid-blockers.csv` expectations shift. Cover with regression case 10.
- **R-4 (CSV robustness):** all parsing is naive `Split(delimiter)` (`:288, :299, :433`). Quoted fields / embedded delimiters remain unsupported; the new `local` path inherits this. Out of scope, but note it — local files are likelier to contain quoted commas than broker exports.
- **O-1 (profile persistence):** Where do operator-defined profiles live durably? This lane makes the registry DI/config-seedable only. Open: dedicated JSON store (mirror `JsonCanonicalStatementStore`) vs. options binding vs. DB.
- **O-2 (per-account profile binding):** No per-account profile binding exists (spec §3). Should `ExternalAccountId` select a profile automatically? Currently operator-chosen `MappingProfileId` only.
- **O-3 (matcher seam):** `StatementMatchingEngine` (`StatementMatchingEngine.cs:598-634`) is still never invoked by intake — there's no `StatementPosition` → `NormalizedStatementPosition` adapter. `MatchRows` (the `NormalizedStatementRow` matcher) remains the intake matcher. Connecting the staged exact/tolerance/candidate engine is a separate lane.
- **O-4 (`ImportAsync` error surfacing):** `NormalizedStatementImportResult` has no error list. Decide whether to (a) extend it with parse errors, (b) throw on `IsBlocked`, or (c) only surface errors via the validation DTO path. Recommend (b) for the strict import call + (c) for the validation endpoint.

---

Key files to touch (absolute):
- `/home/user/Meridian-main/src/Meridian.FinancialOperations/Reconciliation/StatementReconciliationService.cs` (primary)
- `/home/user/Meridian-main/src/Meridian.FinancialOperations/Reconciliation/StatementMappingProfiles.cs`
- `/home/user/Meridian-main/src/Meridian.FinancialOperations/Reconciliation/StatementValidationService.cs` (validation bridge, step 13)
- `/home/user/Meridian-main/src/Meridian.FinancialOperations/Reconciliation/ReconciliationServiceRegistration.cs` (DI, step 11)
- `/home/user/Meridian-main/src/Meridian.Contracts/Workstation/StatementReconciliationDtos.cs` (DTO, step 12)
- `/home/user/Meridian-main/src/Meridian.Ui.Shared/Endpoints/WorkstationEndpoints.Reconciliation.cs` (endpoint route, step 12)
- `/home/user/Meridian-main/src/Meridian.Ui.Shared/Contracts/Reconciliation/IReconciliationApiService.cs` (interface, step 12)
- `/home/user/Meridian-main/src/Meridian.Ui.Services/Services/Reconciliation/ReconciliationApiService.cs` and `/home/user/Meridian-main/src/Meridian.Ui.Shared/Services/ReconciliationApiService.cs` (implementations, step 12)
- New test: `/home/user/Meridian-main/tests/Meridian.Tests/Reconciliation/LocalStatementIntakeTests.cs`
