using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// Server-owned bounds for evidence that can support an automated accounting draft. Request
/// thresholds may strengthen these controls, but cannot weaken the confidence floor or variance
/// cap selected by the host.
/// </summary>
public sealed record AutomatedJournalEvidencePolicy(
    decimal MinimumCapitalAccountConfidence,
    decimal MaximumCapitalAccountVarianceTolerance)
{
    public static AutomatedJournalEvidencePolicy Default { get; } = new(
        MinimumCapitalAccountConfidence: 0.90m,
        MaximumCapitalAccountVarianceTolerance: 0.01m);

    public decimal ResolveMinimumCapitalAccountConfidence(decimal requestedMinimum)
    {
        if (requestedMinimum is < 0m or > 1m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedMinimum),
                "Capital-account confidence threshold must be between 0 and 1.");
        }

        if (MinimumCapitalAccountConfidence is < 0m or > 1m)
        {
            throw new InvalidOperationException(
                "The server-owned capital-account confidence policy must be between 0 and 1.");
        }

        return Math.Max(requestedMinimum, MinimumCapitalAccountConfidence);
    }

    public decimal ResolveMaximumCapitalAccountVarianceTolerance(decimal retainedTolerance)
    {
        if (retainedTolerance < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retainedTolerance),
                "Capital-account reconciliation tolerance cannot be negative.");
        }

        if (MaximumCapitalAccountVarianceTolerance < 0m)
        {
            throw new InvalidOperationException(
                "The server-owned capital-account variance tolerance cannot be negative.");
        }

        return Math.Min(retainedTolerance, MaximumCapitalAccountVarianceTolerance);
    }
}

internal sealed record AutomatedJournalFeeEvidenceEvaluation(
    bool IsReady,
    AutomatedJournalScheduleStateDto FailureState,
    AutomatedJournalEvidenceAssessmentDto Assessment,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<OperationsEvidenceLinkDto> EvidenceLinks);

/// <summary>
/// The single fee-basis admission gate used by both direct intake and recurring schedules.
/// Missing evidence blocks; reviewed-but-mismatched or low-confidence evidence requires
/// investigation. The evaluator never trusts a caller-supplied threshold below server policy.
/// </summary>
internal static class AutomatedJournalFeeEvidenceEvaluator
{
    public static AutomatedJournalFeeEvidenceEvaluation Evaluate(
        string periodId,
        string currency,
        decimal? beginningNav,
        decimal? endingNavBeforeFees,
        decimal? highWaterMark,
        AutomatedJournalCapitalAccountReconciliationDto? reconciliation,
        decimal requestedMinimumConfidence,
        DateTimeOffset evaluatedAtUtc,
        AutomatedJournalEvidencePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var minimumConfidence = policy.ResolveMinimumCapitalAccountConfidence(requestedMinimumConfidence);
        var missing = new List<string>();
        var mismatches = new List<string>();
        if (!beginningNav.HasValue)
            missing.Add("Beginning NAV is missing for the fee-accrual cycle.");
        if (!endingNavBeforeFees.HasValue)
            missing.Add("Ending NAV before fees is missing for the fee-accrual cycle.");
        if (!highWaterMark.HasValue)
            missing.Add("High-water mark is missing for the fee-accrual cycle.");
        if (reconciliation is null)
        {
            missing.Add("Reviewed capital-account reconciliation evidence is missing for the fee-accrual cycle.");
            return Build(false, AutomatedJournalScheduleStateDto.Blocked, 0m, [], missing, mismatches, minimumConfidence, policy.MaximumCapitalAccountVarianceTolerance);
        }

        if (string.IsNullOrWhiteSpace(reconciliation.ReconciliationId))
            missing.Add("Capital-account reconciliation id is missing.");
        if (reconciliation.EvidenceLinks.Count == 0 ||
            reconciliation.EvidenceLinks.All(static link => string.IsNullOrWhiteSpace(link.Route)))
            missing.Add("Capital-account reconciliation evidence links are missing.");
        if (string.IsNullOrWhiteSpace(reconciliation.SourceVersion))
            missing.Add("Capital-account reconciliation source version is missing.");
        if (string.IsNullOrWhiteSpace(reconciliation.ReviewedBy))
            missing.Add("Capital-account reconciliation reviewer is missing.");
        if (reconciliation.ReviewedAtUtc == default)
            missing.Add("Capital-account reconciliation review time is missing.");
        else if (reconciliation.ReviewedAtUtc.ToUniversalTime() > evaluatedAtUtc.ToUniversalTime())
            mismatches.Add("Capital-account reconciliation review time is later than the actual execution evaluation time.");

        if (!string.Equals(reconciliation.PeriodId, periodId, StringComparison.OrdinalIgnoreCase))
            mismatches.Add($"Capital-account reconciliation period '{reconciliation.PeriodId}' does not match schedule period '{periodId}'.");
        if (!string.Equals(reconciliation.Currency, currency, StringComparison.OrdinalIgnoreCase))
            mismatches.Add($"Capital-account reconciliation currency '{reconciliation.Currency}' does not match schedule currency '{currency}'.");
        if (beginningNav.HasValue && beginningNav.Value != reconciliation.ReconciledBeginningNav)
            mismatches.Add("Scheduled beginning NAV does not match the reviewed capital-account reconciliation.");
        if (endingNavBeforeFees.HasValue && endingNavBeforeFees.Value != reconciliation.ReconciledEndingNavBeforeFees)
            mismatches.Add("Scheduled ending NAV before fees does not match the reviewed capital-account reconciliation.");
        if (highWaterMark.HasValue && highWaterMark.Value != reconciliation.ReconciledHighWaterMark)
            mismatches.Add("Scheduled high-water mark does not match the reviewed capital-account reconciliation.");
        if (reconciliation.ConfidenceScore is < 0m or > 1m)
            mismatches.Add("Capital-account reconciliation confidence must be between 0% and 100%.");
        if (reconciliation.ReconciledBeginningNav < 0m ||
            reconciliation.ReconciledEndingNavBeforeFees < 0m ||
            reconciliation.ReconciledHighWaterMark < 0m ||
            reconciliation.CapitalAccountOpeningBalance < 0m ||
            reconciliation.CapitalAccountEndingBalanceBeforeFees < 0m ||
            reconciliation.CapitalAccountHighWaterMark < 0m)
        {
            mismatches.Add("Capital-account reconciliation balances cannot be negative.");
        }

        var maximumObservedVariance = new[]
        {
            decimal.Abs(reconciliation.ReconciledBeginningNav - reconciliation.CapitalAccountOpeningBalance),
            decimal.Abs(reconciliation.ReconciledEndingNavBeforeFees - reconciliation.CapitalAccountEndingBalanceBeforeFees),
            decimal.Abs(reconciliation.ReconciledHighWaterMark - reconciliation.CapitalAccountHighWaterMark)
        }.Max();
        var effectiveVarianceTolerance = reconciliation.MaximumVarianceTolerance < 0m
            ? policy.MaximumCapitalAccountVarianceTolerance
            : policy.ResolveMaximumCapitalAccountVarianceTolerance(reconciliation.MaximumVarianceTolerance);
        if (reconciliation.MaximumVarianceTolerance < 0m)
            mismatches.Add("Capital-account reconciliation tolerance cannot be negative.");
        if (!reconciliation.IsReconciled)
            mismatches.Add("Capital-account reconciliation is not marked reconciled.");
        if (maximumObservedVariance > effectiveVarianceTolerance)
        {
            mismatches.Add(FormattableString.Invariant(
                $"Capital-account reconciliation variance {maximumObservedVariance:0.00} exceeds the server-governed tolerance {effectiveVarianceTolerance:0.00}."));
        }
        if (reconciliation.ConfidenceScore < minimumConfidence)
        {
            mismatches.Add(FormattableString.Invariant(
                $"Capital-account reconciliation confidence {reconciliation.ConfidenceScore:P0} is below the server-governed {minimumConfidence:P0} threshold."));
        }

        var ready = missing.Count == 0 && mismatches.Count == 0;
        return Build(
            ready,
            missing.Count > 0 ? AutomatedJournalScheduleStateDto.Blocked : AutomatedJournalScheduleStateDto.NeedsInvestigation,
            reconciliation.ConfidenceScore,
            reconciliation.EvidenceLinks,
            missing,
            mismatches,
            minimumConfidence,
            effectiveVarianceTolerance);
    }

    private static AutomatedJournalFeeEvidenceEvaluation Build(
        bool isReady,
        AutomatedJournalScheduleStateDto failureState,
        decimal confidence,
        IReadOnlyList<OperationsEvidenceLinkDto> evidenceLinks,
        IReadOnlyList<string> missing,
        IReadOnlyList<string> mismatches,
        decimal minimumConfidence,
        decimal maximumVarianceTolerance)
    {
        var blockers = missing.Concat(mismatches).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var quality = confidence >= 0.90m
            ? AutomatedJournalEvidenceQualityDto.High
            : confidence >= minimumConfidence
                ? AutomatedJournalEvidenceQualityDto.Medium
                : AutomatedJournalEvidenceQualityDto.Low;
        var summary = isReady
            ? FormattableString.Invariant(
                $"Capital-account reconciliation confidence {confidence:P0} satisfies the server-governed {minimumConfidence:P0} threshold and the fee basis ties within {maximumVarianceTolerance:0.00}.")
            : $"Fee-accrual preparation cannot enter approval: {string.Join(" ", blockers)}";
        var assessment = new AutomatedJournalEvidenceAssessmentDto(
            "capital-account-reconciliation-confidence",
            confidence,
            quality,
            RequiresInvestigation: !isReady,
            summary,
            blockers,
            evidenceLinks.Select(static link => link.Route).ToArray());
        return new AutomatedJournalFeeEvidenceEvaluation(
            isReady,
            failureState,
            assessment,
            blockers,
            evidenceLinks);
    }
}
