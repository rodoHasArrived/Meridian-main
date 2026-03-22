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
