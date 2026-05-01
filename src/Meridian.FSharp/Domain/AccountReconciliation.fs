namespace Meridian.FSharp.Domain

open System

/// Position-focused reconciliation break for custody/position validation.
type PositionReconciliationBreak = {
    BreakId: Guid
    AccountId: AccountId
    AsOfDate: DateOnly
    Symbol: string
    ExpectedQuantity: decimal
    ActualQuantity: decimal
    QuantityVariance: decimal
    Reason: string
}

/// Cash-focused reconciliation break for bank/cash-balance validation.
type CashReconciliationBreak = {
    BreakId: Guid
    AccountId: AccountId
    AsOfDate: DateOnly
    Currency: string
    ExpectedBalance: decimal
    ActualBalance: decimal
    BalanceVariance: decimal
    Reason: string
}

/// Shared envelope for account-level aggregation in UI/API projections.
type AccountReconciliationBreak =
    | Position of PositionReconciliationBreak
    | Cash of CashReconciliationBreak

