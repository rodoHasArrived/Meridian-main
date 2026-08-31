using static Meridian.Contracts.Text.TextPrimitives;
namespace Meridian.Ledger;

public sealed record JournalEvidenceReference(
    string EvidenceId,
    string Uri,
    string Kind,
    string SourceSystem,
    DateTimeOffset RetainedAtUtc,
    string RetainedBy,
    string? SubjectId = null,
    string? ContentHash = null,
    string? Description = null,
    string? SourceReference = null,
    string? ReviewStatus = null,
    string? ReviewedBy = null,
    DateTimeOffset? ReviewedAtUtc = null,
    DateOnly? EffectiveDate = null,
    long? EvidenceVersion = null,
    string? SubjectType = null)
{
    public JournalEvidenceReference Normalize()
        => new(
            RequireText(EvidenceId, nameof(EvidenceId)),
            RequireText(Uri, nameof(Uri)),
            RequireText(Kind, nameof(Kind)),
            RequireText(SourceSystem, nameof(SourceSystem)),
            RetainedAtUtc,
            RequireText(RetainedBy, nameof(RetainedBy)),
            NormalizeOptional(SubjectId),
            NormalizeOptional(ContentHash),
            NormalizeOptional(Description),
            NormalizeOptional(SourceReference),
            NormalizeOptional(ReviewStatus),
            NormalizeOptional(ReviewedBy),
            ReviewedAtUtc,
            EffectiveDate,
            EvidenceVersion,
            NormalizeOptional(SubjectType));

    private static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return value.Trim();
    }
}
