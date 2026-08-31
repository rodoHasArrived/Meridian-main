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
    Missing,
    Blocked
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
    public const string Blocked = "Blocked";
    public const string Unknown = "Unknown";
}

public sealed record DataConfidenceIndicatorModel(
    DataConfidenceLevel ConfidenceLevel,
    DateTimeOffset? FreshnessTimestamp,
    string SourceName,
    DataConfidenceReconciliationStatus ReconciliationStatus,
    string? Notes,
    string? ExplanationRoute = null,
    string? ProviderStatus = null)
{
    /// <summary>
    /// Computed from the current field values rather than captured at construction: a
    /// record <c>with</c> update must never leave the tooltip and accessible text
    /// describing the fields the update replaced.
    /// </summary>
    public string Explanation
        => BuildExplanation(ConfidenceLevel, ReconciliationStatus, SourceName, FreshnessTimestamp, Notes);

    public static DataConfidenceIndicatorModel Unknown(string sourceName = "Not reported", string? notes = null)
        => new(
            DataConfidenceLevel.Unknown,
            FreshnessTimestamp: null,
            Normalize(sourceName, "Not reported"),
            DataConfidenceReconciliationStatus.Unknown,
            notes);

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
            // Blocked evidence is a hard failure (rejected approval, failed delivery) that the
            // workstation presents as a danger state, distinct from routine review.
            EvidenceStatusDto.Blocked => DataConfidenceLevel.Blocked,
            EvidenceStatusDto.ReviewRequired => DataConfidenceLevel.Partial,
            _ when freshness.IsStale => DataConfidenceLevel.Stale,
            // Current is asserted only when the evidence carries an as-of instant; the DTO
            // permits a missing timestamp, and "Current · As of unavailable" contradicts itself.
            EvidenceStatusDto.Ready when freshness.AsOf is not null => DataConfidenceLevel.Current,
            _ => DataConfidenceLevel.Unknown
        };

        // EvidenceStatusDto describes evidence readiness, not a reconciliation result, so no
        // reconciliation posture is derived from it: callers that have one supply it explicitly.
        // The visible label and the explanation share one normalized source so the tooltip
        // cannot say "source not reported" while the badge shows the fallback name.
        var source = Normalize(sourceSystem, "Evidence");
        var evidenceNotes = Normalize(notes, freshness.Reason);
        return new DataConfidenceIndicatorModel(
            confidence,
            freshness.AsOf,
            source,
            reconciliationStatus,
            evidenceNotes,
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
        // A reconnect in progress degrades the badge even while the socket still reads
        // connected: the provider-health endpoint classifies the same flag as yellow.
        var degraded = !provider.IsConnected
            || !provider.IsEnabled
            || ContainsAny(status, "degraded", "unhealthy", "error", "failed", "blocked", "disconnected", "disconnecting")
            || provider.IsReconnecting is true
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
        // The route deliberately emits IsConnected = null with ConnectionState "unknown"
        // when it has neither runtime diagnostics nor stored metrics — health that is
        // merely unavailable must not read as a degradation verdict.
        // The contract reports Recovering separately from Failed: a stream mid-recovery is
        // not yet delivering trustworthy data, so it degrades the badge the same way. A
        // reconnect in progress likewise degrades even while the socket still reads
        // connected (the provider-health endpoint classifies the same flag as yellow), and
        // a stream the contract itself marks degraded is a partial degradation.
        var degraded = provider.IsConnected is false
            || !provider.IsEnabled
            || ContainsAny(status, "degraded", "unhealthy", "error", "failed", "blocked", "disconnected", "disconnecting")
            || provider.IsReconnecting is true
            || !string.IsNullOrWhiteSpace(provider.LastFailureKind)
            || provider.FailedSubscriptions is > 0
            || provider.RecoveringSubscriptions is > 0
            || HasDegradedStream(provider.Streams);
        // Only genuine received-at fields feed the freshness instant: the route populates
        // LastHeartbeat from a stored metrics-snapshot timestamp when it has no live
        // diagnostics, and a snapshot time is not evidence that data arrived. Subscription
        // traffic outranks the wire timestamps when the provider tracks it — the transport
        // advances LastMessageReceivedAt for control frames too, so fresh control traffic
        // must not mask stale subscribed data.
        var asOf = provider.LastSubscriptionMessageAt
            ?? provider.LastMessageReceivedAt
            ?? provider.LastHeartbeatReceivedAt;

        return FromProviderCore(
            Normalize(provider.Name, "Provider"),
            status,
            degraded,
            asOf,
            // Degraded-stream reasons outrank the generic lifecycle fallbacks: when a stream
            // is the sole cause of the warning, its actionable reason must reach the badge.
            Normalize(notes, provider.LastFailureKind, DegradedStreamReasons(provider.Streams), provider.LifecycleState, provider.WebSocketState),
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
        DataConfidenceLevel.Blocked => DataConfidenceLabels.Blocked,
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
        // Missing and blocked values are the strongest signals; an unreconciled posture
        // outranks any reassuring confidence level so a reconciliation exception is never
        // visually suppressed by a "Current" badge; estimated reconciliation reads as
        // informational.
        { ConfidenceLevel: DataConfidenceLevel.Missing or DataConfidenceLevel.Blocked } => WorkspaceTone.Danger,
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
        DataConfidenceLevel.Blocked => "\uE733",
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
        DataConfidenceLevel.Blocked => DataConfidenceLabels.Blocked,
        _ => DataConfidenceLabels.Unknown
    };

    private static string LabelFor(DataConfidenceReconciliationStatus status) => status switch
    {
        DataConfidenceReconciliationStatus.Reconciled => DataConfidenceLabels.Reconciled,
        DataConfidenceReconciliationStatus.Unreconciled => DataConfidenceLabels.Unreconciled,
        DataConfidenceReconciliationStatus.Estimated => DataConfidenceLabels.Estimated,
        _ => DataConfidenceLabels.Unknown
    };

    private static bool HasDegradedStream(
        IReadOnlyList<Meridian.Contracts.Api.ProviderStreamStatusResponse>? streams)
        => streams is not null && streams.Any(static stream => stream.IsDegraded);

    private static string? DegradedStreamReasons(
        IReadOnlyList<Meridian.Contracts.Api.ProviderStreamStatusResponse>? streams)
    {
        if (streams is null)
        {
            return null;
        }

        var reasons = streams
            .Where(static stream => stream.IsDegraded && !string.IsNullOrWhiteSpace(stream.DegradationReason))
            .Select(static stream => stream.DegradationReason!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return reasons.Length == 0 ? null : string.Join("; ", reasons);
    }

    private static string Normalize(params string?[] candidates)
        => candidates.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static bool ContainsAny(string value, params string[] needles)
        => needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
