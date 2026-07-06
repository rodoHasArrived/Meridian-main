using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Meridian.Contracts.Plaid;
using Meridian.Infrastructure.Utilities;

namespace Meridian.Infrastructure.Adapters.Plaid;

public sealed class PlaidHttpClient : IPlaidClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;

    public PlaidHttpClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<PlaidLinkTokenResponse> CreateLinkTokenAsync(
        PlaidClientCredentials credentials,
        PlaidLinkTokenRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var products = ResolveProducts(request.Products);
        var payload = new
        {
            client_id = credentials.ClientId,
            secret = credentials.Secret,
            client_name = string.IsNullOrWhiteSpace(request.ClientName) ? "Meridian" : request.ClientName,
            language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language,
            country_codes = request.CountryCodes is { Count: > 0 } ? request.CountryCodes : ["US"],
            user = new { client_user_id = request.UserId },
            products = products.Select(ToPlaidProduct).ToArray(),
            webhook = request.WebhookUrl,
            institution_id = string.IsNullOrWhiteSpace(request.InstitutionId) ? null : request.InstitutionId
        };

        var response = await PostAsync<LinkTokenCreateResponse>(credentials, "/link/token/create", payload, ct)
            .ConfigureAwait(false);
        return new PlaidLinkTokenResponse(
            response.LinkToken,
            response.Expiration,
            response.RequestId,
            products,
            request.InstitutionId,
            request.InstitutionName,
            credentials.Environment);
    }

    public async Task<PlaidInstitutionSearchResult> SearchInstitutionsAsync(
        PlaidClientCredentials credentials,
        string query,
        IReadOnlyList<PlaidProductDto>? products = null,
        IReadOnlyList<string>? countryCodes = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var requestedProducts = ResolveProducts(products);
        var requestedCountryCodes = countryCodes is { Count: > 0 } ? countryCodes : ["US"];
        var response = await PostAsync<InstitutionsSearchResponse>(
                credentials,
                "/institutions/search",
                new
                {
                    client_id = credentials.ClientId,
                    secret = credentials.Secret,
                    query = query.Trim(),
                    products = requestedProducts.Select(ToPlaidProduct).ToArray(),
                    country_codes = requestedCountryCodes
                },
                ct)
            .ConfigureAwait(false);
        return new PlaidInstitutionSearchResult(
            query.Trim(),
            response.Institutions.Select(MapInstitution).ToArray(),
            response.RequestId);
    }

    public async Task<(string ItemId, string AccessToken, string? RequestId)> ExchangePublicTokenAsync(
        PlaidClientCredentials credentials,
        string publicToken,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicToken);
        var response = await PostAsync<ItemPublicTokenExchangeResponse>(
                credentials,
                "/item/public_token/exchange",
                new
                {
                    client_id = credentials.ClientId,
                    secret = credentials.Secret,
                    public_token = publicToken
                },
                ct)
            .ConfigureAwait(false);
        return (response.ItemId, response.AccessToken, response.RequestId);
    }

    public async Task<IReadOnlyList<PlaidBalanceDto>> GetBalancesAsync(
        PlaidClientCredentials credentials,
        string accessToken,
        CancellationToken ct = default)
    {
        var response = await PostAsync<AccountsGetResponse>(
                credentials,
                "/accounts/balance/get",
                AuthenticatedPayload(credentials, accessToken),
                ct)
            .ConfigureAwait(false);
        var asOf = DateTimeOffset.UtcNow;
        return response.Accounts.Select(account => new PlaidBalanceDto(
            account.AccountId,
            account.Balances.Available,
            account.Balances.Current,
            account.Balances.IsoCurrencyCode ?? account.Balances.UnofficialCurrencyCode ?? "USD",
            asOf,
            IsFresh: true)).ToArray();
    }

    public async Task<PlaidTransactionsSyncResult> SyncTransactionsAsync(
        PlaidClientCredentials credentials,
        string accessToken,
        string? cursor,
        CancellationToken ct = default)
    {
        var response = await PostAsync<TransactionsSyncResponse>(
                credentials,
                "/transactions/sync",
                new
                {
                    client_id = credentials.ClientId,
                    secret = credentials.Secret,
                    access_token = accessToken,
                    cursor,
                    count = 500
                },
                ct)
            .ConfigureAwait(false);
        return new PlaidTransactionsSyncResult(
            response.NextCursor,
            response.HasMore,
            response.Added.Select(MapTransaction).ToArray(),
            response.Modified.Select(MapTransaction).ToArray(),
            response.Removed.Select(static removed => removed.TransactionId).ToArray(),
            response.RequestId);
    }

    public async Task<PlaidInvestmentsSnapshot> GetInvestmentsAsync(
        PlaidClientCredentials credentials,
        string accessToken,
        CancellationToken ct = default)
    {
        var holdings = await PostAsync<InvestmentsHoldingsGetResponse>(
                credentials,
                "/investments/holdings/get",
                AuthenticatedPayload(credentials, accessToken),
                ct)
            .ConfigureAwait(false);
        var transactions = await PostAsync<InvestmentsTransactionsGetResponse>(
                credentials,
                "/investments/transactions/get",
                new
                {
                    client_id = credentials.ClientId,
                    secret = credentials.Secret,
                    access_token = accessToken,
                    start_date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90)).ToString("yyyy-MM-dd"),
                    end_date = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd")
                },
                ct)
            .ConfigureAwait(false);
        var securities = holdings.Securities.ToDictionary(static s => s.SecurityId, StringComparer.OrdinalIgnoreCase);
        var asOf = DateTimeOffset.UtcNow;
        return new PlaidInvestmentsSnapshot(
            holdings.Holdings.Select(holding =>
            {
                securities.TryGetValue(holding.SecurityId, out var security);
                return new PlaidInvestmentHoldingDto(
                    holding.AccountId,
                    holding.SecurityId,
                    security?.TickerSymbol,
                    security?.Name,
                    holding.Quantity,
                    holding.InstitutionValue,
                    holding.IsoCurrencyCode ?? "USD",
                    asOf);
            }).ToArray(),
            transactions.InvestmentTransactions.Select(transaction => new PlaidInvestmentTransactionDto(
                transaction.InvestmentTransactionId,
                transaction.AccountId,
                transaction.SecurityId,
                ParseDate(transaction.Date) ?? DateOnly.FromDateTime(DateTime.UtcNow),
                transaction.Name,
                transaction.Type,
                transaction.Quantity ?? 0m,
                transaction.Amount,
                transaction.IsoCurrencyCode ?? "USD")).ToArray(),
            holdings.RequestId ?? transactions.RequestId);
    }

    public async Task<IReadOnlyList<PlaidIdentitySnapshot>> GetIdentityAsync(
        PlaidClientCredentials credentials,
        string accessToken,
        CancellationToken ct = default)
    {
        var response = await PostAsync<IdentityGetResponse>(
                credentials,
                "/identity/get",
                AuthenticatedPayload(credentials, accessToken),
                ct)
            .ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        return response.Accounts.Select(account =>
        {
            var owner = account.Owners.FirstOrDefault();
            return new PlaidIdentitySnapshot(
                account.AccountId,
                owner?.Names.FirstOrDefault(),
                owner?.Emails.FirstOrDefault()?.Data,
                owner?.PhoneNumbers.FirstOrDefault()?.Data,
                string.IsNullOrWhiteSpace(owner?.Names.FirstOrDefault()) ? "unverified" : "verified",
                now);
        }).ToArray();
    }

    public async Task<(string AuthorizationId, bool Approved, string? DecisionRationale)> AuthorizeTransferAsync(
        PlaidClientCredentials credentials,
        string accessToken,
        PlaidTransferRequest request,
        CancellationToken ct = default)
    {
        var response = await PostAsync<TransferAuthorizationCreateResponse>(
                credentials,
                "/transfer/authorization/create",
                new
                {
                    client_id = credentials.ClientId,
                    secret = credentials.Secret,
                    access_token = accessToken,
                    account_id = request.PlaidAccountId,
                    type = request.Direction,
                    network = "ach",
                    amount = request.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                    ach_class = request.SecCode,
                    user = new { legal_name = request.RequestedBy }
                },
                ct)
            .ConfigureAwait(false);
        return (response.Authorization.Id, response.Authorization.Decision == "approved", response.Authorization.DecisionRationale?.Description);
    }

    public async Task<(string TransferId, string Status)> CreateTransferAsync(
        PlaidClientCredentials credentials,
        string accessToken,
        PlaidTransferRequest request,
        string authorizationId,
        CancellationToken ct = default)
    {
        var response = await PostAsync<TransferCreateResponse>(
                credentials,
                "/transfer/create",
                new
                {
                    client_id = credentials.ClientId,
                    secret = credentials.Secret,
                    access_token = accessToken,
                    account_id = request.PlaidAccountId,
                    authorization_id = authorizationId,
                    description = $"Meridian payment {request.PendingPaymentId:N}"
                },
                ct)
            .ConfigureAwait(false);
        return (response.Transfer.Id, response.Transfer.Status);
    }

    private async Task<T> PostAsync<T>(
        PlaidClientCredentials credentials,
        string path,
        object payload,
        CancellationToken ct)
    {
        using var response = await _httpClient.PostAsJsonAsync(BuildUri(credentials, path), payload, JsonOptions, ct)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
        return result ?? throw new InvalidOperationException($"Plaid endpoint '{path}' returned an empty response.");
    }

    private static Uri BuildUri(PlaidClientCredentials credentials, string path)
    {
        var host = credentials.Environment switch
        {
            PlaidEnvironmentDto.Production => "https://production.plaid.com",
            PlaidEnvironmentDto.Development => "https://development.plaid.com",
            _ => "https://sandbox.plaid.com"
        };
        return new Uri($"{host}{path}", UriKind.Absolute);
    }

    private static object AuthenticatedPayload(PlaidClientCredentials credentials, string accessToken)
        => new
        {
            client_id = credentials.ClientId,
            secret = credentials.Secret,
            access_token = accessToken
        };

    private static PlaidTransactionDto MapTransaction(PlaidTransaction transaction)
        => new(
            transaction.TransactionId,
            transaction.AccountId,
            ParseDate(transaction.Date) ?? DateOnly.FromDateTime(DateTime.UtcNow),
            ParseDate(transaction.AuthorizedDate),
            transaction.Amount,
            transaction.IsoCurrencyCode ?? transaction.UnofficialCurrencyCode ?? "USD",
            transaction.Name,
            transaction.MerchantName,
            transaction.Category is { Length: > 0 } ? string.Join(" / ", transaction.Category) : null,
            transaction.PaymentChannel,
            transaction.Pending,
            transaction.TransactionType);

    private static IReadOnlyList<PlaidProductDto> ResolveProducts(IReadOnlyList<PlaidProductDto>? products)
        => products is { Count: > 0 }
            ? products.Distinct().ToArray()
            : [PlaidProductDto.Transactions, PlaidProductDto.Auth, PlaidProductDto.Identity];

    private static PlaidInstitutionDto MapInstitution(PlaidInstitution institution)
        => new(
            institution.InstitutionId,
            institution.Name,
            institution.Products.Select(ParseProduct).Where(static product => product is not null).Select(static product => product!.Value).Distinct().ToArray(),
            institution.CountryCodes,
            institution.Url,
            institution.PrimaryColor,
            institution.Logo);

    private static PlaidProductDto? ParseProduct(string value)
        => Enum.TryParse<PlaidProductDto>(value, ignoreCase: true, out var parsed) ? parsed : null;

    private static string ToPlaidProduct(PlaidProductDto product)
        => product.ToString().ToLowerInvariant();

    private static DateOnly? ParseDate(string? value)
        => ProviderDateParsing.ParseProviderDateOrNull(value);

    private sealed record LinkTokenCreateResponse(
        [property: JsonPropertyName("link_token")] string LinkToken,
        [property: JsonPropertyName("expiration")] DateTimeOffset? Expiration,
        [property: JsonPropertyName("request_id")] string? RequestId);

    private sealed record ItemPublicTokenExchangeResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("item_id")] string ItemId,
        [property: JsonPropertyName("request_id")] string? RequestId);

    private sealed record InstitutionsSearchResponse(
        [property: JsonPropertyName("institutions")] PlaidInstitution[] Institutions,
        [property: JsonPropertyName("request_id")] string? RequestId);

    private sealed record PlaidInstitution(
        [property: JsonPropertyName("institution_id")] string InstitutionId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("products")] string[] Products,
        [property: JsonPropertyName("country_codes")] string[] CountryCodes,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("primary_color")] string? PrimaryColor,
        [property: JsonPropertyName("logo")] string? Logo);

    private sealed record AccountsGetResponse(
        [property: JsonPropertyName("accounts")] PlaidAccountResponse[] Accounts);

    private sealed record PlaidAccountResponse(
        [property: JsonPropertyName("account_id")] string AccountId,
        [property: JsonPropertyName("balances")] PlaidBalanceResponse Balances,
        [property: JsonPropertyName("owners")] PlaidOwnerResponse[] Owners);

    private sealed record PlaidBalanceResponse(
        [property: JsonPropertyName("available")] decimal? Available,
        [property: JsonPropertyName("current")] decimal? Current,
        [property: JsonPropertyName("iso_currency_code")] string? IsoCurrencyCode,
        [property: JsonPropertyName("unofficial_currency_code")] string? UnofficialCurrencyCode);

    private sealed record TransactionsSyncResponse(
        [property: JsonPropertyName("added")] PlaidTransaction[] Added,
        [property: JsonPropertyName("modified")] PlaidTransaction[] Modified,
        [property: JsonPropertyName("removed")] PlaidRemovedTransaction[] Removed,
        [property: JsonPropertyName("next_cursor")] string? NextCursor,
        [property: JsonPropertyName("has_more")] bool HasMore,
        [property: JsonPropertyName("request_id")] string? RequestId);

    private sealed record PlaidTransaction(
        [property: JsonPropertyName("transaction_id")] string TransactionId,
        [property: JsonPropertyName("account_id")] string AccountId,
        [property: JsonPropertyName("date")] string Date,
        [property: JsonPropertyName("authorized_date")] string? AuthorizedDate,
        [property: JsonPropertyName("amount")] decimal Amount,
        [property: JsonPropertyName("iso_currency_code")] string? IsoCurrencyCode,
        [property: JsonPropertyName("unofficial_currency_code")] string? UnofficialCurrencyCode,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("merchant_name")] string? MerchantName,
        [property: JsonPropertyName("category")] string[]? Category,
        [property: JsonPropertyName("payment_channel")] string? PaymentChannel,
        [property: JsonPropertyName("pending")] bool Pending,
        [property: JsonPropertyName("transaction_type")] string? TransactionType);

    private sealed record PlaidRemovedTransaction(
        [property: JsonPropertyName("transaction_id")] string TransactionId);

    private sealed record InvestmentsHoldingsGetResponse(
        [property: JsonPropertyName("holdings")] PlaidHolding[] Holdings,
        [property: JsonPropertyName("securities")] PlaidSecurity[] Securities,
        [property: JsonPropertyName("request_id")] string? RequestId);

    private sealed record PlaidHolding(
        [property: JsonPropertyName("account_id")] string AccountId,
        [property: JsonPropertyName("security_id")] string SecurityId,
        [property: JsonPropertyName("quantity")] decimal Quantity,
        [property: JsonPropertyName("institution_value")] decimal? InstitutionValue,
        [property: JsonPropertyName("iso_currency_code")] string? IsoCurrencyCode);

    private sealed record PlaidSecurity(
        [property: JsonPropertyName("security_id")] string SecurityId,
        [property: JsonPropertyName("ticker_symbol")] string? TickerSymbol,
        [property: JsonPropertyName("name")] string? Name);

    private sealed record InvestmentsTransactionsGetResponse(
        [property: JsonPropertyName("investment_transactions")] PlaidInvestmentTransaction[] InvestmentTransactions,
        [property: JsonPropertyName("request_id")] string? RequestId);

    private sealed record PlaidInvestmentTransaction(
        [property: JsonPropertyName("investment_transaction_id")] string InvestmentTransactionId,
        [property: JsonPropertyName("account_id")] string AccountId,
        [property: JsonPropertyName("security_id")] string? SecurityId,
        [property: JsonPropertyName("date")] string Date,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("quantity")] decimal? Quantity,
        [property: JsonPropertyName("amount")] decimal Amount,
        [property: JsonPropertyName("iso_currency_code")] string? IsoCurrencyCode);

    private sealed record IdentityGetResponse(
        [property: JsonPropertyName("accounts")] PlaidAccountResponse[] Accounts);

    private sealed record PlaidOwnerResponse(
        [property: JsonPropertyName("names")] string[] Names,
        [property: JsonPropertyName("emails")] PlaidContactPoint[] Emails,
        [property: JsonPropertyName("phone_numbers")] PlaidContactPoint[] PhoneNumbers);

    private sealed record PlaidContactPoint(
        [property: JsonPropertyName("data")] string? Data);

    private sealed record TransferAuthorizationCreateResponse(
        [property: JsonPropertyName("authorization")] TransferAuthorization Authorization);

    private sealed record TransferAuthorization(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("decision")] string Decision,
        [property: JsonPropertyName("decision_rationale")] TransferDecisionRationale? DecisionRationale);

    private sealed record TransferDecisionRationale(
        [property: JsonPropertyName("description")] string? Description);

    private sealed record TransferCreateResponse(
        [property: JsonPropertyName("transfer")] TransferResponse Transfer);

    private sealed record TransferResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("status")] string Status);
}
