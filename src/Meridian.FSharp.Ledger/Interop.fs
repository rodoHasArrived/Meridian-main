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
type AccrualValidationDto = {
    IsValid: bool
    Errors: string array
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
    Severity: string
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
    Severity: string
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

[<CLIMutable>]
type AccountingPeriodDto = {
    PeriodId   : Guid
    FiscalYear : int
    PeriodNo   : int
    Label      : string
    StartDate  : DateOnly
    EndDate    : DateOnly
    Status     : string
    OpenedAt   : DateTimeOffset
    ClosedAt   : DateTimeOffset option
}

[<CLIMutable>]
type PeriodCloseEventDto = {
    EventId     : Guid
    PeriodId    : Guid
    PriorStatus : string
    NewStatus   : string
    ClosedBy    : string
    Notes       : string
    RecordedAt  : DateTimeOffset
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

    static member ValidateAccrualEntry(entry: AccrualEntry) =
        let errors = AccrualEntry.validate entry
        {
            IsValid = errors.Length = 0
            Errors = errors
        }

    static member BuildAccrualSummary(
        loanId: Guid,
        periodStartDate: DateOnly,
        periodEndDate: DateOnly,
        currency: string,
        entries: seq<AccrualEntry>) =
        AccrualSummary.summarize loanId periodStartDate periodEndDate currency entries

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
                Severity = result.Severity
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
        if String.IsNullOrWhiteSpace value then None else Some (value.Trim())

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
            Severity = BreakSeverity.asString classification.Severity
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

    // ── Period management ────────────────────────────────────────────────────

    static member private ToPeriodDto (period: AccountingPeriod) : AccountingPeriodDto =
        { PeriodId   = period.PeriodId
          FiscalYear = period.FiscalYear
          PeriodNo   = period.PeriodNo
          Label      = period.Label
          StartDate  = period.StartDate
          EndDate    = period.EndDate
          Status     = PeriodStatus.asString period.Status
          OpenedAt   = period.OpenedAt
          ClosedAt   = period.ClosedAt }

    static member private ToCloseEventDto (event: PeriodCloseEvent) : PeriodCloseEventDto =
        { EventId     = event.EventId
          PeriodId    = event.PeriodId
          PriorStatus = PeriodStatus.asString event.PriorStatus
          NewStatus   = PeriodStatus.asString event.NewStatus
          ClosedBy    = event.ClosedBy
          Notes       = event.Notes
          RecordedAt  = event.RecordedAt }

    static member private FromPeriodDto (dto: AccountingPeriodDto) (status: PeriodStatus) : AccountingPeriod =
        { PeriodId   = dto.PeriodId
          FiscalYear = dto.FiscalYear
          PeriodNo   = dto.PeriodNo
          Label      = dto.Label
          StartDate  = dto.StartDate
          EndDate    = dto.EndDate
          Status     = status
          OpenedAt   = dto.OpenedAt
          ClosedAt   = dto.ClosedAt }

    /// <summary>Generate calendar-month accounting periods for a fiscal year.</summary>
    static member GenerateCalendarMonthPeriods(fiscalYear: int, now: DateTimeOffset) : AccountingPeriodDto array =
        PeriodManagement.generateCalendarMonthPeriods fiscalYear now
        |> List.map LedgerInterop.ToPeriodDto
        |> List.toArray

    /// <summary>Check whether a posting at the given date is permitted.</summary>
    /// <param name="date">Date of the posting being validated.</param>
    /// <param name="isAdjustment">True when this is an approved adjustment entry rather than an originating posting.</param>
    /// <param name="periods">Active period list.</param>
    /// <returns>
    ///   <c>"Allowed"</c> when the posting can proceed, <c>"PeriodNotFound"</c> when no period covers the date,
    ///   or <c>"Denied: &lt;reason&gt;"</c> when the period rejects the posting.
    /// </returns>
    static member CheckPostingDate(date: DateOnly, isAdjustment: bool, periods: seq<AccountingPeriodDto>) : string =
        let domainPeriods =
            periods
            |> Seq.choose (fun dto ->
                PeriodStatus.fromString dto.Status
                |> Option.map (LedgerInterop.FromPeriodDto dto))
            |> Seq.toList

        match PeriodManagement.checkPostingDate date isAdjustment domainPeriods with
        | Allowed          -> "Allowed"
        | PeriodNotFound   -> "PeriodNotFound"
        | Denied reason    -> sprintf "Denied: %s" reason

    /// <summary>Attempt to soft-close or hard-close a period.</summary>
    /// <param name="period">The period to close.</param>
    /// <param name="newStatus">Target status: "SoftClosed" or "HardClosed".</param>
    /// <param name="closedBy">Identifier of the operator requesting the close.</param>
    /// <param name="notes">Optional close notes.</param>
    /// <param name="now">Wall-clock time to stamp the close event.</param>
    /// <param name="error">Populated with a failure message when the operation cannot proceed; otherwise null.</param>
    /// <returns>
    ///   The <see cref="PeriodCloseEventDto"/> on success, or default when the period is already closed
    ///   or the transition is invalid.
    /// </returns>
    static member TryClosePeriod(
            period: AccountingPeriodDto,
            newStatus: string,
            closedBy: string,
            notes: string,
            now: DateTimeOffset,
            [<System.Runtime.InteropServices.Out>] error: string byref) : PeriodCloseEventDto =
        match PeriodStatus.fromString newStatus, PeriodStatus.fromString period.Status with
        | None, _ ->
            error <- sprintf "Unknown target period status '%s'." newStatus
            Unchecked.defaultof<PeriodCloseEventDto>
        | _, None ->
            error <- sprintf "Unknown existing period status '%s'." period.Status
            Unchecked.defaultof<PeriodCloseEventDto>
        | Some targetStatus, Some priorStatus ->
            let domainPeriod = LedgerInterop.FromPeriodDto period priorStatus
            match PeriodManagement.close domainPeriod targetStatus closedBy notes now with
            | CloseAccepted event ->
                error <- null
                LedgerInterop.ToCloseEventDto event
            | AlreadyClosed ->
                error <- sprintf "Period '%s' is already hard-closed." period.Label
                Unchecked.defaultof<PeriodCloseEventDto>
            | InvalidTransition reason ->
                error <- reason
                Unchecked.defaultof<PeriodCloseEventDto>


