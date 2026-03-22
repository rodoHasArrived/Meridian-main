namespace Meridian.Ledger;

/// <summary>
/// Unique key for one logical ledger inside a project-scoped ledger collection.
/// </summary>
public sealed record LedgerBookKey(
    string ProjectId,
    string LedgerBook,
    LedgerViewKind LedgerView = LedgerViewKind.Actual,
    string? ScenarioId = null)
{
    public LedgerBookKey Normalize()
    {
        if (string.IsNullOrWhiteSpace(ProjectId))
            throw new ArgumentException("ProjectId must not be null or whitespace.", nameof(ProjectId));
        if (string.IsNullOrWhiteSpace(LedgerBook))
            throw new ArgumentException("LedgerBook must not be null or whitespace.", nameof(LedgerBook));

        return this with
        {
            ProjectId = ProjectId.Trim(),
            LedgerBook = LedgerBook.Trim(),
            ScenarioId = string.IsNullOrWhiteSpace(ScenarioId) ? null : ScenarioId.Trim(),
        };
    }
}
