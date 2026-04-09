using Meridian.Contracts.Api;

namespace Meridian.Ui.Services;

/// <summary>
/// Result envelope for provider-operations connection queries.
/// </summary>
public sealed class ProviderConnectionsResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<ProviderConnectionDto> Connections { get; set; } = [];
}

/// <summary>
/// Result envelope for provider-operations binding queries.
/// </summary>
public sealed class ProviderBindingsResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<ProviderBindingDto> Bindings { get; set; } = [];
}

/// <summary>
/// Result envelope for provider-operations policy queries.
/// </summary>
public sealed class ProviderPoliciesResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<ProviderPolicyDto> Policies { get; set; } = [];
}

/// <summary>
/// Result envelope for provider-operations preset queries.
/// </summary>
public sealed class ProviderPresetsResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<ProviderPresetDto> Presets { get; set; } = [];
}

/// <summary>
/// Result envelope for provider certification queries.
/// </summary>
public sealed class ProviderCertificationsResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<ProviderCertificationDto> Certifications { get; set; } = [];
}

/// <summary>
/// Result envelope for provider trust-snapshot queries.
/// </summary>
public sealed class ProviderTrustSnapshotsResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<ProviderTrustSnapshotDto> Snapshots { get; set; } = [];
}

/// <summary>
/// Result envelope for provider route preview queries.
/// </summary>
public sealed class ProviderRoutePreviewQueryResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public RoutePreviewResponse? Preview { get; set; }
}

/// <summary>
/// Result envelope for provider route history queries.
/// </summary>
public sealed class ProviderRouteHistoryResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<RoutePreviewResponse> History { get; set; } = [];
}

/// <summary>
/// Result for create or update provider connection mutations.
/// </summary>
public sealed class ProviderConnectionMutationResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public ProviderConnectionDto? Connection { get; set; }
}

/// <summary>
/// Result for create or update provider binding mutations.
/// </summary>
public sealed class ProviderBindingMutationResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public ProviderBindingDto? Binding { get; set; }
}

/// <summary>
/// Result for preset activation mutations.
/// </summary>
public sealed class ProviderPresetApplyResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public ProviderPresetDto? Preset { get; set; }
}

/// <summary>
/// Result for provider certification mutations.
/// </summary>
public sealed class ProviderCertificationMutationResult
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public ProviderCertificationDto? Certification { get; set; }
}
