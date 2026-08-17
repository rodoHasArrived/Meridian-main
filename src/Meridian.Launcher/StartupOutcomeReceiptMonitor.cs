using System.Security.Cryptography;
using System.Text.Json;
using Meridian.Contracts.Operations;

namespace Meridian.Launcher;

internal sealed record StartupOutcomeReceiptFingerprint(
    long Length,
    long LastWriteTimeUtcTicks,
    string ContentHashSha256,
    string? OperationId,
    string? OperationKind,
    string? CorrelationId,
    DateTimeOffset? StartedAtUtc);

internal static class StartupOutcomeReceiptMonitor
{
    private const string SearchPattern = "startup-terminal-*.verified-outcome.json";

    public static IReadOnlyDictionary<string, StartupOutcomeReceiptFingerprint> Capture(
        string receiptRoot)
    {
        var snapshot = new Dictionary<string, StartupOutcomeReceiptFingerprint>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var path in EnumerateReceiptPaths(receiptRoot))
        {
            if (TryFingerprint(path, out var fingerprint))
                snapshot[path] = fingerprint!;
        }
        return snapshot;
    }

    public static bool TryReadChanged(
        string receiptRoot,
        IReadOnlyDictionary<string, StartupOutcomeReceiptFingerprint> baseline,
        string expectedOperationKind,
        string expectedRequestId,
        DateTimeOffset launchedAtUtc,
        out VerifiedOperationOutcome? outcome,
        out string? receiptPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedOperationKind);
        outcome = null;
        receiptPath = null;
        foreach (var file in EnumerateReceiptPaths(receiptRoot)
                     .Select(path => new FileInfo(path))
                     .OrderByDescending(file => file.LastWriteTimeUtc))
        {
            if (!TryRead(file.FullName, out var current, out var candidate) ||
                candidate is null)
                continue;
            var existed = baseline.TryGetValue(file.FullName, out var prior);
            var changed = existed && prior != current;
            var newlyCreated = !existed && candidate.StartedAtUtc >= launchedAtUtc;
            if (!changed && !newlyCreated)
                continue;
            if (!MatchesExpectedStartupReceipt(
                    file.FullName,
                    candidate,
                    expectedOperationKind,
                    expectedRequestId,
                    launchedAtUtc,
                    existed ? prior : null))
                continue;

            outcome = candidate;
            receiptPath = file.FullName;
            return true;
        }
        return false;
    }

    private static bool MatchesExpectedStartupReceipt(
        string path,
        VerifiedOperationOutcome candidate,
        string expectedOperationKind,
        string expectedRequestId,
        DateTimeOffset launchedAtUtc,
        StartupOutcomeReceiptFingerprint? prior)
    {
        if (!string.Equals(candidate.OperationKind, expectedOperationKind, StringComparison.Ordinal) ||
            !string.Equals(candidate.CorrelationId, expectedRequestId, StringComparison.Ordinal) ||
            !string.Equals(
                candidate.OperationId,
                $"startup:{expectedRequestId}",
                StringComparison.Ordinal) ||
            !Path.GetFileName(path).StartsWith(
                $"startup-terminal-{expectedRequestId}-attempt-",
                StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(path).EndsWith(
                ".verified-outcome.json",
                StringComparison.OrdinalIgnoreCase) ||
            candidate.CompletedAtUtc < launchedAtUtc)
        {
            return false;
        }

        if (prior is null)
            return candidate.StartedAtUtc >= launchedAtUtc;

        return string.Equals(prior.OperationKind, expectedOperationKind, StringComparison.Ordinal) &&
               string.Equals(prior.OperationId, candidate.OperationId, StringComparison.Ordinal) &&
               string.Equals(prior.CorrelationId, candidate.CorrelationId, StringComparison.Ordinal) &&
               prior.StartedAtUtc == candidate.StartedAtUtc;
    }

    public static string PersistLauncherFailure(
        string receiptRoot,
        string requestId,
        DateTimeOffset startedAtUtc,
        OperationTerminalState state,
        string message,
        string supervisorPath,
        string supervisorLogPath,
        bool processStarted,
        string? exceptionType = null)
    {
        if (state is not (OperationTerminalState.Failed or OperationTerminalState.Blocked))
            throw new ArgumentOutOfRangeException(nameof(state), state, null);
        var completedAtUtc = DateTimeOffset.UtcNow;
        var attemptNumber = GetNextLauncherAttemptNumber(receiptRoot, requestId);
        var evidence = new OperationEvidenceReference[]
        {
            new(
                "supervisor-executable",
                "executable",
                "Lifecycle supervisor executable expected by the launcher.",
                new Uri(Path.GetFullPath(supervisorPath)).AbsoluteUri,
                CapturedAtUtc: completedAtUtc),
            new(
                "supervisor-log",
                "log",
                "Lifecycle supervisor diagnostic log.",
                new Uri(Path.GetFullPath(supervisorLogPath)).AbsoluteUri,
                CapturedAtUtc: completedAtUtc),
            new(
                "receipt-root",
                "verified-outcome-directory",
                "Directory observed for request-bound startup receipts.",
                new Uri(Path.GetFullPath(receiptRoot) + Path.DirectorySeparatorChar).AbsoluteUri,
                CapturedAtUtc: completedAtUtc)
        };
        var outcome = new VerifiedOperationOutcome(
            OperationId: $"launcher-startup:{requestId}",
            OperationKind: "lifecycle.launcher.startup",
            State: state,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: completedAtUtc,
            AttemptNumber: attemptNumber,
            CorrelationId: requestId,
            // Deliberately NOT routed through Sha256Digest (which lowercases): receipt hashes
            // cross the launcher/supervisor process boundary and persist across restarts, so a
            // casing change must be coordinated on both sides at once (#2691).
            InputHashSha256: Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                $"{requestId}\n{supervisorPath}"))),
            Postconditions:
            [
                new OperationPostcondition(
                    "supervisor-process-started",
                    "The lifecycle supervisor process was started.",
                    processStarted
                        ? OperationPostconditionState.Satisfied
                        : OperationPostconditionState.NotSatisfied,
                    Required: true,
                    EvidenceIds: ["supervisor-executable"]),
                new OperationPostcondition(
                    "verified-startup-outcome-observed",
                    "A request-bound terminal startup outcome was observed.",
                    OperationPostconditionState.NotSatisfied,
                    Required: true,
                    EvidenceIds: ["receipt-root", "supervisor-log"])
            ],
            Evidence: evidence,
            Artifacts: [],
            Issues:
            [
                new OperationIssue(
                    state == OperationTerminalState.Blocked ? "launcher-startup-blocked" : "launcher-startup-failed",
                    message,
                    OperationIssueSeverity.Error,
                    exceptionType,
                    processStarted ? "supervisor-log" : "supervisor-executable")
                {
                    IsBlocking = state == OperationTerminalState.Blocked
                }
            ],
            Recovery:
            [
                new OperationRecoveryAction(
                    "repair-preflight-retry",
                    "Repair, preflight, and retry",
                    $"Inspect {supervisorLogPath}, repair the reported condition, run Meridian.LifecycleSupervisor preflight, then retry Meridian.",
                    Retryable: true,
                    RequiresHumanAction: true,
                    Route: "command:Meridian.LifecycleSupervisor preflight")
                {
                    EvidenceIds = ["supervisor-executable", "supervisor-log", "receipt-root"]
                }
            ]);
        VerifiedOperationOutcomeValidator.ValidateAndThrow(outcome);
        Directory.CreateDirectory(receiptRoot);
        var path = Path.Combine(
            receiptRoot,
            $"launcher-terminal-{requestId}-attempt-{attemptNumber:D4}.verified-outcome.json");
        WriteAtomic(
            path,
            JsonSerializer.SerializeToUtf8Bytes(
                outcome,
                OperationsContractsJsonContext.Default.VerifiedOperationOutcome));
        return path;
    }

    private static IEnumerable<string> EnumerateReceiptPaths(string receiptRoot)
    {
        try
        {
            return Directory.Exists(receiptRoot)
                ? Directory.EnumerateFiles(receiptRoot, SearchPattern).ToArray()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool TryFingerprint(
        string path,
        out StartupOutcomeReceiptFingerprint? fingerprint)
        => TryRead(path, out fingerprint, out _);

    private static bool TryRead(
        string path,
        out StartupOutcomeReceiptFingerprint? fingerprint,
        out VerifiedOperationOutcome? outcome)
    {
        fingerprint = null;
        outcome = null;
        try
        {
            var bytes = File.ReadAllBytes(path);
            var info = new FileInfo(path);
            outcome = JsonSerializer.Deserialize(
                bytes,
                OperationsContractsJsonContext.Default.VerifiedOperationOutcome);
            if (outcome is null || VerifiedOperationOutcomeValidator.Validate(outcome).Count != 0)
            {
                outcome = null;
                return false;
            }
            fingerprint = new StartupOutcomeReceiptFingerprint(
                bytes.LongLength,
                info.LastWriteTimeUtc.Ticks,
                // Kept uppercase for consistency with the receipt hash family above (#2691).
                Convert.ToHexString(SHA256.HashData(bytes)),
                outcome.OperationId,
                outcome.OperationKind,
                outcome.CorrelationId,
                outcome.StartedAtUtc);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static void WriteAtomic(string path, ReadOnlySpan<byte> bytes)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("A receipt parent directory is required.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static int GetNextLauncherAttemptNumber(string receiptRoot, string requestId)
    {
        if (!Directory.Exists(receiptRoot))
            return 1;
        var maxAttempt = 0;
        foreach (var path in Directory.EnumerateFiles(
                     receiptRoot,
                     $"launcher-terminal-{requestId}-attempt-*.verified-outcome.json"))
        {
            try
            {
                var outcome = JsonSerializer.Deserialize(
                    File.ReadAllBytes(path),
                    OperationsContractsJsonContext.Default.VerifiedOperationOutcome);
                if (outcome is not null &&
                    string.Equals(outcome.OperationId, $"launcher-startup:{requestId}", StringComparison.Ordinal))
                {
                    maxAttempt = Math.Max(maxAttempt, outcome.AttemptNumber);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }
        return checked(maxAttempt + 1);
    }
}
