using System.Text.Json.Serialization;
using Meridian.Contracts.SecurityMaster;

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
    long Version)
{
    /// <summary>
    /// <see cref="SettlementType"/> as a typed convention, or null when the stored text is absent or
    /// names no recognized settlement method. The raw text stays on <see cref="SettlementType"/>.
    /// </summary>
    public SecuritySettlementMethod? SettlementMethod
        => SecurityTermConventions.TryParseSettlementMethod(SettlementType, out var method) ? method : null;
}
