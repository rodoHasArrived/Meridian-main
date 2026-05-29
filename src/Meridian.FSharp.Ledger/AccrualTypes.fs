namespace Meridian.FSharp.Ledger

open System

[<CLIMutable>]
type AccrualEntry = {
    AccrualEntryId: Guid
    LoanId: Guid
    AccrualDate: DateOnly
    PeriodStartDate: DateOnly
    PeriodEndDate: DateOnly
    InterestAmount: decimal
    CommitmentFeeAmount: decimal
    PenaltyAmount: decimal
    Currency: string
    SourceEventId: Guid
    SourceEventType: string
    AggregateVersion: int64
    RecordedAt: DateTimeOffset
}

[<CLIMutable>]
type AccrualSummary = {
    LoanId: Guid
    PeriodStartDate: DateOnly
    PeriodEndDate: DateOnly
    Currency: string
    EntryCount: int
    InterestAmount: decimal
    CommitmentFeeAmount: decimal
    PenaltyAmount: decimal
    SourceEventIds: Guid array
}

module AccrualEntry =
    let validate (entry: AccrualEntry) =
        [|
            if entry.AccrualEntryId = Guid.Empty then
                "Accrual entry id is required."
            if entry.LoanId = Guid.Empty then
                "Loan id is required."
            if entry.SourceEventId = Guid.Empty then
                "Source event id is required."
            if String.IsNullOrWhiteSpace(entry.SourceEventType) then
                "Source event type is required."
            if String.IsNullOrWhiteSpace(entry.Currency) then
                "Currency is required."
            if entry.PeriodStartDate > entry.PeriodEndDate then
                "Accrual period start date must be on or before period end date."
            if entry.AccrualDate < entry.PeriodStartDate || entry.AccrualDate > entry.PeriodEndDate then
                "Accrual date must fall within the accrual period slice."
            if entry.InterestAmount < 0m then
                "Interest amount cannot be negative."
            if entry.CommitmentFeeAmount < 0m then
                "Commitment fee amount cannot be negative."
            if entry.PenaltyAmount < 0m then
                "Penalty amount cannot be negative."
            if entry.AggregateVersion < 0L then
                "Aggregate version cannot be negative."
        |]

module AccrualSummary =
    let private normalizeCurrency (currency: string) =
        if isNull currency then String.Empty else currency.Trim().ToUpperInvariant()

    let summarize (loanId: Guid) (periodStartDate: DateOnly) (periodEndDate: DateOnly) (currency: string) (entries: AccrualEntry seq) =
        if loanId = Guid.Empty then
            invalidArg (nameof loanId) "Loan id is required."
        if periodStartDate > periodEndDate then
            invalidArg (nameof periodStartDate) "Summary period start date must be on or before period end date."
        if String.IsNullOrWhiteSpace(currency) then
            invalidArg (nameof currency) "Currency is required."

        let normalizedCurrency = normalizeCurrency currency
        let periodEntries =
            entries
            |> Seq.filter (fun entry -> AccrualEntry.validate entry |> Array.isEmpty)
            |> Seq.filter (fun entry ->
                entry.LoanId = loanId
                && entry.AccrualDate >= periodStartDate
                && entry.AccrualDate <= periodEndDate
                && String.Equals(normalizeCurrency entry.Currency, normalizedCurrency, StringComparison.Ordinal))
            |> Seq.sortBy (fun entry -> entry.AccrualDate, entry.RecordedAt, entry.AccrualEntryId)
            |> Seq.toArray

        {
            LoanId = loanId
            PeriodStartDate = periodStartDate
            PeriodEndDate = periodEndDate
            Currency = normalizedCurrency
            EntryCount = periodEntries.Length
            InterestAmount = periodEntries |> Array.sumBy _.InterestAmount
            CommitmentFeeAmount = periodEntries |> Array.sumBy _.CommitmentFeeAmount
            PenaltyAmount = periodEntries |> Array.sumBy _.PenaltyAmount
            SourceEventIds = periodEntries |> Array.map _.SourceEventId |> Array.distinct
        }
