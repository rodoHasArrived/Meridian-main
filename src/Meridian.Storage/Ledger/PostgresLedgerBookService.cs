using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;

namespace Meridian.Storage.Ledger;

public sealed class PostgresLedgerBookService : ILedgerBookService
{
    private const string OpenStatus = "Open";
    private const string SoftClosedStatus = "SoftClosed";
    private const string HardClosedStatus = "HardClosed";

    private readonly ILedgerJournalStore _store;
    private readonly IOperatorInboxService? _operatorInbox;

    public PostgresLedgerBookService(
        ILedgerJournalStore store,
        IOperatorInboxService? operatorInbox = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _operatorInbox = operatorInbox;
    }

    public async Task<LedgerBookDto> CreateBookAsync(CreateLedgerBookRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var fundProfileId = RequireText(request.FundProfileId, nameof(request.FundProfileId));
        if (request.FundStructureNodeId == Guid.Empty)
        {
            throw new LedgerBookValidationException("Fund-structure node id is required.");
        }

        var displayName = RequireText(request.DisplayName, nameof(request.DisplayName));
        var baseCurrency = RequireText(request.BaseCurrency, nameof(request.BaseCurrency)).ToUpperInvariant();
        var accountingBasis = request.AccountingBasis;
        var accountingPolicyId = RequireText(request.AccountingPolicyId, nameof(request.AccountingPolicyId));
        var accountingPolicyVersion = RequireText(request.AccountingPolicyVersion, nameof(request.AccountingPolicyVersion));
        var existing = await _store
            .ListLedgerBooksAsync(fundProfileId, request.FundStructureNodeId, request.FundStructureNodeKind, ct)
            .ConfigureAwait(false);
        var existingForBasis = existing.FirstOrDefault(book => book.AccountingBasis == accountingBasis);
        if (existingForBasis is not null)
        {
            return MapBook(existingForBasis);
        }

        var now = DateTimeOffset.UtcNow;
        var record = new LedgerBookRecord(
            LedgerBookId: Guid.NewGuid(),
            FundProfileId: fundProfileId,
            FundStructureNodeId: request.FundStructureNodeId,
            FundStructureNodeKind: request.FundStructureNodeKind,
            DisplayName: displayName,
            BaseCurrency: baseCurrency,
            CreatedAt: now,
            UpdatedAt: now,
            Description: NormalizeOptional(request.Description),
            AccountingBasis: accountingBasis,
            AccountingPolicyId: accountingPolicyId,
            AccountingPolicyVersion: accountingPolicyVersion);

        return MapBook(await _store.SaveLedgerBookAsync(record, ct).ConfigureAwait(false));
    }

    public async Task<LedgerBookDto?> GetBookAsync(Guid ledgerBookId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (ledgerBookId == Guid.Empty)
        {
            throw new LedgerBookValidationException("Ledger book id is required.");
        }

        var book = await _store.GetLedgerBookAsync(ledgerBookId, ct).ConfigureAwait(false);
        return book is null ? null : MapBook(book);
    }

    public async Task<IReadOnlyList<LedgerBookDto>> ListBooksAsync(LedgerBookQuery query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(query);

        var records = await _store
            .ListLedgerBooksAsync(
                NormalizeOptional(query.FundProfileId),
                query.FundStructureNodeId,
                query.FundStructureNodeKind,
                ct)
            .ConfigureAwait(false);
        if (query.AccountingBasis.HasValue)
        {
            records = records.Where(book => book.AccountingBasis == query.AccountingBasis.Value).ToArray();
        }

        return records.Select(MapBook).ToArray();
    }

    public async Task<LedgerBookRolloutAssessmentDto> AssessRolloutAsync(
        LedgerBookRolloutAssessmentRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var fundProfileId = NormalizeOptional(request.FundProfileId);
        var records = await _store
            .ListLedgerBooksAsync(
                fundProfileId,
                request.FundStructureNodeId,
                request.FundStructureNodeKind,
                ct)
            .ConfigureAwait(false);
        if (request.AccountingBasis.HasValue)
        {
            records = records.Where(book => book.AccountingBasis == request.AccountingBasis.Value).ToArray();
        }

        var issues = new List<LedgerBookRolloutIssueDto>();
        var statuses = new List<LedgerBookRolloutBookStatusDto>();
        foreach (var book in records.OrderBy(static book => book.FundProfileId, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static book => book.FundStructureNodeId)
                     .ThenBy(static book => book.AccountingBasis)
                     .ThenBy(static book => book.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var periods = await _store
                .ListPeriodsAsync(book.LedgerBookId, status: null, fundProfileId: null, fundStructureNodeId: null, ct)
                .ConfigureAwait(false);
            var orderedPeriods = periods.OrderBy(static period => period.StartDate).ToArray();
            var status = new LedgerBookRolloutBookStatusDto(
                book.LedgerBookId,
                book.FundProfileId,
                book.FundStructureNodeId,
                book.FundStructureNodeKind,
                book.AccountingBasis,
                book.AccountingPolicyId,
                book.AccountingPolicyVersion,
                orderedPeriods.Length,
                orderedPeriods.Count(static period => string.Equals(period.Status, OpenStatus, StringComparison.Ordinal)),
                orderedPeriods.Count(static period => string.Equals(period.Status, SoftClosedStatus, StringComparison.Ordinal)),
                orderedPeriods.Count(static period => string.Equals(period.Status, HardClosedStatus, StringComparison.Ordinal)),
                orderedPeriods.FirstOrDefault()?.StartDate,
                orderedPeriods.LastOrDefault()?.EndDate);
            statuses.Add(status);

            if (status.PeriodCount == 0)
            {
                issues.Add(new LedgerBookRolloutIssueDto(
                    "LedgerBookMissingPeriods",
                    LedgerBookRolloutIssueSeverityDto.Critical,
                    $"Ledger book '{book.DisplayName}' has no accounting periods; migration cannot prove close/reporting readiness.",
                    Scope: BuildRolloutScope(book),
                    LedgerBookId: book.LedgerBookId,
                    FundStructureNodeId: book.FundStructureNodeId,
                    AccountingBasis: book.AccountingBasis));
            }

            if (string.Equals(book.AccountingPolicyId, "legacy-v1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(book.AccountingPolicyVersion, "legacy-v1", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new LedgerBookRolloutIssueDto(
                    "LedgerBookLegacyAccountingPolicy",
                    LedgerBookRolloutIssueSeverityDto.Warning,
                    $"Ledger book '{book.DisplayName}' still uses legacy accounting policy metadata; promote a governed policy before production cutover.",
                    Scope: BuildRolloutScope(book),
                    LedgerBookId: book.LedgerBookId,
                    FundStructureNodeId: book.FundStructureNodeId,
                    AccountingBasis: book.AccountingBasis));
            }

            if (status.PeriodCount > 0 && status.HardClosedPeriodCount == 0)
            {
                issues.Add(new LedgerBookRolloutIssueDto(
                    "LedgerBookNoHardClosedPeriods",
                    LedgerBookRolloutIssueSeverityDto.Warning,
                    $"Ledger book '{book.DisplayName}' has no hard-closed periods; production reporting certification has not been proven.",
                    Scope: BuildRolloutScope(book),
                    LedgerBookId: book.LedgerBookId,
                    FundStructureNodeId: book.FundStructureNodeId,
                    AccountingBasis: book.AccountingBasis));
            }
        }

        AddMissingRequiredScopeIssues(request, records, issues);
        AddUnscopedPeriodIssues(records, issues, await _store
            .ListPeriodsAsync(ledgerBookId: null, status: null, fundProfileId: null, fundStructureNodeId: null, ct)
            .ConfigureAwait(false));

        if (records.Count == 0)
        {
            issues.Add(new LedgerBookRolloutIssueDto(
                "LedgerBookScopeMissing",
                LedgerBookRolloutIssueSeverityDto.Critical,
                "No ledger books match the requested rollout scope.",
                Scope: BuildRolloutScope(fundProfileId, request.FundStructureNodeId, request.FundStructureNodeKind, request.AccountingBasis),
                FundStructureNodeId: request.FundStructureNodeId,
                AccountingBasis: request.AccountingBasis));
        }

        return new LedgerBookRolloutAssessmentDto(
            DateTimeOffset.UtcNow,
            fundProfileId,
            request.FundStructureNodeId,
            request.FundStructureNodeKind,
            request.AccountingBasis,
            statuses,
            issues.OrderByDescending(static issue => issue.Severity)
                .ThenBy(static issue => issue.Code, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static issue => issue.Scope, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public async Task<LedgerPeriodDto> CreatePeriodAsync(CreateLedgerPeriodRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        if (request.LedgerBookId == Guid.Empty)
        {
            throw new LedgerBookValidationException("Ledger book id is required.");
        }

        if (request.FiscalYear <= 0)
        {
            throw new LedgerBookValidationException("Fiscal year must be positive.");
        }

        if (request.PeriodNo <= 0)
        {
            throw new LedgerBookValidationException("Period number must be positive.");
        }

        if (request.StartDate > request.EndDate)
        {
            throw new LedgerBookValidationException("Period start date must be before or equal to the end date.");
        }

        var book = await RequireBookAsync(request.LedgerBookId, ct).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var period = new LedgerAccountingPeriod(
            PeriodId: Guid.NewGuid(),
            LedgerBookId: request.LedgerBookId,
            FiscalYear: request.FiscalYear,
            PeriodNo: request.PeriodNo,
            Label: RequireText(request.Label, nameof(request.Label)),
            StartDate: request.StartDate,
            EndDate: request.EndDate,
            Status: OpenStatus,
            OpenedAt: now,
            ClosedAt: null,
            Version: 0);

        var saved = await _store.SavePeriodAsync(period, expectedVersion: 0, closeEvent: null, ct).ConfigureAwait(false);
        return MapPeriod(saved, book);
    }

    public async Task<IReadOnlyList<LedgerPeriodDto>> ListPeriodsAsync(LedgerPeriodQuery query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(query);

        var status = query.OpenOnly
            ? OpenStatus
            : query.Status?.ToString();
        var periods = await _store
            .ListPeriodsAsync(
                query.LedgerBookId,
                status,
                NormalizeOptional(query.FundProfileId),
                query.FundStructureNodeId,
                ct)
            .ConfigureAwait(false);
        if (query.AccountingBasis.HasValue)
        {
            var books = await _store
                .ListLedgerBooksAsync(query.FundProfileId, query.FundStructureNodeId, fundStructureNodeKind: null, ct)
                .ConfigureAwait(false);
            var matchingBookIds = books
                .Where(book => book.AccountingBasis == query.AccountingBasis.Value)
                .Select(static book => book.LedgerBookId)
                .ToHashSet();
            periods = periods
                .Where(period => period.LedgerBookId is { } id && matchingBookIds.Contains(id))
                .ToArray();
        }

        var bookById = await LoadBooksByIdAsync(periods, ct).ConfigureAwait(false);
        return periods.Select(period => MapPeriod(period, bookById.GetValueOrDefault(RequireLedgerBookId(period)))).ToArray();
    }

    public async Task<IReadOnlyList<LedgerPeriodDto>> ListOpenPeriodsAsync(Guid? ledgerBookId = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var periods = await _store
            .ListPeriodsAsync(ledgerBookId, OpenStatus, fundProfileId: null, fundStructureNodeId: null, ct)
            .ConfigureAwait(false);
        var bookById = await LoadBooksByIdAsync(periods, ct).ConfigureAwait(false);
        return periods.Select(period => MapPeriod(period, bookById.GetValueOrDefault(RequireLedgerBookId(period)))).ToArray();
    }

    public async Task<LedgerPeriodSummaryDto?> GetPeriodSummaryAsync(Guid periodId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var period = await _store.GetPeriodAsync(periodId, ct).ConfigureAwait(false);
        if (period is null || string.Equals(period.Status, OpenStatus, StringComparison.Ordinal))
        {
            return null;
        }

        var book = await RequireBookAsync(RequireLedgerBookId(period), ct).ConfigureAwait(false);
        var financials = await BuildFinancialsAsync(period, ct).ConfigureAwait(false);
        var variance = await CalculatePeriodVarianceAsync(period, financials.NetIncome, ct).ConfigureAwait(false);
        var openBreakCount = await CountOpenBreaksAsync(period, book, ct).ConfigureAwait(false);

        return BuildSummary(
            period,
            book,
            financials,
            variance,
            openBreakCount,
            LedgerPeriodSignoffStatusDto.Pending,
            period.ClosedAt ?? DateTimeOffset.UtcNow);
    }

    public async Task<LedgerPeriodCloseResultDto> ClosePeriodAsync(
        Guid periodId,
        CloseLedgerPeriodRequest request,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        EnsureHumanOrigin(request.ActionOrigin, "close ledger periods");

        var current = await _store.GetPeriodAsync(periodId, ct).ConfigureAwait(false)
            ?? throw new LedgerBookNotFoundException($"Ledger period '{periodId}' was not found.");
        var book = await RequireBookAsync(RequireLedgerBookId(current), ct).ConfigureAwait(false);
        var targetStatus = request.CloseKind switch
        {
            LedgerPeriodCloseKindDto.SoftClose => SoftClosedStatus,
            LedgerPeriodCloseKindDto.HardClose => HardClosedStatus,
            _ => throw new LedgerBookValidationException($"Unsupported period close kind '{request.CloseKind}'.")
        };

        ValidateTransition(current, targetStatus);

        var now = DateTimeOffset.UtcNow;
        var closeEvent = new PeriodCloseEventRecord(
            EventId: Guid.NewGuid(),
            PeriodId: current.PeriodId,
            PriorStatus: current.Status,
            NewStatus: targetStatus,
            ClosedBy: RequireText(request.ClosedBy, nameof(request.ClosedBy)),
            Notes: NormalizeOptional(request.Notes) ?? string.Empty,
            RecordedAt: now);

        var updated = current with
        {
            Status = targetStatus,
            ClosedAt = string.Equals(targetStatus, HardClosedStatus, StringComparison.Ordinal)
                ? now
                : current.ClosedAt
        };
        var saved = await _store
            .SavePeriodAsync(updated, current.Version, closeEvent, ct)
            .ConfigureAwait(false);

        var requiredRole = NormalizeOptional(request.RequiredSignoffRole) ?? "Fund Controller";
        var toleranceProfile = NormalizeOptional(request.ToleranceProfileId) ?? "standard-recon-tolerance";
        var financials = await BuildFinancialsAsync(saved, ct).ConfigureAwait(false);
        var variance = await CalculatePeriodVarianceAsync(saved, financials.NetIncome, ct).ConfigureAwait(false);
        var openBreakCount = await CountOpenBreaksAsync(saved, book, ct).ConfigureAwait(false);
        var summary = BuildSummary(
            saved,
            book,
            financials,
            variance,
            openBreakCount,
            LedgerPeriodSignoffStatusDto.Pending,
            now);
        var workItem = BuildPeriodCloseWorkItem(saved, book, targetStatus, requiredRole, toleranceProfile, now);

        if (_operatorInbox is not null)
        {
            await _operatorInbox.UpsertItemAsync(workItem, ct).ConfigureAwait(false);
        }

        return new LedgerPeriodCloseResultDto(MapPeriod(saved, book), summary, workItem);
    }

    private static void EnsureHumanOrigin(OperationsActionOriginDto actionOrigin, string action)
    {
        if (actionOrigin != OperationsActionOriginDto.HumanOperator)
        {
            throw new LedgerBookValidationException(
                $"Reviewed automation cannot {action}; a human operator approval is required.");
        }
    }

    private async Task<LedgerBookRecord> RequireBookAsync(Guid ledgerBookId, CancellationToken ct)
        => await _store.GetLedgerBookAsync(ledgerBookId, ct).ConfigureAwait(false)
           ?? throw new LedgerBookNotFoundException($"Ledger book '{ledgerBookId}' was not found.");

    private static void ValidateTransition(LedgerAccountingPeriod period, string targetStatus)
    {
        var isValid = (period.Status, targetStatus) switch
        {
            (OpenStatus, SoftClosedStatus) => true,
            (OpenStatus, HardClosedStatus) => true,
            (SoftClosedStatus, HardClosedStatus) => true,
            _ => false
        };

        if (!isValid)
        {
            throw new LedgerPeriodTransitionException(
                $"Cannot transition period '{period.Label}' from {period.Status} to {targetStatus}.");
        }
    }

    private async Task<LedgerPeriodFinancials> BuildFinancialsAsync(LedgerAccountingPeriod period, CancellationToken ct)
    {
        var entries = await _store.GetByPeriodAsync(period.PeriodId, ct).ConfigureAwait(false);
        return CalculateFinancials(entries);
    }

    private async Task<decimal?> CalculatePeriodVarianceAsync(
        LedgerAccountingPeriod period,
        decimal netIncome,
        CancellationToken ct)
    {
        var ledgerBookId = RequireLedgerBookId(period);
        var periods = await _store
            .ListPeriodsAsync(ledgerBookId, status: null, fundProfileId: null, fundStructureNodeId: null, ct)
            .ConfigureAwait(false);
        var prior = periods
            .Where(p => p.PeriodId != period.PeriodId
                        && p.EndDate < period.StartDate
                        && !string.Equals(p.Status, OpenStatus, StringComparison.Ordinal))
            .OrderByDescending(static p => p.EndDate)
            .FirstOrDefault();
        if (prior is null)
        {
            return null;
        }

        var priorFinancials = await BuildFinancialsAsync(prior, ct).ConfigureAwait(false);
        return netIncome - priorFinancials.NetIncome;
    }

    private async Task<int> CountOpenBreaksAsync(
        LedgerAccountingPeriod period,
        LedgerBookRecord book,
        CancellationToken ct)
    {
        if (_operatorInbox is null)
        {
            return 0;
        }

        var items = await _operatorInbox.GetItemsAsync(ct).ConfigureAwait(false);
        return items.Count(item =>
            item.Kind == OperatorWorkItemKindDto.ReconciliationBreak
            && item.Tone is OperatorWorkItemToneDto.Warning or OperatorWorkItemToneDto.Critical
            && IsScopedToPeriodOrBook(item, period, book));
    }

    private static LedgerPeriodSummaryDto BuildSummary(
        LedgerAccountingPeriod period,
        LedgerBookRecord book,
        LedgerPeriodFinancials financials,
        decimal? variance,
        int openBreakCount,
        LedgerPeriodSignoffStatusDto signoffStatus,
        DateTimeOffset completedAt)
        => new(
            PeriodId: period.PeriodId,
            LedgerBookId: RequireLedgerBookId(period),
            FiscalYear: period.FiscalYear,
            PeriodNo: period.PeriodNo,
            Label: period.Label,
            TrialBalance: financials.TrialBalance
                .Select(row => row with
                {
                    AccountingBasis = book.AccountingBasis,
                    AccountingPolicyId = book.AccountingPolicyId,
                    AccountingPolicyVersion = book.AccountingPolicyVersion,
                    Dimensions = EnsureBookDimensions(row.Dimensions, book)
                })
                .ToArray(),
            TotalDebits: financials.TotalDebits,
            TotalCredits: financials.TotalCredits,
            NetIncome: financials.NetIncome,
            PeriodOnPeriodVariance: variance,
            OpenBreakCount: openBreakCount,
            SignoffStatus: signoffStatus,
            CompletedAt: completedAt,
            AccountingBasis: book.AccountingBasis,
            AccountingPolicyId: book.AccountingPolicyId,
            AccountingPolicyVersion: book.AccountingPolicyVersion);

    private static OperatorWorkItemDto BuildPeriodCloseWorkItem(
        LedgerAccountingPeriod period,
        LedgerBookRecord book,
        string targetStatus,
        string requiredRole,
        string toleranceProfile,
        DateTimeOffset now)
        => new(
            WorkItemId: $"ledger-period-close-{period.PeriodId:N}",
            Kind: OperatorWorkItemKindDto.LedgerPeriodClose,
            Label: $"{book.AccountingBasis} {targetStatus} sign-off required",
            Detail: $"{book.DisplayName} {period.Label} ({book.AccountingBasis} basis, policy {book.AccountingPolicyId}/{book.AccountingPolicyVersion}) is in {targetStatus}. Required sign-off role: {requiredRole}. Tolerance profile: {toleranceProfile}. Open FundReconciliation before approving the close.",
            Tone: string.Equals(targetStatus, HardClosedStatus, StringComparison.Ordinal)
                ? OperatorWorkItemToneDto.Critical
                : OperatorWorkItemToneDto.Warning,
            CreatedAt: now,
            AuditReference: period.PeriodId.ToString("N"),
            Workspace: "Accounting",
            TargetRoute: UiApiRoutes.ReconciliationBreakQueue,
            TargetPageTag: "FundReconciliation",
            Scope: $"ledger-book:{book.LedgerBookId:N};ledger-period:{period.PeriodId:N}",
            RequiredSignoffRole: requiredRole,
            ToleranceProfileId: toleranceProfile,
            SignoffStatus: LedgerPeriodSignoffStatusDto.Pending.ToString());

    private static bool IsScopedToPeriodOrBook(
        OperatorWorkItemDto item,
        LedgerAccountingPeriod period,
        LedgerBookRecord book)
    {
        var periodId = period.PeriodId;
        var ledgerBookId = book.LedgerBookId;
        return ContainsIdentifier(item.Scope, periodId)
               || ContainsIdentifier(item.AuditReference, periodId)
               || ContainsIdentifier(item.TargetRoute, periodId)
               || ContainsIdentifier(item.Scope, ledgerBookId)
               || ContainsIdentifier(item.AuditReference, ledgerBookId)
               || ContainsIdentifier(item.TargetRoute, ledgerBookId);
    }

    private static bool ContainsIdentifier(string? value, Guid id)
        => !string.IsNullOrWhiteSpace(value)
           && (value.Contains(id.ToString("D"), StringComparison.OrdinalIgnoreCase)
               || value.Contains(id.ToString("N"), StringComparison.OrdinalIgnoreCase));

    private static void AddMissingRequiredScopeIssues(
        LedgerBookRolloutAssessmentRequest request,
        IReadOnlyList<LedgerBookRecord> books,
        ICollection<LedgerBookRolloutIssueDto> issues)
    {
        var fundProfileId = NormalizeOptional(request.FundProfileId);
        foreach (var required in request.RequiredScopes)
        {
            if (required.FundStructureNodeId == Guid.Empty)
            {
                issues.Add(new LedgerBookRolloutIssueDto(
                    "LedgerBookRequiredScopeInvalid",
                    LedgerBookRolloutIssueSeverityDto.Critical,
                    "Required ledger-book scope includes an empty fund-structure node id.",
                    AccountingBasis: required.AccountingBasis));
                continue;
            }

            var exists = books.Any(book =>
                book.FundStructureNodeId == required.FundStructureNodeId &&
                book.FundStructureNodeKind == required.FundStructureNodeKind &&
                book.AccountingBasis == required.AccountingBasis &&
                (fundProfileId is null || string.Equals(book.FundProfileId, fundProfileId, StringComparison.OrdinalIgnoreCase)));
            if (exists)
            {
                continue;
            }

            var displayName = NormalizeOptional(required.DisplayName) ?? required.FundStructureNodeId.ToString("D");
            issues.Add(new LedgerBookRolloutIssueDto(
                "LedgerBookRequiredScopeMissing",
                LedgerBookRolloutIssueSeverityDto.Critical,
                $"Required ledger-book scope '{displayName}' for {required.AccountingBasis} basis is missing.",
                Scope: BuildRolloutScope(fundProfileId, required.FundStructureNodeId, required.FundStructureNodeKind, required.AccountingBasis),
                FundStructureNodeId: required.FundStructureNodeId,
                AccountingBasis: required.AccountingBasis));
        }
    }

    private static void AddUnscopedPeriodIssues(
        IReadOnlyList<LedgerBookRecord> scopedBooks,
        ICollection<LedgerBookRolloutIssueDto> issues,
        IReadOnlyList<LedgerAccountingPeriod> allPeriods)
    {
        var unscopedPeriods = allPeriods.Where(static period => !period.LedgerBookId.HasValue).ToArray();
        if (unscopedPeriods.Length == 0)
        {
            return;
        }

        var relevantPeriodCount = scopedBooks.Count == 0
            ? unscopedPeriods.Length
            : unscopedPeriods.Count(period => scopedBooks.Any(book =>
                book.FundStructureNodeId == Guid.Empty ||
                period.Label.Contains(book.FundProfileId, StringComparison.OrdinalIgnoreCase)));
        if (relevantPeriodCount == 0)
        {
            return;
        }

        issues.Add(new LedgerBookRolloutIssueDto(
            "LedgerPeriodsMissingLedgerBookScope",
            LedgerBookRolloutIssueSeverityDto.Critical,
            $"{relevantPeriodCount} retained ledger period(s) are missing ledger-book scope and require migration/backfill before production cutover."));
    }

    private static string BuildRolloutScope(LedgerBookRecord book)
        => BuildRolloutScope(book.FundProfileId, book.FundStructureNodeId, book.FundStructureNodeKind, book.AccountingBasis);

    private static string BuildRolloutScope(
        string? fundProfileId,
        Guid? fundStructureNodeId,
        FundStructureNodeKindDto? fundStructureNodeKind,
        AccountingBasisKindDto? accountingBasis)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(fundProfileId))
        {
            parts.Add($"fund:{fundProfileId.Trim()}");
        }

        if (fundStructureNodeId.HasValue)
        {
            parts.Add($"node:{fundStructureNodeId.Value:D}");
        }

        if (fundStructureNodeKind.HasValue)
        {
            parts.Add($"kind:{fundStructureNodeKind.Value}");
        }

        if (accountingBasis.HasValue)
        {
            parts.Add($"basis:{accountingBasis.Value}");
        }

        return parts.Count == 0 ? "ledger-book-rollout" : string.Join(";", parts);
    }

    private static LedgerPeriodFinancials CalculateFinancials(IReadOnlyList<LedgerJournalEntryRecord> entries)
    {
        var totals = new Dictionary<string, AccountAccumulator>(StringComparer.Ordinal);
        var totalDebits = 0m;
        var totalCredits = 0m;

        foreach (var entry in entries)
        {
            var entryDimensions = BuildDimensions(entry.Entry.Metadata);
            foreach (var line in entry.Entry.Lines)
            {
                totalDebits += line.Debit;
                totalCredits += line.Credit;

                var dimensions = BuildDimensions(line.Dimensions) ?? BuildDimensions(entry.Entry.Metadata, line.EntryId) ?? entryDimensions;
                var key = BuildAccumulatorKey(line.Account, dimensions);
                if (!totals.TryGetValue(key, out var accumulator))
                {
                    accumulator = new AccountAccumulator(line.Account, dimensions);
                    totals[key] = accumulator;
                }

                accumulator.Add(line.Debit, line.Credit);
            }
        }

        var trialBalance = totals.Values
            .Select(static accumulator =>
            {
                var balance = CalculateNetBalance(accumulator.Account, accumulator.Debits, accumulator.Credits);
                return new LedgerPeriodTrialBalanceLineDto(
                    accumulator.Account.Name,
                    accumulator.Account.AccountType.ToString(),
                    accumulator.Account.Symbol,
                    accumulator.Account.FinancialAccountId,
                    accumulator.Debits,
                    accumulator.Credits,
                    balance,
                    accumulator.EntryCount,
                    Dimensions: accumulator.Dimensions);
            })
            .OrderBy(static row => row.AccountType, StringComparer.Ordinal)
            .ThenBy(static row => row.AccountName, StringComparer.Ordinal)
            .ThenBy(static row => row.Symbol, StringComparer.Ordinal)
            .ThenBy(static row => row.FinancialAccountId, StringComparer.Ordinal)
            .ThenBy(static row => BuildDimensionsKey(row.Dimensions), StringComparer.Ordinal)
            .ToArray();

        var netIncome = trialBalance.Sum(static row =>
            row.AccountType switch
            {
                nameof(LedgerAccountType.Revenue) => row.Balance,
                nameof(LedgerAccountType.Expense) => -row.Balance,
                _ => 0m
            });

        return new LedgerPeriodFinancials(trialBalance, totalDebits, totalCredits, netIncome);
    }

    private static decimal CalculateNetBalance(LedgerAccount account, decimal debits, decimal credits)
        => account.AccountType is LedgerAccountType.Asset or LedgerAccountType.Expense
            ? debits - credits
            : credits - debits;

    private static LedgerDimensionSetDto? EnsureBookDimensions(
        LedgerDimensionSetDto? dimensions,
        LedgerBookRecord book)
    {
        var fundId = NormalizeOptional(dimensions?.FundId) ?? book.FundProfileId;
        if (dimensions is null)
        {
            return new LedgerDimensionSetDto(FundId: fundId);
        }

        return dimensions with { FundId = fundId };
    }

    private static LedgerDimensionSetDto? BuildDimensions(JournalEntryMetadata metadata)
    {
        var tags = metadata.Tags;
        var externalGlDimensions = ExtractExternalGlDimensions(tags);
        var dimensionSet = new LedgerDimensionSetDto(
            FundId: FirstTag(tags, "fundId", "fundProfileId"),
            EntityId: FirstTag(tags, "entityId", "legalEntityId"),
            SleeveId: FirstTag(tags, "sleeveId"),
            StrategyId: metadata.StrategyId ?? FirstTag(tags, "strategyId"),
            InvestorId: metadata.InvestorId ?? FirstTag(tags, "investorId"),
            CapitalAccountId: metadata.CapitalAccountId ?? FirstTag(tags, "capitalAccountId"),
            InstrumentId: metadata.SecurityId,
            TaxLotId: FirstTag(tags, "taxLotId", "lotId"),
            CostCenterId: FirstTag(tags, "costCenterId"),
            CounterpartyId: metadata.CounterpartyAccountId ?? FirstTag(tags, "counterpartyId", "counterpartyAccountId"),
            ExternalGlDimensions: externalGlDimensions,
            OrganizationId: FirstTag(tags, "organizationId"),
            PortfolioId: FirstTag(tags, "portfolioId"),
            BookId: metadata.LedgerBook ?? FirstTag(tags, "bookId"),
            AccountId: metadata.FinancialAccountId ?? FirstTag(tags, "accountId"),
            CustomerId: FirstTag(tags, "customerId"),
            VendorId: FirstTag(tags, "vendorId"),
            ProjectId: metadata.ProjectId ?? FirstTag(tags, "projectId"));

        return HasAnyDimension(dimensionSet) ? dimensionSet : null;
    }

    private static LedgerDimensionSetDto? BuildDimensions(LedgerLineDimensionSet? dimensions)
    {
        if (dimensions is null)
        {
            return null;
        }

        var dimensionSet = new LedgerDimensionSetDto(
            FundId: dimensions.FundId,
            EntityId: dimensions.EntityId,
            SleeveId: dimensions.SleeveId,
            StrategyId: dimensions.StrategyId,
            InvestorId: dimensions.InvestorId,
            CapitalAccountId: dimensions.CapitalAccountId,
            InstrumentId: dimensions.InstrumentId,
            TaxLotId: dimensions.TaxLotId,
            CostCenterId: dimensions.CostCenterId,
            CounterpartyId: dimensions.CounterpartyId,
            ExternalGlDimensions: dimensions.ExternalGlDimensions,
            OrganizationId: dimensions.OrganizationId,
            PortfolioId: dimensions.PortfolioId,
            BookId: dimensions.BookId,
            AccountId: dimensions.AccountId,
            CustomerId: dimensions.CustomerId,
            VendorId: dimensions.VendorId,
            ProjectId: dimensions.ProjectId);

        return HasAnyDimension(dimensionSet) ? dimensionSet : null;
    }

    private static LedgerDimensionSetDto? BuildDimensions(JournalEntryMetadata metadata, Guid lineEntryId)
    {
        var tags = metadata.Tags;
        var prefix = $"lineDimensions.{lineEntryId:N}.";
        if (tags is null || tags.Keys.All(key => !key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var instrumentId = Guid.TryParse(FirstTag(tags, prefix + "instrumentId"), out var parsedInstrumentId)
            ? parsedInstrumentId
            : (Guid?)null;
        var dimensionSet = new LedgerDimensionSetDto(
            FundId: FirstTag(tags, prefix + "fundId"),
            EntityId: FirstTag(tags, prefix + "entityId"),
            SleeveId: FirstTag(tags, prefix + "sleeveId"),
            StrategyId: FirstTag(tags, prefix + "strategyId"),
            InvestorId: FirstTag(tags, prefix + "investorId"),
            CapitalAccountId: FirstTag(tags, prefix + "capitalAccountId"),
            InstrumentId: instrumentId,
            TaxLotId: FirstTag(tags, prefix + "taxLotId"),
            CostCenterId: FirstTag(tags, prefix + "costCenterId"),
            CounterpartyId: FirstTag(tags, prefix + "counterpartyId"),
            ExternalGlDimensions: ExtractExternalGlDimensions(tags, prefix),
            OrganizationId: FirstTag(tags, prefix + "organizationId"),
            PortfolioId: FirstTag(tags, prefix + "portfolioId"),
            BookId: FirstTag(tags, prefix + "bookId"),
            AccountId: FirstTag(tags, prefix + "accountId"),
            CustomerId: FirstTag(tags, prefix + "customerId"),
            VendorId: FirstTag(tags, prefix + "vendorId"),
            ProjectId: FirstTag(tags, prefix + "projectId"));

        return HasAnyDimension(dimensionSet) ? dimensionSet : null;
    }

    private static bool HasAnyDimension(LedgerDimensionSetDto dimensions)
        => !string.IsNullOrWhiteSpace(dimensions.FundId)
           || !string.IsNullOrWhiteSpace(dimensions.EntityId)
           || !string.IsNullOrWhiteSpace(dimensions.SleeveId)
           || !string.IsNullOrWhiteSpace(dimensions.StrategyId)
           || !string.IsNullOrWhiteSpace(dimensions.InvestorId)
           || !string.IsNullOrWhiteSpace(dimensions.CapitalAccountId)
           || dimensions.InstrumentId.HasValue
           || !string.IsNullOrWhiteSpace(dimensions.TaxLotId)
           || !string.IsNullOrWhiteSpace(dimensions.CostCenterId)
           || !string.IsNullOrWhiteSpace(dimensions.CounterpartyId)
           || dimensions.ExternalGlDimensions.Count > 0
           || !string.IsNullOrWhiteSpace(dimensions.OrganizationId)
           || !string.IsNullOrWhiteSpace(dimensions.PortfolioId)
           || !string.IsNullOrWhiteSpace(dimensions.BookId)
           || !string.IsNullOrWhiteSpace(dimensions.AccountId)
           || !string.IsNullOrWhiteSpace(dimensions.CustomerId)
           || !string.IsNullOrWhiteSpace(dimensions.VendorId)
           || !string.IsNullOrWhiteSpace(dimensions.ProjectId);

    private static IReadOnlyDictionary<string, string> ExtractExternalGlDimensions(
        IReadOnlyDictionary<string, string>? tags)
        => ExtractExternalGlDimensions(tags, prefix: null);

    private static IReadOnlyDictionary<string, string> ExtractExternalGlDimensions(
        IReadOnlyDictionary<string, string>? tags,
        string? prefix)
    {
        if (tags is null || tags.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in tags)
        {
            var key = NormalizeOptional(pair.Key);
            var value = NormalizeOptional(pair.Value);
            if (key is null || value is null)
            {
                continue;
            }

            var scopedKey = prefix is null
                ? key
                : StripPrefix(key, prefix);
            if (scopedKey is null)
            {
                continue;
            }

            var dimensionKey = StripPrefix(scopedKey, "externalGl.")
                               ?? StripPrefix(scopedKey, "externalGl:")
                               ?? StripPrefix(scopedKey, "gl.")
                               ?? StripPrefix(scopedKey, "gl:");
            if (!string.IsNullOrWhiteSpace(dimensionKey))
            {
                result[dimensionKey] = value;
            }
        }

        return result;
    }

    private static string? StripPrefix(string value, string prefix)
        => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? NormalizeOptional(value[prefix.Length..])
            : null;

    private static string? FirstTag(
        IReadOnlyDictionary<string, string>? tags,
        params string[] keys)
    {
        if (tags is null || tags.Count == 0)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (tags.TryGetValue(key, out var value))
            {
                return NormalizeOptional(value);
            }
        }

        return null;
    }

    private static string BuildAccumulatorKey(LedgerAccount account, LedgerDimensionSetDto? dimensions)
        => string.Join(
            "\u001f",
            account.Name,
            account.AccountType,
            account.Symbol ?? string.Empty,
            account.FinancialAccountId ?? string.Empty,
            BuildDimensionsKey(dimensions));

    private static string BuildDimensionsKey(LedgerDimensionSetDto? dimensions)
    {
        if (dimensions is null)
        {
            return string.Empty;
        }

        var externalGl = dimensions.ExternalGlDimensions
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => $"{pair.Key.Trim()}={pair.Value.Trim()}");
        return string.Join(
            "\u001e",
            dimensions.FundId ?? string.Empty,
            dimensions.EntityId ?? string.Empty,
            dimensions.SleeveId ?? string.Empty,
            dimensions.StrategyId ?? string.Empty,
            dimensions.InvestorId ?? string.Empty,
            dimensions.CapitalAccountId ?? string.Empty,
            dimensions.InstrumentId?.ToString("D") ?? string.Empty,
            dimensions.TaxLotId ?? string.Empty,
            dimensions.CostCenterId ?? string.Empty,
            dimensions.CounterpartyId ?? string.Empty,
            dimensions.OrganizationId ?? string.Empty,
            dimensions.PortfolioId ?? string.Empty,
            dimensions.BookId ?? string.Empty,
            dimensions.AccountId ?? string.Empty,
            dimensions.CustomerId ?? string.Empty,
            dimensions.VendorId ?? string.Empty,
            dimensions.ProjectId ?? string.Empty,
            string.Join("\u001d", externalGl));
    }

    private static LedgerBookDto MapBook(LedgerBookRecord record)
        => new(
            record.LedgerBookId,
            record.FundProfileId,
            record.FundStructureNodeId,
            record.FundStructureNodeKind,
            record.DisplayName,
            record.BaseCurrency,
            record.CreatedAt,
            record.UpdatedAt,
            record.Description,
            record.AccountingBasis,
            record.AccountingPolicyId,
            record.AccountingPolicyVersion);

    private static LedgerPeriodDto MapPeriod(LedgerAccountingPeriod period, LedgerBookRecord? book)
        => new(
            period.PeriodId,
            RequireLedgerBookId(period),
            period.FiscalYear,
            period.PeriodNo,
            period.Label,
            period.StartDate,
            period.EndDate,
            ParseStatus(period.Status),
            period.OpenedAt,
            period.ClosedAt,
            period.Version,
            book?.AccountingBasis ?? AccountingBasisKindDto.Primary,
            book?.AccountingPolicyId ?? "legacy-v1",
            book?.AccountingPolicyVersion ?? "legacy-v1");

    private async Task<IReadOnlyDictionary<Guid, LedgerBookRecord>> LoadBooksByIdAsync(
        IReadOnlyList<LedgerAccountingPeriod> periods,
        CancellationToken ct)
    {
        var result = new Dictionary<Guid, LedgerBookRecord>();
        foreach (var ledgerBookId in periods
                     .Select(RequireLedgerBookId)
                     .Distinct())
        {
            result[ledgerBookId] = await RequireBookAsync(ledgerBookId, ct).ConfigureAwait(false);
        }

        return result;
    }

    private static LedgerPeriodStatusDto ParseStatus(string status)
        => status switch
        {
            OpenStatus => LedgerPeriodStatusDto.Open,
            SoftClosedStatus => LedgerPeriodStatusDto.SoftClosed,
            HardClosedStatus => LedgerPeriodStatusDto.HardClosed,
            _ => throw new LedgerBookValidationException($"Unknown ledger period status '{status}'.")
        };

    private static Guid RequireLedgerBookId(LedgerAccountingPeriod period)
        => period.LedgerBookId is { } ledgerBookId && ledgerBookId != Guid.Empty
            ? ledgerBookId
            : throw new LedgerBookValidationException($"Ledger period '{period.PeriodId}' is not scoped to a ledger book.");

    private static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new LedgerBookValidationException($"{parameterName} is required.");
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class AccountAccumulator
    {
        public AccountAccumulator(LedgerAccount account)
            : this(account, null)
        {
        }

        public AccountAccumulator(LedgerAccount account, LedgerDimensionSetDto? dimensions)
        {
            Account = account;
            Dimensions = dimensions;
        }

        public LedgerAccount Account { get; }
        public LedgerDimensionSetDto? Dimensions { get; }
        public decimal Debits { get; private set; }
        public decimal Credits { get; private set; }
        public int EntryCount { get; private set; }

        public void Add(decimal debit, decimal credit)
        {
            Debits += debit;
            Credits += credit;
            EntryCount++;
        }
    }

    private sealed record LedgerPeriodFinancials(
        IReadOnlyList<LedgerPeriodTrialBalanceLineDto> TrialBalance,
        decimal TotalDebits,
        decimal TotalCredits,
        decimal NetIncome);
}
