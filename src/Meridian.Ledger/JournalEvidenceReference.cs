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
    string? Description = null)
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
            NormalizeOptional(Description));

    private static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
