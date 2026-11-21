using System.Text.Json.Serialization;

namespace Meridian.Contracts.Api;

/// <summary>Secret-free limits selected for one vendor account profile.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProviderQuotaEditDto(string TierId, int UnitsPerMinute, int? DailyLimit, int RequestsPerMinute);

public sealed record ProviderQuotaTierDto(
    string Id,
    string Label,
    int MinuteCeiling,
    int DailyCeiling,
    int? DefaultDailyLimit);

public sealed record ProviderQuotaSettingsDto(
    string? TierId,
    int UnitsPerMinute,
    int? DailyLimit,
    int RequestsPerMinute);

/// <summary>Limits measured on an initialized provider adapter.</summary>
public sealed record ProviderQuotaRuntimeDto(
    int MaximumPerWindow,
    double WindowSeconds,
    int? DailyLimit,
    double MinimumDelaySeconds);

public sealed record ProviderQuotaResponse(
    string ProviderId,
    string DisplayName,
    string Unit,
    string? DailyResetPolicy,
    bool Editable,
    bool CanEdit,
    string Policy,
    ProviderQuotaSettingsDto? Saved,
    ProviderQuotaRuntimeDto? Running,
    bool PendingRestart,
    IReadOnlyList<ProviderQuotaTierDto> Tiers);
