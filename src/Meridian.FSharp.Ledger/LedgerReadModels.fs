namespace Meridian.FSharp.Ledger

[<CLIMutable>]
type TrialBalanceRow = {
    AccountName: string
    AccountType: int
    Symbol: string
    FinancialAccountId: string
    Balance: decimal
}

[<RequireQualifiedAccess>]
module LedgerReadModels =

    let buildTrialBalance (lines: LedgerBalanceInput seq) =
        lines
        |> Seq.groupBy (fun line -> line.AccountName, line.AccountType, line.Symbol, line.FinancialAccountId)
        |> Seq.map (fun ((accountName, accountType, symbol, financialAccountId), groupedLines) ->
            let debits = groupedLines |> Seq.sumBy (fun line -> line.Debit)
            let credits = groupedLines |> Seq.sumBy (fun line -> line.Credit)
            {
                AccountName = accountName
                AccountType = accountType
                Symbol = symbol
                FinancialAccountId = financialAccountId
                Balance = Posting.calculateNetBalance accountType debits credits
            })
        |> Seq.toArray
