namespace Meridian.FSharp.Domain

open System

/// External balance snapshot captured from a custodian or bank statement.
/// Stored for reconciliation against the internal ledger.
type AccountBalanceSnapshot = {
    SnapshotId: Guid
    AccountId: AccountId
    FundId: FundId option
    AsOfDate: DateOnly
    Currency: string
    CashBalance: decimal
    SecuritiesMarketValue: decimal option   // populated for custodian accounts
    AccruedInterest: decimal option
    PendingSettlement: decimal option
    Source: string                          // "CustodianStatement" | "BankStatement" | "Manual"
    RecordedAt: DateTimeOffset
    ExternalReference: string option
}

/// One position line from a custodian statement.
/// Used as the "actual" side in account-level position reconciliation.
type CustodianPositionLine = {
    PositionLineId: Guid
    BatchId: Guid
    AccountId: AccountId
    AsOfDate: DateOnly
    SecurityIdentifier: string
    IdentifierType: string                  // "ISIN" | "CUSIP" | "TICKER"
    Quantity: decimal
    MarketValue: decimal
    MarketValueCurrency: string
    CostBasis: decimal option
    AccruedIncome: decimal option
    SettlementPending: bool
    RawPayload: string option               // original statement line as JSON
}

/// One transaction line from a bank account statement.
/// Used as the "actual" side in cash reconciliation.
type BankStatementLine = {
    StatementLineId: Guid
    BatchId: Guid
    AccountId: AccountId
    StatementDate: DateOnly
    ValueDate: DateOnly
    Amount: decimal
    Currency: string
    TransactionType: string
    Description: string
    ExternalReference: string option
    RunningBalance: decimal option
}

[<RequireQualifiedAccess>]
module FundAccountDetailsOps =
    let tryGetCustodian = function
        | FundAccountDetails.Custodian d -> Some d
        | _ -> None

    let tryGetBank = function
        | FundAccountDetails.Bank d -> Some d
        | _ -> None

    let isCustodian details = details |> tryGetCustodian |> Option.isSome
    let isBank      details = details |> tryGetBank      |> Option.isSome
