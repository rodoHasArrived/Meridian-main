using Meridian.Contracts.Ledger;
using Meridian.Ledger;
using Meridian.Contracts.Integrity;

namespace Meridian.Ui.Shared.Services;

internal sealed class DailyValuationPendingDraftException : InvalidOperationException
{
    public DailyValuationPendingDraftException(
        IReadOnlyList<Guid> pendingJournalEntryIds,
        ManualJournalEntryStatusDto pendingStatus,
        string scope,
        string? pendingBatchCorrelationId)
        : base(
            $"Daily valuation correction for {scope} is blocked while {pendingJournalEntryIds.Count} prior same-day draft(s), including '{pendingJournalEntryIds[0]:D}', remain pending ({pendingStatus}); post or reject the pending batch before creating a corrected mark.")
    {
        PendingJournalEntryIds = pendingJournalEntryIds;
        PendingBatchCorrelationId = string.IsNullOrWhiteSpace(pendingBatchCorrelationId)
            ? null
            : pendingBatchCorrelationId.Trim();
    }

    public IReadOnlyList<Guid> PendingJournalEntryIds { get; }

    public string? PendingBatchCorrelationId { get; }
}

/// <summary>
/// Batch of automated economic events (dividends, interest, fees, withholding) to admit
/// into the manual journal workbench queue for the named fund profile.
/// </summary>
public sealed record AutomatedJournalDraftIntakeRequest(
    string FundProfileId,
    string Currency,
    IReadOnlyList<AutomatedJournalEvent> Events,
    string Actor,
    Guid? LedgerBookId = null,
    string? PeriodId = null,
    string? EntityId = null,
    string? TenantId = null,
    string? CompanyId = null,
    IReadOnlyDictionary<string, AutomatedJournalEvidenceAssessmentDto>? EvidenceAssessments = null,
    string? BatchCorrelationId = null);

/// <summary>
/// Batch of prebuilt automated journal drafts (for example period-close closing entries,
/// whose lines come from a projection rather than a single economic event) to admit into
/// the manual journal workbench queue for the named fund profile.
/// </summary>
public sealed record AutomatedJournalPreparedDraftIntakeRequest(
    string FundProfileId,
    string Currency,
    IReadOnlyList<AutomatedJournalDraft> Drafts,
    string Actor,
    Guid? LedgerBookId = null,
    string? PeriodId = null,
    string? EntityId = null,
    string? TenantId = null,
    string? CompanyId = null,
    IReadOnlyDictionary<string, AutomatedJournalEvidenceAssessmentDto>? EvidenceAssessments = null,
    string? BatchCorrelationId = null);

/// <summary>
/// One event the intake did not turn into a new draft, with the reason it was skipped.
/// </summary>
public enum AutomatedJournalDraftIntakeDisposition
{
    ProjectionFailed = 0,
    ExistingDraftReady = 1,
    ExistingDraftNeedsFix = 2,
    ExistingDraftRejected = 3,
    ExistingDraftGoverned = 4,
    ExistingDraftTerminal = 5,
    ExistingDraftReassessmentRequired = 6
}

public sealed record AutomatedJournalDraftIntakeSkip(
    Guid JournalEntryId,
    string IdempotencyKey,
    string Reason,
    AutomatedJournalDraftIntakeDisposition Disposition = AutomatedJournalDraftIntakeDisposition.ProjectionFailed,
    ManualJournalEntryStatusDto? ExistingStatus = null,
    AutomatedJournalEvidenceAssessmentDto? ExistingEvidenceAssessment = null)
{
    public bool IsReadyDuplicate => Disposition is
        AutomatedJournalDraftIntakeDisposition.ExistingDraftReady or
        AutomatedJournalDraftIntakeDisposition.ExistingDraftGoverned;
}

/// <summary>
/// Outcome of an automated journal intake run. Created drafts land in the workbench queue
/// as <see cref="ManualJournalEntryStatusDto.Draft"/> (or NeedsFix when validation flags
/// them) awaiting human submit/approve; skips are reported, never silent.
/// </summary>
public sealed record AutomatedJournalDraftIntakeResult(
    IReadOnlyList<ManualJournalEntryDraftDto> Created,
    IReadOnlyList<AutomatedJournalDraftIntakeSkip> Skipped)
{
    /// <summary>Created drafts that need account-mapping or other fixes before submission.</summary>
    public int NeedsFixCount => Created.Count(static draft => draft.Status == ManualJournalEntryStatusDto.NeedsFix);
}

/// <summary>
/// Admits automated journal events into the close-cockpit approval queue: projects each
/// event through <see cref="AutomatedJournalDraftProjector"/>, maps ledger accounts onto
/// the fund's chart of accounts, and saves the balanced result through the manual journal
/// workbench so it inherits validation, audit, and the human submit/approve lifecycle.
/// Intake is idempotent per event: the draft id derives from the event idempotency key,
/// and events whose draft already exists are skipped rather than duplicated or overwritten.
/// </summary>
public sealed class AutomatedJournalDraftIntakeService
{
    private readonly IManualJournalEntryWorkbenchService _workbench;
    private readonly IManualJournalEntryDraftStore _draftStore;
    private readonly IAccountingConfigurationService _configurationService;

    public AutomatedJournalDraftIntakeService(
        IManualJournalEntryWorkbenchService workbench,
        IManualJournalEntryDraftStore draftStore,
        IAccountingConfigurationService configurationService)
    {
        _workbench = workbench ?? throw new ArgumentNullException(nameof(workbench));
        _draftStore = draftStore ?? throw new ArgumentNullException(nameof(draftStore));
        _configurationService = configurationService ?? throw new ArgumentNullException(nameof(configurationService));
    }

    public async Task<AutomatedJournalDraftIntakeResult> IntakeAsync(
        AutomatedJournalDraftIntakeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Events.Count == 0)
            throw new ArgumentException("At least one automated journal event is required.", nameof(request));

        var drafts = new List<AutomatedJournalDraft>(request.Events.Count);
        var skipped = new List<AutomatedJournalDraftIntakeSkip>();

        foreach (var journalEvent in request.Events)
        {
            try
            {
                drafts.Add(AutomatedJournalDraftProjector.Project(journalEvent));
            }
            catch (Exception ex) when (ex is ArgumentException or ArgumentOutOfRangeException)
            {
                skipped.Add(new AutomatedJournalDraftIntakeSkip(
                    Guid.Empty,
                    journalEvent.IdempotencyKey ?? BuildFallbackIdempotencyKey(journalEvent),
                    $"Projection failed: {ex.Message}"));
            }
        }

        return await IntakeCoreAsync(
            new AutomatedJournalPreparedDraftIntakeRequest(
                request.FundProfileId,
                request.Currency,
                drafts,
                request.Actor,
                request.LedgerBookId,
                request.PeriodId,
                request.EntityId,
                request.TenantId,
                request.CompanyId,
                request.EvidenceAssessments,
                BatchCorrelationId: null),
            skipped,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Admits prebuilt drafts (for example period-close closing entries) into the workbench
    /// queue with the same idempotent dedup, chart mapping, and human approve lifecycle as
    /// event-projected drafts.
    /// </summary>
    public Task<AutomatedJournalDraftIntakeResult> IntakeDraftsAsync(
        AutomatedJournalPreparedDraftIntakeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Drafts.Count == 0)
            throw new ArgumentException("At least one automated journal draft is required.", nameof(request));

        return IntakeCoreAsync(request, [], ct);
    }

    private async Task<AutomatedJournalDraftIntakeResult> IntakeCoreAsync(
        AutomatedJournalPreparedDraftIntakeRequest request,
        List<AutomatedJournalDraftIntakeSkip> skipped,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FundProfileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Currency);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Actor);

        var workspace = await _configurationService
            .GetWorkspaceAsync(request.FundProfileId, request.LedgerBookId, ct, request.TenantId, request.CompanyId)
            .ConfigureAwait(false);
        var chartLookup = ChartAccountLookup.Build(workspace.ChartOfAccounts);

        var created = new List<ManualJournalEntryDraftDto>();
        var existingDrafts = await _draftStore
            .ListAsync(request.FundProfileId, request.LedgerBookId, ct, request.TenantId, request.CompanyId)
            .ConfigureAwait(false);
        EnsureNoPendingDailyValuationCorrections(request, existingDrafts);

        foreach (var draft in request.Drafts)
        {
            ct.ThrowIfCancellationRequested();

            var idempotencyKey = draft.Event.IdempotencyKey ?? BuildFallbackIdempotencyKey(draft.Event);
            var journalEntryId = BuildDeterministicJournalEntryId(request, idempotencyKey);

            var existing = await _draftStore
                .GetAsync(request.FundProfileId, journalEntryId, ct, request.TenantId, request.CompanyId)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                var incomingAssessment = request.EvidenceAssessments is not null &&
                                         request.EvidenceAssessments.TryGetValue(idempotencyKey, out var candidateAssessment)
                    ? candidateAssessment
                    : null;
                var disposition = ClassifyExistingDraft(existing, incomingAssessment);
                skipped.Add(new AutomatedJournalDraftIntakeSkip(
                    journalEntryId,
                    idempotencyKey,
                    BuildExistingDraftReason(existing, disposition),
                    disposition,
                    existing.Status,
                    existing.AutomationEvidenceAssessment));
                continue;
            }

            var evidenceLinks = draft.Metadata.EvidenceReferences
                .Select(static reference => reference.Uri)
                .Where(static uri => !string.IsNullOrWhiteSpace(uri))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var evidenceAssessment = request.EvidenceAssessments is not null &&
                                     request.EvidenceAssessments.TryGetValue(idempotencyKey, out var retainedAssessment)
                ? retainedAssessment
                : null;
            var dto = BuildDraftDto(
                request,
                draft,
                journalEntryId,
                idempotencyKey,
                evidenceLinks,
                chartLookup,
                evidenceAssessment);
            var saved = await _workbench.SaveDraftAsync(
                new SaveManualJournalEntryDraftRequest(
                    dto,
                    Actor: request.Actor,
                    CorrelationId: string.IsNullOrWhiteSpace(request.BatchCorrelationId)
                        ? idempotencyKey
                        : request.BatchCorrelationId,
                    EvidenceLinks: evidenceLinks,
                    LedgerBookId: request.LedgerBookId,
                    TenantId: request.TenantId,
                    CompanyId: request.CompanyId),
                ct).ConfigureAwait(false);
            created.Add(saved);
        }

        return new AutomatedJournalDraftIntakeResult(created, skipped);
    }

    private static void EnsureNoPendingDailyValuationCorrections(
        AutomatedJournalPreparedDraftIntakeRequest request,
        IReadOnlyList<ManualJournalEntryDraftDto> existingDrafts)
    {
        foreach (var draft in request.Drafts.Where(static item =>
                     item.Event.Kind == AutomatedJournalEventKind.FairValueMarkAdjustment))
        {
            var idempotencyKey = string.IsNullOrWhiteSpace(draft.Metadata.IdempotencyKey)
                ? BuildFallbackIdempotencyKey(draft.Event)
                : draft.Metadata.IdempotencyKey.Trim();
            var journalEntryId = BuildDeterministicJournalEntryId(request, idempotencyKey);
            if (existingDrafts.Any(existing => existing.JournalEntryId == journalEntryId))
            {
                continue;
            }

            var effectiveDate = draft.Metadata.EffectiveDate ??
                                DateOnly.FromDateTime(draft.Event.Timestamp.UtcDateTime);
            var pendingOverlap = existingDrafts.FirstOrDefault(existingDraft =>
                IsPendingDailyValuationDraft(existingDraft) &&
                existingDraft.AccountingDate == effectiveDate &&
                string.Equals(existingDraft.PeriodId, request.PeriodId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(existingDraft.EntityId, request.EntityId, StringComparison.OrdinalIgnoreCase) &&
                HasOverlappingValuationScope(existingDraft, draft));
            if (pendingOverlap is not null)
            {
                var pendingIds = SelectPendingCorrectionBatchIds(
                    existingDrafts,
                    pendingOverlap,
                    draft,
                    effectiveDate,
                    request.PeriodId,
                    request.EntityId);
                throw new DailyValuationPendingDraftException(
                    pendingIds,
                    pendingOverlap.Status,
                    DescribeValuationScope(draft),
                    pendingOverlap.TreasuryContext?.BatchCorrelationId);
            }
        }
    }

    internal static IReadOnlyList<Guid> SelectPendingCorrectionBatchIds(
        IReadOnlyList<ManualJournalEntryDraftDto> existingDrafts,
        ManualJournalEntryDraftDto pendingOverlap,
        AutomatedJournalDraft candidate,
        DateOnly effectiveDate,
        string? periodId,
        string? entityId)
    {
        var pendingBatchCorrelationId = NormalizeText(
            pendingOverlap.TreasuryContext?.BatchCorrelationId);
        return existingDrafts
            .Where(IsPendingDailyValuationDraft)
            .Where(existingDraft => existingDraft.AccountingDate == effectiveDate)
            .Where(existingDraft => string.Equals(
                existingDraft.PeriodId,
                periodId,
                StringComparison.OrdinalIgnoreCase))
            .Where(existingDraft => string.Equals(
                existingDraft.EntityId,
                entityId,
                StringComparison.OrdinalIgnoreCase))
            .Where(existingDraft => pendingBatchCorrelationId is not null
                ? string.Equals(
                    NormalizeText(existingDraft.TreasuryContext?.BatchCorrelationId),
                    pendingBatchCorrelationId,
                    StringComparison.OrdinalIgnoreCase)
                : HasOverlappingValuationScope(existingDraft, candidate))
            .Select(static existingDraft => existingDraft.JournalEntryId)
            .Where(static id => id != Guid.Empty)
            .Distinct()
            .OrderBy(static id => id)
            .ToArray();
    }

    private static bool IsPendingDailyValuationDraft(ManualJournalEntryDraftDto draft)
        => draft.TreasuryContext?.IdempotencyKey?.StartsWith("fair-value|", StringComparison.OrdinalIgnoreCase) == true &&
           draft.Status is ManualJournalEntryStatusDto.Draft or
               ManualJournalEntryStatusDto.NeedsFix or
               ManualJournalEntryStatusDto.Submitted or
               ManualJournalEntryStatusDto.Approved;

    private static bool HasOverlappingValuationScope(
        ManualJournalEntryDraftDto existing,
        AutomatedJournalDraft candidate)
    {
        var existingScopes = existing.Lines
            .Select(static line => new ValuationScopeKey(
                line.SecurityId ?? line.Dimensions?.InstrumentId,
                NormalizeOptionalUpperInvariant(line.LedgerAccountSymbol ?? line.SecurityDisplayName),
                NormalizeOptionalUpperInvariant(line.LedgerAccountFinancialAccountId)))
            .Where(static key => key.SecurityId.HasValue || key.Symbol is not null)
            .ToHashSet();
        var candidateScopes = candidate.Lines
            .Select(line => new ValuationScopeKey(
                candidate.Event.SecurityId ?? line.dimensions?.InstrumentId,
                NormalizeOptionalUpperInvariant(line.account.Symbol ?? candidate.Event.Symbol),
                NormalizeOptionalUpperInvariant(line.account.FinancialAccountId)))
            .Where(static key => key.SecurityId.HasValue || key.Symbol is not null)
            .ToHashSet();

        if (existingScopes.Count == 0 || candidateScopes.Count == 0)
            return true;

        return existingScopes.Overlaps(candidateScopes);
    }

    private static string DescribeValuationScope(AutomatedJournalDraft draft)
    {
        var line = draft.Lines.FirstOrDefault();
        var symbol = NormalizeOptionalUpperInvariant(line.account?.Symbol) ?? NormalizeOptionalUpperInvariant(draft.Event.Symbol) ?? "unknown security";
        var account = NormalizeOptionalUpperInvariant(line.account?.FinancialAccountId) ?? "unscoped account";
        return $"{symbol}/{account}";
    }

    /// <summary>Trims to null and upper-cases.</summary>
    /// <remarks>
    /// Named for the case folding. It previously shared the name and signature of the plain
    /// trim-to-null helper used in dozens of other files, so the difference was invisible at
    /// the call site even though this one changes the value it returns.
    /// </remarks>
    private static string? NormalizeOptionalUpperInvariant(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string? NormalizeText(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ValuationScopeKey(Guid? SecurityId, string? Symbol, string? FinancialAccountId);

    private static ManualJournalEntryDraftDto BuildDraftDto(
        AutomatedJournalPreparedDraftIntakeRequest request,
        AutomatedJournalDraft draft,
        Guid journalEntryId,
        string idempotencyKey,
        IReadOnlyList<string> evidenceLinks,
        ChartAccountLookup chartLookup,
        AutomatedJournalEvidenceAssessmentDto? evidenceAssessment)
    {
        var effectiveDate = draft.Metadata.EffectiveDate
            ?? DateOnly.FromDateTime(draft.Event.Timestamp.UtcDateTime);
        var firstEvidenceLink = evidenceLinks.Count > 0 ? evidenceLinks[0] : null;

        var lines = draft.Lines
            .Select((line, index) =>
            {
                var isDebit = line.debit > 0m;
                return new ManualJournalEntryLineDto(
                    LineId: FormattableString.Invariant($"auto-{index + 1}"),
                    Side: isDebit ? AccountingTemplateLineSideDto.Debit : AccountingTemplateLineSideDto.Credit,
                    Amount: isDebit ? line.debit : line.credit,
                    Currency: request.Currency,
                    AccountPath: chartLookup.ResolvePath(line.account),
                    SecurityId: line.account.Symbol is not null ? draft.Event.SecurityId : null,
                    SecurityDisplayName: line.account.Symbol,
                    Description: line.account.ToString(),
                    EvidenceLink: firstEvidenceLink,
                    Dimensions: LedgerDimensionMapper.ToDto(line.dimensions),
                    LedgerAccountSymbol: line.account.Symbol,
                    LedgerAccountFinancialAccountId: line.account.FinancialAccountId);
            })
            .ToArray();

        return new ManualJournalEntryDraftDto(
            JournalEntryId: journalEntryId,
            Status: ManualJournalEntryStatusDto.Draft,
            FundProfileId: request.FundProfileId,
            LedgerBookId: request.LedgerBookId,
            AccountingBasis: AccountingBasisKindDto.Primary,
            AccountingDate: effectiveDate,
            PeriodId: request.PeriodId,
            EntityId: request.EntityId,
            FundNodeId: null,
            Currency: request.Currency,
            Memo: draft.Description,
            PreparedBy: request.Actor,
            CreatedAtUtc: default,
            UpdatedAtUtc: default,
            Version: 0,
            Lines: lines,
            EvidenceLinks: evidenceLinks,
            ValidationIssues: [],
            EntryType: MapEntryType(draft.Event.Kind),
            TreasuryContext: new TreasuryLedgerContextDto(
                EffectiveDate: effectiveDate,
                IdempotencyKey: idempotencyKey,
                BatchCorrelationId: NormalizeText(request.BatchCorrelationId)),
            AutomationEvidenceAssessment: evidenceAssessment);
    }

    private static ManualJournalEntryTypeDto MapEntryType(AutomatedJournalEventKind kind)
        => kind switch
        {
            AutomatedJournalEventKind.DividendDeclared => ManualJournalEntryTypeDto.AccruedBalance,
            AutomatedJournalEventKind.ManagementFeeAccrued or
            AutomatedJournalEventKind.PerformanceFeeAccrued or
            AutomatedJournalEventKind.CommissionAccrued or
            AutomatedJournalEventKind.WithholdingTaxAccrued => ManualJournalEntryTypeDto.AccruedExpense,
            AutomatedJournalEventKind.CorporateActionExpense => ManualJournalEntryTypeDto.Expense,
            // Closing entries carry a dedicated type so the workbench posts them as the sanctioned
            // ClosingEntry kind into the (closed) period being finalized.
            AutomatedJournalEventKind.PeriodCloseClosingEntries => ManualJournalEntryTypeDto.ClosingEntry,
            _ => ManualJournalEntryTypeDto.General
        };

    private static string BuildFallbackIdempotencyKey(AutomatedJournalEvent journalEvent)
    {
        var effectiveDate = journalEvent.EffectiveDate ?? DateOnly.FromDateTime(journalEvent.Timestamp.UtcDateTime);
        return FormattableString.Invariant(
            $"{journalEvent.Kind}|{journalEvent.Symbol.Trim().ToUpperInvariant()}|{journalEvent.Amount}|{effectiveDate:yyyy-MM-dd}|{journalEvent.FinancialAccountId ?? "-"}");
    }

    private static Guid BuildDeterministicJournalEntryId(
        AutomatedJournalPreparedDraftIntakeRequest request,
        string idempotencyKey)
    {
        var seed = FormattableString.Invariant(
            $"automated-journal|tenant={NormalizeIdentity(request.TenantId)}|company={NormalizeIdentity(request.CompanyId)}|fund={NormalizeIdentity(request.FundProfileId)}|book={request.LedgerBookId?.ToString("N") ?? "-"}|entity={NormalizeIdentity(request.EntityId)}|currency={NormalizeIdentity(request.Currency)}|event={idempotencyKey.Trim().ToLowerInvariant()}");
        var hash = Sha256Digest.ComputeBytesUtf8(seed);
        return new Guid(hash.AsSpan(0, 16));
    }

    private static AutomatedJournalDraftIntakeDisposition ClassifyExistingDraft(
        ManualJournalEntryDraftDto existing,
        AutomatedJournalEvidenceAssessmentDto? incomingAssessment)
    {
        if (existing.Status == ManualJournalEntryStatusDto.Rejected)
            return AutomatedJournalDraftIntakeDisposition.ExistingDraftRejected;
        if (existing.Status is ManualJournalEntryStatusDto.Reversed or ManualJournalEntryStatusDto.Rebooked)
            return AutomatedJournalDraftIntakeDisposition.ExistingDraftTerminal;
        if (existing.Status is ManualJournalEntryStatusDto.Submitted or
            ManualJournalEntryStatusDto.Approved or
            ManualJournalEntryStatusDto.Posted or
            ManualJournalEntryStatusDto.CloseLocked)
        {
            return AutomatedJournalDraftIntakeDisposition.ExistingDraftGoverned;
        }

        if (incomingAssessment is not null &&
            !EvidenceAssessmentsEquivalent(existing.AutomationEvidenceAssessment, incomingAssessment))
        {
            return AutomatedJournalDraftIntakeDisposition.ExistingDraftReassessmentRequired;
        }

        if (existing.Status == ManualJournalEntryStatusDto.NeedsFix ||
            existing.AutomationEvidenceAssessment?.RequiresInvestigation == true ||
            existing.ValidationIssues.Any(static issue =>
                issue.Severity == AccountingConfigurationValidationSeverityDto.Critical))
        {
            return AutomatedJournalDraftIntakeDisposition.ExistingDraftNeedsFix;
        }

        return AutomatedJournalDraftIntakeDisposition.ExistingDraftReady;
    }

    private static string BuildExistingDraftReason(
        ManualJournalEntryDraftDto existing,
        AutomatedJournalDraftIntakeDisposition disposition)
        => disposition switch
        {
            AutomatedJournalDraftIntakeDisposition.ExistingDraftReady =>
                $"Draft already exists with status {existing.Status} and remains ready for human review.",
            AutomatedJournalDraftIntakeDisposition.ExistingDraftGoverned =>
                $"Draft already exists with governed status {existing.Status}; no duplicate was created.",
            AutomatedJournalDraftIntakeDisposition.ExistingDraftNeedsFix =>
                $"Draft already exists with status {existing.Status} and still requires fixes or evidence investigation.",
            AutomatedJournalDraftIntakeDisposition.ExistingDraftRejected =>
                "Draft already exists with status Rejected and cannot be reported as ready by a scheduler retry.",
            AutomatedJournalDraftIntakeDisposition.ExistingDraftTerminal =>
                $"Draft already exists with terminal correction status {existing.Status} and cannot be reported as ready.",
            AutomatedJournalDraftIntakeDisposition.ExistingDraftReassessmentRequired =>
                "Draft already exists with a different immutable automated-evidence assessment; retain the original assessment and route an explicit reassessment before readiness can change.",
            _ => $"Draft already exists with status {existing.Status}."
        };

    private static bool EvidenceAssessmentsEquivalent(
        AutomatedJournalEvidenceAssessmentDto? existing,
        AutomatedJournalEvidenceAssessmentDto incoming)
        => existing is not null &&
           string.Equals(existing.AssessmentCode, incoming.AssessmentCode, StringComparison.Ordinal) &&
           existing.ConfidenceScore == incoming.ConfidenceScore &&
           existing.Quality == incoming.Quality &&
           existing.RequiresInvestigation == incoming.RequiresInvestigation &&
           string.Equals(existing.Summary, incoming.Summary, StringComparison.Ordinal) &&
           existing.Reasons.SequenceEqual(incoming.Reasons, StringComparer.Ordinal) &&
           existing.EvidenceLinks.SequenceEqual(incoming.EvidenceLinks, StringComparer.Ordinal);

    private static string NormalizeIdentity(string? value)
        => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim().ToLowerInvariant();

    /// <summary>
    /// Resolves ledger accounts to chart-of-accounts paths: an exact match on
    /// name + symbol + financial account wins, then a name-only match, then the raw
    /// ledger account name — which fails chart validation loudly (NeedsFix) instead of
    /// posting to a wrong account.
    /// </summary>
    private sealed class ChartAccountLookup
    {
        private readonly Dictionary<string, string> _pathByIdentity;
        private readonly Dictionary<string, string> _pathByName;

        private ChartAccountLookup(
            Dictionary<string, string> pathByIdentity,
            Dictionary<string, string> pathByName)
        {
            _pathByIdentity = pathByIdentity;
            _pathByName = pathByName;
        }

        public static ChartAccountLookup Build(IReadOnlyList<ChartOfAccountsNodeDto> chart)
        {
            var pathByIdentity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pathByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var node in chart)
            {
                if (node.IsArchived)
                    continue;

                pathByIdentity.TryAdd(IdentityKey(node.AccountName, node.Symbol, node.FinancialAccountId), node.Path);
                pathByName.TryAdd(node.AccountName, node.Path);
            }

            return new ChartAccountLookup(pathByIdentity, pathByName);
        }

        public string ResolvePath(LedgerAccount account)
        {
            if (_pathByIdentity.TryGetValue(IdentityKey(account.Name, account.Symbol, account.FinancialAccountId), out var identityPath))
                return identityPath;

            if (_pathByName.TryGetValue(account.Name, out var namePath))
                return namePath;

            return account.Name;
        }

        private static string IdentityKey(string name, string? symbol, string? financialAccountId)
            => FormattableString.Invariant(
                $"{name.Trim()}|{symbol?.Trim().ToUpperInvariant() ?? "-"}|{financialAccountId?.Trim() ?? "-"}");
    }
}
