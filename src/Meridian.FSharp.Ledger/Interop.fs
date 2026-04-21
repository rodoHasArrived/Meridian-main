namespace Meridian.FSharp.Ledger

open System
open System.Runtime.CompilerServices

[<CLIMutable>]
type LedgerValidationDto = {
    IsValid: bool
    Errors: string array
    TotalDebit: decimal
    TotalCredit: decimal
}

[<CLIMutable>]
type LedgerBalanceResultDto = {
    AccountName: string
    AccountType: int
    Symbol: string
    FinancialAccountId: string
    Balance: decimal
}

[<CLIMutable>]
type PortfolioLedgerCheckDto = {
    CheckId: string
    Label: string
    ExpectedSource: string
    ActualSource: string
    ExpectedAmount: decimal
    ActualAmount: decimal
    HasExpectedAmount: bool
    HasActualAmount: bool
    ExpectedPresent: bool
    ActualPresent: bool
    ExpectedAsOf: DateTimeOffset
    ActualAsOf: DateTimeOffset
    HasExpectedAsOf: bool
    HasActualAsOf: bool
    CategoryHint: string
    MissingSourceHint: string
    ActualKind: string
}

[<CLIMutable>]
type PortfolioLedgerCheckResultDto = {
    CheckId: string
    Label: string
    IsMatch: bool
    Category: string
    Status: string
    MissingSource: string
    ExpectedSource: string
    ActualSource: string
    ExpectedAmount: decimal
    ActualAmount: decimal
    HasExpectedAmount: bool
    HasActualAmount: bool
    Variance: decimal
    Reason: string
    ExpectedAsOf: DateTimeOffset
    ActualAsOf: DateTimeOffset
    HasExpectedAsOf: bool
    HasActualAsOf: bool
}

[<CLIMutable>]
type ReconciliationOutcomeDto = {
    Outcome: string
    Variance: decimal option
    DaysLate: int option
    ExpectedCurrency: string option
    ActualCurrency: string option
}

[<CLIMutable>]
type ReconciliationResultDto = {
    SecurityId: string
    FlowId: string
    EventId: string
    ExpectedAmount: decimal
    ActualAmount: decimal
    Variance: decimal
    ExpectedCurrency: string
    ActualCurrency: string
    DueDate: DateTimeOffset
    PostedAt: DateTimeOffset
    Outcome: ReconciliationOutcomeDto
    Status: string
}

[<CLIMutable>]
type BreakFactsDto = {
    BreakType: string
    ExpectedQuantity: decimal option
    ActualQuantity: decimal option
    ExpectedPrice: decimal option
    ActualPrice: decimal option
    ExpectedInstrumentId: string
    ActualInstrumentId: string
    ExpectedCashAmount: decimal option
    ActualCashAmount: decimal option
    ExpectedCurrency: string
    ActualCurrency: string
    ExpectedSettlementDate: DateTimeOffset option
    ActualSettlementDate: DateTimeOffset option
    TimingToleranceDays: int
    ExpectedCorporateActionType: string
    ActualCorporateActionType: string
    ExpectedCorporateActionFactor: decimal option
    ActualCorporateActionFactor: decimal option
    MappingKey: string
    MappingResolved: bool option
}

[<CLIMutable>]
type CanonicalBreakClassificationDto = {
    TaxonomyVersion: string
    BreakClass: string
    PrimaryReasonCode: string
    ReasonCodes: string array
    IsFallback: bool
}

[<CLIMutable>]
type BreakRecordClassificationDto = {
    BreakId: Guid
    RunId: Guid
    SecurityId: Guid
    FlowId: Guid
    LegacyClassification: string
    TaxonomyVersion: string
    CanonicalClass: string
    PrimaryReasonCode: string
    ReasonCodes: string array
    Severity: string
    IsFallbackClassification: bool
    Notes: string
}

[<Sealed; Extension>]
type LedgerInterop private () =

    static member ValidateJournalEntry(
        journalEntryId: Guid,
        timestamp: DateTimeOffset,
        description: string,
        lines: seq<LedgerLineInput>,
        existingJournalIds: seq<Guid>,
        existingEntryIds: seq<Guid>) =
        let totalDebit, totalCredit, errors =
            JournalValidation.validate journalEntryId timestamp description (lines |> Seq.toList) (existingJournalIds |> Set.ofSeq) (existingEntryIds |> Set.ofSeq)

        {
            IsValid = List.isEmpty errors
            Errors = errors |> List.toArray
            TotalDebit = totalDebit
            TotalCredit = totalCredit
        }

    static member CalculateNetBalance(accountType: int, debits: decimal, credits: decimal) =
        Posting.calculateNetBalance accountType debits credits

    static member BuildTrialBalance(lines: seq<LedgerBalanceInput>) : LedgerBalanceResultDto array =
        LedgerReadModels.buildTrialBalance lines
        |> Array.map (fun (row: TrialBalanceRow) ->
            {
                AccountName = row.AccountName
                AccountType = row.AccountType
                Symbol = row.Symbol
                FinancialAccountId = row.FinancialAccountId
                Balance = row.Balance
            })

    static member ClassifyDifference(expected: decimal, actual: decimal) =
        Reconciliation.classifyDifference expected actual

    static member ReconcilePortfolioLedgerChecks(
        amountTolerance: decimal,
        maxAsOfDriftMinutes: int,
        checks: seq<PortfolioLedgerCheckDto>) : PortfolioLedgerCheckResultDto array =
        checks
        |> Seq.map (fun (check: PortfolioLedgerCheckDto) ->
            ({
                CheckId = check.CheckId
                Label = check.Label
                ExpectedSource = check.ExpectedSource
                ActualSource = check.ActualSource
                ExpectedAmount = check.ExpectedAmount
                ActualAmount = check.ActualAmount
                HasExpectedAmount = check.HasExpectedAmount
                HasActualAmount = check.HasActualAmount
                ExpectedPresent = check.ExpectedPresent
                ActualPresent = check.ActualPresent
                ExpectedAsOf = check.ExpectedAsOf
                ActualAsOf = check.ActualAsOf
                HasExpectedAsOf = check.HasExpectedAsOf
                HasActualAsOf = check.HasActualAsOf
                CategoryHint = check.CategoryHint
                MissingSourceHint = check.MissingSourceHint
                ActualKind = check.ActualKind
            } : PortfolioLedgerCheck))
        |> Reconciliation.reconcilePortfolioLedgerChecks amountTolerance maxAsOfDriftMinutes
        |> Array.map (fun (result: PortfolioLedgerCheckResult) ->
            ({
                CheckId = result.CheckId
                Label = result.Label
                IsMatch = result.IsMatch
                Category = result.Category
                Status = result.Status
                MissingSource = result.MissingSource
                ExpectedSource = result.ExpectedSource
                ActualSource = result.ActualSource
                ExpectedAmount = result.ExpectedAmount
                ActualAmount = result.ActualAmount
                HasExpectedAmount = result.HasExpectedAmount
                HasActualAmount = result.HasActualAmount
                Variance = result.Variance
                Reason = result.Reason
                ExpectedAsOf = result.ExpectedAsOf
                ActualAsOf = result.ActualAsOf
                HasExpectedAsOf = result.HasExpectedAsOf
                HasActualAsOf = result.HasActualAsOf
            } : PortfolioLedgerCheckResultDto))

    static member private OutcomeToDto(outcome: ReconciliationOutcome) : ReconciliationOutcomeDto =
        let ccy = ReconciliationOutcome.currencies outcome
        {
            Outcome = ReconciliationOutcome.label outcome
            Variance = ReconciliationOutcome.variance outcome
            DaysLate = ReconciliationOutcome.daysLate outcome
            ExpectedCurrency = ccy |> Option.map fst
            ActualCurrency = ccy |> Option.map snd
        }

    static member ToReconciliationResultDtos(results: seq<ReconciliationResult>) : ReconciliationResultDto array =
        results
        |> Seq.map (fun result ->
            {
                SecurityId = result.SecurityId
                FlowId = result.FlowId
                EventId = result.EventId
                ExpectedAmount = result.ExpectedAmount
                ActualAmount = result.ActualAmount
                Variance = result.Variance
                ExpectedCurrency = result.ExpectedCurrency
                ActualCurrency = result.ActualCurrency
                DueDate = result.DueDate
                PostedAt = result.PostedAt
                Outcome = LedgerInterop.OutcomeToDto result.Outcome
                Status = result.OutcomeLabel
            })
        |> Seq.toArray

    static member private NormalizeString (value: string) : string option =
        if String.IsNullOrWhiteSpace value then None else Some value

    static member private ToRawBreakFacts (dto: BreakFactsDto) : RawBreakFacts =
        {
            BreakType = LedgerInterop.NormalizeString dto.BreakType
            ExpectedQuantity = dto.ExpectedQuantity
            ActualQuantity = dto.ActualQuantity
            ExpectedPrice = dto.ExpectedPrice
            ActualPrice = dto.ActualPrice
            ExpectedInstrumentId = LedgerInterop.NormalizeString dto.ExpectedInstrumentId
            ActualInstrumentId = LedgerInterop.NormalizeString dto.ActualInstrumentId
            ExpectedCashAmount = dto.ExpectedCashAmount
            ActualCashAmount = dto.ActualCashAmount
            ExpectedCurrency = LedgerInterop.NormalizeString dto.ExpectedCurrency
            ActualCurrency = LedgerInterop.NormalizeString dto.ActualCurrency
            ExpectedSettlementDate = dto.ExpectedSettlementDate
            ActualSettlementDate = dto.ActualSettlementDate
            TimingToleranceDays = dto.TimingToleranceDays
            ExpectedCorporateActionType = LedgerInterop.NormalizeString dto.ExpectedCorporateActionType
            ActualCorporateActionType = LedgerInterop.NormalizeString dto.ActualCorporateActionType
            ExpectedCorporateActionFactor = dto.ExpectedCorporateActionFactor
            ActualCorporateActionFactor = dto.ActualCorporateActionFactor
            MappingKey = LedgerInterop.NormalizeString dto.MappingKey
            MappingResolved = dto.MappingResolved
        }

    static member private ToCanonicalBreakClassificationDto
        (classification: CanonicalBreakClassification)
        : CanonicalBreakClassificationDto =
        {
            TaxonomyVersion = BreakTaxonomyVersion.asString classification.TaxonomyVersion
            BreakClass = CanonicalBreakClass.asString classification.BreakClass
            PrimaryReasonCode = BreakReasonCode.asString classification.PrimaryReasonCode
            ReasonCodes = classification.ReasonCodes |> List.map BreakReasonCode.asString |> List.toArray
            IsFallback = classification.IsFallback
        }

    static member ClassifyBreakFacts(facts: seq<BreakFactsDto>) : CanonicalBreakClassificationDto array =
        facts
        |> Seq.map LedgerInterop.ToRawBreakFacts
        |> Seq.map ReconciliationClassification.classify
        |> Seq.map LedgerInterop.ToCanonicalBreakClassificationDto
        |> Seq.toArray

    static member ToBreakRecordClassificationDtos(records: seq<BreakRecord>) : BreakRecordClassificationDto array =
        records
        |> Seq.map (fun record ->
            {
                BreakId = record.BreakId
                RunId = record.RunId
                SecurityId = record.SecurityId
                FlowId = record.FlowId
                LegacyClassification = record.Classification
                TaxonomyVersion = record.TaxonomyVersion
                CanonicalClass = record.CanonicalClass
                PrimaryReasonCode = record.PrimaryReasonCode
                ReasonCodes = record.ReasonCodes
                Severity = record.Severity
                IsFallbackClassification = record.IsFallbackClassification
                Notes = record.Notes
            })
        |> Seq.toArray
