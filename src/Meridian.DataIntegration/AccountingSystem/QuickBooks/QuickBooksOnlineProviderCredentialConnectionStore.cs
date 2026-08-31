using Meridian.Contracts.AccountingSystem;
using Meridian.Contracts.Configuration;
using Meridian.DataIntegration.Credentials;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.DataIntegration.AccountingSystem.QuickBooks;

public sealed class QuickBooksOnlineProviderCredentialConnectionStore : IQuickBooksOnlineConnectionStore
{
    public const string ClientIdField = "ClientId";
    public const string ClientSecretField = "ClientSecret";
    public const string RefreshTokenField = "RefreshToken";
    public const string RealmIdField = "RealmId";
    public const string CompanyNameField = "CompanyName";

    private readonly IProviderCredentialStore _credentialStore;

    public QuickBooksOnlineProviderCredentialConnectionStore(IProviderCredentialStore credentialStore)
    {
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
    }

    public async Task<QuickBooksOnlineConnection?> ReadAsync(CancellationToken ct = default)
    {
        var read = await _credentialStore.ReadForProviderAsync(QuickBooksOnlineAccountingProvider.Id, ct)
            .ConfigureAwait(false);
        if (read is null)
        {
            return null;
        }

        var clientId = read.Get(ClientIdField);
        var clientSecret = read.Get(ClientSecretField);
        var refreshToken = read.Get(RefreshTokenField);
        var realmId = read.Get(RealmIdField);
        if (string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret) ||
            string.IsNullOrWhiteSpace(refreshToken) ||
            string.IsNullOrWhiteSpace(realmId))
        {
            return null;
        }

        return new QuickBooksOnlineConnection(
            clientId.Trim(),
            clientSecret.Trim(),
            refreshToken.Trim(),
            realmId.Trim(),
            NormalizeEnvironment(read.Environment),
            NormalizeOptional(read.Get(CompanyNameField)));
    }

    public async Task<AccountingSystemConnectionMetadataDto> GetMetadataAsync(CancellationToken ct = default)
    {
        var status = await _credentialStore.GetStatusAsync(QuickBooksOnlineAccountingProvider.Id, ct)
            .ConfigureAwait(false);
        var read = await _credentialStore.ReadForProviderAsync(QuickBooksOnlineAccountingProvider.Id, ct)
            .ConfigureAwait(false);
        var hasConfig = status.CredentialState is
            ProviderCredentialStateDto.Configured or
            ProviderCredentialStateDto.Verified;
        var realmId = read?.Get(RealmIdField);
        var companyName = read?.Get(CompanyNameField);

        return new AccountingSystemConnectionMetadataDto(
            QuickBooksOnlineAccountingProvider.Id,
            NormalizeEnvironment(status.Environment),
            NormalizeOptional(realmId),
            NormalizeOptional(companyName),
            HasLocalConfig: hasConfig,
            HasRefreshToken: !string.IsNullOrWhiteSpace(read?.Get(RefreshTokenField)),
            LastConnectedAtUtc: status.LastSuccessfulAt,
            StatusLabel: BuildStatusLabel(status),
            StatusDetail: BuildStatusDetail(status, realmId, companyName),
            MissingFields: status.MissingFields);
    }

    public Task SaveRefreshTokenAsync(string refreshToken, CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(refreshToken)
            ? Task.CompletedTask
            : _credentialStore.SaveAsync(
                new ProviderCredentialSaveRequest(
                    QuickBooksOnlineAccountingProvider.Id,
                    new Dictionary<string, string?> { [RefreshTokenField] = refreshToken.Trim() },
                    Actor: "quickbooks-token-exchange"),
                ct);

    public Task RecordConnectionAsync(
        bool success,
        string? externalCompanyId,
        string? error,
        DateTimeOffset? occurredAtUtc = null,
        CancellationToken ct = default)
        => _credentialStore.RecordVerificationAsync(
            new ProviderCredentialVerificationUpdate(
                QuickBooksOnlineAccountingProvider.Id,
                success,
                ErrorMessage: error,
                ExternalAccountId: externalCompanyId,
                VerifiedAt: occurredAtUtc ?? DateTimeOffset.UtcNow,
                Actor: "quickbooks-read-only-import"),
            ct);

    private static string NormalizeEnvironment(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "production" or "live" ? "production" : "sandbox";
    }

    private static string BuildStatusLabel(ProviderCredentialStoreStatus status)
        => status.CredentialState switch
        {
            ProviderCredentialStateDto.Verified => "Company verified",
            ProviderCredentialStateDto.Configured => "Local config ready",
            ProviderCredentialStateDto.Invalid => "OAuth repair required",
            ProviderCredentialStateDto.Partial => "Local config incomplete",
            ProviderCredentialStateDto.Missing => "Local config required",
            _ => "Review required"
        };

    private static string BuildStatusDetail(
        ProviderCredentialStoreStatus status,
        string? realmId,
        string? companyName)
    {
        if (status.CredentialState is ProviderCredentialStateDto.Missing or ProviderCredentialStateDto.Partial)
        {
            return "Add QuickBooks client ID, client secret, refresh token, and company realm ID before importing read-only GL evidence.";
        }

        if (status.CredentialState == ProviderCredentialStateDto.Invalid)
        {
            return status.LastError ?? "Refresh token exchange failed. Re-authorize the Meridian app in Intuit Developer and save the rotated token.";
        }

        var selectedCompany = !string.IsNullOrWhiteSpace(companyName)
            ? companyName.Trim()
            : !string.IsNullOrWhiteSpace(realmId)
                ? $"realm {realmId.Trim()}"
                : "selected company";
        return $"Read-only QuickBooks Online evidence is configured for {selectedCompany}; posting/export remains disabled.";
    }
}
