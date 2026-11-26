using System.Collections.Immutable;
using Meridian.Contracts.Integrity;

namespace Meridian.Contracts.Operations;

/// <summary>Versioned, allowlisted evidence captured from a single terminal execution.</summary>
public sealed record ProviderProvenanceReceipt(
    int SchemaVersion,
    Guid ReceiptId,
    string Capability,
    string RoutingDecision,
    ImmutableArray<ProviderProvenanceSource> Providers,
    ImmutableArray<string> FallbackChain,
    bool FallbackOccurred,
    ProviderProvenanceValidation ValidationProfile,
    ProviderProvenanceRequest Request,
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,
    string DataSemantics,
    string? ReproductionUnavailableReason = null,
    bool RoutingHistoryComplete = true)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record ProviderProvenanceSource(
    string ProviderId,
    string? ConnectionId,
    string Environment,
    string FeedLabel,
    string EntitlementLabel,
    string DataSemantics,
    int? Priority = null,
    long Requests = 0,
    long ResponsesWithData = 0,
    long Unavailable = 0,
    long EmptyResponses = 0,
    long StaleResponses = 0,
    long StaleResponsesAccepted = 0,
    long ReplayEvents = 0,
    int? StaleToleranceDays = null);

/// <summary>Only typed execution settings belong in a receipt. Free-form configuration stays in its owner.</summary>
public sealed record ProviderProvenanceValidation(
    string Version,
    int? StaleToleranceDays = null,
    int? FutureToleranceDays = null,
    bool? TryOtherProvidersForStaleBars = null,
    bool? SymbolResolution = null,
    bool? CrossValidation = null,
    bool? RateLimitRotation = null,
    double? RateLimitRotationThreshold = null,
    int? RateLimitRetries = null,
    double? RateLimitRetryBudgetSeconds = null,
    double? FailureBackoffSeconds = null,
    bool? PreflightEnabled = null,
    bool? CorporateActionsApplied = null,
    bool? FailOnUnknownSymbols = null,
    int? OptionsStrikeRange = null);

public sealed record ProviderProvenanceRequest(
    DateOnly? From,
    DateOnly? To,
    ImmutableArray<string> Symbols,
    string? Granularity = null,
    int? MaxConcurrentSymbols = null,
    ImmutableArray<ProviderProvenanceSymbolPriority>? SymbolPriorities = null,
    bool? ResumeFromCheckpoint = null,
    decimal? InitialCash = null,
    bool? AdjustForCorporateActions = null,
    bool? FailOnUnknownSymbols = null,
    int WithheldSymbolCount = 0,
    bool UsesDiscoveredUniverse = false,
    bool? SkipExistingData = null,
    bool? FillGapsOnly = null);

public sealed record ProviderProvenanceSymbolPriority(string Symbol, int Priority);

/// <summary>Storage-only ownership and runtime revision checks. This is never part of an exported receipt.</summary>
public sealed record ProviderProvenanceAccess(
    ImmutableArray<string> TenantIds,
    ImmutableDictionary<string, string> RuntimeRevisions)
{
    public static ProviderProvenanceAccess Public { get; } = new([], ImmutableDictionary<string, string>.Empty);

    public bool IsVisibleTo(string? tenantId) => !TenantIds.IsDefault &&
        TenantIds.All(id => string.Equals(id, tenantId, StringComparison.Ordinal));

    public ProviderProvenanceAccess ForRequestTenant(string? tenantId) =>
        string.IsNullOrWhiteSpace(tenantId) || TenantIds.Contains(tenantId, StringComparer.Ordinal)
            ? this : this with { TenantIds = TenantIds.Add(tenantId) };
}

public static class ProviderProvenanceLabels
{
    public const string Unknown = "Unknown";

    // Replay event tags can come from imported files. Only recognized public provider IDs are
    // exported verbatim. A registered adapter with a private ID receives an opaque alias.
    public static string? PublicProviderId(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "ib" or "interactivebrokers" or "ibkr" => "ibkr",
        "alpaca" or "polygon" or "stooq" or "yahoo" or "nasdaq" or "tiingo" or "twelvedata" or
        "alphavantage" or "finnhub" or "fred" or "robinhood" or "nyse" or "synthetic" or "sample" or "seeded"
            => value.Trim().ToLowerInvariant(),
        _ => null
    };

    public static string ProviderId(string value) => PublicProviderId(value) ?? OpaqueId("provider", value);

    // Connection IDs may be operator-supplied strings. Receipt IDs deliberately use an opaque,
    // namespaced alias so a connection named after an external account never exposes that name.
    public static string ConnectionId(string providerId, string tenantId, string connectionId) =>
        OpaqueId("connection", $"{providerId}\n{tenantId}\n{connectionId}");

    public static string? RuntimeRevision(string? generation) => string.IsNullOrWhiteSpace(generation)
        ? null : OpaqueId("runtime", generation);

    public static string Environment(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "live" => "Live",
        "production" => "Production",
        "paper" => "Paper",
        "sandbox" => "Sandbox",
        "demo" => "Demo",
        "test" => "Test",
        _ => Unknown
    };

    public static bool IsSymbol(string? value)
    {
        if (value is not { Length: > 0 and <= 20 } ||
            value.Any(static ch => !char.IsAsciiLetterOrDigit(ch) && ch is not ('.' or '-' or '/' or '^' or '=')))
            return false;

        // Ticker syntax can overlap with brokerage account IDs and API key prefixes.
        if (value.StartsWith("sk-", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("pk-", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("AKIA", StringComparison.OrdinalIgnoreCase) && value.Length == 20)
            return false;

        var consecutiveDigits = 0;
        foreach (var ch in value)
        {
            consecutiveDigits = char.IsAsciiDigit(ch) ? consecutiveDigits + 1 : 0;
            if (consecutiveDigits > 6)
                return false;
        }

        return value.Any(char.IsAsciiLetterOrDigit);
    }

    private static string OpaqueId(string kind, string value) =>
        $"{kind}-{Sha256Digest.ComputeUtf8($"{kind}:v1:{value}")[..24]}";
}
