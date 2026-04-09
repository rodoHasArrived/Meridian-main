using Meridian.Contracts.Configuration;
using Meridian.ProviderSdk;

namespace Meridian.Application.Config;

/// <summary>
/// Maps shared configuration DTOs to core configuration records.
/// </summary>
public static class ConfigDtoMapper
{
    public static AlpacaOptions? ToDomain(this AlpacaOptionsDto? dto)
        => dto is null
            ? null
            : new AlpacaOptions(
                KeyId: dto.KeyId ?? string.Empty,
                SecretKey: dto.SecretKey ?? string.Empty,
                Feed: dto.Feed,
                UseSandbox: dto.UseSandbox,
                SubscribeQuotes: dto.SubscribeQuotes);

    public static PolygonOptions? ToDomain(this PolygonOptionsDto? dto)
        => dto is null
            ? null
            : new PolygonOptions(
                ApiKey: dto.ApiKey,
                UseDelayed: dto.UseDelayed,
                Feed: dto.Feed,
                SubscribeTrades: dto.SubscribeTrades,
                SubscribeQuotes: dto.SubscribeQuotes,
                SubscribeAggregates: dto.SubscribeAggregates);

    public static IBOptions? ToDomain(this IBOptionsDto? dto)
        => dto is null
            ? null
            : new IBOptions(
                Host: dto.Host,
                Port: dto.Port,
                ClientId: dto.ClientId,
                UsePaperTrading: dto.UsePaperTrading,
                SubscribeDepth: dto.SubscribeDepth,
                DepthLevels: dto.DepthLevels,
                TickByTick: dto.TickByTick);

    public static DerivativesConfig? ToDomain(this DerivativesConfigDto? dto)
        => dto is null
            ? null
            : new DerivativesConfig(
                Enabled: dto.Enabled,
                Underlyings: dto.Underlyings,
                MaxDaysToExpiration: dto.MaxDaysToExpiration,
                StrikeRange: dto.StrikeRange,
                CaptureGreeks: dto.CaptureGreeks,
                CaptureChainSnapshots: dto.CaptureChainSnapshots,
                ChainSnapshotIntervalSeconds: dto.ChainSnapshotIntervalSeconds,
                CaptureOpenInterest: dto.CaptureOpenInterest,
                ExpirationFilter: dto.ExpirationFilter,
                IndexOptions: dto.IndexOptions?.ToDomain());

    public static IndexOptionsConfig? ToDomain(this IndexOptionsConfigDto? dto)
        => dto is null
            ? null
            : new IndexOptionsConfig(
                Enabled: dto.Enabled,
                Indices: dto.Indices,
                IncludeWeeklies: dto.IncludeWeeklies,
                IncludeAmSettled: dto.IncludeAmSettled,
                IncludePmSettled: dto.IncludePmSettled);

    public static ProviderConnectionsConfig? ToDomain(this ProviderConnectionsConfigDto? dto)
        => dto is null
            ? null
            : new ProviderConnectionsConfig(
                Connections: dto.Connections?.Select(ToDomain).ToArray(),
                Bindings: dto.Bindings?.Select(ToDomain).ToArray(),
                Policies: dto.Policies?.Select(ToDomain).ToArray(),
                Presets: dto.Presets?.Select(ToDomain).ToArray(),
                Certifications: dto.Certifications?.Select(ToDomain).ToArray());

    public static ProviderConnectionConfig ToDomain(this ProviderConnectionConfigDto dto)
        => new(
            ConnectionId: dto.ConnectionId,
            ProviderFamilyId: dto.ProviderFamilyId,
            DisplayName: dto.DisplayName,
            ConnectionType: ParseEnum(dto.ConnectionType, ProviderConnectionType.DataVendor),
            ConnectionMode: ParseEnum(dto.ConnectionMode, ProviderConnectionMode.ReadOnly),
            Enabled: dto.Enabled,
            CredentialReference: dto.CredentialReference,
            InstitutionId: dto.InstitutionId,
            ExternalAccountId: dto.ExternalAccountId,
            Scope: dto.Scope?.ToConnectionScope(),
            Tags: dto.Tags,
            Description: dto.Description,
            ProductionReady: dto.ProductionReady);

    public static ProviderBindingConfig ToDomain(this ProviderBindingConfigDto dto)
        => new(
            BindingId: dto.BindingId,
            Capability: ParseEnum(dto.Capability, ProviderCapabilityKind.RealtimeMarketData),
            ConnectionId: dto.ConnectionId,
            Target: dto.Target?.ToBindingTarget(),
            Priority: dto.Priority,
            Enabled: dto.Enabled,
            FailoverConnectionIds: dto.FailoverConnectionIds,
            SafetyModeOverride: string.IsNullOrWhiteSpace(dto.SafetyModeOverride)
                ? null
                : ParseEnum(dto.SafetyModeOverride, ProviderSafetyMode.HealthAwareFailover),
            Notes: dto.Notes);

    public static ProviderPolicyConfig ToDomain(this ProviderPolicyConfigDto dto)
        => new(
            PolicyId: dto.PolicyId,
            Capability: ParseEnum(dto.Capability, ProviderCapabilityKind.RealtimeMarketData),
            SafetyMode: ParseEnum(dto.SafetyMode, ProviderSafetyMode.HealthAwareFailover),
            RequireExplicitBinding: dto.RequireExplicitBinding,
            RequireProductionReady: dto.RequireProductionReady,
            AllowedFailoverConnectionIds: dto.AllowedFailoverConnectionIds,
            AllowedProviderFamilies: dto.AllowedProviderFamilies,
            Description: dto.Description);

    public static ProviderPresetConfig ToDomain(this ProviderPresetConfigDto dto)
        => new(
            PresetId: dto.PresetId,
            Name: dto.Name,
            Description: dto.Description,
            Policies: dto.Policies?.Select(ToDomain).ToArray(),
            Highlights: dto.Highlights,
            IsBuiltIn: dto.IsBuiltIn,
            IsEnabled: dto.IsEnabled);

    public static ProviderCertificationConfig ToDomain(this ProviderCertificationConfigDto dto)
        => new(
            ConnectionId: dto.ConnectionId,
            Status: dto.Status,
            LastRunAt: dto.LastRunAt,
            ExpiresAt: dto.ExpiresAt,
            ProductionReady: dto.ProductionReady,
            Checks: dto.Checks,
            Notes: dto.Notes);

    public static ProviderConnectionScope ToConnectionScope(this ProviderScopeDto dto)
        => new(
            Workspace: dto.Workspace,
            FundProfileId: dto.FundProfileId,
            EntityId: dto.EntityId,
            SleeveId: dto.SleeveId,
            VehicleId: dto.VehicleId,
            AccountId: dto.AccountId);

    public static ProviderBindingTarget ToBindingTarget(this ProviderScopeDto dto)
        => new(
            Workspace: dto.Workspace,
            FundProfileId: dto.FundProfileId,
            EntityId: dto.EntityId,
            SleeveId: dto.SleeveId,
            VehicleId: dto.VehicleId,
            AccountId: dto.AccountId);

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
}
