using System.Text.Json;
using Meridian.FinancialOperations.Ledger;
using Meridian.Contracts.DirectLending;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ledger;
using Meridian.Storage.DirectLending;
using Meridian.Storage.Ledger;
using SecurityMasterQueryService = Meridian.Contracts.SecurityMaster.ISecurityMasterQueryService;

namespace Meridian.Application.DirectLending;

/// <summary>
/// Builds ledger journal writes for loan events using the applicable accounting policy.
/// </summary>
public sealed class LoanAccountingProjector
{
    private readonly IChartOfAccounts _chartOfAccounts = StandardChartOfAccounts.Instance;
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
        var accountingDate = effectiveDate ?? contract.EffectiveDate;
        var journalEntryId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(accountingDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var currency = contract.CurrentTerms.BaseCurrency.ToString();
        var lines = new List<LedgerEntry>();
        var description = eventType;
        var securityReference = contract.CurrentTerms.SecurityMasterReference;
        var instrumentSymbol = NormalizeSymbol(securityReference?.Symbol);

        void Add(LedgerAccountRole accountRole, decimal debit, decimal credit)
        {
            var account = _chartOfAccounts.Resolve(
                accountRole,
                loanId.ToString("D"),
                accountRole == LedgerAccountRole.Cash ? null : instrumentSymbol);
            if (debit < 0m || credit < 0m)
            {
                // Fail closed: a negative amount would otherwise be dropped (or posted inverted)
                // while the loan event itself is still persisted.
                throw new DirectLendingCommandException(
                    new DirectLendingCommandError(
                        DirectLendingErrorCode.Validation,
                        $"Direct-lending ledger event '{eventType}' produced a negative amount for account '{account.Name}'; the journal was not posted."));
            }

            if (debit == 0m && credit == 0m)
            {
                return;
            }

            lines.Add(new LedgerEntry(
                Guid.NewGuid(),
                journalEntryId,
                timestamp,
                account,
                debit,
                credit,
                description));
        }

        var root = payload.RootElement;
        switch (eventType)
        {
            case "loan.drawdown-booked":
                description = "Loan drawdown funding";
                var drawdownAmount = GetRequiredDecimal(root, eventType, "amount");
                Add(LedgerAccountRole.LoanPrincipal, drawdownAmount, 0m);
                Add(LedgerAccountRole.Cash, 0m, drawdownAmount);
                break;

            case "loan.daily-accrual-posted":
                description = "Loan daily accrual";
                var commitmentFee = GetDecimal(root, "commitmentFeeAmount");
                var penalty = GetDecimal(root, "penaltyAmount");
                var pikInterest = GetDecimal(root, "pikInterestAmount");
                // PRD-115: interest settled from prepaid interest first (shared with DirectLendingOutboxDispatcher).
                foreach (var line in DirectLendingJournalLines.ForAccruedInterest(root))
                {
                    Add(ResolveAccountRole(line.Account), line.Debit, line.Credit);
                }

                if (IsPikCapitalizationDeferred(root))
                {
                    // Capitalization policy: PIK is earned now but is neither cash nor principal
                    // until a capitalization date moves it (loan.pik-capitalized).
                    Add(LedgerAccountRole.LoanPikInterestReceivable, pikInterest, 0m);
                    Add(LedgerAccountRole.LoanPikInterestIncome, 0m, pikInterest);
                }
                else
                {
                    // Legacy PIK toggle: PIK interest capitalizes into loan principal the same day.
                    Add(LedgerAccountRole.LoanPrincipal, pikInterest, 0m);
                    Add(LedgerAccountRole.LoanInterestIncome, 0m, pikInterest);
                }
                Add(LedgerAccountRole.LoanCommitmentFeeReceivable, commitmentFee, 0m);
                Add(LedgerAccountRole.LoanCommitmentFeeIncome, 0m, commitmentFee);
                Add(LedgerAccountRole.LoanPenaltyReceivable, penalty, 0m);
                Add(LedgerAccountRole.LoanPenaltyIncome, 0m, penalty);
                break;

            case "loan.daily-accrual-reversed":
                description = "Loan daily accrual reversal";
                var reversedCommitmentFee = GetDecimal(root, "commitmentFeeAmount");
                var reversedPenalty = GetDecimal(root, "penaltyAmount");
                var reversedPikInterest = GetDecimal(root, "pikInterestAmount");
                // PRD-115: the exact inverse of the posting, so interest that consumed prepaid interest
                // restores PrepaidInterestLiability instead of crediting a receivable never debited.
                foreach (var line in DirectLendingJournalLines.ForAccruedInterest(root))
                {
                    Add(ResolveAccountRole(line.Account), line.Credit, line.Debit);
                }
                if (IsPikCapitalizationDeferred(root))
                {
                    Add(LedgerAccountRole.LoanPikInterestIncome, reversedPikInterest, 0m);
                    Add(LedgerAccountRole.LoanPikInterestReceivable, 0m, reversedPikInterest);
                }
                else
                {
                    Add(LedgerAccountRole.LoanInterestIncome, reversedPikInterest, 0m);
                    Add(LedgerAccountRole.LoanPrincipal, 0m, reversedPikInterest);
                }
                Add(LedgerAccountRole.LoanCommitmentFeeIncome, reversedCommitmentFee, 0m);
                Add(LedgerAccountRole.LoanCommitmentFeeReceivable, 0m, reversedCommitmentFee);
                Add(LedgerAccountRole.LoanPenaltyIncome, reversedPenalty, 0m);
                Add(LedgerAccountRole.LoanPenaltyReceivable, 0m, reversedPenalty);
                break;

            // PRD-115: payments now settle FeeReceivable and PenaltyReceivable directly, so the
            // assessments that create those receivables must reach the authoritative ledger too
            // (the same pairs the outbox dispatcher drafts), or settlement would drive them negative.
            case "loan.fee-assessed":
                description = "Loan fee assessment";
                var assessedFee = GetRequiredDecimal(root, eventType, "amount", "Amount");
                Add(LedgerAccountRole.LoanFeeReceivable, assessedFee, 0m);
                Add(LedgerAccountRole.LoanFeeIncome, 0m, assessedFee);
                break;

            case "loan.prepayment-penalty-charged":
                description = "Loan prepayment penalty";
                var chargedPenalty = GetRequiredDecimal(root, eventType, "penaltyAmount", "PenaltyAmount");
                Add(LedgerAccountRole.LoanPenaltyReceivable, chargedPenalty, 0m);
                Add(LedgerAccountRole.LoanPenaltyIncome, 0m, chargedPenalty);
                break;

            case "loan.pik-capitalized":
                description = "Loan PIK capitalization";
                var capitalizedPik = GetRequiredDecimal(root, eventType, "amount");
                Add(LedgerAccountRole.LoanPrincipal, capitalizedPik, 0m);
                Add(LedgerAccountRole.LoanPikInterestReceivable, 0m, capitalizedPik);
                break;

            case "loan.principal-payment-applied":
                description = "Loan principal receipt";
                var appliedPrincipal = GetRequiredDecimal(root, eventType, "AppliedAmount", "appliedAmount", "amount");
                Add(LedgerAccountRole.Cash, appliedPrincipal, 0m);
                Add(LedgerAccountRole.LoanPrincipal, 0m, appliedPrincipal);
                break;

            case "loan.mixed-payment-applied":
                description = "Loan cash receipt";
                // PRD-115: split by allocation (shared with DirectLendingOutboxDispatcher).
                foreach (var line in DirectLendingJournalLines.ForMixedPayment(root))
                {
                    Add(ResolveAccountRole(line.Account), line.Debit, line.Credit);
                }
                break;

            case "loan.unapplied-cash-applied":
            case "loan.unapplied-cash-refunded":
                description = eventType == "loan.unapplied-cash-applied" ? "Loan unapplied cash applied" : "Loan unapplied cash refund";
                // PRD-115 increment 7 (shared with DirectLendingOutboxDispatcher).
                foreach (var line in eventType == "loan.unapplied-cash-applied"
                    ? DirectLendingJournalLines.ForUnappliedCashApplied(root)
                    : DirectLendingJournalLines.ForUnappliedCashRefunded(root))
                {
                    Add(ResolveAccountRole(line.Account), line.Debit, line.Credit);
                }
                break;

            case "loan.write-off-applied":
                description = "Loan write-off";
                var writeOffAmount = GetRequiredDecimal(root, eventType, "AppliedAmount", "appliedAmount", "Amount", "amount");
                Add(LedgerAccountRole.LoanWriteOffExpense, writeOffAmount, 0m);
                Add(LedgerAccountRole.LoanPrincipal, 0m, writeOffAmount);
                break;

            case "loan.discount-premium-amortized":
            case "DiscountPremiumAmortized":
                description = "Loan discount/premium amortization";
                var discountAmortization = GetDecimal(root, "discountAmort", "discountAmortization");
                var premiumAmortization = GetDecimal(root, "premiumAmort", "premiumAmortization");
                Add(LedgerAccountRole.LoanDiscountAmortizationExpense, discountAmortization, 0m);
                Add(LedgerAccountRole.LoanDiscount, 0m, discountAmortization);
                Add(LedgerAccountRole.LoanPremium, premiumAmortization, 0m);
                Add(LedgerAccountRole.LoanPremiumAmortizationIncome, 0m, premiumAmortization);
                break;

            case "loan.restructured":
            case "LoanRestructured":
                description = "Loan restructuring";
                var principalHaircut = GetDecimal(root, "PrincipalHaircut", "principalHaircut", "haircutAmount");
                Add(LedgerAccountRole.LoanRestructuringLoss, principalHaircut, 0m);
                Add(LedgerAccountRole.LoanPrincipal, 0m, principalHaircut);
                break;
        }

        if (lines.Count == 0)
        {
            return [];
        }

        if (_journalStore is null)
        {
            throw new DirectLendingCommandException(
                new DirectLendingCommandError(
                    DirectLendingErrorCode.Validation,
                    "Ledger journal store is required for direct-lending ledger-impacting events."));
        }

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

        var securityLineage = await ResolveSecurityMasterPostingLineageAsync(securityReference, eventType, ct).ConfigureAwait(false);
        instrumentSymbol = securityLineage.Symbol;
        lines = ApplyAuthoritativeInstrumentSymbol(lines, instrumentSymbol);
        lines = ApplyLineDimensions(lines, contract, loanId, securityLineage.SecurityId, period.LedgerBookId);
        var postingCommandId = metadata.CommandId.GetValueOrDefault(sourceEventId);
        var idempotencyKey = $"direct-lending:{loanId:N}:{eventType}:{sourceEventId:N}";
        var sourceEvidence = new JournalEvidenceReference(
            $"direct-lending-event-{sourceEventId:N}",
            $"direct-lending://events/{sourceEventId:D}",
            AccountingPostingEvidenceKindDto.Source.ToString(),
            "DirectLending",
            timestamp,
            "meridian.direct-lending",
            SubjectId: loanId.ToString("D"),
            Description: eventType);

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
                EffectiveDate: accountingDate,
                IdempotencyKey: idempotencyKey,
                Tags: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["loanId"] = loanId.ToString("D"),
                    ["currency"] = currency,
                    ["sourceEventId"] = sourceEventId.ToString("D"),
                    ["sourceEventType"] = eventType,
                    ["securityMasterProvenance"] = securityLineage.Provenance,
                    ["securityMasterLineage"] = securityLineage.Lineage
                },
                EvidenceReferences: [sourceEvidence]));

        if (!entry.IsBalanced)
        {
            throw new DirectLendingCommandException(
                new DirectLendingCommandError(
                    DirectLendingErrorCode.Validation,
                    $"Direct-lending ledger event '{eventType}' produced an unbalanced journal; the journal was not posted."));
        }

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
                AdjustmentApproval: adjustmentApproval,
                LedgerBookId: period.LedgerBookId,
                PostingCommand: new AccountingPostingCommandDto(
                    postingCommandId,
                    loanId,
                    period.PeriodId,
                    accountingDate,
                    timestamp,
                    idempotencyKey,
                    postingKind == LedgerPostingKindDto.Adjustment
                        ? AccountingPostingIntentDto.Adjustment
                        : AccountingPostingIntentDto.Originating,
                    SourceEventId: sourceEventId,
                    CorrelationId: metadata.CorrelationId,
                    CausationId: postingCommandId,
                    ExpectedVersion: period.Version,
                    SourceEventType: eventType,
                    ApprovalState: adjustmentApproval?.Status == LedgerAdjustmentApprovalStatusDto.Approved
                        ? AccountingPostingApprovalStateDto.Approved
                        : AccountingPostingApprovalStateDto.NotRequired,
                    ApprovalId: adjustmentApproval?.ApprovalId,
                    LedgerBookId: period.LedgerBookId,
                    Evidence:
                    [
                        new AccountingPostingEvidenceReferenceDto(
                            sourceEvidence.EvidenceId,
                            sourceEvidence.Uri,
                            AccountingPostingEvidenceKindDto.Source,
                            sourceEvidence.SourceSystem,
                            sourceEvidence.RetainedAtUtc,
                            sourceEvidence.RetainedBy,
                            sourceEvidence.SubjectId,
                            sourceEvidence.ContentHash,
                            sourceEvidence.Description)
                    ]))
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
                line.Description,
                line.Dimensions))
            .ToList();

    private static List<LedgerEntry> ApplyLineDimensions(
        IReadOnlyList<LedgerEntry> lines,
        LoanContractDetailDto contract,
        Guid loanId,
        Guid securityId,
        Guid? ledgerBookId)
    {
        var dimensions = new LedgerLineDimensionSet(
            EntityId: contract.Borrower.LegalEntityId?.ToString("D"),
            InstrumentId: securityId,
            CounterpartyId: contract.Borrower.BorrowerId.ToString("D"),
            BookId: ledgerBookId?.ToString("D"),
            AccountId: loanId.ToString("D"));

        return lines
            .Select(line => new LedgerEntry(
                line.EntryId,
                line.JournalEntryId,
                line.Timestamp,
                line.Account,
                line.Debit,
                line.Credit,
                line.Description,
                dimensions))
            .ToList();
    }

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

    private static bool IsPikCapitalizationDeferred(JsonElement root) =>
        root.TryGetProperty("pikCapitalizationDeferred", out var value) && value.ValueKind == JsonValueKind.True;

    private static LedgerAccountRole ResolveAccountRole(string account) => account switch
    {
        "Cash" => LedgerAccountRole.Cash,
        "LoanPrincipal" => LedgerAccountRole.LoanPrincipal,
        "AccruedInterestReceivable" => LedgerAccountRole.LoanAccruedInterestReceivable,
        "InterestIncome" => LedgerAccountRole.LoanInterestIncome,
        "PikInterestReceivable" => LedgerAccountRole.LoanPikInterestReceivable,
        "PikInterestIncome" => LedgerAccountRole.LoanPikInterestIncome,
        "CommitmentFeeReceivable" => LedgerAccountRole.LoanCommitmentFeeReceivable,
        "CommitmentFeeIncome" => LedgerAccountRole.LoanCommitmentFeeIncome,
        "FeeReceivable" => LedgerAccountRole.LoanFeeReceivable,
        "FeeIncome" => LedgerAccountRole.LoanFeeIncome,
        "PenaltyReceivable" => LedgerAccountRole.LoanPenaltyReceivable,
        "PenaltyIncome" => LedgerAccountRole.LoanPenaltyIncome,
        "LoanAndAccrualsClearing" => LedgerAccountRole.LoanAndAccrualsClearing,
        "PrepaidInterestLiability" => LedgerAccountRole.LoanPrepaidInterestLiability,
        "UnappliedCashLiability" => LedgerAccountRole.LoanUnappliedCashLiability,
        "WriteOffExpense" => LedgerAccountRole.LoanWriteOffExpense,
        "DiscountAmortizationExpense" => LedgerAccountRole.LoanDiscountAmortizationExpense,
        "LoanDiscount" => LedgerAccountRole.LoanDiscount,
        "LoanPremium" => LedgerAccountRole.LoanPremium,
        "PremiumAmortizationIncome" => LedgerAccountRole.LoanPremiumAmortizationIncome,
        "RestructuringLoss" => LedgerAccountRole.LoanRestructuringLoss,
        _ => throw new LedgerValidationException($"Direct-lending account '{account}' is not registered in the shared chart of accounts.")
    };

    private static bool IsDirectLendingInstrumentAccount(string account) =>
        !string.Equals(account, "Cash", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeSymbol(string? symbol) =>
        string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant();

    /// <summary>
    /// Reads an optional amount. An absent member means the component does not apply and reads as
    /// zero; a member that is present but not a JSON number is rejected rather than read as zero.
    /// </summary>
    private static decimal GetDecimal(JsonElement root, params string[] propertyNames) =>
        TryGetDecimal(root, propertyNames, out var value) ? value : 0m;

    /// <summary>
    /// Reads the amount a ledger-impacting event cannot post without. A missing amount fails the
    /// command instead of silently producing no journal for a persisted loan event.
    /// </summary>
    private static decimal GetRequiredDecimal(JsonElement root, string eventType, params string[] propertyNames)
    {
        if (TryGetDecimal(root, propertyNames, out var value))
        {
            return value;
        }

        throw new DirectLendingCommandException(
            new DirectLendingCommandError(
                DirectLendingErrorCode.Validation,
                $"Direct-lending ledger event '{eventType}' payload is missing required amount '{propertyNames[0]}'; the journal was not posted."));
    }

    private static bool TryGetDecimal(JsonElement root, string[] propertyNames, out decimal value)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            if (element.ValueKind != JsonValueKind.Number)
            {
                throw new DirectLendingCommandException(
                    new DirectLendingCommandError(
                        DirectLendingErrorCode.Validation,
                        $"Direct-lending ledger payload member '{propertyName}' must be a JSON number; the journal was not posted."));
            }

            value = element.GetDecimal();
            return true;
        }

        value = 0m;
        return false;
    }
}
