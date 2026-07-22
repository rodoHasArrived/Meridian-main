namespace Meridian.FSharp.Ledger

/// Ordinals correspond to the C# LedgerAccountType enum:
///   Asset = 0 | Liability = 1 | Equity = 2 | Revenue = 3 | Expense = 4
/// Debit-normal accounts (normal balance is a debit): Asset, Expense.
/// Credit-normal accounts (normal balance is a credit): Liability, Equity, Revenue.
///
/// CROSS-LANGUAGE CONTRACT: this project cannot reference Meridian.Ledger (the dependency
/// points the other way), so the ordinals below duplicate the C# enum by value. Reordering
/// LedgerAccountType would silently flip balance signs here. The pinning is enforced by
/// LedgerAccountTypeOrdinalContractTests in Meridian.Tests — update both together.
[<RequireQualifiedAccess>]
module Posting =

    [<Literal>]
    let AssetOrdinal = 0

    [<Literal>]
    let LiabilityOrdinal = 1

    [<Literal>]
    let EquityOrdinal = 2

    [<Literal>]
    let RevenueOrdinal = 3

    [<Literal>]
    let ExpenseOrdinal = 4

    let calculateNetBalance (accountType: int) (debits: decimal) (credits: decimal) =
        match accountType with
        | AssetOrdinal
        | ExpenseOrdinal -> debits - credits   // debit-normal: net balance = debits - credits
        | _ -> credits - debits                // credit-normal: net balance = credits - debits
