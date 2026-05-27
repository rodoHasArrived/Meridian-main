using System.Text.Json.Serialization;

namespace Meridian.Contracts.Futures;

[JsonConverter(typeof(JsonStringEnumConverter<FutureLifecycleStat>))]
public enum FutureLifecycleStat
{
    Active,
    RollTarget,
    Expired,
    Retired
}

public sealed record FutureReferenceDto(
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
    FutureLifecycleStat LifecycleStat,
    string PrimaryIdentifier,
    long Version);
