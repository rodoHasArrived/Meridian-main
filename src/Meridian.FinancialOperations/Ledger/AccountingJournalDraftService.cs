using Meridian.Contracts.Ledger;
using System.Security.Cryptography;
using System.Text;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Ledger;

public interface IAccountingJournalDraftService
{
    Task<AccountingJournalDraftResult> BuildDraftAsync(
        AccountingJournalDraftRequest request,
        CancellationToken ct = default);
}

public sealed record AccountingJournalDraftLineRequest(
    LedgerAccount Account,
    decimal Debit,
    decimal Credit,
    string? Description = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    LedgerDimensionSetDto? Dimensions = null);

public sealed record AccountingJournalDraftRequest(
    Guid AggregateId,
    Guid PeriodId,
    DateTimeOffset AccountingTimestamp,
    string Description,
    IReadOnlyList<AccountingJournalDraftLineRequest> Lines,
    AccountingBasisKindDto AccountingBasis = AccountingBasisKindDto.Primary,
    DateOnly? EffectiveDate = null,
    Guid? CommandId = null,
    Guid? CorrelationId = null,
    Guid? SourceEventId = null,
    Guid? SourceJournalEntryId = null,
    string? FundProfileId = null,
    Guid? FundStructureNodeId = null,
    string? InstrumentId = null,
    string? PolicyId = null,
    string? RuleId = null,
    AccountingTreatmentKindDto? TreatmentKind = null,
    string? SourceEventType = null,
    Guid? LedgerBookId = null,
    LedgerPostingKindDto PostingKind = LedgerPostingKindDto.Originating,
    LedgerAdjustmentApprovalMetadataDto? AdjustmentApproval = null,
    TreasuryLedgerContextDto? TreasuryContext = null,
    IReadOnlyList<string>? EvidenceLinks = null,
    string? PostingRuleId = null,
    string? PostingRuleVersion = null,
    string? DryRunCorrelationId = null);

public sealed record AccountingJournalDraftResult(
    AccountingPolicyDto Policy,
    AccountingPolicyRuleDto? Rule,
    JournalEntry? DraftEntry,
    LedgerJournalEntryWrite? Write,
    decimal TotalDebits,
    decimal TotalCredits,
    decimal Imbalance,
    bool IsBalanced,
    bool HasCriticalIssues,
    bool CanSubmitForApproval,
    bool CanPostWithoutAdditionalApproval,
    IReadOnlyList<string> EvidenceLinks,
    IReadOnlyList<AccountingConfigurationValidationIssueDto> ValidationIssues);

public sealed class AccountingJournalDraftService : IAccountingJournalDraftService
{
    private const decimal BalanceTolerance = LedgerToleranceConstants.Balance;

    private readonly IAccountingPolicyService _accountingPolicyService;
    private readonly IAccountingBasisProjectionService _projectionService;

    public AccountingJournalDraftService(
        IAccountingPolicyService accountingPolicyService,
        IAccountingBasisProjectionService projectionService)
    {
        _accountingPolicyService = accountingPolicyService ?? throw new ArgumentNullException(nameof(accountingPolicyService));
        _projectionService = projectionService ?? throw new ArgumentNullException(nameof(projectionService));
    }

    public async Task<AccountingJournalDraftResult> BuildDraftAsync(
        AccountingJournalDraftRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var issues = new List<AccountingConfigurationValidationIssueDto>();
        var evidenceLinks = NormalizeEvidenceLinks(request.EvidenceLinks);
        var effectiveDate = request.EffectiveDate ?? DateOnly.FromDateTime(request.AccountingTimestamp.UtcDateTime);
        var policy = await _accountingPolicyService.ResolvePolicyAsync(
                new AccountingPolicyQuery(
                    request.AccountingBasis,
                    effectiveDate,
                    request.PolicyId,
                    request.FundProfileId,
                    request.FundStructureNodeId,
                    request.InstrumentId,
                    request.SourceEventId),
                ct)
            .ConfigureAwait(false);

        var rule = ResolveRule(policy, request);
        if (rule is null)
        {
            AddIssue(
                issues,
                "ACCOUNTING_POLICY_RULE_NOT_FOUND",
                AccountingConfigurationValidationSeverityDto.Critical,
                "No typed accounting policy rule matched the requested treatment, source event type, or rule id.",
                request.RuleId,
                "Create or select a typed accounting policy rule before drafting the journal.");
        }
        else
        {
            if (rule.RequiresEvidence && evidenceLinks.Count == 0)
            {
                AddIssue(
                    issues,
                    "JOURNAL_DRAFT_EVIDENCE_REQUIRED",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Accounting rule '{rule.RuleId}' requires source evidence before the draft can be submitted.",
                    rule.RuleId,
                    "Attach retained source evidence or reconciliation support to the draft.");
            }

            if (rule.RequiresApproval)
            {
                AddIssue(
                    issues,
                    "JOURNAL_DRAFT_APPROVAL_REQUIRED",
                    AccountingConfigurationValidationSeverityDto.Info,
                    $"Accounting rule '{rule.RuleId}' requires approval before posting.",
                    rule.RuleId,
                    "Submit the validated draft through the accounting approval workflow.");
            }

            if (!rule.AllowsAutoPosting)
            {
                AddIssue(
                    issues,
                    "JOURNAL_DRAFT_AUTO_POSTING_DISABLED",
                    AccountingConfigurationValidationSeverityDto.Info,
                    $"Accounting rule '{rule.RuleId}' disables automatic posting.",
                    rule.RuleId,
                    "Keep the draft approval-gated until an authorized posting workflow posts the write.");
            }
        }

        ValidateHeader(request, issues);
        var treasuryContext = NormalizeTreasuryContext(request.TreasuryContext, effectiveDate);
        ValidateTreasuryContext(treasuryContext, request, issues);
        var (draftEntry, totalDebits, totalCredits) = BuildDraftEntry(request, issues, evidenceLinks, treasuryContext);
        var imbalance = totalDebits - totalCredits;
        var isBalanced = Math.Abs(imbalance) <= BalanceTolerance;

        if (!isBalanced)
        {
            AddIssue(
                issues,
                "JOURNAL_DRAFT_UNBALANCED",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Draft debits {totalDebits:0.00} do not equal credits {totalCredits:0.00}.",
                "lines",
                "Adjust debit and credit lines until the journal is balanced.");
        }

        if (request.PostingKind == LedgerPostingKindDto.Adjustment &&
            request.AdjustmentApproval?.Status != LedgerAdjustmentApprovalStatusDto.Approved)
        {
            AddIssue(
                issues,
                "JOURNAL_DRAFT_ADJUSTMENT_APPROVAL_REQUIRED",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Adjustment drafts require approved adjustment metadata before they can be projected for posting.",
                "adjustmentApproval",
                "Attach an approved adjustment approval record.");
        }

        var hasCriticalIssues = issues.Any(static issue => issue.Severity == AccountingConfigurationValidationSeverityDto.Critical);
        LedgerJournalEntryWrite? write = null;
        if (!hasCriticalIssues && draftEntry is not null && rule is not null)
        {
            var projected = await _projectionService.ProjectAsync(
                    new AccountingBasisProjectionRequest(
                        draftEntry,
                        request.AggregateId,
                        request.PeriodId,
                        request.AccountingBasis,
                        effectiveDate,
                        request.CommandId,
                        request.CorrelationId,
                        request.SourceEventId,
                        request.SourceJournalEntryId,
                        request.FundProfileId,
                        request.FundStructureNodeId,
                        request.InstrumentId,
                        request.PolicyId,
                        rule.RuleId,
                        rule.RuleVersion,
                        request.PostingKind,
                        request.AdjustmentApproval,
                        request.LedgerBookId,
                        BuildPostingCommand(request, rule, draftEntry, treasuryContext, evidenceLinks, effectiveDate)),
                    ct)
                .ConfigureAwait(false);

            write = projected.Write;
        }

        return new AccountingJournalDraftResult(
            policy,
            rule,
            draftEntry,
            write,
            totalDebits,
            totalCredits,
            imbalance,
            isBalanced,
            hasCriticalIssues,
            CanSubmitForApproval: write is not null,
            CanPostWithoutAdditionalApproval: write is not null && rule?.AllowsAutoPosting == true,
            evidenceLinks,
            issues);
    }

    private static void ValidateHeader(
        AccountingJournalDraftRequest request,
        List<AccountingConfigurationValidationIssueDto> issues)
    {
        if (request.AggregateId == Guid.Empty)
        {
            AddIssue(
                issues,
                "JOURNAL_DRAFT_AGGREGATE_REQUIRED",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Draft aggregate id is required.",
                "aggregateId",
                "Use the source transaction, reconciliation case, or accounting record id as the aggregate id.");
        }

        if (request.PeriodId == Guid.Empty)
        {
            AddIssue(
                issues,
                "JOURNAL_DRAFT_PERIOD_REQUIRED",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Draft period id is required.",
                "periodId",
                "Resolve the open accounting period before building the draft.");
        }

        if (!request.LedgerBookId.HasValue || request.LedgerBookId.Value == Guid.Empty)
        {
            AddIssue(
                issues,
                "JOURNAL_DRAFT_LEDGER_BOOK_REQUIRED",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Draft ledger-book scope is required.",
                "ledgerBookId",
                "Select the primary, GAAP, tax, statutory, cash, or other target ledger book before building a governed journal draft.");
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            AddIssue(
                issues,
                "JOURNAL_DRAFT_DESCRIPTION_REQUIRED",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Draft description is required.",
                "description",
                "Provide a concise description of the accounting event.");
        }

        if (request.Lines is null || request.Lines.Count == 0)
        {
            AddIssue(
                issues,
                "JOURNAL_DRAFT_LINES_REQUIRED",
                AccountingConfigurationValidationSeverityDto.Critical,
                "At least one journal line is required.",
                "lines",
                "Add debit and credit lines for the accounting treatment.");
        }
    }

    private static (JournalEntry? Entry, decimal TotalDebits, decimal TotalCredits) BuildDraftEntry(
        AccountingJournalDraftRequest request,
        List<AccountingConfigurationValidationIssueDto> issues,
        List<string> evidenceLinks,
        TreasuryLedgerContextDto? treasuryContext)
    {
        if (request.Lines is null || request.Lines.Count == 0)
        {
            return (null, 0m, 0m);
        }

        var journalEntryId = Guid.NewGuid();
        var lines = new List<LedgerEntry>(request.Lines.Count);
        var lineDimensionScopes = new List<(Guid EntryId, LedgerDimensionSetDto? Dimensions)>(request.Lines.Count);
        var totalDebits = 0m;
        var totalCredits = 0m;
        var description = request.Description?.Trim() ?? string.Empty;

        for (var index = 0; index < request.Lines.Count; index++)
        {
            var line = request.Lines[index];
            if (line is null)
            {
                AddLineIssue(issues, index, "JOURNAL_DRAFT_LINE_REQUIRED", "Journal line is required.", "Provide a debit or credit line.");
                continue;
            }

            foreach (var evidenceLink in NormalizeEvidenceLinks(line.EvidenceLinks))
            {
                if (!evidenceLinks.Contains(evidenceLink, StringComparer.OrdinalIgnoreCase))
                {
                    evidenceLinks.Add(evidenceLink);
                }
            }

            if (line.Account is null)
            {
                AddLineIssue(issues, index, "JOURNAL_DRAFT_ACCOUNT_REQUIRED", "Journal line account is required.", "Select a chart account for the line.");
                continue;
            }

            if (line.Debit < 0m || line.Credit < 0m)
            {
                AddLineIssue(issues, index, "JOURNAL_DRAFT_NEGATIVE_AMOUNT", "Journal line amounts cannot be negative.", "Use a positive debit or credit amount.");
                continue;
            }

            if (line.Debit == 0m && line.Credit == 0m)
            {
                AddLineIssue(issues, index, "JOURNAL_DRAFT_ZERO_AMOUNT", "Journal line must have a non-zero amount.", "Enter either a debit or credit amount.");
                continue;
            }

            if (line.Debit != 0m && line.Credit != 0m)
            {
                AddLineIssue(issues, index, "JOURNAL_DRAFT_DOUBLE_SIDED_LINE", "Journal line cannot contain both debit and credit amounts.", "Split the debit and credit into separate lines.");
                continue;
            }

            if (!ValidateLineDimensionLedgerBook(request, line, index, issues))
            {
                continue;
            }

            totalDebits += line.Debit;
            totalCredits += line.Credit;
            var lineEntryId = Guid.NewGuid();
            lines.Add(new LedgerEntry(
                lineEntryId,
                journalEntryId,
                request.AccountingTimestamp,
                line.Account,
                line.Debit,
                line.Credit,
                description,
                ToLedgerLineDimensions(line.Dimensions)));
            lineDimensionScopes.Add((lineEntryId, line.Dimensions));
        }

        if (lines.Count == 0)
        {
            return (null, totalDebits, totalCredits);
        }

        try
        {
            return (
                new JournalEntry(
                    journalEntryId,
                    request.AccountingTimestamp,
                    description,
                    lines,
                    BuildJournalEntryMetadata(request, lineDimensionScopes, evidenceLinks, treasuryContext)),
                totalDebits,
                totalCredits);
        }
        catch (LedgerValidationException ex)
        {
            AddIssue(
                issues,
                "JOURNAL_DRAFT_LEDGER_VALIDATION_FAILED",
                AccountingConfigurationValidationSeverityDto.Critical,
                ex.Message,
                "lines",
                "Correct the draft line metadata before submitting.");
            return (null, totalDebits, totalCredits);
        }
    }

    private static bool ValidateLineDimensionLedgerBook(
        AccountingJournalDraftRequest request,
        AccountingJournalDraftLineRequest line,
        int index,
        List<AccountingConfigurationValidationIssueDto> issues)
    {
        var lineBookId = NormalizeOptional(line.Dimensions?.BookId);
        if (lineBookId is null || !request.LedgerBookId.HasValue || request.LedgerBookId.Value == Guid.Empty)
        {
            return true;
        }

        var draftBookId = request.LedgerBookId.Value.ToString("D");
        if (string.Equals(lineBookId, draftBookId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        AddIssue(
            issues,
            "JOURNAL_DRAFT_LINE_LEDGER_BOOK_MISMATCH",
            AccountingConfigurationValidationSeverityDto.Critical,
            $"Journal line dimension book '{lineBookId}' does not match draft ledger book '{draftBookId}'.",
            $"lines[{index}].dimensions.bookId",
            "Use one ledger book across draft header and line dimensions before submitting or posting.");
        return false;
    }

    private static JournalEntryMetadata BuildJournalEntryMetadata(
        AccountingJournalDraftRequest request,
        IReadOnlyList<(Guid EntryId, LedgerDimensionSetDto? Dimensions)> lineDimensionScopes,
        IReadOnlyList<string> evidenceLinks,
        TreasuryLedgerContextDto? treasuryContext)
    {
        var tags = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (evidenceLinks.Count > 0)
        {
            tags["evidenceLinks"] = string.Join("|", evidenceLinks);
        }

        if (!string.IsNullOrWhiteSpace(request.SourceEventType))
        {
            tags["sourceEventType"] = request.SourceEventType.Trim();
        }

        AddTag(tags, "sourceEventId", request.SourceEventId?.ToString("D"));
        AddTag(tags, "sourceJournalEntryId", request.SourceJournalEntryId?.ToString("D"));
        AddTag(tags, "ledgerBookId", request.LedgerBookId?.ToString("D"));
        AddTag(tags, "postingRuleId", request.PostingRuleId);
        AddTag(tags, "postingRuleVersion", request.PostingRuleVersion);
        AddTag(tags, "dryRunCorrelationId", request.DryRunCorrelationId);

        foreach (var (entryId, dimensions) in lineDimensionScopes)
        {
            AppendLineDimensionTags(tags, entryId, dimensions);
        }

        return new JournalEntryMetadata(
            ActivityType: NormalizeOptional(request.SourceEventType) ?? "ManualJournalEntry",
            ProjectId: NormalizeOptional(request.FundProfileId),
            EffectiveDate: treasuryContext?.EffectiveDate ?? request.EffectiveDate,
            LedgerBook: request.LedgerBookId?.ToString("D"),
            IdempotencyKey: NormalizeOptional(treasuryContext?.IdempotencyKey),
            FundEventId: NormalizeOptional(treasuryContext?.FundEventId),
            FundEventType: NormalizeOptional(treasuryContext?.FundEventType),
            CapitalAccountId: NormalizeOptional(treasuryContext?.CapitalAccountId),
            InvestorId: NormalizeOptional(treasuryContext?.InvestorId),
            PaymentIntentId: NormalizeOptional(treasuryContext?.PaymentIntentId),
            SettlementReference: NormalizeOptional(treasuryContext?.SettlementReference),
            Tags: tags.Count == 0 ? null : tags,
            EvidenceReferences: evidenceLinks.Select(link => new JournalEvidenceReference(
                EvidenceId: link,
                Uri: link,
                Kind: AccountingPostingEvidenceKindDto.Source.ToString(),
                SourceSystem: "FinancialOperations",
                RetainedAtUtc: request.AccountingTimestamp,
                RetainedBy: "financial-operations")).ToArray());
    }

    private static void AppendLineDimensionTags(
        Dictionary<string, string> tags,
        Guid lineEntryId,
        LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            return;
        }

        var prefix = $"lineDimensions.{lineEntryId:N}.";
        AddTag(tags, prefix + "fundId", dimensions.FundId);
        AddTag(tags, prefix + "entityId", dimensions.EntityId);
        AddTag(tags, prefix + "sleeveId", dimensions.SleeveId);
        AddTag(tags, prefix + "strategyId", dimensions.StrategyId);
        AddTag(tags, prefix + "investorId", dimensions.InvestorId);
        AddTag(tags, prefix + "capitalAccountId", dimensions.CapitalAccountId);
        AddTag(tags, prefix + "instrumentId", dimensions.InstrumentId?.ToString("D"));
        AddTag(tags, prefix + "positionId", dimensions.PositionId?.ToString("D"));
        AddTag(tags, prefix + "taxLotId", dimensions.TaxLotId);
        AddTag(tags, prefix + "costCenterId", dimensions.CostCenterId);
        AddTag(tags, prefix + "counterpartyId", dimensions.CounterpartyId);
        AddTag(tags, prefix + "organizationId", dimensions.OrganizationId);
        AddTag(tags, prefix + "portfolioId", dimensions.PortfolioId);
        AddTag(tags, prefix + "bookId", dimensions.BookId);
        AddTag(tags, prefix + "accountId", dimensions.AccountId);
        AddTag(tags, prefix + "customerId", dimensions.CustomerId);
        AddTag(tags, prefix + "vendorId", dimensions.VendorId);
        AddTag(tags, prefix + "projectId", dimensions.ProjectId);

        foreach (var pair in dimensions.ExternalGlDimensions.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var key = NormalizeOptional(pair.Key);
            var value = NormalizeOptional(pair.Value);
            if (key is not null && value is not null)
            {
                tags[$"{prefix}externalGl.{key}"] = value;
            }
        }
    }

    private static void AddTag(Dictionary<string, string> tags, string key, string? value)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is not null)
        {
            tags[key] = normalized;
        }
    }

    private static LedgerLineDimensionSet? ToLedgerLineDimensions(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            return null;
        }

        return new LedgerLineDimensionSet(
            FundId: NormalizeOptional(dimensions.FundId),
            EntityId: NormalizeOptional(dimensions.EntityId),
            SleeveId: NormalizeOptional(dimensions.SleeveId),
            StrategyId: NormalizeOptional(dimensions.StrategyId),
            InvestorId: NormalizeOptional(dimensions.InvestorId),
            CapitalAccountId: NormalizeOptional(dimensions.CapitalAccountId),
            InstrumentId: dimensions.InstrumentId,
            TaxLotId: NormalizeOptional(dimensions.TaxLotId),
            CostCenterId: NormalizeOptional(dimensions.CostCenterId),
            CounterpartyId: NormalizeOptional(dimensions.CounterpartyId),
            ExternalGlDimensions: dimensions.ExternalGlDimensions
                .Where(static item => !string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Value))
                .OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static item => item.Key.Trim(), static item => item.Value.Trim(), StringComparer.OrdinalIgnoreCase),
            OrganizationId: NormalizeOptional(dimensions.OrganizationId),
            PortfolioId: NormalizeOptional(dimensions.PortfolioId),
            BookId: NormalizeOptional(dimensions.BookId),
            AccountId: NormalizeOptional(dimensions.AccountId),
            CustomerId: NormalizeOptional(dimensions.CustomerId),
            VendorId: NormalizeOptional(dimensions.VendorId),
            ProjectId: NormalizeOptional(dimensions.ProjectId))
        {
            PositionId = dimensions.PositionId
        };
    }

    private static AccountingPostingCommandDto BuildPostingCommand(
        AccountingJournalDraftRequest request,
        AccountingPolicyRuleDto rule,
        JournalEntry draftEntry,
        TreasuryLedgerContextDto? treasuryContext,
        IReadOnlyList<string> evidenceLinks,
        DateOnly effectiveDate)
    {
        var idempotencyKey = treasuryContext?.IdempotencyKey
                             ?? request.SourceEventId?.ToString("N")
                             ?? NormalizeOptional(request.PolicyId)
                             ?? draftEntry.JournalEntryId.ToString("N");
        var commandId = request.CommandId ?? CreateDeterministicCommandId(
            request.AggregateId,
            request.PeriodId,
            idempotencyKey,
            request.SourceEventId,
            rule.RuleId,
            effectiveDate);
        var approvalState = request.AdjustmentApproval?.Status == LedgerAdjustmentApprovalStatusDto.Approved
            ? AccountingPostingApprovalStateDto.Approved
            : rule.RequiresApproval
                ? AccountingPostingApprovalStateDto.Pending
                : AccountingPostingApprovalStateDto.NotRequired;
        var intent = request.PostingKind == LedgerPostingKindDto.Adjustment
            ? AccountingPostingIntentDto.Adjustment
            : request.TreatmentKind switch
            {
                AccountingTreatmentKindDto.Reversal => AccountingPostingIntentDto.Reversal,
                _ => AccountingPostingIntentDto.Originating
            };

        return new AccountingPostingCommandDto(
            commandId,
            request.AggregateId,
            request.PeriodId,
            effectiveDate,
            request.AccountingTimestamp,
            idempotencyKey,
            intent,
            SourceEventId: request.SourceEventId,
            CorrelationId: request.CorrelationId,
            CausationId: request.SourceEventId,
            SourceJournalEntryId: request.SourceJournalEntryId,
            SourceEventType: request.SourceEventType ?? rule.SourceEventType,
            TreasuryContext: treasuryContext,
            ApprovalState: approvalState,
            ApprovalId: request.AdjustmentApproval?.ApprovalId,
            OperatorRationale: evidenceLinks.Count == 0 ? draftEntry.Description : null,
            Evidence: evidenceLinks.Select(link => new AccountingPostingEvidenceReferenceDto(
                EvidenceId: link,
                Uri: link,
                Kind: AccountingPostingEvidenceKindDto.Source,
                SourceSystem: "FinancialOperations",
                RetainedAtUtc: request.AccountingTimestamp,
                RetainedBy: "financial-operations")).ToArray(),
            LedgerBookId: request.LedgerBookId);
    }

    private static Guid CreateDeterministicCommandId(
        Guid aggregateId,
        Guid periodId,
        string idempotencyKey,
        Guid? sourceEventId,
        string ruleId,
        DateOnly effectiveDate)
    {
        var input = string.Join(
            '|',
            "accounting-posting-command",
            aggregateId.ToString("D"),
            periodId.ToString("D"),
            sourceEventId?.ToString("D") ?? string.Empty,
            NormalizeOptional(ruleId),
            effectiveDate.ToString("yyyy-MM-dd"),
            NormalizeOptional(idempotencyKey));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return new Guid(hash[..16]);
    }

    private static TreasuryLedgerContextDto? NormalizeTreasuryContext(
        TreasuryLedgerContextDto? context,
        DateOnly effectiveDate)
    {
        if (context is null)
        {
            return null;
        }

        return context with
        {
            EffectiveDate = context.EffectiveDate ?? effectiveDate,
            IdempotencyKey = NormalizeOptional(context.IdempotencyKey),
            FundEventId = NormalizeOptional(context.FundEventId),
            FundEventType = NormalizeOptional(context.FundEventType),
            CapitalAccountId = NormalizeOptional(context.CapitalAccountId),
            InvestorId = NormalizeOptional(context.InvestorId),
            PaymentIntentId = NormalizeOptional(context.PaymentIntentId),
            SettlementReference = NormalizeOptional(context.SettlementReference)
        };
    }

    private static void ValidateTreasuryContext(
        TreasuryLedgerContextDto? context,
        AccountingJournalDraftRequest request,
        List<AccountingConfigurationValidationIssueDto> issues)
    {
        if (context is null)
        {
            return;
        }

        if (context.EffectiveDate is null)
        {
            AddIssue(
                issues,
                "JOURNAL_DRAFT_EFFECTIVE_DATE_REQUIRED",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Treasury ledger context requires an effective date.",
                "treasuryContext.effectiveDate",
                "Set the business-effective date that controls ledger balances.");
        }

        if (string.IsNullOrWhiteSpace(context.IdempotencyKey))
        {
            AddIssue(
                issues,
                "JOURNAL_DRAFT_IDEMPOTENCY_KEY_REQUIRED",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Treasury ledger context requires an idempotency key.",
                "treasuryContext.idempotencyKey",
                "Provide a stable key for the source event and posting attempt.");
        }

        var hasFundEventContext =
            !string.IsNullOrWhiteSpace(context.FundEventId) ||
            !string.IsNullOrWhiteSpace(context.FundEventType) ||
            !string.IsNullOrWhiteSpace(context.CapitalAccountId) ||
            !string.IsNullOrWhiteSpace(context.InvestorId);
        if (hasFundEventContext)
        {
            RequireTreasuryContextText(issues, context.FundEventId, "JOURNAL_DRAFT_FUND_EVENT_REQUIRED", "fund event id", "treasuryContext.fundEventId");
            RequireTreasuryContextText(issues, context.FundEventType, "JOURNAL_DRAFT_FUND_EVENT_TYPE_REQUIRED", "fund event type", "treasuryContext.fundEventType");
            RequireTreasuryContextText(issues, context.CapitalAccountId, "JOURNAL_DRAFT_CAPITAL_ACCOUNT_REQUIRED", "capital account id", "treasuryContext.capitalAccountId");
        }

        var hasPaymentContext =
            !string.IsNullOrWhiteSpace(context.PaymentIntentId) ||
            !string.IsNullOrWhiteSpace(context.SettlementReference);
        if (hasPaymentContext && request.SourceEventId is null)
        {
            AddIssue(
                issues,
                "JOURNAL_DRAFT_PAYMENT_SOURCE_EVENT_REQUIRED",
                AccountingConfigurationValidationSeverityDto.Warning,
                "Payment-linked treasury ledger drafts should carry a source event id for audit reconstruction.",
                "sourceEventId",
                "Link the payment or settlement event id before final posting.");
        }
    }

    private static void RequireTreasuryContextText(
        List<AccountingConfigurationValidationIssueDto> issues,
        string? value,
        string code,
        string label,
        string targetId)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        AddIssue(
            issues,
            code,
            AccountingConfigurationValidationSeverityDto.Critical,
            $"Treasury ledger context requires {label}.",
            targetId,
            $"Attach {label} before submitting or posting the draft.");
    }

    private static AccountingPolicyRuleDto? ResolveRule(
        AccountingPolicyDto policy,
        AccountingJournalDraftRequest request)
    {
        var rules = policy.RulePack?.Rules ?? [];
        if (!string.IsNullOrWhiteSpace(request.RuleId))
        {
            var rule = rules.FirstOrDefault(rule => string.Equals(rule.RuleId, request.RuleId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (rule is not null)
            {
                return rule;
            }
        }

        if (request.TreatmentKind.HasValue)
        {
            var rule = rules.FirstOrDefault(rule => rule.TreatmentKind == request.TreatmentKind.Value);
            if (rule is not null)
            {
                return rule;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.SourceEventType))
        {
            var rule = rules.FirstOrDefault(rule => string.Equals(rule.SourceEventType, request.SourceEventType.Trim(), StringComparison.OrdinalIgnoreCase));
            if (rule is not null)
            {
                return rule;
            }
        }

        return rules.FirstOrDefault();
    }

    private static List<string> NormalizeEvidenceLinks(IReadOnlyList<string>? evidenceLinks)
    {
        if (evidenceLinks is null || evidenceLinks.Count == 0)
        {
            return [];
        }

        return evidenceLinks
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void AddLineIssue(
        List<AccountingConfigurationValidationIssueDto> issues,
        int lineIndex,
        string code,
        string message,
        string suggestedAction)
    {
        AddIssue(
            issues,
            code,
            AccountingConfigurationValidationSeverityDto.Critical,
            message,
            $"lines[{lineIndex}]",
            suggestedAction);
    }

    private static void AddIssue(
        List<AccountingConfigurationValidationIssueDto> issues,
        string code,
        AccountingConfigurationValidationSeverityDto severity,
        string message,
        string? targetId,
        string? suggestedAction)
    {
        issues.Add(new AccountingConfigurationValidationIssueDto(
            code,
            severity,
            message,
            targetId,
            suggestedAction));
    }
}
