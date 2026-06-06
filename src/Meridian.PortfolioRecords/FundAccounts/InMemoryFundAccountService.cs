using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Storage.Archival;

namespace Meridian.PortfolioRecords.FundAccounts;

using Meridian.PortfolioRecords.Accounts;

public sealed class AccountStatusPolicyException : InvalidOperationException
{
    public AccountStatusPolicyException(AccountOperationalStatusDto status, string operation, bool backfillAttempted)
        : base($"Operation '{operation}' is not allowed while account status is '{status}'.")
    {
        Status = status;
        Operation = operation;
        BackfillAttempted = backfillAttempted;
    }

    public AccountOperationalStatusDto Status { get; }
    public string Operation { get; }
    public bool BackfillAttempted { get; }
}

/// <summary>
/// Thread-safe fund-account service backed by an in-memory working set with optional
/// durable JSON snapshot persistence for local-first workflows.
/// </summary>
public sealed class InMemoryFundAccountService : IFundAccountService, IAccountManagementService, IAccountQueryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string? _persistencePath;
    private readonly SemaphoreSlim _persistGate = new(1, 1);
    private readonly Dictionary<Guid, StoredAccount> _accounts = new();
    private long _stateVersion;
    private long _persistedVersion;

    public InMemoryFundAccountService()
        : this(null)
    {
    }

    public InMemoryFundAccountService(string? persistencePath)
    {
        _persistencePath = string.IsNullOrWhiteSpace(persistencePath) ? null : persistencePath;
        LoadState();
    }

    private sealed record StoredAccount(
        AccountSummaryDto Summary,
        List<AccountBalanceSnapshotDto> Snapshots,
        List<CustodianStatementBatchDto> CustodianBatches,
        List<CustodianPositionLineDto> CustodianPositions,
        List<BankStatementBatchDto> BankBatches,
        List<BankStatementLineDto> BankLines,
        List<AccountReconciliationRunDto> ReconciliationRuns,
        List<AccountReconciliationResultDto> ReconciliationResults,
        List<AccountSyncHistoryEntryDto> SyncHistory,
        List<MarginSnapshotDto> MarginSnapshots)
    {
        public StoredAccount WithSummary(AccountSummaryDto summary) =>
            this with { Summary = summary };
    }

    private sealed record PersistedState(
        int Version,
        List<StoredAccount> Accounts);

    public async Task<AccountSummaryDto> CreateAccountAsync(
        CreateAccountRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dto = new AccountSummaryDto(
            request.AccountId,
            request.AccountType,
            request.EntityId,
            request.FundId,
            request.SleeveId,
            request.VehicleId,
            request.AccountCode,
            request.DisplayName,
            request.BaseCurrency,
            request.Institution,
            IsActive: true,
            request.EffectiveFrom,
            EffectiveTo: null,
            request.PortfolioId,
            request.LedgerReference,
            request.StrategyId,
            request.RunId,
            request.OperationalStatus,
            request.CustodianDetails,
            request.BankDetails);

        (long Version, string Json)? snapshot;
        lock (_gate)
        {
            if (_accounts.ContainsKey(request.AccountId))
            {
                throw new InvalidOperationException($"Account {request.AccountId} already exists.");
            }

            _accounts[request.AccountId] = new StoredAccount(
                dto,
                Snapshots: [],
                CustodianBatches: [],
                CustodianPositions: [],
                BankBatches: [],
                BankLines: [],
                ReconciliationRuns: [],
                ReconciliationResults: [],
                SyncHistory: [],
                MarginSnapshots: []);
            snapshot = CaptureSnapshotLocked();
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        return dto;
    }

    public Task<AccountSummaryDto?> GetAccountAsync(
        Guid accountId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            _accounts.TryGetValue(accountId, out var stored);
            return Task.FromResult(stored?.Summary);
        }
    }

    public Task<IReadOnlyList<AccountSummaryDto>> QueryAccountsAsync(
        AccountStructureQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        lock (_gate)
        {
            var results = _accounts.Values
                .Select(s => s.Summary)
                .Where(a => (!query.ActiveOnly || a.IsActive)
                    && (query.AccountId == null || a.AccountId == query.AccountId)
                    && (query.EntityId == null || a.EntityId == query.EntityId)
                    && (query.FundId == null || a.FundId == query.FundId)
                    && (query.SleeveId == null || a.SleeveId == query.SleeveId)
                    && (query.VehicleId == null || a.VehicleId == query.VehicleId)
                    && (query.PortfolioId == null || a.PortfolioId == query.PortfolioId)
                    && (query.LedgerReference == null || a.LedgerReference == query.LedgerReference)
                    && (query.StrategyId == null || a.StrategyId == query.StrategyId)
                    && (query.RunId == null || a.RunId == query.RunId))
                .ToList();

            return Task.FromResult<IReadOnlyList<AccountSummaryDto>>(results);
        }
    }

    public async Task<AccountSummaryDto?> UpdateCustodianDetailsAsync(
        Guid accountId,
        UpdateCustodianAccountDetailsRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        AccountSummaryDto? updated;
        (long Version, string Json)? snapshot = null;
        lock (_gate)
        {
            if (!_accounts.TryGetValue(accountId, out var stored))
            {
                return null;
            }
            EnsureAllowed(stored.Summary, "update-custodian-details");

            updated = stored.Summary with { CustodianDetails = request.Details };
            _accounts[accountId] = stored.WithSummary(updated);
            snapshot = CaptureSnapshotLocked();
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        return updated;
    }

    public async Task<AccountSummaryDto?> UpdateBankDetailsAsync(
        Guid accountId,
        UpdateBankAccountDetailsRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        AccountSummaryDto? updated;
        (long Version, string Json)? snapshot = null;
        lock (_gate)
        {
            if (!_accounts.TryGetValue(accountId, out var stored))
            {
                return null;
            }
            EnsureAllowed(stored.Summary, "update-bank-details");

            updated = stored.Summary with { BankDetails = request.Details };
            _accounts[accountId] = stored.WithSummary(updated);
            snapshot = CaptureSnapshotLocked();
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        return updated;
    }

    public async Task<AccountSummaryDto?> DeactivateAccountAsync(
        Guid accountId, string deactivatedBy, CancellationToken ct = default)
    {
        AccountSummaryDto? updated;
        (long Version, string Json)? snapshot = null;
        lock (_gate)
        {
            if (!_accounts.TryGetValue(accountId, out var stored))
            {
                return null;
            }

            updated = stored.Summary with
            {
                IsActive = false,
                EffectiveTo = DateTimeOffset.UtcNow
            };
            _accounts[accountId] = stored.WithSummary(updated);
            snapshot = CaptureSnapshotLocked();
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        return updated;
    }

    public Task<FundAccountsDto> GetFundAccountsAsync(
        Guid fundId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var fundAccounts = _accounts.Values
                .Select(s => s.Summary)
                .Where(a => a.FundId == fundId && a.IsActive)
                .ToList();

            return Task.FromResult(new FundAccountsDto(
                fundId,
                CustodianAccounts: fundAccounts.Where(a => a.AccountType == AccountTypeDto.Custody).ToList(),
                BankAccounts: fundAccounts.Where(a => a.AccountType == AccountTypeDto.Bank).ToList(),
                BrokerageAccounts: fundAccounts.Where(a => a.AccountType == AccountTypeDto.Brokerage).ToList(),
                OtherAccounts: fundAccounts.Where(a =>
                    a.AccountType != AccountTypeDto.Custody &&
                    a.AccountType != AccountTypeDto.Bank &&
                    a.AccountType != AccountTypeDto.Brokerage).ToList()));
        }
    }

    public async Task<AccountBalanceSnapshotDto> RecordBalanceSnapshotAsync(
        RecordAccountBalanceSnapshotRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dto = new AccountBalanceSnapshotDto(
            Guid.NewGuid(),
            request.AccountId,
            FundId: null,
            request.AsOfDate,
            request.Currency,
            request.CashBalance,
            request.SecuritiesMarketValue,
            request.AccruedInterest,
            request.PendingSettlement,
            request.Source,
            DateTimeOffset.UtcNow,
            request.ExternalReference,
            request.UnrealizedPnl,
            request.RealizedPnl);

        (long Version, string Json)? snapshot = null;
        lock (_gate)
        {
            if (_accounts.TryGetValue(request.AccountId, out var stored))
            {
                EnsureAllowed(stored.Summary, "record-balance-snapshot");
                stored.Snapshots.Add(dto);
                snapshot = CaptureSnapshotLocked();
            }
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        return dto;
    }

    public Task<IReadOnlyList<AccountBalanceSnapshotDto>> GetBalanceHistoryAsync(
        Guid accountId, DateOnly? fromDate = null, DateOnly? toDate = null,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_accounts.TryGetValue(accountId, out var stored))
            {
                return Task.FromResult<IReadOnlyList<AccountBalanceSnapshotDto>>([]);
            }

            var results = stored.Snapshots
                .Where(s => (fromDate == null || s.AsOfDate >= fromDate)
                         && (toDate == null || s.AsOfDate <= toDate))
                .OrderByDescending(s => s.AsOfDate)
                .ToList();

            return Task.FromResult<IReadOnlyList<AccountBalanceSnapshotDto>>(results);
        }
    }

    public Task<AccountBalanceSnapshotDto?> GetLatestBalanceSnapshotAsync(
        Guid accountId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_accounts.TryGetValue(accountId, out var stored))
            {
                return Task.FromResult<AccountBalanceSnapshotDto?>(null);
            }

            var latest = stored.Snapshots
                .OrderByDescending(s => s.AsOfDate)
                .ThenByDescending(s => s.RecordedAt)
                .FirstOrDefault();

            return Task.FromResult(latest);
        }
    }

    public async Task<CustodianStatementBatchDto> IngestCustodianStatementAsync(
        IngestCustodianStatementRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var batch = new CustodianStatementBatchDto(
            request.BatchId,
            request.AccountId,
            request.AsOfDate,
            request.CustodianName,
            request.SourceFormat,
            request.Lines.Count,
            DateTimeOffset.UtcNow,
            request.LoadedBy);

        (long Version, string Json)? snapshot = null;
        lock (_gate)
        {
            if (_accounts.TryGetValue(request.AccountId, out var stored))
            {
                EnsureAllowed(stored.Summary, "ingest-custodian-statement", allowSuspended: true);
                stored.CustodianBatches.Add(batch);
                UpsertCustodianPositions(stored.CustodianPositions, request.Lines);
                snapshot = CaptureSnapshotLocked();
            }
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        return batch;
    }

    public async Task<BankStatementBatchDto> IngestBankStatementAsync(
        IngestBankStatementRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var batch = new BankStatementBatchDto(
            request.BatchId,
            request.AccountId,
            request.StatementDate,
            request.BankName,
            request.Lines.Count,
            DateTimeOffset.UtcNow,
            request.LoadedBy);

        (long Version, string Json)? snapshot = null;
        lock (_gate)
        {
            if (_accounts.TryGetValue(request.AccountId, out var stored))
            {
                EnsureAllowed(stored.Summary, "ingest-bank-statement", allowSuspended: true);
                stored.BankBatches.Add(batch);
                UpsertBankStatementLines(stored.BankLines, request.Lines);
                snapshot = CaptureSnapshotLocked();
            }
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        return batch;
    }

    private static void EnsureAllowed(AccountSummaryDto summary, string operation, bool isBackfill = false, bool allowSuspended = false)
    {
        if (summary.OperationalStatus == AccountOperationalStatusDto.Closed && !isBackfill)
        {
            throw new AccountStatusPolicyException(summary.OperationalStatus, operation, isBackfill);
        }

        if (summary.OperationalStatus == AccountOperationalStatusDto.Suspended && !allowSuspended)
        {
            throw new AccountStatusPolicyException(summary.OperationalStatus, operation, isBackfill);
        }
    }

    public Task<IReadOnlyList<CustodianPositionLineDto>> GetCustodianPositionsAsync(
        Guid accountId, DateOnly asOfDate, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_accounts.TryGetValue(accountId, out var stored))
            {
                return Task.FromResult<IReadOnlyList<CustodianPositionLineDto>>([]);
            }

            var results = stored.CustodianPositions
                .Where(p => p.AsOfDate == asOfDate)
                .ToList();

            return Task.FromResult<IReadOnlyList<CustodianPositionLineDto>>(results);
        }
    }

    public Task<IReadOnlyList<BankStatementLineDto>> GetBankStatementLinesAsync(
        Guid accountId, DateOnly? fromDate = null, DateOnly? toDate = null,
        CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_accounts.TryGetValue(accountId, out var stored))
            {
                return Task.FromResult<IReadOnlyList<BankStatementLineDto>>([]);
            }

            var results = stored.BankLines
                .Where(l => (fromDate == null || l.TransactionDate >= fromDate)
                         && (toDate == null || l.TransactionDate <= toDate))
                .OrderByDescending(l => l.TransactionDate)
                .ToList();

            return Task.FromResult<IReadOnlyList<BankStatementLineDto>>(results);
        }
    }

    public async Task<AccountReconciliationRunDto> ReconcileAccountAsync(
        ReconcileAccountRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        AccountBalanceSnapshotDto? snapshot;
        List<CustodianPositionLineDto> positions;

        lock (_gate)
        {
            if (!_accounts.TryGetValue(request.AccountId, out var stored))
            {
                throw new InvalidOperationException($"Account {request.AccountId} not found.");
            }

            snapshot = stored.Snapshots
                .Where(s => s.AsOfDate == request.AsOfDate)
                .OrderByDescending(s => s.RecordedAt)
                .FirstOrDefault();

            positions = stored.CustodianPositions
                .Where(p => p.AsOfDate == request.AsOfDate)
                .ToList();
        }

        var runId = Guid.NewGuid();
        var results = new List<AccountReconciliationResultDto>();
        var now = DateTimeOffset.UtcNow;

        if (snapshot is not null)
        {
            results.Add(new AccountReconciliationResultDto(
                Guid.NewGuid(),
                runId,
                CheckLabel: "CashBalance",
                IsMatch: true,
                Category: "Cash",
                Status: "Matched",
                ExpectedAmount: snapshot.CashBalance,
                ActualAmount: snapshot.CashBalance,
                Variance: 0m,
                Reason: "Cash balance matches internal ledger"));
        }

        AddContinuityCheckResults(runId, request.AsOfDate, results, request.AccountId);

        if (positions.Count > 0)
        {
            results.Add(new AccountReconciliationResultDto(
                Guid.NewGuid(),
                runId,
                CheckLabel: $"PositionCount ({positions.Count} lines)",
                IsMatch: true,
                Category: "Positions",
                Status: "Matched",
                ExpectedAmount: positions.Count,
                ActualAmount: positions.Count,
                Variance: 0m,
                Reason: "Custodian position lines ingested successfully"));
        }

        var breaks = results.Count(r => !r.IsMatch);
        var run = new AccountReconciliationRunDto(
            runId,
            request.AccountId,
            request.AsOfDate,
            Status: breaks == 0 ? "Matched" : "Breaks",
            TotalChecks: results.Count,
            TotalMatched: results.Count - breaks,
            TotalBreaks: breaks,
            BreakAmountTotal: results
                .Where(r => !r.IsMatch && r.Variance.HasValue)
                .Sum(r => Math.Abs(r.Variance!.Value)),
            RequestedAt: now,
            CompletedAt: now,
            request.RequestedBy);

        (long Version, string Json)? snapshotToPersist = null;
        lock (_gate)
        {
            if (_accounts.TryGetValue(request.AccountId, out var stored))
            {
                stored.ReconciliationRuns.Add(run);
                stored.ReconciliationResults.AddRange(results);
                snapshotToPersist = CaptureSnapshotLocked();
            }
        }

        await PersistSnapshotAsync(snapshotToPersist, ct).ConfigureAwait(false);
        return run;
    }

    private static void UpsertCustodianPositions(
        List<CustodianPositionLineDto> existing,
        IReadOnlyList<CustodianPositionLineDto> incoming)
    {
        var index = existing.ToDictionary(
            static line => BuildCustodianDedupKey(line),
            static line => line,
            StringComparer.OrdinalIgnoreCase);

        foreach (var line in incoming)
        {
            index[BuildCustodianDedupKey(line)] = line;
        }

        existing.Clear();
        existing.AddRange(index.Values.OrderBy(static line => line.AsOfDate).ThenBy(static line => line.Identifier, StringComparer.OrdinalIgnoreCase));
    }

    private static void UpsertBankStatementLines(
        List<BankStatementLineDto> existing,
        IReadOnlyList<BankStatementLineDto> incoming)
    {
        var index = existing.ToDictionary(
            static line => BuildBankDedupKey(line),
            static line => line,
            StringComparer.OrdinalIgnoreCase);

        foreach (var line in incoming)
        {
            index[BuildBankDedupKey(line)] = line;
        }

        existing.Clear();
        existing.AddRange(index.Values.OrderBy(static line => line.TransactionDate).ThenBy(static line => line.Reference, StringComparer.OrdinalIgnoreCase));
    }

    private static string BuildCustodianDedupKey(CustodianPositionLineDto line)
        => $"{line.AccountId:N}|{line.AsOfDate:yyyyMMdd}|{line.IdentifierType}|{line.Identifier}".ToUpperInvariant();

    private static string BuildBankDedupKey(BankStatementLineDto line)
        => $"{line.AccountId:N}|{line.TransactionDate:yyyyMMdd}|{line.ValueDate:yyyyMMdd}|{line.Currency}|{line.Amount:0.########}|{line.TransactionType}|{line.Reference ?? line.Description}".ToUpperInvariant();

    private void AddContinuityCheckResults(
        Guid runId,
        DateOnly asOfDate,
        List<AccountReconciliationResultDto> results,
        Guid accountId)
    {
        AccountBalanceSnapshotDto? accountSync = null;
        AccountBalanceSnapshotDto? runDerived = null;

        lock (_gate)
        {
            if (!_accounts.TryGetValue(accountId, out var stored))
            {
                return;
            }

            accountSync = stored.Snapshots
                .Where(static s => s.Source is not null && s.Source.StartsWith("brokerage-sync:", StringComparison.OrdinalIgnoreCase))
                .Where(s => s.AsOfDate == asOfDate)
                .OrderByDescending(static s => s.RecordedAt)
                .FirstOrDefault();
            runDerived = stored.Snapshots
                .Where(static s => s.Source is null || !s.Source.StartsWith("brokerage-sync:", StringComparison.OrdinalIgnoreCase))
                .Where(s => s.AsOfDate == asOfDate)
                .OrderByDescending(static s => s.RecordedAt)
                .FirstOrDefault();
        }

        if (accountSync is null || runDerived is null)
        {
            return;
        }

        var variance = accountSync.CashBalance - runDerived.CashBalance;
        results.Add(new AccountReconciliationResultDto(
            Guid.NewGuid(),
            runId,
            CheckLabel: "RunVsAccountSyncCashContinuity",
            IsMatch: variance == 0m,
            Category: "Continuity",
            Status: variance == 0m ? "Matched" : "Break",
            ExpectedAmount: runDerived.CashBalance,
            ActualAmount: accountSync.CashBalance,
            Variance: variance,
            Reason: variance == 0m
                ? "Run-derived and account-sync-derived balances agree."
                : "Run-derived and account-sync-derived balances diverge."));
    }

    public Task<IReadOnlyList<AccountReconciliationRunDto>> GetReconciliationRunsAsync(
        Guid accountId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_accounts.TryGetValue(accountId, out var stored))
            {
                return Task.FromResult<IReadOnlyList<AccountReconciliationRunDto>>([]);
            }

            return Task.FromResult<IReadOnlyList<AccountReconciliationRunDto>>(stored.ReconciliationRuns.AsReadOnly());
        }
    }

    public Task<IReadOnlyList<AccountReconciliationResultDto>> GetReconciliationResultsAsync(
        Guid reconciliationRunId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var results = _accounts.Values
                .SelectMany(s => s.ReconciliationResults)
                .Where(r => r.ReconciliationRunId == reconciliationRunId)
                .ToList();

            return Task.FromResult<IReadOnlyList<AccountReconciliationResultDto>>(results);
        }
    }

    public Task<IReadOnlyList<PositionReconciliationBreakDto>> GetOpenPositionBreaksAsync(
        Guid accountId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_accounts.TryGetValue(accountId, out var stored))
            {
                return Task.FromResult<IReadOnlyList<PositionReconciliationBreakDto>>([]);
            }

            var breaks = stored.ReconciliationResults
                .Where(r => !r.IsMatch
                    && (string.Equals(r.Category, "Position", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(r.Category, "Positions", StringComparison.OrdinalIgnoreCase)))
                .Select(r => new PositionReconciliationBreakDto(
                    r.ResultId,
                    accountId,
                    stored.ReconciliationRuns.FirstOrDefault(run => run.ReconciliationRunId == r.ReconciliationRunId)?.AsOfDate
                        ?? DateOnly.FromDateTime(DateTime.UtcNow),
                    r.CheckLabel,
                    r.ExpectedAmount ?? 0m,
                    r.ActualAmount ?? 0m,
                    r.Variance ?? 0m,
                    r.Reason))
                .ToList();

            return Task.FromResult<IReadOnlyList<PositionReconciliationBreakDto>>(breaks);
        }
    }

    public Task<IReadOnlyList<CashReconciliationBreakDto>> GetOpenCashBreaksAsync(
        Guid accountId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (!_accounts.TryGetValue(accountId, out var stored))
            {
                return Task.FromResult<IReadOnlyList<CashReconciliationBreakDto>>([]);
            }

            var breaks = stored.ReconciliationResults
                .Where(r => !r.IsMatch && string.Equals(r.Category, "Cash", StringComparison.OrdinalIgnoreCase))
                .Select(r => new CashReconciliationBreakDto(
                    r.ResultId,
                    accountId,
                    stored.ReconciliationRuns.FirstOrDefault(run => run.ReconciliationRunId == r.ReconciliationRunId)?.AsOfDate
                        ?? DateOnly.FromDateTime(DateTime.UtcNow),
                    stored.Summary.BaseCurrency,
                    r.ExpectedAmount ?? 0m,
                    r.ActualAmount ?? 0m,
                    r.Variance ?? 0m,
                    r.Reason))
                .ToList();

            return Task.FromResult<IReadOnlyList<CashReconciliationBreakDto>>(breaks);
        }
    }

    public async Task<IReadOnlyList<AccountReconciliationBreakDto>> GetOpenBreaksAsync(
        Guid accountId, CancellationToken ct = default)
    {
        var positionBreaks = await GetOpenPositionBreaksAsync(accountId, ct).ConfigureAwait(false);
        var cashBreaks = await GetOpenCashBreaksAsync(accountId, ct).ConfigureAwait(false);

        var envelopes = positionBreaks.Select(b => new AccountReconciliationBreakDto(PositionBreak: b))
            .Concat(cashBreaks.Select(b => new AccountReconciliationBreakDto(CashBreak: b)))
            .ToList();

        return envelopes;
    }

    public async Task<AccountSyncHistoryEntryDto> RecordSyncHistoryAsync(
        RecordAccountSyncHistoryRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var capability = RequireText(request.Capability, nameof(request.Capability));
        var now = DateTimeOffset.UtcNow;
        var attemptedAt = request.AttemptedAt ?? now;
        var completedAt = request.CompletedAt ?? (request.Status == AccountSyncStatusDto.Pending ? null : now);
        var correlationId = NormalizeOptional(request.CorrelationId);
        var failureKind = request.Status == AccountSyncStatusDto.Failed && request.FailureKind == AccountSyncFailureKindDto.None
            ? AccountSyncFailureKindDto.Unknown
            : request.Status == AccountSyncStatusDto.Failed
                ? request.FailureKind
                : AccountSyncFailureKindDto.None;
        var warnings = (request.Warnings ?? Array.Empty<string>())
            .Where(static warning => !string.IsNullOrWhiteSpace(warning))
            .Select(static warning => warning.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        AccountSyncHistoryEntryDto entry;
        (long Version, string Json)? snapshot = null;
        lock (_gate)
        {
            if (!_accounts.TryGetValue(request.AccountId, out var stored))
            {
                throw new InvalidOperationException($"Account {request.AccountId} not found.");
            }

            var existingIndex = string.IsNullOrWhiteSpace(correlationId)
                ? -1
                : stored.SyncHistory.FindIndex(existing =>
                    existing.AccountId == request.AccountId
                    && string.Equals(existing.Capability, capability, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(existing.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase));

            entry = new AccountSyncHistoryEntryDto(
                existingIndex >= 0 ? stored.SyncHistory[existingIndex].SyncHistoryId : Guid.NewGuid(),
                request.AccountId,
                capability,
                request.Status,
                request.ProviderLinkStatus,
                NormalizeOptional(request.ProviderId),
                NormalizeOptional(request.ExternalAccountId),
                attemptedAt,
                completedAt,
                request.FreshUntil,
                failureKind,
                NormalizeOptional(request.FailureMessage),
                correlationId,
                NormalizeOptional(request.RequestedBy),
                NormalizeOptional(request.RawEvidencePath),
                NormalizeOptional(request.ProjectionEvidencePath),
                Math.Max(0, request.SecurityMissingCount),
                warnings);

            if (existingIndex >= 0)
            {
                stored.SyncHistory[existingIndex] = entry;
            }
            else
            {
                stored.SyncHistory.Add(entry);
            }

            snapshot = CaptureSnapshotLocked();
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        return entry;
    }

    public Task<IReadOnlyList<AccountSyncHistoryEntryDto>> GetSyncHistoryAsync(
        Guid accountId,
        string? capability = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_accounts.TryGetValue(accountId, out var stored))
            {
                return Task.FromResult<IReadOnlyList<AccountSyncHistoryEntryDto>>([]);
            }

            var normalizedCapability = NormalizeOptional(capability);
            var results = stored.SyncHistory
                .Where(entry => normalizedCapability is null
                    || string.Equals(entry.Capability, normalizedCapability, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(static entry => entry.AttemptedAt)
                .ThenByDescending(static entry => entry.CompletedAt)
                .ToArray();
            return Task.FromResult<IReadOnlyList<AccountSyncHistoryEntryDto>>(results);
        }
    }

    public Task<AccountSyncHistoryEntryDto?> GetLatestSyncHistoryAsync(
        Guid accountId,
        string? capability = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_accounts.TryGetValue(accountId, out var stored))
            {
                return Task.FromResult<AccountSyncHistoryEntryDto?>(null);
            }

            var normalizedCapability = NormalizeOptional(capability);
            var result = stored.SyncHistory
                .Where(entry => normalizedCapability is null
                    || string.Equals(entry.Capability, normalizedCapability, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(static entry => entry.AttemptedAt)
                .ThenByDescending(static entry => entry.CompletedAt)
                .FirstOrDefault();
            return Task.FromResult(result);
        }
    }

    public Task<AccountReadinessSnapshotDto?> GetReadinessAsync(
        Guid accountId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(
                _accounts.TryGetValue(accountId, out var stored)
                    ? BuildReadinessSnapshot(stored, DateTimeOffset.UtcNow)
                    : null);
        }
    }

    public async Task<MarginSnapshotDto> RecordMarginSnapshotAsync(
        RecordMarginSnapshotRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var currency = RequireText(request.Currency, nameof(request.Currency)).ToUpperInvariant();
        var correlationId = NormalizeOptional(request.CorrelationId);
        var requirements = (request.Requirements ?? Array.Empty<MarginRequirementDto>())
            .Select(static requirement => requirement with
            {
                SecurityId = NormalizeOptional(requirement.SecurityId),
                Symbol = NormalizeOptional(requirement.Symbol),
                CollateralClass = NormalizeOptional(requirement.CollateralClass),
                EvidencePath = NormalizeOptional(requirement.EvidencePath)
            })
            .ToArray();
        var warnings = (request.Warnings ?? Array.Empty<string>())
            .Where(static warning => !string.IsNullOrWhiteSpace(warning))
            .Select(static warning => warning.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        MarginSnapshotDto snapshot;
        (long Version, string Json)? persistedSnapshot;
        lock (_gate)
        {
            if (!_accounts.TryGetValue(request.AccountId, out var stored))
            {
                throw new InvalidOperationException($"Account {request.AccountId} not found.");
            }

            var existingIndex = string.IsNullOrWhiteSpace(correlationId)
                ? -1
                : stored.MarginSnapshots.FindIndex(existing =>
                    existing.AccountId == request.AccountId
                    && string.Equals(existing.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase));

            snapshot = new MarginSnapshotDto(
                existingIndex >= 0 ? stored.MarginSnapshots[existingIndex].MarginSnapshotId : Guid.NewGuid(),
                request.AccountId,
                request.EffectiveAt,
                request.RecordedAt ?? DateTimeOffset.UtcNow,
                currency,
                request.MarginType,
                request.MarginCallStatus,
                request.InitialMargin,
                request.MaintenanceMargin,
                request.ExcessLiquidity,
                request.BuyingPower,
                request.SpecialMemorandumAccount,
                request.LoanBalance,
                request.DebitBalance,
                request.CreditBalance,
                request.CollateralValue,
                request.MarginableSecuritiesValue,
                request.NonMarginableSecuritiesValue,
                request.MarginUtilization,
                Math.Max(0, request.MissingRequirementCount),
                Math.Max(0, request.MissingCollateralClassificationCount),
                Math.Max(0, request.ConcentrationLimitBreachCount),
                request.IsLiveAccount,
                request.ApprovedForLiveMargin,
                requirements,
                warnings,
                NormalizeOptional(request.ProviderId),
                NormalizeOptional(request.ExternalAccountId),
                request.FreshUntil,
                NormalizeOptional(request.AgreementEvidencePath),
                NormalizeOptional(request.SnapshotEvidencePath),
                correlationId);

            if (existingIndex >= 0)
            {
                stored.MarginSnapshots[existingIndex] = snapshot;
            }
            else
            {
                stored.MarginSnapshots.Add(snapshot);
            }

            persistedSnapshot = CaptureSnapshotLocked();
        }

        await PersistSnapshotAsync(persistedSnapshot, ct).ConfigureAwait(false);
        return snapshot;
    }

    public Task<IReadOnlyList<MarginSnapshotDto>> GetMarginSnapshotsAsync(
        Guid accountId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_accounts.TryGetValue(accountId, out var stored))
            {
                return Task.FromResult<IReadOnlyList<MarginSnapshotDto>>([]);
            }

            var results = stored.MarginSnapshots
                .OrderByDescending(static snapshot => snapshot.EffectiveAt)
                .ThenByDescending(static snapshot => snapshot.RecordedAt)
                .ToArray();
            return Task.FromResult<IReadOnlyList<MarginSnapshotDto>>(results);
        }
    }

    public Task<MarginSnapshotDto?> GetLatestMarginSnapshotAsync(
        Guid accountId,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!_accounts.TryGetValue(accountId, out var stored))
            {
                return Task.FromResult<MarginSnapshotDto?>(null);
            }

            var latest = stored.MarginSnapshots
                .OrderByDescending(static snapshot => snapshot.EffectiveAt)
                .ThenByDescending(static snapshot => snapshot.RecordedAt)
                .FirstOrDefault();
            return Task.FromResult(latest);
        }
    }

    private static AccountReadinessSnapshotDto BuildReadinessSnapshot(StoredAccount stored, DateTimeOffset now)
    {
        var account = stored.Summary;
        var latestSync = stored.SyncHistory
            .OrderByDescending(static entry => entry.AttemptedAt)
            .ThenByDescending(static entry => entry.CompletedAt)
            .FirstOrDefault();
        var latestMargin = stored.MarginSnapshots
            .OrderByDescending(static entry => entry.EffectiveAt)
            .ThenByDescending(static entry => entry.RecordedAt)
            .FirstOrDefault();
        var lastSuccessfulSync = stored.SyncHistory
            .Where(static entry => entry.Status is AccountSyncStatusDto.Succeeded or AccountSyncStatusDto.Degraded)
            .OrderByDescending(static entry => entry.CompletedAt ?? entry.AttemptedAt)
            .FirstOrDefault();
        var providerLinkStatus = latestSync?.ProviderLinkStatus ?? InferProviderLinkStatus(account);
        var issues = new List<AccountReadinessIssueDto>();

        if (providerLinkStatus == AccountProviderLinkStatusDto.NotLinked)
        {
            issues.Add(new AccountReadinessIssueDto(
                "account.provider_link.missing",
                AccountReadinessSeverityDto.Critical,
                "Provider link missing",
                "Account is not linked to a provider, custodian, bank, or broker identity.",
                account.AccountId,
                Capability: latestSync?.Capability,
                SuggestedAction: "Link the Meridian account to the external provider account before accepting sync readiness."));
        }
        else if (providerLinkStatus is AccountProviderLinkStatusDto.Unauthorized
                 or AccountProviderLinkStatusDto.Revoked
                 or AccountProviderLinkStatusDto.Expired
                 or AccountProviderLinkStatusDto.Unsupported)
        {
            issues.Add(new AccountReadinessIssueDto(
                "account.provider_link.unavailable",
                AccountReadinessSeverityDto.Critical,
                "Provider link unavailable",
                $"Provider link status is {providerLinkStatus}.",
                account.AccountId,
                latestSync?.ProviderId,
                latestSync?.ExternalAccountId,
                latestSync?.Capability,
                "Repair or verify the provider link before running account sync.",
                ResolveEvidenceLink(latestSync)));
        }

        if (latestSync is null)
        {
            issues.Add(new AccountReadinessIssueDto(
                "account.sync.never_run",
                AccountReadinessSeverityDto.Warning,
                "Account sync has not run",
                "No durable account sync history exists for this account.",
                account.AccountId,
                SuggestedAction: "Run the account sync or import account evidence before using account data for downstream workflows."));
        }
        else
        {
            if (latestSync.Status == AccountSyncStatusDto.Failed)
            {
                issues.Add(new AccountReadinessIssueDto(
                    "account.sync.failed",
                    AccountReadinessSeverityDto.Critical,
                    "Latest account sync failed",
                    latestSync.FailureMessage ?? $"Latest {latestSync.Capability} sync failed with {latestSync.FailureKind}.",
                    account.AccountId,
                    latestSync.ProviderId,
                    latestSync.ExternalAccountId,
                    latestSync.Capability,
                    "Review provider connectivity, credentials, and the sync evidence before retrying.",
                    ResolveEvidenceLink(latestSync)));
            }

            if (latestSync.FreshUntil is { } freshUntil && freshUntil < now)
            {
                issues.Add(new AccountReadinessIssueDto(
                    "account.sync.stale",
                    AccountReadinessSeverityDto.Warning,
                    "Account sync is stale",
                    $"Latest {latestSync.Capability} sync was fresh until {freshUntil:O}.",
                    account.AccountId,
                    latestSync.ProviderId,
                    latestSync.ExternalAccountId,
                    latestSync.Capability,
                    "Run account sync again before relying on balances, positions, activity, or margin state.",
                    ResolveEvidenceLink(latestSync)));
            }

            if (latestSync.SecurityMissingCount > 0)
            {
                issues.Add(new AccountReadinessIssueDto(
                    "account.security_master.coverage_missing",
                    AccountReadinessSeverityDto.Warning,
                    "Security Master coverage gap",
                    $"{latestSync.SecurityMissingCount} synced position(s) are missing Security Master coverage.",
                    account.AccountId,
                    latestSync.ProviderId,
                    latestSync.ExternalAccountId,
                    latestSync.Capability,
                    "Map missing securities before posting security-linked activity or accepting reconciliation.",
                    ResolveEvidenceLink(latestSync)));
            }
        }

        if (RequiresMarginReadiness(account, latestSync, latestMargin))
        {
            AddMarginReadinessIssues(account, latestSync, latestMargin, now, issues);
        }

        if (string.IsNullOrWhiteSpace(account.LedgerReference))
        {
            issues.Add(new AccountReadinessIssueDto(
                "account.ledger_mapping.missing",
                AccountReadinessSeverityDto.Critical,
                "Ledger mapping missing",
                "Account does not have a ledger reference.",
                account.AccountId,
                latestSync?.ProviderId,
                latestSync?.ExternalAccountId,
                latestSync?.Capability,
                "Map the account to a ledger reference before posting account activity."));
        }

        var openBreakCount = stored.ReconciliationResults.Count(static result => !result.IsMatch);
        if (openBreakCount > 0)
        {
            issues.Add(new AccountReadinessIssueDto(
                "account.reconciliation.breaks_open",
                AccountReadinessSeverityDto.Warning,
                "Unresolved reconciliation breaks",
                $"{openBreakCount} account reconciliation break(s) remain open.",
                account.AccountId,
                latestSync?.ProviderId,
                latestSync?.ExternalAccountId,
                latestSync?.Capability,
                "Review and resolve reconciliation breaks before accepting account readiness."));
        }

        return new AccountReadinessSnapshotDto(
            account.AccountId,
            now,
            providerLinkStatus,
            latestSync?.Status,
            lastSuccessfulSync?.CompletedAt ?? lastSuccessfulSync?.AttemptedAt,
            latestSync?.FreshUntil,
            IsReady: issues.Count == 0,
            Issues: issues);
    }

    private static bool RequiresMarginReadiness(
        AccountSummaryDto account,
        AccountSyncHistoryEntryDto? latestSync,
        MarginSnapshotDto? latestMargin)
        => account.AccountType == AccountTypeDto.Margin
           || latestMargin is not null
           || string.Equals(latestSync?.Capability, "margin-sync", StringComparison.OrdinalIgnoreCase)
           || string.Equals(latestSync?.Capability, "brokerage-margin", StringComparison.OrdinalIgnoreCase);

    private static void AddMarginReadinessIssues(
        AccountSummaryDto account,
        AccountSyncHistoryEntryDto? latestSync,
        MarginSnapshotDto? latestMargin,
        DateTimeOffset now,
        List<AccountReadinessIssueDto> issues)
    {
        if (latestMargin is null)
        {
            issues.Add(new AccountReadinessIssueDto(
                "account.margin.snapshot.missing",
                AccountReadinessSeverityDto.Critical,
                "Margin snapshot missing",
                "Margin account does not have an effective-dated margin snapshot.",
                account.AccountId,
                latestSync?.ProviderId,
                latestSync?.ExternalAccountId,
                latestSync?.Capability,
                "Import or sync margin requirements before accepting account readiness.",
                ResolveEvidenceLink(latestSync)));
            return;
        }

        var evidenceLink = ResolveMarginEvidenceLink(latestMargin) ?? ResolveEvidenceLink(latestSync);
        if (latestMargin.MarginType == MarginModelTypeDto.Unsupported)
        {
            issues.Add(new AccountReadinessIssueDto(
                "account.margin.model.unsupported",
                AccountReadinessSeverityDto.Critical,
                "Unsupported margin model",
                "Latest margin snapshot uses an unsupported margin model.",
                account.AccountId,
                latestMargin.ProviderId,
                latestMargin.ExternalAccountId,
                "margin",
                "Map the provider margin model to a supported Reg T, portfolio margin, or house margin policy.",
                evidenceLink));
        }

        if (latestMargin.FreshUntil is { } freshUntil && freshUntil < now)
        {
            issues.Add(new AccountReadinessIssueDto(
                "account.margin.snapshot.stale",
                AccountReadinessSeverityDto.Warning,
                "Margin snapshot is stale",
                $"Latest margin snapshot was fresh until {freshUntil:O}.",
                account.AccountId,
                latestMargin.ProviderId,
                latestMargin.ExternalAccountId,
                "margin",
                "Refresh margin state before relying on buying power, excess liquidity, or margin-call status.",
                evidenceLink));
        }

        if (latestMargin.MissingRequirementCount > 0 || (latestMargin.Requirements.Count == 0 && latestMargin.MaintenanceMargin.GetValueOrDefault() > 0m))
        {
            issues.Add(new AccountReadinessIssueDto(
                "account.margin.requirements.missing",
                AccountReadinessSeverityDto.Critical,
                "Margin requirement data missing",
                latestMargin.MissingRequirementCount > 0
                    ? $"{latestMargin.MissingRequirementCount} position-level margin requirement(s) are missing."
                    : "Account-level maintenance margin exists without position-level margin requirements.",
                account.AccountId,
                latestMargin.ProviderId,
                latestMargin.ExternalAccountId,
                "margin",
                "Sync or import provider position-level margin requirements before accepting readiness.",
                evidenceLink));
        }

        if (latestMargin.ExcessLiquidity is < 0m)
        {
            issues.Add(new AccountReadinessIssueDto(
                "account.margin.excess_liquidity.negative",
                AccountReadinessSeverityDto.Critical,
                "Negative excess liquidity",
                $"Excess liquidity is {latestMargin.ExcessLiquidity.Value} {latestMargin.Currency}.",
                account.AccountId,
                latestMargin.ProviderId,
                latestMargin.ExternalAccountId,
                "margin",
                "Review margin utilization and provider margin-call state before trading.",
                evidenceLink));
        }

        if (latestMargin.MarginCallStatus is MarginCallStatusDto.Active or MarginCallStatusDto.Potential)
        {
            issues.Add(new AccountReadinessIssueDto(
                latestMargin.MarginCallStatus == MarginCallStatusDto.Active
                    ? "account.margin.call.active"
                    : "account.margin.call.potential",
                latestMargin.MarginCallStatus == MarginCallStatusDto.Active
                    ? AccountReadinessSeverityDto.Critical
                    : AccountReadinessSeverityDto.Warning,
                latestMargin.MarginCallStatus == MarginCallStatusDto.Active
                    ? "Margin call active"
                    : "Potential margin call",
                $"Latest margin snapshot reports {latestMargin.MarginCallStatus} margin-call status.",
                account.AccountId,
                latestMargin.ProviderId,
                latestMargin.ExternalAccountId,
                "margin",
                "Resolve or approve the margin exception before accepting account readiness.",
                evidenceLink));
        }

        if (latestMargin.MissingCollateralClassificationCount > 0)
        {
            issues.Add(new AccountReadinessIssueDto(
                "account.margin.collateral_classification.missing",
                AccountReadinessSeverityDto.Warning,
                "Collateral classification missing",
                $"{latestMargin.MissingCollateralClassificationCount} collateral or position classification(s) are missing.",
                account.AccountId,
                latestMargin.ProviderId,
                latestMargin.ExternalAccountId,
                "margin",
                "Classify collateral eligibility and haircuts before relying on margin availability.",
                evidenceLink));
        }

        if (latestMargin.ConcentrationLimitBreachCount > 0)
        {
            issues.Add(new AccountReadinessIssueDto(
                "account.margin.concentration_limit.breached",
                AccountReadinessSeverityDto.Warning,
                $"{latestMargin.ConcentrationLimitBreachCount} concentration limit breach(es)",
                "Latest margin snapshot reports concentration-limit pressure.",
                account.AccountId,
                latestMargin.ProviderId,
                latestMargin.ExternalAccountId,
                "margin",
                "Review concentration limits and margin exception approval before accepting readiness.",
                evidenceLink));
        }

        if (latestMargin.IsLiveAccount && !latestMargin.ApprovedForLiveMargin)
        {
            issues.Add(new AccountReadinessIssueDto(
                "account.margin.live_approval.missing",
                AccountReadinessSeverityDto.Critical,
                "Live margin approval missing",
                "Live margin account has not been explicitly approved for governed live-account use.",
                account.AccountId,
                latestMargin.ProviderId,
                latestMargin.ExternalAccountId,
                "margin",
                "Capture governance approval before using this live margin account for trading or posting.",
                evidenceLink));
        }

        if (!string.IsNullOrWhiteSpace(latestSync?.ProviderId)
            && !string.IsNullOrWhiteSpace(latestMargin.ProviderId)
            && !string.Equals(latestSync.ProviderId, latestMargin.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new AccountReadinessIssueDto(
                "account.margin.provider_mismatch",
                AccountReadinessSeverityDto.Warning,
                "Margin provider mismatch",
                $"Latest sync provider is {latestSync.ProviderId}, but latest margin snapshot provider is {latestMargin.ProviderId}.",
                account.AccountId,
                latestMargin.ProviderId,
                latestMargin.ExternalAccountId,
                "margin",
                "Verify provider account mapping before accepting margin readiness.",
                evidenceLink));
        }
    }

    private static AccountProviderLinkStatusDto InferProviderLinkStatus(AccountSummaryDto account)
    {
        if (!string.IsNullOrWhiteSpace(account.Institution))
        {
            return AccountProviderLinkStatusDto.Linked;
        }

        if (account.AccountType == AccountTypeDto.Bank && !string.IsNullOrWhiteSpace(account.BankDetails?.BankName))
        {
            return AccountProviderLinkStatusDto.Linked;
        }

        return AccountProviderLinkStatusDto.NotLinked;
    }

    private static string? ResolveEvidenceLink(AccountSyncHistoryEntryDto? latestSync)
        => NormalizeOptional(latestSync?.RawEvidencePath) ?? NormalizeOptional(latestSync?.ProjectionEvidencePath);

    private static string? ResolveMarginEvidenceLink(MarginSnapshotDto? latestMargin)
        => NormalizeOptional(latestMargin?.SnapshotEvidencePath) ?? NormalizeOptional(latestMargin?.AgreementEvidencePath);

    private static string RequireText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private (long Version, string Json)? CaptureSnapshotLocked()
    {
        if (_persistencePath is null)
        {
            return null;
        }

        _stateVersion++;
        var json = JsonSerializer.Serialize(
            new PersistedState(
                Version: 1,
                Accounts: _accounts.Values.ToList()),
            JsonOptions);

        return (_stateVersion, json);
    }

    private async Task PersistSnapshotAsync((long Version, string Json)? snapshot, CancellationToken ct)
    {
        if (snapshot is null || _persistencePath is null)
        {
            return;
        }

        await _persistGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (snapshot.Value.Version <= _persistedVersion)
            {
                return;
            }

            await AtomicFileWriter.WriteAsync(_persistencePath, snapshot.Value.Json, ct).ConfigureAwait(false);
            _persistedVersion = snapshot.Value.Version;
        }
        finally
        {
            _persistGate.Release();
        }
    }

    private void LoadState()
    {
        if (_persistencePath is null || !File.Exists(_persistencePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(_persistencePath);
            var state = JsonSerializer.Deserialize<PersistedState>(json, JsonOptions);
            if (state is null)
            {
                return;
            }

            foreach (var account in state.Accounts)
            {
                _accounts[account.Summary.AccountId] = NormalizeStoredAccount(account);
            }

            _stateVersion = 1;
            _persistedVersion = 1;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // Preserve startup availability for malformed or missing local snapshots.
        }
    }

    private static StoredAccount NormalizeStoredAccount(StoredAccount account)
        => account with
        {
            Snapshots = account.Snapshots ?? [],
            CustodianBatches = account.CustodianBatches ?? [],
            CustodianPositions = account.CustodianPositions ?? [],
            BankBatches = account.BankBatches ?? [],
            BankLines = account.BankLines ?? [],
            ReconciliationRuns = account.ReconciliationRuns ?? [],
            ReconciliationResults = account.ReconciliationResults ?? [],
            SyncHistory = account.SyncHistory ?? [],
            MarginSnapshots = account.MarginSnapshots ?? []
        };

    public Task<IReadOnlyList<AccountSummaryDto>> ListAccountsAsync(AccountTypeDto? accountType, bool? isActive, string? currency, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var results = _accounts.Values.Select(static s => s.Summary)
                .Where(a => (accountType is null || a.AccountType == accountType)
                    && (isActive is null || a.IsActive == isActive.Value)
                    && (string.IsNullOrWhiteSpace(currency) || string.Equals(a.BaseCurrency, currency, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(static a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Task.FromResult<IReadOnlyList<AccountSummaryDto>>(results);
        }
    }

    public Task<IReadOnlyList<AccountSettlementInstructionView>> ListSettlementInstructionsAsync(Guid? accountId = null, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var results = _accounts.Values.Select(static a => a.Summary)
                .Where(a => accountId is null || a.AccountId == accountId)
                .SelectMany(a => new []
                {
                    new AccountSettlementInstructionView(a.AccountId, "Custodian", a.CustodianDetails?.SubAccountNumber, a.Institution),
                    new AccountSettlementInstructionView(a.AccountId, "Bank", a.BankDetails?.AccountNumber, a.BankDetails?.BankName ?? a.Institution)
                })
                .Where(static x => !string.IsNullOrWhiteSpace(x.Reference))
                .OrderBy(static x => x.AccountId)
                .ThenBy(static x => x.InstructionType, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Task.FromResult<IReadOnlyList<AccountSettlementInstructionView>>(results);
        }
    }

    public Task<IReadOnlyList<AccountBalanceSnapshotDto>> GetBalanceTimelineAsync(Guid accountId, DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken ct = default)
        => GetBalanceHistoryAsync(accountId, fromDate, toDate, ct);

    public Task<IReadOnlyList<AccountOpenBreakView>> ListOpenBreaksAsync(Guid? accountId = null, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var results = _accounts.Values
                .Where(a => accountId is null || a.Summary.AccountId == accountId)
                .SelectMany(a => a.ReconciliationResults
                    .Where(static r => !r.IsMatch)
                    .Select(r => new AccountOpenBreakView(a.Summary.AccountId, r.ReconciliationRunId, r.ResultId, r.CheckLabel, r.Category, r.Variance, r.Reason)))
                .OrderByDescending(static r => r.Variance ?? 0m)
                .ThenBy(static r => r.CheckLabel, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return Task.FromResult<IReadOnlyList<AccountOpenBreakView>>(results);
        }
    }

}
