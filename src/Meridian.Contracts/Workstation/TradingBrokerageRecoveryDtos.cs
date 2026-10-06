namespace Meridian.Contracts.Workstation;

/// <summary>Account-scoped broker evidence used by execution risk and recovery.</summary>
public sealed record TradingBrokerageRecoveryDto(
    Guid? FundAccountId,
    string? ProviderId,
    string? ExternalAccountId,
    string Status,
    string Detail,
    TradingBrokerageRecoveryPortfolioDto? Portfolio,
    IReadOnlyList<string> BlockingReasons,
    IReadOnlyList<TradingBrokerageAffectedRunDto> AffectedRuns);

public sealed record TradingBrokerageRecoveryPortfolioDto(
    decimal? Cash,
    decimal? BuyingPower,
    decimal? PortfolioValue,
    string? Currency,
    int PositionCount,
    DateTimeOffset? ObservedAt,
    DateTimeOffset? LastAttemptedAt,
    DateTimeOffset? LastSuccessfulAt,
    bool IsComplete,
    bool IsFresh,
    bool IsConsistent,
    IReadOnlyList<string> Warnings,
    DateTimeOffset? ExpiresAt = null);

public sealed record TradingBrokerageAffectedRunDto(
    string RunId,
    string StrategyId,
    string Status,
    string Detail);

/// <summary>The external account is resolved from the retained account link, never client input.</summary>
public sealed record TradingBrokerageRecoveryRequestDto(Guid FundAccountId);
