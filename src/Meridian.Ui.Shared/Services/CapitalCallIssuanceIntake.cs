using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// One operator-attested commitment-register line backing a capital call. The total commitment is
/// the operator's attestation (there is no server commitment store yet), so each line must carry
/// retained register evidence; the called-to-date state is never taken from the caller — it is
/// recomputed server-side from posted private-capital fund events before any draft is produced.
/// </summary>
public sealed record CapitalCallCommitmentInput(
    string CommitmentId,
    string CapitalAccountId,
    string InvestorId,
    decimal TotalCommitment,
    DateOnly CommitmentDate,
    CommitmentStatus Status = CommitmentStatus.Active,
    DateOnly? InvestmentPeriodEndDate = null,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } = EvidenceLinks ?? [];
}

/// <summary>
/// Request to plan a fund-level capital call over the supplied commitment register and land the
/// per-LP issuance drafts in the manual journal workbench queue. Drafts flow through the same
/// governed submit → approve → post lifecycle as every other automated intake lane; this run
/// never posts anything.
/// </summary>
public sealed record RunCapitalCallIssuanceDraftIntakeRequest(
    string FundProfileId,
    string Currency,
    string Actor,
    string CallId,
    decimal AmountToCall,
    DateOnly NoticeDate,
    DateOnly DueDate,
    IReadOnlyList<CapitalCallCommitmentInput> Commitments,
    CapitalCallAllocationBasis AllocationBasis = CapitalCallAllocationBasis.ProRataByUncalled,
    string? Purpose = null,
    Guid? LedgerBookId = null,
    string? PeriodId = null,
    string? EntityId = null,
    string? TenantId = null,
    string? CompanyId = null,
    DateTimeOffset? AsOf = null);

/// <summary>Outcome of planning one capital call into governed issuance drafts.</summary>
internal sealed record CapitalCallIssuanceProduction(
    bool IsReady,
    AutomatedJournalIntakeReadiness Readiness,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<AutomatedJournalDraft> Drafts,
    IReadOnlyDictionary<string, AutomatedJournalEvidenceAssessmentDto> EvidenceAssessments,
    IReadOnlyList<AutomatedJournalEventProductionSkip> Skipped);

/// <summary>
/// Turns a validated capital-call request into governed issuance drafts through the fund-economics
/// kernel (<see cref="CapitalCallPlanBuilder"/> → <see cref="CapitalCallScheduleDraftBuilder"/>),
/// failing closed on everything the tie-out discipline demands:
/// <list type="bullet">
/// <item>the uncalled basis is recomputed from posted private-capital fund events, never accepted
/// from the caller;</item>
/// <item>a commitment line without retained register evidence blocks the run;</item>
/// <item>a plan that is not executable (over-capacity call, invariant breach, critical roll-forward
/// issue) blocks the run with the kernel's own reasons instead of drafting wrong numbers.</item>
/// </list>
/// </summary>
internal static class CapitalCallIssuanceDraftProducer
{
    internal const string AssessmentCode = "capital-call-commitment-corroboration";

    public static string BuildRunAssessmentKey(string fundProfileId, string callId)
        => FormattableString.Invariant(
            $"capital-call|{fundProfileId.Trim().ToLowerInvariant()}|{callId.Trim().ToLowerInvariant()}");

    public static CapitalCallIssuanceProduction Produce(
        RunCapitalCallIssuanceDraftIntakeRequest request,
        IReadOnlyList<PrivateCapitalFundEventDto> postedFundEvents,
        DateTimeOffset evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(postedFundEvents);
        Validate(request);

        var blockers = new List<string>();
        var commitments = new List<(InvestorCommitment Commitment, CapitalCallCommitmentInput Input)>(request.Commitments.Count);
        foreach (var input in request.Commitments)
        {
            // InvestorCommitment enforces the structural invariants (positive total, ids, currency);
            // violations surface as ArgumentException → 400 at the endpoint.
            var commitment = new InvestorCommitment(
                input.CommitmentId,
                request.FundProfileId,
                request.LedgerBookId,
                input.CapitalAccountId,
                input.InvestorId,
                request.Currency,
                input.TotalCommitment,
                input.CommitmentDate,
                input.InvestmentPeriodEndDate,
                input.Status);
            commitments.Add((commitment, input));

            if (!input.EvidenceLinks.Any(static link => !string.IsNullOrWhiteSpace(link)))
            {
                blockers.Add(FormattableString.Invariant(
                    $"Commitment '{commitment.CommitmentId}' carries no retained commitment-register evidence; the attested total cannot back a capital call."));
            }
        }

        // Server-owned corroboration: fold each commitment's called-to-date state from the posted
        // private-capital fund events. Recallable distributions are treated as non-recallable here
        // (conservative: restoration never widens capacity without an explicit recall policy).
        var rollForwards = commitments
            .Select(pair => CommitmentRollForwardCalculator.BuildFromFundEvents(pair.Commitment, postedFundEvents))
            .ToArray();

        var planRequest = new CapitalCallPlanRequest(
            request.CallId,
            request.FundProfileId,
            request.AmountToCall,
            request.NoticeDate,
            request.DueDate,
            rollForwards,
            request.AllocationBasis,
            request.Purpose);
        var plan = CapitalCallPlanBuilder.Build(planRequest);

        var skipped = plan.ValidationIssues
            .Where(static issue => issue.Severity != AccountingConfigurationValidationSeverityDto.Critical)
            .Select(static issue => new AutomatedJournalEventProductionSkip(
                issue.TargetId ?? "(capital-call)",
                issue.Message))
            .ToArray();
        blockers.AddRange(plan.ValidationIssues
            .Where(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical)
            .Select(static issue => issue.Message));
        if (blockers.Count == 0 && !plan.IsExecutable)
        {
            blockers.Add(FormattableString.Invariant(
                $"Capital call '{plan.Request.CallId}' is not executable: allocated {plan.AllocatedAmount} of requested {plan.Request.AmountToCall} across {plan.Lines.Count} line(s)."));
        }

        var runKey = BuildRunAssessmentKey(request.FundProfileId, request.CallId);
        if (blockers.Count > 0)
        {
            var distinctBlockers = blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return new CapitalCallIssuanceProduction(
                IsReady: false,
                Readiness: AutomatedJournalIntakeReadiness.Blocked,
                Blockers: distinctBlockers,
                Drafts: [],
                EvidenceAssessments: new Dictionary<string, AutomatedJournalEvidenceAssessmentDto>(StringComparer.OrdinalIgnoreCase)
                {
                    [runKey] = new AutomatedJournalEvidenceAssessmentDto(
                        AssessmentCode,
                        ConfidenceScore: 0m,
                        Quality: AutomatedJournalEvidenceQualityDto.Low,
                        RequiresInvestigation: true,
                        Summary: $"Capital-call issuance cannot enter approval: {string.Join(" ", distinctBlockers)}",
                        Reasons: distinctBlockers)
                },
                Skipped: skipped);
        }

        var evidenceByCommitmentId = commitments.ToDictionary(
            static pair => pair.Commitment.CommitmentId,
            pair => BuildEvidenceReferences(pair.Commitment.CommitmentId, pair.Input.EvidenceLinks, request.Actor, evaluatedAtUtc),
            StringComparer.OrdinalIgnoreCase);
        var drafts = CapitalCallScheduleDraftBuilder.BuildIssuanceDrafts(plan, evaluatedAtUtc, evidenceByCommitmentId);

        var rollForwardByCommitmentId = rollForwards.ToDictionary(
            static rollForward => rollForward.CommitmentId,
            StringComparer.OrdinalIgnoreCase);
        var lineByInstallmentId = plan.Lines.ToDictionary(
            static line => line.InstallmentId,
            StringComparer.OrdinalIgnoreCase);
        var assessments = new Dictionary<string, AutomatedJournalEvidenceAssessmentDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var draft in drafts)
        {
            var idempotencyKey = draft.Metadata.IdempotencyKey;
            if (string.IsNullOrWhiteSpace(idempotencyKey) ||
                draft.Metadata.Tags is null ||
                !draft.Metadata.Tags.TryGetValue("commitmentId", out var commitmentId) ||
                !draft.Metadata.Tags.TryGetValue("drawdownInstallmentId", out var installmentId) ||
                !rollForwardByCommitmentId.TryGetValue(commitmentId, out var rollForward) ||
                !lineByInstallmentId.TryGetValue(installmentId, out var line))
            {
                continue;
            }

            assessments[idempotencyKey] = BuildDraftAssessment(
                request,
                rollForward,
                line,
                evidenceByCommitmentId[commitmentId]);
        }

        return new CapitalCallIssuanceProduction(
            IsReady: true,
            Readiness: AutomatedJournalIntakeReadiness.Ready,
            Blockers: [],
            Drafts: drafts,
            EvidenceAssessments: assessments,
            Skipped: skipped);
    }

    private static void Validate(RunCapitalCallIssuanceDraftIntakeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FundProfileId))
            throw new ArgumentException("Fund profile identifier is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Currency))
            throw new ArgumentException("Capital-call accounting currency is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Actor))
            throw new ArgumentException("Actor is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.CallId))
            throw new ArgumentException("Capital-call identifier is required.", nameof(request));
        if (request.AmountToCall <= 0m)
            throw new ArgumentOutOfRangeException(nameof(request), "Amount to call must be positive.");
        if (request.DueDate < request.NoticeDate)
            throw new ArgumentException("Capital-call due date cannot precede the notice date.", nameof(request));
        if (request.Commitments is null || request.Commitments.Count == 0)
            throw new ArgumentException("At least one commitment-register line is required.", nameof(request));
    }

    private static IReadOnlyList<JournalEvidenceReference> BuildEvidenceReferences(
        string commitmentId,
        IReadOnlyList<string> evidenceLinks,
        string retainedBy,
        DateTimeOffset retainedAtUtc)
        => evidenceLinks
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((link, index) => new JournalEvidenceReference(
                EvidenceId: FormattableString.Invariant($"commitment-register:{commitmentId}:{index + 1}"),
                Uri: link,
                Kind: "commitment-register",
                SourceSystem: "capital-call-intake",
                RetainedAtUtc: retainedAtUtc,
                RetainedBy: retainedBy,
                SubjectId: commitmentId))
            .ToArray();

    private static AutomatedJournalEvidenceAssessmentDto BuildDraftAssessment(
        RunCapitalCallIssuanceDraftIntakeRequest request,
        CommitmentRollForward rollForward,
        CapitalCallPlanLine line,
        IReadOnlyList<JournalEvidenceReference> evidenceReferences)
    {
        var postedSteps = rollForward.Steps.Count;
        var reasons = new List<string>();
        // Honesty grading: with posted history the server has corroborated the called-to-date
        // basis against the ledger; a first call rests solely on the operator-attested register,
        // which the retained evidence documents but nothing recomputes yet.
        var confidence = postedSteps > 0 ? 0.95m : 0.80m;
        if (postedSteps == 0)
        {
            reasons.Add(FormattableString.Invariant(
                $"No posted private-capital activity exists for capital account '{rollForward.CapitalAccountId}'; the operator-attested commitment register is the sole basis for this first call."));
        }

        var quality = confidence >= 0.90m
            ? AutomatedJournalEvidenceQualityDto.High
            : AutomatedJournalEvidenceQualityDto.Medium;
        var summary = FormattableString.Invariant(
            $"Server-recomputed uncalled commitment {rollForward.Uncalled:0.00} {request.Currency.Trim().ToUpperInvariant()} for '{rollForward.CommitmentId}' from {postedSteps} posted fund-event step(s); call {line.CallAmount:0.00} is {line.SharePercent:P2} of the requested {request.AmountToCall:0.00}.");
        return new AutomatedJournalEvidenceAssessmentDto(
            AssessmentCode,
            ConfidenceScore: confidence,
            Quality: quality,
            RequiresInvestigation: false,
            Summary: summary,
            Reasons: reasons,
            EvidenceLinks: evidenceReferences.Select(static reference => reference.Uri).ToArray());
    }
}
