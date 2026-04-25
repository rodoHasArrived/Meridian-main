using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Application.FundAccounts;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Execution.Sdk;
using Meridian.Storage.Archival;
using Meridian.Strategies.Services;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

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
                    RetrievedAt: account.RetrievedAt)));
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
            Warnings: ["Brokerage account is linked, but no sync has been run."]);
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
            Warnings: statusWarnings);

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
        var account = await ResolveFundAccountAsync(fundAccountId, ct).ConfigureAwait(false);
        var providerId = NormalizeProviderId(request?.ProviderId)
            ?? NormalizeProviderId(account?.Institution)
            ?? _options.DefaultProviderId;
        var externalAccountId = NormalizeExternalAccountId(request?.ExternalAccountId)
            ?? NormalizeExternalAccountId(account?.CustodianDetails?.SubAccountNumber)
            ?? NormalizeExternalAccountId(account?.PortfolioId)
            ?? NormalizeExternalAccountId(account?.AccountCode);

        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(externalAccountId))
        {
            return null;
        }

        return new WorkstationBrokerageAccountLinkDto(
            FundAccountId: fundAccountId,
            ProviderId: providerId,
            ExternalAccountId: externalAccountId,
            DisplayName: account?.DisplayName ?? $"{providerId}:{externalAccountId}",
            LinkedAt: DateTimeOffset.UtcNow,
            LinkedBy: request?.RequestedBy);
    }

    private async Task<AccountSummaryDto?> ResolveFundAccountAsync(Guid fundAccountId, CancellationToken ct)
    {
        var fundAccountService = _services.GetService(typeof(IFundAccountService)) as IFundAccountService;
        if (fundAccountService is null)
        {
            return null;
        }

        return await fundAccountService.GetAccountAsync(fundAccountId, ct).ConfigureAwait(false);
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
            Warnings: [error]);

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
        var invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
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
