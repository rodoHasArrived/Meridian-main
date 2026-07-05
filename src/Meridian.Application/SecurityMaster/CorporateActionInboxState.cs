namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Snapshot of the corporate-action inbox: staged (not auto-applied) proposals from the most
/// recent ingest sweep, with enough run context for the workbench badge and review list.
/// </summary>
public sealed record CorporateActionInboxDto(
    DateTimeOffset? LastIngestAt,
    int StagedCount,
    int AppliedLastRun,
    int DuplicatesSkippedLastRun,
    IReadOnlyList<CorporateActionProposal> Staged,
    IReadOnlyList<string> Errors);

/// <summary>
/// Holds the latest corporate-action ingest outcome so the workbench can badge and list
/// staged proposals between sweeps. In-memory by design: proposals are re-derivable by
/// re-running ingest, and applied actions are already durable in the event store.
/// </summary>
public sealed class CorporateActionInboxState
{
    private readonly object _sync = new();
    private CorporateActionIngestResult? _latest;
    private DateTimeOffset? _latestAt;

    public void Record(CorporateActionIngestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        lock (_sync)
        {
            _latest = result;
            _latestAt = DateTimeOffset.UtcNow;
        }
    }

    public CorporateActionInboxDto GetInbox()
    {
        CorporateActionIngestResult? latest;
        DateTimeOffset? latestAt;
        lock (_sync)
        {
            latest = _latest;
            latestAt = _latestAt;
        }

        if (latest is null)
        {
            return new CorporateActionInboxDto(null, 0, 0, 0, [], []);
        }

        var staged = latest.Proposals
            .Where(static proposal => !proposal.AutoApplied)
            .OrderBy(static proposal => proposal.ExDate)
            .ThenBy(static proposal => proposal.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CorporateActionInboxDto(
            LastIngestAt: latestAt,
            StagedCount: staged.Length,
            AppliedLastRun: latest.Applied,
            DuplicatesSkippedLastRun: latest.DuplicatesSkipped,
            Staged: staged,
            Errors: latest.Errors);
    }
}
