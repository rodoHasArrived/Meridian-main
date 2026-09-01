using Meridian.Contracts.Ledger;

namespace Meridian.Ledger;

/// <summary>
/// Composes a planned fund-level capital call into the set of governed capital-call issuance drafts:
/// one balanced <see cref="AutomatedJournalDraft"/> per investor plan line, built through
/// <see cref="CapitalCallDraftFactory"/> so every draft carries a deterministic idempotency key and
/// flows through the standard <see cref="AutomatedJournalApproval"/> submit → approve → post
/// lifecycle. This closes the wiring gap between <see cref="CapitalCallPlanBuilder"/> — which
/// apportions a fund-level call across the commitment register — and the governed journal, which
/// previously had no caller turning the plan into postings.
/// </summary>
public static class CapitalCallScheduleDraftBuilder
{
    /// <summary>
    /// Builds and returns the governed issuance drafts for a fund-level call, planning it over the
    /// supplied commitment roll-forwards first. Fails closed when the plan is not executable.
    /// </summary>
    public static IReadOnlyList<AutomatedJournalDraft> BuildIssuanceDrafts(
        CapitalCallPlanRequest request,
        DateTimeOffset occurredAtUtc,
        IReadOnlyDictionary<string, IReadOnlyList<JournalEvidenceReference>>? evidenceByCommitmentId = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return BuildIssuanceDrafts(CapitalCallPlanBuilder.Build(request), occurredAtUtc, evidenceByCommitmentId);
    }

    /// <summary>
    /// Builds the governed issuance drafts for an already-planned capital call. Each executable plan
    /// line becomes one balanced <c>Dr capital-call receivable / Cr investor capital</c> draft, in a
    /// deterministic per-investor order. Throws when the plan is not executable (allocated amount does
    /// not tie to the requested amount, no lines survive, or a critical validation issue is present)
    /// so a broken commitment state can never silently issue notices or postings.
    /// </summary>
    /// <param name="plan">The apportioned capital-call plan produced by <see cref="CapitalCallPlanBuilder"/>.</param>
    /// <param name="occurredAtUtc">Wall-clock time the drafts are produced (audit/occurrence stamp).</param>
    /// <param name="evidenceByCommitmentId">
    /// Optional retained evidence references keyed by commitment id, attached to the matching draft.
    /// </param>
    public static IReadOnlyList<AutomatedJournalDraft> BuildIssuanceDrafts(
        CapitalCallPlan plan,
        DateTimeOffset occurredAtUtc,
        IReadOnlyDictionary<string, IReadOnlyList<JournalEvidenceReference>>? evidenceByCommitmentId = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var commitmentsById = ValidateExecutablePlan(plan);

        var drafts = new List<AutomatedJournalDraft>(plan.Lines.Count);
        foreach (var line in plan.Lines
            .OrderBy(static line => line.Commitment.InvestorId, StringComparer.Ordinal)
            .ThenBy(static line => line.InstallmentId, StringComparer.Ordinal))
        {
            var evidence = evidenceByCommitmentId is not null
                && evidenceByCommitmentId.TryGetValue(line.Commitment.CommitmentId, out var references)
                    ? references
                    : null;

            drafts.Add(CapitalCallDraftFactory.BuildCapitalCallDraft(
                commitmentsById[line.Commitment.CommitmentId],
                line.InstallmentId,
                line.CallAmount,
                effectiveDate: plan.Request.NoticeDate,
                occurredAtUtc: occurredAtUtc,
                evidenceReferences: evidence));
        }

        return drafts;
    }

    private static IReadOnlyDictionary<string, InvestorCommitment> ValidateExecutablePlan(CapitalCallPlan plan)
    {
        if (!plan.IsExecutable)
        {
            var criticalCount = plan.ValidationIssues?.Count(static issue =>
                issue.Severity == AccountingConfigurationValidationSeverityDto.Critical) ?? 0;
            throw new InvalidOperationException(FormattableString.Invariant(
                $"Capital call '{plan.Request?.CallId}' is not executable: allocated {plan.AllocatedAmount} across {plan.Lines?.Count ?? 0} line(s) with {criticalCount} critical validation issue(s)."));
        }

        var rollForwardsByCommitmentId = plan.Request.RollForwards.ToDictionary(
            static rollForward => rollForward.CommitmentId,
            static rollForward => rollForward,
            StringComparer.OrdinalIgnoreCase);
        var seenCommitments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenInstallments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in plan.Lines)
        {
            if (string.IsNullOrWhiteSpace(line.InstallmentId)
                || line.Commitment is null
                || line.CallAmount <= 0m
                || !seenCommitments.Add(line.Commitment.CommitmentId)
                || !seenInstallments.Add(line.InstallmentId.Trim())
                || !rollForwardsByCommitmentId.TryGetValue(line.Commitment.CommitmentId, out var rollForward)
                || !Equals(line.Commitment, rollForward.Commitment)
                || !rollForward.InvariantHolds
                || !rollForward.Commitment.IsCallable
                || line.CallAmount > rollForward.Uncalled
                || !string.Equals(line.Commitment.FundProfileId, plan.Request.FundProfileId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(FormattableString.Invariant(
                    $"Capital call '{plan.Request.CallId}' contains an invalid plan line and cannot issue drafts."));
            }
        }

        return rollForwardsByCommitmentId.ToDictionary(
            static pair => pair.Key,
            static pair => pair.Value.Commitment,
            StringComparer.OrdinalIgnoreCase);
    }
}
