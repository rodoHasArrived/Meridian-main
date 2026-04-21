using Meridian.Application.Config;
using Meridian.Contracts.Api;
using Meridian.ProviderSdk;

namespace Meridian.Application.ProviderRouting;

internal static class ProviderRoutingMapper
{
    public static ProviderConnectionDto ToDto(ProviderConnectionConfig config)
        => new(
            ConnectionId: config.ConnectionId,
            ProviderFamilyId: config.ProviderFamilyId,
            DisplayName: config.DisplayName,
            ConnectionType: config.ConnectionType.ToString(),
            ConnectionMode: config.ConnectionMode.ToString(),
            Enabled: config.Enabled,
            CredentialReference: config.CredentialReference,
            InstitutionId: config.InstitutionId,
            ExternalAccountId: config.ExternalAccountId,
            Scope: ToDto(config.Scope),
            Tags: config.Tags ?? Array.Empty<string>(),
            Description: config.Description,
            ProductionReady: config.ProductionReady);

    public static ProviderBindingDto ToDto(ProviderBindingConfig config)
        => new(
            BindingId: config.BindingId,
            Capability: config.Capability.ToString(),
            ConnectionId: config.ConnectionId,
            Target: ToDto(config.Target),
            Priority: config.Priority,
            Enabled: config.Enabled,
            FailoverConnectionIds: config.FailoverConnectionIds ?? Array.Empty<string>(),
            SafetyModeOverride: config.SafetyModeOverride?.ToString(),
            Notes: config.Notes);

    public static ProviderPolicyDto ToDto(ProviderPolicyConfig config)
        => new(
            PolicyId: config.PolicyId,
            Capability: config.Capability.ToString(),
            SafetyMode: config.SafetyMode.ToString(),
            RequireExplicitBinding: config.RequireExplicitBinding,
            RequireProductionReady: config.RequireProductionReady,
            AllowedFailoverConnectionIds: config.AllowedFailoverConnectionIds ?? Array.Empty<string>(),
            AllowedProviderFamilies: config.AllowedProviderFamilies ?? Array.Empty<string>(),
            Description: config.Description);

    public static ProviderPresetDto ToDto(ProviderPresetConfig config)
        => new(
            PresetId: config.PresetId,
            Name: config.Name,
            Description: config.Description,
            Highlights: config.Highlights ?? Array.Empty<string>(),
            IsBuiltIn: config.IsBuiltIn,
            IsEnabled: config.IsEnabled);

    public static ProviderCertificationDto ToDto(ProviderCertificationConfig config)
        => new(
            ConnectionId: config.ConnectionId,
            Status: config.Status,
            LastRunAt: config.LastRunAt,
            ExpiresAt: config.ExpiresAt,
            ProductionReady: config.ProductionReady,
            Checks: config.Checks ?? Array.Empty<string>(),
            Notes: config.Notes ?? Array.Empty<string>());

    public static ProviderSafetyPolicy ToPolicy(ProviderPolicyConfig config)
        => new(
            Capability: config.Capability,
            Mode: config.SafetyMode,
            RequireExplicitBinding: config.RequireExplicitBinding,
            RequireProductionReady: config.RequireProductionReady,
            AllowedFailoverConnectionIds: config.AllowedFailoverConnectionIds,
            AllowedProviderFamilies: config.AllowedProviderFamilies);

    public static RoutePreviewResponse ToDto(ProviderRouteResult result)
    {
        var selected = result.SelectedDecision;
        return new RoutePreviewResponse(
            Capability: result.Context.Capability.ToString(),
            IsRoutable: result.IsSuccess,
            SelectedConnectionId: selected?.ConnectionId,
            SelectedProviderFamilyId: selected?.ProviderFamilyId,
            SafetyMode: selected?.SafetyMode.ToString() ?? ProviderSafetyMode.HealthAwareFailover.ToString(),
            RequiresManualApproval: result.RequiresManualApproval,
            ReasonCodes: selected?.ReasonCodes.ToArray() ?? Array.Empty<string>(),
            SkippedCandidates: result.SkippedCandidates.ToArray(),
            FallbackConnectionIds: selected?.FallbackConnectionIds.ToArray() ?? Array.Empty<string>(),
            PolicyGate: result.PolicyGate,
            Candidates: result.Candidates.Select(ToDto).ToArray());
    }

    public static RoutePreviewCandidateDto ToDto(ProviderRouteDecision decision)
        => new(
            ConnectionId: decision.ConnectionId,
            ProviderFamilyId: decision.ProviderFamilyId,
            IsHealthy: decision.IsHealthy,
            ScopeRank: decision.ScopeRank,
            Priority: decision.Priority,
            ReasonCodes: decision.ReasonCodes.ToArray(),
            FallbackConnectionIds: decision.FallbackConnectionIds.ToArray(),
            PolicyGate: decision.PolicyGate);

    public static ProviderRouteContext ToRouteContext(RoutePreviewRequest request)
        => new(
            Capability: ParseEnum(request.Capability, ProviderCapabilityKind.RealtimeMarketData),
            Workspace: request.Workspace,
            FundProfileId: request.FundProfileId,
            EntityId: request.EntityId,
            SleeveId: request.SleeveId,
            VehicleId: request.VehicleId,
            AccountId: request.AccountId,
            SecurityId: request.SecurityId,
            Symbol: request.Symbol,
            Market: request.Market,
            AssetClass: request.AssetClass,
            RequireProductionReady: request.RequireProductionReady);

    public static ProviderConnectionScope? ToConnectionScope(ProviderRouteScopeDto? dto)
        => dto is null
            ? null
            : new ProviderConnectionScope(
                Workspace: dto.Workspace,
                FundProfileId: dto.FundProfileId,
                EntityId: dto.EntityId,
                SleeveId: dto.SleeveId,
                VehicleId: dto.VehicleId,
                AccountId: dto.AccountId);

    public static ProviderBindingTarget? ToBindingTarget(ProviderRouteScopeDto? dto)
        => dto is null
            ? null
            : new ProviderBindingTarget(
                Workspace: dto.Workspace,
                FundProfileId: dto.FundProfileId,
                EntityId: dto.EntityId,
                SleeveId: dto.SleeveId,
                VehicleId: dto.VehicleId,
                AccountId: dto.AccountId);

    public static ProviderRouteScopeDto? ToDto(ProviderConnectionScope? scope)
        => scope is null
            ? null
            : new ProviderRouteScopeDto
            {
                Workspace = scope.Workspace,
                FundProfileId = scope.FundProfileId,
                EntityId = scope.EntityId,
                SleeveId = scope.SleeveId,
                VehicleId = scope.VehicleId,
                AccountId = scope.AccountId
            };

    public static ProviderRouteScopeDto? ToDto(ProviderBindingTarget? scope)
        => scope is null
            ? null
            : new ProviderRouteScopeDto
            {
                Workspace = scope.Workspace,
                FundProfileId = scope.FundProfileId,
                EntityId = scope.EntityId,
                SleeveId = scope.SleeveId,
                VehicleId = scope.VehicleId,
                AccountId = scope.AccountId
            };

    public static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
}

internal static class ProviderRoutingConfigExtensions
{
    public static ProviderConnectionsConfig GetSection(AppConfig cfg)
        => cfg.ProviderConnections ?? new ProviderConnectionsConfig();

    public static IReadOnlyList<ProviderConnectionConfig> GetEffectiveConnections(AppConfig cfg)
    {
        var section = GetSection(cfg);
        var connections = (section.Connections ?? Array.Empty<ProviderConnectionConfig>()).ToList();
        foreach (var source in cfg.DataSources?.Sources ?? Array.Empty<DataSourceConfig>())
        {
            if (connections.Any(c => string.Equals(c.ConnectionId, source.Id, StringComparison.OrdinalIgnoreCase)))
                continue;

            connections.Add(new ProviderConnectionConfig(
                ConnectionId: source.Id,
                ProviderFamilyId: source.Provider.ToString().ToLowerInvariant(),
                DisplayName: source.Name,
                ConnectionType: ProviderConnectionType.DataVendor,
                ConnectionMode: ProviderConnectionMode.ReadOnly,
                Enabled: source.Enabled,
                Description: source.Description,
                Tags: source.Tags,
                ProductionReady: true));
        }

        return connections;
    }

    public static IReadOnlyList<ProviderBindingConfig> GetEffectiveBindings(AppConfig cfg)
    {
        var section = GetSection(cfg);
        var bindings = (section.Bindings ?? Array.Empty<ProviderBindingConfig>()).ToList();

        if (!bindings.Any(b => b.Capability == ProviderCapabilityKind.RealtimeMarketData) &&
            !string.IsNullOrWhiteSpace(cfg.DataSources?.DefaultRealTimeSourceId))
        {
            bindings.Add(new ProviderBindingConfig(
                BindingId: "legacy-default-realtime",
                Capability: ProviderCapabilityKind.RealtimeMarketData,
                ConnectionId: cfg.DataSources.DefaultRealTimeSourceId!,
                Target: new ProviderBindingTarget(),
                Priority: 100,
                Enabled: true,
                FailoverConnectionIds: ResolveLegacyFailover(cfg, cfg.DataSources.DefaultRealTimeSourceId!),
                Notes: "Synthesized from legacy default real-time source."));
        }

        if (!bindings.Any(b => b.Capability == ProviderCapabilityKind.HistoricalBars) &&
            !string.IsNullOrWhiteSpace(cfg.DataSources?.DefaultHistoricalSourceId))
        {
            bindings.Add(new ProviderBindingConfig(
                BindingId: "legacy-default-historical",
                Capability: ProviderCapabilityKind.HistoricalBars,
                ConnectionId: cfg.DataSources.DefaultHistoricalSourceId!,
                Target: new ProviderBindingTarget(),
                Priority: 100,
                Enabled: true,
                FailoverConnectionIds: ResolveLegacyFailover(cfg, cfg.DataSources.DefaultHistoricalSourceId!),
                Notes: "Synthesized from legacy default historical source."));
        }

        return bindings;
    }

    public static IReadOnlyList<ProviderPolicyConfig> GetEffectivePolicies(AppConfig cfg)
    {
        var configured = (GetSection(cfg).Policies ?? Array.Empty<ProviderPolicyConfig>()).ToList();
        foreach (var capability in Enum.GetValues<ProviderCapabilityKind>())
        {
            if (configured.Any(p => p.Capability == capability))
                continue;

            var policy = ProviderSafetyPolicy.DefaultFor(capability);
            configured.Add(new ProviderPolicyConfig(
                PolicyId: $"default-{capability}",
                Capability: capability,
                SafetyMode: policy.Mode,
                RequireExplicitBinding: policy.RequireExplicitBinding,
                RequireProductionReady: policy.RequireProductionReady,
                AllowedFailoverConnectionIds: policy.AllowedFailoverConnectionIds?.ToArray(),
                AllowedProviderFamilies: policy.AllowedProviderFamilies?.ToArray(),
                Description: "Built-in default policy."));
        }

        return configured;
    }

    public static IReadOnlyList<ProviderPresetConfig> GetEffectivePresets(AppConfig cfg)
    {
        var configured = (GetSection(cfg).Presets ?? Array.Empty<ProviderPresetConfig>()).ToList();
        foreach (var preset in BuiltInPresets)
        {
            var existing = configured.FirstOrDefault(p => string.Equals(p.PresetId, preset.PresetId, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                configured.Add(preset);
            }
            else if (existing.IsEnabled)
            {
                var index = configured.IndexOf(existing);
                configured[index] = preset with { IsEnabled = true };
            }
        }

        return configured;
    }

    private static string[] ResolveLegacyFailover(AppConfig cfg, string primaryConnectionId)
        => (cfg.DataSources?.FailoverRules ?? Array.Empty<FailoverRuleConfig>())
            .FirstOrDefault(rule => string.Equals(rule.PrimaryProviderId, primaryConnectionId, StringComparison.OrdinalIgnoreCase))
            ?.BackupProviderIds
            ?? Array.Empty<string>();

    private static readonly ProviderPresetConfig[] BuiltInPresets =
    [
        new ProviderPresetConfig(
            PresetId: "research-sandbox",
            Name: "Research Sandbox",
            Description: "Biases routing toward shared data vendors and safe failover for research workflows.",
            Highlights: ["Shared market-data routing", "Health-aware failover", "Low operational risk"],
            IsBuiltIn: true),
        new ProviderPresetConfig(
            PresetId: "us-equity-fund-starter",
            Name: "US Equity Fund Starter",
            Description: "Balanced default policies for a single-fund US equity operating model.",
            Highlights: ["Scoped execution policies", "Reference-data defaults", "Certification-aware routing"],
            IsBuiltIn: true),
        new ProviderPresetConfig(
            PresetId: "multi-broker-fund-ops",
            Name: "Multi-Broker Fund Ops",
            Description: "Strict account-bound routing for funds operating across many brokers and banks.",
            Highlights: ["Account-scoped bindings", "Same-institution failover", "Governance-first posture"],
            IsBuiltIn: true)
    ];
}
