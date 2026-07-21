using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.FinancialOperations.PrivateCapital;

/// <summary>
/// Folds posted private-capital activity against an investor commitment into the
/// uncalled-commitment roll-forward, enforcing the invariant
/// net-called + uncalled + expired = total on every step. Pure and deterministic:
/// the same events always produce the same steps.
/// </summary>
public static class CommitmentRollForwardCalculator
{
    public const string InvariantBreachIssueCode = "private-capital.commitment-invariant-breach";
    public const string OverCallIssueCode = "private-capital.commitment-over-call";
    public const string OverExpiryIssueCode = "private-capital.commitment-over-expiry";

    /// <summary>
    /// Builds the roll-forward for one commitment from dated activity and governed expiry events.
    /// </summary>
    public static CommitmentRollForward Build(
        InvestorCommitment commitment,
        IReadOnlyList<CommitmentActivityEvent> events,
        IReadOnlyList<CommitmentExpiryEvent>? expiryEvents = null)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        ArgumentNullException.ThrowIfNull(events);

        var issues = new List<AccountingConfigurationValidationIssueDto>();
        var recallableDistributions = new List<RecallableDistributionEvent>();
        var steps = new List<CommitmentRollForwardStep>();

        var cumulativeCalled = 0m;
        var cumulativeRestored = 0m;
        var cumulativeExpired = 0m;

        foreach (var item in OrderTimeline(commitment, events, expiryEvents, issues))
        {
            var callDelta = 0m;
            var recallableDelta = 0m;
            var expiredDelta = 0m;
            var uncalledBefore = commitment.TotalCommitment - cumulativeCalled + cumulativeRestored - cumulativeExpired;

            switch (item)
            {
                case TimelineEntry(var activity, null):
                    switch (activity!.EntryType)
                    {
                        case ManualJournalEntryTypeDto.CapitalCall:
                            callDelta = activity.GrossAmount;
                            if (callDelta > uncalledBefore + LedgerToleranceConstants.Balance)
                            {
                                issues.Add(new AccountingConfigurationValidationIssueDto(
                                    OverCallIssueCode,
                                    AccountingConfigurationValidationSeverityDto.Critical,
                                    $"Capital call '{activity.FundEventId}' for {callDelta} exceeds uncalled commitment {uncalledBefore} on '{commitment.CommitmentId}'.",
                                    commitment.CommitmentId,
                                    "Verify the call amount against the commitment register before posting."));
                            }

                            cumulativeCalled += callDelta;
                            break;

                        case ManualJournalEntryTypeDto.Distribution
                            when activity.Recallability == DistributionRecallability.RecallableReturnOfCapital:
                            var capacity = Math.Max(0m, commitment.RecallCapAmount - cumulativeRestored);
                            var restorable = Math.Min(activity.GrossAmount, capacity);
                            recallableDelta = restorable;
                            cumulativeRestored += restorable;
                            recallableDistributions.Add(new RecallableDistributionEvent(
                                activity.FundEventId,
                                commitment.CommitmentId,
                                activity.EffectiveDate,
                                activity.GrossAmount,
                                restorable,
                                activity.GrossAmount - restorable));
                            break;

                        default:
                            // Non-recallable distributions, redemptions, fees, subscriptions, and
                            // transfers do not move uncalled commitment.
                            continue;
                    }

                    steps.Add(new CommitmentRollForwardStep(
                        activity.FundEventId,
                        activity.EntryType,
                        activity.EffectiveDate,
                        callDelta,
                        recallableDelta,
                        expiredDelta,
                        RunningUncalled: uncalledBefore - callDelta + recallableDelta,
                        RunningNetCalled: cumulativeCalled - cumulativeRestored));
                    break;

                case TimelineEntry(null, var expiry):
                    expiredDelta = expiry!.Amount;
                    if (expiredDelta > uncalledBefore + LedgerToleranceConstants.Balance)
                    {
                        issues.Add(new AccountingConfigurationValidationIssueDto(
                            OverExpiryIssueCode,
                            AccountingConfigurationValidationSeverityDto.Critical,
                            $"Expiry '{expiry.ExpiryEventId}' releases {expiredDelta} but only {uncalledBefore} is uncalled on '{commitment.CommitmentId}'.",
                            commitment.CommitmentId,
                            "Correct the expiry amount to at most the remaining uncalled commitment."));
                    }

                    cumulativeExpired += expiredDelta;
                    steps.Add(new CommitmentRollForwardStep(
                        expiry.ExpiryEventId,
                        ManualJournalEntryTypeDto.General,
                        expiry.EffectiveDate,
                        CallDelta: 0m,
                        RecallableDelta: 0m,
                        expiredDelta,
                        RunningUncalled: uncalledBefore - expiredDelta,
                        RunningNetCalled: cumulativeCalled - cumulativeRestored));
                    break;
            }
        }

        var rollForward = new CommitmentRollForward(
            commitment,
            cumulativeCalled,
            cumulativeRestored,
            cumulativeExpired,
            steps,
            issues)
        {
            RecallableDistributions = recallableDistributions,
        };

        if (!rollForward.InvariantHolds
            && issues.All(static issue => issue.Severity != AccountingConfigurationValidationSeverityDto.Critical))
        {
            // The identity holds by construction, so a breach here means arithmetic drift rather
            // than a flagged over-call/over-expiry. Surface it on the same channel.
            issues.Add(new AccountingConfigurationValidationIssueDto(
                InvariantBreachIssueCode,
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Commitment '{commitment.CommitmentId}' roll-forward violated net-called + uncalled + expired = total.",
                commitment.CommitmentId,
                "Recompute the roll-forward from posted journals and reconcile the commitment register."));
        }

        return rollForward;
    }

    /// <summary>
    /// Adapts the private-capital fund-event projection for one commitment. Events are matched by
    /// capital account (and investor when both sides declare one) in the commitment currency;
    /// recallability is read from the supplied classifier because the projection does not carry it
    /// structurally (it rides journal metadata tags).
    /// </summary>
    public static CommitmentRollForward BuildFromFundEvents(
        InvestorCommitment commitment,
        IReadOnlyList<PrivateCapitalFundEventDto> fundEvents,
        Func<PrivateCapitalFundEventDto, DistributionRecallability>? recallabilityClassifier = null,
        IReadOnlyList<CommitmentExpiryEvent>? expiryEvents = null,
        bool includeUnposted = false)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        ArgumentNullException.ThrowIfNull(fundEvents);

        var events = fundEvents
            .Where(fundEvent => Matches(commitment, fundEvent) && (includeUnposted || fundEvent.IsPosted))
            .Select(fundEvent => new CommitmentActivityEvent(
                fundEvent.FundEventId,
                fundEvent.EntryType,
                fundEvent.EffectiveDate,
                Math.Abs(fundEvent.GrossAmount),
                recallabilityClassifier?.Invoke(fundEvent) ?? DistributionRecallability.NonRecallable))
            .ToArray();

        return Build(commitment, events, expiryEvents);
    }

    private static bool Matches(InvestorCommitment commitment, PrivateCapitalFundEventDto fundEvent)
        => string.Equals(fundEvent.CapitalAccountId, commitment.CapitalAccountId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(fundEvent.Currency, commitment.Currency, StringComparison.OrdinalIgnoreCase)
           && (fundEvent.InvestorId is null
               || string.Equals(fundEvent.InvestorId, commitment.InvestorId, StringComparison.OrdinalIgnoreCase));

    private sealed record TimelineEntry(CommitmentActivityEvent? Activity, CommitmentExpiryEvent? Expiry)
    {
        public DateOnly EffectiveDate => Activity?.EffectiveDate ?? Expiry!.EffectiveDate;

        public string OrdinalId => Activity?.FundEventId ?? Expiry!.ExpiryEventId;
    }

    private static IEnumerable<TimelineEntry> OrderTimeline(
        InvestorCommitment commitment,
        IReadOnlyList<CommitmentActivityEvent> events,
        IReadOnlyList<CommitmentExpiryEvent>? expiryEvents,
        List<AccountingConfigurationValidationIssueDto> issues)
    {
        var timeline = events.Select(static item => new TimelineEntry(item, null)).ToList();
        foreach (var expiry in expiryEvents ?? [])
        {
            if (!string.Equals(expiry.CommitmentId, commitment.CommitmentId, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new AccountingConfigurationValidationIssueDto(
                    InvariantBreachIssueCode,
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Expiry event '{expiry.ExpiryEventId}' targets commitment '{expiry.CommitmentId}', not '{commitment.CommitmentId}'.",
                    commitment.CommitmentId,
                    "Route the expiry event to the commitment it belongs to."));
                continue;
            }

            timeline.Add(new TimelineEntry(null, expiry));
        }

        return timeline
            .OrderBy(static entry => entry.EffectiveDate)
            .ThenBy(static entry => entry.OrdinalId, StringComparer.Ordinal);
    }
}
