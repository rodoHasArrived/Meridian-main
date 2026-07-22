using System.Text.Json.Serialization;

namespace Meridian.Contracts.Operations;

[JsonConverter(typeof(JsonStringEnumConverter<OperationTerminalState>))]
public enum OperationTerminalState : byte
{
    Unknown = 0,
    Succeeded = 1,
    CompletedWithWarnings = 2,
    Failed = 3,
    Blocked = 4
}

[JsonConverter(typeof(JsonStringEnumConverter<OperationPostconditionState>))]
public enum OperationPostconditionState : byte
{
    Unknown = 0,
    Satisfied = 1,
    NotSatisfied = 2,
    NotEvaluated = 3
}

[JsonConverter(typeof(JsonStringEnumConverter<OperationIssueSeverity>))]
public enum OperationIssueSeverity : byte
{
    Unknown = 0,
    Warning = 1,
    Error = 2
}

public sealed record OperationPostcondition(
    string Code,
    string Description,
    OperationPostconditionState State,
    bool Required,
    IReadOnlyList<string> EvidenceIds)
{
    public IReadOnlyList<string> ArtifactIds { get; init; } = [];
}

public sealed record OperationEvidenceReference(
    string EvidenceId,
    string Kind,
    string Description,
    string? Uri = null,
    string? ContentHashSha256 = null,
    DateTimeOffset? CapturedAtUtc = null);

public sealed record OperationArtifactReference(
    string ArtifactId,
    string FileName,
    string ContentType,
    long ByteLength,
    string ContentHashSha256,
    string? Uri = null,
    string? PreviewUri = null);

public sealed record OperationIssue(
    string Code,
    string Message,
    OperationIssueSeverity Severity,
    string? ExceptionType = null,
    string? EvidenceId = null)
{
    public string? ArtifactId { get; init; }
    public bool IsBlocking { get; init; }
}

public sealed record OperationRecoveryAction(
    string ActionId,
    string Label,
    string Guidance,
    bool Retryable,
    bool RequiresHumanAction,
    string? Route = null)
{
    public IReadOnlyList<string> EvidenceIds { get; init; } = [];
    public IReadOnlyList<string> ArtifactIds { get; init; } = [];
}

/// <summary>
/// Common terminal receipt for an operation whose postconditions have been evaluated. Domain
/// result records should compose this receipt instead of maintaining an independent success flag.
/// </summary>
public sealed record VerifiedOperationOutcome(
    string OperationId,
    string OperationKind,
    OperationTerminalState State,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    int AttemptNumber,
    string? CorrelationId,
    string? InputHashSha256,
    IReadOnlyList<OperationPostcondition> Postconditions,
    IReadOnlyList<OperationEvidenceReference> Evidence,
    IReadOnlyList<OperationArtifactReference> Artifacts,
    IReadOnlyList<OperationIssue> Issues,
    IReadOnlyList<OperationRecoveryAction> Recovery)
{
    public const string CurrentSchemaVersion = "meridian.verified-operation-outcome.v1";

    public string SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// Origin of the figures this receipt attests to. Defaults to <see cref="DataProvenance.Real"/>;
    /// any operation whose inputs are simulated, seeded, or sample data must downgrade this so the
    /// signal travels with the receipt and downstream evidence gates can fail closed on it.
    /// </summary>
    public DataProvenance Provenance { get; init; } = DataProvenance.Real;

    public bool IsSuccessful =>
        State is OperationTerminalState.Succeeded or OperationTerminalState.CompletedWithWarnings;
}

/// <summary>Fail-closed invariant checks for terminal operation receipts.</summary>
public static class VerifiedOperationOutcomeValidator
{
    public static IReadOnlyList<string> Validate(VerifiedOperationOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        var errors = new List<string>();
        RequireText(outcome.SchemaVersion, nameof(outcome.SchemaVersion), errors);
        if (!string.Equals(
                outcome.SchemaVersion,
                VerifiedOperationOutcome.CurrentSchemaVersion,
                StringComparison.Ordinal))
        {
            errors.Add($"Unsupported SchemaVersion '{outcome.SchemaVersion}'.");
        }
        RequireText(outcome.OperationId, nameof(outcome.OperationId), errors);
        RequireText(outcome.OperationKind, nameof(outcome.OperationKind), errors);

        if (!Enum.IsDefined(outcome.Provenance))
        {
            errors.Add($"Provenance '{outcome.Provenance}' is not a defined DataProvenance value.");
        }

        if (outcome.AttemptNumber <= 0)
        {
            errors.Add("AttemptNumber must be positive.");
        }

        if (outcome.StartedAtUtc == default || outcome.CompletedAtUtc == default)
        {
            errors.Add("StartedAtUtc and CompletedAtUtc are required.");
        }
        else if (outcome.StartedAtUtc.Offset != TimeSpan.Zero
                 || outcome.CompletedAtUtc.Offset != TimeSpan.Zero)
        {
            errors.Add("StartedAtUtc and CompletedAtUtc must be UTC timestamps.");
        }
        else if (outcome.CompletedAtUtc < outcome.StartedAtUtc)
        {
            errors.Add("CompletedAtUtc cannot precede StartedAtUtc.");
        }

        RequireText(outcome.CorrelationId, nameof(outcome.CorrelationId), errors);
        ValidateRequiredHash(outcome.InputHashSha256, nameof(outcome.InputHashSha256), errors);

        var postconditions = outcome.Postconditions ?? [];
        var evidence = outcome.Evidence ?? [];
        var artifacts = outcome.Artifacts ?? [];
        var issues = outcome.Issues ?? [];
        var recovery = outcome.Recovery ?? [];

        if (evidence.Count == 0)
        {
            errors.Add("At least one retained evidence reference is required.");
        }

        if (postconditions.Count == 0)
        {
            errors.Add("At least one evaluated postcondition is required.");
        }
        else if (!postconditions.Any(static item => item.Required))
        {
            errors.Add("At least one required postcondition is required.");
        }

        ValidateUnique(postconditions.Select(static item => item.Code), "postcondition code", errors);
        ValidateUnique(evidence.Select(static item => item.EvidenceId), "evidence id", errors);
        ValidateUnique(artifacts.Select(static item => item.ArtifactId), "artifact id", errors);
        ValidateUnique(issues.Select(static item => item.Code), "issue code", errors);
        ValidateUnique(recovery.Select(static item => item.ActionId), "recovery action id", errors);

        var evidenceIds = evidence
            .Where(static item => !string.IsNullOrWhiteSpace(item.EvidenceId))
            .Select(static item => item.EvidenceId)
            .ToHashSet(StringComparer.Ordinal);
        var artifactIds = artifacts
            .Where(static item => !string.IsNullOrWhiteSpace(item.ArtifactId))
            .Select(static item => item.ArtifactId)
            .ToHashSet(StringComparer.Ordinal);
        var referencedEvidenceIds = new HashSet<string>(StringComparer.Ordinal);
        var referencedArtifactIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var postcondition in postconditions)
        {
            RequireText(postcondition.Code, "Postcondition.Code", errors);
            RequireText(postcondition.Description, $"Postcondition[{postcondition.Code}].Description", errors);
            if (postcondition.State == OperationPostconditionState.Unknown
                || !Enum.IsDefined(postcondition.State))
            {
                errors.Add($"Postcondition '{postcondition.Code}' has unsupported state '{postcondition.State}'.");
            }
            foreach (var evidenceId in postcondition.EvidenceIds ?? [])
            {
                if (!evidenceIds.Contains(evidenceId))
                {
                    errors.Add($"Postcondition '{postcondition.Code}' references missing evidence '{evidenceId}'.");
                }
                else
                {
                    referencedEvidenceIds.Add(evidenceId);
                }
            }
            foreach (var artifactId in postcondition.ArtifactIds ?? [])
            {
                if (!artifactIds.Contains(artifactId))
                {
                    errors.Add($"Postcondition '{postcondition.Code}' references missing artifact '{artifactId}'.");
                }
                else
                {
                    referencedArtifactIds.Add(artifactId);
                }
            }
            if (postcondition.Required &&
                (postcondition.EvidenceIds?.Count ?? 0) == 0 &&
                (postcondition.ArtifactIds?.Count ?? 0) == 0)
            {
                errors.Add($"Required postcondition '{postcondition.Code}' must reference retained evidence or an artifact.");
            }
        }

        foreach (var item in evidence)
        {
            RequireText(item.EvidenceId, "Evidence.EvidenceId", errors);
            RequireText(item.Kind, $"Evidence[{item.EvidenceId}].Kind", errors);
            RequireText(item.Description, $"Evidence[{item.EvidenceId}].Description", errors);
            ValidateOptionalHash(item.ContentHashSha256, $"Evidence[{item.EvidenceId}].ContentHashSha256", errors);
            ValidateOptionalUri(item.Uri, $"Evidence[{item.EvidenceId}].Uri", errors);
            if (string.IsNullOrWhiteSpace(item.Uri) && string.IsNullOrWhiteSpace(item.ContentHashSha256))
            {
                errors.Add(
                    $"Evidence '{item.EvidenceId}' must be durably locatable by URI or bound to retained content by SHA-256.");
            }
            if (item.CapturedAtUtc is not { } capturedAtUtc)
            {
                errors.Add($"Evidence '{item.EvidenceId}' requires a capture timestamp.");
            }
            else if (capturedAtUtc.Offset != TimeSpan.Zero
                     || capturedAtUtc < outcome.StartedAtUtc
                     || capturedAtUtc > outcome.CompletedAtUtc)
            {
                errors.Add(
                    $"Evidence '{item.EvidenceId}' must be captured in UTC within the operation interval.");
            }
        }

        foreach (var artifact in artifacts)
        {
            RequireText(artifact.ArtifactId, "Artifact.ArtifactId", errors);
            RequireText(artifact.FileName, $"Artifact[{artifact.ArtifactId}].FileName", errors);
            RequireText(artifact.ContentType, $"Artifact[{artifact.ArtifactId}].ContentType", errors);
            ValidateRequiredUri(artifact.Uri, $"Artifact[{artifact.ArtifactId}].Uri", errors);
            ValidateOptionalUri(artifact.PreviewUri, $"Artifact[{artifact.ArtifactId}].PreviewUri", errors);
            if (artifact.ByteLength <= 0)
            {
                errors.Add($"Artifact '{artifact.ArtifactId}' must reference non-empty retained bytes.");
            }
            ValidateRequiredHash(
                artifact.ContentHashSha256,
                $"Artifact[{artifact.ArtifactId}].ContentHashSha256",
                errors);
        }

        foreach (var issue in issues)
        {
            RequireText(issue.Code, "Issue.Code", errors);
            RequireText(issue.Message, $"Issue[{issue.Code}].Message", errors);
            if (issue.Severity == OperationIssueSeverity.Unknown
                || !Enum.IsDefined(issue.Severity))
            {
                errors.Add($"Issue '{issue.Code}' has unsupported severity '{issue.Severity}'.");
            }
            if (issue.EvidenceId is { Length: > 0 } issueEvidenceId && !evidenceIds.Contains(issueEvidenceId))
            {
                errors.Add($"Issue '{issue.Code}' references missing evidence '{issueEvidenceId}'.");
            }
            else if (issue.EvidenceId is { Length: > 0 })
            {
                referencedEvidenceIds.Add(issue.EvidenceId);
            }
            if (issue.ArtifactId is { Length: > 0 } issueArtifactId && !artifactIds.Contains(issueArtifactId))
            {
                errors.Add($"Issue '{issue.Code}' references missing artifact '{issueArtifactId}'.");
            }
            else if (issue.ArtifactId is { Length: > 0 })
            {
                referencedArtifactIds.Add(issue.ArtifactId);
            }
            if (string.IsNullOrWhiteSpace(issue.EvidenceId) && string.IsNullOrWhiteSpace(issue.ArtifactId))
            {
                errors.Add($"Issue '{issue.Code}' must reference retained evidence or an artifact.");
            }
            if (issue.IsBlocking && issue.Severity != OperationIssueSeverity.Error)
            {
                errors.Add($"Blocking issue '{issue.Code}' must have Error severity.");
            }
        }

        foreach (var action in recovery)
        {
            RequireText(action.ActionId, "Recovery.ActionId", errors);
            RequireText(action.Label, $"Recovery[{action.ActionId}].Label", errors);
            RequireText(action.Guidance, $"Recovery[{action.ActionId}].Guidance", errors);
            ValidateReferences(
                $"Recovery action '{action.ActionId}'",
                action.EvidenceIds,
                action.ArtifactIds,
                evidenceIds,
                artifactIds,
                referencedEvidenceIds,
                referencedArtifactIds,
                errors);
        }

        foreach (var evidenceId in evidenceIds.Except(referencedEvidenceIds, StringComparer.Ordinal))
        {
            errors.Add($"Evidence '{evidenceId}' is not linked to a postcondition, issue, or recovery action.");
        }
        foreach (var artifactId in artifactIds.Except(referencedArtifactIds, StringComparer.Ordinal))
        {
            errors.Add($"Artifact '{artifactId}' is not linked to a postcondition, issue, or recovery action.");
        }

        var allRequiredSatisfied = postconditions
            .Where(static item => item.Required)
            .All(static item => item.State == OperationPostconditionState.Satisfied);
        var hasUnmetRequired = postconditions.Any(static item =>
            item.Required && item.State is OperationPostconditionState.NotSatisfied or OperationPostconditionState.NotEvaluated);
        var hasWarning = issues.Any(static item => item.Severity == OperationIssueSeverity.Warning);
        var hasError = issues.Any(static item => item.Severity == OperationIssueSeverity.Error);
        var hasBlockingError = issues.Any(static item =>
            item.Severity == OperationIssueSeverity.Error && item.IsBlocking);

        if (outcome.State != OperationTerminalState.Blocked && hasBlockingError)
        {
            errors.Add("An outcome with a blocking error must use the Blocked terminal state.");
        }

        switch (outcome.State)
        {
            case OperationTerminalState.Succeeded:
                if (!allRequiredSatisfied)
                    errors.Add("Succeeded requires every required postcondition to be satisfied.");
                if (issues.Count > 0)
                    errors.Add("Succeeded cannot contain warning or error issues.");
                break;
            case OperationTerminalState.CompletedWithWarnings:
                if (!allRequiredSatisfied)
                    errors.Add("CompletedWithWarnings requires every required postcondition to be satisfied.");
                if (!hasWarning)
                    errors.Add("CompletedWithWarnings requires at least one warning issue.");
                if (hasError)
                    errors.Add("CompletedWithWarnings cannot contain error issues.");
                if (recovery.Count == 0)
                    errors.Add("CompletedWithWarnings requires recovery or review guidance.");
                break;
            case OperationTerminalState.Failed:
                if (!hasUnmetRequired)
                    errors.Add("Failed requires an unmet or unevaluated required postcondition.");
                if (!hasError)
                    errors.Add("Failed requires at least one error issue.");
                if (recovery.Count == 0)
                    errors.Add("Failed requires recovery guidance.");
                break;
            case OperationTerminalState.Blocked:
                if (!hasUnmetRequired)
                    errors.Add("Blocked requires an unmet or unevaluated required prerequisite.");
                if (!hasBlockingError)
                    errors.Add("Blocked requires at least one blocking error issue.");
                if (recovery.Count == 0)
                    errors.Add("Blocked requires recovery guidance.");
                break;
            default:
                errors.Add($"Unsupported terminal state '{outcome.State}'.");
                break;
        }

        return errors;
    }

    public static VerifiedOperationOutcome ValidateAndThrow(VerifiedOperationOutcome outcome)
    {
        var errors = Validate(outcome);
        if (errors.Count > 0)
        {
            throw new ArgumentException(
                $"Verified operation outcome is invalid: {string.Join(" ", errors)}",
                nameof(outcome));
        }

        return outcome;
    }

    private static void ValidateUnique(IEnumerable<string?> values, string label, ICollection<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            if (!seen.Add(value))
                errors.Add($"Duplicate {label} '{value}'.");
        }
    }

    private static void RequireText(string? value, string label, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"{label} is required.");
    }

    private static void ValidateOptionalHash(string? value, string label, ICollection<string> errors)
    {
        if (value is not null)
            ValidateRequiredHash(value, label, errors);
    }

    private static void ValidateRequiredHash(string? value, string label, ICollection<string> errors)
    {
        if (value is not { Length: 64 } || !value.All(Uri.IsHexDigit))
            errors.Add($"{label} must be a 64-character SHA-256 value.");
    }

    private static void ValidateReferences(
        string owner,
        IReadOnlyList<string>? ownerEvidenceIds,
        IReadOnlyList<string>? ownerArtifactIds,
        IReadOnlySet<string> evidenceIds,
        IReadOnlySet<string> artifactIds,
        ISet<string> referencedEvidenceIds,
        ISet<string> referencedArtifactIds,
        ICollection<string> errors)
    {
        foreach (var evidenceId in ownerEvidenceIds ?? [])
        {
            if (!evidenceIds.Contains(evidenceId))
                errors.Add($"{owner} references missing evidence '{evidenceId}'.");
            else
                referencedEvidenceIds.Add(evidenceId);
        }
        foreach (var artifactId in ownerArtifactIds ?? [])
        {
            if (!artifactIds.Contains(artifactId))
                errors.Add($"{owner} references missing artifact '{artifactId}'.");
            else
                referencedArtifactIds.Add(artifactId);
        }
    }

    private static void ValidateRequiredUri(string? value, string label, ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out _))
            errors.Add($"{label} must be a valid retained-artifact URI.");
    }

    private static void ValidateOptionalUri(string? value, string label, ICollection<string> errors)
    {
        if (value is not null)
            ValidateRequiredUri(value, label, errors);
    }
}
