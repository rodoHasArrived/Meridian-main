using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster.CorporateActions;

/// <summary>Operator request to apply one staged inbox proposal.</summary>
public sealed record CorporateActionInboxApplyRequest(
    Guid SecurityId,
    string ActionType,
    DateOnly ExDate);

/// <summary>
/// Maps an ingest proposal onto the corporate-action event contract, mirroring the
/// orchestrator's auto-apply mapping so operator applies land identically.
/// </summary>
public static class CorporateActionProposalMapper
{
    public static CorporateActionDto ToCorporateAction(CorporateActionProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        return new CorporateActionDto(
            CorpActId: Guid.NewGuid(),
            SecurityId: proposal.SecurityId,
            EventType: proposal.ActionType,
            ExDate: proposal.ExDate,
            PayDate: proposal.PayableDate,
            DividendPerShare: proposal.Amount,
            Currency: proposal.Currency,
            SplitRatio: proposal.SplitFromFactor is > 0 && proposal.SplitToFactor is not null
                ? proposal.SplitToFactor / proposal.SplitFromFactor
                : null,
            NewSecurityId: null,
            DistributionRatio: null,
            AcquirerSecurityId: null,
            ExchangeRatio: null,
            SubscriptionPricePerShare: null,
            RightsPerShare: null,
            RecordDate: proposal.RecordDate);
    }
}

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

    /// <summary>
    /// Removes and returns the staged proposal matching (security, action type, ex-date) so an
    /// operator apply consumes it exactly once. Auto-applied proposals are not takeable.
    /// </summary>
    public bool TryTakeStaged(Guid securityId, string actionType, DateOnly exDate, out CorporateActionProposal proposal)
    {
        lock (_sync)
        {
            var match = _latest?.Proposals.FirstOrDefault(candidate =>
                !candidate.AutoApplied
                && candidate.SecurityId == securityId
                && string.Equals(candidate.ActionType, actionType, StringComparison.OrdinalIgnoreCase)
                && candidate.ExDate == exDate);
            if (match is null)
            {
                proposal = null!;
                return false;
            }

            _latest = _latest! with
            {
                Proposals = _latest.Proposals.Where(candidate => !ReferenceEquals(candidate, match)).ToArray(),
                Staged = Math.Max(0, _latest.Staged - 1)
            };
            proposal = match;
            return true;
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
