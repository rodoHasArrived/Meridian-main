namespace Meridian.Contracts.Workstation;

/// <summary>Retained components, without an invented net-equity total or cross-currency aggregation.</summary>
public sealed record AccountBalanceHistoryPointDto(
    Guid SnapshotId, Guid AccountId, DateOnly AsOfDate, DateTimeOffset RecordedAtUtc,
    string Currency, string Source, string CashBalance, string? SecuritiesMarketValue);

public sealed record AccountBalanceHistoryDto(
    Guid AccountId, DateTimeOffset AsOfUtc, IReadOnlyList<AccountBalanceHistoryPointDto> Points, bool IsTruncated);
