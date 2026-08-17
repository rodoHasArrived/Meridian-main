using System.Globalization;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Storage.Archival;

namespace Meridian.Strategies.Services;

public sealed record ReconciliationPolicyThresholds(
    int MaxOpenBreakCount = 0,
    int MaxCriticalOpenBreakCount = 0,
    decimal MaxAbsoluteVariance = 0.01m,
    int MaxBreakAgeHours = 24,
    bool RequireSecondaryApprovalForWaivers = true);

public sealed record ReconciliationGateEvaluation(
    TradingAcceptanceGateStatusDto Status,
    string Detail,
    int OpenBreakCount,
    int CriticalOpenBreakCount,
    decimal MaxObservedAbsoluteVariance,
    bool SecondaryApprovalRequired)
{
    public string? StrategyRunId { get; init; }

    public string? ReconciliationRunId { get; init; }

    public DateTimeOffset? ReconciliationRunCreatedAt { get; init; }

    public DateTimeOffset? EvaluatedAtUtc { get; init; }

    public ReconciliationPolicyThresholds? Policy { get; init; }

    public bool WaiverRequested { get; init; }

    public bool SecondaryApprovalSigned { get; init; }

    public IReadOnlyList<string> BreachReasons { get; init; } = [];

    public bool SnapshotWasAuthoritative { get; init; }

    public string? ReconciliationSnapshotFingerprint { get; init; }

    public bool HasInvalidChronology { get; init; }

    public double MaxObservedBreakAgeHours { get; init; }

    public long MaxObservedBreakAgeTicks { get; init; }

    public string? OldestOpenBreakIdentity { get; init; }

    public DateTimeOffset? OldestOpenBreakFirstObservedAt { get; init; }
}

public interface IReconciliationGovernanceAuditStore
{
    Task AppendAsync(ReconciliationGateEvaluation evaluation, CancellationToken ct = default);
}

public sealed class JsonlReconciliationGovernanceAuditStore : IReconciliationGovernanceAuditStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly byte[] LineFeed = [(byte)'\n'];

    private readonly string _path;
    private readonly string _mutationLockPath;

    public JsonlReconciliationGovernanceAuditStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _mutationLockPath = _path + ".lock";
    }

    public async Task AppendAsync(ReconciliationGateEvaluation evaluation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evaluation);

        var payload = JsonSerializer.Serialize(new
        {
            evidenceVersion = 1,
            asOf = evaluation.EvaluatedAtUtc,
            evaluation.StrategyRunId,
            evaluation.ReconciliationRunId,
            evaluation.ReconciliationRunCreatedAt,
            evaluation.ReconciliationSnapshotFingerprint,
            evaluation.SnapshotWasAuthoritative,
            status = evaluation.Status.ToString(),
            evaluation.Detail,
            evaluation.Policy,
            evaluation.WaiverRequested,
            evaluation.SecondaryApprovalSigned,
            evaluation.SecondaryApprovalRequired,
            evaluation.BreachReasons,
            evaluation.HasInvalidChronology,
            evaluation.OpenBreakCount,
            evaluation.CriticalOpenBreakCount,
            evaluation.MaxObservedAbsoluteVariance,
            evaluation.MaxObservedBreakAgeHours,
            evaluation.MaxObservedBreakAgeTicks,
            evaluation.OldestOpenBreakIdentity,
            evaluation.OldestOpenBreakFirstObservedAt
        }, JsonOptions);

        // A single-byte terminator cannot tear halfway through a Windows CRLF sequence. Recovery
        // still accepts existing CRLF records because it searches for the final LF byte.
        var record = Utf8WithoutBom.GetBytes(payload + "\n");
        Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? ".");
        await using var mutationLease = await AcquireMutationLeaseAsync(ct).ConfigureAwait(false);
        await using var stream = new FileStream(
            _path,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        var needsSeparator = await RecoverTrailingRecordAsync(stream, ct).ConfigureAwait(false);
        stream.Seek(0, SeekOrigin.End);
        if (needsSeparator)
        {
            await stream.WriteAsync(LineFeed, ct).ConfigureAwait(false);
        }

        await stream.WriteAsync(record, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private async Task<FileStream> AcquireMutationLeaseAsync(CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(
                    _mutationLockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task<bool> RecoverTrailingRecordAsync(FileStream stream, CancellationToken ct)
    {
        if (stream.Length == 0)
        {
            return false;
        }

        var lastNewline = await FindLastNewlineAsync(stream, ct).ConfigureAwait(false);
        if (lastNewline == stream.Length - 1)
        {
            return false;
        }

        var trailingRecordStart = lastNewline + 1;
        stream.Position = trailingRecordStart;
        try
        {
            using var trailingRecord = await JsonDocument.ParseAsync(stream, cancellationToken: ct)
                .ConfigureAwait(false);
            return true;
        }
        catch (JsonException)
        {
            stream.SetLength(trailingRecordStart);
            stream.Position = trailingRecordStart;
            return false;
        }
    }

    private static async Task<long> FindLastNewlineAsync(FileStream stream, CancellationToken ct)
    {
        var buffer = new byte[4096];
        var searchEnd = stream.Length;
        while (searchEnd > 0)
        {
            ct.ThrowIfCancellationRequested();
            var searchStart = Math.Max(0, searchEnd - buffer.Length);
            var count = checked((int)(searchEnd - searchStart));
            stream.Position = searchStart;
            await stream.ReadExactlyAsync(buffer.AsMemory(0, count), ct).ConfigureAwait(false);
            for (var index = count - 1; index >= 0; index--)
            {
                if (buffer[index] == (byte)'\n')
                {
                    return searchStart + index;
                }
            }

            searchEnd = searchStart;
        }

        return -1;
    }
}

public sealed class ReconciliationGovernanceService
{
    private static readonly JsonSerializerOptions FingerprintJsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    private readonly IReconciliationRunRepository _repository;
    private readonly IReconciliationGovernanceAuditStore? _auditStore;
    private readonly TimeProvider _timeProvider;

    public ReconciliationGovernanceService(
        IReconciliationRunRepository repository,
        IReconciliationGovernanceAuditStore? auditStore = null)
        : this(repository, auditStore, TimeProvider.System)
    {
    }

    public ReconciliationGovernanceService(
        IReconciliationRunRepository repository,
        IReconciliationGovernanceAuditStore? auditStore,
        TimeProvider timeProvider)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _auditStore = auditStore;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<ReconciliationGateEvaluation> EvaluateGateAsync(
        string runId,
        ReconciliationPolicyThresholds policy,
        bool waiverRequested,
        bool secondaryApprovalSigned,
        CancellationToken ct = default,
        bool writeAudit = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(policy);
        ValidatePolicy(policy);

        return await _repository.ExecuteWithLatestForRunLeaseAsync(
            runId,
            async (latest, leaseCt) =>
            {
                var evaluatedAt = _timeProvider.GetUtcNow();
                var evaluation = EvaluateLeasedSnapshot(
                    runId,
                    latest,
                    policy,
                    waiverRequested,
                    secondaryApprovalSigned,
                    evaluatedAt);
                await AppendAuditIfRequestedAsync(evaluation, writeAudit, leaseCt).ConfigureAwait(false);
                return evaluation;
            },
            ct).ConfigureAwait(false);
    }

    private static ReconciliationGateEvaluation EvaluateLeasedSnapshot(
        string runId,
        ReconciliationRunDetail? latest,
        ReconciliationPolicyThresholds policy,
        bool waiverRequested,
        bool secondaryApprovalSigned,
        DateTimeOffset evaluatedAt)
    {
        if (latest is null)
        {
            return new ReconciliationGateEvaluation(
                TradingAcceptanceGateStatusDto.Blocked,
                "No reconciliation run is available for this strategy run.",
                0,
                0,
                0m,
                waiverRequested && policy.RequireSecondaryApprovalForWaivers)
            {
                StrategyRunId = runId,
                EvaluatedAtUtc = evaluatedAt,
                Policy = policy,
                WaiverRequested = waiverRequested,
                SecondaryApprovalSigned = secondaryApprovalSigned,
                BreachReasons = ["reconciliation-run-missing"],
                SnapshotWasAuthoritative = false
            };
        }

        var snapshotFingerprint = ComputeSnapshotFingerprint(latest);
        var openBreaks = latest.Breaks
            .Where(static item =>
                item.Status is not ReconciliationBreakStatus.Resolved
                    and not ReconciliationBreakStatus.Matched)
            .ToArray();
        var openCount = openBreaks.Length;
        var criticalCount = openBreaks.Count(static item =>
            item.Severity == ReconciliationBreakSeverity.Critical);
        var maxAbsoluteVariance = openBreaks
            .Select(static item => Absolute(item.Variance))
            .DefaultIfEmpty(0m)
            .Max();

        var hasInvalidChronology = latest.Summary.CreatedAt > evaluatedAt;
        var maxObservedAge = TimeSpan.Zero;
        ReconciliationBreakDto? oldestBreak = null;
        DateTimeOffset? oldestFirstObservedAt = null;
        foreach (var breakItem in openBreaks)
        {
            var firstObservedAt = breakItem.FirstObservedAt ?? latest.Summary.CreatedAt;
            if (!oldestFirstObservedAt.HasValue || firstObservedAt < oldestFirstObservedAt.Value)
            {
                oldestBreak = breakItem;
                oldestFirstObservedAt = firstObservedAt;
            }

            if (firstObservedAt > latest.Summary.CreatedAt || firstObservedAt > evaluatedAt)
            {
                hasInvalidChronology = true;
                continue;
            }

            var age = evaluatedAt - firstObservedAt;
            if (age > maxObservedAge)
            {
                maxObservedAge = age;
            }
        }

        var breachReasons = new List<string>();
        if (openCount > policy.MaxOpenBreakCount)
        {
            breachReasons.Add("open-break-count");
        }

        if (criticalCount > policy.MaxCriticalOpenBreakCount)
        {
            breachReasons.Add("critical-open-break-count");
        }

        if (maxAbsoluteVariance > policy.MaxAbsoluteVariance)
        {
            breachReasons.Add("absolute-variance");
        }

        if (maxObservedAge > TimeSpan.FromHours(policy.MaxBreakAgeHours))
        {
            breachReasons.Add("break-age");
        }

        if (hasInvalidChronology)
        {
            breachReasons.Add("break-chronology-invalid");
        }

        var secondaryApprovalRequired = waiverRequested && policy.RequireSecondaryApprovalForWaivers;
        var hasWaivableBreach = breachReasons.Any(static reason =>
            !string.Equals(reason, "break-chronology-invalid", StringComparison.Ordinal));
        var waiverIsComplete = waiverRequested
            && (!secondaryApprovalRequired || secondaryApprovalSigned);
        var status = hasInvalidChronology
            ? TradingAcceptanceGateStatusDto.Blocked
            : !hasWaivableBreach
                ? TradingAcceptanceGateStatusDto.Ready
                : waiverIsComplete
                    ? TradingAcceptanceGateStatusDto.ReviewRequired
                    : TradingAcceptanceGateStatusDto.Blocked;

        var detail = BuildDetail(
            latest.Summary.ReconciliationRunId,
            evaluatedAt,
            openCount,
            criticalCount,
            maxAbsoluteVariance,
            maxObservedAge,
            policy,
            breachReasons);

        return new ReconciliationGateEvaluation(
            status,
            detail,
            openCount,
            criticalCount,
            maxAbsoluteVariance,
            secondaryApprovalRequired)
        {
            StrategyRunId = runId,
            ReconciliationRunId = latest.Summary.ReconciliationRunId,
            ReconciliationRunCreatedAt = latest.Summary.CreatedAt,
            EvaluatedAtUtc = evaluatedAt,
            Policy = policy,
            WaiverRequested = waiverRequested,
            SecondaryApprovalSigned = secondaryApprovalSigned,
            BreachReasons = breachReasons.ToArray(),
            SnapshotWasAuthoritative = true,
            ReconciliationSnapshotFingerprint = snapshotFingerprint,
            HasInvalidChronology = hasInvalidChronology,
            MaxObservedBreakAgeHours = maxObservedAge.TotalHours,
            MaxObservedBreakAgeTicks = maxObservedAge.Ticks,
            OldestOpenBreakIdentity = oldestBreak?.LogicalBreakIdentity,
            OldestOpenBreakFirstObservedAt = oldestFirstObservedAt
        };
    }

    public static async Task<string> ExportEvidenceAsync(
        ReconciliationGateEvaluation evaluation,
        string outputDirectory,
        string runId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        if (!string.Equals(evaluation.StrategyRunId, runId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The requested strategy run does not match the run bound to this reconciliation evaluation.");
        }

        var reconciliationRunId = evaluation.ReconciliationRunId;
        var snapshotFingerprint = evaluation.ReconciliationSnapshotFingerprint;
        var evaluatedAt = evaluation.EvaluatedAtUtc;
        var boundPolicy = evaluation.Policy;
        if (string.IsNullOrWhiteSpace(reconciliationRunId)
            || string.IsNullOrWhiteSpace(snapshotFingerprint)
            || !evaluatedAt.HasValue
            || boundPolicy is null)
        {
            throw new InvalidOperationException(
                "Reconciliation evidence requires an evaluation bound to a run snapshot, policy, and evaluation time.");
        }

        Directory.CreateDirectory(outputDirectory);
        var safeRunId = SanitizeFileNameComponent(runId);
        var safeReconciliationRunId = SanitizeFileNameComponent(reconciliationRunId);
        var baseName = FormattableString.Invariant(
            $"reconciliation-evidence-{safeRunId}-{safeReconciliationRunId}-{evaluatedAt.Value:yyyyMMddHHmmssfffffff}-{Guid.NewGuid():N}");
        var jsonPath = Path.Combine(outputDirectory, baseName + ".json");
        var mdPath = Path.Combine(outputDirectory, baseName + ".md");

        await AtomicFileWriter.WriteAsync(
                jsonPath,
                JsonSerializer.Serialize(
                    evaluation,
                    new JsonSerializerOptions { WriteIndented = true }),
                ct)
            .ConfigureAwait(false);

        var breachReasons = evaluation.BreachReasons.Count == 0
            ? "none"
            : string.Join(", ", evaluation.BreachReasons);
        var maxAge = TimeSpan.FromTicks(evaluation.MaxObservedBreakAgeTicks);
        var summary = FormattableString.Invariant($"""
            # Reconciliation Evidence

            - Strategy run: `{evaluation.StrategyRunId}`
            - Reconciliation run: `{reconciliationRunId}`
            - Reconciliation created at: `{evaluation.ReconciliationRunCreatedAt:O}`
            - Snapshot fingerprint: `{snapshotFingerprint}`
            - Snapshot authoritative at decision boundary: `{evaluation.SnapshotWasAuthoritative}`
            - Evaluated at: `{evaluatedAt:O}`
            - Status: `{evaluation.Status}`
            - Detail: {evaluation.Detail}
            - Policy max open breaks: {boundPolicy.MaxOpenBreakCount}
            - Policy max critical open breaks: {boundPolicy.MaxCriticalOpenBreakCount}
            - Policy max absolute variance: {boundPolicy.MaxAbsoluteVariance}
            - Policy max break age (hours): {boundPolicy.MaxBreakAgeHours}
            - Policy requires secondary approval for waivers: {boundPolicy.RequireSecondaryApprovalForWaivers}
            - Waiver requested: {evaluation.WaiverRequested}
            - Secondary approval signed: {evaluation.SecondaryApprovalSigned}
            - Secondary approval required: {evaluation.SecondaryApprovalRequired}
            - Breach reasons: {breachReasons}
            - Invalid chronology: {evaluation.HasInvalidChronology}
            - Open breaks: {evaluation.OpenBreakCount}
            - Critical open breaks: {evaluation.CriticalOpenBreakCount}
            - Max absolute variance: {evaluation.MaxObservedAbsoluteVariance}
            - Max observed break age: `{maxAge:c}`
            - Max observed break age (ticks): {evaluation.MaxObservedBreakAgeTicks}
            - Max observed break age (hours): {evaluation.MaxObservedBreakAgeHours.ToString("R", CultureInfo.InvariantCulture)}
            - Oldest open break identity: `{evaluation.OldestOpenBreakIdentity ?? "none"}`
            - Oldest open break first observed at: `{evaluation.OldestOpenBreakFirstObservedAt:O}`
            """);
        await AtomicFileWriter.WriteAsync(mdPath, summary, ct).ConfigureAwait(false);
        return jsonPath;
    }

    private static string BuildDetail(
        string reconciliationRunId,
        DateTimeOffset evaluatedAt,
        int openCount,
        int criticalCount,
        decimal maxAbsoluteVariance,
        TimeSpan maxObservedAge,
        ReconciliationPolicyThresholds policy,
        IReadOnlyList<string> breachReasons)
    {
        var outcome = breachReasons.Count == 0 ? "within threshold" : "breached";
        var reasons = breachReasons.Count == 0 ? "none" : string.Join(",", breachReasons);
        return FormattableString.Invariant(
            $"""Reconciliation policy {outcome} for snapshot '{reconciliationRunId}' at {evaluatedAt:O} (open={openCount}/{policy.MaxOpenBreakCount}, critical={criticalCount}/{policy.MaxCriticalOpenBreakCount}, maxVariance={maxAbsoluteVariance}/{policy.MaxAbsoluteVariance}, maxBreakAge={maxObservedAge:c}, maxBreakAgeTicks={maxObservedAge.Ticks}, maxBreakAgeHours={maxObservedAge.TotalHours.ToString("R", CultureInfo.InvariantCulture)}/{policy.MaxBreakAgeHours}, reasons={reasons}).""");
    }

    private static string ComputeSnapshotFingerprint(ReconciliationRunDetail detail)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(detail, FingerprintJsonOptions);
        return $"sha256:{Sha256Digest.Compute(payload)}";
    }

    private static decimal Absolute(decimal value) =>
        value == decimal.MinValue ? decimal.MaxValue : Math.Abs(value);

    private static void ValidatePolicy(ReconciliationPolicyThresholds policy)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(policy.MaxOpenBreakCount);
        ArgumentOutOfRangeException.ThrowIfNegative(policy.MaxCriticalOpenBreakCount);
        ArgumentOutOfRangeException.ThrowIfNegative(policy.MaxAbsoluteVariance);
        ArgumentOutOfRangeException.ThrowIfNegative(policy.MaxBreakAgeHours);
        if (policy.MaxBreakAgeHours > TimeSpan.MaxValue.TotalHours)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy),
                policy.MaxBreakAgeHours,
                "Maximum reconciliation break age exceeds the supported TimeSpan range.");
        }
    }

    private static string SanitizeFileNameComponent(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(invalid.Contains(character) ? '_' : character);
        }

        return builder.ToString();
    }

    private async Task AppendAuditIfRequestedAsync(
        ReconciliationGateEvaluation evaluation,
        bool writeAudit,
        CancellationToken ct)
    {
        if (writeAudit && _auditStore is not null)
        {
            await _auditStore.AppendAsync(evaluation, ct).ConfigureAwait(false);
        }
    }
}
