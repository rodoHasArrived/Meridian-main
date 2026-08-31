using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// One operator-attested cash receipt against an issued capital call. The amount is what the LP
/// remitted (full or partial); the fundable ceiling is never taken from the caller — it is
/// recomputed server-side as posted issuance minus posted funding for the capital account before
/// any draft is produced. Each line must carry retained remittance/bank evidence.
/// </summary>
public sealed record CapitalCallFundingInput(
    string CommitmentId,
    string CapitalAccountId,
    string InvestorId,
    decimal FundedAmount,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } = EvidenceLinks ?? [];
}

/// <summary>
/// Request to record LP cash receipts against an issued capital call and land the per-LP funding
/// drafts (Dr Cash / Cr Capital Call Receivable) in the manual journal workbench queue. Drafts
/// flow through the same governed submit → approve → post lifecycle as every other automated
/// intake lane; this run never posts anything, and partial funding leaves the receivable open.
/// </summary>
public sealed record RunCapitalCallFundingDraftIntakeRequest(
    string FundProfileId,
    string Currency,
    string Actor,
    string CallId,
    DateOnly ReceivedDate,
    IReadOnlyList<CapitalCallFundingInput> Fundings,
    Guid? LedgerBookId = null,
    string? PeriodId = null,
    string? EntityId = null,
    string? TenantId = null,
    string? CompanyId = null,
    DateTimeOffset? AsOf = null);

/// <summary>Outcome of corroborating one funding run into governed settlement drafts.</summary>
internal sealed record CapitalCallFundingProduction(
    bool IsReady,
    AutomatedJournalIntakeReadiness Readiness,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<AutomatedJournalDraft> Drafts,
    IReadOnlyDictionary<string, AutomatedJournalEvidenceAssessmentDto> EvidenceAssessments,
    IReadOnlyList<AutomatedJournalEventProductionSkip> Skipped);

/// <summary>
/// Turns validated LP cash receipts into governed <c>CapitalCallFunded</c> drafts through the
/// fund-economics kernel (<see cref="CapitalCallDraftFactory.BuildCapitalCallFundingDraft"/>),
/// failing closed on everything the tie-out discipline demands:
/// <list type="bullet">
/// <item>a funding line must reference an issued call whose posted <c>CapitalCallIssued</c> event
/// the private-capital projection can see — a call the ledger never raised cannot be funded;</item>
/// <item>the fundable ceiling is the open receivable recomputed from posted ledger lines
/// (issuance debits minus funding credits on the LP's capital-call receivable), never the
/// caller's numbers; over-funding blocks the run with the computed amounts;</item>
/// <item>a funding line without retained remittance/bank evidence blocks the run.</item>
/// </list>
/// </summary>
internal static class CapitalCallFundingDraftProducer
{
    internal const string AssessmentCode = "capital-call-funding-corroboration";
    private const string ReceivableAccountName = "Capital Call Receivable";

    public static string BuildRunAssessmentKey(string fundProfileId, string callId)
        => FormattableString.Invariant(
            $"capital-call-funding|{fundProfileId.Trim().ToLowerInvariant()}|{callId.Trim().ToLowerInvariant()}");

    public static CapitalCallFundingProduction Produce(
        RunCapitalCallFundingDraftIntakeRequest request,
        IReadOnlyList<PrivateCapitalFundEventDto> postedFundEvents,
        IReadOnlyList<PrivateCapitalLedgerImpactDto> ledgerImpacts,
        DateTimeOffset evaluatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(postedFundEvents);
        ArgumentNullException.ThrowIfNull(ledgerImpacts);
        Validate(request);

        var fundProfileId = request.FundProfileId.Trim();
        var callId = request.CallId.Trim();
        var currency = request.Currency.Trim().ToUpperInvariant();
        var blockers = new List<string>();
        var corroborated = new List<CorroboratedFundingLine>(request.Fundings.Count);

        foreach (var input in request.Fundings
                     .OrderBy(static line => line.InvestorId, StringComparer.Ordinal)
                     .ThenBy(static line => line.CommitmentId, StringComparer.Ordinal))
        {
            var commitmentId = input.CommitmentId.Trim();
            var installmentId = FormattableString.Invariant($"{callId}:{commitmentId}");
            var fundEventId = FormattableString.Invariant(
                $"fund-event:{fundProfileId}:capital-call:{installmentId}");

            if (!input.EvidenceLinks.Any(static link => !string.IsNullOrWhiteSpace(link)))
            {
                blockers.Add(FormattableString.Invariant(
                    $"Funding for commitment '{commitmentId}' carries no retained funding evidence; an unattested cash receipt cannot relieve the capital-call receivable."));
            }

            // Server-owned corroboration gate 1: the call must exist as a POSTED CapitalCallIssued
            // fund event the projection reconstructs from the ledger. Drafts still in the approval
            // queue (IsPosted == false) never back funding.
            var issuance = postedFundEvents.FirstOrDefault(fundEvent =>
                fundEvent.IsPosted &&
                fundEvent.EntryType == ManualJournalEntryTypeDto.CapitalCall &&
                string.Equals(fundEvent.FundEventId, fundEventId, StringComparison.OrdinalIgnoreCase));
            if (issuance is null)
            {
                blockers.Add(FormattableString.Invariant(
                    $"Capital call '{callId}' for commitment '{commitmentId}' has no posted CapitalCallIssued event in the private-capital projection; funding cannot relieve a receivable the ledger never raised."));
                continue;
            }

            if (!string.Equals(issuance.CapitalAccountId, input.CapitalAccountId.Trim(), StringComparison.OrdinalIgnoreCase) ||
                (issuance.InvestorId is not null &&
                 !string.Equals(issuance.InvestorId, input.InvestorId.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                blockers.Add(FormattableString.Invariant(
                    $"Funding for commitment '{commitmentId}' names capital account '{input.CapitalAccountId}'/investor '{input.InvestorId}', but the posted call '{callId}' belongs to '{issuance.CapitalAccountId}'/'{issuance.InvestorId}'."));
                continue;
            }

            if (!string.Equals(issuance.Currency, currency, StringComparison.OrdinalIgnoreCase))
            {
                blockers.Add(FormattableString.Invariant(
                    $"Funding currency '{currency}' does not match posted call '{callId}' currency '{issuance.Currency}' for commitment '{commitmentId}'."));
                continue;
            }

            if (request.ReceivedDate < issuance.EffectiveDate)
            {
                blockers.Add(FormattableString.Invariant(
                    $"Funding received date {request.ReceivedDate:yyyy-MM-dd} precedes the posted call's effective date {issuance.EffectiveDate:yyyy-MM-dd} for commitment '{commitmentId}'."));
                continue;
            }

            // Server-owned corroboration gate 2: the fundable ceiling is the open receivable folded
            // from the call's posted ledger lines — issuance debits minus funding credits on the
            // LP's capital-call receivable. The projection maps posted ledger entries to Approved
            // and, once a fund event is posted, excludes that event's pending queue drafts; the
            // approval-state gate below keeps any leaked Draft/NeedsFix/Submitted impact out of
            // the fold so pending queue drafts never count as settled cash and identical re-runs
            // still reach the intake's ready-duplicate dedup.
            var issued = 0m;
            var alreadyFunded = 0m;
            foreach (var impact in ledgerImpacts.Where(impact =>
                         string.Equals(impact.FundEventId, fundEventId, StringComparison.OrdinalIgnoreCase) &&
                         impact.ApprovalState is ManualJournalEntryStatusDto.Approved
                             or ManualJournalEntryStatusDto.Posted
                             or ManualJournalEntryStatusDto.CloseLocked))
            {
                foreach (var line in impact.Lines.Where(line => IsReceivableLine(line, input.InvestorId)))
                {
                    if (line.Side == AccountingTemplateLineSideDto.Debit)
                        issued += line.Amount;
                    else
                        alreadyFunded += line.Amount;
                }
            }

            if (issued <= 0m)
            {
                blockers.Add(FormattableString.Invariant(
                    $"Posted call '{callId}' for commitment '{commitmentId}' carries no capital-call receivable debit in its ledger impacts; the fundable balance cannot be corroborated."));
                continue;
            }

            var openReceivable = issued - alreadyFunded;
            if (input.FundedAmount > openReceivable + LedgerToleranceConstants.Balance)
            {
                blockers.Add(FormattableString.Invariant(
                    $"Funding {input.FundedAmount} for commitment '{commitmentId}' exceeds the open capital-call receivable {openReceivable} on call '{callId}' (posted issuance {issued} minus posted funding {alreadyFunded})."));
                continue;
            }

            corroborated.Add(new CorroboratedFundingLine(
                input,
                commitmentId,
                installmentId,
                issuance,
                issued,
                alreadyFunded,
                openReceivable));
        }

        var runKey = BuildRunAssessmentKey(fundProfileId, callId);
        if (blockers.Count > 0)
        {
            var distinctBlockers = blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return new CapitalCallFundingProduction(
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
                        Summary: $"Capital-call funding cannot enter approval: {string.Join(" ", distinctBlockers)}",
                        Reasons: distinctBlockers)
                },
                Skipped: []);
        }

        var drafts = new List<AutomatedJournalDraft>(corroborated.Count);
        var assessments = new Dictionary<string, AutomatedJournalEvidenceAssessmentDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in corroborated)
        {
            // Identity-only carrier for the kernel draft factory: the ledger lines never read
            // TotalCommitment, so the server-corroborated posted issuance amount (positive by the
            // gates above) stands in for the register total instead of trusting a caller value.
            var commitment = new InvestorCommitment(
                line.CommitmentId,
                fundProfileId,
                request.LedgerBookId,
                line.Input.CapitalAccountId,
                line.Input.InvestorId,
                request.Currency,
                totalCommitment: line.Issued,
                commitmentDate: line.Issuance.EffectiveDate,
                investmentPeriodEndDate: null,
                status: CommitmentStatus.Active);
            var evidenceReferences = BuildEvidenceReferences(
                line.CommitmentId,
                line.Input.EvidenceLinks,
                request.Actor,
                evaluatedAtUtc);
            var draft = CapitalCallDraftFactory.BuildCapitalCallFundingDraft(
                commitment,
                line.InstallmentId,
                line.Input.FundedAmount,
                effectiveDate: request.ReceivedDate,
                occurredAtUtc: evaluatedAtUtc,
                evidenceReferences: evidenceReferences);
            drafts.Add(draft);

            if (!string.IsNullOrWhiteSpace(draft.Metadata.IdempotencyKey))
            {
                assessments[draft.Metadata.IdempotencyKey] = BuildDraftAssessment(
                    request,
                    line,
                    currency,
                    evidenceReferences);
            }
        }

        return new CapitalCallFundingProduction(
            IsReady: true,
            Readiness: AutomatedJournalIntakeReadiness.Ready,
            Blockers: [],
            Drafts: drafts,
            EvidenceAssessments: assessments,
            Skipped: []);
    }

    private sealed record CorroboratedFundingLine(
        CapitalCallFundingInput Input,
        string CommitmentId,
        string InstallmentId,
        PrivateCapitalFundEventDto Issuance,
        decimal Issued,
        decimal AlreadyFunded,
        decimal OpenReceivable);

    private static void Validate(RunCapitalCallFundingDraftIntakeRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FundProfileId))
            throw new ArgumentException("Fund profile identifier is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Currency))
            throw new ArgumentException("Capital-call funding currency is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Actor))
            throw new ArgumentException("Actor is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.CallId))
            throw new ArgumentException("Capital-call identifier is required.", nameof(request));
        if (request.Fundings is null || request.Fundings.Count == 0)
            throw new ArgumentException("At least one funding line is required.", nameof(request));

        var seenCommitments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in request.Fundings)
        {
            if (string.IsNullOrWhiteSpace(input.CommitmentId))
                throw new ArgumentException("Funding lines require a commitment identifier.", nameof(request));
            if (string.IsNullOrWhiteSpace(input.CapitalAccountId))
                throw new ArgumentException("Funding lines require a capital account identifier.", nameof(request));
            if (string.IsNullOrWhiteSpace(input.InvestorId))
                throw new ArgumentException("Funding lines require an investor identifier.", nameof(request));
            if (input.FundedAmount <= 0m)
                throw new ArgumentOutOfRangeException(nameof(request), "Funded amounts must be positive.");
            if (!seenCommitments.Add(input.CommitmentId.Trim()))
            {
                throw new ArgumentException(
                    $"Funding line for commitment '{input.CommitmentId.Trim()}' is duplicated; merge receipts per commitment before intake.",
                    nameof(request));
            }
        }
    }

    /// <summary>
    /// Matches the LP's capital-call receivable in a projected ledger line. Posted lines carry the
    /// ledger account name ("Capital Call Receivable") with the investor as the scoped entity;
    /// chart-mapped paths keep the name as the last segment. The exact-name match deliberately
    /// excludes the default-interest receivable.
    /// </summary>
    private static bool IsReceivableLine(PrivateCapitalLedgerLineImpactDto line, string investorId)
    {
        var path = line.AccountPath?.Trim();
        if (string.IsNullOrEmpty(path))
            return false;

        var separator = path.LastIndexOf(':');
        var accountName = separator >= 0 ? path[(separator + 1)..].Trim() : path;
        if (!string.Equals(accountName, ReceivableAccountName, StringComparison.OrdinalIgnoreCase))
            return false;

        return line.EntityId is null ||
               string.Equals(line.EntityId, investorId.Trim(), StringComparison.OrdinalIgnoreCase);
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
                EvidenceId: FormattableString.Invariant($"funding-receipt:{commitmentId}:{index + 1}"),
                Uri: link,
                Kind: "funding-receipt",
                SourceSystem: "capital-call-funding-intake",
                RetainedAtUtc: retainedAtUtc,
                RetainedBy: retainedBy,
                SubjectId: commitmentId))
            .ToArray();

    private static AutomatedJournalEvidenceAssessmentDto BuildDraftAssessment(
        RunCapitalCallFundingDraftIntakeRequest request,
        CorroboratedFundingLine line,
        string currency,
        IReadOnlyList<JournalEvidenceReference> evidenceReferences)
    {
        var remaining = line.OpenReceivable - line.Input.FundedAmount;
        // Honesty grading: the fundable ceiling is fully ledger-corroborated (posted issuance and
        // posted funding), but the cash arrival itself rests on the operator-retained remittance
        // evidence — no bank-feed corroboration is wired into this lane yet.
        var reasons = new List<string>
        {
            "The cash receipt itself rests on the retained remittance evidence; no bank-feed corroboration is wired into this lane."
        };
        if (remaining > LedgerToleranceConstants.Balance)
        {
            reasons.Add(FormattableString.Invariant(
                $"Partial funding: {remaining:0.00} {currency} of the capital-call receivable stays open after this draft posts."));
        }

        var summary = FormattableString.Invariant(
            $"Server-recomputed open capital-call receivable {line.OpenReceivable:0.00} {currency} for '{line.CommitmentId}' on call '{request.CallId.Trim()}' (posted issuance {line.Issued:0.00} minus posted funding {line.AlreadyFunded:0.00}); this draft relieves {line.Input.FundedAmount:0.00}, leaving {remaining:0.00} open.");
        return new AutomatedJournalEvidenceAssessmentDto(
            AssessmentCode,
            ConfidenceScore: 0.90m,
            Quality: AutomatedJournalEvidenceQualityDto.High,
            RequiresInvestigation: false,
            Summary: summary,
            Reasons: reasons,
            EvidenceLinks: evidenceReferences.Select(static reference => reference.Uri).ToArray());
    }
}
