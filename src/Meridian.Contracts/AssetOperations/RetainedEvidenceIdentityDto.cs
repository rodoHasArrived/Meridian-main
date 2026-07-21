using System.Globalization;
using Meridian.Contracts.Ledger;

namespace Meridian.Contracts.AssetOperations;

/// <summary>
/// Provider-neutral identity for evidence whose bytes, source, review, retention, and subject
/// scope have all been retained. A URI or source reference alone is not retained evidence.
/// </summary>
public sealed record RetainedEvidenceIdentityDto(
    string EvidenceId,
    string EvidenceUri,
    string ContentHashSha256,
    string SourceSystem,
    string SourceReference,
    string ReviewStatus,
    string ReviewedBy,
    DateTimeOffset ReviewedAtUtc,
    DateOnly EffectiveDate,
    long EvidenceVersion,
    DateTimeOffset RetainedAtUtc,
    string RetainedBy,
    string SubjectType,
    string SubjectId);

/// <summary>Exact typed subject bindings used as accounting authority.</summary>
public static class AssetAccountingEvidenceSubjects
{
    public const string Event = "AssetAccountingEvent";
    public const string PostingApproval = "AssetAccountingPostingApproval";
    public const string Reconciliation = "AssetAccountingReconciliation";
    public const string Report = "AssetAccountingReport";

    public static string PostingApprovalSubjectId(
        Guid eventId,
        long eventVersion,
        string fundProfileId,
        Guid ledgerBookId,
        Guid periodId,
        AccountingBasisKindDto accountingBasis,
        string approvalId,
        string draftedCandidateFingerprint,
        string? tenantId,
        string? companyId)
    {
        if (eventId == Guid.Empty)
            throw new ArgumentException("A canonical approval subject requires an event id.", nameof(eventId));
        if (eventVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(eventVersion), "A canonical approval subject requires a positive event version.");
        if (ledgerBookId == Guid.Empty)
            throw new ArgumentException("A canonical approval subject requires a ledger book id.", nameof(ledgerBookId));
        if (periodId == Guid.Empty)
            throw new ArgumentException("A canonical approval subject requires an accounting period id.", nameof(periodId));
        if (!Enum.IsDefined(accountingBasis))
            throw new ArgumentOutOfRangeException(nameof(accountingBasis), "A canonical approval subject requires a defined accounting basis.");

        return string.Join(
            "|",
            eventId.ToString("D"),
            eventVersion.ToString(CultureInfo.InvariantCulture),
            EscapeRequired(fundProfileId, nameof(fundProfileId)),
            ledgerBookId.ToString("D"),
            periodId.ToString("D"),
            accountingBasis.ToString(),
            EscapeRequired(approvalId, nameof(approvalId)),
            NormalizeSha256(draftedCandidateFingerprint, nameof(draftedCandidateFingerprint)),
            EscapeOptional(tenantId),
            EscapeOptional(companyId));
    }

    /// <summary>
    /// Produces the legacy event/fund/book-only key for retained-data migration and diagnostics.
    /// New approval evidence must use the fully scoped overload and validators reject this key.
    /// </summary>
    public static string LegacyPostingApprovalSubjectId(
        Guid eventId,
        string fundProfileId,
        Guid ledgerBookId,
        string? tenantId,
        string? companyId)
        => string.Join(
            "|",
            eventId.ToString("D"),
            EscapeRequired(fundProfileId, nameof(fundProfileId)),
            ledgerBookId.ToString("D"),
            EscapeOptional(tenantId),
            EscapeOptional(companyId));

    public static string PostingApprovalSubjectId(
        Guid eventId,
        string fundProfileId,
        Guid ledgerBookId,
        string? tenantId,
        string? companyId)
        => LegacyPostingApprovalSubjectId(eventId, fundProfileId, ledgerBookId, tenantId, companyId);

    public static string LifecycleSubjectId(
        Guid eventId,
        string fundProfileId,
        Guid ledgerBookId,
        string? tenantId,
        string? companyId,
        string referenceId)
        => string.Join(
            "|",
            eventId.ToString("D"),
            EscapeRequired(fundProfileId, nameof(fundProfileId)),
            ledgerBookId.ToString("D"),
            EscapeOptional(tenantId),
            EscapeOptional(companyId),
            EscapeRequired(referenceId, nameof(referenceId)));

    private static string EscapeRequired(string? value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A canonical evidence subject component is required.", parameterName)
            : Uri.EscapeDataString(value.Trim());

    private static string EscapeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : Uri.EscapeDataString(value.Trim());

    private static string NormalizeSha256(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A canonical approval subject requires a drafted candidate fingerprint.", parameterName);

        var normalized = value.Trim();
        if (normalized.Length != 64 ||
            normalized.Any(static character => !Uri.IsHexDigit(character)) ||
            normalized.All(static character => character == '0'))
        {
            throw new ArgumentException(
                "A canonical approval subject requires a non-zero 64-character hexadecimal drafted candidate fingerprint.",
                parameterName);
        }

        return normalized.ToLowerInvariant();
    }
}

/// <summary>Fail-closed completeness checks for retained evidence identities.</summary>
public static class RetainedEvidenceIdentityValidator
{
    public const string AcceptedReviewStatus = "Accepted";

    public static IReadOnlyList<string> Validate(RetainedEvidenceIdentityDto? evidence)
    {
        if (evidence is null)
        {
            return ["Retained evidence identity is required."];
        }

        var issues = new List<string>();
        RequireText(evidence.EvidenceId, nameof(evidence.EvidenceId), issues);

        if (string.IsNullOrWhiteSpace(evidence.EvidenceUri))
        {
            issues.Add("EvidenceUri is required.");
        }
        else if (!Uri.TryCreate(evidence.EvidenceUri.Trim(), UriKind.Absolute, out _))
        {
            issues.Add("EvidenceUri must be an absolute URI.");
        }

        ValidateSha256(evidence.ContentHashSha256, issues);
        RequireText(evidence.SourceSystem, nameof(evidence.SourceSystem), issues);
        RequireText(evidence.SourceReference, nameof(evidence.SourceReference), issues);

        if (!string.Equals(evidence.ReviewStatus?.Trim(), AcceptedReviewStatus, StringComparison.Ordinal))
        {
            issues.Add($"ReviewStatus must be {AcceptedReviewStatus}.");
        }

        RequireText(evidence.ReviewedBy, nameof(evidence.ReviewedBy), issues);
        ValidateUtcTimestamp(evidence.ReviewedAtUtc, nameof(evidence.ReviewedAtUtc), issues);

        if (evidence.EffectiveDate == default)
        {
            issues.Add("EffectiveDate is required.");
        }

        if (evidence.EvidenceVersion <= 0)
        {
            issues.Add("EvidenceVersion must be positive.");
        }

        ValidateUtcTimestamp(evidence.RetainedAtUtc, nameof(evidence.RetainedAtUtc), issues);
        if (evidence.ReviewedAtUtc != default &&
            evidence.RetainedAtUtc != default &&
            evidence.RetainedAtUtc < evidence.ReviewedAtUtc)
        {
            issues.Add("RetainedAtUtc cannot precede ReviewedAtUtc.");
        }
        RequireText(evidence.RetainedBy, nameof(evidence.RetainedBy), issues);
        RequireText(evidence.SubjectType, nameof(evidence.SubjectType), issues);
        RequireText(evidence.SubjectId, nameof(evidence.SubjectId), issues);

        return issues;
    }

    public static bool IsComplete(RetainedEvidenceIdentityDto? evidence) => Validate(evidence).Count == 0;

    private static void RequireText(string? value, string fieldName, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add($"{fieldName} is required.");
        }
    }

    private static void ValidateSha256(string? value, ICollection<string> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add("ContentHashSha256 is required.");
            return;
        }

        var hash = value.Trim();
        if (hash.Length != 64 ||
            hash.Any(static character => !Uri.IsHexDigit(character)) ||
            hash.All(static character => character == '0'))
        {
            issues.Add("ContentHashSha256 must be a non-zero 64-character hexadecimal SHA-256 hash.");
        }
    }

    private static void ValidateUtcTimestamp(
        DateTimeOffset value,
        string fieldName,
        ICollection<string> issues)
    {
        if (value == default)
        {
            issues.Add($"{fieldName} is required.");
        }
        else if (value.Offset != TimeSpan.Zero)
        {
            issues.Add($"{fieldName} must be a UTC timestamp.");
        }
    }
}
