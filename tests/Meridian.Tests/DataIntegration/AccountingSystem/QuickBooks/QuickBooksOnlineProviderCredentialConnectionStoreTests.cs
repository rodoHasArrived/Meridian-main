using FluentAssertions;
using Meridian.Contracts.Configuration;
using Meridian.DataIntegration.AccountingSystem.QuickBooks;
using Meridian.DataIntegration.Credentials;
using Xunit;

namespace Meridian.Tests.DataIntegration.AccountingSystem.QuickBooks;

public sealed class QuickBooksOnlineProviderCredentialConnectionStoreTests
{
    [Fact]
    public async Task ReadAsync_ReturnsNormalizedConnectionFromCompleteProviderCredentials()
    {
        var store = new FakeProviderCredentialStore
        {
            ReadResult = CreateReadResult(
                new Dictionary<string, string>
                {
                    [QuickBooksOnlineProviderCredentialConnectionStore.ClientIdField] = " client-id ",
                    [QuickBooksOnlineProviderCredentialConnectionStore.ClientSecretField] = " client-secret ",
                    [QuickBooksOnlineProviderCredentialConnectionStore.RefreshTokenField] = " refresh-token ",
                    [QuickBooksOnlineProviderCredentialConnectionStore.RealmIdField] = " realm-1 ",
                    [QuickBooksOnlineProviderCredentialConnectionStore.CompanyNameField] = " Meridian Test Co "
                },
                environment: "live")
        };
        var connectionStore = new QuickBooksOnlineProviderCredentialConnectionStore(store);

        var connection = await connectionStore.ReadAsync();

        connection.Should().NotBeNull();
        connection!.ClientId.Should().Be("client-id");
        connection.ClientSecret.Should().Be("client-secret");
        connection.RefreshToken.Should().Be("refresh-token");
        connection.RealmId.Should().Be("realm-1");
        connection.Environment.Should().Be("production");
        connection.CompanyName.Should().Be("Meridian Test Co");
    }

    [Fact]
    public async Task GetMetadataAsync_ProjectsCredentialStatusAndCompanyEvidence()
    {
        var lastSuccessfulAt = DateTimeOffset.UtcNow.AddMinutes(-15);
        var store = new FakeProviderCredentialStore
        {
            ReadResult = CreateReadResult(
                new Dictionary<string, string>
                {
                    [QuickBooksOnlineProviderCredentialConnectionStore.RealmIdField] = "realm-2",
                    [QuickBooksOnlineProviderCredentialConnectionStore.CompanyNameField] = "Accounting Entity",
                    [QuickBooksOnlineProviderCredentialConnectionStore.RefreshTokenField] = "refresh-token"
                },
                environment: "production"),
            Status = CreateStatus(
                ProviderCredentialStateDto.Verified,
                ProviderVerificationStateDto.Verified,
                environment: "production",
                lastSuccessfulAt: lastSuccessfulAt,
                missingFields: Array.Empty<string>())
        };
        var connectionStore = new QuickBooksOnlineProviderCredentialConnectionStore(store);

        var metadata = await connectionStore.GetMetadataAsync();

        metadata.ProviderId.Should().Be(QuickBooksOnlineAccountingProvider.Id);
        metadata.Environment.Should().Be("production");
        metadata.CompanyId.Should().Be("realm-2");
        metadata.CompanyName.Should().Be("Accounting Entity");
        metadata.HasLocalConfig.Should().BeTrue();
        metadata.HasRefreshToken.Should().BeTrue();
        metadata.LastConnectedAtUtc.Should().Be(lastSuccessfulAt);
        metadata.StatusLabel.Should().Be("Company verified");
        metadata.MissingFields.Should().BeEmpty();
    }

    [Fact]
    public async Task SaveRefreshTokenAsync_StoresTrimmedTokenThroughProviderCredentialStore()
    {
        var store = new FakeProviderCredentialStore();
        var connectionStore = new QuickBooksOnlineProviderCredentialConnectionStore(store);

        await connectionStore.SaveRefreshTokenAsync(" rotated-token ");

        store.LastSave.Should().NotBeNull();
        store.LastSave!.ProviderId.Should().Be(QuickBooksOnlineAccountingProvider.Id);
        store.LastSave.Credentials.Should()
            .ContainKey(QuickBooksOnlineProviderCredentialConnectionStore.RefreshTokenField)
            .WhoseValue.Should().Be("rotated-token");
        store.LastSave.Actor.Should().Be("quickbooks-token-exchange");
    }

    [Fact]
    public async Task RecordConnectionAsync_WritesVerificationMetadataThroughProviderCredentialStore()
    {
        var occurredAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        var store = new FakeProviderCredentialStore();
        var connectionStore = new QuickBooksOnlineProviderCredentialConnectionStore(store);

        await connectionStore.RecordConnectionAsync(
            success: false,
            externalCompanyId: "realm-3",
            error: "token rejected",
            occurredAtUtc: occurredAt);

        store.LastVerification.Should().NotBeNull();
        store.LastVerification!.ProviderId.Should().Be(QuickBooksOnlineAccountingProvider.Id);
        store.LastVerification.Success.Should().BeFalse();
        store.LastVerification.ExternalAccountId.Should().Be("realm-3");
        store.LastVerification.ErrorMessage.Should().Be("token rejected");
        store.LastVerification.VerifiedAt.Should().Be(occurredAt);
        store.LastVerification.Actor.Should().Be("quickbooks-read-only-import");
    }

    private static ProviderCredentialReadResult CreateReadResult(
        IReadOnlyDictionary<string, string> credentials,
        string? environment = "sandbox")
        => new(
            QuickBooksOnlineAccountingProvider.Id,
            ProviderCredentialSourceDto.LocalEncryptedStore,
            credentials,
            environment,
            ExternalAccountId: null,
            SavedAt: DateTimeOffset.UtcNow.AddHours(-1),
            LastVerifiedAt: null,
            LastSuccessfulAt: null,
            LastFailureAt: null,
            LastError: null,
            AuditMetadata: new Dictionary<string, string>());

    private static ProviderCredentialStoreStatus CreateStatus(
        ProviderCredentialStateDto state,
        ProviderVerificationStateDto verificationState,
        string? environment,
        DateTimeOffset? lastSuccessfulAt,
        IReadOnlyList<string> missingFields)
        => new(
            QuickBooksOnlineAccountingProvider.Id,
            "QuickBooks Online",
            state,
            ProviderCredentialSourceDto.LocalEncryptedStore,
            verificationState,
            SavedAt: DateTimeOffset.UtcNow.AddHours(-1),
            UpdatedAt: DateTimeOffset.UtcNow.AddMinutes(-30),
            LastVerifiedAt: lastSuccessfulAt,
            LastSuccessfulAt: lastSuccessfulAt,
            LastFailureAt: null,
            LastError: null,
            MaskedKeyPreview: null,
            environment,
            ExternalAccountId: null,
            MissingFields: missingFields,
            PresentFields: Array.Empty<string>(),
            AuditMetadata: new Dictionary<string, string>());

    private sealed class FakeProviderCredentialStore : IProviderCredentialStore
    {
        public string VaultPath => "fake-vault.json";

        public ProviderCredentialReadResult? ReadResult { get; init; }

        public ProviderCredentialStoreStatus Status { get; init; } = CreateStatus(
            ProviderCredentialStateDto.Missing,
            ProviderVerificationStateDto.NotVerified,
            environment: "sandbox",
            lastSuccessfulAt: null,
            missingFields:
            [
                QuickBooksOnlineProviderCredentialConnectionStore.ClientIdField,
                QuickBooksOnlineProviderCredentialConnectionStore.ClientSecretField,
                QuickBooksOnlineProviderCredentialConnectionStore.RefreshTokenField,
                QuickBooksOnlineProviderCredentialConnectionStore.RealmIdField
            ]);

        public ProviderCredentialSaveRequest? LastSave { get; private set; }

        public ProviderCredentialVerificationUpdate? LastVerification { get; private set; }

        public Task<ProviderCredentialStoreStatus> GetStatusAsync(string providerId, CancellationToken ct = default)
            => Task.FromResult(Status);

        public Task SaveAsync(ProviderCredentialSaveRequest request, CancellationToken ct = default)
        {
            LastSave = request;
            return Task.CompletedTask;
        }

        public Task<ProviderCredentialReadResult?> ReadForProviderAsync(string providerId, CancellationToken ct = default)
            => Task.FromResult(ReadResult);

        public Task DeleteAsync(string providerId, string? actor = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RecordVerificationAsync(ProviderCredentialVerificationUpdate update, CancellationToken ct = default)
        {
            LastVerification = update;
            return Task.CompletedTask;
        }
    }
}
