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
        var description = eventType switch
        {
            "loan.drawdown-booked" => "Loan drawdown funding",
            "loan.daily-accrual-posted" => "Loan daily accrual",
            "loan.daily-accrual-reversed" => "Loan daily accrual reversal",
            "loan.fee-assessed" => "Loan fee assessment",
            "loan.prepayment-penalty-charged" => "Loan prepayment penalty",
            "loan.pik-capitalized" => "Loan PIK capitalization",
            "loan.principal-payment-applied" => "Loan principal receipt",
            "loan.mixed-payment-applied" => "Loan cash receipt",
            "loan.unapplied-cash-applied" => "Loan unapplied cash applied",
            "loan.unapplied-cash-refunded" => "Loan unapplied cash refund",
            "loan.write-off-applied" => "Loan write-off",
            "loan.discount-premium-amortized" or "DiscountPremiumAmortized" => "Loan discount/premium amortization",
            "loan.restructured" or "LoanRestructured" => "Loan restructuring",
            _ => eventType
        };
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
                description,
                currency: new LedgerEntryCurrency(currency, currency, debit, credit, 1m)));
        }

        foreach (var line in DirectLendingJournalLines.ForEvent(eventType, payload.RootElement, loanId.ToString("D")))
        {
            Add(DirectLendingJournalLines.RequiredRole(line), line.Debit, line.Credit);
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

    /// <summary>
    /// Reports whether a retained loan event requires a journal, using exactly the same amount
    /// validation and line projection as posting. Zero-value and non-accounting events have no
    /// required journal; malformed or negative accounting amounts fail closed.
    /// </summary>
    public static bool HasAccountingImpact(string eventType, JsonElement payload)
        => DirectLendingJournalLines.ForEvent(eventType, payload, Guid.Empty.ToString("D")).Count > 0;

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
                line.Dimensions,
                line.Currency))
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
                dimensions,
                line.Currency))
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

    private static bool IsDirectLendingInstrumentAccount(string account) =>
        !string.Equals(account, "Cash", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeSymbol(string? symbol) =>
        string.IsNullOrWhiteSpace(symbol) ? null : symbol.Trim().ToUpperInvariant();

}
