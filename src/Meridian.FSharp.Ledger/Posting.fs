namespace Meridian.FSharp.Ledger

/// Ordinals correspond to the C# LedgerAccountType enum:
///   Asset = 0 | Liability = 1 | Equity = 2 | Revenue = 3 | Expense = 4
/// Debit-normal accounts (normal balance is a debit): Asset, Expense.
/// Credit-normal accounts (normal balance is a credit): Liability, Equity, Revenue.
[<RequireQualifiedAccess>]
module Posting =

    [<Literal>]
    let private AssetOrdinal = 0

    [<Literal>]
    let private ExpenseOrdinal = 4

    let calculateNetBalance (accountType: int) (debits: decimal) (credits: decimal) =
        match accountType with
        | AssetOrdinal
        | ExpenseOrdinal -> debits - credits   // debit-normal: net balance = debits - credits
        | _ -> credits - debits                // credit-normal: net balance = credits - debits
