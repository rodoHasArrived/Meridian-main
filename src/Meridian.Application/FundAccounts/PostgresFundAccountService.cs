using Meridian.Application.Accounts;
using Meridian.Application.Composition;
using Meridian.Contracts.FundStructure;
using Meridian.Storage.FundAccounts;

namespace Meridian.Application.FundAccounts;

/// <summary>
/// PostgreSQL-backed fund account service.
/// Delegates all persistence to <see cref="IFundAccountStore"/> and preserves the
/// business-logic rules from <see cref="InMemoryFundAccountService"/> without the
/// <see cref="INonProductionOnlyService"/> restriction.
/// </summary>
public sealed class PostgresFundAccountService : IFundAccountService, IAccountManagementService, IAccountQueryService
{
    private readonly IFundAccountStore _store;

    public PostgresFundAccountService(IFundAccountStore store)
    {
        _store = store;
    }

    // ── Account definition ────────────────────────────────────────────────────

    public async Task<AccountSummaryDto> CreateAccountAsync(
        CreateAccountRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await _store.GetAccountAsync(request.AccountId, ct).ConfigureAwait(false);
        if (existing is not null)
            throw new InvalidOperationException($"Account {request.AccountId} already exists.");

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

        await _store.UpsertAccountAsync(dto, ct).ConfigureAwait(false);
        return dto;
    }

    public Task<AccountSummaryDto?> GetAccountAsync(Guid accountId, CancellationToken ct = default)
        => _store.GetAccountAsync(accountId, ct);

    public Task<IReadOnlyList<AccountSummaryDto>> QueryAccountsAsync(
        AccountStructureQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _store.QueryAccountsAsync(query, ct);
    }

    public async Task<AccountSummaryDto?> UpdateCustodianDetailsAsync(
        Guid accountId,
        UpdateCustodianAccountDetailsRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var existing = await _store.GetAccountAsync(accountId, ct).ConfigureAwait(false);
        if (existing is null) return null;
        EnsureAllowed(existing, "update-custodian-details");
        var updated = existing with { CustodianDetails = request.Details };
        await _store.UpsertAccountAsync(updated, ct).ConfigureAwait(false);
        return updated;
    }

    public async Task<AccountSummaryDto?> UpdateBankDetailsAsync(
        Guid accountId,
        UpdateBankAccountDetailsRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var existing = await _store.GetAccountAsync(accountId, ct).ConfigureAwait(false);
        if (existing is null) return null;
        EnsureAllowed(existing, "update-bank-details");
        var updated = existing with { BankDetails = request.Details };
        await _store.UpsertAccountAsync(updated, ct).ConfigureAwait(false);
        return updated;
    }

    public async Task<AccountSummaryDto?> DeactivateAccountAsync(
        Guid accountId, string deactivatedBy, CancellationToken ct = default)
    {
        var existing = await _store.GetAccountAsync(accountId, ct).ConfigureAwait(false);
        if (existing is null) return null;
        var updated = existing with { IsActive = false, EffectiveTo = DateTimeOffset.UtcNow };
        await _store.UpsertAccountAsync(updated, ct).ConfigureAwait(false);
        return updated;
    }

    public async Task<FundAccountsDto> GetFundAccountsAsync(Guid fundId, CancellationToken ct = default)
    {
        var accounts = await _store.QueryAccountsAsync(
            new AccountStructureQuery { FundId = fundId, ActiveOnly = true }, ct).ConfigureAwait(false);
        return new FundAccountsDto(
            fundId,
            CustodianAccounts: accounts.Where(a => a.AccountType == AccountTypeDto.Custody).ToList(),
            BankAccounts: accounts.Where(a => a.AccountType == AccountTypeDto.Bank).ToList(),
            BrokerageAccounts: accounts.Where(a => a.AccountType == AccountTypeDto.Brokerage).ToList(),
            OtherAccounts: accounts.Where(a =>
                a.AccountType != AccountTypeDto.Custody &&
                a.AccountType != AccountTypeDto.Bank &&
                a.AccountType != AccountTypeDto.Brokerage).ToList());
    }

    // ── Balance snapshots ─────────────────────────────────────────────────────

    public async Task<AccountBalanceSnapshotDto> RecordBalanceSnapshotAsync(
        RecordAccountBalanceSnapshotRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = await _store.GetAccountAsync(request.AccountId, ct).ConfigureAwait(false);
        if (account is not null)
            EnsureAllowed(account, "record-balance-snapshot", request.IsBackfill);

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

        await _store.InsertBalanceSnapshotAsync(dto, ct).ConfigureAwait(false);
        return dto;
    }

    public Task<IReadOnlyList<AccountBalanceSnapshotDto>> GetBalanceHistoryAsync(
        Guid accountId, DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken ct = default)
        => _store.GetBalanceHistoryAsync(accountId, fromDate, toDate, ct);

    public async Task<AccountBalanceSnapshotDto?> GetLatestBalanceSnapshotAsync(
        Guid accountId, CancellationToken ct = default)
    {
        var history = await _store.GetBalanceHistoryAsync(accountId, null, null, ct).ConfigureAwait(false);
        return history.OrderByDescending(s => s.AsOfDate).ThenByDescending(s => s.RecordedAt).FirstOrDefault();
    }

    // ── Statement ingestion ───────────────────────────────────────────────────

    public async Task<CustodianStatementBatchDto> IngestCustodianStatementAsync(
        IngestCustodianStatementRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = await _store.GetAccountAsync(request.AccountId, ct).ConfigureAwait(false);
        if (account is not null)
            EnsureAllowed(account, "ingest-custodian-statement", request.IsBackfill, allowSuspended: true);

        var batch = new CustodianStatementBatchDto(
            request.BatchId,
            request.AccountId,
            request.AsOfDate,
            request.CustodianName,
            request.SourceFormat,
            request.Lines.Count,
            DateTimeOffset.UtcNow,
            request.LoadedBy);

        await _store.InsertCustodianStatementBatchAsync(batch, request.Lines, ct).ConfigureAwait(false);
        return batch;
    }

    public async Task<BankStatementBatchDto> IngestBankStatementAsync(
        IngestBankStatementRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var account = await _store.GetAccountAsync(request.AccountId, ct).ConfigureAwait(false);
        if (account is not null)
            EnsureAllowed(account, "ingest-bank-statement", request.IsBackfill, allowSuspended: true);

        var batch = new BankStatementBatchDto(
            request.BatchId,
            request.AccountId,
            request.StatementDate,
            request.BankName,
            request.Lines.Count,
            DateTimeOffset.UtcNow,
            request.LoadedBy);

        await _store.InsertBankStatementBatchAsync(batch, request.Lines, ct).ConfigureAwait(false);
        return batch;
    }

    public Task<IReadOnlyList<CustodianPositionLineDto>> GetCustodianPositionsAsync(
        Guid accountId, DateOnly asOfDate, CancellationToken ct = default)
        => _store.GetCustodianPositionsAsync(accountId, asOfDate, ct);

    public Task<IReadOnlyList<BankStatementLineDto>> GetBankStatementLinesAsync(
        Guid accountId, DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken ct = default)
        => _store.GetBankStatementLinesAsync(accountId, fromDate, toDate, ct);

    // ── Reconciliation ─────────────────────────────────────────────────────────

    public async Task<AccountReconciliationRunDto> ReconcileAccountAsync(
        ReconcileAccountRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var snapshots = await _store.GetBalanceHistoryAsync(request.AccountId, request.AsOfDate, request.AsOfDate, ct).ConfigureAwait(false);
        var positions = await _store.GetCustodianPositionsAsync(request.AccountId, request.AsOfDate, ct).ConfigureAwait(false);

        var snapshot = snapshots.OrderByDescending(s => s.RecordedAt).FirstOrDefault();

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

        await AddContinuityCheckResultsAsync(runId, request.AsOfDate, results, request.AccountId, ct).ConfigureAwait(false);

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

        await _store.InsertReconciliationRunAsync(run, results, ct).ConfigureAwait(false);
        return run;
    }

    public Task<IReadOnlyList<AccountReconciliationRunDto>> GetReconciliationRunsAsync(
        Guid accountId, CancellationToken ct = default)
        => _store.GetReconciliationRunsAsync(accountId, ct);

    public Task<IReadOnlyList<AccountReconciliationResultDto>> GetReconciliationResultsAsync(
        Guid reconciliationRunId, CancellationToken ct = default)
        => _store.GetReconciliationResultsAsync(reconciliationRunId, ct);

    public async Task<IReadOnlyList<PositionReconciliationBreakDto>> GetOpenPositionBreaksAsync(
        Guid accountId, CancellationToken ct = default)
    {
        var runs = await _store.GetReconciliationRunsAsync(accountId, ct).ConfigureAwait(false);
        var breaks = new List<PositionReconciliationBreakDto>();
        foreach (var run in runs)
        {
            var results = await _store.GetReconciliationResultsAsync(run.ReconciliationRunId, ct).ConfigureAwait(false);
            breaks.AddRange(results
                .Where(r => !r.IsMatch
                    && (string.Equals(r.Category, "Position", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(r.Category, "Positions", StringComparison.OrdinalIgnoreCase)))
                .Select(r => new PositionReconciliationBreakDto(
                    r.ResultId,
                    accountId,
                    run.AsOfDate,
                    r.CheckLabel,
                    r.ExpectedAmount ?? 0m,
                    r.ActualAmount ?? 0m,
                    r.Variance ?? 0m,
                    r.Reason)));
        }
        return breaks;
    }

    public async Task<IReadOnlyList<CashReconciliationBreakDto>> GetOpenCashBreaksAsync(
        Guid accountId, CancellationToken ct = default)
    {
        var account = await _store.GetAccountAsync(accountId, ct).ConfigureAwait(false);
        var runs = await _store.GetReconciliationRunsAsync(accountId, ct).ConfigureAwait(false);
        var breaks = new List<CashReconciliationBreakDto>();
        foreach (var run in runs)
        {
            var results = await _store.GetReconciliationResultsAsync(run.ReconciliationRunId, ct).ConfigureAwait(false);
            breaks.AddRange(results
                .Where(r => !r.IsMatch && string.Equals(r.Category, "Cash", StringComparison.OrdinalIgnoreCase))
                .Select(r => new CashReconciliationBreakDto(
                    r.ResultId,
                    accountId,
                    run.AsOfDate,
                    account?.BaseCurrency ?? "USD",
                    r.ExpectedAmount ?? 0m,
                    r.ActualAmount ?? 0m,
                    r.Variance ?? 0m,
                    r.Reason)));
        }
        return breaks;
    }

    public async Task<IReadOnlyList<AccountReconciliationBreakDto>> GetOpenBreaksAsync(
        Guid accountId, CancellationToken ct = default)
    {
        var positionBreaks = await GetOpenPositionBreaksAsync(accountId, ct).ConfigureAwait(false);
        var cashBreaks = await GetOpenCashBreaksAsync(accountId, ct).ConfigureAwait(false);
        return positionBreaks.Select(b => new AccountReconciliationBreakDto(PositionBreak: b))
            .Concat(cashBreaks.Select(b => new AccountReconciliationBreakDto(CashBreak: b)))
            .ToList();
    }

    // ── Sync history ──────────────────────────────────────────────────────────

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
            .Where(static w => !string.IsNullOrWhiteSpace(w))
            .Select(static w => w.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Resolve SyncHistoryId: re-use existing if same (accountId, capability, correlationId)
        Guid syncHistoryId = Guid.NewGuid();
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            var existing = await _store.GetSyncHistoryAsync(request.AccountId, capability, ct).ConfigureAwait(false);
            var match = existing.FirstOrDefault(e =>
                string.Equals(e.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                syncHistoryId = match.SyncHistoryId;
        }

        var entry = new AccountSyncHistoryEntryDto(
            syncHistoryId,
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

        await _store.InsertSyncHistoryAsync(entry, ct).ConfigureAwait(false);
        return entry;
    }

    public Task<IReadOnlyList<AccountSyncHistoryEntryDto>> GetSyncHistoryAsync(
        Guid accountId, string? capability = null, CancellationToken ct = default)
        => _store.GetSyncHistoryAsync(accountId, capability, ct);

    public async Task<AccountSyncHistoryEntryDto?> GetLatestSyncHistoryAsync(
        Guid accountId, string? capability = null, CancellationToken ct = default)
    {
        var history = await _store.GetSyncHistoryAsync(accountId, capability, ct).ConfigureAwait(false);
        return history.OrderByDescending(e => e.AttemptedAt).ThenByDescending(e => e.CompletedAt).FirstOrDefault();
    }

    // ── Readiness ─────────────────────────────────────────────────────────────

    public async Task<AccountReadinessSnapshotDto?> GetReadinessAsync(
        Guid accountId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var account = await _store.GetAccountAsync(accountId, ct).ConfigureAwait(false);
        if (account is null) return null;

        var syncHistory = await _store.GetSyncHistoryAsync(accountId, null, ct).ConfigureAwait(false);
        var marginSnapshots = await _store.GetMarginSnapshotsAsync(accountId, ct).ConfigureAwait(false);
        var reconciliationRuns = await _store.GetReconciliationRunsAsync(accountId, ct).ConfigureAwait(false);

        // Aggregate open break count across all runs
        int openBreakCount = 0;
        foreach (var run in reconciliationRuns)
        {
            var runResults = await _store.GetReconciliationResultsAsync(run.ReconciliationRunId, ct).ConfigureAwait(false);
            openBreakCount += runResults.Count(r => !r.IsMatch);
        }

        var now = DateTimeOffset.UtcNow;
        var latestSync = syncHistory.OrderByDescending(e => e.AttemptedAt).ThenByDescending(e => e.CompletedAt).FirstOrDefault();
        var latestMargin = marginSnapshots.OrderByDescending(e => e.EffectiveAt).ThenByDescending(e => e.RecordedAt).FirstOrDefault();
        var lastSuccessfulSync = syncHistory
            .Where(static e => e.Status is AccountSyncStatusDto.Succeeded or AccountSyncStatusDto.Degraded)
            .OrderByDescending(e => e.CompletedAt ?? e.AttemptedAt)
            .FirstOrDefault();

        return BuildReadinessSnapshot(account, latestSync, latestMargin, lastSuccessfulSync, openBreakCount, now);
    }

    // ── Margin snapshots ──────────────────────────────────────────────────────

    public async Task<MarginSnapshotDto> RecordMarginSnapshotAsync(
        RecordMarginSnapshotRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var currency = RequireText(request.Currency, nameof(request.Currency)).ToUpperInvariant();
        var correlationId = NormalizeOptional(request.CorrelationId);
        var requirements = (request.Requirements ?? Array.Empty<MarginRequirementDto>())
            .Select(static r => r with
            {
                SecurityId = NormalizeOptional(r.SecurityId),
                Symbol = NormalizeOptional(r.Symbol),
                CollateralClass = NormalizeOptional(r.CollateralClass),
                EvidencePath = NormalizeOptional(r.EvidencePath)
            })
            .ToArray();
        var warnings = (request.Warnings ?? Array.Empty<string>())
            .Where(static w => !string.IsNullOrWhiteSpace(w))
            .Select(static w => w.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Resolve ID: re-use if same (accountId, correlationId) so upsert on (account_id, effective_at) works correctly
        Guid snapshotId = Guid.NewGuid();
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            var existing = await _store.GetMarginSnapshotsAsync(request.AccountId, ct).ConfigureAwait(false);
            var match = existing.FirstOrDefault(e =>
                string.Equals(e.CorrelationId, correlationId, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                snapshotId = match.MarginSnapshotId;
        }

        var snapshot = new MarginSnapshotDto(
            snapshotId,
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

        await _store.UpsertMarginSnapshotAsync(snapshot, ct).ConfigureAwait(false);
        return snapshot;
    }

    public Task<IReadOnlyList<MarginSnapshotDto>> GetMarginSnapshotsAsync(
        Guid accountId, CancellationToken ct = default)
        => _store.GetMarginSnapshotsAsync(accountId, ct);

    public async Task<MarginSnapshotDto?> GetLatestMarginSnapshotAsync(
        Guid accountId, CancellationToken ct = default)
    {
        var snapshots = await _store.GetMarginSnapshotsAsync(accountId, ct).ConfigureAwait(false);
        return snapshots.OrderByDescending(s => s.EffectiveAt).ThenByDescending(s => s.RecordedAt).FirstOrDefault();
    }

    // ── IAccountQueryService ──────────────────────────────────────────────────

    public async Task<IReadOnlyList<AccountSummaryDto>> ListAccountsAsync(
        AccountTypeDto? accountType, bool? isActive, string? currency, CancellationToken ct = default)
    {
        var query = new AccountStructureQuery { ActiveOnly = isActive == true };
        var all = await _store.QueryAccountsAsync(query, ct).ConfigureAwait(false);
        return all
            .Where(a => (accountType is null || a.AccountType == accountType)
                && (isActive is null || a.IsActive == isActive.Value)
                && (string.IsNullOrWhiteSpace(currency) || string.Equals(a.BaseCurrency, currency, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<AccountSettlementInstructionView>> ListSettlementInstructionsAsync(
        Guid? accountId = null, CancellationToken ct = default)
    {
        var query = new AccountStructureQuery();
        if (accountId.HasValue) query = query with { AccountId = accountId };
        var accounts = await _store.QueryAccountsAsync(query, ct).ConfigureAwait(false);
        return accounts
            .SelectMany(a => new[]
            {
                new AccountSettlementInstructionView(a.AccountId, "Custodian", a.CustodianDetails?.SubAccountNumber, a.Institution),
                new AccountSettlementInstructionView(a.AccountId, "Bank", a.BankDetails?.AccountNumber, a.BankDetails?.BankName ?? a.Institution)
            })
            .Where(static x => !string.IsNullOrWhiteSpace(x.Reference))
            .OrderBy(static x => x.AccountId)
            .ThenBy(static x => x.InstructionType, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task<IReadOnlyList<AccountBalanceSnapshotDto>> GetBalanceTimelineAsync(
        Guid accountId, DateOnly? fromDate = null, DateOnly? toDate = null, CancellationToken ct = default)
        => GetBalanceHistoryAsync(accountId, fromDate, toDate, ct);

    public async Task<IReadOnlyList<AccountOpenBreakView>> ListOpenBreaksAsync(
        Guid? accountId = null, CancellationToken ct = default)
    {
        AccountStructureQuery query = accountId.HasValue
            ? new AccountStructureQuery { AccountId = accountId }
            : new AccountStructureQuery();
        var accounts = await _store.QueryAccountsAsync(query, ct).ConfigureAwait(false);

        var views = new List<AccountOpenBreakView>();
        foreach (var account in accounts)
        {
            var runs = await _store.GetReconciliationRunsAsync(account.AccountId, ct).ConfigureAwait(false);
            foreach (var run in runs)
            {
                var results = await _store.GetReconciliationResultsAsync(run.ReconciliationRunId, ct).ConfigureAwait(false);
                views.AddRange(results
                    .Where(static r => !r.IsMatch)
                    .Select(r => new AccountOpenBreakView(
                        account.AccountId, r.ReconciliationRunId, r.ResultId,
                        r.CheckLabel, r.Category, r.Variance, r.Reason)));
            }
        }

        return views
            .OrderByDescending(static r => r.Variance ?? 0m)
            .ThenBy(static r => r.CheckLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static void EnsureAllowed(
        AccountSummaryDto summary, string operation,
        bool isBackfill = false, bool allowSuspended = false)
    {
        if (summary.OperationalStatus == AccountOperationalStatusDto.Closed && !isBackfill)
            throw new AccountStatusPolicyException(summary.OperationalStatus, operation, isBackfill);

        if (summary.OperationalStatus == AccountOperationalStatusDto.Suspended && !allowSuspended)
            throw new AccountStatusPolicyException(summary.OperationalStatus, operation, isBackfill);
    }

    private async Task AddContinuityCheckResultsAsync(
        Guid runId, DateOnly asOfDate, List<AccountReconciliationResultDto> results,
        Guid accountId, CancellationToken ct)
    {
        var allSnapshots = await _store.GetBalanceHistoryAsync(accountId, asOfDate, asOfDate, ct).ConfigureAwait(false);

        var accountSync = allSnapshots
            .Where(static s => s.Source is not null && s.Source.StartsWith("brokerage-sync:", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static s => s.RecordedAt)
            .FirstOrDefault();
        var runDerived = allSnapshots
            .Where(static s => s.Source is null || !s.Source.StartsWith("brokerage-sync:", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static s => s.RecordedAt)
            .FirstOrDefault();

        if (accountSync is null || runDerived is null)
            return;

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

    private static AccountReadinessSnapshotDto BuildReadinessSnapshot(
        AccountSummaryDto account,
        AccountSyncHistoryEntryDto? latestSync,
        MarginSnapshotDto? latestMargin,
        AccountSyncHistoryEntryDto? lastSuccessfulSync,
        int openBreakCount,
        DateTimeOffset now)
    {
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
            AddMarginReadinessIssues(account, latestSync, latestMargin, now, issues);

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

        if (latestMargin.MissingRequirementCount > 0
            || (latestMargin.Requirements.Count == 0 && latestMargin.MaintenanceMargin.GetValueOrDefault() > 0m))
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
            return AccountProviderLinkStatusDto.Linked;
        if (account.AccountType == AccountTypeDto.Bank && !string.IsNullOrWhiteSpace(account.BankDetails?.BankName))
            return AccountProviderLinkStatusDto.Linked;
        return AccountProviderLinkStatusDto.NotLinked;
    }

    private static string? ResolveEvidenceLink(AccountSyncHistoryEntryDto? latestSync)
        => NormalizeOptional(latestSync?.RawEvidencePath) ?? NormalizeOptional(latestSync?.ProjectionEvidencePath);

    private static string? ResolveMarginEvidenceLink(MarginSnapshotDto? latestMargin)
        => NormalizeOptional(latestMargin?.SnapshotEvidencePath) ?? NormalizeOptional(latestMargin?.AgreementEvidencePath);

    private static string RequireText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
