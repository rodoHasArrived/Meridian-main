using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.Ui.Shared.Services;

/// <summary>One partner taking part in a tiered waterfall allocation.</summary>
public sealed record PartnershipWaterfallInvestorRequest(string InvestorId, string DisplayName, decimal AllocationPercent);

/// <summary>One investor's share of a waterfall tier, as a fraction of the tier (0 &lt; percent ≤ 1).</summary>
public sealed record PartnershipWaterfallRuleRequest(string InvestorId, decimal AllocationPercent);

/// <summary>One ordered waterfall tier with an optional cap on the profit it absorbs.</summary>
public sealed record PartnershipWaterfallTierRequest(
    string TierId,
    string Description,
    IReadOnlyList<PartnershipWaterfallRuleRequest> AllocationRules,
    decimal? CapAmount = null);

/// <summary>
/// Request to allocate a soft-closed period's distributable profit from retained earnings to partners'
/// capital through ordered waterfall tiers. The allocation is projected server-side and may not exceed the
/// retained earnings the period's close left in the named account.
/// </summary>
public sealed record RunPartnershipWaterfallDraftIntakeRequest(
    string FundProfileId,
    Guid PeriodId,
    string Actor,
    decimal DistributableProfit,
    IReadOnlyList<PartnershipWaterfallInvestorRequest> Investors,
    IReadOnlyList<PartnershipWaterfallTierRequest> Tiers,
    string? RetainedEarningsFinancialAccountId = null,
    Guid? LedgerBookId = null,
    string? Description = null,
    string? EntityId = null,
    string? TenantId = null,
    string? CompanyId = null);

public sealed partial class AutomatedJournalIntakeRunner
{
    /// <summary>
    /// Projects the tiered allocation for a soft-closed period, refuses any allocation larger than the posted
    /// retained earnings it draws on, and queues the debit-retained-earnings / credit-partners'-capital draft as
    /// an adjusting entry for review, approval and posting. Nothing posts here.
    /// </summary>
    public async Task<AutomatedJournalIntakeRunResult> RunPartnershipWaterfallIntakeAsync(
        RunPartnershipWaterfallDraftIntakeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FundProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Actor);
        ArgumentNullException.ThrowIfNull(request.Investors);
        ArgumentNullException.ThrowIfNull(request.Tiers);
        if (_ledgerBookService is null)
        {
            throw new InvalidOperationException(
                "Partnership waterfall intake requires the ledger book service, which is not configured.");
        }

        var summary = await _ledgerBookService.GetPeriodSummaryAsync(request.PeriodId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Ledger period '{request.PeriodId}' was not found or is still open; close the period before allocating its profit.");
        if (request.LedgerBookId is { } requestedBook && requestedBook != summary.LedgerBookId)
        {
            throw new InvalidOperationException(
                $"Ledger period '{request.PeriodId}' belongs to book '{summary.LedgerBookId}', not the requested book '{requestedBook}'.");
        }

        var book = await _ledgerBookService.GetBookAsync(summary.LedgerBookId, ct).ConfigureAwait(false);
        if (book is null || !string.Equals(book.FundProfileId, request.FundProfileId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The ledger book is unavailable or does not belong to the requested fund profile.");
        }

        var period = (await _ledgerBookService
                .ListPeriodsAsync(new LedgerPeriodQuery(LedgerBookId: summary.LedgerBookId), ct)
                .ConfigureAwait(false))
            .FirstOrDefault(item => item.PeriodId == request.PeriodId)
            ?? throw new InvalidOperationException($"Ledger period '{request.PeriodId}' was not found in book '{summary.LedgerBookId}'.");
        if (period.Status != LedgerPeriodStatusDto.SoftClosed)
        {
            throw new InvalidOperationException(
                $"Ledger period '{period.Label}' must be soft-closed, with its closing entries posted to retained earnings, before its profit can be allocated; current status is {period.Status}.");
        }

        var retainedEarningsScope = string.IsNullOrWhiteSpace(request.RetainedEarningsFinancialAccountId)
            ? null
            : request.RetainedEarningsFinancialAccountId.Trim();
        var retainedEarnings = retainedEarningsScope is null
            ? LedgerAccounts.RetainedEarnings
            : LedgerAccounts.RetainedEarningsFor(retainedEarningsScope);
        var available = summary.TrialBalance
            .Where(line => string.Equals(line.AccountName, retainedEarnings.Name, StringComparison.Ordinal)
                && string.Equals(line.AccountType, nameof(LedgerAccountType.Equity), StringComparison.OrdinalIgnoreCase)
                && string.Equals(string.IsNullOrWhiteSpace(line.FinancialAccountId) ? null : line.FinancialAccountId.Trim(),
                    retainedEarningsScope, StringComparison.Ordinal))
            .Sum(static line => line.CreditTotal - line.DebitTotal);
        if (request.DistributableProfit > available)
        {
            throw new InvalidOperationException(
                $"The allocation of {request.DistributableProfit} exceeds the {available} of retained earnings the period's close left available to allocate.");
        }

        AutomatedJournalDraft draft;
        try
        {
            var input = new PartnershipWaterfallAllocationInput(
                request.FundProfileId,
                request.PeriodId.ToString("D"),
                new DateTimeOffset(period.EndDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero),
                request.DistributableProfit,
                request.Investors.Select(static investor =>
                    new PartnershipInvestor(investor.InvestorId, investor.DisplayName, investor.AllocationPercent)).ToArray(),
                request.Tiers.Select(static tier => new PartnershipWaterfallTier(
                    tier.TierId,
                    tier.Description,
                    (tier.AllocationRules ?? []).Select(static rule =>
                        new PartnershipWaterfallAllocationRule(rule.InvestorId, rule.AllocationPercent)).ToArray(),
                    tier.CapAmount)).ToArray(),
                request.Description)
            {
                RetainedEarningsAccount = retainedEarnings
            };
            var projection = PartnershipWaterfallProjector.Project(input);
            draft = PartnershipWaterfallDraftBuilder.BuildDraft(projection, period.EndDate, _timeProvider.GetUtcNow(), request.Actor);
        }
        catch (ArgumentException ex)
        {
            // Term and projection refusals describe the requested allocation, not a malformed call.
            throw new InvalidOperationException(ex.Message, ex);
        }

        var intake = await _intake.IntakeDraftsAsync(
                new AutomatedJournalPreparedDraftIntakeRequest(
                    request.FundProfileId,
                    book.BaseCurrency,
                    [draft],
                    request.Actor,
                    summary.LedgerBookId,
                    request.PeriodId.ToString("D"),
                    request.EntityId,
                    request.TenantId,
                    request.CompanyId),
                ct)
            .ConfigureAwait(false);
        return new AutomatedJournalIntakeRunResult([], intake);
    }
}
