using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.Accounts;
using Meridian.Application.FundAccounts;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.Storage.Archival;
using Meridian.Strategies.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

#pragma warning disable CS0618 // Retained compatibility service persists legacy brokerage projection DTOs.

/// <summary>
/// Configures the durable brokerage read-side sync used by workstation fund ops.
/// </summary>
public sealed record BrokeragePortfolioSyncOptions(
    string RootDirectory,
    TimeSpan StaleAfter,
    string DefaultProviderId)
{
    public static BrokeragePortfolioSyncOptions Default { get; } = new(
        RootDirectory: Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Meridian",
            "workstation",
            "brokerage-sync"),
        StaleAfter: TimeSpan.FromMinutes(30),
        DefaultProviderId: "alpaca");
}

/// <summary>
/// Pulls read-only broker account, portfolio, order, fill, and cash activity snapshots into
/// a durable workstation projection. This service is operator-triggered and intentionally
/// avoids live-trading readiness decisions.
/// </summary>
public sealed class BrokeragePortfolioSyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly BrokeragePortfolioSyncOptions _options;
    private readonly IReadOnlyDictionary<string, IBrokerageAccountCatalog> _catalogs;
    private readonly IReadOnlyDictionary<string, IBrokeragePortfolioSync> _portfolioAdapters;
    private readonly IReadOnlyDictionary<string, IBrokerageActivitySync> _activityAdapters;
    private readonly IServiceProvider _services;
    private readonly ILogger<BrokeragePortfolioSyncService> _logger;

    public BrokeragePortfolioSyncService(
        BrokeragePortfolioSyncOptions options,
        IEnumerable<IBrokerageAccountCatalog> catalogs,
        IEnumerable<IBrokeragePortfolioSync> portfolioAdapters,
        IEnumerable<IBrokerageActivitySync> activityAdapters,
        IServiceProvider services,
        ILogger<BrokeragePortfolioSyncService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _catalogs = catalogs
            .GroupBy(static adapter => adapter.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        _portfolioAdapters = portfolioAdapters
            .GroupBy(static adapter => adapter.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        _activityAdapters = activityAdapters
            .GroupBy(static adapter => adapter.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<WorkstationBrokerageAccountDto>> DiscoverAccountsAsync(CancellationToken ct = default)
    {
        var accounts = new List<WorkstationBrokerageAccountDto>();

        foreach (var catalog in _catalogs.Values)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var providerAccounts = await catalog.GetAccountsAsync(ct).ConfigureAwait(false);
                accounts.AddRange(providerAccounts.Select(static account => new WorkstationBrokerageAccountDto(
                    ProviderId: account.ProviderId,
                    AccountId: account.AccountId,
                    DisplayName: account.DisplayName,
                    Status: account.Status,
                    Currency: account.Currency,
                    RetrievedAt: account.RetrievedAt,
                    AccountKind: InferAccountKind(account.Metadata, account.DisplayName))));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Brokerage account discovery failed for provider {ProviderId}", catalog.ProviderId);
            }
        }

        return accounts
            .OrderBy(static account => account.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static account => account.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<WorkstationBrokerageSyncStatusDto> GetStatusAsync(Guid fundAccountId, CancellationToken ct = default)
    {
        var projection = await LoadProjectionAsync(fundAccountId, ct).ConfigureAwait(false);
        if (projection is not null)
        {
            return RefreshStatus(projection.Status);
        }

        var link = await ResolveLinkAsync(fundAccountId, request: null, ct).ConfigureAwait(false);
        if (link is null)
        {
            return UnlinkedStatus(fundAccountId, "Fund account is not linked to a brokerage account.");
        }

        return new WorkstationBrokerageSyncStatusDto(
            FundAccountId: fundAccountId,
            ProviderId: link.ProviderId,
            ExternalAccountId: link.ExternalAccountId,
            Health: WorkstationBrokerageSyncHealth.Stale,
            IsLinked: true,
            IsStale: true,
            LastAttemptedSyncAt: null,
            LastSuccessfulSyncAt: null,
            LastError: null,
            PositionCount: 0,
            OpenOrderCount: 0,
            FillCount: 0,
            CashTransactionCount: 0,
            SecurityMissingCount: 0,
            Warnings: ["Brokerage account is linked, but no sync has been run."],
            AccountKind: link.AccountKind);
    }

    public async Task<WorkstationBrokerageSyncStatusDto> RunSyncAsync(
        Guid fundAccountId,
        WorkstationBrokerageSyncRunRequestDto? request = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        request ??= new WorkstationBrokerageSyncRunRequestDto();

        var attemptedAt = DateTimeOffset.UtcNow;
        var link = await ResolveLinkAsync(fundAccountId, request, ct).ConfigureAwait(false);
        if (link is null)
        {
            var requestProviderId = NormalizeProviderId(request.ProviderId);
            var requestExternalAccountId = NormalizeExternalAccountId(request.ExternalAccountId);
            if (requestProviderId is not null && requestExternalAccountId is not null)
            {
                link = new WorkstationBrokerageAccountLinkDto(
                    FundAccountId: fundAccountId,
                    ProviderId: requestProviderId,
                    ExternalAccountId: requestExternalAccountId,
                    DisplayName: $"{requestProviderId}:{requestExternalAccountId}",
                    LinkedAt: attemptedAt,
                    LinkedBy: request.RequestedBy,
                    AccountKind: request.AccountKind);
            }
        }

        if (link is null)
        {
            return UnlinkedStatus(fundAccountId, "Run request did not include a provider/account link and the fund account does not expose one.");
        }

        if (!_portfolioAdapters.TryGetValue(link.ProviderId, out var portfolioAdapter)
            && !_activityAdapters.TryGetValue(link.ProviderId, out _))
        {
            var status = FailedStatus(
                fundAccountId,
                link,
                attemptedAt,
                $"No brokerage sync adapter is registered for provider '{link.ProviderId}'.");
            await PersistFailureProjectionAsync(fundAccountId, link, status, attemptedAt, ct).ConfigureAwait(false);
            return status;
        }

        _portfolioAdapters.TryGetValue(link.ProviderId, out portfolioAdapter);
        _activityAdapters.TryGetValue(link.ProviderId, out var activityAdapter);

        var warnings = new List<string>();
        BrokeragePortfolioSnapshotDto? portfolio = null;
        BrokerageActivitySnapshotDto? activity = null;

        if (portfolioAdapter is not null)
        {
            try
            {
                portfolio = await portfolioAdapter.GetPortfolioSnapshotAsync(link.ExternalAccountId, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                warnings.Add($"Portfolio snapshot failed: {ex.Message}");
                _logger.LogWarning(ex, "Brokerage portfolio sync failed for {ProviderId}/{AccountId}", link.ProviderId, link.ExternalAccountId);
            }
        }
        else
        {
            warnings.Add($"Provider '{link.ProviderId}' has no portfolio sync adapter registered.");
        }

        if (activityAdapter is not null)
        {
            try
            {
                activity = await activityAdapter.GetActivitySnapshotAsync(link.ExternalAccountId, request.Since, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                warnings.Add($"Activity snapshot failed: {ex.Message}");
                _logger.LogWarning(ex, "Brokerage activity sync failed for {ProviderId}/{AccountId}", link.ProviderId, link.ExternalAccountId);
            }
        }
        else
        {
            warnings.Add($"Provider '{link.ProviderId}' has no activity sync adapter registered.");
        }

        var rawPath = BuildRawSnapshotPath(link.ProviderId, link.ExternalAccountId, attemptedAt);
        var raw = new BrokerageRawSyncSnapshot(
            CapturedAt: attemptedAt,
            ProviderId: link.ProviderId,
            ExternalAccountId: link.ExternalAccountId,
            RequestedBy: request.RequestedBy,
            Portfolio: portfolio,
            Activity: activity,
            Warnings: warnings);
        await WriteJsonAsync(rawPath, raw, ct).ConfigureAwait(false);

        var projection = await BuildProjectionAsync(
            fundAccountId,
            link,
            attemptedAt,
            rawPath,
            portfolio,
            activity,
            warnings,
            ct).ConfigureAwait(false);

        await PersistProjectionAsync(projection, ct).ConfigureAwait(false);
        await EnrichSharedReadServicesAsync(fundAccountId, attemptedAt, projection, ct).ConfigureAwait(false);
        return projection.Status;
    }

    public async Task<IReadOnlyList<WorkstationBrokeragePositionDto>> GetPositionsAsync(
        Guid fundAccountId,
        CancellationToken ct = default)
    {
        var projection = await LoadProjectionAsync(fundAccountId, ct).ConfigureAwait(false);
        return projection?.Positions ?? [];
    }

    public async Task<WorkstationBrokerageSyncViewDto?> GetActivityAsync(Guid fundAccountId, CancellationToken ct = default)
        => await LoadProjectionAsync(fundAccountId, ct).ConfigureAwait(false);

    public async Task<WorkstationBrokerageSyncViewDto?> GetViewAsync(Guid fundAccountId, CancellationToken ct = default)
        => await LoadProjectionAsync(fundAccountId, ct).ConfigureAwait(false);

    public async Task<WorkstationBrokerageAccountLinkDto?> LinkAccountAsync(
        Guid fundAccountId,
        BrokerageAccountLinkRequestDto request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        var account = await ResolveFundAccountAsync(fundAccountId, ct).ConfigureAwait(false);
        if (account is null
            || string.IsNullOrWhiteSpace(request.ProviderId)
            || string.IsNullOrWhiteSpace(request.ExternalAccountId))
        {
            return null;
        }

        var providerId = NormalizeProviderId(request.ProviderId);
        var externalAccountId = NormalizeExternalAccountId(request.ExternalAccountId);
        if (providerId is null || externalAccountId is null)
        {
            return null;
        }

        var link = new WorkstationBrokerageAccountLinkDto(
            FundAccountId: fundAccountId,
            ProviderId: providerId,
            ExternalAccountId: externalAccountId,
            DisplayName: string.IsNullOrWhiteSpace(request.DisplayName)
                ? account.DisplayName ?? $"{providerId}:{externalAccountId}"
                : request.DisplayName.Trim(),
            LinkedAt: DateTimeOffset.UtcNow,
            LinkedBy: request.LinkedBy,
            AccountKind: request.AccountKind);

        await WriteJsonAsync(BuildLinkPath(fundAccountId), link, ct).ConfigureAwait(false);
        return link;
    }

    public async Task<BrokeragePortfolioPerformanceDto> GetPerformanceAsync(
        Guid fundAccountId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var projection = await LoadProjectionAsync(fundAccountId, ct).ConfigureAwait(false);
        var timeline = await GetBalanceHistoryAsync(fundAccountId, from, to, ct).ConfigureAwait(false);
        var cashEntries = FilterCashTransactions(projection, from, to)
            .Select(ToCashFlowEntry)
            .ToArray();
        var cashByDate = cashEntries
            .GroupBy(static entry => DateOnly.FromDateTime(entry.PostedAt.UtcDateTime))
            .ToDictionary(static group => group.Key, static group => group.Sum(entry => entry.Amount));
        var points = timeline
            .OrderBy(static snapshot => snapshot.AsOfDate)
            .Select(snapshot => new BrokeragePortfolioPerformancePointDto(
                snapshot.AsOfDate,
                snapshot.CashBalance + (snapshot.SecuritiesMarketValue ?? 0m),
                snapshot.CashBalance,
                cashByDate.GetValueOrDefault(snapshot.AsOfDate)))
            .ToArray();

        var warnings = new List<string>();
        if (projection is null && points.Length == 0)
        {
            warnings.Add("No brokerage sync projection exists for this fund account.");
        }

        if (points.Length < 2)
        {
            warnings.Add("At least two balance snapshots are required for cash-adjusted performance.");
        }

        var beginningEquity = points.Length >= 2 ? points[0].Equity : (decimal?)null;
        var endingEquity = points.Length >= 2 ? points[^1].Equity : (decimal?)null;
        var netCashFlow = cashEntries.Sum(static entry => entry.Amount);
        var cashAdjustedReturn = beginningEquity.HasValue && endingEquity.HasValue
            ? endingEquity.Value - beginningEquity.Value - netCashFlow
            : (decimal?)null;
        var cashAdjustedReturnPercent = cashAdjustedReturn.HasValue && beginningEquity.GetValueOrDefault() != 0m
            ? cashAdjustedReturn.Value / beginningEquity!.Value
            : (decimal?)null;

        return new BrokeragePortfolioPerformanceDto(
            FundAccountId: fundAccountId,
            ProviderId: projection?.Link.ProviderId,
            ExternalAccountId: projection?.Link.ExternalAccountId,
            AccountKind: projection?.Link.AccountKind ?? BrokerageAccountKindDto.Unknown,
            From: from,
            To: to,
            HasSufficientHistory: points.Length >= 2,
            BeginningEquity: beginningEquity,
            EndingEquity: endingEquity,
            NetCashFlow: netCashFlow,
            CashAdjustedReturn: cashAdjustedReturn,
            CashAdjustedReturnPercent: cashAdjustedReturnPercent,
            Points: points,
            Warnings: warnings);
    }

    public async Task<BrokerageCashFlowSummaryDto> GetCashFlowAsync(
        Guid fundAccountId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var projection = await LoadProjectionAsync(fundAccountId, ct).ConfigureAwait(false);
        var entries = FilterCashTransactions(projection, from, to)
            .Select(ToCashFlowEntry)
            .OrderByDescending(static entry => entry.PostedAt)
            .ToArray();
        var currency = projection?.Balance?.Currency
            ?? entries.Select(static entry => entry.Currency).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))
            ?? "USD";
        var warnings = projection is null
            ? ["No brokerage sync projection exists for this fund account."]
            : Array.Empty<string>();

        return new BrokerageCashFlowSummaryDto(
            FundAccountId: fundAccountId,
            ProviderId: projection?.Link.ProviderId,
            ExternalAccountId: projection?.Link.ExternalAccountId,
            AccountKind: projection?.Link.AccountKind ?? BrokerageAccountKindDto.Unknown,
            From: from,
            To: to,
            TotalInflows: entries.Where(static entry => entry.Amount > 0m).Sum(static entry => entry.Amount),
            TotalOutflows: Math.Abs(entries.Where(static entry => entry.Amount < 0m).Sum(static entry => entry.Amount)),
            NetCashFlow: entries.Sum(static entry => entry.Amount),
            Currency: currency,
            TransactionCount: entries.Length,
            Entries: entries,
            Warnings: warnings);
    }

    public async Task<BrokerageHouseholdPortfolioDto> GetHouseholdAsync(
        string? providerId = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var providerFilter = NormalizeProviderId(providerId);
        var projectionRoot = Path.Combine(_options.RootDirectory, "projections");
        var projections = new List<WorkstationBrokerageSyncViewDto>();
        if (Directory.Exists(projectionRoot))
        {
            foreach (var path in Directory.EnumerateFiles(projectionRoot, "current.json", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                var projection = await ReadProjectionFileAsync(path, ct).ConfigureAwait(false);
                if (projection is not null
                    && (providerFilter is null || string.Equals(projection.Link.ProviderId, providerFilter, StringComparison.OrdinalIgnoreCase)))
                {
                    projections.Add(projection);
                }
            }
        }

        var accounts = projections
            .OrderBy(static projection => projection.Link.AccountKind)
            .ThenBy(static projection => projection.Link.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(static projection => new BrokerageHouseholdAccountDto(
                FundAccountId: projection.FundAccountId,
                ProviderId: projection.Link.ProviderId,
                ExternalAccountId: projection.Link.ExternalAccountId,
                DisplayName: projection.Link.DisplayName,
                AccountKind: projection.Link.AccountKind,
                Health: projection.Status.Health,
                Cash: projection.Balance?.Cash ?? 0m,
                Equity: projection.Balance?.Equity ?? 0m,
                BuyingPower: projection.Balance?.BuyingPower ?? 0m,
                Currency: projection.Balance?.Currency ?? "USD",
                SyncedAt: projection.SyncedAt,
                PositionCount: projection.Positions.Count,
                CashTransactionCount: projection.CashTransactions.Count,
                Warnings: projection.Status.Warnings))
            .ToArray();
        var positions = projections
            .SelectMany(static projection => projection.Positions.Select(position => new BrokerageHouseholdPositionDto(
                FundAccountId: projection.FundAccountId,
                ProviderId: projection.Link.ProviderId,
                ExternalAccountId: projection.Link.ExternalAccountId,
                AccountKind: projection.Link.AccountKind,
                Symbol: position.Symbol,
                Quantity: position.Quantity,
                AverageEntryPrice: position.AverageEntryPrice,
                MarketPrice: position.MarketPrice,
                MarketValue: position.MarketValue,
                UnrealizedPnl: position.UnrealizedPnl,
                AssetClass: position.AssetClass,
                Security: position.Security,
                Description: position.Description,
                PositionId: position.PositionId,
                Currency: position.Currency)))
            .OrderBy(static position => position.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var currency = accounts.Select(static account => account.Currency).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? "USD";

        return new BrokerageHouseholdPortfolioDto(
            ProviderId: providerFilter ?? "all",
            AsOf: DateTimeOffset.UtcNow,
            TotalCash: accounts.Sum(static account => account.Cash),
            TotalEquity: accounts.Sum(static account => account.Equity),
            TotalBuyingPower: accounts.Sum(static account => account.BuyingPower),
            Currency: currency,
            Accounts: accounts,
            Positions: positions,
            Warnings: projections.Count == 0 ? ["No brokerage sync projections match the requested provider."] : []);
    }

    private async Task<WorkstationBrokerageSyncViewDto> BuildProjectionAsync(
        Guid fundAccountId,
        WorkstationBrokerageAccountLinkDto link,
        DateTimeOffset attemptedAt,
        string rawPath,
        BrokeragePortfolioSnapshotDto? portfolio,
        BrokerageActivitySnapshotDto? activity,
        IReadOnlyList<string> warnings,
        CancellationToken ct)
    {
        var missingSecurityCount = 0;
        var positions = new List<WorkstationBrokeragePositionDto>();
        foreach (var position in portfolio?.Positions ?? [])
        {
            ct.ThrowIfCancellationRequested();

            var security = await ResolveSecurityAsync(position.Symbol, ct).ConfigureAwait(false);
            if (security is null)
            {
                missingSecurityCount++;
            }

            positions.Add(new WorkstationBrokeragePositionDto(
                Symbol: position.Symbol,
                Quantity: position.Quantity,
                AverageEntryPrice: position.AverageEntryPrice,
                MarketPrice: position.MarketPrice,
                MarketValue: position.MarketValue,
                UnrealizedPnl: position.UnrealizedPnl,
                AssetClass: position.AssetClass,
                Security: security,
                Description: position.Description,
                PositionId: position.PositionId,
                Currency: position.Currency));
        }

        var statusWarnings = new List<string>(warnings);
        if (missingSecurityCount > 0)
        {
            statusWarnings.Add($"{missingSecurityCount} brokerage position(s) are not covered by Security Master.");
        }

        var hasPartialFailure = warnings.Count > 0;
        var health = hasPartialFailure || missingSecurityCount > 0
            ? WorkstationBrokerageSyncHealth.Degraded
            : WorkstationBrokerageSyncHealth.Healthy;
        var lastError = hasPartialFailure ? string.Join(" ", warnings) : null;
        if (portfolio is null && activity is null)
        {
            health = WorkstationBrokerageSyncHealth.Failed;
            lastError = lastError ?? "Brokerage sync returned no portfolio or activity snapshot.";
        }

        var status = new WorkstationBrokerageSyncStatusDto(
            FundAccountId: fundAccountId,
            ProviderId: link.ProviderId,
            ExternalAccountId: link.ExternalAccountId,
            Health: health,
            IsLinked: true,
            IsStale: false,
            LastAttemptedSyncAt: attemptedAt,
            LastSuccessfulSyncAt: portfolio is not null || activity is not null ? attemptedAt : null,
            LastError: lastError,
            PositionCount: positions.Count,
            OpenOrderCount: activity?.Orders.Count ?? 0,
            FillCount: activity?.Fills.Count ?? 0,
            CashTransactionCount: activity?.CashTransactions.Count ?? 0,
            SecurityMissingCount: missingSecurityCount,
            Warnings: statusWarnings,
            AccountKind: link.AccountKind);

        var projectionPath = BuildProjectionPath(fundAccountId);
        return new WorkstationBrokerageSyncViewDto(
            FundAccountId: fundAccountId,
            Link: link,
            Status: status,
            Balance: portfolio?.Balance is null
                ? null
                : new WorkstationBrokerageBalanceSnapshotDto(
                    Cash: portfolio.Balance.Cash,
                    Equity: portfolio.Balance.Equity,
                    BuyingPower: portfolio.Balance.BuyingPower,
                    Currency: portfolio.Balance.Currency,
                    MarginBalance: portfolio.Balance.MarginBalance),
            Positions: positions,
            Orders: activity?.Orders
                .Select(static order => new WorkstationBrokerageOrderDto(
                    OrderId: order.OrderId,
                    ClientOrderId: order.ClientOrderId,
                    Symbol: order.Symbol,
                    Side: order.Side.ToString(),
                    Type: order.Type.ToString(),
                    Status: order.Status.ToString(),
                    Quantity: order.Quantity,
                    FilledQuantity: order.FilledQuantity,
                    LimitPrice: order.LimitPrice,
                    StopPrice: order.StopPrice,
                    CreatedAt: order.CreatedAt,
                    UpdatedAt: order.UpdatedAt))
                .ToArray() ?? [],
            Fills: activity?.Fills
                .Select(static fill => new WorkstationBrokerageFillDto(
                    FillId: fill.FillId,
                    OrderId: fill.OrderId,
                    Symbol: fill.Symbol,
                    Side: fill.Side.ToString(),
                    Quantity: fill.Quantity,
                    Price: fill.Price,
                    FilledAt: fill.FilledAt,
                    Venue: fill.Venue,
                    Commission: fill.Commission))
                .ToArray() ?? [],
            CashTransactions: activity?.CashTransactions
                .Select(static cash => new WorkstationBrokerageCashTransactionDto(
                    TransactionId: cash.TransactionId,
                    TransactionType: cash.TransactionType,
                    Amount: cash.Amount,
                    Currency: cash.Currency,
                    PostedAt: cash.PostedAt,
                    Symbol: cash.Symbol,
                    Description: cash.Description))
                .ToArray() ?? [],
            SyncedAt: attemptedAt,
            RawSnapshotPath: rawPath,
            ProjectionPath: projectionPath);
    }

    private async Task PersistFailureProjectionAsync(
        Guid fundAccountId,
        WorkstationBrokerageAccountLinkDto link,
        WorkstationBrokerageSyncStatusDto status,
        DateTimeOffset attemptedAt,
        CancellationToken ct)
    {
        var projectionPath = BuildProjectionPath(fundAccountId);
        var projection = new WorkstationBrokerageSyncViewDto(
            FundAccountId: fundAccountId,
            Link: link,
            Status: status,
            Balance: null,
            Positions: [],
            Orders: [],
            Fills: [],
            CashTransactions: [],
            SyncedAt: attemptedAt,
            RawSnapshotPath: string.Empty,
            ProjectionPath: projectionPath);

        await PersistProjectionAsync(projection, ct).ConfigureAwait(false);
    }

    private async Task PersistProjectionAsync(WorkstationBrokerageSyncViewDto projection, CancellationToken ct)
    {
        await WriteJsonAsync(projection.ProjectionPath, projection, ct).ConfigureAwait(false);
        await WriteJsonAsync(
            BuildCursorPath(projection.FundAccountId),
            new BrokerageSyncCursor(
                FundAccountId: projection.FundAccountId,
                ProviderId: projection.Link.ProviderId,
                ExternalAccountId: projection.Link.ExternalAccountId,
                LastAttemptedSyncAt: projection.Status.LastAttemptedSyncAt,
                LastSuccessfulSyncAt: projection.Status.LastSuccessfulSyncAt,
                LastRawSnapshotPath: projection.RawSnapshotPath,
                LastProjectionPath: projection.ProjectionPath),
            ct).ConfigureAwait(false);
    }

    private async Task EnrichSharedReadServicesAsync(
        Guid fundAccountId,
        DateTimeOffset attemptedAt,
        WorkstationBrokerageSyncViewDto projection,
        CancellationToken ct)
    {
        if (projection.Balance is null)
        {
            return;
        }

        var asOfDate = DateOnly.FromDateTime(attemptedAt.UtcDateTime);
        var source = $"brokerage-sync:{projection.Link.ProviderId}";
        var externalReference = projection.Link.ExternalAccountId;
        var latestSnapshot = await GetLatestBalanceSnapshotAsync(fundAccountId, ct).ConfigureAwait(false);
        var isDuplicateSnapshot = latestSnapshot is not null
            && latestSnapshot.AsOfDate == asOfDate
            && string.Equals(latestSnapshot.Source, source, StringComparison.OrdinalIgnoreCase)
            && string.Equals(latestSnapshot.ExternalReference, externalReference, StringComparison.OrdinalIgnoreCase)
            && latestSnapshot.Currency == projection.Balance.Currency
            && latestSnapshot.CashBalance == projection.Balance.Cash
            && latestSnapshot.SecuritiesMarketValue == projection.Balance.Equity - projection.Balance.Cash;

        if (isDuplicateSnapshot)
        {
            return;
        }

        var snapshotRecorded = await RecordBalanceSnapshotAsync(
            new RecordAccountBalanceSnapshotRequest(
                AccountId: fundAccountId,
                AsOfDate: asOfDate,
                Currency: projection.Balance.Currency,
                CashBalance: projection.Balance.Cash,
                SecuritiesMarketValue: projection.Balance.Equity - projection.Balance.Cash,
                AccruedInterest: 0m,
                PendingSettlement: 0m,
                Source: source,
                RecordedBy: projection.Link.LinkedBy ?? "brokerage-sync",
                ExternalReference: externalReference),
            ct).ConfigureAwait(false);

        if (!snapshotRecorded)
        {
            return;
        }

        await ReconcileAccountAsync(
            new ReconcileAccountRequest(
                fundAccountId,
                DateOnly.FromDateTime(attemptedAt.UtcDateTime),
                projection.Link.LinkedBy ?? "brokerage-sync"),
            ct).ConfigureAwait(false);
    }

    private async Task<WorkstationBrokerageSyncViewDto?> LoadProjectionAsync(Guid fundAccountId, CancellationToken ct)
    {
        var path = BuildProjectionPath(fundAccountId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<WorkstationBrokerageSyncViewDto>(stream, JsonOptions, ct).ConfigureAwait(false);
    }

    private async Task<WorkstationBrokerageSyncViewDto?> ReadProjectionFileAsync(string path, CancellationToken ct)
    {
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<WorkstationBrokerageSyncViewDto>(stream, JsonOptions, ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Skipping unreadable brokerage projection {Path}", path);
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Skipping locked brokerage projection {Path}", path);
            return null;
        }
    }

    private async Task<WorkstationBrokerageAccountLinkDto?> LoadLinkAsync(Guid fundAccountId, CancellationToken ct)
    {
        var path = BuildLinkPath(fundAccountId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<WorkstationBrokerageAccountLinkDto>(stream, JsonOptions, ct).ConfigureAwait(false);
    }

    private static IEnumerable<WorkstationBrokerageCashTransactionDto> FilterCashTransactions(
        WorkstationBrokerageSyncViewDto? projection,
        DateOnly? from,
        DateOnly? to)
    {
        if (projection is null)
        {
            return [];
        }

        return projection.CashTransactions.Where(transaction =>
        {
            var date = DateOnly.FromDateTime(transaction.PostedAt.UtcDateTime);
            return (!from.HasValue || date >= from.Value)
                && (!to.HasValue || date <= to.Value);
        });
    }

    private static BrokerageCashFlowEntryDto ToCashFlowEntry(WorkstationBrokerageCashTransactionDto transaction)
        => new(
            TransactionId: transaction.TransactionId,
            TransactionType: transaction.TransactionType,
            Category: CategorizeCashTransaction(transaction.TransactionType, transaction.Description),
            Amount: transaction.Amount,
            Currency: transaction.Currency,
            PostedAt: transaction.PostedAt,
            Symbol: transaction.Symbol,
            Description: transaction.Description);

    private static string CategorizeCashTransaction(string transactionType, string? description)
    {
        var value = $"{transactionType} {description}".ToUpperInvariant();
        if (value.Contains("DIV", StringComparison.Ordinal) || value.Contains("DIVIDEND", StringComparison.Ordinal))
        {
            return "Dividend";
        }

        if (value.Contains("INTEREST", StringComparison.Ordinal))
        {
            return "Interest";
        }

        if (value.Contains("FEE", StringComparison.Ordinal) || value.Contains("COMM", StringComparison.Ordinal))
        {
            return "Fee";
        }

        if (value.Contains("DEPOSIT", StringComparison.Ordinal) || value.Contains("ACH_IN", StringComparison.Ordinal))
        {
            return "Deposit";
        }

        if (value.Contains("WITHDRAW", StringComparison.Ordinal) || value.Contains("ACH_OUT", StringComparison.Ordinal))
        {
            return "Withdrawal";
        }

        if (value.Contains("OPTION", StringComparison.Ordinal) || value.Contains("PREMIUM", StringComparison.Ordinal))
        {
            return "Option Premium";
        }

        if (value.Contains("TRADE", StringComparison.Ordinal) || value.Contains("FILL", StringComparison.Ordinal))
        {
            return "Trade";
        }

        if (value.Contains("TRANSFER", StringComparison.Ordinal))
        {
            return "Transfer";
        }

        return "Other";
    }

    private WorkstationBrokerageSyncStatusDto RefreshStatus(WorkstationBrokerageSyncStatusDto status)
    {
        if (!status.IsLinked || status.LastSuccessfulSyncAt is null)
        {
            return status;
        }

        var age = DateTimeOffset.UtcNow - status.LastSuccessfulSyncAt.Value;
        if (age <= _options.StaleAfter)
        {
            return status;
        }

        var warnings = status.Warnings.Contains("Brokerage sync is stale.", StringComparer.OrdinalIgnoreCase)
            ? status.Warnings
            : status.Warnings.Concat(["Brokerage sync is stale."]).ToArray();

        return status with
        {
            Health = status.Health == WorkstationBrokerageSyncHealth.Failed
                ? WorkstationBrokerageSyncHealth.Failed
                : WorkstationBrokerageSyncHealth.Stale,
            IsStale = true,
            Warnings = warnings
        };
    }

    private async Task<WorkstationBrokerageAccountLinkDto?> ResolveLinkAsync(
        Guid fundAccountId,
        WorkstationBrokerageSyncRunRequestDto? request,
        CancellationToken ct)
    {
        var requestProviderId = NormalizeProviderId(request?.ProviderId);
        var requestExternalAccountId = NormalizeExternalAccountId(request?.ExternalAccountId);
        if (requestProviderId is not null && requestExternalAccountId is not null)
        {
            var requestedAccount = await ResolveFundAccountAsync(fundAccountId, ct).ConfigureAwait(false);
            return new WorkstationBrokerageAccountLinkDto(
                FundAccountId: fundAccountId,
                ProviderId: requestProviderId,
                ExternalAccountId: requestExternalAccountId,
                DisplayName: requestedAccount?.DisplayName ?? $"{requestProviderId}:{requestExternalAccountId}",
                LinkedAt: DateTimeOffset.UtcNow,
                LinkedBy: request?.RequestedBy,
                AccountKind: request?.AccountKind ?? BrokerageAccountKindDto.Unknown);
        }

        var account = await ResolveFundAccountAsync(fundAccountId, ct).ConfigureAwait(false);
        if (account is null)
        {
            return null;
        }

        var persistedLink = await LoadLinkAsync(fundAccountId, ct).ConfigureAwait(false);
        if (persistedLink is not null)
        {
            return persistedLink;
        }

        var providerId = NormalizeProviderId(account.Institution)
            ?? _options.DefaultProviderId;
        var externalAccountId = NormalizeExternalAccountId(account.CustodianDetails?.SubAccountNumber)
            ?? NormalizeExternalAccountId(account.PortfolioId)
            ?? NormalizeExternalAccountId(account.AccountCode);

        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(externalAccountId))
        {
            return null;
        }

        return new WorkstationBrokerageAccountLinkDto(
            FundAccountId: fundAccountId,
            ProviderId: providerId,
            ExternalAccountId: externalAccountId,
            DisplayName: account.DisplayName ?? $"{providerId}:{externalAccountId}",
            LinkedAt: DateTimeOffset.UtcNow,
            LinkedBy: request?.RequestedBy,
            AccountKind: request?.AccountKind ?? BrokerageAccountKindDto.Unknown);
    }

    private async Task<IReadOnlyList<AccountBalanceSnapshotDto>> GetBalanceHistoryAsync(
        Guid fundAccountId,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct)
    {
        if (_services.GetService<IAccountQueryService>() is { } query)
        {
            return await query.GetBalanceTimelineAsync(fundAccountId, from, to, ct).ConfigureAwait(false);
        }

        if (_services.GetService<IFundAccountService>() is { } fundAccountService)
        {
            return await fundAccountService.GetBalanceHistoryAsync(fundAccountId, from, to, ct).ConfigureAwait(false);
        }

        return [];
    }

    private async Task<AccountBalanceSnapshotDto?> GetLatestBalanceSnapshotAsync(Guid fundAccountId, CancellationToken ct)
    {
        if (_services.GetService<IAccountQueryService>() is { } query)
        {
            return await query.GetLatestBalanceSnapshotAsync(fundAccountId, ct).ConfigureAwait(false);
        }

        if (_services.GetService<IFundAccountService>() is { } fundAccountService)
        {
            return await fundAccountService.GetLatestBalanceSnapshotAsync(fundAccountId, ct).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<bool> RecordBalanceSnapshotAsync(RecordAccountBalanceSnapshotRequest request, CancellationToken ct)
    {
        if (_services.GetService<IAccountManagementService>() is { } management)
        {
            await management.RecordBalanceSnapshotAsync(request, ct).ConfigureAwait(false);
            return true;
        }

        if (_services.GetService<IFundAccountService>() is { } fundAccountService)
        {
            await fundAccountService.RecordBalanceSnapshotAsync(request, ct).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private async Task ReconcileAccountAsync(ReconcileAccountRequest request, CancellationToken ct)
    {
        if (_services.GetService<IAccountManagementService>() is { } management)
        {
            await management.ReconcileAccountAsync(request, ct).ConfigureAwait(false);
            return;
        }

        if (_services.GetService<IFundAccountService>() is { } fundAccountService)
        {
            await fundAccountService.ReconcileAccountAsync(request, ct).ConfigureAwait(false);
        }
    }

    private async Task<AccountSummaryDto?> ResolveFundAccountAsync(Guid fundAccountId, CancellationToken ct)
    {
        if (_services.GetService<IAccountQueryService>() is { } query)
        {
            return await query.GetAccountAsync(fundAccountId, ct).ConfigureAwait(false);
        }

        if (_services.GetService<IFundAccountService>() is { } fundAccountService)
        {
            return await fundAccountService.GetAccountAsync(fundAccountId, ct).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<WorkstationSecurityReference?> ResolveSecurityAsync(string symbol, CancellationToken ct)
    {
        var lookup = _services.GetService(typeof(ISecurityReferenceLookup)) as ISecurityReferenceLookup;
        if (lookup is null || string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        return await lookup.GetBySymbolAsync(symbol, ct).ConfigureAwait(false);
    }

    private WorkstationBrokerageSyncStatusDto UnlinkedStatus(Guid fundAccountId, string warning) => new(
        FundAccountId: fundAccountId,
        ProviderId: null,
        ExternalAccountId: null,
        Health: WorkstationBrokerageSyncHealth.Unlinked,
        IsLinked: false,
        IsStale: true,
        LastAttemptedSyncAt: null,
        LastSuccessfulSyncAt: null,
        LastError: null,
        PositionCount: 0,
        OpenOrderCount: 0,
        FillCount: 0,
        CashTransactionCount: 0,
        SecurityMissingCount: 0,
        Warnings: [warning]);

    private static WorkstationBrokerageSyncStatusDto FailedStatus(
        Guid fundAccountId,
        WorkstationBrokerageAccountLinkDto link,
        DateTimeOffset attemptedAt,
        string error) => new(
            FundAccountId: fundAccountId,
            ProviderId: link.ProviderId,
            ExternalAccountId: link.ExternalAccountId,
            Health: WorkstationBrokerageSyncHealth.Failed,
            IsLinked: true,
            IsStale: true,
            LastAttemptedSyncAt: attemptedAt,
            LastSuccessfulSyncAt: null,
            LastError: error,
            PositionCount: 0,
            OpenOrderCount: 0,
            FillCount: 0,
            CashTransactionCount: 0,
            SecurityMissingCount: 0,
            Warnings: [error],
            AccountKind: link.AccountKind);

    private static string? NormalizeProviderId(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        var value = providerId.Trim();
        return value.Contains("alpaca", StringComparison.OrdinalIgnoreCase)
            ? "alpaca"
            : value.ToLowerInvariant();
    }

    private static string? NormalizeExternalAccountId(string? externalAccountId)
        => string.IsNullOrWhiteSpace(externalAccountId) ? null : externalAccountId.Trim();

    private string BuildProjectionPath(Guid fundAccountId)
        => Path.Combine(_options.RootDirectory, "projections", fundAccountId.ToString("N"), "current.json");

    private string BuildLinkPath(Guid fundAccountId)
        => Path.Combine(_options.RootDirectory, "links", $"{fundAccountId:N}.json");

    private string BuildCursorPath(Guid fundAccountId)
        => Path.Combine(_options.RootDirectory, "cursors", $"{fundAccountId:N}.json");

    private string BuildRawSnapshotPath(string providerId, string externalAccountId, DateTimeOffset capturedAt)
    {
        var fileName = capturedAt.UtcDateTime.ToString("yyyyMMddTHHmmssfffffffZ", System.Globalization.CultureInfo.InvariantCulture);
        return Path.Combine(
            _options.RootDirectory,
            "raw",
            SanitizePathSegment(providerId),
            SanitizePathSegment(externalAccountId),
            $"{fileName}.json");
    }

    private static string SanitizePathSegment(string value)
    {
        return new string(value.Select(static ch => IsPortablePathSegmentCharacter(ch) ? ch : '_').ToArray());
    }

    private static bool IsPortablePathSegmentCharacter(char ch)
        => ch >= ' '
            && ch is not '<' and not '>' and not ':' and not '"' and not '/' and not '\\' and not '|' and not '?' and not '*';

    private static BrokerageAccountKindDto InferAccountKind(
        IReadOnlyDictionary<string, string>? metadata,
        string displayName)
    {
        var value = metadata?.TryGetValue("accountKind", out var accountKind) == true
            ? accountKind
            : metadata?.TryGetValue("account_type", out var accountType) == true
                ? accountType
                : metadata?.TryGetValue("type", out var type) == true
                    ? type
                    : displayName;
        var normalized = value.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);

        if (normalized.Contains("roth", StringComparison.OrdinalIgnoreCase))
        {
            return BrokerageAccountKindDto.RothIra;
        }

        if (normalized.Contains("traditionalira", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("tradira", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("ira", StringComparison.OrdinalIgnoreCase))
        {
            return BrokerageAccountKindDto.TraditionalIra;
        }

        if (normalized.Contains("taxable", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("brokerage", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("individual", StringComparison.OrdinalIgnoreCase))
        {
            return BrokerageAccountKindDto.TaxableBrokerage;
        }

        return BrokerageAccountKindDto.Unknown;
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        await AtomicFileWriter.WriteAsync(path, json, ct).ConfigureAwait(false);
    }

    private sealed record BrokerageRawSyncSnapshot(
        DateTimeOffset CapturedAt,
        string ProviderId,
        string ExternalAccountId,
        string? RequestedBy,
        BrokeragePortfolioSnapshotDto? Portfolio,
        BrokerageActivitySnapshotDto? Activity,
        IReadOnlyList<string> Warnings);

    private sealed record BrokerageSyncCursor(
        Guid FundAccountId,
        string ProviderId,
        string ExternalAccountId,
        DateTimeOffset? LastAttemptedSyncAt,
        DateTimeOffset? LastSuccessfulSyncAt,
        string LastRawSnapshotPath,
        string LastProjectionPath);
}

#pragma warning restore CS0618
