using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.Ui.Shared.Services;

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
    string? CompanyId = null);

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
    string? CompanyId = null);

/// <summary>
/// One event the intake did not turn into a new draft, with the reason it was skipped.
/// </summary>
public sealed record AutomatedJournalDraftIntakeSkip(
    Guid JournalEntryId,
    string IdempotencyKey,
    string Reason);

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
                request.CompanyId),
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

        foreach (var draft in request.Drafts)
        {
            ct.ThrowIfCancellationRequested();

            var idempotencyKey = draft.Event.IdempotencyKey ?? BuildFallbackIdempotencyKey(draft.Event);
            var journalEntryId = BuildDeterministicJournalEntryId(request.FundProfileId, idempotencyKey);

            var existing = await _draftStore
                .GetAsync(request.FundProfileId, journalEntryId, ct, request.TenantId, request.CompanyId)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                skipped.Add(new AutomatedJournalDraftIntakeSkip(
                    journalEntryId,
                    idempotencyKey,
                    $"Draft already exists with status {existing.Status}."));
                continue;
            }

            var evidenceLinks = draft.Metadata.EvidenceReferences
                .Select(static reference => reference.Uri)
                .Where(static uri => !string.IsNullOrWhiteSpace(uri))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var dto = BuildDraftDto(request, draft, journalEntryId, idempotencyKey, evidenceLinks, chartLookup);
            var saved = await _workbench.SaveDraftAsync(
                new SaveManualJournalEntryDraftRequest(
                    dto,
                    Actor: request.Actor,
                    CorrelationId: idempotencyKey,
                    EvidenceLinks: evidenceLinks,
                    LedgerBookId: request.LedgerBookId,
                    TenantId: request.TenantId,
                    CompanyId: request.CompanyId),
                ct).ConfigureAwait(false);
            created.Add(saved);
        }

        return new AutomatedJournalDraftIntakeResult(created, skipped);
    }

    private static ManualJournalEntryDraftDto BuildDraftDto(
        AutomatedJournalPreparedDraftIntakeRequest request,
        AutomatedJournalDraft draft,
        Guid journalEntryId,
        string idempotencyKey,
        IReadOnlyList<string> evidenceLinks,
        ChartAccountLookup chartLookup)
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
                    Dimensions: LedgerDimensionMapper.ToDto(line.dimensions));
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
                IdempotencyKey: idempotencyKey));
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
            _ => ManualJournalEntryTypeDto.General
        };

    private static string BuildFallbackIdempotencyKey(AutomatedJournalEvent journalEvent)
    {
        var effectiveDate = journalEvent.EffectiveDate ?? DateOnly.FromDateTime(journalEvent.Timestamp.UtcDateTime);
        return FormattableString.Invariant(
            $"{journalEvent.Kind}|{journalEvent.Symbol.Trim().ToUpperInvariant()}|{journalEvent.Amount}|{effectiveDate:yyyy-MM-dd}|{journalEvent.FinancialAccountId ?? "-"}");
    }

    private static Guid BuildDeterministicJournalEntryId(string fundProfileId, string idempotencyKey)
    {
        var seed = FormattableString.Invariant(
            $"automated-journal|{fundProfileId.Trim().ToLowerInvariant()}|{idempotencyKey.Trim().ToLowerInvariant()}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(hash.AsSpan(0, 16));
    }

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
