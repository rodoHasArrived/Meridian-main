using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Instruments.AssetOperations;
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

/// <summary>
/// Internal-process authority handoff used only after the Asset Accounting Event Spine has
/// resolved immutable Asset Operations, Security Master, ledger-book, period, evidence, model,
/// and rule-pack state. Generic candidate callers cannot opt canonical asset events into this path.
/// </summary>
public interface IAccountingPostingCandidateAuthorityBuilder
{
    Task<AccountingPostingCandidateWriteResult> BuildAuthoritativeCandidateWriteAsync(
        PostingRuleJournalCandidateRequestDto request,
        AssetAccountingCandidateAuthorityContext authority,
        CancellationToken ct = default);
}

public sealed record AssetAccountingCandidateAuthorityContext(
    Guid EventId,
    long EventVersion,
    long SourceSpineVersion,
    long DraftSpineVersion,
    string SourceProjectionFingerprint,
    AssetAccountingEventKindDto EventKind,
    Guid SecurityId,
    Guid BookPositionId,
    long ExpectedBookPositionVersion,
    Guid LedgerBookId,
    Guid PeriodId,
    long ExpectedPeriodVersion,
    string RulePackId,
    string RulePackVersion);

public sealed record AccountingPostingCandidateWriteResult(
    PostingRuleJournalCandidateResultDto Candidate,
    LedgerJournalEntryWrite? Write);

public sealed class AccountingPostingCandidateService :
    IAccountingPostingCandidateService,
    IAccountingPostingCandidateWriteBuilder,
    IAccountingPostingCandidateAuthorityBuilder
{
    private readonly IAccountingConfigurationService _configurationService;
    private readonly IAccountingJournalDraftService _journalDraftService;
    private readonly ILedgerBookService? _ledgerBookService;
    private readonly IAccountingPolicyService? _accountingPolicyService;
    private readonly IAssetOperationsQueryService? _assetOperationsQueryService;
    private readonly IFactorPaydownProjectionService? _factorPaydownProjector;
    private readonly ILedgerJournalStore? _taxLotStore;

    public AccountingPostingCandidateService(
        IAccountingConfigurationService configurationService,
        IAccountingJournalDraftService journalDraftService,
        ILedgerBookService? ledgerBookService = null,
        IAccountingPolicyService? accountingPolicyService = null,
        IAssetOperationsQueryService? assetOperationsQueryService = null,
        IFactorPaydownProjectionService? factorPaydownProjector = null,
        ILedgerJournalStore? taxLotStore = null)
    {
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
        _journalDraftService = journalDraftService ?? throw new ArgumentNullException(nameof(journalDraftService));
        _ledgerBookService = ledgerBookService;
        _accountingPolicyService = accountingPolicyService;
        _assetOperationsQueryService = assetOperationsQueryService;
        _factorPaydownProjector = factorPaydownProjector;
        _taxLotStore = taxLotStore;
    }

    public async Task<PostingRuleJournalCandidateResultDto> BuildCandidateAsync(
        PostingRuleJournalCandidateRequestDto request,
        CancellationToken ct = default)
        => (await BuildCandidateWriteAsync(request, ct).ConfigureAwait(false)).Candidate;

    public async Task<AccountingPostingCandidateWriteResult> BuildCandidateWriteAsync(
        PostingRuleJournalCandidateRequestDto request,
        CancellationToken ct = default)
        => await BuildCandidateWriteCoreAsync(request, authority: null, ct).ConfigureAwait(false);

    public async Task<AccountingPostingCandidateWriteResult> BuildAuthoritativeCandidateWriteAsync(
        PostingRuleJournalCandidateRequestDto request,
        AssetAccountingCandidateAuthorityContext authority,
        CancellationToken ct = default)
        => await BuildCandidateWriteCoreAsync(request, authority, ct).ConfigureAwait(false);

    private async Task<AccountingPostingCandidateWriteResult> BuildCandidateWriteCoreAsync(
        PostingRuleJournalCandidateRequestDto request,
        AssetAccountingCandidateAuthorityContext? authority,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var authorityIssues = ValidateAssetAccountingAuthority(request, authority);
        if (authorityIssues.Any(static issue => issue.BlocksCandidate))
        {
            return BuildPreDryRunBlockedResult(request, authorityIssues);
        }

        var typedProjectionIssues = new List<PostingRuleJournalCandidateIssueDto>();
        var authoritativeEventAmount = await ResolveTypedInstrumentProjectionAsync(
            request,
            typedProjectionIssues,
            ct).ConfigureAwait(false);

        var dryRun = await _configurationService.DryRunPostingRuleAsync(
                new RuleDryRunRequestDto(
                    request.FundProfileId,
                    request.SourceEventType,
                    authoritativeEventAmount ?? request.EventAmount,
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
        issues.AddRange(typedProjectionIssues);
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
                ExpectedVersion = authority?.ExpectedPeriodVersion ?? command.ExpectedVersion,
                Evidence = MergeRetainedEvidence(command.Evidence, request.RetainedEvidence),
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

    private async Task<decimal?> ResolveTypedInstrumentProjectionAsync(
        PostingRuleJournalCandidateRequestDto request,
        List<PostingRuleJournalCandidateIssueDto> issues,
        CancellationToken ct)
    {
        var requestedLineage = request.ProjectionLineage;
        if (!IsFactorPaydownRequest(request))
        {
            return null;
        }

        if (request.EconomicEvent is null)
        {
            issues.Add(Issue(
                "posting-candidate.instrument-economic-event-required",
                AccountingConfigurationValidationSeverityDto.Critical,
                "MBS factor-paydown candidates require a typed economic event.",
                "economicEvent",
                "Attach the server-produced factor-paydown economic event before creating the candidate."));
            return null;
        }

        if (requestedLineage is null ||
            !TextEquals(requestedLineage.ModelKey, FactorPaydownProjectionService.ModelKey))
        {
            issues.Add(Issue(
                "posting-candidate.instrument-factor-lineage-required",
                AccountingConfigurationValidationSeverityDto.Critical,
                "MBS factor-paydown candidates require lineage from the registered factor-paydown projection model.",
                "projectionLineage.modelKey",
                "Attach the authoritative Asset Operations factor-paydown lineage; client-selected model keys are not accepted."));
            return null;
        }

        if (!TextEquals(request.EconomicEvent.EventType, FactorPaydownProjectionService.EventType) ||
            !TextEquals(request.SourceEventType, FactorPaydownProjectionService.EventType))
        {
            issues.Add(Issue(
                "posting-candidate.instrument-factor-event-type-mismatch",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Factor-paydown lineage may only be used with the canonical MBS factor-paydown event type.",
                "economicEvent.eventType",
                "Use the event type emitted by the registered factor-paydown projector."));
            return null;
        }

        if (_assetOperationsQueryService is null || _factorPaydownProjector is null || _taxLotStore is null)
        {
            issues.Add(Issue(
                "posting-candidate.instrument-projection-resolver-required",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Typed factor-paydown candidates require authoritative Asset Operations, projection, and lot-of-record resolvers.",
                "projectionLineage.modelKey",
                "Register IAssetOperationsQueryService, IFactorPaydownProjectionService, and ILedgerJournalStore before accepting typed instrument events."));
            return null;
        }

        var requestedEvent = request.EconomicEvent;
        if (requestedEvent?.SecurityId is not Guid securityId || securityId == Guid.Empty)
        {
            issues.Add(Issue(
                "posting-candidate.instrument-security-required",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Typed factor-paydown candidates require a Security Master identity.",
                "economicEvent.securityId",
                "Attach the persisted Security Master identity to the economic event."));
            return null;
        }

        if (request.BookPositionId is not Guid positionId || positionId == Guid.Empty)
        {
            issues.Add(Issue(
                "posting-candidate.instrument-position-required",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Typed factor-paydown candidates require a persisted book-position identity.",
                "bookPositionId",
                "Attach the persisted Asset Operations book position."));
            return null;
        }

        AssetOperationsDetailDto? detail;
        try
        {
            detail = await _assetOperationsQueryService.GetOperationsAsync(securityId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            issues.Add(Issue(
                "posting-candidate.instrument-projection-resolution-failed",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Authoritative Asset Operations projection resolution failed: {ex.Message}",
                securityId.ToString("D"),
                "Restore Asset Operations projection resolution before creating the candidate."));
            return null;
        }

        if (detail is null)
        {
            issues.Add(Issue(
                "posting-candidate.instrument-projection-not-found",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"No Asset Operations projection exists for Security Master identity '{securityId:D}'.",
                securityId.ToString("D"),
                "Publish the governed role, position, economic state, and lineage before creating the candidate."));
            return null;
        }

        var matchingPositions = detail.BookPositions
            .Where(candidate => candidate.PositionId == positionId)
            .ToArray();
        if (matchingPositions.Length != 1)
        {
            issues.Add(Issue(
                matchingPositions.Length == 0
                    ? "posting-candidate.instrument-position-not-found"
                    : "posting-candidate.instrument-position-ambiguous",
                AccountingConfigurationValidationSeverityDto.Critical,
                matchingPositions.Length == 0
                    ? $"Book position '{positionId:D}' was not found in Asset Operations."
                    : $"Book position '{positionId:D}' resolves to multiple Asset Operations projections.",
                "bookPositionId",
                "Use a persisted position owned by the selected Security Master record and ledger book."));
            return null;
        }

        var position = matchingPositions[0];

        var matchingRoles = detail.InstrumentRoles
            .Where(candidate => candidate.RoleId == position.RoleId)
            .ToArray();
        var role = matchingRoles.Length == 1 ? matchingRoles[0] : null;
        if (role is null || !TextEquals(role.RoleKind, InstrumentRoleKinds.Holder))
        {
            issues.Add(Issue(
                "posting-candidate.instrument-holder-role-required",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Book position '{positionId:D}' does not resolve to an active holder role.",
                "bookPosition.roleId",
                "Publish an effective holder role for the book position before creating the candidate."));
            return null;
        }

        var state = detail.PositionEconomicStates
            .Where(candidate => candidate.PositionId == positionId && candidate.AsOfDate == requestedEvent.EffectiveDate)
            .OrderByDescending(static candidate => candidate.Version)
            .FirstOrDefault() ?? position.CurrentEconomicState;
        if (state is null)
        {
            issues.Add(Issue(
                "posting-candidate.instrument-economic-state-required",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Book position '{positionId:D}' has no factor economic state for {requestedEvent.EffectiveDate:yyyy-MM-dd}.",
                "bookPosition.currentEconomicState",
                "Persist the factor economic-state projection before creating the candidate."));
            return null;
        }

        var persistedLineage = detail.ProjectionLineages
            .FirstOrDefault(candidate => candidate.ProjectionRunId == requestedLineage.ProjectionRunId) ??
            position.ProjectionLineage;
        if (persistedLineage is null)
        {
            issues.Add(Issue(
                "posting-candidate.instrument-lineage-required",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Book position '{positionId:D}' has no persisted projection lineage matching the candidate.",
                "projectionLineage.projectionRunId",
                "Persist the typed projection lineage before creating the candidate."));
            return null;
        }

        ValidatePersistedInstrumentAssertions(request, position, role, state, persistedLineage, issues);
        if (issues.Any(static issue => issue.BlocksCandidate))
        {
            return null;
        }

        if (state.PriorFactor is null || state.CurrentFactor is null)
        {
            issues.Add(Issue(
                "posting-candidate.instrument-factor-state-incomplete",
                AccountingConfigurationValidationSeverityDto.Critical,
                "Persisted factor-paydown economic state is missing factor values.",
                "bookPosition.currentEconomicState",
                "Rebuild the position economic state from retained factor evidence."));
            return null;
        }

        // Held face comes from the lots of record, not from the economic-state projection. The old
        // OriginalFaceAmount ?? ParAmount ?? NotionalAmount ?? Quantity chain would fall all the way
        // through to a unit quantity, which is not a face at all, and it presumed every recorded face
        // had been booked at factor 1. Both are the assumptions the persisted par conventions exist
        // to remove: principal paydown scales the held face directly, so a face taken from the wrong
        // source or the wrong factor posts the wrong amount of cash.
        var heldFace = await ResolveLotOfRecordHeldFaceAsync(
                request.LedgerBookId!.Value,
                securityId,
                positionId,
                issues,
                ct)
            .ConfigureAwait(false);
        if (heldFace is null)
        {
            return null;
        }

        var projection = _factorPaydownProjector.Project(new FactorPaydownProjectionRequest(
            securityId,
            positionId,
            position.Version,
            position.Version,
            heldFace.Value,
            state.PriorFactor.Value,
            state.CurrentFactor.Value,
            state.Currency,
            requestedEvent.EffectiveDate,
            persistedLineage.TriggerEvent.OccurredAtUtc,
            persistedLineage.TriggerEvent.SourceDomain,
            persistedLineage.TriggerEvent.SourceEntityId ?? string.Empty,
            persistedLineage.TriggerEvent.SourceContentHash ?? string.Empty,
            persistedLineage.TriggerEvent.EvidenceLinks,
            persistedLineage.TriggerEvent.CorrelationId,
            persistedLineage.TriggerEvent.CausationId,
            persistedLineage.GeneratedAtUtc));
        if (!projection.ProducesPostingCandidate)
        {
            foreach (var projectionIssue in projection.Issues)
            {
                issues.Add(Issue(
                    $"posting-candidate.{projectionIssue.Code}",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    projectionIssue.Message,
                    "projectionLineage",
                    "Rebuild the governed factor-paydown projection from valid persisted inputs."));
            }

            return null;
        }

        AddMismatch(
            issues,
            projection.EconomicEvent!.EventId == requestedEvent.EventId,
            "posting-candidate.instrument-event-id-mismatch",
            "The submitted economic event does not match the server-recalculated factor event.",
            "economicEvent.eventId");
        AddMismatch(
            issues,
            projection.Lineage!.ProjectionRunId == requestedLineage.ProjectionRunId &&
            projection.Lineage.ProjectionEventId == requestedLineage.ProjectionEventId &&
            TextEquals(projection.Lineage.ModelVersion, requestedLineage.ModelVersion) &&
            TextEquals(projection.Lineage.EngineVersion, requestedLineage.EngineVersion),
            "posting-candidate.instrument-lineage-mismatch",
            "The submitted projection lineage does not match the server-recalculated lineage.",
            "projectionLineage");
        AddMismatch(
            issues,
            projection.PrincipalPaydown == request.EventAmount,
            "posting-candidate.factor-paydown-amount-mismatch",
            "The submitted event amount does not match the server-calculated principal paydown.",
            "eventAmount");

        return projection.PrincipalPaydown;
    }

    private static void ValidatePersistedInstrumentAssertions(
        PostingRuleJournalCandidateRequestDto request,
        BookPositionDto position,
        InstrumentRoleDto role,
        PositionEconomicStateDto state,
        ProjectionLineageDto lineage,
        List<PostingRuleJournalCandidateIssueDto> issues)
    {
        var economicEvent = request.EconomicEvent!;
        var requestedLineage = request.ProjectionLineage!;
        AddMismatch(issues, position.SecurityId == economicEvent.SecurityId, "posting-candidate.instrument-position-security-mismatch", "Persisted position Security Master identity must match the economic event.", "bookPosition.securityId");
        AddMismatch(issues, role.SecurityId == position.SecurityId, "posting-candidate.instrument-role-security-mismatch", "Persisted holder role Security Master identity must match the position.", "bookPosition.roleId");
        AddMismatch(issues, request.LedgerBookId == position.BookContext.LedgerBookId, "posting-candidate.instrument-position-book-mismatch", "Persisted position must belong to the candidate ledger book.", "bookPosition.bookContext.ledgerBookId");
        AddMismatch(issues, TextEquals(request.Currency, state.Currency), "posting-candidate.instrument-position-currency-mismatch", "Persisted economic-state currency must match the candidate.", "bookPosition.currentEconomicState.currency");
        AddMismatch(issues, state.PositionId == position.PositionId, "posting-candidate.instrument-economic-state-position-mismatch", "Persisted economic state must belong to the selected position.", "bookPosition.currentEconomicState.positionId");
        AddMismatch(issues, state.AsOfDate == economicEvent.EffectiveDate, "posting-candidate.instrument-economic-state-date-mismatch", "Persisted factor state must be effective on the economic-event date.", "bookPosition.currentEconomicState.asOfDate");
        AddMismatch(issues, state.Version == position.Version + 1, "posting-candidate.instrument-economic-state-version-stale", "Persisted factor state must be the next version derived from the current position.", "bookPosition.currentEconomicState.version");
        AddMismatch(issues, TextEquals(position.Status, "Active"), "posting-candidate.instrument-position-inactive", "Persisted position must be active before a posting candidate can be created.", "bookPosition.status");
        AddMismatch(issues, position.EffectiveFrom <= economicEvent.EffectiveDate && (position.EffectiveTo is null || position.EffectiveTo >= economicEvent.EffectiveDate), "posting-candidate.instrument-position-not-effective", "Persisted position is not effective on the economic-event date.", "bookPosition.effectiveFrom");
        AddMismatch(issues, role.EffectiveFrom <= economicEvent.EffectiveDate && (role.EffectiveTo is null || role.EffectiveTo >= economicEvent.EffectiveDate), "posting-candidate.instrument-role-not-effective", "Persisted holder role is not effective on the economic-event date.", "instrumentRole.effectiveFrom");
        AddMismatch(issues, lineage.ProjectionRunId == requestedLineage.ProjectionRunId && lineage.ProjectionEventId == requestedLineage.ProjectionEventId, "posting-candidate.instrument-persisted-lineage-mismatch", "Candidate projection identity must match persisted Asset Operations lineage.", "projectionLineage.projectionRunId");
        AddMismatch(issues, EventsMatch(lineage.TriggerEvent, economicEvent), "posting-candidate.instrument-trigger-event-mismatch", "Persisted projection trigger must match the candidate economic event.", "projectionLineage.triggerEvent");
        var persistedEvidence = NormalizeEvidence(lineage.TriggerEvent.EvidenceLinks);
        var candidateEvidence = NormalizeEvidence(request.EvidenceLinks);
        AddMismatch(issues, persistedEvidence.Count > 0 && persistedEvidence.All(candidateEvidence.Contains), "posting-candidate.instrument-evidence-mismatch", "Candidate evidence must retain every source link used by the persisted projection.", "evidenceLinks");
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

    private static AccountingPostingCandidateWriteResult BuildPreDryRunBlockedResult(
        PostingRuleJournalCandidateRequestDto request,
        IReadOnlyList<PostingRuleJournalCandidateIssueDto> issues)
    {
        var dryRun = new RuleDryRunResultDto(
            request.FundProfileId,
            request.LedgerBookId,
            request.SourceEventType,
            request.EffectiveDate,
            request.EventAmount,
            request.Currency,
            IsPostingBalanced: false,
            SelectedRuleId: null,
            RuleMatches: [],
            GeneratedLines: [],
            ValidationIssues: [],
            GeneratedPostingLines: []);
        return new AccountingPostingCandidateWriteResult(
            BuildBlockedResult(request, dryRun, selectedRuleVersion: null, issues),
            Write: null);
    }

    private static IReadOnlyList<PostingRuleJournalCandidateIssueDto> ValidateAssetAccountingAuthority(
        PostingRuleJournalCandidateRequestDto request,
        AssetAccountingCandidateAuthorityContext? authority)
    {
        if (!AssetAccountingEventTypeNames.TryParse(request.SourceEventType, out var eventKind))
        {
            return authority is null
                ? []
                :
                [
                    Issue(
                        "posting-candidate.asset-authority-unexpected",
                        AccountingConfigurationValidationSeverityDto.Critical,
                        "Asset Accounting Event Spine authority may only be used for canonical AssetAccounting event types.",
                        "sourceEventType",
                        "Use the generic candidate builder for non-asset source events.")
                ];
        }

        if (authority is null)
        {
            return
            [
                Issue(
                    "posting-candidate.asset-authority-required",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    "Canonical asset-accounting events must be resolved by the Asset Accounting Event Spine before Rules Studio can build a candidate.",
                    "sourceEventType",
                    "Use IAssetAccountingEventSpineService; direct generic candidate requests are not posting authority.")
            ];
        }

        var issues = new List<PostingRuleJournalCandidateIssueDto>();
        AddMismatch(issues, authority.EventKind == eventKind, "posting-candidate.asset-authority-kind-mismatch", "Resolved asset-event kind must match the canonical source-event type.", "sourceEventType");
        AddMismatch(issues, authority.EventId != Guid.Empty && request.SourceEventId == authority.EventId && request.EconomicEvent?.EventId == authority.EventId, "posting-candidate.asset-authority-event-mismatch", "Resolved asset-event identity must match the posting request.", "sourceEventId");
        AddMismatch(issues, authority.EventVersion > 0 && request.EconomicEvent?.EventVersion == authority.EventVersion, "posting-candidate.asset-authority-event-version-mismatch", "Resolved source-event version must match the typed economic event.", "economicEvent.eventVersion");
        AddMismatch(issues, authority.SourceSpineVersion > 0 && authority.DraftSpineVersion == authority.SourceSpineVersion + 1, "posting-candidate.asset-authority-spine-version-mismatch", "Resolved asset-accounting authority must target the next append-only spine version.", "authority.spineVersion");
        AddMismatch(issues, IsSha256(authority.SourceProjectionFingerprint), "posting-candidate.asset-authority-fingerprint-required", "Resolved asset-accounting authority requires the retained source projection fingerprint.", "authority.sourceProjectionFingerprint");
        AddMismatch(issues, authority.SecurityId != Guid.Empty && request.EconomicEvent?.SecurityId == authority.SecurityId && request.Dimensions?.InstrumentId == authority.SecurityId, "posting-candidate.asset-authority-security-mismatch", "Resolved Security Master identity must match the event and candidate dimensions.", "dimensions.instrumentId");
        AddMismatch(issues, authority.BookPositionId != Guid.Empty && request.BookPositionId == authority.BookPositionId && request.Dimensions?.PositionId == authority.BookPositionId, "posting-candidate.asset-authority-position-mismatch", "Resolved book-position identity must match the event and candidate dimensions.", "bookPositionId");
        AddMismatch(issues, authority.ExpectedBookPositionVersion > 0, "posting-candidate.asset-authority-position-version-required", "Resolved asset-accounting authority requires a positive book-position version assertion.", "authority.expectedBookPositionVersion");
        AddMismatch(issues, authority.LedgerBookId != Guid.Empty && request.LedgerBookId == authority.LedgerBookId && request.AggregateId == authority.LedgerBookId && request.BookContext?.LedgerBookId == authority.LedgerBookId, "posting-candidate.asset-authority-book-mismatch", "Resolved ledger-book identity must own the candidate aggregate and typed book context.", "ledgerBookId");
        AddMismatch(issues, authority.PeriodId != Guid.Empty && request.PeriodId == authority.PeriodId && request.BookContext?.PeriodId == authority.PeriodId, "posting-candidate.asset-authority-period-mismatch", "Resolved accounting period must match the candidate and typed book context.", "periodId");
        AddMismatch(issues, authority.ExpectedPeriodVersion > 0, "posting-candidate.asset-authority-period-version-required", "Resolved asset-accounting authority requires a positive period-version assertion.", "authority.expectedPeriodVersion");
        AddMismatch(issues, request.EconomicEvent is not null && IsSha256(request.EconomicEvent.SourceContentHash), "posting-candidate.asset-authority-source-hash-required", "Resolved asset events require a canonical SHA-256 source-content hash.", "economicEvent.sourceContentHash");
        AddMismatch(issues, request.ProjectionLineage is { } lineage && lineage.ProjectionRunId != Guid.Empty && !string.IsNullOrWhiteSpace(lineage.ModelKey) && !string.IsNullOrWhiteSpace(lineage.ModelVersion) && !string.IsNullOrWhiteSpace(lineage.EngineVersion), "posting-candidate.asset-authority-lineage-required", "Resolved asset events require complete projection model lineage.", "projectionLineage");
        AddMismatch(issues, request.RetainedEvidence.Count > 0 && request.RetainedEvidence.All(static evidence => RetainedEvidenceIdentityValidator.IsComplete(evidence)), "posting-candidate.asset-authority-evidence-required", "Resolved asset events require complete typed retained evidence before Rules Studio.", "retainedEvidence");
        AddMismatch(issues, request.RulePackReference is { } rulePack && !string.IsNullOrWhiteSpace(rulePack.SelectedRuleId) && !string.IsNullOrWhiteSpace(rulePack.SelectedRuleVersion) && string.Equals(rulePack.RulePackId, authority.RulePackId, StringComparison.Ordinal) && string.Equals(rulePack.RulePackVersion, authority.RulePackVersion, StringComparison.Ordinal), "posting-candidate.asset-authority-rule-pack-mismatch", "Resolved asset events require the authoritative rule pack and selected Rules Studio rule.", "rulePackReference");
        return issues;
    }

    private static IReadOnlyList<AccountingPostingEvidenceReferenceDto> MergeRetainedEvidence(
        IReadOnlyList<AccountingPostingEvidenceReferenceDto> existing,
        IReadOnlyList<RetainedEvidenceIdentityDto> retainedEvidence)
    {
        if (retainedEvidence.Count == 0)
        {
            return existing;
        }

        return existing
            .Concat(retainedEvidence.Select(static evidence =>
                new AccountingPostingEvidenceReferenceDto(
                    EvidenceId: evidence.EvidenceId,
                    Uri: evidence.EvidenceUri,
                    Kind: AccountingPostingEvidenceKindDto.Source,
                    SourceSystem: evidence.SourceSystem,
                    RetainedAtUtc: evidence.RetainedAtUtc,
                    RetainedBy: evidence.RetainedBy,
                    SubjectId: evidence.SubjectId,
                    ContentHash: evidence.ContentHashSha256,
                    SourceReference: evidence.SourceReference,
                    Reviewer: evidence.ReviewedBy,
                    ReviewedAtUtc: evidence.ReviewedAtUtc,
                    EffectiveDate: evidence.EffectiveDate,
                    EvidenceVersion: evidence.EvidenceVersion,
                    ReviewStatus: evidence.ReviewStatus,
                    SubjectType: evidence.SubjectType)))
            .GroupBy(static evidence => evidence.EvidenceId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.Last())
            .ToArray();
    }

    // Posting-candidate fingerprints arrive from external authority projections, so surrounding
    // whitespace is tolerated here before the shared digest contract is applied.
    private static bool IsSha256(string? value) => Sha256Digest.IsWellFormed(value?.Trim());

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
            if (IsFactorPaydownRequest(request))
            {
                issues.Add(Issue(
                    "posting-candidate.rule-pack-reference-required",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    "Typed MBS factor-paydown candidates require an authoritative rule-pack and selected-rule reference.",
                    "rulePackReference",
                    "Attach the rule pack and rule version selected by Rules Studio before creating the candidate."));
            }

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

    /// <summary>
    /// Resolves held face from the open lots of record for one (security, book position) scope,
    /// restated to a factor of 1 — the basis <see cref="FactorPaydownProjectionService"/> multiplies
    /// by the factor delta. Each lot is restated through the canonical <c>FaceValueLot</c> aggregate
    /// so the face it was booked at, the pool factor it was booked under, and the basis its price was
    /// struck in are all honoured rather than presumed. Fails closed: a scope with no open lots, or
    /// any open lot that never recorded its par conventions, yields a typed issue instead of a
    /// substituted quantity.
    /// </summary>
    private async Task<decimal?> ResolveLotOfRecordHeldFaceAsync(
        Guid ledgerBookId,
        Guid securityId,
        Guid positionId,
        ICollection<PostingRuleJournalCandidateIssueDto> issues,
        CancellationToken ct)
    {
        IReadOnlyList<LedgerTaxLotRecord> lots;
        try
        {
            lots = await _taxLotStore!
                .ListOpenTaxLotsByAssetScopeAsync(ledgerBookId, securityId, positionId, ct)
                .ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            issues.Add(Issue(
                "posting-candidate.instrument-lot-of-record-unavailable",
                AccountingConfigurationValidationSeverityDto.Critical,
                "The configured ledger journal store does not expose the lots of record required to derive held face.",
                "bookPositionId",
                "Configure a ledger journal store that persists tax lots before accepting typed factor-paydown events."));
            return null;
        }

        if (lots.Count == 0)
        {
            issues.Add(Issue(
                "posting-candidate.instrument-lot-of-record-missing",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Book position '{positionId:D}' holds no open lot of record for security '{securityId:D}', so held face cannot be derived.",
                "bookPositionId",
                "Post the acquisition lots for the position before projecting a principal paydown against it."));
            return null;
        }

        var heldFace = 0m;
        foreach (var lot in lots)
        {
            if (lot.ToFaceValueLot() is not { } faceLot)
            {
                issues.Add(Issue(
                    "posting-candidate.instrument-lot-face-terms-missing",
                    AccountingConfigurationValidationSeverityDto.Critical,
                    $"Open lot '{lot.LotId}' did not record its original face, booked factor, and par basis, so its held face cannot be derived.",
                    "bookPositionId",
                    "Backfill the lot's acquisition-time par conventions from retained evidence before projecting a principal paydown against it."));
                return null;
            }

            // CurrentFace(1) restates the lot's face from the factor it was booked at to a factor of
            // 1; the open share carries the part of the lot that has not already been relieved.
            var openShare = lot.OpenQuantity / lot.OriginalQuantity;
            heldFace += faceLot.CurrentFace(1m) * openShare;
        }

        if (heldFace <= 0m)
        {
            issues.Add(Issue(
                "posting-candidate.instrument-lot-face-non-positive",
                AccountingConfigurationValidationSeverityDto.Critical,
                $"Open lots of record for book position '{positionId:D}' resolve to a non-positive held face.",
                "bookPositionId",
                "Correct the open lot quantities and par conventions before projecting a principal paydown."));
            return null;
        }

        return heldFace;
    }

    private static bool IsFactorPaydownRequest(PostingRuleJournalCandidateRequestDto request)
        => TextEquals(request.SourceEventType, FactorPaydownProjectionService.EventType)
           || TextEquals(request.EconomicEvent?.EventType, FactorPaydownProjectionService.EventType)
           || TextEquals(request.ProjectionLineage?.ModelKey, FactorPaydownProjectionService.ModelKey);

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
