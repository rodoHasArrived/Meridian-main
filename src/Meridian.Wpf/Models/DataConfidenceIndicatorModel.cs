using Meridian.Contracts.Workstation;
using Meridian.Ui.Services;

namespace Meridian.Wpf.Models;

public enum DataConfidenceLevel
{
    Unknown = 0,
    Current,
    Stale,
    Partial,
    Estimated,
    ProviderDegraded,
    Missing
}

public enum DataConfidenceReconciliationStatus
{
    Unknown = 0,
    Reconciled,
    Unreconciled,
    Estimated
}

public static class DataConfidenceLabels
{
    public const string Current = "Current";
    public const string Stale = "Stale";
    public const string Partial = "Partial";
    public const string Reconciled = "Reconciled";
    public const string Unreconciled = "Unreconciled";
    public const string Estimated = "Estimated";
    public const string ProviderDegraded = "Provider Degraded";
    public const string Missing = "Missing";
    public const string Unknown = "Unknown";
}

public sealed record DataConfidenceIndicatorModel(
    DataConfidenceLevel ConfidenceLevel,
    DateTimeOffset? FreshnessTimestamp,
    string SourceName,
    DataConfidenceReconciliationStatus ReconciliationStatus,
    string? Notes,
    string Explanation,
    string? ExplanationRoute = null,
    string? ProviderStatus = null)
{
    public static DataConfidenceIndicatorModel Unknown(string sourceName = "Not reported", string? notes = null)
        => new(
            DataConfidenceLevel.Unknown,
            FreshnessTimestamp: null,
            Normalize(sourceName, "Not reported"),
            DataConfidenceReconciliationStatus.Unknown,
            notes,
            string.IsNullOrWhiteSpace(notes)
                ? "Source confidence has not been reported for this value."
                : $"Source confidence has not been reported for this value. Notes: {notes.Trim()}");

    public static DataConfidenceIndicatorModel FromEvidence(
        EvidenceStatusDto status,
        EvidenceFreshnessDto freshness,
        string? sourceSystem,
        string? notes = null,
        string? explanationRoute = null,
        DataConfidenceReconciliationStatus reconciliationStatus = DataConfidenceReconciliationStatus.Unknown)
    {
        var confidence = status switch
        {
            EvidenceStatusDto.Stale => DataConfidenceLevel.Stale,
            EvidenceStatusDto.Missing => DataConfidenceLevel.Missing,
            EvidenceStatusDto.Blocked => DataConfidenceLevel.Partial,
            EvidenceStatusDto.ReviewRequired => DataConfidenceLevel.Partial,
            _ when freshness.IsStale => DataConfidenceLevel.Stale,
            // Current is asserted only when the evidence carries an as-of instant; the DTO
            // permits a missing timestamp, and "Current · As of unavailable" contradicts itself.
            EvidenceStatusDto.Ready when freshness.AsOf is not null => DataConfidenceLevel.Current,
            _ => DataConfidenceLevel.Unknown
        };

        // EvidenceStatusDto describes evidence readiness, not a reconciliation result, so no
        // reconciliation posture is derived from it: callers that have one supply it explicitly.
        return new DataConfidenceIndicatorModel(
            confidence,
            freshness.AsOf,
            Normalize(sourceSystem, "Evidence"),
            reconciliationStatus,
            Normalize(notes, freshness.Reason),
            BuildExplanation(confidence, reconciliationStatus, sourceSystem, freshness.AsOf, Normalize(notes, freshness.Reason)),
            explanationRoute);
    }

    public static DataConfidenceIndicatorModel FromProviderStatus(
        ProviderStatusInfo provider,
        DataConfidenceReconciliationStatus reconciliationStatus = DataConfidenceReconciliationStatus.Unknown,
        string? notes = null,
        string? explanationRoute = null,
        TimeSpan? freshnessWindow = null)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var status = Normalize(provider.Status, provider.IsConnected ? "Connected" : "Disconnected");
        var degraded = !provider.IsConnected
            || !provider.IsEnabled
            || ContainsAny(status, "degraded", "unhealthy", "error", "failed", "blocked", "disconnected")
            || !string.IsNullOrWhiteSpace(provider.LastError)
            || !string.IsNullOrWhiteSpace(provider.LastFailureKind);
        // Connection time is not a data-delivery signal: a newly connected provider that
        // has never delivered anything must stay Unknown, so only received-at timestamps
        // feed the freshness instant.
        var asOf = provider.LastMessageReceivedAt ?? provider.LastHeartbeatReceivedAt;

        return FromProviderCore(
            Normalize(provider.DisplayName, provider.Name, "Provider"),
            status,
            degraded,
            asOf,
            Normalize(notes, provider.LastError, provider.LastFailureKind, provider.LifecycleState, provider.WebSocketState),
            reconciliationStatus,
            explanationRoute,
            freshnessWindow);
    }

    /// <summary>
    /// Builds the indicator from the shared <c>/api/providers/status</c> contract, which is
    /// what the live route actually returns; <c>ConnectionState</c> is its authoritative
    /// connection posture when present.
    /// </summary>
    public static DataConfidenceIndicatorModel FromProviderStatus(
        Meridian.Contracts.Api.ProviderStatusResponse provider,
        DataConfidenceReconciliationStatus reconciliationStatus = DataConfidenceReconciliationStatus.Unknown,
        string? notes = null,
        string? explanationRoute = null,
        TimeSpan? freshnessWindow = null)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var status = Normalize(
            provider.ConnectionState,
            provider.IsConnected switch { true => "Connected", false => "Disconnected", null => null });
        var degraded = provider.IsConnected is not true
            || !provider.IsEnabled
            || ContainsAny(status, "degraded", "unhealthy", "error", "failed", "blocked", "disconnected")
            || !string.IsNullOrWhiteSpace(provider.LastFailureKind)
            || provider.FailedSubscriptions is > 0;
        var asOf = provider.LastMessageReceivedAt
            ?? provider.LastHeartbeatReceivedAt
            ?? provider.LastHeartbeat;

        return FromProviderCore(
            Normalize(provider.Name, "Provider"),
            status,
            degraded,
            asOf,
            Normalize(notes, provider.LastFailureKind, provider.LifecycleState, provider.WebSocketState),
            reconciliationStatus,
            explanationRoute,
            freshnessWindow);
    }

    private static DataConfidenceIndicatorModel FromProviderCore(
        string sourceName,
        string status,
        bool degraded,
        DateTimeOffset? asOf,
        string providerNotes,
        DataConfidenceReconciliationStatus reconciliationStatus,
        string? explanationRoute,
        TimeSpan? freshnessWindow)
    {
        // A connected provider is not proof of current data: without any received-at
        // timestamp freshness cannot be evaluated, and with a caller-supplied window an
        // old timestamp is stale even while the socket stays up.
        var confidence = degraded ? DataConfidenceLevel.ProviderDegraded
            : asOf is null ? DataConfidenceLevel.Unknown
            : freshnessWindow is { } window && DateTimeOffset.UtcNow - asOf.Value > window ? DataConfidenceLevel.Stale
            : DataConfidenceLevel.Current;

        return new DataConfidenceIndicatorModel(
            confidence,
            asOf,
            sourceName,
            reconciliationStatus,
            providerNotes,
            BuildExplanation(confidence, reconciliationStatus, sourceName, asOf, providerNotes),
            explanationRoute,
            status);
    }

    public string ConfidenceLabel => ConfidenceLevel switch
    {
        DataConfidenceLevel.Current => DataConfidenceLabels.Current,
        DataConfidenceLevel.Stale => DataConfidenceLabels.Stale,
        DataConfidenceLevel.Partial => DataConfidenceLabels.Partial,
        DataConfidenceLevel.Estimated => DataConfidenceLabels.Estimated,
        DataConfidenceLevel.ProviderDegraded => DataConfidenceLabels.ProviderDegraded,
        DataConfidenceLevel.Missing => DataConfidenceLabels.Missing,
        _ => DataConfidenceLabels.Unknown
    };

    public string ReconciliationLabel => ReconciliationStatus switch
    {
        DataConfidenceReconciliationStatus.Reconciled => DataConfidenceLabels.Reconciled,
        DataConfidenceReconciliationStatus.Unreconciled => DataConfidenceLabels.Unreconciled,
        DataConfidenceReconciliationStatus.Estimated => DataConfidenceLabels.Estimated,
        _ => DataConfidenceLabels.Unknown
    };

    public string FreshnessLabel => FormatUtc(FreshnessTimestamp) ?? "As of unavailable";

    public string ProviderLabel => string.IsNullOrWhiteSpace(ProviderStatus)
        ? SourceName
        : $"{SourceName} · {ProviderStatus}";

    public string SummaryLabel => $"{ConfidenceLabel} · {ReconciliationLabel}";

    public string Tone => this switch
    {
        // A missing value is the strongest signal; an unreconciled posture outranks any
        // reassuring confidence level so a reconciliation exception is never visually
        // suppressed by a "Current" badge; estimated reconciliation reads as informational.
        { ConfidenceLevel: DataConfidenceLevel.Missing } => WorkspaceTone.Danger,
        { ReconciliationStatus: DataConfidenceReconciliationStatus.Unreconciled } => WorkspaceTone.Warning,
        { ConfidenceLevel: DataConfidenceLevel.ProviderDegraded or DataConfidenceLevel.Stale or DataConfidenceLevel.Partial } => WorkspaceTone.Warning,
        { ConfidenceLevel: DataConfidenceLevel.Estimated } => WorkspaceTone.Info,
        { ReconciliationStatus: DataConfidenceReconciliationStatus.Estimated } => WorkspaceTone.Info,
        { ConfidenceLevel: DataConfidenceLevel.Current, ReconciliationStatus: DataConfidenceReconciliationStatus.Reconciled } => WorkspaceTone.Success,
        _ => WorkspaceTone.Neutral
    };

    public string IconGlyph => ConfidenceLevel switch
    {
        DataConfidenceLevel.Current => "\uE73E",
        DataConfidenceLevel.ProviderDegraded => "\uE7BA",
        DataConfidenceLevel.Stale => "\uE823",
        DataConfidenceLevel.Partial => "\uE9D5",
        DataConfidenceLevel.Estimated => "\uE9D2",
        DataConfidenceLevel.Missing => "\uE783",
        _ => "\uE946"
    };

    public string AccessibleExplanation
        => $"Data confidence {SummaryLabel}; source {ProviderLabel}; freshness {FreshnessLabel}. {Explanation}";

    private static string BuildExplanation(
        DataConfidenceLevel confidence,
        DataConfidenceReconciliationStatus reconciliation,
        string? sourceName,
        DateTimeOffset? freshnessTimestamp,
        string? notes)
    {
        var source = Normalize(sourceName, "source not reported");
        var freshness = FormatUtc(freshnessTimestamp) ?? "freshness unavailable";
        var noteText = string.IsNullOrWhiteSpace(notes) ? string.Empty : $" Notes: {notes}";
        return $"{LabelFor(confidence)} value from {source}; {LabelFor(reconciliation)}; {freshness}.{noteText}";
    }

    /// <summary>Converts to UTC before formatting: a nonzero offset must not keep its wall-clock value under a UTC label.</summary>
    private static string? FormatUtc(DateTimeOffset? timestamp)
        => timestamp?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture);

    private static string LabelFor(DataConfidenceLevel level) => level switch
    {
        DataConfidenceLevel.Current => DataConfidenceLabels.Current,
        DataConfidenceLevel.Stale => DataConfidenceLabels.Stale,
        DataConfidenceLevel.Partial => DataConfidenceLabels.Partial,
        DataConfidenceLevel.Estimated => DataConfidenceLabels.Estimated,
        DataConfidenceLevel.ProviderDegraded => DataConfidenceLabels.ProviderDegraded,
        DataConfidenceLevel.Missing => DataConfidenceLabels.Missing,
        _ => DataConfidenceLabels.Unknown
    };

    private static string LabelFor(DataConfidenceReconciliationStatus status) => status switch
    {
        DataConfidenceReconciliationStatus.Reconciled => DataConfidenceLabels.Reconciled,
        DataConfidenceReconciliationStatus.Unreconciled => DataConfidenceLabels.Unreconciled,
        DataConfidenceReconciliationStatus.Estimated => DataConfidenceLabels.Estimated,
        _ => DataConfidenceLabels.Unknown
    };

    private static string Normalize(params string?[] candidates)
        => candidates.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
