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

module AccrualSummary =
    let summarize (loanId: Guid) (periodStartDate: DateOnly) (periodEndDate: DateOnly) (currency: string) (entries: AccrualEntry seq) =
        let periodEntries =
            entries
            |> Seq.filter (fun entry ->
                entry.LoanId = loanId
                && entry.PeriodStartDate = periodStartDate
                && entry.PeriodEndDate = periodEndDate
                && String.Equals(entry.Currency, currency, StringComparison.OrdinalIgnoreCase))
            |> Seq.toArray

        {
            LoanId = loanId
            PeriodStartDate = periodStartDate
            PeriodEndDate = periodEndDate
            Currency = currency
            EntryCount = periodEntries.Length
            InterestAmount = periodEntries |> Array.sumBy _.InterestAmount
            CommitmentFeeAmount = periodEntries |> Array.sumBy _.CommitmentFeeAmount
            PenaltyAmount = periodEntries |> Array.sumBy _.PenaltyAmount
            SourceEventIds = periodEntries |> Array.map _.SourceEventId
        }
