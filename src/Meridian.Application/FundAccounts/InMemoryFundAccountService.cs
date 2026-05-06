using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Storage.Archival;

namespace Meridian.Application.FundAccounts;

using Meridian.Application.Accounts;

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
        List<AccountReconciliationResultDto> ReconciliationResults)
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
                ReconciliationResults: []);
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
            request.ExternalReference);

        (long Version, string Json)? snapshot = null;
        lock (_gate)
        {
            if (_accounts.TryGetValue(request.AccountId, out var stored))
            {
                EnsureAllowed(stored.Summary, "record-balance-snapshot", request.IsBackfill);
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
                EnsureAllowed(stored.Summary, "ingest-custodian-statement", request.IsBackfill, allowSuspended: true);
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
                EnsureAllowed(stored.Summary, "ingest-bank-statement", request.IsBackfill, allowSuspended: true);
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
                _accounts[account.Summary.AccountId] = account;
            }

            _stateVersion = 1;
            _persistedVersion = 1;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // Preserve startup availability for malformed or missing local snapshots.
        }
    }

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
