using System.Security.Cryptography;
using System.Text;
using Meridian.FinancialOperations.Banking;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Meridian.PortfolioRecords.FundAccounts;
using Meridian.Contracts.Banking;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Plaid;
using Meridian.Infrastructure.Adapters.Plaid;
using Microsoft.Extensions.Logging;

namespace Meridian.Ui.Shared.Services;

public sealed class PlaidWorkstationService : IPlaidIngestionService, IPlaidTransferService
{
    private const string ProviderId = "plaid";

    private readonly IPlaidClient _client;
    private readonly IPlaidConnectionRepository _repository;
    private readonly IProviderCredentialStore _credentialStore;
    private readonly IFundAccountService? _fundAccountService;
    private readonly IBankingService? _bankingService;
    private readonly PlaidOptions _options;
    private readonly ILogger<PlaidWorkstationService> _logger;

    public PlaidWorkstationService(
        IPlaidClient client,
        IPlaidConnectionRepository repository,
        IProviderCredentialStore credentialStore,
        PlaidOptions options,
        ILogger<PlaidWorkstationService> logger,
        IFundAccountService? fundAccountService = null,
        IBankingService? bankingService = null)
    {
        _client = client;
        _repository = repository;
        _credentialStore = credentialStore;
        _options = options;
        _logger = logger;
        _fundAccountService = fundAccountService;
        _bankingService = bankingService;
    }

    public async Task<PlaidLinkTokenResponse> CreateLinkTokenAsync(PlaidLinkTokenRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var credentials = await ResolveCredentialsAsync(ct).ConfigureAwait(false);
        var products = request.Products is { Count: > 0 }
            ? request.Products
            : _options.DefaultProducts ?? PlaidOptions.Default.DefaultProducts!;
        if (!_options.EnableInvestments)
        {
            products = products.Where(static product => product != PlaidProductDto.Investments).ToArray();
        }

        var webhookUrl = request.WebhookUrl ?? BuildWebhookUrl();
        var result = await _client.CreateLinkTokenAsync(
                credentials,
                request with
                {
                    Products = products,
                    WebhookUrl = webhookUrl
                },
                ct)
            .ConfigureAwait(false);
        return result with
        {
            InstitutionId = request.InstitutionId,
            InstitutionName = request.InstitutionName,
            Environment = credentials.Environment
        };
    }

    public async Task<PlaidInstitutionSearchResult> SearchInstitutionsAsync(
        string query,
        IReadOnlyList<PlaidProductDto>? products = null,
        IReadOnlyList<string>? countryCodes = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            return new PlaidInstitutionSearchResult(query ?? string.Empty, Array.Empty<PlaidInstitutionDto>(), RequestId: null);
        }

        var credentials = await ResolveCredentialsAsync(ct).ConfigureAwait(false);
        var requestedProducts = products is { Count: > 0 }
            ? products
            : _options.DefaultProducts ?? PlaidOptions.Default.DefaultProducts!;
        if (!_options.EnableInvestments)
        {
            requestedProducts = requestedProducts.Where(static product => product != PlaidProductDto.Investments).ToArray();
        }

        return await _client.SearchInstitutionsAsync(
                credentials,
                query.Trim(),
                requestedProducts,
                countryCodes is { Count: > 0 } ? countryCodes : ["US"],
                ct)
            .ConfigureAwait(false);
    }

    public async Task<PlaidPublicTokenExchangeResult> ExchangePublicTokenAsync(
        PlaidPublicTokenExchangeRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var credentials = await ResolveCredentialsAsync(ct).ConfigureAwait(false);
        var exchange = await _client.ExchangePublicTokenAsync(credentials, request.PublicToken, ct).ConfigureAwait(false);
        var accessTokenKey = FilePlaidConnectionRepository.BuildAccessTokenKey(exchange.ItemId);
        await _credentialStore.SaveAsync(
                new ProviderCredentialSaveRequest(
                    ProviderId,
                    new Dictionary<string, string?>
                    {
                        [accessTokenKey] = exchange.AccessToken
                    },
                    Environment: credentials.Environment.ToString().ToLowerInvariant(),
                    Actor: request.RequestedBy,
                    Metadata: new Dictionary<string, string>
                    {
                        ["lastLinkedPlaidItemId"] = exchange.ItemId,
                        ["lastPlaidInstitutionId"] = request.InstitutionId ?? string.Empty
                    }),
                ct)
            .ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var item = new PlaidItemDto(
            exchange.ItemId,
            request.InstitutionId ?? "unknown",
            request.InstitutionName ?? "Plaid institution",
            credentials.Environment,
            PlaidItemStatusDto.Linked,
            accessTokenKey,
            TransactionsCursor: null,
            LinkedAt: now,
            LastSyncedAt: null,
            ConsentExpiresAt: null,
            LastWebhookType: null,
            LastWebhookCode: null,
            LastError: null);
        var accounts = request.Accounts.Select(account => new PlaidAccountDto(
            account.PlaidAccountId,
            exchange.ItemId,
            account.Name,
            account.OfficialName,
            MaskOnly(account.Mask),
            account.Type,
            account.Subtype,
            account.PersistentAccountId,
            account.MeridianAccountId,
            account.EntityId,
            VerificationStatus: null)).ToArray();
        await _repository.UpsertItemAsync(item, accounts, ct).ConfigureAwait(false);
        return new PlaidPublicTokenExchangeResult(item, accounts, exchange.RequestId);
    }

    public Task<IReadOnlyList<PlaidItemDto>> ListItemsAsync(CancellationToken ct = default)
        => _repository.ListItemsAsync(ct);

    public Task<IReadOnlyList<PlaidAccountDto>> ListAccountsAsync(string? itemId = null, CancellationToken ct = default)
        => _repository.ListAccountsAsync(itemId, ct);

    public async Task<PlaidSyncResult> SyncItemAsync(PlaidSyncRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var credentials = await ResolveCredentialsAsync(ct).ConfigureAwait(false);
        var item = await _repository.GetItemAsync(request.ItemId, ct).ConfigureAwait(false)
                   ?? throw new InvalidOperationException($"Plaid item '{request.ItemId}' is not linked.");
        var accessToken = await ResolveAccessTokenAsync(item, ct).ConfigureAwait(false);
        var accounts = await _repository.ListAccountsAsync(item.ItemId, ct).ConfigureAwait(false);
        var warnings = new List<string>();
        var balanceCount = 0;
        var bankLineCount = 0;
        var investmentHoldingCount = 0;
        var investmentTransactionCount = 0;
        var identityCount = 0;
        var syncedCursor = item.TransactionsCursor;

        if (request.SyncBalances)
        {
            var balances = await _client.GetBalancesAsync(credentials, accessToken, ct).ConfigureAwait(false);
            balanceCount = await RecordBalancesAsync(accounts, balances, request.RequestedBy, warnings, ct).ConfigureAwait(false);
        }

        if (request.SyncTransactions)
        {
            var transactions = await _client.SyncTransactionsAsync(credentials, accessToken, item.TransactionsCursor, ct).ConfigureAwait(false);
            bankLineCount = await RecordBankStatementLinesAsync(accounts, transactions.Added.Concat(transactions.Modified).ToArray(), request.RequestedBy, warnings, ct)
                .ConfigureAwait(false);
            syncedCursor = transactions.NextCursor;
        }

        if (request.SyncInvestments && _options.EnableInvestments)
        {
            var investments = await _client.GetInvestmentsAsync(credentials, accessToken, ct).ConfigureAwait(false);
            investmentHoldingCount = investments.Holdings.Count;
            investmentTransactionCount = investments.Transactions.Count;
        }
        else if (request.SyncInvestments)
        {
            warnings.Add("Plaid investment sync is disabled by Meridian configuration.");
        }

        if (request.SyncIdentity)
        {
            var identities = await _client.GetIdentityAsync(credentials, accessToken, ct).ConfigureAwait(false);
            identityCount = await UpdateIdentityStatusAsync(item, accounts, identities, ct).ConfigureAwait(false);
        }

        var syncedAt = DateTimeOffset.UtcNow;
        await _repository.UpdateTransactionsCursorAsync(item.ItemId, syncedCursor, syncedAt, ct).ConfigureAwait(false);
        _logger.LogInformation(
            "Plaid item {ItemId} synced with {BalanceCount} balance snapshots and {BankLineCount} bank lines",
            item.ItemId,
            balanceCount,
            bankLineCount);
        return new PlaidSyncResult(
            item.ItemId,
            balanceCount,
            bankLineCount,
            investmentHoldingCount,
            investmentTransactionCount,
            identityCount,
            syncedAt,
            warnings);
    }

    public async Task<PlaidWebhookEventDto> RecordWebhookAsync(PlaidWebhookEventDto webhook, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(webhook);
        var added = await _repository.TryAppendWebhookAsync(webhook, ct).ConfigureAwait(false);
        return webhook with { Duplicate = !added };
    }

    public async Task<PlaidTransferResult> CreateSandboxTransferAsync(PlaidTransferRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_options.AllowsTransferCreation)
        {
            return Blocked("Plaid transfer creation is disabled. Enable sandbox/development transfers explicitly before using this route.", request);
        }

        if (_options.Environment == PlaidEnvironmentDto.Production && !_options.EnableLiveTransfers)
        {
            return Blocked("Plaid live transfers are blocked until live-transfer readiness is explicitly enabled.", request);
        }

        if (_bankingService is null)
        {
            return Blocked("Banking service is not registered; Meridian payment approval cannot be verified.", request);
        }

        var payment = await _bankingService.GetPaymentAsync(request.PendingPaymentId, ct).ConfigureAwait(false);
        if (payment is null)
        {
            return Blocked("Approved Meridian payment evidence was not found for the requested payment id.", request);
        }

        if (payment.EntityId != request.EntityId)
        {
            return Blocked("Approved Meridian payment evidence does not belong to the requested entity.", request);
        }

        if (payment.Status != PaymentApprovalStatus.Approved)
        {
            return Blocked("Plaid transfer authorization requires an approved Meridian payment.", request);
        }

        if (payment.Amount != request.Amount)
        {
            return Blocked("Plaid transfer authorization amount must match the approved Meridian payment.", request);
        }

        if (!string.Equals(request.Currency, "USD", StringComparison.OrdinalIgnoreCase))
        {
            return Blocked("Plaid transfer authorization is currently limited to USD.", request);
        }

        var item = await _repository.GetItemAsync(request.ItemId, ct).ConfigureAwait(false)
                   ?? throw new InvalidOperationException($"Plaid item '{request.ItemId}' is not linked.");
        var accessToken = await ResolveAccessTokenAsync(item, ct).ConfigureAwait(false);
        var credentials = await ResolveCredentialsAsync(ct).ConfigureAwait(false);
        var authorization = await _client.AuthorizeTransferAsync(credentials, accessToken, request, ct).ConfigureAwait(false);
        if (!authorization.Approved)
        {
            var rejected = new PlaidTransferResult(
                PlaidTransferStatusDto.Rejected,
                authorization.DecisionRationale ?? "Plaid transfer authorization was rejected.",
                authorization.AuthorizationId,
                PlaidAccountId: request.PlaidAccountId,
                PendingPaymentId: request.PendingPaymentId,
                CreatedAt: DateTimeOffset.UtcNow);
            await _repository.RecordTransferAsync(rejected, ct).ConfigureAwait(false);
            return rejected;
        }

        var transfer = await _client.CreateTransferAsync(credentials, accessToken, request, authorization.AuthorizationId, ct).ConfigureAwait(false);
        var result = new PlaidTransferResult(
            PlaidTransferStatusDto.Created,
            $"Plaid sandbox transfer created with status '{transfer.Status}'.",
            authorization.AuthorizationId,
            transfer.TransferId,
            request.PlaidAccountId,
            request.PendingPaymentId,
            DateTimeOffset.UtcNow);
        await _repository.RecordTransferAsync(result, ct).ConfigureAwait(false);
        return result;
    }

    private async Task<PlaidClientCredentials> ResolveCredentialsAsync(CancellationToken ct)
    {
        var read = await _credentialStore.ReadForProviderAsync(ProviderId, ct).ConfigureAwait(false);
        var clientId = FirstNonBlank(read?.Get("ClientId"), _options.ClientId);
        var secret = FirstNonBlank(read?.Get("Secret"), _options.Secret);
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(secret))
        {
            throw new InvalidOperationException("Plaid client credentials are missing. Configure ClientId and Secret for provider 'plaid'.");
        }

        var environment = ParseEnvironment(read?.Environment ?? _options.Environment.ToString());
        return new PlaidClientCredentials(clientId, secret, environment);
    }

    private async Task<string> ResolveAccessTokenAsync(PlaidItemDto item, CancellationToken ct)
    {
        var read = await _credentialStore.ReadForProviderAsync(ProviderId, ct).ConfigureAwait(false);
        var token = read?.Get(item.AccessTokenKey);
        return string.IsNullOrWhiteSpace(token)
            ? throw new InvalidOperationException($"Plaid access token for item '{item.ItemId}' is missing from the encrypted credential store.")
            : token;
    }

    private async Task<int> RecordBalancesAsync(
        IReadOnlyList<PlaidAccountDto> accounts,
        IReadOnlyList<PlaidBalanceDto> balances,
        string actor,
        List<string> warnings,
        CancellationToken ct)
    {
        if (_fundAccountService is null)
        {
            warnings.Add("Fund account service is not registered; Plaid balances were not persisted.");
            return 0;
        }

        var accountLookup = accounts.ToDictionary(static account => account.PlaidAccountId, StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var balance in balances)
        {
            if (!accountLookup.TryGetValue(balance.PlaidAccountId, out var account) || account.MeridianAccountId is not Guid meridianAccountId)
            {
                continue;
            }

            await _fundAccountService.RecordBalanceSnapshotAsync(
                    new RecordAccountBalanceSnapshotRequest(
                        meridianAccountId,
                        DateOnly.FromDateTime(balance.AsOf.UtcDateTime),
                        balance.Currency,
                        balance.Current ?? balance.Available ?? 0m,
                        balance.IsFresh ? "PlaidBalance" : "PlaidCachedBalance",
                        actor,
                        ExternalReference: balance.PlaidAccountId),
                    ct)
                .ConfigureAwait(false);
            count++;
        }

        return count;
    }

    private async Task<int> RecordBankStatementLinesAsync(
        IReadOnlyList<PlaidAccountDto> accounts,
        IReadOnlyList<PlaidTransactionDto> transactions,
        string actor,
        List<string> warnings,
        CancellationToken ct)
    {
        if (_fundAccountService is null)
        {
            warnings.Add("Fund account service is not registered; Plaid transactions were not persisted.");
            return 0;
        }

        var accountLookup = accounts.ToDictionary(static account => account.PlaidAccountId, StringComparer.OrdinalIgnoreCase);
        var grouped = transactions
            .Where(transaction => accountLookup.TryGetValue(transaction.PlaidAccountId, out var account) && account.MeridianAccountId is not null)
            .GroupBy(transaction => accountLookup[transaction.PlaidAccountId].MeridianAccountId!.Value)
            .ToArray();
        var total = 0;
        foreach (var group in grouped)
        {
            var batchId = DeterministicGuid($"plaid-bank-batch:{group.Key}:{string.Join('|', group.Select(static transaction => transaction.TransactionId).Order(StringComparer.OrdinalIgnoreCase))}");
            var lines = group.Select(transaction => new BankStatementLineDto(
                DeterministicGuid($"plaid-bank-line:{transaction.TransactionId}"),
                batchId,
                group.Key,
                transaction.Date,
                transaction.AuthorizedDate ?? transaction.Date,
                -transaction.Amount,
                transaction.Currency,
                transaction.TransactionType ?? transaction.PaymentChannel ?? "PlaidTransaction",
                transaction.MerchantName ?? transaction.Name,
                transaction.TransactionId,
                ClosingBalance: null)).ToArray();
            await _fundAccountService.IngestBankStatementAsync(
                    new IngestBankStatementRequest(
                        batchId,
                        group.Key,
                        lines.Max(static line => line.StatementDate),
                        "Plaid",
                        "Plaid transactions/sync import",
                        lines,
                        actor,
                        IsBackfill: true),
                    ct)
                .ConfigureAwait(false);
            total += lines.Length;
        }

        return total;
    }

    private async Task<int> UpdateIdentityStatusAsync(
        PlaidItemDto item,
        IReadOnlyList<PlaidAccountDto> accounts,
        IReadOnlyList<PlaidIdentitySnapshot> identities,
        CancellationToken ct)
    {
        var identityLookup = identities.ToDictionary(static identity => identity.PlaidAccountId, StringComparer.OrdinalIgnoreCase);
        var updated = accounts.Select(account =>
            identityLookup.TryGetValue(account.PlaidAccountId, out var identity)
                ? account with { VerificationStatus = identity.VerificationStatus }
                : account).ToArray();
        await _repository.UpsertItemAsync(item, updated, ct).ConfigureAwait(false);
        return identities.Count;
    }

    private string? BuildWebhookUrl()
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookBaseUrl))
        {
            return null;
        }

        return $"{_options.WebhookBaseUrl.TrimEnd('/')}/api/plaid/webhook";
    }

    private static PlaidTransferResult Blocked(string message, PlaidTransferRequest request)
        => new(
            PlaidTransferStatusDto.Blocked,
            message,
            PlaidAccountId: request.PlaidAccountId,
            PendingPaymentId: request.PendingPaymentId,
            CreatedAt: DateTimeOffset.UtcNow);

    private static string? MaskOnly(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[^Math.Min(value.Trim().Length, 4)..];

    private static string? FirstNonBlank(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));

    private static PlaidEnvironmentDto ParseEnvironment(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "production" => PlaidEnvironmentDto.Production,
            "development" => PlaidEnvironmentDto.Development,
            _ => PlaidEnvironmentDto.Sandbox
        };

    private static Guid DeterministicGuid(string value)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes);
    }
}
