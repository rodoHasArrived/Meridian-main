using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.FinancialOperations.Ledger;

public interface IAccountingPostingCandidateService
{
    Task<PostingRuleJournalCandidateResultDto> BuildCandidateAsync(
        PostingRuleJournalCandidateRequestDto request,
        CancellationToken ct = default);
}

public sealed class AccountingPostingCandidateService : IAccountingPostingCandidateService
{
    private readonly IAccountingConfigurationService _configurationService;
    private readonly IAccountingJournalDraftService _journalDraftService;

    public AccountingPostingCandidateService(
        IAccountingConfigurationService configurationService,
        IAccountingJournalDraftService journalDraftService)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _journalDraftService = journalDraftService ?? throw new ArgumentNullException(nameof(journalDraftService));
    }

    public async Task<PostingRuleJournalCandidateResultDto> BuildCandidateAsync(
        PostingRuleJournalCandidateRequestDto request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var dryRun = await _configurationService.DryRunPostingRuleAsync(
                new RuleDryRunRequestDto(
                    request.FundProfileId,
                    request.SourceEventType,
                    request.EventAmount,
                    request.Currency,
                    request.EffectiveDate,
                    request.Actor,
                    request.LedgerBookId,
                    request.Dimensions,
                    request.CounterpartyId,
                    request.InstrumentSymbol,
                    request.CorrelationId?.ToString("D")),
                ct)
            .ConfigureAwait(false);

        var issues = dryRun.ValidationIssues
            .Select(ToCandidateIssue)
            .ToList();
        var selectedRuleVersion = dryRun.RuleMatches
            .Where(match => match.IsMatched)
            .Where(match => string.Equals(match.RuleId, dryRun.SelectedRuleId, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static match => match.Priority)
            .Select(static match => match.RuleVersion)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(dryRun.SelectedRuleId))
        {
            issues.Add(Issue(
                "posting-candidate.rule-required",
                AccountingConfigurationValidationSeverityDto.Critical,
                "A matched posting rule is required before a governed journal draft candidate can be built.",
                "selectedRuleId",
                "Resolve the dry-run rule match issues before building a journal draft candidate."));
        }

        if (dryRun.GeneratedPostingLines.Count == 0)
        {
            issues.Add(Issue(
                "posting-candidate.lines-required",
                AccountingConfigurationValidationSeverityDto.Critical,
                "The selected posting rule did not generate any posting lines.",
                dryRun.SelectedRuleId ?? request.SourceEventType,
                "Configure generated postings or a journal template before building a journal draft candidate."));
        }

        if (!request.LedgerBookId.HasValue)
        {
            issues.Add(Issue(
                "posting-candidate.ledger-book-required",
                AccountingConfigurationValidationSeverityDto.Critical,
                "A source-event posting candidate must target a ledger book before a governed journal draft can be built.",
                "ledgerBookId",
                "Select the ledger book that owns the source event, posting rule configuration, journal draft, and approval workflow."));
        }

        if (issues.Any(static issue => issue.BlocksCandidate))
        {
            return BuildBlockedResult(request, dryRun, selectedRuleVersion, issues);
        }

        var workspace = await _configurationService.GetWorkspaceAsync(request.FundProfileId, request.LedgerBookId, ct)
            .ConfigureAwait(false);
        var chartByPath = BuildChartByPath(workspace.ChartOfAccounts);
        var duplicatePaths = workspace.ChartOfAccounts
            .Where(static node => !string.IsNullOrWhiteSpace(node.Path))
            .GroupBy(static node => node.Path.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lines = new List<AccountingJournalDraftLineRequest>(dryRun.GeneratedPostingLines.Count);

        for (var index = 0; index < dryRun.GeneratedPostingLines.Count; index++)
        {
            var line = dryRun.GeneratedPostingLines[index];
            if (duplicatePaths.Contains(line.AccountPath.Trim()))
            {
                issues.Add(Issue(
                    "posting-candidate.account-ambiguous",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Generated posting line '{line.LineId}' references ambiguous account path '{line.AccountPath}'.",
                    line.LineId,
                    "Keep chart account paths unique before building a posting candidate."));
                continue;
            }

            if (!chartByPath.TryGetValue(line.AccountPath.Trim(), out var chartNode))
            {
                issues.Add(Issue(
                    "posting-candidate.account-missing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Generated posting line '{line.LineId}' references missing account path '{line.AccountPath}'.",
                    line.LineId,
                    "Map generated postings to active chart accounts before building a posting candidate."));
                continue;
            }

            if (chartNode.IsArchived)
            {
                issues.Add(Issue(
                    "posting-candidate.account-archived",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Generated posting line '{line.LineId}' references archived account path '{line.AccountPath}'.",
                    line.LineId,
                    "Map generated postings to active chart accounts before building a posting candidate."));
                continue;
            }

            if (!TryMapLedgerAccountType(chartNode.AccountType, out var accountType))
            {
                issues.Add(Issue(
                    "posting-candidate.account-type-unsupported",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Generated posting line '{line.LineId}' references account path '{line.AccountPath}' with unsupported account type '{chartNode.AccountType}'.",
                    line.LineId,
                    "Use Asset, Liability, Equity, Revenue, or Expense chart account types."));
                continue;
            }

            var amount = Math.Abs(line.Amount);
            lines.Add(new AccountingJournalDraftLineRequest(
                new LedgerAccount(chartNode.AccountName, accountType),
                line.Side == AccountingTemplateLineSideDto.Debit ? amount : 0m,
                line.Side == AccountingTemplateLineSideDto.Credit ? amount : 0m,
                line.Description,
                request.EvidenceLinks,
                line.Dimensions));
        }

        if (issues.Any(static issue => issue.BlocksCandidate))
        {
            return BuildBlockedResult(request, dryRun, selectedRuleVersion, issues);
        }

        var draft = await _journalDraftService.BuildDraftAsync(
                new AccountingJournalDraftRequest(
                    request.AggregateId,
                    request.PeriodId,
                    request.AccountingTimestamp,
                    request.Description,
                    lines,
                    request.AccountingBasis,
                    request.EffectiveDate,
                    CorrelationId: request.CorrelationId,
                    SourceEventId: request.SourceEventId,
                    SourceJournalEntryId: request.SourceJournalEntryId,
                    FundProfileId: request.FundProfileId,
                    PolicyId: request.PolicyId,
                    TreatmentKind: request.TreatmentKind,
                    SourceEventType: request.SourceEventType,
                    LedgerBookId: request.LedgerBookId,
                    PostingKind: request.PostingKind,
                    AdjustmentApproval: request.AdjustmentApproval,
                    TreasuryContext: request.TreasuryContext,
                    EvidenceLinks: request.EvidenceLinks,
                    PostingRuleId: dryRun.SelectedRuleId,
                    PostingRuleVersion: selectedRuleVersion,
                    DryRunCorrelationId: request.CorrelationId?.ToString("D")),
                ct)
            .ConfigureAwait(false);

        issues.AddRange(draft.ValidationIssues.Select(ToCandidateIssue));

        return new PostingRuleJournalCandidateResultDto(
            dryRun,
            dryRun.SelectedRuleId,
            selectedRuleVersion,
            dryRun.GeneratedPostingLines,
            draft.Write?.PostingCommand,
            draft.DraftEntry?.JournalEntryId,
            draft.TotalDebits,
            draft.TotalCredits,
            draft.Imbalance,
            draft.IsBalanced,
            issues.Any(static issue => issue.BlocksCandidate),
            draft.CanSubmitForApproval,
            draft.CanPostWithoutAdditionalApproval,
            draft.EvidenceLinks,
            issues.OrderByDescending(static issue => issue.Severity)
                .ThenBy(static issue => issue.Code, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static PostingRuleJournalCandidateResultDto BuildBlockedResult(
        PostingRuleJournalCandidateRequestDto request,
        RuleDryRunResultDto dryRun,
        string? selectedRuleVersion,
        IReadOnlyList<PostingRuleJournalCandidateIssueDto> issues)
    {
        var totalDebits = dryRun.GeneratedPostingLines
            .Where(static line => line.Side == AccountingTemplateLineSideDto.Debit)
            .Sum(static line => Math.Abs(line.Amount));
        var totalCredits = dryRun.GeneratedPostingLines
            .Where(static line => line.Side == AccountingTemplateLineSideDto.Credit)
            .Sum(static line => Math.Abs(line.Amount));

        return new PostingRuleJournalCandidateResultDto(
            dryRun,
            dryRun.SelectedRuleId,
            selectedRuleVersion,
            dryRun.GeneratedPostingLines,
            PostingCommand: null,
            JournalEntryId: null,
            totalDebits,
            totalCredits,
            totalDebits - totalCredits,
            dryRun.IsPostingBalanced,
            HasBlockingIssues: true,
            CanSubmitForApproval: false,
            CanPostWithoutAdditionalApproval: false,
            request.EvidenceLinks,
            issues.OrderByDescending(static issue => issue.Severity)
                .ThenBy(static issue => issue.Code, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static Dictionary<string, ChartOfAccountsNodeDto> BuildChartByPath(
        IReadOnlyList<ChartOfAccountsNodeDto> chart)
        => chart
            .Where(static node => !string.IsNullOrWhiteSpace(node.Path))
            .GroupBy(static node => node.Path.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.Single(), StringComparer.OrdinalIgnoreCase);

    private static bool TryMapLedgerAccountType(string? accountType, out LedgerAccountType ledgerAccountType)
    {
        if (Enum.TryParse(accountType?.Trim(), ignoreCase: true, out ledgerAccountType))
        {
            return ledgerAccountType is LedgerAccountType.Asset
                or LedgerAccountType.Liability
                or LedgerAccountType.Equity
                or LedgerAccountType.Revenue
                or LedgerAccountType.Expense;
        }

        return false;
    }

    private static PostingRuleJournalCandidateIssueDto ToCandidateIssue(
        AccountingConfigurationValidationIssueDto issue)
        => new(
            issue.Code,
            issue.Severity,
            issue.Message,
            issue.Severity == AccountingConfigurationValidationSeverityDto.Critical,
            issue.TargetId,
            issue.SuggestedAction);

    private static PostingRuleJournalCandidateIssueDto Issue(
        string code,
        AccountingConfigurationValidationSeverityDto severity,
        string message,
        string? targetId,
        string? suggestedAction)
        => new(
            code,
            severity,
            message,
            severity == AccountingConfigurationValidationSeverityDto.Critical,
            targetId,
            suggestedAction);
}
