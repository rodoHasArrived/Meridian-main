using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Storage.Ledger;

namespace Meridian.FinancialOperations.Ledger;

public interface IAccountingPostingCandidateService
{
    Task<PostingRuleJournalCandidateResultDto> BuildCandidateAsync(
        PostingRuleJournalCandidateRequestDto request,
        CancellationToken ct = default);
}

public interface IAccountingPostingCandidateWriteBuilder
{
    Task<AccountingPostingCandidateWriteResult> BuildCandidateWriteAsync(
        PostingRuleJournalCandidateRequestDto request,
        CancellationToken ct = default);
}

public sealed record AccountingPostingCandidateWriteResult(
    PostingRuleJournalCandidateResultDto Candidate,
    LedgerJournalEntryWrite? Write);

public sealed class AccountingPostingCandidateService : IAccountingPostingCandidateService, IAccountingPostingCandidateWriteBuilder
{
    private readonly IAccountingConfigurationService _configurationService;
    private readonly IAccountingJournalDraftService _journalDraftService;
    private readonly ILedgerBookService? _ledgerBookService;
    private readonly IAccountingPolicyService? _accountingPolicyService;

    public AccountingPostingCandidateService(
        IAccountingConfigurationService configurationService,
        IAccountingJournalDraftService journalDraftService,
        ILedgerBookService? ledgerBookService = null,
        IAccountingPolicyService? accountingPolicyService = null)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _journalDraftService = journalDraftService ?? throw new ArgumentNullException(nameof(journalDraftService));
        _ledgerBookService = ledgerBookService;
        _accountingPolicyService = accountingPolicyService;
    }

    public async Task<PostingRuleJournalCandidateResultDto> BuildCandidateAsync(
        PostingRuleJournalCandidateRequestDto request,
        CancellationToken ct = default)
        => (await BuildCandidateWriteAsync(request, ct).ConfigureAwait(false)).Candidate;

    public async Task<AccountingPostingCandidateWriteResult> BuildCandidateWriteAsync(
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
                    request.CorrelationId?.ToString("D"),
                    request.TenantId,
                    request.CompanyId),
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

        await ValidateBookContextAsync(request, dryRun, issues, ct).ConfigureAwait(false);
        ValidateEconomicEvent(request, issues);
        ValidateProjectionLineage(request, issues);
        ValidateBookPosition(request, dryRun.GeneratedPostingLines, issues);
        await ValidateRulePackReferenceAsync(request, dryRun, selectedRuleVersion, issues, ct)
            .ConfigureAwait(false);

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
        else if (request.AggregateId != request.LedgerBookId.Value)
        {
            issues.Add(Issue(
                "posting-candidate.ledger-book-aggregate-required",
                AccountingConfigurationValidationSeverityDto.Critical,
                "A source-event posting candidate aggregate id must equal the target ledger book id before a governed journal draft can be built.",
                "aggregateId",
                "Use the target ledger book as the aggregate boundary for generated posting candidates; keep the source economic event in sourceEventId."));
        }

        if (issues.Any(static issue => issue.BlocksCandidate))
        {
            return new AccountingPostingCandidateWriteResult(
                BuildBlockedResult(request, dryRun, selectedRuleVersion, issues),
                Write: null);
        }

        var workspace = await _configurationService.GetWorkspaceAsync(request.FundProfileId, request.LedgerBookId, ct, request.TenantId, request.CompanyId)
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
            return new AccountingPostingCandidateWriteResult(
                BuildBlockedResult(request, dryRun, selectedRuleVersion, issues),
                Write: null);
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

        var postingCommand = draft.Write?.PostingCommand is { } command
            ? command with
            {
                BookContext = request.BookContext,
                BookPositionId = request.BookPositionId,
                EconomicEvent = request.EconomicEvent,
                ProjectionLineage = request.ProjectionLineage,
                RulePackReference = request.RulePackReference
            }
            : null;
        var write = draft.Write is null
            ? null
            : draft.Write with { PostingCommand = postingCommand };

        return new AccountingPostingCandidateWriteResult(
            new PostingRuleJournalCandidateResultDto(
                dryRun,
                dryRun.SelectedRuleId,
                selectedRuleVersion,
                dryRun.GeneratedPostingLines,
                postingCommand,
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
                    .ToArray())
            {
                BookContext = request.BookContext,
                BookPositionId = request.BookPositionId,
                EconomicEvent = request.EconomicEvent,
                ProjectionLineage = request.ProjectionLineage,
                RulePackReference = request.RulePackReference
            },
            write);
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
                .ToArray())
        {
            BookContext = request.BookContext,
            BookPositionId = request.BookPositionId,
            EconomicEvent = request.EconomicEvent,
            ProjectionLineage = request.ProjectionLineage,
            RulePackReference = request.RulePackReference
        };
    }

    private async Task ValidateBookContextAsync(
        PostingRuleJournalCandidateRequestDto request,
        RuleDryRunResultDto dryRun,
        List<PostingRuleJournalCandidateIssueDto> issues,
        CancellationToken ct)
    {
        var context = request.BookContext;
        if (context is null)
        {
            return;
        }

        AddMismatch(
            issues,
            request.LedgerBookId == context.LedgerBookId,
            "posting-candidate.book-context-ledger-book-mismatch",
            "Typed book context ledger book must match the candidate ledger book.",
            "bookContext.ledgerBookId");
        AddMismatch(
            issues,
            request.AggregateId == context.LedgerBookId,
            "posting-candidate.book-context-aggregate-mismatch",
            "Typed book context ledger book must be the candidate aggregate boundary.",
            "aggregateId");
        AddMismatch(
            issues,
            !context.PeriodId.HasValue || request.PeriodId == context.PeriodId.Value,
            "posting-candidate.book-context-period-mismatch",
            "Typed book context period must match the candidate period.",
            "bookContext.periodId");
        AddMismatch(
            issues,
            request.AccountingBasis == context.AccountingBasis,
            "posting-candidate.book-context-basis-mismatch",
            "Typed book context accounting basis must match the candidate basis.",
            "bookContext.accountingBasis");
        AddMismatch(
            issues,
            TextEquals(request.PolicyId, context.AccountingPolicyId),
            "posting-candidate.book-context-policy-mismatch",
            "Typed book context accounting policy must match the candidate policy.",
            "bookContext.accountingPolicyId");
        AddMismatch(
            issues,
            TextEquals(request.FundProfileId, context.FundProfileId),
            "posting-candidate.book-context-fund-mismatch",
            "Typed book context fund profile must match the candidate fund profile.",
            "bookContext.fundProfileId");
        AddMismatch(
            issues,
            TextEquals(request.Currency, context.BaseCurrency),
            "posting-candidate.book-context-currency-mismatch",
            "Typed book context base currency must match the candidate currency.",
            "bookContext.baseCurrency");
        AddMismatch(
            issues,
            dryRun.LedgerBookId == context.LedgerBookId,
            "posting-candidate.book-context-dry-run-mismatch",
            "Rules Studio must evaluate the same ledger book asserted by typed book context.",
            "dryRunResult.ledgerBookId");
        ValidateDimensionBook(context.LedgerBookId, request.Dimensions?.BookId, "dimensions.bookId", issues);
        ValidateDimensionBook(context.LedgerBookId, context.Dimensions?.BookId, "bookContext.dimensions.bookId", issues);
        ValidateDimensionAssertions(context.Dimensions, request.Dimensions, issues);

        if (_ledgerBookService is null)
        {
            issues.Add(Issue(
                "posting-candidate.book-context-resolver-required",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Typed book context cannot be trusted without an authoritative ledger-book resolver.",
                "bookContext",
                "Register ILedgerBookService before accepting typed book context."));
            return;
        }

        LedgerBookDto? book;
        try
        {
            book = await _ledgerBookService.GetBookAsync(context.LedgerBookId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            issues.Add(Issue(
                "posting-candidate.book-context-resolution-failed",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Authoritative ledger-book context could not be resolved: {ex.Message}",
                context.LedgerBookId.ToString("D"),
                "Restore ledger-book resolution before building the candidate."));
            return;
        }

        if (book is null)
        {
            issues.Add(Issue(
                "posting-candidate.book-context-not-found",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Ledger book '{context.LedgerBookId:D}' was not found.",
                context.LedgerBookId.ToString("D"),
                "Select an authoritative ledger book before building the candidate."));
            return;
        }

        AddAuthoritativeBookMismatches(context, book, issues);
        ValidateAuthoritativeDimensions(request, context, book, dryRun.GeneratedPostingLines, issues);
        await ValidateAuthoritativePeriodAsync(request.PeriodId, context, issues, ct).ConfigureAwait(false);
    }

    private async Task ValidateAuthoritativePeriodAsync(
        Guid requestPeriodId,
        AccountingBookContextDto context,
        List<PostingRuleJournalCandidateIssueDto> issues,
        CancellationToken ct)
    {
        try
        {
            var periods = await _ledgerBookService!
                .ListPeriodsAsync(new LedgerPeriodQuery(LedgerBookId: context.LedgerBookId), ct)
                .ConfigureAwait(false);
            var period = periods.FirstOrDefault(candidate => candidate.PeriodId == requestPeriodId);
            if (period is null)
            {
                issues.Add(Issue(
                    "posting-candidate.book-context-period-not-found",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Candidate period '{requestPeriodId:D}' does not belong to ledger book '{context.LedgerBookId:D}'.",
                    "periodId",
                    "Resolve a period owned by the selected ledger book."));
                return;
            }

            AddMismatch(
                issues,
                period.LedgerBookId == context.LedgerBookId,
                "posting-candidate.book-context-period-ledger-book-mismatch",
                "Authoritative period must belong to the typed ledger book.",
                "periodId");
            AddMismatch(
                issues,
                period.AccountingBasis == context.AccountingBasis,
                "posting-candidate.book-context-period-basis-mismatch",
                "Authoritative period basis does not match typed book context.",
                "bookContext.periodId");
            AddMismatch(
                issues,
                TextEquals(period.AccountingPolicyId, context.AccountingPolicyId) &&
                TextEquals(period.AccountingPolicyVersion, context.AccountingPolicyVersion),
                "posting-candidate.book-context-period-policy-mismatch",
                "Authoritative period policy does not match typed book context.",
                "bookContext.periodId");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            issues.Add(Issue(
                "posting-candidate.book-context-period-resolution-failed",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Authoritative ledger period could not be resolved: {ex.Message}",
                "bookContext.periodId",
                "Restore ledger-period resolution before building the candidate."));
        }
    }

    private static void ValidateAuthoritativeDimensions(
        PostingRuleJournalCandidateRequestDto request,
        AccountingBookContextDto context,
        LedgerBookDto book,
        IReadOnlyList<GeneratedPostingLineDto> generatedLines,
        List<PostingRuleJournalCandidateIssueDto> issues)
    {
        var authoritativeBookDimension = book.LedgerBookId.ToString("D");

        AddMismatch(
            issues,
            TextEquals(request.Dimensions?.FundId, book.FundProfileId),
            "posting-candidate.book-context-request-fund-dimension-mismatch",
            "Candidate fund dimension must match the authoritative ledger-book fund.",
            "dimensions.fundId");
        AddMismatch(
            issues,
            TextEquals(request.Dimensions?.BookId, authoritativeBookDimension),
            "posting-candidate.book-context-request-book-dimension-mismatch",
            "Candidate book dimension must match the authoritative ledger book.",
            "dimensions.bookId");

        if (context.Dimensions is { } contextDimensions)
        {
            AddMismatch(
                issues,
                TextEquals(contextDimensions.FundId, book.FundProfileId),
                "posting-candidate.book-context-fund-dimension-mismatch",
                "Typed book-context fund dimension must match the authoritative ledger-book fund.",
                "bookContext.dimensions.fundId");
            AddMismatch(
                issues,
                TextEquals(contextDimensions.BookId, authoritativeBookDimension),
                "posting-candidate.book-context-book-dimension-mismatch",
                "Typed book-context book dimension must match the authoritative ledger book.",
                "bookContext.dimensions.bookId");
        }

        var instrumentId = context.Dimensions?.InstrumentId
            ?? request.EconomicEvent?.SecurityId
            ?? request.Dimensions?.InstrumentId;
        var positionId = context.Dimensions?.PositionId
            ?? request.BookPositionId
            ?? request.Dimensions?.PositionId;

        if (instrumentId.HasValue)
        {
            AddMismatch(
                issues,
                request.Dimensions?.InstrumentId == instrumentId,
                "posting-candidate.book-context-request-instrument-dimension-mismatch",
                "Candidate instrument dimension must match the typed book context and economic event.",
                "dimensions.instrumentId");
        }

        if (positionId.HasValue)
        {
            AddMismatch(
                issues,
                request.Dimensions?.PositionId == positionId,
                "posting-candidate.book-context-request-position-dimension-mismatch",
                "Candidate position dimension must match the typed book context and position reference.",
                "dimensions.positionId");
        }

        for (var index = 0; index < generatedLines.Count; index++)
        {
            var dimensions = generatedLines[index].Dimensions;
            AddMismatch(
                issues,
                TextEquals(dimensions?.FundId, book.FundProfileId),
                "posting-candidate.book-context-generated-line-fund-mismatch",
                "Every generated posting line fund dimension must match the authoritative ledger-book fund.",
                $"generatedPostingLines[{index}].dimensions.fundId");
            AddMismatch(
                issues,
                TextEquals(dimensions?.BookId, authoritativeBookDimension),
                "posting-candidate.book-context-generated-line-book-mismatch",
                "Every generated posting line book dimension must match the authoritative ledger book.",
                $"generatedPostingLines[{index}].dimensions.bookId");

            if (instrumentId.HasValue)
            {
                AddMismatch(
                    issues,
                    dimensions?.InstrumentId == instrumentId,
                    "posting-candidate.book-context-generated-line-instrument-mismatch",
                    "Every generated posting line instrument dimension must match the typed book context.",
                    $"generatedPostingLines[{index}].dimensions.instrumentId");
            }

            if (positionId.HasValue)
            {
                AddMismatch(
                    issues,
                    dimensions?.PositionId == positionId,
                    "posting-candidate.book-context-generated-line-position-mismatch",
                    "Every generated posting line position dimension must match the typed book context.",
                    $"generatedPostingLines[{index}].dimensions.positionId");
            }
        }
    }

    private static void AddAuthoritativeBookMismatches(
        AccountingBookContextDto context,
        LedgerBookDto book,
        List<PostingRuleJournalCandidateIssueDto> issues)
    {
        AddMismatch(issues, book.LedgerBookId == context.LedgerBookId, "posting-candidate.book-context-authoritative-id-mismatch", "Authoritative ledger-book id does not match typed book context.", "bookContext.ledgerBookId");
        AddMismatch(issues, TextEquals(book.FundProfileId, context.FundProfileId), "posting-candidate.book-context-authoritative-fund-mismatch", "Authoritative ledger-book fund profile does not match typed book context.", "bookContext.fundProfileId");
        AddMismatch(issues, book.FundStructureNodeId == context.FundStructureNodeId, "posting-candidate.book-context-authoritative-node-mismatch", "Authoritative ledger-book owner node does not match typed book context.", "bookContext.fundStructureNodeId");
        AddMismatch(issues, book.FundStructureNodeKind == context.FundStructureNodeKind, "posting-candidate.book-context-authoritative-node-kind-mismatch", "Authoritative ledger-book owner kind does not match typed book context.", "bookContext.fundStructureNodeKind");
        AddMismatch(issues, TextEquals(book.BaseCurrency, context.BaseCurrency), "posting-candidate.book-context-authoritative-currency-mismatch", "Authoritative ledger-book currency does not match typed book context.", "bookContext.baseCurrency");
        AddMismatch(issues, book.AccountingBasis == context.AccountingBasis, "posting-candidate.book-context-authoritative-basis-mismatch", "Authoritative ledger-book basis does not match typed book context.", "bookContext.accountingBasis");
        AddMismatch(
            issues,
            TextEquals(book.AccountingPolicyId, context.AccountingPolicyId) &&
            TextEquals(book.AccountingPolicyVersion, context.AccountingPolicyVersion),
            "posting-candidate.book-context-authoritative-policy-mismatch",
            "Authoritative ledger-book policy id/version does not match typed book context.",
            "bookContext.accountingPolicyId");
    }

    private static void ValidateEconomicEvent(
        PostingRuleJournalCandidateRequestDto request,
        List<PostingRuleJournalCandidateIssueDto> issues)
    {
        if (request.EconomicEvent is null)
        {
            return;
        }

        ValidateEventAgainstLegacy(request, request.EconomicEvent, "economicEvent", issues);
        ValidateEventInstrument(request, request.EconomicEvent, "economicEvent.securityId", issues);
        var eventEvidence = NormalizeEvidence(request.EconomicEvent.EvidenceLinks);
        if (eventEvidence.Count == 0)
        {
            issues.Add(Issue(
                "posting-candidate.economic-event-evidence-required",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Typed economic events require retained source evidence.",
                "economicEvent.evidenceLinks",
                "Attach the retained evidence used to identify and calculate the economic event."));
            return;
        }

        var candidateEvidence = NormalizeEvidence(request.EvidenceLinks);
        if (eventEvidence.Any(link => !candidateEvidence.Contains(link)))
        {
            issues.Add(Issue(
                "posting-candidate.economic-event-evidence-mismatch",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Every typed economic-event evidence link must also be retained by the posting candidate.",
                "economicEvent.evidenceLinks",
                "Copy the economic-event evidence links into the candidate evidence collection."));
        }
    }

    private static void ValidateProjectionLineage(
        PostingRuleJournalCandidateRequestDto request,
        List<PostingRuleJournalCandidateIssueDto> issues)
    {
        var lineage = request.ProjectionLineage;
        if (lineage is null)
        {
            return;
        }

        var trigger = lineage.TriggerEvent;
        if (trigger is null)
        {
            issues.Add(Issue(
                "posting-candidate.projection-trigger-required",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Projection lineage requires a trigger economic event.",
                "projectionLineage.triggerEvent",
                "Attach the source economic event that triggered the projection."));
            return;
        }

        ValidateEventAgainstLegacy(request, trigger, "projectionLineage.triggerEvent", issues);
        ValidateEventInstrument(request, trigger, "projectionLineage.triggerEvent.securityId", issues);
        if (request.EconomicEvent is { } economicEvent)
        {
            AddMismatch(
                issues,
                EventsMatch(economicEvent, trigger),
                "posting-candidate.projection-trigger-mismatch",
                "Projection trigger event must match the typed economic event.",
                "projectionLineage.triggerEvent");
        }
    }

    private static void ValidateBookPosition(
        PostingRuleJournalCandidateRequestDto request,
        IReadOnlyList<GeneratedPostingLineDto> generatedLines,
        List<PostingRuleJournalCandidateIssueDto> issues)
    {
        var typedPositionIds = new[]
        {
            request.EconomicEvent?.BookPositionId,
            request.ProjectionLineage?.BookPositionId,
            request.ProjectionLineage?.TriggerEvent?.BookPositionId
        };
        if (!request.BookPositionId.HasValue)
        {
            AddMismatch(
                issues,
                typedPositionIds.All(static positionId => !positionId.HasValue),
                "posting-candidate.book-position-header-required",
                "Typed event or projection position identity requires a candidate book-position id.",
                "bookPositionId");
            return;
        }

        var positionId = request.BookPositionId.Value;
        var candidatePositionId = request.Dimensions?.PositionId;
        AddMismatch(
            issues,
            positionId != Guid.Empty,
            "posting-candidate.book-position-required",
            "Typed book-position id cannot be empty.",
            "bookPositionId");
        AddMismatch(
            issues,
            candidatePositionId.HasValue && candidatePositionId.Value == positionId,
            "posting-candidate.book-position-dimension-mismatch",
            "Typed book-position id requires a matching candidate position dimension.",
            "dimensions.positionId");

        for (var index = 0; index < generatedLines.Count; index++)
        {
            var generatedPositionId = generatedLines[index].Dimensions?.PositionId;
            AddMismatch(
                issues,
                generatedPositionId.HasValue && generatedPositionId == positionId,
                "posting-candidate.book-position-generated-line-mismatch",
                "Every generated posting line requires a position dimension matching the typed book-position id.",
                $"generatedPostingLines[{index}].dimensions.positionId");
        }

        ValidateExplicitPosition(positionId, request.EconomicEvent?.BookPositionId, "economicEvent.bookPositionId", issues);
        ValidateExplicitPosition(positionId, request.ProjectionLineage?.BookPositionId, "projectionLineage.bookPositionId", issues);
        ValidateExplicitPosition(positionId, request.ProjectionLineage?.TriggerEvent?.BookPositionId, "projectionLineage.triggerEvent.bookPositionId", issues);
    }

    private async Task ValidateRulePackReferenceAsync(
        PostingRuleJournalCandidateRequestDto request,
        RuleDryRunResultDto dryRun,
        string? selectedRuleVersion,
        List<PostingRuleJournalCandidateIssueDto> issues,
        CancellationToken ct)
    {
        var reference = request.RulePackReference;
        if (reference is null)
        {
            return;
        }

        AddMismatch(
            issues,
            TextEquals(reference.SelectedRuleId, dryRun.SelectedRuleId),
            "posting-candidate.rule-pack-selected-rule-mismatch",
            "Rule-pack reference selected rule must match the Rules Studio dry-run selection.",
            "rulePackReference.selectedRuleId");
        AddMismatch(
            issues,
            TextEquals(reference.SelectedRuleVersion, selectedRuleVersion),
            "posting-candidate.rule-pack-selected-version-mismatch",
            "Rule-pack reference selected rule version must match the Rules Studio dry-run selection.",
            "rulePackReference.selectedRuleVersion");

        if (_accountingPolicyService is null)
        {
            issues.Add(Issue(
                "posting-candidate.rule-pack-resolver-required",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Typed rule-pack references cannot be trusted without an authoritative accounting-policy resolver.",
                "rulePackReference",
                "Register IAccountingPolicyService before accepting typed rule-pack references."));
            return;
        }

        AccountingPolicyDto policy;
        try
        {
            policy = await _accountingPolicyService.ResolvePolicyAsync(
                    new AccountingPolicyQuery(
                        request.AccountingBasis,
                        request.EffectiveDate,
                        request.BookContext?.AccountingPolicyId ?? request.PolicyId,
                        request.FundProfileId,
                        request.BookContext?.FundStructureNodeId,
                        request.Dimensions?.InstrumentId?.ToString("D"),
                        request.SourceEventId),
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            issues.Add(Issue(
                "posting-candidate.rule-pack-resolution-failed",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Authoritative accounting rule pack could not be resolved: {ex.Message}",
                "rulePackReference",
                "Resolve an effective accounting policy and promoted rule pack before building the candidate."));
            return;
        }

        if (request.BookContext is { } context)
        {
            AddMismatch(
                issues,
                TextEquals(policy.PolicyId, context.AccountingPolicyId) &&
                TextEquals(policy.Version, context.AccountingPolicyVersion),
                "posting-candidate.rule-pack-policy-mismatch",
                "Resolved accounting policy id/version must match typed book context.",
                "rulePackReference");
        }

        var rulePack = policy.RulePack;
        AddMismatch(
            issues,
            rulePack is not null && TextEquals(reference.RulePackId, rulePack.RulePackId),
            "posting-candidate.rule-pack-id-mismatch",
            "Rule-pack reference id must match the authoritative accounting policy rule pack.",
            "rulePackReference.rulePackId");
        AddMismatch(
            issues,
            rulePack is not null && TextEquals(reference.RulePackVersion, rulePack.RulePackVersion),
            "posting-candidate.rule-pack-version-mismatch",
            "Rule-pack reference version must match the authoritative accounting policy rule pack.",
            "rulePackReference.rulePackVersion");
        AddMismatch(
            issues,
            rulePack?.Rules.Any(rule =>
                TextEquals(rule.RuleId, reference.SelectedRuleId) &&
                TextEquals(rule.RuleVersion, reference.SelectedRuleVersion)) == true,
            "posting-candidate.rule-pack-selected-rule-membership-mismatch",
            "The selected rule id/version must belong to the authoritative accounting policy rule pack.",
            "rulePackReference.selectedRuleId");
    }

    private static void ValidateEventAgainstLegacy(
        PostingRuleJournalCandidateRequestDto request,
        EconomicEventReferenceDto eventReference,
        string target,
        List<PostingRuleJournalCandidateIssueDto> issues)
    {
        AddMismatch(issues, request.SourceEventId == eventReference.EventId, "posting-candidate.economic-event-id-mismatch", "Typed economic-event id must match legacy source-event id.", $"{target}.eventId");
        AddMismatch(issues, TextEquals(request.SourceEventType, eventReference.EventType), "posting-candidate.economic-event-type-mismatch", "Typed economic-event type must match legacy source-event type.", $"{target}.eventType");
        AddMismatch(issues, request.EffectiveDate == eventReference.EffectiveDate, "posting-candidate.economic-event-date-mismatch", "Typed economic-event effective date must match the candidate effective date.", $"{target}.effectiveDate");
        AddMismatch(issues, request.CorrelationId == eventReference.CorrelationId, "posting-candidate.economic-event-correlation-mismatch", "Typed economic-event correlation id must match the candidate correlation id.", $"{target}.correlationId");
    }

    private static void ValidateDimensionBook(
        Guid ledgerBookId,
        string? dimensionBookId,
        string target,
        List<PostingRuleJournalCandidateIssueDto> issues)
    {
        if (string.IsNullOrWhiteSpace(dimensionBookId))
        {
            return;
        }

        AddMismatch(
            issues,
            string.Equals(dimensionBookId.Trim(), ledgerBookId.ToString("D"), StringComparison.OrdinalIgnoreCase),
            "posting-candidate.book-context-dimension-book-mismatch",
            "Supplied dimension book must match typed book context.",
            target);
    }

    private static void ValidateDimensionAssertions(
        LedgerDimensionSetDto? typed,
        LedgerDimensionSetDto? candidate,
        List<PostingRuleJournalCandidateIssueDto> issues)
    {
        if (typed is null)
        {
            return;
        }

        ValidateOptionalDimension(typed.FundId, candidate?.FundId, "fundId", issues);
        ValidateOptionalDimension(typed.EntityId, candidate?.EntityId, "entityId", issues);
        ValidateOptionalDimension(typed.SleeveId, candidate?.SleeveId, "sleeveId", issues);
        ValidateOptionalDimension(typed.StrategyId, candidate?.StrategyId, "strategyId", issues);
        ValidateOptionalDimension(typed.InvestorId, candidate?.InvestorId, "investorId", issues);
        ValidateOptionalDimension(typed.CapitalAccountId, candidate?.CapitalAccountId, "capitalAccountId", issues);
        ValidateOptionalDimension(typed.InstrumentId, candidate?.InstrumentId, "instrumentId", issues);
        ValidateOptionalDimension(typed.PositionId, candidate?.PositionId, "positionId", issues);
        ValidateOptionalDimension(typed.TaxLotId, candidate?.TaxLotId, "taxLotId", issues);
        ValidateOptionalDimension(typed.CostCenterId, candidate?.CostCenterId, "costCenterId", issues);
        ValidateOptionalDimension(typed.CounterpartyId, candidate?.CounterpartyId, "counterpartyId", issues);
        ValidateOptionalDimension(typed.OrganizationId, candidate?.OrganizationId, "organizationId", issues);
        ValidateOptionalDimension(typed.PortfolioId, candidate?.PortfolioId, "portfolioId", issues);
        ValidateOptionalDimension(typed.BookId, candidate?.BookId, "bookId", issues);
        ValidateOptionalDimension(typed.AccountId, candidate?.AccountId, "accountId", issues);
        ValidateOptionalDimension(typed.CustomerId, candidate?.CustomerId, "customerId", issues);
        ValidateOptionalDimension(typed.VendorId, candidate?.VendorId, "vendorId", issues);
        ValidateOptionalDimension(typed.ProjectId, candidate?.ProjectId, "projectId", issues);

        foreach (var pair in typed.ExternalGlDimensions)
        {
            var matches = candidate?.ExternalGlDimensions.TryGetValue(pair.Key, out var candidateValue) == true &&
                          TextEquals(pair.Value, candidateValue);
            AddMismatch(
                issues,
                matches,
                "posting-candidate.book-context-dimensions-mismatch",
                $"Typed book-context external-GL dimension '{pair.Key}' must match candidate dimensions.",
                $"bookContext.dimensions.externalGlDimensions.{pair.Key}");
        }
    }

    private static void ValidateOptionalDimension(
        string? typed,
        string? candidate,
        string field,
        List<PostingRuleJournalCandidateIssueDto> issues)
    {
        if (string.IsNullOrWhiteSpace(typed))
        {
            return;
        }

        AddMismatch(
            issues,
            TextEquals(typed, candidate),
            "posting-candidate.book-context-dimensions-mismatch",
            $"Typed book-context dimension '{field}' must match candidate dimensions.",
            $"bookContext.dimensions.{field}");
    }

    private static void ValidateOptionalDimension(
        Guid? typed,
        Guid? candidate,
        string field,
        List<PostingRuleJournalCandidateIssueDto> issues)
    {
        if (!typed.HasValue)
        {
            return;
        }

        AddMismatch(
            issues,
            candidate.HasValue && typed.Value == candidate.Value,
            "posting-candidate.book-context-dimensions-mismatch",
            $"Typed book-context dimension '{field}' must match candidate dimensions.",
            $"bookContext.dimensions.{field}");
    }

    private static void ValidateExplicitPosition(
        Guid candidatePositionId,
        Guid? typedPositionId,
        string target,
        List<PostingRuleJournalCandidateIssueDto> issues)
    {
        if (!typedPositionId.HasValue)
        {
            return;
        }

        AddMismatch(
            issues,
            typedPositionId.Value == candidatePositionId,
            "posting-candidate.book-position-lineage-mismatch",
            "Typed event and projection position identity must match the candidate book-position id.",
            target);
    }

    private static void ValidateEventInstrument(
        PostingRuleJournalCandidateRequestDto request,
        EconomicEventReferenceDto eventReference,
        string target,
        List<PostingRuleJournalCandidateIssueDto> issues)
    {
        if (!eventReference.SecurityId.HasValue)
        {
            return;
        }

        AddMismatch(
            issues,
            request.Dimensions?.InstrumentId == eventReference.SecurityId,
            "posting-candidate.economic-event-instrument-mismatch",
            "Typed economic-event security id must match the candidate instrument dimension.",
            target);
    }

    private static bool EventsMatch(EconomicEventReferenceDto left, EconomicEventReferenceDto right)
        => left.EventId == right.EventId &&
           TextEquals(left.EventType, right.EventType) &&
           left.EventVersion == right.EventVersion &&
           left.EffectiveDate == right.EffectiveDate &&
           left.CorrelationId == right.CorrelationId &&
           left.CausationId == right.CausationId &&
           left.SecurityId == right.SecurityId &&
           left.BookPositionId == right.BookPositionId &&
           TextEquals(left.SourceDomain, right.SourceDomain) &&
           OptionalTextEquals(left.SourceEntityId, right.SourceEntityId) &&
           OptionalTextEquals(left.SourceContentHash, right.SourceContentHash);

    private static HashSet<string> NormalizeEvidence(IReadOnlyList<string>? links)
        => (links ?? [])
            .Where(static link => !string.IsNullOrWhiteSpace(link))
            .Select(static link => link.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool TextEquals(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left) &&
           !string.IsNullOrWhiteSpace(right) &&
           string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool OptionalTextEquals(string? left, string? right)
        => string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right) ||
           TextEquals(left, right);

    private static void AddMismatch(
        List<PostingRuleJournalCandidateIssueDto> issues,
        bool isValid,
        string code,
        string message,
        string targetId)
    {
        if (isValid)
        {
            return;
        }

        issues.Add(Issue(
            code,
            AccountingConfigurationValidationSeverityDto.Critical,
            message,
            targetId,
            "Align the typed assertion with the authoritative and legacy candidate fields before retrying."));
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
