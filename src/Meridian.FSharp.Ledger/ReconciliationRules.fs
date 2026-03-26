namespace Meridian.FSharp.Ledger

open System
open ReconciliationTypes

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

[<RequireQualifiedAccess>]
module ReconciliationRules =

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
                let sev = LedgerBreakClassification.severity c.ExpectedAmount classification
                Some {
                    BreakId        = Guid.NewGuid()
                    RunId          = runId
                    SecurityId     = c.SecurityId
                    FlowId         = c.CandidateId
                    Classification = LedgerBreakClassification.asString classification
                    Severity       = BreakSeverity.asString sev
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
