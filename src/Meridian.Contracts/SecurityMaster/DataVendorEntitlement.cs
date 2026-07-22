using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Meridian.Contracts.SecurityMaster;

[JsonConverter(typeof(JsonStringEnumConverter<DataVendorEntitlementStatus>))]
public enum DataVendorEntitlementStatus
{
    Active,
    ExpiringSoon,
    Expired,
    PendingRenewal
}

[JsonConverter(typeof(JsonStringEnumConverter<DataVendorDataType>))]
public enum DataVendorDataType
{
    Identifiers,
    TermsAndConditions,
    CreditRatings,
    Pricing,
    CashFlows,
    FxRates,
    CreditRisk,
    Classification
}

public sealed record DataVendorEntitlementDto(
    Guid EntitlementId,
    string VendorName,
    DataVendorDataType DataType,
    string? ContractReference,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    decimal? AumThresholdUsd,
    bool RequiresDirectClientContract,
    string? ContactEmail,
    int RenewalReminderDays,
    DataVendorEntitlementStatus Status,
    string CreatedBy,
    DateTimeOffset CreatedAt)
{
    public string? ClientId { get; init; }
    public string? AccountId { get; init; }
    public string? FundProfileId { get; init; }
    public Guid? SecurityId { get; init; }
    public string? SourceCategory { get; init; }
    public string? ExpectedRefreshCadence { get; init; }
    public int? DefaultMaxDaysStale { get; init; }
    public string? OperatorMetadata { get; init; }
}

public sealed record UpsertDataVendorEntitlementRequest(
    string VendorName,
    DataVendorDataType DataType,
    string? ContractReference,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    decimal? AumThresholdUsd,
    bool RequiresDirectClientContract,
    string? ContactEmail,
    int RenewalReminderDays,
    string Actor,
    // Supply the existing id to renew or edit an entitlement in place; omit to create a new one.
    Guid? EntitlementId = null)
{
    public string? ClientId { get; init; }
    public string? AccountId { get; init; }
    public string? FundProfileId { get; init; }
    public Guid? SecurityId { get; init; }
    public string? SourceCategory { get; init; }
    public string? ExpectedRefreshCadence { get; init; }
    public int? DefaultMaxDaysStale { get; init; }
    public string? OperatorMetadata { get; init; }
}
