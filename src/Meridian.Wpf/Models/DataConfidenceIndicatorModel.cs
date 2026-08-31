using Meridian.Contracts.Workstation;
using Meridian.Ui.Services.Services;

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
            "Source confidence has not been reported for this value.");

    public static DataConfidenceIndicatorModel FromEvidence(
        EvidenceStatusDto status,
        EvidenceFreshnessDto freshness,
        string? sourceSystem,
        string? notes = null,
        string? explanationRoute = null)
    {
        var confidence = status switch
        {
            EvidenceStatusDto.Ready when !freshness.IsStale => DataConfidenceLevel.Current,
            EvidenceStatusDto.Stale => DataConfidenceLevel.Stale,
            EvidenceStatusDto.Missing => DataConfidenceLevel.Missing,
            EvidenceStatusDto.Blocked => DataConfidenceLevel.Partial,
            EvidenceStatusDto.ReviewRequired => DataConfidenceLevel.Partial,
            _ when freshness.IsStale => DataConfidenceLevel.Stale,
            _ => DataConfidenceLevel.Unknown
        };

        var reconciliation = status switch
        {
            EvidenceStatusDto.Ready => DataConfidenceReconciliationStatus.Reconciled,
            EvidenceStatusDto.Missing or EvidenceStatusDto.Blocked or EvidenceStatusDto.ReviewRequired or EvidenceStatusDto.Stale => DataConfidenceReconciliationStatus.Unreconciled,
            _ => DataConfidenceReconciliationStatus.Unknown
        };

        return new DataConfidenceIndicatorModel(
            confidence,
            freshness.AsOf,
            Normalize(sourceSystem, "Evidence"),
            reconciliation,
            Normalize(notes, freshness.Reason),
            BuildExplanation(confidence, reconciliation, sourceSystem, freshness.AsOf, Normalize(notes, freshness.Reason)),
            explanationRoute);
    }

    public static DataConfidenceIndicatorModel FromProviderStatus(
        ProviderStatusInfo provider,
        DataConfidenceReconciliationStatus reconciliationStatus = DataConfidenceReconciliationStatus.Unknown,
        string? notes = null,
        string? explanationRoute = null)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var status = Normalize(provider.Status, provider.IsConnected ? "Connected" : "Disconnected");
        var degraded = !provider.IsConnected
            || !provider.IsEnabled
            || ContainsAny(status, "degraded", "unhealthy", "error", "failed", "blocked", "disconnected")
            || !string.IsNullOrWhiteSpace(provider.LastError)
            || !string.IsNullOrWhiteSpace(provider.LastFailureKind);
        var asOf = provider.LastMessageReceivedAt
            ?? provider.LastHeartbeatReceivedAt
            ?? (provider.LastConnectedAt.HasValue ? new DateTimeOffset(DateTime.SpecifyKind(provider.LastConnectedAt.Value, DateTimeKind.Utc)) : null);
        var confidence = degraded ? DataConfidenceLevel.ProviderDegraded : DataConfidenceLevel.Current;
        var providerNotes = Normalize(notes, provider.LastError, provider.LastFailureKind, provider.LifecycleState, provider.WebSocketState);

        return new DataConfidenceIndicatorModel(
            confidence,
            asOf,
            Normalize(provider.DisplayName, provider.Name, "Provider"),
            reconciliationStatus,
            providerNotes,
            BuildExplanation(confidence, reconciliationStatus, provider.DisplayName, asOf, providerNotes),
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

    public string FreshnessLabel => FreshnessTimestamp?.ToString("yyyy-MM-dd HH:mm 'UTC'") ?? "As of unavailable";

    public string ProviderLabel => string.IsNullOrWhiteSpace(ProviderStatus)
        ? SourceName
        : $"{SourceName} · {ProviderStatus}";

    public string SummaryLabel => $"{ConfidenceLabel} · {ReconciliationLabel}";

    public string Tone => ConfidenceLevel switch
    {
        DataConfidenceLevel.Current when ReconciliationStatus is DataConfidenceReconciliationStatus.Reconciled => WorkspaceTone.Success,
        DataConfidenceLevel.ProviderDegraded or DataConfidenceLevel.Stale or DataConfidenceLevel.Partial => WorkspaceTone.Warning,
        DataConfidenceLevel.Missing => WorkspaceTone.Danger,
        DataConfidenceLevel.Estimated => WorkspaceTone.Info,
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
        var freshness = freshnessTimestamp?.ToString("yyyy-MM-dd HH:mm 'UTC'") ?? "freshness unavailable";
        var noteText = string.IsNullOrWhiteSpace(notes) ? string.Empty : $" Notes: {notes}";
        return $"{LabelFor(confidence)} value from {source}; {LabelFor(reconciliation)}; {freshness}.{noteText}";
    }

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
