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
        if (!plan.IsExecutable)
        {
            var criticalCount = plan.ValidationIssues.Count(static issue =>
                issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
            throw new InvalidOperationException(FormattableString.Invariant(
                $"Capital call '{plan.Request.CallId}' is not executable: allocated {plan.AllocatedAmount} of requested {plan.Request.AmountToCall} across {plan.Lines.Count} line(s) with {criticalCount} critical validation issue(s)."));
        }

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
                line.Commitment,
                line.InstallmentId,
                line.CallAmount,
                effectiveDate: plan.Request.NoticeDate,
                occurredAtUtc: occurredAtUtc,
                evidenceReferences: evidence));
        }

        return drafts;
    }
}
