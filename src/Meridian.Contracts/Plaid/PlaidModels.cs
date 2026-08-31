using System.Text.Json.Serialization;
using Meridian.Contracts.FundStructure;

namespace Meridian.Contracts.Plaid;

[JsonConverter(typeof(JsonStringEnumConverter<PlaidEnvironmentDto>))]
public enum PlaidEnvironmentDto
{
    Sandbox,
    Development,
    Production
}

[JsonConverter(typeof(JsonStringEnumConverter<PlaidProductDto>))]
public enum PlaidProductDto
{
    Auth,
    Identity,
    Transactions,
    Investments,
    Transfer
}

public enum PlaidItemStatusDto
{
    Linked,
    RequiresUpdate,
    Revoked,
    Error
}

public enum PlaidTransferStatusDto
{
    Disabled,
    PendingAuthorization,
    Authorized,
    Created,
    Rejected,
    Blocked
}

public sealed record PlaidOptions(
    PlaidEnvironmentDto Environment = PlaidEnvironmentDto.Sandbox,
    string? ClientId = null,
    string? Secret = null,
    string? WebhookBaseUrl = null,
    string? WebhookVerificationKeyPem = null,
    string? WebhookVerificationKeyId = null,
    bool EnableTransfers = false,
    bool EnableInvestments = true,
    IReadOnlyList<PlaidProductDto>? DefaultProducts = null,
    bool EnableLiveTransfers = false)
{
    public static PlaidOptions Default => new(
        Environment: PlaidEnvironmentDto.Sandbox,
        DefaultProducts:
        [
            PlaidProductDto.Transactions,
            PlaidProductDto.Auth,
            PlaidProductDto.Identity,
            PlaidProductDto.Investments
        ]);

    /// <summary>
    /// True when an ES256 public key is configured for verifying the <c>Plaid-Verification</c>
    /// header on inbound webhooks. When false the webhook route fails closed: an unverifiable
    /// callback is refused rather than recorded, because the payload hash a webhook carries
    /// authenticates nothing when the caller supplies the payload.
    /// </summary>
    public bool CanVerifyWebhooks => !string.IsNullOrWhiteSpace(WebhookVerificationKeyPem);

    public bool AllowsTransferCreation =>
        EnableTransfers &&
        (Environment is PlaidEnvironmentDto.Sandbox or PlaidEnvironmentDto.Development || EnableLiveTransfers);
}

public sealed record PlaidClientCredentials(
    string ClientId,
    string Secret,
    PlaidEnvironmentDto Environment);

public sealed record PlaidLinkTokenRequest(
    // Optional client hint only: the endpoint always rewrites this with the authenticated
    // session actor (PlaidEndpoints.ResolveActor), so browsers may omit it entirely.
    string? UserId = null,
    Guid? MeridianAccountId = null,
    IReadOnlyList<PlaidProductDto>? Products = null,
    string? WebhookUrl = null,
    string? ClientName = null,
    string? Language = "en",
    IReadOnlyList<string>? CountryCodes = null,
    string? InstitutionId = null,
    string? InstitutionName = null);

public sealed record PlaidLinkTokenResponse(
    string LinkToken,
    DateTimeOffset? Expiration,
    string? RequestId,
    IReadOnlyList<PlaidProductDto> Products,
    string? InstitutionId = null,
    string? InstitutionName = null,
    PlaidEnvironmentDto? Environment = null);

public sealed record PlaidInstitutionDto(
    string InstitutionId,
    string Name,
    IReadOnlyList<PlaidProductDto> Products,
    IReadOnlyList<string> CountryCodes,
    string? Url,
    string? PrimaryColor,
    string? Logo);

public sealed record PlaidInstitutionSearchResult(
    string Query,
    IReadOnlyList<PlaidInstitutionDto> Institutions,
    string? RequestId);

public sealed record PlaidAccountLinkRequest(
    string PlaidAccountId,
    string Name,
    string? OfficialName,
    string? Mask,
    string Type,
    string? Subtype,
    string? PersistentAccountId = null,
    Guid? MeridianAccountId = null,
    Guid? EntityId = null);

public sealed record PlaidPublicTokenExchangeRequest(
    string PublicToken,
    string? InstitutionId,
    string? InstitutionName,
    IReadOnlyList<PlaidAccountLinkRequest> Accounts,
    // Optional client hint only: the endpoint always rewrites this with the authenticated
    // session actor (PlaidEndpoints.ResolveActor), so browsers may omit it entirely.
    string? RequestedBy = null);

public sealed record PlaidPublicTokenExchangeResult(
    PlaidItemDto Item,
    IReadOnlyList<PlaidAccountDto> Accounts,
    string? RequestId);

public sealed record PlaidItemDto(
    string ItemId,
    string InstitutionId,
    string InstitutionName,
    PlaidEnvironmentDto Environment,
    PlaidItemStatusDto Status,
    string AccessTokenKey,
    string? TransactionsCursor,
    DateTimeOffset LinkedAt,
    DateTimeOffset? LastSyncedAt,
    DateTimeOffset? ConsentExpiresAt,
    string? LastWebhookType,
    string? LastWebhookCode,
    string? LastError);

public sealed record PlaidAccountDto(
    string PlaidAccountId,
    string ItemId,
    string Name,
    string? OfficialName,
    string? Mask,
    string Type,
    string? Subtype,
    string? PersistentAccountId,
    Guid? MeridianAccountId,
    Guid? EntityId,
    string? VerificationStatus);

public sealed record PlaidBalanceDto(
    string PlaidAccountId,
    decimal? Available,
    decimal? Current,
    string Currency,
    DateTimeOffset AsOf,
    bool IsFresh);

public sealed record PlaidTransactionDto(
    string TransactionId,
    string PlaidAccountId,
    DateOnly Date,
    DateOnly? AuthorizedDate,
    decimal Amount,
    string Currency,
    string Name,
    string? MerchantName,
    string? Category,
    string? PaymentChannel,
    bool Pending,
    string? TransactionType);

public sealed record PlaidTransactionsSyncResult(
    string? NextCursor,
    bool HasMore,
    IReadOnlyList<PlaidTransactionDto> Added,
    IReadOnlyList<PlaidTransactionDto> Modified,
    IReadOnlyList<string> RemovedTransactionIds,
    string? RequestId);

public sealed record PlaidInvestmentHoldingDto(
    string PlaidAccountId,
    string SecurityId,
    string? Symbol,
    string? Name,
    decimal Quantity,
    decimal? MarketValue,
    string Currency,
    DateTimeOffset AsOf);

public sealed record PlaidInvestmentTransactionDto(
    string InvestmentTransactionId,
    string PlaidAccountId,
    string? SecurityId,
    DateOnly Date,
    string Name,
    string Type,
    decimal Quantity,
    decimal Amount,
    string Currency);

public sealed record PlaidInvestmentsSnapshot(
    IReadOnlyList<PlaidInvestmentHoldingDto> Holdings,
    IReadOnlyList<PlaidInvestmentTransactionDto> Transactions,
    string? RequestId);

public sealed record PlaidIdentitySnapshot(
    string PlaidAccountId,
    string? OwnerName,
    string? OwnerEmail,
    string? OwnerPhone,
    string VerificationStatus,
    DateTimeOffset VerifiedAt);

public sealed record PlaidSyncRequest(
    string ItemId,
    bool SyncBalances = true,
    bool SyncTransactions = true,
    bool SyncInvestments = true,
    bool SyncIdentity = true,
    string RequestedBy = "plaid-sync");

public sealed record PlaidSyncResult(
    string ItemId,
    int BalanceSnapshotsRecorded,
    int BankStatementLinesIngested,
    int InvestmentHoldingsRecorded,
    int InvestmentTransactionsRecorded,
    int IdentityRecordsUpdated,
    DateTimeOffset SyncedAt,
    IReadOnlyList<string> Warnings);

public sealed record PlaidWebhookEventDto(
    string EventId,
    string ItemId,
    string WebhookType,
    string WebhookCode,
    DateTimeOffset ReceivedAt,
    string? PayloadHash = null,
    bool Duplicate = false);

public sealed record PlaidTransferRequest(
    string ItemId,
    string PlaidAccountId,
    Guid PendingPaymentId,
    Guid EntityId,
    decimal Amount,
    string Currency,
    string Direction,
    string SecCode,
    string RequestedBy);

public sealed record PlaidTransferResult(
    PlaidTransferStatusDto Status,
    string Message,
    string? AuthorizationId = null,
    string? TransferId = null,
    string? PlaidAccountId = null,
    Guid? PendingPaymentId = null,
    DateTimeOffset? CreatedAt = null);

public interface IPlaidClient
{
    Task<PlaidLinkTokenResponse> CreateLinkTokenAsync(
        PlaidClientCredentials credentials,
        PlaidLinkTokenRequest request,
        CancellationToken ct = default);

    Task<PlaidInstitutionSearchResult> SearchInstitutionsAsync(
        PlaidClientCredentials credentials,
        string query,
        IReadOnlyList<PlaidProductDto>? products = null,
        IReadOnlyList<string>? countryCodes = null,
        CancellationToken ct = default);

    Task<(string ItemId, string AccessToken, string? RequestId)> ExchangePublicTokenAsync(
        PlaidClientCredentials credentials,
        string publicToken,
        CancellationToken ct = default);

    Task<IReadOnlyList<PlaidBalanceDto>> GetBalancesAsync(
        PlaidClientCredentials credentials,
        string accessToken,
        CancellationToken ct = default);

    Task<PlaidTransactionsSyncResult> SyncTransactionsAsync(
        PlaidClientCredentials credentials,
        string accessToken,
        string? cursor,
        CancellationToken ct = default);

    Task<PlaidInvestmentsSnapshot> GetInvestmentsAsync(
        PlaidClientCredentials credentials,
        string accessToken,
        CancellationToken ct = default);

    Task<IReadOnlyList<PlaidIdentitySnapshot>> GetIdentityAsync(
        PlaidClientCredentials credentials,
        string accessToken,
        CancellationToken ct = default);

    Task<(string AuthorizationId, bool Approved, string? DecisionRationale)> AuthorizeTransferAsync(
        PlaidClientCredentials credentials,
        string accessToken,
        PlaidTransferRequest request,
        CancellationToken ct = default);

    Task<(string TransferId, string Status)> CreateTransferAsync(
        PlaidClientCredentials credentials,
        string accessToken,
        PlaidTransferRequest request,
        string authorizationId,
        CancellationToken ct = default);
}

public interface IPlaidConnectionRepository
{
    Task<IReadOnlyList<PlaidItemDto>> ListItemsAsync(CancellationToken ct = default);

    Task<PlaidItemDto?> GetItemAsync(string itemId, CancellationToken ct = default);

    Task<IReadOnlyList<PlaidAccountDto>> ListAccountsAsync(string? itemId = null, CancellationToken ct = default);

    Task UpsertItemAsync(PlaidItemDto item, IReadOnlyList<PlaidAccountDto> accounts, CancellationToken ct = default);

    Task UpdateTransactionsCursorAsync(string itemId, string? cursor, DateTimeOffset syncedAt, CancellationToken ct = default);

    Task UpdateItemStatusAsync(
        string itemId,
        PlaidItemStatusDto status,
        string? webhookType,
        string? webhookCode,
        string? error,
        CancellationToken ct = default);

    Task<bool> TryAppendWebhookAsync(PlaidWebhookEventDto webhook, CancellationToken ct = default);

    Task RecordTransferAsync(PlaidTransferResult result, CancellationToken ct = default);
}

public interface IPlaidIngestionService
{
    Task<PlaidLinkTokenResponse> CreateLinkTokenAsync(PlaidLinkTokenRequest request, CancellationToken ct = default);

    Task<PlaidInstitutionSearchResult> SearchInstitutionsAsync(
        string query,
        IReadOnlyList<PlaidProductDto>? products = null,
        IReadOnlyList<string>? countryCodes = null,
        CancellationToken ct = default);

    Task<PlaidPublicTokenExchangeResult> ExchangePublicTokenAsync(PlaidPublicTokenExchangeRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<PlaidItemDto>> ListItemsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<PlaidAccountDto>> ListAccountsAsync(string? itemId = null, CancellationToken ct = default);

    Task<PlaidSyncResult> SyncItemAsync(PlaidSyncRequest request, CancellationToken ct = default);

    Task<PlaidWebhookEventDto> RecordWebhookAsync(PlaidWebhookEventDto webhook, CancellationToken ct = default);
}

public interface IPlaidTransferService
{
    Task<PlaidTransferResult> CreateSandboxTransferAsync(PlaidTransferRequest request, CancellationToken ct = default);
}
