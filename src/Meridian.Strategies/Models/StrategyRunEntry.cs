using System.Text;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Operations;
using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Models;

/// <summary>
/// Out-of-sample robustness evidence recorded for a run from a walk-forward analysis.
/// Consumed by the promotion policy so eligibility is not judged on a single
/// full-period backtest alone.
/// </summary>
/// <param name="OutOfSampleSharpeRatio">Sharpe ratio of the stitched out-of-sample equity curve.</param>
/// <param name="OutOfSampleMaxDrawdownPercent">Max drawdown of the stitched out-of-sample curve.</param>
/// <param name="DegradationRatio">Mean out-of-sample / in-sample objective ratio (1.0 = no degradation).</param>
/// <param name="WindowCount">Number of walk-forward windows evaluated.</param>
/// <param name="RecordedAt">When the evidence was recorded.</param>
/// <param name="SourceReference">Optional pointer to the retained walk-forward report.</param>
public sealed record StrategyRunWalkForwardEvidence(
    double OutOfSampleSharpeRatio,
    decimal OutOfSampleMaxDrawdownPercent,
    double DegradationRatio,
    int WindowCount,
    DateTimeOffset RecordedAt,
    string? SourceReference = null)
{
    /// <summary>
    /// Validates the metric domains, returning an error message or <c>null</c> when valid.
    /// Failing metric values (weak Sharpe, deep drawdown, heavy degradation) are recordable —
    /// the promotion policy judges those — but values outside their mathematical domain
    /// (non-finite numbers, negative drawdown magnitudes) would bypass the policy's
    /// threshold comparisons and must never be persisted as evidence.
    /// </summary>
    public string? Validate()
    {
        if (WindowCount <= 0)
            return "Walk-forward evidence requires at least one evaluated window.";
        if (!double.IsFinite(OutOfSampleSharpeRatio))
            return "Out-of-sample Sharpe ratio must be a finite number.";
        if (!double.IsFinite(DegradationRatio))
            return "Walk-forward degradation ratio must be a finite number.";
        if (OutOfSampleMaxDrawdownPercent < 0m)
            return "Out-of-sample max drawdown must be a non-negative magnitude.";
        return null;
    }
}

/// <summary>
/// An immutable record of a single strategy run (backtest, paper, or live).
/// Stored by <see cref="Storage.StrategyRunStore"/> and used by the promotion workflow.
/// </summary>
public sealed record StrategyRunEntry(
    string RunId,
    string StrategyId,
    string StrategyName,
    RunType RunType,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    BacktestResult? Metrics,
    string? DatasetReference = null,
    string? FeedReference = null,
    string? PortfolioId = null,
    string? LedgerReference = null,
    string? AuditReference = null,
    string? Engine = null,
    IReadOnlyDictionary<string, string>? ParameterSet = null,
    StrategyRunStatus? TerminalStatus = null,
    string? ParentRunId = null,
    string? FundProfileId = null,
    string? FundDisplayName = null,
    string? SweepId = null,
    string? SweepDefinitionHash = null,
    string? SweepObjective = null,
    IReadOnlyList<string>? OperatorAcceptanceCriteria = null,
    IReadOnlyList<string>? RetainedEvidenceReferences = null,
    IReadOnlyList<string>? AccountingRecordReferences = null,
    IReadOnlyList<string>? ApprovalReferences = null,
    IReadOnlyList<string>? PaperValidationReferences = null,
    IReadOnlyList<string>? GovernedReportReferences = null,
    StrategyRunLifecycleEventType LastLifecycleEvent = StrategyRunLifecycleEventType.Started,
    DateTimeOffset? LifecycleEventAtUtc = null,
    string? ActorId = null,
    string? CorrelationId = null,
    string? Reason = null,
    string? InputHashSha256 = null,
    string? AttemptId = null,
    int AttemptNumber = 1,
    string? RecoveryParentRunId = null,
    string? ExceptionType = null,
    string? ExceptionMessage = null,
    string? ExceptionStackTrace = null,
    IReadOnlyList<string>? ArtifactReferences = null,
    IReadOnlyList<OperationArtifactReference>? RetainedArtifacts = null,
    StrategyRunWalkForwardEvidence? WalkForwardEvidence = null,
    string? DataProvenanceToken = null)
{
    public IReadOnlyList<string> OperatorAcceptanceCriteria { get; init; } =
        OperatorAcceptanceCriteria ?? [];

    public IReadOnlyList<string> RetainedEvidenceReferences { get; init; } =
        RetainedEvidenceReferences ?? [];

    public IReadOnlyList<string> AccountingRecordReferences { get; init; } =
        AccountingRecordReferences ?? [];

    public IReadOnlyList<string> ApprovalReferences { get; init; } =
        ApprovalReferences ?? [];

    public IReadOnlyList<string> PaperValidationReferences { get; init; } =
        PaperValidationReferences ?? [];

    public IReadOnlyList<string> GovernedReportReferences { get; init; } =
        GovernedReportReferences ?? [];

    public IReadOnlyList<string> ArtifactReferences { get; init; } =
        ArtifactReferences ?? [];

    public IReadOnlyList<OperationArtifactReference> RetainedArtifacts { get; init; } =
        RetainedArtifacts ?? [];

    /// <summary>
    /// Terminal output payloads and summaries produced by the run. Unlike <see cref="ParameterSet"/>,
    /// this dictionary is not part of the immutable input hash and may first be populated only when
    /// the run reaches <see cref="StrategyRunLifecycleEventType.Completed"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string> OutputMetadata { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Creates a new run entry with a generated run ID and current timestamp.</summary>
    public static StrategyRunEntry Start(string strategyId, string strategyName, RunType runType)
    {
        var runId = Guid.NewGuid().ToString("N");
        var engine = runType switch
        {
            RunType.Backtest => "MeridianNative",
            RunType.Paper => "BrokerPaper",
            RunType.Live => "BrokerLive",
            _ => "Unknown"
        };
        return Start(strategyId, strategyName, runType, runId, engine: engine);
    }

    /// <summary>Creates a new run entry with an explicit run ID and optional metadata fields.</summary>
    public static StrategyRunEntry Start(
        string strategyId,
        string strategyName,
        RunType runType,
        string runId,
        string? datasetReference = null,
        string? feedReference = null,
        string? engine = null,
        IReadOnlyDictionary<string, string>? parameterSet = null)
        => StartWithEvidence(
            strategyId,
            strategyName,
            runType,
            runId,
            datasetReference,
            feedReference,
            engine,
            parameterSet);

    /// <summary>
    /// Creates a new evidence-bound run entry without changing the established <c>Start</c>
    /// CLR overloads.
    /// </summary>
    public static StrategyRunEntry StartWithEvidence(
        string strategyId,
        string strategyName,
        RunType runType,
        string runId,
        string? datasetReference = null,
        string? feedReference = null,
        string? engine = null,
        IReadOnlyDictionary<string, string>? parameterSet = null,
        IReadOnlyList<string>? operatorAcceptanceCriteria = null,
        IReadOnlyList<string>? retainedEvidenceReferences = null,
        IReadOnlyList<string>? accountingRecordReferences = null,
        IReadOnlyList<string>? approvalReferences = null,
        IReadOnlyList<string>? paperValidationReferences = null,
        IReadOnlyList<string>? governedReportReferences = null)
    {
        var now = DateTimeOffset.UtcNow;
        var portfolioId = $"{strategyId}-{runType.ToString().ToLowerInvariant()}-portfolio";
        var ledgerReference = $"{strategyId}-{runType.ToString().ToLowerInvariant()}-ledger";
        var inputHashSha256 = HasNonBlankEvidenceDeclarations(
            operatorAcceptanceCriteria,
            retainedEvidenceReferences,
            accountingRecordReferences,
            approvalReferences,
            paperValidationReferences,
            governedReportReferences)
            ? ComputeEvidenceBoundInputHash(
                strategyId,
                strategyName,
                runType,
                datasetReference,
                feedReference,
                engine,
                parameterSet,
                portfolioId: portfolioId,
                ledgerReference: ledgerReference,
                operatorAcceptanceCriteria: operatorAcceptanceCriteria,
                retainedEvidenceReferences: retainedEvidenceReferences,
                accountingRecordReferences: accountingRecordReferences,
                approvalReferences: approvalReferences,
                paperValidationReferences: paperValidationReferences,
                governedReportReferences: governedReportReferences)
            : ComputeInputHash(
                strategyId,
                strategyName,
                runType,
                datasetReference,
                feedReference,
                engine,
                parameterSet,
                portfolioId: portfolioId,
                ledgerReference: ledgerReference);
        return new(
            RunId: runId,
            StrategyId: strategyId,
            StrategyName: strategyName,
            RunType: runType,
            StartedAt: now,
            EndedAt: null,
            Metrics: null,
            DatasetReference: datasetReference,
            FeedReference: feedReference,
            PortfolioId: portfolioId,
            LedgerReference: ledgerReference,
            Engine: engine,
            ParameterSet: parameterSet,
            OperatorAcceptanceCriteria: operatorAcceptanceCriteria,
            RetainedEvidenceReferences: retainedEvidenceReferences,
            AccountingRecordReferences: accountingRecordReferences,
            ApprovalReferences: approvalReferences,
            PaperValidationReferences: paperValidationReferences,
            GovernedReportReferences: governedReportReferences,
            LastLifecycleEvent: StrategyRunLifecycleEventType.Started,
            LifecycleEventAtUtc: now,
            ActorId: "system",
            CorrelationId: runId,
            Reason: "Strategy run started.",
            InputHashSha256: inputHashSha256,
            AttemptId: runId,
            AttemptNumber: 1);
    }

    /// <summary>Returns a copy of this entry marked as ended with the provided metrics.</summary>
    public StrategyRunEntry Complete(BacktestResult? metrics)
    {
        var now = DateTimeOffset.UtcNow;
        return this with
        {
            EndedAt = now,
            Metrics = metrics,
            LastLifecycleEvent = StrategyRunLifecycleEventType.Completed,
            LifecycleEventAtUtc = now,
            Reason = "Strategy run completed.",
            ExceptionType = null,
            ExceptionMessage = null,
            ExceptionStackTrace = null
        };
    }

    /// <summary>Returns a copy of this entry marked as failed at the current time.</summary>
    public StrategyRunEntry Fail(Exception? exception = null, string? reason = null)
    {
        var now = DateTimeOffset.UtcNow;
        return this with
        {
            EndedAt = now,
            TerminalStatus = StrategyRunStatus.Failed,
            LastLifecycleEvent = StrategyRunLifecycleEventType.Failed,
            LifecycleEventAtUtc = now,
            Reason = reason ?? "Strategy run failed.",
            ExceptionType = exception?.GetType().FullName,
            ExceptionMessage = exception?.Message,
            ExceptionStackTrace = exception?.StackTrace
        };
    }

    /// <summary>Returns a copy of this entry marked as cancelled at the current time.</summary>
    public StrategyRunEntry Cancel(Exception? exception = null, string? reason = null)
    {
        var now = DateTimeOffset.UtcNow;
        return this with
        {
            EndedAt = now,
            TerminalStatus = StrategyRunStatus.Cancelled,
            LastLifecycleEvent = StrategyRunLifecycleEventType.Cancelled,
            LifecycleEventAtUtc = now,
            Reason = reason ?? "Strategy run was cancelled.",
            ExceptionType = exception?.GetType().FullName,
            ExceptionMessage = exception?.Message,
            ExceptionStackTrace = exception?.StackTrace
        };
    }

    public StrategyRunEntry StartFailed(Exception exception) =>
        Fail(exception, "Strategy start failed.") with
        {
            LastLifecycleEvent = StrategyRunLifecycleEventType.StartFailed
        };

    public StrategyRunEntry RequestStart(string actorId = "system", string? reason = null)
    {
        var now = DateTimeOffset.UtcNow;
        return this with
        {
            LastLifecycleEvent = StrategyRunLifecycleEventType.StartRequested,
            LifecycleEventAtUtc = now,
            ActorId = actorId,
            Reason = reason ?? "Strategy start requested.",
            ExceptionType = null,
            ExceptionMessage = null,
            ExceptionStackTrace = null
        };
    }

    public StrategyRunEntry Started(string actorId = "system", string? reason = null)
    {
        var now = DateTimeOffset.UtcNow;
        return this with
        {
            LastLifecycleEvent = StrategyRunLifecycleEventType.Started,
            LifecycleEventAtUtc = now,
            ActorId = actorId,
            Reason = reason ?? "Strategy started externally.",
            ExceptionType = null,
            ExceptionMessage = null,
            ExceptionStackTrace = null
        };
    }

    public StrategyRunEntry PauseRequested(string actorId = "system", string? reason = null)
    {
        var now = DateTimeOffset.UtcNow;
        return this with
        {
            LastLifecycleEvent = StrategyRunLifecycleEventType.PauseRequested,
            LifecycleEventAtUtc = now,
            ActorId = actorId,
            Reason = reason ?? "Strategy pause requested.",
            ExceptionType = null,
            ExceptionMessage = null,
            ExceptionStackTrace = null
        };
    }

    public StrategyRunEntry Paused(string actorId = "system", string? reason = null)
    {
        var now = DateTimeOffset.UtcNow;
        return this with
        {
            LastLifecycleEvent = StrategyRunLifecycleEventType.Paused,
            LifecycleEventAtUtc = now,
            ActorId = actorId,
            Reason = reason ?? "Strategy run paused.",
            ExceptionType = null,
            ExceptionMessage = null,
            ExceptionStackTrace = null
        };
    }

    public StrategyRunEntry PauseFailed(Exception exception, string actorId = "system")
    {
        var now = DateTimeOffset.UtcNow;
        return this with
        {
            LastLifecycleEvent = StrategyRunLifecycleEventType.PauseFailed,
            LifecycleEventAtUtc = now,
            ActorId = actorId,
            Reason = "Strategy pause failed.",
            ExceptionType = exception.GetType().FullName,
            ExceptionMessage = exception.Message,
            ExceptionStackTrace = exception.StackTrace
        };
    }

    public StrategyRunEntry RequestStop(string actorId = "system", string? reason = null)
    {
        var now = DateTimeOffset.UtcNow;
        return this with
        {
            LastLifecycleEvent = StrategyRunLifecycleEventType.StopRequested,
            LifecycleEventAtUtc = now,
            ActorId = actorId,
            Reason = reason ?? "Strategy stop requested."
        };
    }

    public StrategyRunEntry StopFailed(Exception exception, string actorId = "system")
    {
        ArgumentNullException.ThrowIfNull(exception);
        var now = DateTimeOffset.UtcNow;
        return this with
        {
            LastLifecycleEvent = StrategyRunLifecycleEventType.StopFailed,
            LifecycleEventAtUtc = now,
            ActorId = actorId,
            Reason = "Strategy stop failed.",
            ExceptionType = exception.GetType().FullName,
            ExceptionMessage = exception.Message,
            ExceptionStackTrace = exception.StackTrace
        };
    }

    public StrategyRunEntry RecoveryAttempted(
        string recoveryParentRunId,
        int attemptNumber,
        string actorId = "system",
        string? reason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryParentRunId);
        if (attemptNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        }

        var now = DateTimeOffset.UtcNow;
        return this with
        {
            LastLifecycleEvent = StrategyRunLifecycleEventType.RecoveryAttempted,
            LifecycleEventAtUtc = now,
            ActorId = actorId,
            Reason = reason ?? "Strategy recovery attempted.",
            AttemptNumber = attemptNumber,
            AttemptId = Guid.NewGuid().ToString("N"),
            RecoveryParentRunId = recoveryParentRunId
        };
    }

    public StrategyRunEntry EvidencePersistenceFailed(
        Exception exception,
        string reason,
        string actorId = "system")
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var now = DateTimeOffset.UtcNow;
        return this with
        {
            LastLifecycleEvent = StrategyRunLifecycleEventType.EvidencePersistenceFailed,
            LifecycleEventAtUtc = now,
            ActorId = actorId,
            Reason = reason,
            ExceptionType = exception.GetType().FullName,
            ExceptionMessage = exception.Message,
            ExceptionStackTrace = exception.StackTrace
        };
    }

    /// <summary>
    /// Computes the v3 canonical SHA-256 hash of the inputs and evidence declarations that define
    /// a strategy run.
    /// </summary>
    public static string ComputeEvidenceBoundInputHash(
        string strategyId,
        string strategyName,
        RunType runType,
        string? datasetReference,
        string? feedReference,
        string? engine,
        IReadOnlyDictionary<string, string>? parameterSet,
        string? parentRunId = null,
        string? portfolioId = null,
        string? ledgerReference = null,
        string? auditReference = null,
        string? fundProfileId = null,
        IReadOnlyList<string>? operatorAcceptanceCriteria = null,
        IReadOnlyList<string>? retainedEvidenceReferences = null,
        IReadOnlyList<string>? accountingRecordReferences = null,
        IReadOnlyList<string>? approvalReferences = null,
        IReadOnlyList<string>? paperValidationReferences = null,
        IReadOnlyList<string>? governedReportReferences = null)
    {
        var builder = new StringBuilder();
        AppendCanonical(builder, "meridian.strategy-run-input.v3");
        AppendCanonical(builder, strategyId);
        AppendCanonical(builder, strategyName);
        AppendCanonical(builder, runType.ToString());
        AppendCanonical(builder, datasetReference);
        AppendCanonical(builder, feedReference);
        AppendCanonical(builder, engine);
        AppendCanonical(builder, parentRunId);
        AppendCanonical(builder, portfolioId);
        AppendCanonical(builder, ledgerReference);
        AppendCanonical(builder, auditReference);
        AppendCanonical(builder, fundProfileId);

        foreach (var parameter in parameterSet?.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                     ?? Enumerable.Empty<KeyValuePair<string, string>>())
        {
            AppendCanonical(builder, parameter.Key);
            AppendCanonical(builder, parameter.Value);
        }

        AppendCanonicalCollection(builder, nameof(OperatorAcceptanceCriteria), operatorAcceptanceCriteria);
        AppendCanonicalCollection(builder, nameof(RetainedEvidenceReferences), retainedEvidenceReferences);
        AppendCanonicalCollection(builder, nameof(AccountingRecordReferences), accountingRecordReferences);
        AppendCanonicalCollection(builder, nameof(ApprovalReferences), approvalReferences);
        AppendCanonicalCollection(builder, nameof(PaperValidationReferences), paperValidationReferences);
        AppendCanonicalCollection(builder, nameof(GovernedReportReferences), governedReportReferences);

        return Sha256Digest.ComputeUtf8(builder.ToString());
    }

    /// <summary>
    /// Computes the v4 canonical SHA-256 hash: everything
    /// <see cref="ComputeEvidenceBoundInputHash"/> covers, plus the execution-realism settings that
    /// determine what numbers the run produces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// v2 and v3 hash strategy inputs and evidence but no execution-realism setting, so two runs
    /// differing only in fill timing, fill conservatism, or cost model hash identically while
    /// producing materially different P&amp;L. Callers that know their realism configuration should
    /// use this overload; the earlier versions remain for entries written before realism was
    /// captured.
    /// </para>
    /// <para>
    /// The version prefix keeps the schemes disjoint: a v4 digest can never collide with a v2 or v3
    /// digest for the same inputs, so a hash produced under one scheme is never mistaken for
    /// agreement with another. Comparers must treat differing schemes as "not comparable" rather
    /// than as "unchanged".
    /// </para>
    /// </remarks>
    public static string ComputeRealismBoundInputHash(
        string strategyId,
        string strategyName,
        RunType runType,
        string? datasetReference,
        string? feedReference,
        string? engine,
        IReadOnlyDictionary<string, string>? parameterSet,
        ExecutionRealismDescriptor? executionRealism,
        string? parentRunId = null,
        string? portfolioId = null,
        string? ledgerReference = null,
        string? auditReference = null,
        string? fundProfileId = null,
        IReadOnlyList<string>? operatorAcceptanceCriteria = null,
        IReadOnlyList<string>? retainedEvidenceReferences = null,
        IReadOnlyList<string>? accountingRecordReferences = null,
        IReadOnlyList<string>? approvalReferences = null,
        IReadOnlyList<string>? paperValidationReferences = null,
        IReadOnlyList<string>? governedReportReferences = null)
    {
        var builder = new StringBuilder();
        AppendCanonical(builder, "meridian.strategy-run-input.v4");
        AppendCanonical(builder, strategyId);
        AppendCanonical(builder, strategyName);
        AppendCanonical(builder, runType.ToString());
        AppendCanonical(builder, datasetReference);
        AppendCanonical(builder, feedReference);
        AppendCanonical(builder, engine);
        AppendCanonical(builder, parentRunId);
        AppendCanonical(builder, portfolioId);
        AppendCanonical(builder, ledgerReference);
        AppendCanonical(builder, auditReference);
        AppendCanonical(builder, fundProfileId);

        foreach (var parameter in parameterSet?.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                     ?? Enumerable.Empty<KeyValuePair<string, string>>())
        {
            AppendCanonical(builder, parameter.Key);
            AppendCanonical(builder, parameter.Value);
        }

        AppendCanonicalCollection(builder, nameof(OperatorAcceptanceCriteria), operatorAcceptanceCriteria);
        AppendCanonicalCollection(builder, nameof(RetainedEvidenceReferences), retainedEvidenceReferences);
        AppendCanonicalCollection(builder, nameof(AccountingRecordReferences), accountingRecordReferences);
        AppendCanonicalCollection(builder, nameof(ApprovalReferences), approvalReferences);
        AppendCanonicalCollection(builder, nameof(PaperValidationReferences), paperValidationReferences);
        AppendCanonicalCollection(builder, nameof(GovernedReportReferences), governedReportReferences);

        // A run whose realism is unknown and a run explicitly configured with defaults are not the
        // same claim, so the absent case gets its own marker rather than the default descriptor's
        // canonical form.
        AppendCanonical(builder, "ExecutionRealism");
        AppendCanonical(builder, executionRealism?.ToCanonicalString() ?? "unspecified");

        return Sha256Digest.ComputeUtf8(builder.ToString());
    }

    /// <summary>
    /// Computes the v2 canonical input hash. This exact public signature is retained for binary
    /// compatibility; new evidence-bound runs use <see cref="ComputeEvidenceBoundInputHash"/>.
    /// </summary>
    public static string ComputeInputHash(
        string strategyId,
        string strategyName,
        RunType runType,
        string? datasetReference,
        string? feedReference,
        string? engine,
        IReadOnlyDictionary<string, string>? parameterSet,
        string? parentRunId = null,
        string? portfolioId = null,
        string? ledgerReference = null,
        string? auditReference = null,
        string? fundProfileId = null)
    {
        var builder = new StringBuilder();
        AppendCanonical(builder, "meridian.strategy-run-input.v2");
        AppendCanonical(builder, strategyId);
        AppendCanonical(builder, strategyName);
        AppendCanonical(builder, runType.ToString());
        AppendCanonical(builder, datasetReference);
        AppendCanonical(builder, feedReference);
        AppendCanonical(builder, engine);
        AppendCanonical(builder, parentRunId);
        AppendCanonical(builder, portfolioId);
        AppendCanonical(builder, ledgerReference);
        AppendCanonical(builder, auditReference);
        AppendCanonical(builder, fundProfileId);

        foreach (var parameter in parameterSet?.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                     ?? Enumerable.Empty<KeyValuePair<string, string>>())
        {
            AppendCanonical(builder, parameter.Key);
            AppendCanonical(builder, parameter.Value);
        }

        return Sha256Digest.ComputeUtf8(builder.ToString());
    }

    internal static string ComputeLegacyInputHash(
        string strategyId,
        string strategyName,
        RunType runType,
        string? datasetReference,
        string? feedReference,
        string? engine,
        IReadOnlyDictionary<string, string>? parameterSet)
    {
        var builder = new StringBuilder();
        AppendCanonical(builder, strategyId);
        AppendCanonical(builder, strategyName);
        AppendCanonical(builder, runType.ToString());
        AppendCanonical(builder, datasetReference);
        AppendCanonical(builder, feedReference);
        AppendCanonical(builder, engine);

        foreach (var parameter in parameterSet?.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                     ?? Enumerable.Empty<KeyValuePair<string, string>>())
        {
            AppendCanonical(builder, parameter.Key);
            AppendCanonical(builder, parameter.Value);
        }

        return Sha256Digest.ComputeUtf8(builder.ToString());
    }

    internal static bool HasNonBlankEvidenceDeclarations(
        IEnumerable<string>? operatorAcceptanceCriteria,
        IEnumerable<string>? retainedEvidenceReferences,
        IEnumerable<string>? accountingRecordReferences,
        IEnumerable<string>? approvalReferences,
        IEnumerable<string>? paperValidationReferences,
        IEnumerable<string>? governedReportReferences) =>
        HasNonBlankValue(operatorAcceptanceCriteria) ||
        HasNonBlankValue(retainedEvidenceReferences) ||
        HasNonBlankValue(accountingRecordReferences) ||
        HasNonBlankValue(approvalReferences) ||
        HasNonBlankValue(paperValidationReferences) ||
        HasNonBlankValue(governedReportReferences);

    private static bool HasNonBlankValue(IEnumerable<string>? values) =>
        values?.Any(static value => !string.IsNullOrWhiteSpace(value)) == true;

    private static void AppendCanonical(StringBuilder builder, string? value)
    {
        value ??= string.Empty;
        builder.Append(value.Length).Append(':').Append(value).Append('|');
    }

    private static void AppendCanonicalCollection(
        StringBuilder builder,
        string collectionName,
        IEnumerable<string>? values)
    {
        var normalized = values?
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToArray() ?? [];

        AppendCanonical(builder, collectionName);
        AppendCanonical(builder, normalized.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        foreach (var value in normalized)
        {
            AppendCanonical(builder, value);
        }
    }
}
