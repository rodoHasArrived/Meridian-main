namespace Meridian.Storage.SecurityMaster;

/// <summary>
/// Queries futures reference projections by security identity and root symbol.
/// </summary>
public interface IFutureReferenceProjectionStore
{
    Task<FutureProjectionRow?> GetFutureAsync(Guid securityId, CancellationToken ct = default);
    Task<IReadOnlyList<FutureProjectionRow>> GetByRootSymbolAsync(string rootSymbol, CancellationToken ct = default);
}

public sealed record FutureProjectionRow(
    Guid SecurityId,
    string DisplayName,
    string Currency,
    string RootSymbol,
    string ContractMonth,
    DateOnly ExpiryDate,
    decimal Multiplier,
    string? SettlementType,
    bool IsRollTarget,
    int? RollWindowDays,
    DateOnly? LastTradingDate,
    DateOnly? FirstNoticeDate,
    DateOnly? DeliveryMonthDate,
    string? DeliveryLocationCode,
    string LifecycleStat,
    string PrimaryIdentifierValue,
    long Version);
