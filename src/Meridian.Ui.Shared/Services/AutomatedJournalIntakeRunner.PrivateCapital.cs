using Meridian.Contracts.Ledger;
using Meridian.Contracts.Banking;
using Meridian.Ledger;

namespace Meridian.Ui.Shared.Services;

public sealed partial class AutomatedJournalIntakeRunner
{
    private async Task<PrivateCapitalCallDto> ResolveCapitalCallAsync(
        string? tenantId, string? companyId, string? entityId, string fundProfileId,
        Guid? ledgerBookId, string currency, string callId, CancellationToken ct)
    {
        if (_privateCapitalStore is null)
            throw new InvalidOperationException("Durable private-capital commitment and call authority is unavailable.");
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(companyId)
            || string.IsNullOrWhiteSpace(entityId) || ledgerBookId is null || ledgerBookId == Guid.Empty)
            throw new ArgumentException("Capital-call intake requires explicit tenant, company, entity and ledger-book scope.");
        var scope = PrivateCapitalAuthorityRules.NormalizeScope(new PrivateCapitalScopeDto(tenantId, companyId, entityId, fundProfileId, ledgerBookId.Value, currency));
        var call = await _privateCapitalStore.GetCallAsync(scope, callId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The approved capital-call plan was not retained in this scope.");
        if (call.Scope != scope || call.PlanHash != PrivateCapitalCallPlanHash.Compute(call))
            throw new InvalidOperationException("The retained capital-call plan has inconsistent authority scope.");
        return call;
    }

    private async Task<RunCapitalCallIssuanceDraftIntakeRequest> ResolveIssuanceTermsAsync(
        RunCapitalCallIssuanceDraftIntakeRequest request, PrivateCapitalCallDto call, CancellationToken ct)
    {
        if (request.AmountToCall != call.AmountToCall || request.NoticeDate != call.NoticeDate
            || request.DueDate != call.DueDate || request.PeriodId != call.PeriodId
            || request.AllocationBasis != CapitalCallAllocationBasis.ProRataByUncalled)
            throw new InvalidOperationException("Issuance inputs differ from the retained call plan; review a new preview.");
        var terms = new List<CapitalCallCommitmentInput>(call.Lines.Count);
        foreach (var line in call.Lines)
        {
            var commitment = await _privateCapitalStore!.GetCommitmentAsync(call.Scope, line.CommitmentId,
                line.CommitmentRevision, ct).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The retained call commitment revision is unavailable.");
            if (commitment.ApprovalStatus != PrivateCapitalCommitmentApprovalStatus.Approved
                || commitment.Scope != call.Scope || commitment.InvestorId != line.InvestorId
                || commitment.CapitalAccountId != line.CapitalAccountId)
                throw new InvalidOperationException("The retained call commitment is not approved for this scope.");
            terms.Add(new CapitalCallCommitmentInput(commitment.CommitmentId, commitment.CapitalAccountId,
                commitment.InvestorId, commitment.TotalCommitment, commitment.CommitmentDate,
                commitment.IsActive ? CommitmentStatus.Active : CommitmentStatus.Closed,
                commitment.InvestmentPeriodEndDate, commitment.EvidenceLinks));
        }
        if (request.Commitments is { Count: > 0 })
        {
            if (request.Commitments.Count != terms.Count || request.Commitments.Any(input => !terms.Any(term =>
                term.CommitmentId == input.CommitmentId && term.CapitalAccountId == input.CapitalAccountId
                && term.InvestorId == input.InvestorId && term.TotalCommitment == input.TotalCommitment
                && term.CommitmentDate == input.CommitmentDate && term.Status == input.Status
                && term.InvestmentPeriodEndDate == input.InvestmentPeriodEndDate)))
                throw new InvalidOperationException("Caller commitment terms differ from approved authority; totals are consistency checks only.");
        }
        return request with
        {
            Commitments = terms, Purpose = call.Purpose, CallId = call.CallId,
            FundProfileId = call.Scope.FundProfileId, TenantId = call.Scope.TenantId,
            CompanyId = call.Scope.CompanyId, EntityId = call.Scope.EntityId, Currency = call.Scope.Currency
        };
    }

    private static void ValidateFundingTerms(RunCapitalCallFundingDraftIntakeRequest request, PrivateCapitalCallDto call)
    {
        if (request.PeriodId != call.PeriodId)
            throw new InvalidOperationException("Funding must target the retained call's supported accounting period.");
        if (request.Fundings is not { Count: > 0 })
            throw new ArgumentException("At least one retained funding receipt is required.");
        foreach (var funding in request.Fundings ?? [])
        {
            if (string.IsNullOrWhiteSpace(funding.FundingReceiptId) || string.IsNullOrWhiteSpace(funding.BankLineId))
                throw new ArgumentException("Native capital-call funding requires stable receipt and retained bank-line identities.");
            PrivateCapitalAuthorityRules.Id(funding.FundingReceiptId, nameof(funding.FundingReceiptId));
            var line = call.Lines.SingleOrDefault(line => line.CommitmentId == funding.CommitmentId);
            if (line is null || line.InvestorId != funding.InvestorId || line.CapitalAccountId != funding.CapitalAccountId)
                throw new InvalidOperationException("Funding identity differs from the retained call allocation.");
        }
    }

    private static IReadOnlyList<AutomatedJournalDraft> BindCapitalDrafts(
        IReadOnlyList<AutomatedJournalDraft> drafts, PrivateCapitalCallDto call, string kind,
        IReadOnlyList<CapitalCallFundingInput>? fundings,
        IReadOnlyDictionary<string, BankTransactionDto>? bankTransactions = null)
        => drafts.Select(draft =>
        {
            var commitmentId = draft.Metadata.Tags?.GetValueOrDefault("commitmentId");
            var line = call.Lines.SingleOrDefault(line => line.CommitmentId == commitmentId)
                ?? throw new InvalidOperationException("Capital-call draft is not part of the retained allocation.");
            if (kind == "Issuance" && draft.Event.Amount != line.Amount)
                throw new InvalidOperationException("Capital-call capacity changed after preview; review a new allocation before drafting.");
            var funding = fundings?.Single(input => input.CommitmentId == line.CommitmentId);
            var authority = new PrivateCapitalPostingAuthorityDto(call.Scope, call.CallId, line.CommitmentId,
                line.CommitmentRevision, call.PlanHash, kind, draft.Event.Amount,
                draft.Metadata.EffectiveDate ?? throw new InvalidOperationException("Capital-call effective date is missing."),
                funding?.FundingReceiptId?.Trim().ToLowerInvariant(), funding?.BankLineId is { } bankId ? Guid.Parse(bankId).ToString("D") : null,
                funding is not null ? PrivateCapitalPostingAuthority.BankTransactionHash(bankTransactions![funding.CommitmentId]) : null);
            return PrivateCapitalPostingAuthority.Bind(draft, authority, line.InvestorId, line.CapitalAccountId);
        }).ToArray();

    private async Task<IReadOnlyDictionary<string, BankTransactionDto>> ResolveFundingBankTransactionsAsync(
        RunCapitalCallFundingDraftIntakeRequest request, PrivateCapitalCallDto call, CancellationToken ct)
    {
        if (_privateCapitalBankSource is null || !Guid.TryParse(call.Scope.EntityId, out var entityId))
            throw new InvalidOperationException("Authoritative entity-scoped bank transactions are unavailable for capital-call funding.");
        var sources = await _privateCapitalBankSource.GetBankTransactionsAsync(entityId, ct).ConfigureAwait(false);
        var result = new Dictionary<string, BankTransactionDto>(StringComparer.OrdinalIgnoreCase);
        var usedBankIds = new HashSet<Guid>();
        foreach (var funding in request.Fundings)
        {
            if (!Guid.TryParse(funding.BankLineId, out var sourceId) || !usedBankIds.Add(sourceId))
                throw new ArgumentException("Every funding line requires a distinct retained bank transaction id.");
            var candidates = sources.Where(source => source.BankTransactionId == sourceId).ToArray();
            if (candidates.Length != 1 || candidates[0].EntityId != entityId || candidates[0].IsVoided
                || candidates[0].TransactionType != "CapitalCallFunding"
                || candidates[0].Amount != funding.FundedAmount || candidates[0].Currency != call.Scope.Currency
                || candidates[0].EffectiveDate != request.ReceivedDate)
                throw new InvalidOperationException("Retained bank evidence does not match the funding entity, amount, currency and received date.");
            result.Add(funding.CommitmentId, candidates[0]);
        }
        return result;
    }
}
