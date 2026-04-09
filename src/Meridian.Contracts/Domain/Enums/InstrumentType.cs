namespace Meridian.Contracts.Domain.Enums;

/// <summary>
/// Classification of financial instruments supported by the system.
/// </summary>
public enum InstrumentType : byte
{
    /// <summary>
    /// Equity security (common stock, ETF, ADR).
    /// </summary>
    Equity = 0,

    /// <summary>
    /// Exchange-traded option on an individual equity or ETF.
    /// </summary>
    EquityOption = 1,

    /// <summary>
    /// Exchange-traded option on a market index (e.g., SPX, NDX, RUT, VIX).
    /// Typically European-style, cash-settled.
    /// </summary>
    IndexOption = 2,

    /// <summary>
    /// Exchange-traded futures contract.
    /// </summary>
    Future = 3,

    /// <summary>
    /// Single stock future — a futures contract on an individual equity.
    /// </summary>
    SingleStockFuture = 4,

    /// <summary>
    /// Foreign exchange spot pair (e.g. EUR/USD). Maps to IB SecType "CASH".
    /// </summary>
    Forex = 5,

    /// <summary>
    /// Physical or soft commodity (e.g. gold, oil, corn). Maps to IB SecType "CMDTY".
    /// </summary>
    Commodity = 6,

    /// <summary>
    /// Cryptocurrency spot instrument (e.g. BTC/USD). Maps to IB SecType "CRYPTO".
    /// </summary>
    Crypto = 7,

    /// <summary>
    /// Bond or fixed-income instrument. Maps to IB SecType "BOND".
    /// </summary>
    Bond = 8,

    /// <summary>
    /// Option on a futures contract (e.g. option on ES). Maps to IB SecType "FOP".
    /// </summary>
    FuturesOption = 9,

    /// <summary>
    /// Market index (non-tradeable reference, e.g. SPX, VIX). Maps to IB SecType "IND".
    /// </summary>
    Index = 10,

    /// <summary>
    /// Contract for Difference. Maps to IB SecType "CFD".
    /// </summary>
    CFD = 11,

    /// <summary>
    /// Equity or structured warrant. Maps to IB SecType "WAR".
    /// </summary>
    Warrant = 12,

    /// <summary>
    /// Interest-rate, credit, or total-return swap.
    /// </summary>
    Swap = 13,

    /// <summary>
    /// Directly originated private-credit loan.
    /// </summary>
    DirectLoan = 14,

    /// <summary>
    /// Repurchase agreement (repo / reverse-repo).
    /// </summary>
    Repo = 15,

    /// <summary>
    /// Bank deposit instrument (demand, time, or notice deposit).
    /// </summary>
    Deposit = 16
}
