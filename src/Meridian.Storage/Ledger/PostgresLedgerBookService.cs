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
        var openBreakCount = await CountOpenBreaksAsync(ct).ConfigureAwait(false);

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
        var openBreakCount = await CountOpenBreaksAsync(ct).ConfigureAwait(false);
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

    private async Task<int> CountOpenBreaksAsync(CancellationToken ct)
    {
        if (_operatorInbox is null)
        {
            return 0;
        }

        var items = await _operatorInbox.GetItemsAsync(ct).ConfigureAwait(false);
        return items.Count(static item =>
            item.Kind == OperatorWorkItemKindDto.ReconciliationBreak
            && item.Tone is OperatorWorkItemToneDto.Warning or OperatorWorkItemToneDto.Critical);
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
                    AccountingPolicyVersion = book.AccountingPolicyVersion
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
            Scope: $"ledger-period:{period.PeriodId:N}",
            RequiredSignoffRole: requiredRole,
            ToleranceProfileId: toleranceProfile,
            SignoffStatus: LedgerPeriodSignoffStatusDto.Pending.ToString());

    private static LedgerPeriodFinancials CalculateFinancials(IReadOnlyList<LedgerJournalEntryRecord> entries)
    {
        var totals = new Dictionary<LedgerAccount, AccountAccumulator>();
        var totalDebits = 0m;
        var totalCredits = 0m;

        foreach (var entry in entries)
        {
            foreach (var line in entry.Entry.Lines)
            {
                totalDebits += line.Debit;
                totalCredits += line.Credit;

                if (!totals.TryGetValue(line.Account, out var accumulator))
                {
                    accumulator = new AccountAccumulator(line.Account);
                    totals[line.Account] = accumulator;
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
                    accumulator.EntryCount);
            })
            .OrderBy(static row => row.AccountType, StringComparer.Ordinal)
            .ThenBy(static row => row.AccountName, StringComparer.Ordinal)
            .ThenBy(static row => row.Symbol, StringComparer.Ordinal)
            .ThenBy(static row => row.FinancialAccountId, StringComparer.Ordinal)
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
        {
            Account = account;
        }

        public LedgerAccount Account { get; }
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
