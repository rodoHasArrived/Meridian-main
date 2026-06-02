namespace Meridian.FSharp.Ledger

open System

/// Configuration for a ledger matching rule
type MatchingRule = {
    RuleId              : string
    Description         : string
    AmountTolerancePct  : decimal
    TimingToleranceDays : int
    RequireCurrencyExact: bool
    AllowPartialMatch   : bool
    MinMatchConfidence  : decimal
}

/// Outcome of evaluating a matching rule against one candidate pair
type MatchOutcome =
    | FullMatch    of confidence: decimal
    | PartialMatch of confidence: decimal * reason: string
    | NoMatch      of LedgerBreakClassification

/// A projected-vs-actual candidate pair submitted for rule evaluation
[<CLIMutable>]
type MatchCandidate = {
    CandidateId     : Guid
    SecurityId      : Guid
    ExpectedAmount  : decimal
    ActualAmount    : decimal
    ExpectedCurrency: string
    ActualCurrency  : string
    ExpectedDate    : DateTimeOffset
    ActualDate      : DateTimeOffset
    Notes           : string
}

[<RequireQualifiedAccess>]
module MatchingRule =

    /// Default rule: 1 % amount tolerance, 2-day timing window, currency must match exactly.
    let ``default`` : MatchingRule = {
        RuleId               = "DEFAULT"
        Description          = "Standard cash-flow matching (1% amount, 2-day timing)"
        AmountTolerancePct   = 0.01m
        TimingToleranceDays  = 2
        RequireCurrencyExact = true
        AllowPartialMatch    = false
        MinMatchConfidence   = 0.95m
    }

    /// Strict rule: zero tolerance on both amount and timing.
    let strict : MatchingRule = {
        RuleId               = "STRICT"
        Description          = "Zero-tolerance strict matching"
        AmountTolerancePct   = 0m
        TimingToleranceDays  = 0
        RequireCurrencyExact = true
        AllowPartialMatch    = false
        MinMatchConfidence   = 1.0m
    }

type MatchingThresholdProfile = {
    AmountMaterialityPct    : decimal
    TimingMaterialityDays   : int
    MissingEntryHighAmount  : decimal
    MissingEntryMediumAmount: decimal
}

[<RequireQualifiedAccess>]
module MatchingThresholdProfile =

    let conservative : MatchingThresholdProfile = {
        AmountMaterialityPct = 0.005m
        TimingMaterialityDays = 5
        MissingEntryHighAmount = 10_000m
        MissingEntryMediumAmount = 1_000m
    }

    let liberal : MatchingThresholdProfile = {
        AmountMaterialityPct = 0.010m
        TimingMaterialityDays = 10
        MissingEntryHighAmount = 25_000m
        MissingEntryMediumAmount = 5_000m
    }

[<RequireQualifiedAccess>]
module ReconciliationRules =

    /// Returns true when a candidate match is material for a threshold profile.
    let isMaterialAmountMatch
            (expectedAmount: decimal)
            (actualAmount: decimal)
            (profile: MatchingThresholdProfile) =
        if expectedAmount = 0m then
            abs actualAmount > profile.MissingEntryMediumAmount
        else
            let relativeVariance = abs ((actualAmount - expectedAmount) / expectedAmount)
            relativeVariance >= profile.AmountMaterialityPct
            || abs (actualAmount - expectedAmount) > profile.MissingEntryHighAmount

    /// Classify a missing ledger entry by deterministic amount materiality.
    let classifyMissingEntrySeverity
            (nominalAmount: decimal)
            (profile: MatchingThresholdProfile) =
        if abs nominalAmount >= profile.MissingEntryHighAmount then
            High
        elif abs nominalAmount >= profile.MissingEntryMediumAmount then
            Medium
        else
            Low

    let private isTimingMaterial (daysLate: int) (profile: MatchingThresholdProfile) =
        abs daysLate >= profile.TimingMaterialityDays

    /// Evaluate only the materiality impact of a candidate under the selected profile.
    let evaluateMateriality
            (profile: MatchingThresholdProfile)
            (candidate: MatchCandidate) =
        match candidate.ExpectedAmount, candidate.ActualAmount with
        | expected, actual when expected <> actual ->
            isMaterialAmountMatch expected actual profile
        | _ ->
            false

    /// Apply a single matching rule to a projected-vs-actual candidate pair.
    let apply (rule: MatchingRule) (candidate: MatchCandidate) : MatchOutcome =
        if rule.RequireCurrencyExact
           && not (String.Equals(candidate.ExpectedCurrency, candidate.ActualCurrency, StringComparison.OrdinalIgnoreCase)) then
            NoMatch (CurrencyBreak(candidate.ExpectedCurrency, candidate.ActualCurrency))
        else
            let daysLate = int (candidate.ActualDate - candidate.ExpectedDate).TotalDays
            let variancePct =
                if candidate.ExpectedAmount <> 0m then
                    abs ((candidate.ActualAmount - candidate.ExpectedAmount) / candidate.ExpectedAmount)
                else 0m

            let timingOk = abs daysLate <= rule.TimingToleranceDays
            let amountOk = variancePct <= rule.AmountTolerancePct

            match timingOk, amountOk with
            | true, true ->
                let confidence = 1.0m - variancePct * 0.5m
                FullMatch confidence

            | false, true ->
                let conf = 1.0m - (decimal (abs daysLate) / decimal (max 1 rule.TimingToleranceDays)) * 0.3m
                if rule.AllowPartialMatch && conf >= rule.MinMatchConfidence then
                    PartialMatch(conf, sprintf "Timing drift %d day(s)" (abs daysLate))
                else
                    NoMatch (TimingBreak (abs daysLate))

            | true, false ->
                let conf = 1.0m - (variancePct / max 0.001m rule.AmountTolerancePct) * 0.3m
                if rule.AllowPartialMatch && conf >= rule.MinMatchConfidence then
                    PartialMatch(conf, sprintf "Amount variance %.2f%%" (float variancePct * 100.0))
                else
                    NoMatch (AmountBreak(candidate.ExpectedAmount, candidate.ActualAmount))

            | false, false ->
                // Report the more material dimension
                if variancePct > (decimal (abs daysLate) / 30m) then
                    NoMatch (AmountBreak(candidate.ExpectedAmount, candidate.ActualAmount))
                else
                    NoMatch (TimingBreak (abs daysLate))

    /// Apply a single matching rule and expose deterministic materiality diagnostics.
    let applyWithMateriality
            (rule: MatchingRule)
            (profile: MatchingThresholdProfile)
            (candidate: MatchCandidate) =
        match apply rule candidate with
        | FullMatch _ -> (FullMatch 1.0m, false)
        | PartialMatch (_, reason) ->
            (PartialMatch (1.0m - rule.AmountTolerancePct, reason), isMaterialAmountMatch candidate.ExpectedAmount candidate.ActualAmount profile)
        | NoMatch classification ->
            match classification with
            | AmountBreak _ ->
                (NoMatch classification, isMaterialAmountMatch candidate.ExpectedAmount candidate.ActualAmount profile)
            | TimingBreak days when isTimingMaterial days profile ->
                (NoMatch classification, true)
            | TimingBreak _ ->
                (NoMatch classification, false)
            | CurrencyBreak _ ->
                (NoMatch classification, true)
            | _ ->
                (NoMatch classification, false)

    /// Build canonical break metadata for materiality-aware matching.
    let materialityAdjustedClassifyBreaks
            (profile: MatchingThresholdProfile)
            (runId: Guid)
            (rule: MatchingRule)
            (candidates: MatchCandidate list) : (BreakRecord * bool) list =
        candidates
        |> List.choose (fun candidate ->
            match apply rule candidate with
            | FullMatch _ -> None
            | PartialMatch (_, reason) ->
                let isMaterial = isMaterialAmountMatch candidate.ExpectedAmount candidate.ActualAmount profile
                Some (
                    {
                        BreakId        = Guid.NewGuid()
                        RunId          = runId
                        SecurityId     = candidate.SecurityId
                        FlowId         = candidate.CandidateId
                        Classification = LedgerBreakClassification.asString (TimingBreak 0)
                        TaxonomyVersion = BreakTaxonomyVersion.asString BreakTaxonomyVersion.V1
                        CanonicalClass = CanonicalBreakClass.asString CanonicalBreakClass.CashFlow
                        PrimaryReasonCode = sprintf "PartialMatch:%s:%s" rule.RuleId reason
                        ReasonCodes = [| reason |]
                        IsFallbackClassification = false
                        Severity       = BreakSeverity.asString (if isMaterial then High else Low)
                        ExpectedAmount = candidate.ExpectedAmount
                        ActualAmount   = candidate.ActualAmount
                        Currency       = candidate.ExpectedCurrency
                        ExpectedDate   = candidate.ExpectedDate
                        ActualDate     = Some candidate.ActualDate
                        Notes          = rule.Description
                        CreatedAt      = DateTimeOffset.UtcNow
                        ResolvedAt     = None
                        IsResolved     = false
                    },
                    isMaterial)
            | NoMatch classification ->
                let canonical = ReconciliationClassification.classifyLegacy candidate.ExpectedAmount classification
                let severity =
                    match classification with
                    | AmountBreak(exp, act) when isMaterialAmountMatch exp act profile ->
                        High
                    | AmountBreak _ ->
                        Medium
                    | TimingBreak days -> if isTimingMaterial days profile then High else Low
                    | CurrencyBreak _ -> Critical
                    | MissingEntry -> classifyMissingEntrySeverity candidate.ExpectedAmount profile
                    | DuplicateEntry -> High
                    | ClassificationBreak _ -> High
                    | OtherBreak _ -> Medium
                Some (
                    {
                        BreakId        = Guid.NewGuid()
                        RunId          = runId
                        SecurityId     = candidate.SecurityId
                        FlowId         = candidate.CandidateId
                        Classification = LedgerBreakClassification.asString classification
                        TaxonomyVersion = BreakTaxonomyVersion.asString canonical.TaxonomyVersion
                        CanonicalClass = CanonicalBreakClass.asString canonical.BreakClass
                        PrimaryReasonCode = BreakReasonCode.asString canonical.PrimaryReasonCode
                        ReasonCodes = canonical.ReasonCodes |> List.map BreakReasonCode.asString |> List.toArray
                        IsFallbackClassification = canonical.IsFallback
                        Severity       = BreakSeverity.asString severity
                        ExpectedAmount = candidate.ExpectedAmount
                        ActualAmount   = candidate.ActualAmount
                        Currency       = candidate.ExpectedCurrency
                        ExpectedDate   = candidate.ExpectedDate
                        ActualDate     = Some candidate.ActualDate
                        Notes          = rule.RuleId
                        CreatedAt      = DateTimeOffset.UtcNow
                        ResolvedAt     = None
                        IsResolved     = false
                    },
                    isMaterialAmountMatch candidate.ExpectedAmount candidate.ActualAmount profile)
        )

    /// Apply a prioritised list of rules; accept the first match found.
    /// Falls back to <c>NoMatch MissingEntry</c> if no rule produces a match.
    let applyBest (rules: MatchingRule list) (candidate: MatchCandidate) : MatchOutcome =
        rules
        |> List.tryPick (fun rule ->
            match apply rule candidate with
            | FullMatch c        -> Some (FullMatch c)
            | PartialMatch(c, r) -> Some (PartialMatch(c, r))
            | NoMatch _          -> None)
        |> Option.defaultValue (NoMatch MissingEntry)

    /// Classify all non-matching candidates into <see cref="BreakRecord"/> values.
    let classifyBreaks
            (runId: Guid)
            (rule: MatchingRule)
            (candidates: MatchCandidate list) : BreakRecord list =
        candidates
        |> List.choose (fun c ->
            match apply rule c with
            | FullMatch _ | PartialMatch _ -> None
            | NoMatch classification ->
                let canonical = ReconciliationClassification.classifyLegacy c.ExpectedAmount classification
                Some {
                    BreakId        = Guid.NewGuid()
                    RunId          = runId
                    SecurityId     = c.SecurityId
                    FlowId         = c.CandidateId
                    Classification = LedgerBreakClassification.asString classification
                    TaxonomyVersion = BreakTaxonomyVersion.asString canonical.TaxonomyVersion
                    CanonicalClass = CanonicalBreakClass.asString canonical.BreakClass
                    PrimaryReasonCode = BreakReasonCode.asString canonical.PrimaryReasonCode
                    ReasonCodes = canonical.ReasonCodes |> List.map BreakReasonCode.asString |> List.toArray
                    IsFallbackClassification = canonical.IsFallback
                    Severity       = BreakSeverity.asString canonical.Severity
                    ExpectedAmount = c.ExpectedAmount
                    ActualAmount   = c.ActualAmount
                    Currency       = c.ExpectedCurrency
                    ExpectedDate   = c.ExpectedDate
                    ActualDate     = Some c.ActualDate
                    Notes          = c.Notes
                    CreatedAt      = DateTimeOffset.UtcNow
                    ResolvedAt     = None
                    IsResolved     = false
                })
