using Meridian.Core.Diagnostics;

namespace Meridian.Platform.Diagnostics;

/// <summary>
/// Tracks the most recent graceful shutdown sequence for support diagnostics.
/// </summary>
public sealed class ShutdownDiagnosticsService
{
    private readonly object _gate = new();
    private ShutdownSequenceDiagnosticSnapshot _snapshot = ShutdownSequenceDiagnosticSnapshot.Unavailable;

    public ShutdownSequenceDiagnosticSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            return _snapshot;
        }
    }

    public void RecordStarted(
        string correlationId,
        ShutdownReason reason,
        DateTimeOffset startedAtUtc,
        int flushableCount,
        int disposableCount,
        int callbackCount)
    {
        var snapshot = new ShutdownSequenceDiagnosticSnapshot(
            Available: true,
            OperationName: "runtime.shutdown.sequence",
            Status: "Started",
            CorrelationId: RuntimeDiagnosticRedactor.SanitizeText(correlationId),
            Reason: reason.ToString(),
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: null,
            DurationMs: null,
            FlushTimeoutOccurred: false,
            IncompleteFlushCount: 0,
            WarningCount: 0,
            WarningSummary: [],
            FlushableComponentCount: flushableCount,
            DisposableComponentCount: disposableCount,
            CallbackCount: callbackCount,
            DuplicateRequestCount: 0,
            LastDuplicateRequestAtUtc: null,
            LastUpdatedAtUtc: DateTimeOffset.UtcNow);

        lock (_gate)
        {
            _snapshot = snapshot;
        }
    }

    public void RecordDuplicateRequest(ShutdownReason requestedReason, ShutdownReason activeReason)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            _snapshot = _snapshot.Available
                ? _snapshot with
                {
                    Status = "DuplicateRequestIgnored",
                    Reason = activeReason.ToString(),
                    DuplicateRequestCount = _snapshot.DuplicateRequestCount + 1,
                    LastDuplicateRequestAtUtc = now,
                    LastUpdatedAtUtc = now
                }
                : new ShutdownSequenceDiagnosticSnapshot(
                    Available: true,
                    OperationName: "runtime.shutdown.sequence",
                    Status: "DuplicateRequestIgnored",
                    CorrelationId: null,
                    Reason: activeReason.ToString(),
                    StartedAtUtc: null,
                    CompletedAtUtc: null,
                    DurationMs: null,
                    FlushTimeoutOccurred: false,
                    IncompleteFlushCount: 0,
                    WarningCount: 1,
                    WarningSummary: [$"Duplicate shutdown request ignored; requestedReason={requestedReason}"],
                    FlushableComponentCount: 0,
                    DisposableComponentCount: 0,
                    CallbackCount: 0,
                    DuplicateRequestCount: 1,
                    LastDuplicateRequestAtUtc: now,
                    LastUpdatedAtUtc: now);
        }
    }

    public void RecordCompleted(ShutdownResult result)
    {
        RecordTerminal("Completed", result, CountIncompleteFlushWarnings(result.Warnings));
    }

    public void RecordTimedOut(ShutdownResult result)
    {
        RecordTerminal("TimedOut", result, Math.Max(1, CountIncompleteFlushWarnings(result.Warnings)));
    }

    public void RecordFailed(ShutdownResult result)
    {
        RecordTerminal("Failed", result, Math.Max(1, CountIncompleteFlushWarnings(result.Warnings)));
    }

    private void RecordTerminal(string status, ShutdownResult result, int incompleteFlushCount)
    {
        var warnings = SanitizeWarnings(result.Warnings);
        lock (_gate)
        {
            _snapshot = new ShutdownSequenceDiagnosticSnapshot(
                Available: true,
                OperationName: "runtime.shutdown.sequence",
                Status: status,
                CorrelationId: RuntimeDiagnosticRedactor.SanitizeText(result.CorrelationId),
                Reason: result.Reason.ToString(),
                StartedAtUtc: NormalizeUtc(result.StartedAt),
                CompletedAtUtc: NormalizeUtc(result.CompletedAt),
                DurationMs: result.DurationMs,
                FlushTimeoutOccurred: result.FlushTimeoutOccurred,
                IncompleteFlushCount: incompleteFlushCount,
                WarningCount: result.Warnings?.Length ?? 0,
                WarningSummary: warnings,
                FlushableComponentCount: _snapshot.FlushableComponentCount,
                DisposableComponentCount: _snapshot.DisposableComponentCount,
                CallbackCount: _snapshot.CallbackCount,
                DuplicateRequestCount: _snapshot.DuplicateRequestCount,
                LastDuplicateRequestAtUtc: _snapshot.LastDuplicateRequestAtUtc,
                LastUpdatedAtUtc: DateTimeOffset.UtcNow);
        }
    }

    private static DateTimeOffset? NormalizeUtc(DateTimeOffset value) =>
        value == default ? null : value.ToUniversalTime();

    private static int CountIncompleteFlushWarnings(string[]? warnings) =>
        warnings?.Count(static warning =>
            warning.StartsWith("Flush timeout", StringComparison.OrdinalIgnoreCase) ||
            warning.StartsWith("Flush cancelled", StringComparison.OrdinalIgnoreCase) ||
            warning.StartsWith("Flush error", StringComparison.OrdinalIgnoreCase)) ?? 0;

    private static IReadOnlyList<string> SanitizeWarnings(string[]? warnings) =>
        warnings is null
            ? []
            : warnings
                .Select(RuntimeDiagnosticRedactor.SanitizeText)
                .Where(static warning => !string.IsNullOrWhiteSpace(warning))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToArray();
}

public sealed record ShutdownSequenceDiagnosticSnapshot(
    bool Available,
    string OperationName,
    string Status,
    string? CorrelationId,
    string? Reason,
    DateTimeOffset? StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    double? DurationMs,
    bool FlushTimeoutOccurred,
    int IncompleteFlushCount,
    int WarningCount,
    IReadOnlyList<string> WarningSummary,
    int FlushableComponentCount,
    int DisposableComponentCount,
    int CallbackCount,
    int DuplicateRequestCount,
    DateTimeOffset? LastDuplicateRequestAtUtc,
    DateTimeOffset LastUpdatedAtUtc)
{
    public static ShutdownSequenceDiagnosticSnapshot Unavailable { get; } = new(
        Available: false,
        OperationName: "runtime.shutdown.sequence",
        Status: "Unavailable",
        CorrelationId: null,
        Reason: null,
        StartedAtUtc: null,
        CompletedAtUtc: null,
        DurationMs: null,
        FlushTimeoutOccurred: false,
        IncompleteFlushCount: 0,
        WarningCount: 0,
        WarningSummary: [],
        FlushableComponentCount: 0,
        DisposableComponentCount: 0,
        CallbackCount: 0,
        DuplicateRequestCount: 0,
        LastDuplicateRequestAtUtc: null,
        LastUpdatedAtUtc: DateTimeOffset.UnixEpoch);
}
