namespace Meridian.FSharp.Ledger

open System

[<CLIMutable>]
type LedgerLineInput = {
    EntryId: Guid
    JournalEntryId: Guid
    Timestamp: DateTimeOffset
    AccountName: string
    AccountType: int
    Symbol: string
    FinancialAccountId: string
    Debit: decimal
    Credit: decimal
    Description: string
}

[<CLIMutable>]
type LedgerBalanceInput = {
    AccountName: string
    AccountType: int
    Symbol: string
    FinancialAccountId: string
    Debit: decimal
    Credit: decimal
}
