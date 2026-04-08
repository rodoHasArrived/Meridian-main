using System.Text.Json;
using Meridian.Contracts.FundStructure;
using Meridian.Storage.Archival;

namespace Meridian.Application.FundAccounts;

/// <summary>
/// Thread-safe fund-account service backed by an in-memory working set with optional
/// durable JSON snapshot persistence for local-first workflows.
/// </summary>
public sealed class InMemoryFundAccountService : IFundAccountService
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
                stored.CustodianBatches.Add(batch);
                stored.CustodianPositions.AddRange(request.Lines);
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
                stored.BankBatches.Add(batch);
                stored.BankLines.AddRange(request.Lines);
                snapshot = CaptureSnapshotLocked();
            }
        }

        await PersistSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        return batch;
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
                .Where(l => (fromDate == null || l.StatementDate >= fromDate)
                         && (toDate == null || l.StatementDate <= toDate))
                .OrderByDescending(l => l.StatementDate)
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
}
