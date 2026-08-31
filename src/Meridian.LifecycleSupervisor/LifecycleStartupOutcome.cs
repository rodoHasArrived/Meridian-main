using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Lifecycle;
using Meridian.Contracts.Operations;

namespace Meridian.LifecycleSupervisor;

internal static class LifecycleSupervisorExitCode
{
    public const int Succeeded = 0;
    public const int Failed = 1;
    public const int InvalidCommand = 2;
    public const int SupervisorUnavailable = 3;
    public const int Blocked = 4;

    public static int FromOutcome(OperationTerminalState state)
        => state switch
        {
            OperationTerminalState.Succeeded or OperationTerminalState.CompletedWithWarnings => Succeeded,
            OperationTerminalState.Failed => Failed,
            OperationTerminalState.Blocked => Blocked,
            _ => Failed
        };
}

internal sealed record LifecycleStartupOutcomeReceipt(
    VerifiedOperationOutcome Outcome,
    string ReceiptPath);

internal sealed record LifecycleStartupOperationRequest(
    string RequestId,
    int AttemptNumber,
    bool BrowserRequested);

internal sealed class LifecycleOpenOutcomeGate
{
    private readonly object _gate = new();
    private readonly Dictionary<string, TaskCompletionSource<LifecycleStartupOutcomeReceipt>> _pending =
        new(StringComparer.Ordinal);

    public Task<LifecycleStartupOutcomeReceipt> WaitAsync(string requestId, TimeSpan timeout)
    {
        Task<LifecycleStartupOutcomeReceipt> task;
        lock (_gate)
        {
            if (!_pending.TryGetValue(requestId, out var completion))
            {
                completion = new TaskCompletionSource<LifecycleStartupOutcomeReceipt>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _pending.Add(requestId, completion);
            }
            task = completion.Task;
        }
        return task.WaitAsync(timeout);
    }

    public void Complete(string requestId, LifecycleStartupOutcomeReceipt receipt)
    {
        TaskCompletionSource<LifecycleStartupOutcomeReceipt>? pending;
        lock (_gate)
        {
            _pending.Remove(requestId, out pending);
        }
        pending?.TrySetResult(receipt);
    }

    public void Remove(string requestId)
    {
        lock (_gate)
            _pending.Remove(requestId);
    }

    public static bool IsSuccessful(LifecycleStartupOutcomeReceipt receipt)
        => receipt.Outcome.State is
            OperationTerminalState.Succeeded or OperationTerminalState.CompletedWithWarnings;
}

internal static class LifecycleStartupOutcome
{
    public const string OperationKind = "lifecycle.startup";
    public const string RequestIdEnvironmentVariable = "MDC_LIFECYCLE_REQUEST_ID";

    public static LifecycleStartupOperationRequest CreateRequest(
        LifecycleSupervisorConfiguration configuration,
        string? requestId,
        bool browserRequested)
    {
        var normalizedRequestId = NormalizeRequestId(requestId);
        return new LifecycleStartupOperationRequest(
            normalizedRequestId,
            GetNextAttemptNumber(configuration.StartupOutcomeReceiptRoot, normalizedRequestId),
            browserRequested);
    }

    public static string NormalizeRequestId(string? requestId)
        => Guid.TryParse(requestId, out var parsed)
            ? parsed.ToString("N")
            : Guid.NewGuid().ToString("N");

    public static LifecycleStartupOutcomeReceipt Persist(
        LifecycleSupervisorConfiguration configuration,
        LifecycleStartupOperationRequest request,
        string sessionId,
        DateTimeOffset startedAtUtc,
        OperationTerminalState state,
        bool prerequisitesSatisfied,
        bool readinessSatisfied,
        string terminalMessage,
        int? httpPort = null,
        bool browserRequested = false,
        bool browserOpened = false,
        string? browserUri = null,
        string? exceptionType = null,
        bool readinessGateReceipt = false)
    {
        var completedAtUtc = DateTimeOffset.UtcNow;
        var evidence = BuildEvidence(
            configuration,
            sessionId,
            prerequisitesSatisfied,
            terminalMessage,
            httpPort,
            browserRequested,
            browserUri,
            completedAtUtc);
        var postconditions = BuildPostconditions(
            prerequisitesSatisfied,
            readinessSatisfied,
            browserRequested,
            browserOpened);
        var issues = BuildIssues(
            state,
            prerequisitesSatisfied,
            terminalMessage,
            browserRequested,
            exceptionType);
        var recovery = BuildRecovery(
            state,
            browserUri,
            configuration);
        var outcome = new VerifiedOperationOutcome(
            OperationId: $"startup:{request.RequestId}",
            OperationKind: OperationKind,
            State: state,
            StartedAtUtc: startedAtUtc,
            CompletedAtUtc: completedAtUtc,
            AttemptNumber: request.AttemptNumber,
            CorrelationId: request.RequestId,
            InputHashSha256: ComputeInputHash(configuration, request.BrowserRequested),
            Postconditions: postconditions,
            Evidence: evidence,
            Artifacts: [],
            Issues: issues,
            Recovery: recovery);
        VerifiedOperationOutcomeValidator.ValidateAndThrow(outcome);

        var path = Path.Combine(
            configuration.StartupOutcomeReceiptRoot,
            readinessGateReceipt
                ? $"startup-readiness-{request.RequestId}-attempt-{request.AttemptNumber:D4}.verified-outcome.json"
                : $"startup-terminal-{request.RequestId}-attempt-{request.AttemptNumber:D4}.verified-outcome.json");
        AtomicJsonFile.Write(
            path,
            JsonSerializer.SerializeToUtf8Bytes(
                outcome,
                OperationsContractsJsonContext.Default.VerifiedOperationOutcome));
        return new LifecycleStartupOutcomeReceipt(outcome, path);
    }

    public static LifecycleStartupOutcomeReceipt PersistConfigurationBlocked(
        string installRoot,
        Exception exception,
        string? requestId = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var sessionId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var localServiceRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian",
            "service");
        var receiptRoot = Path.Combine(localServiceRoot, "receipts");
        var normalizedRequestId = NormalizeRequestId(requestId);
        var request = new LifecycleStartupOperationRequest(
            normalizedRequestId,
            GetNextAttemptNumber(receiptRoot, normalizedRequestId),
            BrowserRequested: true);
        var supervisorLogPath = Path.Combine(localServiceRoot, "logs", "lifecycle-supervisor.log");
        var manifestPath = Path.Combine(Path.GetFullPath(installRoot), "service", "lifecycle-supervisor.json");
        var message =
            $"Lifecycle configuration could not be loaded ({exception.GetType().Name}): {exception.Message}";
        AppendConfigurationDiagnostic(supervisorLogPath, message, now);
        var outcome = new VerifiedOperationOutcome(
            OperationId: $"startup:{request.RequestId}",
            OperationKind: OperationKind,
            State: OperationTerminalState.Blocked,
            StartedAtUtc: now,
            CompletedAtUtc: now,
            AttemptNumber: 1,
            CorrelationId: request.RequestId,
            // Deliberately NOT routed through Sha256Digest (which lowercases): receipt hashes
            // cross the launcher/supervisor process boundary and persist across restarts, so a
            // casing change must be coordinated on both sides at once (#2691).
            InputHashSha256: Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{manifestPath}\n{exception.GetType().FullName}\n{exception.Message}"))),
            Postconditions:
            [
                new OperationPostcondition(
                    "lifecycle-configuration",
                    "Lifecycle configuration is readable and valid.",
                    OperationPostconditionState.NotSatisfied,
                    Required: true,
                    EvidenceIds: ["manifest", "supervisor-log"])
            ],
            Evidence:
            [
                new OperationEvidenceReference(
                    "manifest",
                    "configuration",
                    message,
                    ToFileUri(manifestPath),
                    CapturedAtUtc: now),
                new OperationEvidenceReference(
                    "supervisor-log",
                    "log",
                    "Lifecycle supervisor diagnostics.",
                    ToFileUri(supervisorLogPath),
                    CapturedAtUtc: now)
            ],
            Artifacts: [],
            Issues:
            [
                new OperationIssue(
                    "lifecycle-configuration-blocked",
                    message,
                    OperationIssueSeverity.Error,
                    exception.GetType().Name,
                    "manifest")
                {
                    IsBlocking = true
                }
            ],
            Recovery:
            [
                new OperationRecoveryAction(
                    "repair-lifecycle-configuration",
                    "Repair lifecycle configuration",
                    $"Repair {manifestPath} or its permissions, run Meridian.LifecycleSupervisor preflight, then retry start.",
                    Retryable: true,
                    RequiresHumanAction: true,
                    Route: "command:Meridian.LifecycleSupervisor preflight")
                {
                    EvidenceIds = ["manifest", "supervisor-log"]
                }
            ]);
        VerifiedOperationOutcomeValidator.ValidateAndThrow(outcome);
        var path = Path.Combine(
            receiptRoot,
            $"startup-terminal-{request.RequestId}-attempt-{request.AttemptNumber:D4}.verified-outcome.json");
        AtomicJsonFile.Write(
            path,
            JsonSerializer.SerializeToUtf8Bytes(
                outcome,
                OperationsContractsJsonContext.Default.VerifiedOperationOutcome));
        return new LifecycleStartupOutcomeReceipt(outcome, path);
    }

    private static IReadOnlyList<OperationPostcondition> BuildPostconditions(
        bool prerequisitesSatisfied,
        bool readinessSatisfied,
        bool browserRequested,
        bool browserOpened)
    {
        var items = new List<OperationPostcondition>
        {
            new(
                "startup-prerequisites",
                "The host, data root, and configured PostgreSQL mode passed preflight validation.",
                prerequisitesSatisfied
                    ? OperationPostconditionState.Satisfied
                    : OperationPostconditionState.NotSatisfied,
                Required: true,
                EvidenceIds: ["session", "preflight", "supervisor-log"]),
            new(
                "host-ready",
                "The host returned a successful /readyz response with exact Ready status.",
                readinessSatisfied
                    ? OperationPostconditionState.Satisfied
                    : OperationPostconditionState.NotSatisfied,
                Required: true,
                EvidenceIds: ["readiness", "host-log", "database-log"])
        };

        if (browserRequested)
        {
            items.Add(new OperationPostcondition(
                "browser-opened",
                "The operating system accepted the workstation URL for browser launch.",
                browserOpened
                    ? OperationPostconditionState.Satisfied
                    : OperationPostconditionState.NotSatisfied,
                Required: false,
                EvidenceIds: ["browser-launch"]));
        }

        return items;
    }

    private static IReadOnlyList<OperationEvidenceReference> BuildEvidence(
        LifecycleSupervisorConfiguration configuration,
        string sessionId,
        bool prerequisitesSatisfied,
        string terminalMessage,
        int? httpPort,
        bool browserRequested,
        string? browserUri,
        DateTimeOffset capturedAtUtc)
    {
        var evidence = new List<OperationEvidenceReference>
        {
            EvidenceReference(
                "session",
                "lifecycle-session",
                $"Lifecycle supervisor session {sessionId}.",
                $"urn:meridian:lifecycle-session:{sessionId}",
                capturedAtUtc),
            EvidenceReference(
                "preflight",
                "preflight-result",
                prerequisitesSatisfied
                    ? "Lifecycle startup prerequisites passed."
                    : terminalMessage,
                ToFileUri(configuration.ManifestPath),
                capturedAtUtc),
            EvidenceReference(
                "supervisor-log",
                "log",
                $"Lifecycle supervisor diagnostics. Terminal detail: {terminalMessage}",
                ToFileUri(configuration.SupervisorLogPath),
                capturedAtUtc),
            EvidenceReference(
                "host-log",
                "log-directory",
                "Meridian host logs are retained in this directory.",
                ToFileUri(configuration.HostLogRoot),
                capturedAtUtc),
            EvidenceReference(
                "database-log",
                "log",
                configuration.Manifest.DatabaseMode == LifecycleDatabaseManagementMode.Dedicated
                    ? "Dedicated PostgreSQL diagnostics."
                    : "External database mode retains PostgreSQL logs outside Meridian ownership.",
                configuration.Manifest.DatabaseMode == LifecycleDatabaseManagementMode.Dedicated
                    ? ToFileUri(configuration.DatabaseLogPath)
                    : "urn:meridian:lifecycle:external-database-logs:operator-managed",
                capturedAtUtc),
            EvidenceReference(
                "readiness",
                "http-readiness",
                terminalMessage,
                httpPort is null
                    ? "urn:meridian:lifecycle:readiness-endpoint-unavailable"
                    : $"http://127.0.0.1:{httpPort}/readyz",
                capturedAtUtc)
        };
        if (browserRequested)
        {
            evidence.Add(EvidenceReference(
                "browser-launch",
                "browser-launch",
                browserUri is null
                    ? "A browser launch was requested, but no workstation URL was available."
                    : $"Workstation browser launch target: {browserUri}",
                browserUri ?? "urn:meridian:lifecycle:browser-target-unavailable",
                capturedAtUtc));
        }
        return evidence;
    }

    private static OperationEvidenceReference EvidenceReference(
        string evidenceId,
        string kind,
        string description,
        string? uri,
        DateTimeOffset capturedAtUtc)
    {
        return new OperationEvidenceReference(
            evidenceId,
            kind,
            description,
            uri,
            ContentHashSha256: null,
            capturedAtUtc);
    }

    private static IReadOnlyList<OperationIssue> BuildIssues(
        OperationTerminalState state,
        bool prerequisitesSatisfied,
        string terminalMessage,
        bool browserRequested,
        string? exceptionType)
    {
        return state switch
        {
            OperationTerminalState.Succeeded => [],
            OperationTerminalState.CompletedWithWarnings =>
            [
                new OperationIssue(
                    "browser-open-warning",
                    terminalMessage,
                    OperationIssueSeverity.Warning,
                    exceptionType,
                    browserRequested ? "browser-launch" : "supervisor-log")
            ],
            OperationTerminalState.Failed =>
            [
                new OperationIssue(
                    "startup-failed",
                    terminalMessage,
                    OperationIssueSeverity.Error,
                    exceptionType,
                    "readiness")
            ],
            OperationTerminalState.Blocked =>
            [
                new OperationIssue(
                    "startup-prerequisite-blocked",
                    terminalMessage,
                    OperationIssueSeverity.Error,
                    exceptionType,
                    prerequisitesSatisfied ? "supervisor-log" : "preflight")
                {
                    IsBlocking = true
                }
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
    }

    private static IReadOnlyList<OperationRecoveryAction> BuildRecovery(
        OperationTerminalState state,
        string? browserUri,
        LifecycleSupervisorConfiguration configuration)
    {
        if (state == OperationTerminalState.Succeeded)
            return [];

        if (state == OperationTerminalState.CompletedWithWarnings)
        {
            return
            [
                new OperationRecoveryAction(
                    "open-manually",
                    "Open Meridian manually",
                    $"Open {browserUri ?? "the workstation URL shown in the receipt"} in a browser, then retry with Meridian.LifecycleSupervisor open if needed.",
                    Retryable: true,
                    RequiresHumanAction: true,
                    Route: browserUri)
                {
                    EvidenceIds = ["browser-launch", "supervisor-log"]
                }
            ];
        }

        return
        [
            new OperationRecoveryAction(
                "inspect-startup-evidence",
                "Inspect startup evidence",
                $"Review the supervisor log at {configuration.SupervisorLogPath}, host logs under {configuration.HostLogRoot}, and PostgreSQL log at {configuration.DatabaseLogPath}.",
                Retryable: false,
                RequiresHumanAction: true)
            {
                EvidenceIds = ["supervisor-log", "host-log", "database-log", "readiness"]
            },
            new OperationRecoveryAction(
                "repair-and-retry",
                "Repair and retry startup",
                "Correct the reported prerequisite or runtime failure, run Meridian.LifecycleSupervisor preflight, then retry Meridian.LifecycleSupervisor start.",
                Retryable: true,
                RequiresHumanAction: true,
                Route: "command:Meridian.LifecycleSupervisor preflight")
            {
                EvidenceIds = ["preflight"]
            }
        ];
    }

    private static string ComputeInputHash(
        LifecycleSupervisorConfiguration configuration,
        bool browserRequested)
    {
        var material = new StringBuilder()
            .Append(configuration.ManifestPath).Append('\n')
            .Append(configuration.HostPath).Append('\n')
            .Append(configuration.DataRoot).Append('\n')
            .Append(configuration.Manifest.DatabaseMode).Append('\n')
            .Append(configuration.Manifest.HttpPort).Append('\n')
            .Append(configuration.Manifest.StartupTimeoutSeconds).Append('\n')
            .Append(browserRequested)
            .ToString();
        try
        {
            if (File.Exists(configuration.ManifestPath))
            {
                material += "\n" + Convert.ToHexString(
                    SHA256.HashData(File.ReadAllBytes(configuration.ManifestPath)));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            material += $"\nmanifest-unreadable:{ex.GetType().Name}";
        }
        // Deliberately NOT routed through Sha256Digest (which lowercases): receipt input hashes
        // cross the launcher/supervisor process boundary and persist across restarts, so a
        // casing change must be coordinated on both sides at once (#2691).
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static string ToFileUri(string path)
        => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    private static int GetNextAttemptNumber(string receiptRoot, string requestId)
    {
        if (!Directory.Exists(receiptRoot))
            return 1;

        var maxAttempt = 0;
        foreach (var path in Directory.EnumerateFiles(
                     receiptRoot,
                     $"startup-*-{requestId}-attempt-*.verified-outcome.json"))
        {
            try
            {
                var outcome = JsonSerializer.Deserialize(
                    File.ReadAllBytes(path),
                    OperationsContractsJsonContext.Default.VerifiedOperationOutcome);
                if (outcome is not null &&
                    string.Equals(outcome.OperationId, $"startup:{requestId}", StringComparison.Ordinal))
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

    private static void AppendConfigurationDiagnostic(
        string supervisorLogPath,
        string message,
        DateTimeOffset capturedAtUtc)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(supervisorLogPath)!);
            using var stream = new FileStream(
                supervisorLogPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                4096,
                FileOptions.WriteThrough);
            using var writer = new StreamWriter(stream);
            writer.WriteLine($"{capturedAtUtc:O} {message.Replace('\r', ' ').Replace('\n', ' ')}");
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

internal sealed class LifecycleStartupBlockedException(string message) : InvalidOperationException(message);
