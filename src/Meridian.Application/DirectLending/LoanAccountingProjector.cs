using System.Text.Json;
using Meridian.Application.Ledger;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.Storage.DirectLending;
using Meridian.Storage.Ledger;
using LedgerAccount = Meridian.Ledger.LedgerAccount;
using LedgerAccountType = Meridian.Ledger.LedgerAccountType;
using SecurityMasterQueryService = Meridian.Application.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Application.DirectLending;

public sealed class LoanAccountingProjector
{
    private readonly ILedgerJournalStore? _journalStore;
    private readonly IAccountingPolicyService _accountingPolicyService;
    private readonly SecurityMasterQueryService? _securityMasterQueryService;

    public LoanAccountingProjector(
        ILedgerJournalStore? journalStore,
        IAccountingPolicyService accountingPolicyService,
        SecurityMasterQueryService? securityMasterQueryService = null)
    {
        _journalStore = journalStore;
        _accountingPolicyService = accountingPolicyService;
        _securityMasterQueryService = securityMasterQueryService;
    }

    public async Task<IReadOnlyList<LedgerJournalEntryWrite>> ProjectAsync(
        Guid loanId,
        LoanContractDetailDto contract,
        string eventType,
        DateOnly? effectiveDate,
        JsonDocument payload,
        Guid sourceEventId,
        DirectLendingEventWriteMetadata metadata,
        CancellationToken ct,
        LedgerPostingKindDto postingKind = LedgerPostingKindDto.Originating,
        LedgerAdjustmentApprovalMetadataDto? adjustmentApproval = null)
    {
        if (_journalStore is null)
        {
            throw new DirectLendingCommandException(
                new DirectLendingCommandError(
                    DirectLendingErrorCode.Validation,
                    "Ledger journal store is required for direct-lending ledger-impacting events."));
        }

        var accountingDate = effectiveDate ?? contract.EffectiveDate;
        var period = await ResolvePostingPeriodAsync(accountingDate, postingKind, ct).ConfigureAwait(false);
        if (period is null)
        {
            throw new DirectLendingCommandException(
                new DirectLendingCommandError(
                    DirectLendingErrorCode.Validation,
                    $"No ledger accounting period accepts {postingKind} posting date '{accountingDate:yyyy-MM-dd}'."));
        }

        var policy = await _accountingPolicyService
            .ResolvePolicyAsync(new AccountingPolicyQuery(AccountingBasisKindDto.Primary, accountingDate), ct)
            .ConfigureAwait(false);

        var journalEntryId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(accountingDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var currency = contract.CurrentTerms.BaseCurrency.ToString();
        var lines = new List<LedgerEntry>();
        var description = eventType;
        var securityReference = contract.CurrentTerms.SecurityMasterReference;
        var instrumentSymbol = NormalizeSymbol(securityReference?.Symbol);

        void Add(string account, LedgerAccountType accountType, decimal debit, decimal credit)
        {
            if (debit <= 0m && credit <= 0m)
            {
                return;
            }

            lines.Add(new LedgerEntry(
                Guid.NewGuid(),
                journalEntryId,
                timestamp,
                new LedgerAccount(
                    account,
                    accountType,
                    Symbol: IsDirectLendingInstrumentAccount(account) ? instrumentSymbol : null,
                    FinancialAccountId: loanId.ToString("D")),
                debit,
                credit,
                description));
        }

        var root = payload.RootElement;
        switch (eventType)
        {
            case "loan.drawdown-booked":
                description = "Loan drawdown funding";
                var drawdownAmount = GetDecimal(root, "amount");
                Add("LoanPrincipal", LedgerAccountType.Asset, drawdownAmount, 0m);
                Add("Cash", LedgerAccountType.Asset, 0m, drawdownAmount);
                break;

            case "loan.daily-accrual-posted":
                description = "Loan daily accrual";
                var interest = GetDecimal(root, "interestAmount");
                var commitmentFee = GetDecimal(root, "commitmentFeeAmount");
                var penalty = GetDecimal(root, "penaltyAmount");
                Add("AccruedInterestReceivable", LedgerAccountType.Asset, interest, 0m);
                Add("InterestIncome", LedgerAccountType.Revenue, 0m, interest);
                Add("CommitmentFeeReceivable", LedgerAccountType.Asset, commitmentFee, 0m);
                Add("CommitmentFeeIncome", LedgerAccountType.Revenue, 0m, commitmentFee);
                Add("PenaltyReceivable", LedgerAccountType.Asset, penalty, 0m);
                Add("PenaltyIncome", LedgerAccountType.Revenue, 0m, penalty);
                break;

            case "loan.daily-accrual-reversed":
                description = "Loan daily accrual reversal";
                var reversedInterest = GetDecimal(root, "interestAmount");
                var reversedCommitmentFee = GetDecimal(root, "commitmentFeeAmount");
                var reversedPenalty = GetDecimal(root, "penaltyAmount");
                Add("InterestIncome", LedgerAccountType.Revenue, reversedInterest, 0m);
                Add("AccruedInterestReceivable", LedgerAccountType.Asset, 0m, reversedInterest);
                Add("CommitmentFeeIncome", LedgerAccountType.Revenue, reversedCommitmentFee, 0m);
                Add("CommitmentFeeReceivable", LedgerAccountType.Asset, 0m, reversedCommitmentFee);
                Add("PenaltyIncome", LedgerAccountType.Revenue, reversedPenalty, 0m);
                Add("PenaltyReceivable", LedgerAccountType.Asset, 0m, reversedPenalty);
                break;

            case "loan.principal-payment-applied":
                description = "Loan principal receipt";
                var appliedPrincipal = GetDecimal(root, "AppliedAmount", "appliedAmount", "amount");
                Add("Cash", LedgerAccountType.Asset, appliedPrincipal, 0m);
                Add("LoanPrincipal", LedgerAccountType.Asset, 0m, appliedPrincipal);
                break;

            case "loan.mixed-payment-applied":
                description = "Loan cash receipt";
                var paymentAmount = GetDecimal(root, "amount");
                Add("Cash", LedgerAccountType.Asset, paymentAmount, 0m);
                Add("LoanAndAccrualsClearing", LedgerAccountType.Asset, 0m, paymentAmount);
                break;

            case "loan.write-off-applied":
                description = "Loan write-off";
                var writeOffAmount = GetDecimal(root, "AppliedAmount", "appliedAmount", "Amount", "amount");
                Add("WriteOffExpense", LedgerAccountType.Expense, writeOffAmount, 0m);
                Add("LoanPrincipal", LedgerAccountType.Asset, 0m, writeOffAmount);
                break;

            case "loan.discount-premium-amortized":
            case "DiscountPremiumAmortized":
                description = "Loan discount/premium amortization";
                var discountAmortization = GetDecimal(root, "discountAmort", "discountAmortization");
                var premiumAmortization = GetDecimal(root, "premiumAmort", "premiumAmortization");
                Add("DiscountAmortizationExpense", LedgerAccountType.Expense, discountAmortization, 0m);
                Add("LoanDiscount", LedgerAccountType.Asset, 0m, discountAmortization);
                Add("LoanPremium", LedgerAccountType.Liability, premiumAmortization, 0m);
                Add("PremiumAmortizationIncome", LedgerAccountType.Revenue, 0m, premiumAmortization);
                break;

            case "loan.restructured":
            case "LoanRestructured":
                description = "Loan restructuring";
                var principalHaircut = GetDecimal(root, "PrincipalHaircut", "principalHaircut", "haircutAmount");
                Add("RestructuringLoss", LedgerAccountType.Expense, principalHaircut, 0m);
                Add("LoanPrincipal", LedgerAccountType.Asset, 0m, principalHaircut);
                break;
        }

        if (lines.Count == 0)
        {
            return [];
        }

        var securityLineage = await ResolveSecurityMasterPostingLineageAsync(securityReference, eventType, ct).ConfigureAwait(false);
        instrumentSymbol = securityLineage.Symbol;
        lines = ApplyAuthoritativeInstrumentSymbol(lines, instrumentSymbol);

        var entry = new JournalEntry(
            journalEntryId,
            timestamp,
            description,
            lines,
            new JournalEntryMetadata(
                ActivityType: eventType,
                Symbol: instrumentSymbol,
                SecurityId: securityLineage.SecurityId,
                FinancialAccountId: loanId.ToString("D"),
                Institution: "DirectLending",
                Tags: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["loanId"] = loanId.ToString("D"),
                    ["currency"] = currency,
                    ["sourceEventId"] = sourceEventId.ToString("D"),
                    ["sourceEventType"] = eventType,
                    ["securityMasterProvenance"] = securityLineage.Provenance,
                    ["securityMasterLineage"] = securityLineage.Lineage
                }));

        return
        [
            new LedgerJournalEntryWrite(
                entry,
                AggregateId: loanId,
                PeriodId: period.PeriodId,
                CommandId: metadata.CommandId,
                CorrelationId: metadata.CorrelationId,
                AccountingBasis: policy.AccountingBasis,
                AccountingPolicyId: policy.PolicyId,
                AccountingPolicyVersion: policy.Version,
                RuleId: eventType,
                RuleVersion: policy.Version,
                SourceEventId: sourceEventId,
                PostingKind: postingKind,
                AdjustmentApproval: adjustmentApproval)
        ];
    }

    private async Task<LedgerAccountingPeriod?> ResolvePostingPeriodAsync(
        DateOnly accountingDate,
        LedgerPostingKindDto postingKind,
        CancellationToken ct)
    {
        var periods = (await _journalStore!.ListPeriodsAsync(ct: ct).ConfigureAwait(false))
            .Where(period =>
                period.StartDate <= accountingDate &&
                period.EndDate >= accountingDate &&
                IsPostableForKind(period.Status, postingKind))
            .OrderBy(period => period.StartDate)
            .Take(2)
            .ToArray();

        if (periods.Length > 1)
        {
            throw new DirectLendingCommandException(
                new DirectLendingCommandError(
                    DirectLendingErrorCode.Validation,
                    $"Multiple open accounting periods contain posting date '{accountingDate:yyyy-MM-dd}'."));
        }

        return periods.SingleOrDefault();
    }

    private static bool IsPostableForKind(string status, LedgerPostingKindDto postingKind)
    {
        if (string.Equals(status, "Open", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return postingKind == LedgerPostingKindDto.Adjustment &&
               string.Equals(status, "SoftClosed", StringComparison.OrdinalIgnoreCase);
    }

    private static List<LedgerEntry> ApplyAuthoritativeInstrumentSymbol(
        IReadOnlyList<LedgerEntry> lines,
        string instrumentSymbol) =>
        lines
            .Select(line => new LedgerEntry(
                line.EntryId,
                line.JournalEntryId,
                line.Timestamp,
                line.Account with
                {
                    Symbol = IsDirectLendingInstrumentAccount(line.Account.Name)
                        ? instrumentSymbol
                        : null
                },
                line.Debit,
                line.Credit,
                line.Description))
            .ToList();

    private async Task<(Guid SecurityId, string Symbol, string Provenance, string Lineage)> ResolveSecurityMasterPostingLineageAsync(
        DirectLendingSecurityMasterReferenceDto? reference,
        string eventType,
        CancellationToken ct)
    {
        if (reference is null)
        {
            throw new DirectLendingCommandException(
                new DirectLendingCommandError(
                    DirectLendingErrorCode.Validation,
                    $"Direct-lending ledger event '{eventType}' requires a Security Master reference before posting."));
        }

        if (reference.SecurityId == Guid.Empty)
        {
            throw new DirectLendingCommandException(
                new DirectLendingCommandError(
                    DirectLendingErrorCode.Validation,
                    $"Direct-lending ledger event '{eventType}' requires a resolved Security Master security id before posting."));
        }

        var requestedSymbol = NormalizeSymbol(reference.Symbol);
        if (requestedSymbol is null)
        {
            throw new DirectLendingCommandException(
                new DirectLendingCommandError(
                    DirectLendingErrorCode.Validation,
                    $"Direct-lending ledger event '{eventType}' requires a Security Master symbol before posting."));
        }

        if (_securityMasterQueryService is null)
        {
            throw new DirectLendingCommandException(
                new DirectLendingCommandError(
                    DirectLendingErrorCode.Validation,
                    $"Direct-lending ledger event '{eventType}' requires an authoritative Security Master lookup before posting."));
        }

        var security = await _securityMasterQueryService.GetByIdAsync(reference.SecurityId, ct).ConfigureAwait(false);
        if (security is null)
        {
            throw new DirectLendingCommandException(
                new DirectLendingCommandError(
                    DirectLendingErrorCode.Validation,
                    $"Direct-lending ledger event '{eventType}' references Security Master security '{reference.SecurityId}' but no authoritative record exists."));
        }

        if (security.Status != SecurityStatusDto.Active)
        {
            throw new DirectLendingCommandException(
                new DirectLendingCommandError(
                    DirectLendingErrorCode.Validation,
                    $"Direct-lending ledger event '{eventType}' requires an active authoritative Security Master record before posting."));
        }

        if (!SecurityMasterRecordContainsSymbol(security, requestedSymbol))
        {
            throw new DirectLendingCommandException(
                new DirectLendingCommandError(
                    DirectLendingErrorCode.Validation,
                    $"Direct-lending ledger event '{eventType}' Security Master symbol '{requestedSymbol}' does not match the authoritative Security Master record for '{reference.SecurityId}'."));
        }

        var securityId = security.SecurityId.ToString("N");
        var provenance = $"security-master:{securityId};server-resolved:true;status:{security.Status};version:{security.Version}";
        var lineage = string.Join(
            ':',
            requestedSymbol,
            securityId,
            $"ledger-map:direct-lending:{requestedSymbol}:{securityId}",
            $"sm-approval:security-master-active:{securityId}",
            $"security-status:{security.Status}",
            provenance);
        return (security.SecurityId, requestedSymbol, provenance, lineage);
    }

    private static bool SecurityMasterRecordContainsSymbol(SecurityDetailDto security, string symbol)
    {
        if (TokenMatches(security.DisplayName, symbol))
        {
            return true;
        }

        foreach (var identifier in security.Identifiers)
        {
            if (TokenMatches(identifier.Value, symbol) || TokenMatches(identifier.NormalizedValue, symbol))
            {
                return true;
            }
        }

        foreach (var alias in security.Aliases)
        {
            if (alias.IsEnabled && TokenMatches(alias.AliasValue, symbol))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TokenMatches(string? value, string symbol) =>
        string.Equals(NormalizeSymbol(value), symbol, StringComparison.OrdinalIgnoreCase);

    private static bool IsDirectLendingInstrumentAccount(string account) =>
        !string.Equals(account, "Cash", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeSymbol(string? symbol) =>
        string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant();

    private static decimal GetDecimal(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number)
            {
                return value.GetDecimal();
            }
        }

        return 0m;
    }
}
