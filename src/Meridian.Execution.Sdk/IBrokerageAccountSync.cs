namespace Meridian.Execution.Sdk;

/// <summary>
/// Enumerates brokerage or custodian accounts that can be imported into Meridian's
/// read-side Portfolio, ledger, and Accounting workflows.
/// </summary>
public interface IBrokerageAccountCatalog
{
    string ProviderId { get; }

    string ProviderDisplayName { get; }

    Task<IReadOnlyList<BrokerageExternalAccountDto>> GetAccountsAsync(CancellationToken ct = default);
}

/// <summary>
/// Reads point-in-time account, balance, and position state from a brokerage or custodian.
/// </summary>
public interface IBrokeragePortfolioSync
{
    string ProviderId { get; }

    Task<BrokeragePortfolioSnapshotDto> GetPortfolioSnapshotAsync(
        string externalAccountId,
        CancellationToken ct = default);
}

/// <summary>
/// Reads recent orders, fills, and cash movements from a brokerage or custodian.
/// </summary>
public interface IBrokerageActivitySync
{
    string ProviderId { get; }

    Task<BrokerageActivitySnapshotDto> GetActivitySnapshotAsync(
        string externalAccountId,
        DateTimeOffset? since = null,
        CancellationToken ct = default);
}

public sealed record BrokerageExternalAccountDto(
    string ProviderId,
    string AccountId,
    string DisplayName,
    string Status,
    string Currency,
    DateTimeOffset RetrievedAt,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record BrokeragePortfolioSnapshotDto(
    BrokerageExternalAccountDto Account,
    BrokerageBalanceSnapshotDto Balance,
    IReadOnlyList<BrokeragePositionSnapshotDto> Positions,
    DateTimeOffset RetrievedAt,
    BrokerageAccountSnapshotDto? AccountSnapshot = null,
    IReadOnlyList<BrokerageTaxLotSnapshotDto>? TaxLots = null,
    IReadOnlyList<BrokerageBorrowPositionSnapshotDto>? BorrowPositions = null);

public sealed record BrokerageBalanceSnapshotDto(
    decimal Cash,
    decimal Equity,
    decimal BuyingPower,
    string Currency,
    decimal MarginBalance = 0m);

public sealed record BrokeragePositionSnapshotDto(
    string Symbol,
    decimal Quantity,
    decimal AverageEntryPrice,
    decimal MarketPrice,
    decimal MarketValue,
    decimal UnrealizedPnl,
    string AssetClass,
    string? Description = null,
    string? PositionId = null,
    string? Currency = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record BrokerageActivitySnapshotDto(
    string ProviderId,
    string AccountId,
    DateTimeOffset RetrievedAt,
    IReadOnlyList<BrokerageOrderSnapshotDto> Orders,
    IReadOnlyList<BrokerageFillSnapshotDto> Fills,
    IReadOnlyList<BrokerageCashTransactionDto> CashTransactions,
    IReadOnlyList<BrokerageCorporateActionSnapshotDto>? CorporateActions = null,
    IReadOnlyList<BrokerageActivityEventDto>? Activities = null,
    BrokerageActivityCursorDto? Cursor = null);

/// <summary>Broker-neutral margin regime reported for an external account.</summary>
public enum BrokerageMarginRegime
{
    Unknown = 0,
    Cash = 1,
    RegulationT = 2,
    PortfolioMargin = 3,
    Other = 4
}

/// <summary>
/// Canonical point-in-time external account evidence. Nullable monetary fields mean the provider
/// did not report the value; callers must not reinterpret absence as zero.
/// </summary>
public sealed record BrokerageAccountSnapshotDto(
    string ProviderId,
    string AccountId,
    DateTimeOffset AsOf,
    string Currency,
    string Status,
    BrokerageMarginRegime MarginRegime,
    decimal Cash,
    decimal Equity,
    decimal BuyingPower,
    decimal? SettledCash = null,
    decimal? UnsettledCash = null,
    decimal? LongMarketValue = null,
    decimal? ShortMarketValue = null,
    decimal? RegTBuyingPower = null,
    decimal? InitialMargin = null,
    decimal? MaintenanceMargin = null,
    decimal? LastMaintenanceMargin = null,
    decimal? ExcessLiquidity = null,
    decimal? SpecialMemorandumAccount = null,
    decimal? MarginLoan = null,
    decimal? Multiplier = null,
    bool TradingBlocked = false,
    bool TransfersBlocked = false,
    bool AccountBlocked = false,
    bool ShortingEnabled = false,
    int? OptionsApprovedLevel = null,
    int? OptionsTradingLevel = null,
    IReadOnlyList<string>? Restrictions = null,
    IReadOnlyDictionary<string, string>? SourceAttributes = null);

/// <summary>High-level economic category shared by provider activity feeds.</summary>
public enum BrokerageActivityCategory
{
    Other = 0,
    Trade = 1,
    Cash = 2,
    Fee = 3,
    Interest = 4,
    Dividend = 5,
    Transfer = 6,
    Journal = 7,
    CorporateAction = 8,
    OptionLifecycle = 9,
    Borrow = 10,
    Tax = 11
}

/// <summary>Canonical subtype for provider financial-state activity.</summary>
public enum BrokerageActivitySubtype
{
    Other = 0,
    TradeFill = 1,
    TradeCorrection = 2,
    TradeBust = 3,
    CashDeposit = 4,
    CashWithdrawal = 5,
    Fee = 6,
    CryptoFee = 7,
    PassThroughCharge = 8,
    PassThroughRebate = 9,
    CreditInterest = 10,
    MarginInterest = 11,
    BorrowFee = 12,
    BorrowRebate = 13,
    CashDividend = 14,
    StockDividend = 15,
    CapitalGainDistribution = 16,
    ReturnOfCapital = 17,
    DividendWithholding = 18,
    CashTransfer = 19,
    SecurityTransfer = 20,
    AcatsCash = 21,
    AcatsSecurity = 22,
    CashJournal = 23,
    SecurityJournal = 24,
    OptionAssignment = 25,
    OptionExercise = 26,
    OptionExpiration = 27,
    StockSplit = 28,
    Merger = 29,
    Spinoff = 30,
    SymbolChange = 31,
    Reorganization = 32,
    TaxWithholding = 33
}

/// <summary>One canonical provider activity with retained vendor code and correction lineage.</summary>
public sealed record BrokerageActivityEventDto(
    string EventId,
    string ProviderCode,
    BrokerageActivityCategory Category,
    BrokerageActivitySubtype Subtype,
    DateTimeOffset EffectiveAt,
    string Currency,
    decimal NetAmount,
    string? Symbol = null,
    decimal? Quantity = null,
    decimal? Price = null,
    string? OrderId = null,
    string? RelatedEventId = null,
    string? Description = null,
    BrokerageOptionLifecycleSnapshotDto? Option = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>Completeness evidence for one provider activity retrieval.</summary>
public sealed record BrokerageActivityCursorDto(
    string? LastEventId,
    DateTimeOffset? HighWatermark,
    int PageCount,
    int SourceRecordCount,
    bool IsComplete);

/// <summary>Typed option exercise, assignment, expiration, or trade evidence.</summary>
public sealed record BrokerageOptionLifecycleSnapshotDto(
    string ContractId,
    string? UnderlyingSymbol,
    string? OptionType,
    decimal? StrikePrice,
    DateOnly? ExpirationDate,
    decimal? ContractMultiplier,
    string? LifecycleAction,
    string? StrategyId = null,
    string? LegId = null);

public enum BrokerageBorrowStatus
{
    Unknown = 0,
    EasyToBorrow = 1,
    HardToBorrow = 2,
    LocateRequired = 3,
    Located = 4,
    Recalled = 5,
    Unavailable = 6
}

/// <summary>Provider-reported stock-borrow or securities-lending state.</summary>
public sealed record BrokerageBorrowPositionSnapshotDto(
    string Symbol,
    decimal Quantity,
    BrokerageBorrowStatus Status,
    string Currency,
    decimal? AvailableQuantity = null,
    decimal? BorrowRate = null,
    decimal? DailyCost = null,
    decimal? Collateral = null,
    DateOnly? RecallDate = null,
    string SourceAuthority = "ProviderReported",
    string? AccountId = null);

/// <summary>Provider-reported or explicitly calculated tax-lot evidence.</summary>
public sealed record BrokerageTaxLotSnapshotDto(
    string LotId,
    string Symbol,
    DateOnly AcquiredDate,
    decimal Quantity,
    decimal CostBasis,
    string Currency,
    decimal? UnitCost = null,
    decimal? MarketValue = null,
    decimal? UnrealizedPnl = null,
    string SourceAuthority = "ProviderReported",
    string? AccountId = null);

public sealed record BrokerageOrderSnapshotDto(
    string OrderId,
    string? ClientOrderId,
    string Symbol,
    OrderSide Side,
    OrderType Type,
    OrderStatus Status,
    decimal Quantity,
    decimal FilledQuantity,
    decimal? LimitPrice,
    decimal? StopPrice,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt = null);

public sealed record BrokerageFillSnapshotDto(
    string FillId,
    string? OrderId,
    string Symbol,
    OrderSide Side,
    decimal Quantity,
    decimal Price,
    DateTimeOffset FilledAt,
    string? Venue = null,
    decimal? Commission = null,
    decimal? RealizedPnl = null);

public sealed record BrokerageCashTransactionDto(
    string TransactionId,
    string TransactionType,
    decimal Amount,
    string Currency,
    DateTimeOffset PostedAt,
    string? Symbol = null,
    string? Description = null);

public sealed record BrokerageCorporateActionSnapshotDto(
    string EventId,
    string EventType,
    string? Symbol,
    DateOnly? EffectiveDate,
    DateOnly? ExDate,
    decimal? Amount = null,
    decimal? Quantity = null,
    decimal? Factor = null,
    string? Currency = null,
    string? Description = null);
