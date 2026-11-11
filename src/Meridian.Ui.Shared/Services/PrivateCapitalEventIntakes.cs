using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.FinancialOperations.PrivateCapital;
using Meridian.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>
/// The default terms of one commitment on a capital call. The ledger holds the call and its
/// receipts but not the due date, default rate, day-count convention or grace period, so the
/// operator attests them here with retained evidence (the fund agreement or call notice).
/// </summary>
public sealed record CapitalCallDefaultTermsInput(
    string CommitmentId,
    string CapitalAccountId,
    string InvestorId,
    DateOnly DueDate,
    decimal DefaultInterestRateAnnual,
    DefaultInterestConvention Convention,
    int GraceDays,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } = EvidenceLinks ?? [];
}

/// <summary>Accrue default interest on the unfunded part of a posted capital call through a date.</summary>
public sealed record RunCapitalCallDefaultInterestDraftIntakeRequest(
    string FundProfileId,
    string Currency,
    string Actor,
    string CallId,
    DateOnly AccrueThrough,
    IReadOnlyList<CapitalCallDefaultTermsInput> Defaults,
    Guid? LedgerBookId = null,
    string? PeriodId = null,
    string? EntityId = null,
    string? TenantId = null,
    string? CompanyId = null,
    DateTimeOffset? AsOf = null);

/// <summary>One investor's share of a declared distribution and the capital it is measured against.</summary>
public sealed record DistributionInvestorInput(
    string InvestorId,
    string CapitalAccountId,
    decimal ContributedCapital,
    decimal PreferredReturnAccrued,
    decimal AmountToDistribute,
    IReadOnlyList<string>? EvidenceLinks = null)
{
    public IReadOnlyList<string> EvidenceLinks { get; init; } = EvidenceLinks ?? [];
}

/// <summary>Declare a distribution and split it through the European waterfall for each investor.</summary>
public sealed record RunDistributionDeclarationDraftIntakeRequest(
    string FundProfileId,
    string Currency,
    string Actor,
    string DistributionId,
    DateOnly EffectiveDate,
    decimal CarryRate,
    decimal CatchUpRate,
    IReadOnlyList<DistributionInvestorInput> Investors,
    Guid? LedgerBookId = null,
    string? PeriodId = null,
    string? EntityId = null,
    string? TenantId = null,
    string? CompanyId = null,
    DateTimeOffset? AsOf = null);

internal sealed record PrivateCapitalEventProduction(
    IReadOnlyList<string> Blockers,
    IReadOnlyList<AutomatedJournalDraft> Drafts,
    IReadOnlyDictionary<string, AutomatedJournalEvidenceAssessmentDto> EvidenceAssessments,
    IReadOnlyList<AutomatedJournalEventProductionSkip> Skipped);

/// <summary>
/// Builds default-interest drafts from a posted capital call. The called amount and the receipts
/// are folded from posted ledger lines; only the terms the ledger does not hold come from the
/// request. Interest is accrued to its cumulative amount through the requested date and only the
/// part not already posted is drafted, so accruing again later never books the same days twice.
/// An earlier default-interest draft still awaiting approval blocks a further accrual.
/// </summary>
internal static class CapitalCallDefaultInterestDraftProducer
{
    internal const string AssessmentCode = "capital-call-default-interest-terms";
    private const string CallReceivable = "Capital Call Receivable";
    private const string InterestReceivable = "Capital Call Default Interest Receivable";

    public static string BuildRunAssessmentKey(string fundProfileId, string callId)
        => FormattableString.Invariant(
            $"capital-call-default-interest|{fundProfileId.Trim().ToLowerInvariant()}|{callId.Trim().ToLowerInvariant()}");

    public static PrivateCapitalEventProduction Produce(
        RunCapitalCallDefaultInterestDraftIntakeRequest request,
        IReadOnlyList<PrivateCapitalFundEventDto> postedFundEvents,
        IReadOnlyList<PrivateCapitalLedgerImpactDto> ledgerImpacts,
        IReadOnlyList<ManualJournalEntryDraftDto> queueDrafts,
        DateTimeOffset evaluatedAtUtc)
    {
        Validate(request);
        var fund = request.FundProfileId.Trim();
        var callId = request.CallId.Trim();
        var currency = request.Currency.Trim().ToUpperInvariant();
        var blockers = new List<string>();
        var skipped = new List<AutomatedJournalEventProductionSkip>();
        var drafts = new List<AutomatedJournalDraft>();
        var assessments = new Dictionary<string, AutomatedJournalEvidenceAssessmentDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var input in request.Defaults.OrderBy(static line => line.InvestorId, StringComparer.Ordinal)
                     .ThenBy(static line => line.CommitmentId, StringComparer.Ordinal))
        {
            var commitmentId = input.CommitmentId.Trim();
            var investorId = input.InvestorId.Trim();
            var installmentId = FormattableString.Invariant($"{callId}:{commitmentId}");
            var callEventId = FormattableString.Invariant($"fund-event:{fund}:capital-call:{installmentId}");
            var evidenceLinks = input.EvidenceLinks.Where(static link => !string.IsNullOrWhiteSpace(link))
                .Select(static link => link.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (evidenceLinks.Length == 0)
            {
                blockers.Add($"Default terms for commitment '{commitmentId}' carry no retained evidence; an unattested rate or due date cannot accrue interest.");
                continue;
            }

            var issuance = postedFundEvents.FirstOrDefault(fundEvent => fundEvent.IsPosted &&
                fundEvent.EntryType == ManualJournalEntryTypeDto.CapitalCall &&
                string.Equals(fundEvent.FundEventId, callEventId, StringComparison.OrdinalIgnoreCase));
            if (issuance is null)
            {
                blockers.Add($"Capital call '{callId}' for commitment '{commitmentId}' has no posted issuance; interest cannot accrue on a receivable the ledger never raised.");
                continue;
            }
            if (!string.Equals(issuance.CapitalAccountId, input.CapitalAccountId.Trim(), StringComparison.OrdinalIgnoreCase) ||
                (issuance.InvestorId is not null && !string.Equals(issuance.InvestorId, investorId, StringComparison.OrdinalIgnoreCase)))
            {
                blockers.Add($"Commitment '{commitmentId}' names capital account '{input.CapitalAccountId}'/investor '{input.InvestorId}', but posted call '{callId}' belongs to '{issuance.CapitalAccountId}'/'{issuance.InvestorId}'.");
                continue;
            }
            if (!string.Equals(issuance.Currency, currency, StringComparison.OrdinalIgnoreCase))
            {
                blockers.Add($"Currency '{currency}' does not match posted call '{callId}' currency '{issuance.Currency}' for commitment '{commitmentId}'.");
                continue;
            }
            if (input.DueDate < issuance.EffectiveDate)
            {
                blockers.Add($"Due date {input.DueDate:yyyy-MM-dd} for commitment '{commitmentId}' precedes the posted call's notice date {issuance.EffectiveDate:yyyy-MM-dd}.");
                continue;
            }

            // The called amount and every receipt come from posted ledger lines, never the request.
            var called = 0m;
            var receipts = new List<CapitalCallFundingReceipt>();
            foreach (var impact in ledgerImpacts.Where(impact => IsSettled(impact) &&
                         string.Equals(impact.FundEventId, callEventId, StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var line in impact.Lines.Where(line => IsAccount(line, CallReceivable, investorId)))
                {
                    if (line.Side == AccountingTemplateLineSideDto.Debit)
                        called += line.Amount;
                    else if (line.Amount > 0m)
                        receipts.Add(new CapitalCallFundingReceipt(installmentId, impact.EffectiveDate, line.Amount));
                }
            }
            if (called <= 0m)
            {
                blockers.Add($"Posted call '{callId}' for commitment '{commitmentId}' carries no capital-call receivable debit; the defaulted amount cannot be corroborated.");
                continue;
            }

            var commitment = new InvestorCommitment(commitmentId, fund, request.LedgerBookId, input.CapitalAccountId.Trim(), investorId,
                currency, called, issuance.EffectiveDate, null, CommitmentStatus.Active,
                defaultInterestRateAnnual: input.DefaultInterestRateAnnual, defaultInterestConvention: input.Convention,
                defaultGraceDays: input.GraceDays);
            var installment = new DrawdownInstallment(installmentId, commitmentId, 1, issuance.EffectiveDate, input.DueDate,
                null, called, DrawdownInstallmentStatus.Called);
            var defaulted = DefaultInterestCalculator.Evaluate(commitment, [installment], receipts, request.AccrueThrough).SingleOrDefault();
            if (defaulted is null || defaulted.Accruals.Count == 0 || defaulted.AccruedInterest <= 0m)
            {
                skipped.Add(new(commitmentId, $"No default interest has accrued on call '{callId}' through {request.AccrueThrough:yyyy-MM-dd}."));
                continue;
            }

            var through = defaulted.Accruals[^1].AccrualTo;
            var keyPrefix = FormattableString.Invariant($"default-interest:{defaulted.DefaultId}:");
            var thisKey = FormattableString.Invariant($"{keyPrefix}{through:yyyyMMdd}");
            var pending = queueDrafts.FirstOrDefault(draft =>
                draft.TreasuryContext?.IdempotencyKey is { } key &&
                key.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(key, thisKey, StringComparison.OrdinalIgnoreCase) &&
                draft.Status is not (ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.CloseLocked
                    or ManualJournalEntryStatusDto.Rejected or ManualJournalEntryStatusDto.Reversed or ManualJournalEntryStatusDto.Rebooked));
            if (pending is not null)
            {
                blockers.Add($"An earlier default-interest draft for commitment '{commitmentId}' on call '{callId}' is {pending.Status}; post or reject it before accruing further, or its days would be accrued twice.");
                continue;
            }

            var interestEventPrefix = FormattableString.Invariant($"fund-event:{fund}:default-interest:{defaulted.DefaultId}:");
            var alreadyPosted = ledgerImpacts
                .Where(impact => IsSettled(impact) && impact.FundEventId.StartsWith(interestEventPrefix, StringComparison.OrdinalIgnoreCase))
                .SelectMany(static impact => impact.Lines)
                .Where(line => IsAccount(line, InterestReceivable, investorId))
                // A posted reversal keeps the accrual's fund event and credits the receivable back.
                .Sum(static line => line.Side == AccountingTemplateLineSideDto.Debit ? line.Amount : -line.Amount);
            var increment = defaulted.AccruedInterest - alreadyPosted;
            if (increment <= LedgerToleranceConstants.Balance)
            {
                skipped.Add(new(commitmentId, $"Default interest through {through:yyyy-MM-dd} ({defaulted.AccruedInterest:0.00}) is already posted ({alreadyPosted:0.00})."));
                continue;
            }

            var evidence = evidenceLinks.Select((link, index) => new JournalEvidenceReference(
                $"default-terms:{commitmentId}:{index + 1}", link, "commitment-default-terms",
                "capital-call-default-interest-intake", evaluatedAtUtc, request.Actor.Trim(), SubjectId: commitmentId)).ToArray();
            var accrual = new DefaultInterestAccrual(FormattableString.Invariant($"{defaulted.DefaultId}:through:{through:yyyyMMdd}"),
                defaulted.DefaultId, defaulted.Accruals[0].AccrualFrom, through, defaulted.DefaultedAmount,
                input.DefaultInterestRateAnnual, input.Convention, increment);
            var draft = CapitalCallDraftFactory.BuildDefaultInterestDraft(defaulted, accrual, evaluatedAtUtc, evidence);
            drafts.Add(draft);
            assessments[draft.Metadata.IdempotencyKey!] = new AutomatedJournalEvidenceAssessmentDto(AssessmentCode,
                ConfidenceScore: 0.85m, Quality: AutomatedJournalEvidenceQualityDto.High, RequiresInvestigation: false,
                Summary: FormattableString.Invariant(
                    $"Default interest on call '{callId}' for '{commitmentId}': {defaulted.AccruedInterest:0.00} {currency} accrued from {accrual.AccrualFrom:yyyy-MM-dd} through {through:yyyy-MM-dd} on {defaulted.DefaultedAmount:0.00} defaulted of {called:0.00} called, less {alreadyPosted:0.00} already posted; this draft books {increment:0.00}."),
                Reasons: ["The called amount and receipts are folded from posted ledger lines. The due date, rate, day-count convention and grace days are operator-attested terms backed by the retained evidence; the ledger does not hold them."],
                EvidenceLinks: evidenceLinks);
        }

        return new(blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), drafts, assessments, skipped);
    }

    private static void Validate(RunCapitalCallDefaultInterestDraftIntakeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FundProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Currency);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CallId);
        if (request.Defaults is null || request.Defaults.Count == 0)
            throw new ArgumentException("At least one commitment's default terms are required.", nameof(request));
        foreach (var input in request.Defaults)
        {
            if (input is null || string.IsNullOrWhiteSpace(input.CommitmentId) || string.IsNullOrWhiteSpace(input.CapitalAccountId) ||
                string.IsNullOrWhiteSpace(input.InvestorId))
                throw new ArgumentException("Each default-terms line needs a commitment, capital account and investor.", nameof(request));
            if (input.DefaultInterestRateAnnual is <= 0m or > 1m)
                throw new ArgumentException($"Default interest rate for commitment '{input.CommitmentId.Trim()}' must be greater than 0 and at most 1.", nameof(request));
            if (input.GraceDays < 0)
                throw new ArgumentException($"Grace days for commitment '{input.CommitmentId.Trim()}' cannot be negative.", nameof(request));
            if (!Enum.IsDefined(input.Convention))
                throw new ArgumentException($"Day-count convention for commitment '{input.CommitmentId.Trim()}' is not recognized.", nameof(request));
        }
        if (request.Defaults.GroupBy(static line => line.CommitmentId.Trim(), StringComparer.OrdinalIgnoreCase).Any(static group => group.Count() > 1))
            throw new ArgumentException("A commitment appears more than once in the default terms.", nameof(request));
    }

    private static bool IsSettled(PrivateCapitalLedgerImpactDto impact)
        => impact.ApprovalState is ManualJournalEntryStatusDto.Approved or ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.CloseLocked;

    private static bool IsAccount(PrivateCapitalLedgerLineImpactDto line, string accountName, string investorId)
    {
        var path = line.AccountPath?.Trim();
        if (string.IsNullOrEmpty(path))
            return false;
        var separator = path.LastIndexOf(':');
        var name = separator >= 0 ? path[(separator + 1)..].Trim() : path;
        return string.Equals(name, accountName, StringComparison.OrdinalIgnoreCase) &&
               (line.EntityId is null || string.Equals(line.EntityId, investorId, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Builds distribution drafts by running the European waterfall for each investor. The tiers paid
/// by the investor's earlier distributions come from the distribution register, so return of
/// capital, preferred return and GP catch-up are each paid once. An earlier distribution that is
/// not yet posted blocks the next one, because its tiers are not yet final; a rejected one is
/// ignored.
/// </summary>
internal static class DistributionDeclarationDraftProducer
{
    internal const string AssessmentCode = "distribution-waterfall-terms";

    public static string BuildRunAssessmentKey(string fundProfileId, string distributionId)
        => FormattableString.Invariant(
            $"distribution|{fundProfileId.Trim().ToLowerInvariant()}|{distributionId.Trim().ToLowerInvariant()}");

    public static string DraftKey(string fundProfileId, string investorId, string distributionId)
        => FormattableString.Invariant($"distribution:{fundProfileId.Trim()}:{investorId.Trim()}:{distributionId.Trim()}");

    public static (PrivateCapitalEventProduction Production, IReadOnlyList<DistributionDeclarationRecord> Records) Produce(
        RunDistributionDeclarationDraftIntakeRequest request,
        IReadOnlyDictionary<string, IReadOnlyList<DistributionDeclarationRecord>> registeredByInvestor,
        IReadOnlyList<ManualJournalEntryDraftDto> queueDrafts,
        DateTimeOffset evaluatedAtUtc)
    {
        Validate(request);
        var fund = request.FundProfileId.Trim();
        var distributionId = request.DistributionId.Trim();
        var currency = request.Currency.Trim().ToUpperInvariant();
        var periodId = string.IsNullOrWhiteSpace(request.PeriodId) ? request.EffectiveDate.ToString("yyyy-MM") : request.PeriodId.Trim();
        var blockers = new List<string>();
        var drafts = new List<AutomatedJournalDraft>();
        var records = new List<DistributionDeclarationRecord>();
        var assessments = new Dictionary<string, AutomatedJournalEvidenceAssessmentDto>(StringComparer.OrdinalIgnoreCase);
        var draftById = queueDrafts.GroupBy(static draft => draft.JournalEntryId).ToDictionary(static group => group.Key, static group => group.First());

        foreach (var input in request.Investors.OrderBy(static line => line.InvestorId, StringComparer.Ordinal))
        {
            var investorId = input.InvestorId.Trim();
            var evidenceLinks = input.EvidenceLinks.Where(static link => !string.IsNullOrWhiteSpace(link))
                .Select(static link => link.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (evidenceLinks.Length == 0)
            {
                blockers.Add($"Distribution to investor '{investorId}' carries no retained evidence for its capital, preferred return and amount.");
                continue;
            }

            var registered = registeredByInvestor.GetValueOrDefault(investorId) ?? [];
            // A draft admitted without its register record (an interrupted earlier request) holds
            // tiers this split cannot see. Retrying that declaration restores the record.
            var investorKeyPrefix = DraftKey(fund, investorId, string.Empty);
            var unregistered = queueDrafts.FirstOrDefault(draft =>
                draft.Status != ManualJournalEntryStatusDto.Rejected &&
                draft.TreasuryContext?.IdempotencyKey is { } draftKey &&
                draftKey.StartsWith(investorKeyPrefix, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(draftKey, DraftKey(fund, investorId, distributionId), StringComparison.OrdinalIgnoreCase) &&
                !registered.Any(record => string.Equals(DraftKey(fund, investorId, record.DistributionId), draftKey, StringComparison.OrdinalIgnoreCase)));
            if (unregistered is not null)
            {
                blockers.Add($"Distribution draft '{unregistered.TreasuryContext!.IdempotencyKey}' for investor '{investorId}' has no retained waterfall record; repeat that declaration to restore it before declaring another.");
                continue;
            }
            var same = registered.FirstOrDefault(record => string.Equals(record.DistributionId, distributionId, StringComparison.OrdinalIgnoreCase));
            var priors = new List<DistributionDeclarationRecord>();
            var blocked = false;
            foreach (var prior in registered.Where(record => !ReferenceEquals(record, same)))
            {
                var (consumed, problem) = ResolvePrior(prior, draftById.GetValueOrDefault(prior.JournalEntryId), queueDrafts, investorId);
                if (problem is not null)
                {
                    blockers.Add(problem);
                    blocked = true;
                }
                else if (consumed)
                {
                    priors.Add(prior);
                }
            }
            if (blocked)
                continue;

            var result = EuropeanDistributionWaterfall.Distribute(new EuropeanWaterfallInput(
                input.ContributedCapital, input.PreferredReturnAccrued, input.AmountToDistribute, request.CarryRate, request.CatchUpRate,
                priorReturnOfCapital: priors.Sum(static prior => prior.ReturnOfCapital),
                priorPreferredPaid: priors.Sum(static prior => prior.PreferredReturn),
                priorGpCatchUp: priors.Sum(static prior => prior.GpCatchUp),
                priorCatchUpPool: priors.Sum(static prior => prior.GpCatchUp + prior.LpCatchUp)));
            var evidence = evidenceLinks.Select((link, index) => new JournalEvidenceReference(
                $"distribution:{distributionId}:{investorId}:{index + 1}", link, "distribution-notice",
                "distribution-declaration-intake", evaluatedAtUtc, request.Actor.Trim(), SubjectId: investorId)).ToArray();
            var built = FundEconomicsJournalFactory.BuildDistributionWaterfallDraft(fund, investorId, result, request.EffectiveDate,
                evaluatedAtUtc, periodId, distributionId, evidence);
            // The approval flow requires a capital account on every private-capital fund event.
            var draft = built with { Metadata = built.Metadata with { CapitalAccountId = input.CapitalAccountId.Trim() } };
            var key = draft.Metadata.IdempotencyKey!;
            var record = new DistributionDeclarationRecord(request.TenantId?.Trim() ?? "", request.CompanyId?.Trim() ?? "", fund,
                request.LedgerBookId!.Value, investorId, distributionId, request.EffectiveDate, same?.JournalEntryId ?? Guid.Empty,
                input.ContributedCapital, input.PreferredReturnAccrued, input.AmountToDistribute, request.CarryRate, request.CatchUpRate,
                result.ReturnOfCapital, result.PreferredReturn, result.GpCatchUp, result.LpCatchUp, result.LpCarry, result.GpCarry,
                request.Actor.Trim(), evaluatedAtUtc);
            if (same is not null && same with { DeclaredBy = record.DeclaredBy, DeclaredAtUtc = record.DeclaredAtUtc } != record)
            {
                blockers.Add($"Distribution '{distributionId}' is already declared for investor '{investorId}' with different terms or tiers; use a new distribution id.");
                continue;
            }

            drafts.Add(draft);
            records.Add(record);
            assessments[key] = new AutomatedJournalEvidenceAssessmentDto(AssessmentCode,
                ConfidenceScore: 0.85m, Quality: AutomatedJournalEvidenceQualityDto.High, RequiresInvestigation: false,
                Summary: FormattableString.Invariant(
                    $"Distribution '{distributionId}' to '{investorId}': {result.Distributed:0.00} {currency} split as return of capital {result.ReturnOfCapital:0.00}, preferred {result.PreferredReturn:0.00}, GP catch-up {result.GpCatchUp:0.00}, LP catch-up {result.LpCatchUp:0.00}, LP carry {result.LpCarry:0.00}, GP carry {result.GpCarry:0.00}, after {priors.Count} earlier posted distribution(s)."),
                Reasons: ["Earlier tiers come from the distribution register for posted distributions. Contributed capital, preferred return accrued and the amount are operator-attested and backed by the retained evidence."],
                EvidenceLinks: evidenceLinks);
        }

        return (new(blockers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), drafts, assessments, []), records);
    }

    /// <summary>
    /// Whether an earlier declaration's tiers were paid. A rejected or fully reversed distribution
    /// paid nothing; a posted one (or a posted rebook of it) paid its tiers only while the posted
    /// amounts still equal the retained waterfall; anything still in flight blocks.
    /// </summary>
    private static (bool Consumed, string? Problem) ResolvePrior(DistributionDeclarationRecord prior, ManualJournalEntryDraftDto? draft,
        IReadOnlyList<ManualJournalEntryDraftDto> queueDrafts, string investorId)
    {
        for (var depth = 0; depth < 16; depth++)
        {
            switch (draft?.Status)
            {
                case ManualJournalEntryStatusDto.Rejected:
                    return (false, null);
                case ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.CloseLocked:
                    // A draft can be edited before it is posted.
                    var gpTotal = prior.GpCatchUp + prior.GpCarry;
                    var total = prior.ReturnOfCapital + prior.PreferredReturn + prior.LpCatchUp + prior.LpCarry + gpTotal;
                    var postedGp = draft.Lines.Where(static line => line.Side == AccountingTemplateLineSideDto.Debit && IsCarriedInterest(line.AccountPath))
                        .Sum(static line => line.Amount);
                    return Math.Abs(draft.TotalDebits - total) <= LedgerToleranceConstants.Balance &&
                           Math.Abs(postedGp - gpTotal) <= LedgerToleranceConstants.Balance
                        ? (true, null)
                        : (false, FormattableString.Invariant(
                            $"Posted distribution '{prior.DistributionId}' to investor '{investorId}' ({draft.TotalDebits:0.00}, GP {postedGp:0.00}) no longer matches its retained waterfall ({total:0.00}, GP {gpTotal:0.00}); its tiers cannot be relied on. Reverse it, post the reversal, and redeclare under a new distribution id."));
                case ManualJournalEntryStatusDto.Reversed:
                    var reversalId = draft.JournalEntryId;
                    var reversal = queueDrafts.FirstOrDefault(item => item.ReversalOfJournalEntryId == reversalId && item.Status != ManualJournalEntryStatusDto.Rejected);
                    return reversal?.Status is ManualJournalEntryStatusDto.Posted or ManualJournalEntryStatusDto.CloseLocked
                        ? (false, null)
                        : (false, $"Earlier distribution '{prior.DistributionId}' to investor '{investorId}' is being reversed; post its reversal first.");
                case ManualJournalEntryStatusDto.Rebooked:
                    // The rebooked journal stands in for the original; judge it the same way.
                    var rebookedId = draft.JournalEntryId;
                    draft = queueDrafts.FirstOrDefault(item => item.RebookedFromJournalEntryId == rebookedId && item.Status != ManualJournalEntryStatusDto.Rejected);
                    if (draft is null)
                        return (false, $"Earlier distribution '{prior.DistributionId}' to investor '{investorId}' was rebooked but its replacement journal is not in the queue.");
                    continue;
                default:
                    return (false, $"Earlier distribution '{prior.DistributionId}' to investor '{investorId}' is {(draft?.Status.ToString() ?? "not in the journal queue")}; post or reject it first, because this distribution's tiers depend on what it paid.");
            }
        }

        return (false, $"Earlier distribution '{prior.DistributionId}' to investor '{investorId}' has an unresolved correction chain.");
    }

    private static bool IsCarriedInterest(string? accountPath)
    {
        var path = accountPath?.Trim() ?? string.Empty;
        var separator = path.LastIndexOf(':');
        return string.Equals(separator >= 0 ? path[(separator + 1)..].Trim() : path, "Carried Interest Allocation", StringComparison.OrdinalIgnoreCase);
    }

    private static void Validate(RunDistributionDeclarationDraftIntakeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FundProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Currency);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DistributionId);
        if (request.LedgerBookId is null || request.LedgerBookId == Guid.Empty)
            throw new ArgumentException("A distribution is declared against one ledger book.", nameof(request));
        if (request.Investors is null || request.Investors.Count == 0)
            throw new ArgumentException("At least one investor is required.", nameof(request));
        foreach (var input in request.Investors)
        {
            if (input is null || string.IsNullOrWhiteSpace(input.InvestorId) || string.IsNullOrWhiteSpace(input.CapitalAccountId))
                throw new ArgumentException("Each investor line needs an investor and a capital account.", nameof(request));
            if (input.AmountToDistribute <= 0m)
                throw new ArgumentException($"The amount distributed to investor '{input.InvestorId.Trim()}' must be positive.", nameof(request));
        }
        if (request.Investors.GroupBy(static line => line.InvestorId.Trim(), StringComparer.OrdinalIgnoreCase).Any(static group => group.Count() > 1))
            throw new ArgumentException("An investor appears more than once in the distribution.", nameof(request));
    }
}
