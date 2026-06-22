using Meridian.Contracts.Ledger;

namespace Meridian.FinancialOperations.Ledger;

public interface IAccountingBasisProjectionSetService
{
    Task<AccountingBasisProjectionSetDto> BuildProjectionSetAsync(
        AccountingBasisProjectionSetRequestDto request,
        CancellationToken ct = default);
}

public sealed class AccountingBasisProjectionSetService : IAccountingBasisProjectionSetService
{
    private readonly IAccountingPostingCandidateService _candidateService;

    public AccountingBasisProjectionSetService(IAccountingPostingCandidateService candidateService)
    {
        _candidateService = candidateService ?? throw new ArgumentNullException(nameof(candidateService));
    }

    public async Task<AccountingBasisProjectionSetDto> BuildProjectionSetAsync(
        AccountingBasisProjectionSetRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        if (request.SourceEventId == Guid.Empty)
        {
            throw new ArgumentException("A basis projection set requires a source economic event id.", nameof(request));
        }

        if (request.Targets.Count == 0)
        {
            throw new ArgumentException("A basis projection set requires at least one accounting basis target.", nameof(request));
        }

        var fundProfileId = RequireText(request.FundProfileId, nameof(request.FundProfileId));
        var sourceEventType = RequireText(request.SourceEventType, nameof(request.SourceEventType));
        var currency = RequireText(request.Currency, nameof(request.Currency)).ToUpperInvariant();
        var actor = RequireText(request.Actor, nameof(request.Actor));
        var description = RequireText(request.Description, nameof(request.Description));
        var items = new List<AccountingBasisProjectionItemDto>(request.Targets.Count);

        foreach (var target in request.Targets)
        {
            ct.ThrowIfCancellationRequested();
            if (target.LedgerBookId == Guid.Empty)
            {
                throw new ArgumentException("Each basis projection target requires a ledger book id.", nameof(request));
            }

            if (target.PeriodId == Guid.Empty)
            {
                throw new ArgumentException("Each basis projection target requires an accounting period id.", nameof(request));
            }

            var candidate = await _candidateService.BuildCandidateAsync(
                    new PostingRuleJournalCandidateRequestDto(
                        FundProfileId: fundProfileId,
                        SourceEventType: sourceEventType,
                        EventAmount: request.EventAmount,
                        Currency: currency,
                        EffectiveDate: request.EffectiveDate,
                        Actor: actor,
                        AggregateId: target.LedgerBookId,
                        PeriodId: target.PeriodId,
                        AccountingTimestamp: request.AccountingTimestamp,
                        Description: description,
                        AccountingBasis: target.AccountingBasis,
                        LedgerBookId: target.LedgerBookId,
                        Dimensions: target.Dimensions,
                        CounterpartyId: NormalizeOptional(request.CounterpartyId),
                        InstrumentSymbol: NormalizeOptional(request.InstrumentSymbol),
                        CorrelationId: request.CorrelationId,
                        SourceEventId: request.SourceEventId,
                        PolicyId: NormalizeOptional(target.PolicyId),
                        TreatmentKind: target.TreatmentKind,
                        TreasuryContext: request.TreasuryContext,
                        EvidenceLinks: request.EvidenceLinks,
                        TenantId: NormalizeOptional(request.TenantId),
                        CompanyId: NormalizeOptional(request.CompanyId)),
                    ct)
                .ConfigureAwait(false);

            items.Add(new AccountingBasisProjectionItemDto(
                target.AccountingBasis,
                target.LedgerBookId,
                target.PeriodId,
                candidate,
                ClassifyStatus(candidate),
                FirstBlockingIssue(candidate)));
        }

        return new AccountingBasisProjectionSetDto(
            request.SourceEventId,
            fundProfileId,
            sourceEventType,
            request.EffectiveDate,
            request.EventAmount,
            currency,
            DateTimeOffset.UtcNow,
            items);
    }

    private static string ClassifyStatus(PostingRuleJournalCandidateResultDto candidate)
    {
        if (candidate.HasBlockingIssues)
        {
            return "Blocked";
        }

        if (candidate.CanPostWithoutAdditionalApproval)
        {
            return "AutoPostEligible";
        }

        if (candidate.CanSubmitForApproval)
        {
            return "ReadyForApproval";
        }

        return "ReviewRequired";
    }

    private static string? FirstBlockingIssue(PostingRuleJournalCandidateResultDto candidate)
        => candidate.Issues
            .Where(static issue => issue.BlocksCandidate)
            .OrderByDescending(static issue => issue.Severity)
            .ThenBy(static issue => issue.Code, StringComparer.OrdinalIgnoreCase)
            .Select(static issue => issue.Message)
            .FirstOrDefault();

    private static string RequireText(string? value, string parameterName)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{parameterName} is required.", parameterName)
            : value.Trim();

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
