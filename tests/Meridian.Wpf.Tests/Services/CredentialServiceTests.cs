using System.Text;
using Meridian.Contracts.Credentials;
using Meridian.Ui.Services.Contracts;
using Meridian.Wpf.Services;
using SharedCredentialExpirationEventArgs = Meridian.Ui.Services.CredentialExpirationEventArgs;

namespace Meridian.Wpf.Tests.Services;

/// <summary>
/// Tests for <see cref="CredentialService"/> guarding against silent credential loss or leakage
/// in the DPAPI-encrypted vault (swallowed save/load failures), corrupt-store lockout at startup,
/// missed credential-expiration warnings on the WPF and shared UI seams, and OAuth refresh guard
/// regressions that would strand live trading connections with expired tokens.
/// </summary>
/// <remarks>
/// Every service is constructed against an isolated per-test temp directory via the
/// <see cref="CredentialService(string?)"/> storage seam, so the tests never read, move, or
/// delete the real operator vault under %LocalAppData%\Meridian. The temp directory is removed
/// in <see cref="Dispose"/>.
/// </remarks>
public sealed class CredentialServiceTests : IDisposable
{
    private readonly string _storageDir;
    private readonly string _vaultPath;
    private readonly string _metadataPath;
    private readonly List<CredentialService> _createdServices = new();

    public CredentialServiceTests()
    {
        _storageDir = Path.Combine(
            Path.GetTempPath(),
            "meridian-tests",
            $"credential-service-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_storageDir);

        _vaultPath = Path.Combine(_storageDir, "credentials.enc");
        _metadataPath = Path.Combine(_storageDir, "credential_metadata.json");
    }

    public void Dispose()
    {
        foreach (var service in _createdServices)
        {
            service.Dispose();
        }

        if (Directory.Exists(_storageDir))
        {
            Directory.Delete(_storageDir, recursive: true);
        }
    }

    private CredentialService CreateSut()
    {
        var sut = new CredentialService(_storageDir);
        _createdServices.Add(sut);
        return sut;
    }

    private async Task<CredentialService> CreateInitializedSutAsync()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.InitializeAsync(cts.Token);
        return sut;
    }

    private static string UniqueResource() => $"Meridian.Test.{Guid.NewGuid():N}";

    private static string UniqueProviderId() => $"provider{Guid.NewGuid():N}";

    // ── Vault CRUD ───────────────────────────────────────────────────

    [Fact]
    public void SaveCredential_NewResource_GetCredentialReturnsStoredPair()
    {
        var sut = CreateSut();
        var resource = UniqueResource();

        sut.SaveCredential(resource, "user-1", "pass-1");

        var credential = sut.GetCredential(resource);
        credential.HasValue.Should().BeTrue("the credential was just saved");
        credential.Value.Username.Should().Be("user-1");
        credential.Value.Password.Should().Be("pass-1");
        sut.HasCredential(resource).Should().BeTrue();
    }

    [Fact]
    public void SaveCredential_ExistingResource_OverwritesPreviousValue()
    {
        var sut = CreateSut();
        var resource = UniqueResource();

        sut.SaveCredential(resource, "user-old", "pass-old");
        sut.SaveCredential(resource, "user-new", "pass-new");

        var credential = sut.GetCredential(resource);
        credential.HasValue.Should().BeTrue();
        credential.Value.Username.Should().Be("user-new", "the second save must overwrite the first");
        credential.Value.Password.Should().Be("pass-new");
    }

    [Fact]
    public void SaveCredential_PersistedVault_SecondInstanceReadsSameCredential()
    {
        var resource = UniqueResource();
        var writer = CreateSut();
        writer.SaveCredential(resource, "roundtrip-user", "roundtrip-pass");

        var reader = CreateSut();

        var credential = reader.GetCredential(resource);
        credential.HasValue.Should().BeTrue(
            "a fresh instance must decrypt the vault written by the first instance");
        credential.Value.Username.Should().Be("roundtrip-user");
        credential.Value.Password.Should().Be("roundtrip-pass");
    }

    [Fact]
    public void GetCredential_UnknownResource_ReturnsNull()
    {
        var sut = CreateSut();
        var errors = new List<CredentialErrorEventArgs>();
        sut.CredentialError += (_, e) => errors.Add(e);

        var credential = sut.GetCredential(UniqueResource());

        credential.Should().BeNull("nothing was stored under that resource");
        errors.Should().BeEmpty("a missing credential is not an error");
        sut.GetTelemetryCounters().Should().Be((0, 0, 0, 0));
    }

    [Fact]
    public void SaveApiKey_RoundTrip_GetApiKeyReturnsKeyWithApikeyUsername()
    {
        var sut = CreateSut();
        var resource = UniqueResource();

        sut.SaveApiKey(resource, "the-api-key");

        sut.GetApiKey(resource).Should().Be("the-api-key");
        var credential = sut.GetCredential(resource);
        credential.HasValue.Should().BeTrue();
        credential.Value.Username.Should().Be("apikey", "SaveApiKey stores the literal 'apikey' username");
    }

    [Fact]
    public void RemoveCredential_ExistingResource_RemovesFromVaultAndPersists()
    {
        var resource = UniqueResource();
        var writer = CreateSut();
        writer.SaveCredential(resource, "user", "pass");

        writer.RemoveCredential(resource);

        writer.HasCredential(resource).Should().BeFalse("the credential was removed in-memory");

        var reader = CreateSut();
        reader.HasCredential(resource).Should().BeFalse(
            "the removal must be persisted so a fresh instance does not resurrect the credential");
    }

    [Fact]
    public void RemoveCredential_MissingResource_DoesNotRaiseErrorOrChangeCounters()
    {
        var sut = CreateSut();
        var errors = new List<CredentialErrorEventArgs>();
        sut.CredentialError += (_, e) => errors.Add(e);

        sut.RemoveCredential(UniqueResource());

        errors.Should().BeEmpty("removing an absent resource is a silent no-op");
        sut.GetTelemetryCounters().Should().Be((0, 0, 0, 0));
    }

    [Fact]
    public void SaveCredential_NullResource_RaisesCredentialErrorAndIncrementsSaveFailures()
    {
        var sut = CreateSut();
        var errors = new List<CredentialErrorEventArgs>();
        sut.CredentialError += (_, e) => errors.Add(e);

        sut.SaveCredential(null!, "user", "pass");

        errors.Should().ContainSingle("the null-key dictionary failure must surface as a CredentialError");
        errors[0].Operation.Should().Be(CredentialOperation.Save);
        errors[0].Exception.Should().BeOfType<ArgumentNullException>();
        errors[0].Severity.Should().Be(CredentialErrorSeverity.Error);
        sut.GetTelemetryCounters().SaveFailures.Should().Be(1);
    }

    [Fact]
    public void GetCredential_NullResource_ReturnsNullRaisesErrorAndIncrementsRetrievalFailures()
    {
        var sut = CreateSut();
        var errors = new List<CredentialErrorEventArgs>();
        sut.CredentialError += (_, e) => errors.Add(e);

        var credential = sut.GetCredential(null!);

        credential.Should().BeNull("the retrieval failure is converted into a null result");
        errors.Should().ContainSingle();
        errors[0].Operation.Should().Be(CredentialOperation.Retrieve);
        errors[0].Exception.Should().BeOfType<ArgumentNullException>();
        sut.GetTelemetryCounters().RetrievalFailures.Should().Be(1);
    }

    [Fact]
    public void RemoveCredential_NullResource_RaisesErrorAndIncrementsRemovalFailures()
    {
        var sut = CreateSut();
        var errors = new List<CredentialErrorEventArgs>();
        sut.CredentialError += (_, e) => errors.Add(e);

        sut.RemoveCredential(null!);

        errors.Should().ContainSingle();
        errors[0].Operation.Should().Be(CredentialOperation.Remove);
        errors[0].Exception.Should().BeOfType<ArgumentNullException>();
        sut.GetTelemetryCounters().RemovalFailures.Should().Be(1);
    }

    [Fact]
    public void HasCredential_NullResource_ThrowsArgumentNullException()
    {
        var sut = CreateSut();

        var act = () => sut.HasCredential(null!);

        act.Should().Throw<ArgumentNullException>(
            "HasCredential has no catch guard, so the null key propagates to the caller");
    }

    [Fact]
    public void GetTelemetryCounters_FreshService_AllCountersZero()
    {
        var sut = CreateSut();

        sut.GetTelemetryCounters().Should().Be((0, 0, 0, 0));
    }

    [Fact]
    public void GetAllStoredResources_MixedPrefixes_ReturnsOnlyMeridianPrefixedKeys()
    {
        var sut = CreateSut();
        var meridianResource = UniqueResource();
        sut.SaveCredential(meridianResource, "user", "pass");
        sut.SaveCredential("Other.Foreign", "foreign-user", "foreign-pass");

        var resources = sut.GetAllStoredResources();

        resources.Should().Contain(meridianResource);
        resources.Should().NotContain("Other.Foreign", "only Meridian-prefixed keys belong to this application");
    }

    [Fact]
    public void RemoveAllCredentials_MixedPrefixes_RemovesOnlyMeridianResources()
    {
        var sut = CreateSut();
        var meridianResource = UniqueResource();
        sut.SaveCredential(meridianResource, "user", "pass");
        sut.SaveCredential("Other.Foreign", "foreign-user", "foreign-pass");

        sut.RemoveAllCredentials();

        sut.HasCredential(meridianResource).Should().BeFalse("the sweep must clear Meridian-owned resources");
        sut.HasCredential("Other.Foreign").Should().BeTrue("foreign keys must survive the Meridian-only sweep");
        sut.GetAllStoredResources().Should().BeEmpty();
    }

    // ── Corrupt store recovery ───────────────────────────────────────

    public static TheoryData<byte[]> CorruptVaultPayloads => new()
    {
        Array.Empty<byte>(),
        new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x42, 0x13, 0x37 },
        Encoding.UTF8.GetBytes("{\"Meridian.Test\":{\"Username\":\"u\",\"Password\":\"p\"}}")
    };

    [Theory]
    [MemberData(nameof(CorruptVaultPayloads))]
    public void Constructor_CorruptVaultFile_StartsWithEmptyVaultInsteadOfThrowing(byte[] corruptPayload)
    {
        File.WriteAllBytes(_vaultPath, corruptPayload);

        var sut = CreateSut();

        sut.GetAllStoredResources().Should().BeEmpty(
            "a vault that cannot be decrypted must reset to empty instead of locking the operator out");
        sut.GetCredential(UniqueResource()).Should().BeNull();
    }

    [Fact]
    public void SaveCredential_AfterCorruptVaultRecovery_PersistsNewCleanVault()
    {
        File.WriteAllBytes(_vaultPath, new byte[] { 0x01, 0x02, 0x03, 0x04 });
        var resource = UniqueResource();

        var recovered = CreateSut();
        recovered.SaveCredential(resource, "healed-user", "healed-pass");

        var reader = CreateSut();
        var credential = reader.GetCredential(resource);
        credential.HasValue.Should().BeTrue(
            "after corrupt-store recovery the vault must self-heal into a valid encrypted file");
        credential.Value.Username.Should().Be("healed-user");
        credential.Value.Password.Should().Be("healed-pass");
    }

    [Theory]
    [InlineData("this is not json {{{")]
    [InlineData("[]")]
    [InlineData("")]
    public async Task InitializeAsync_CorruptMetadataFile_RecoversWithEmptyMetadata(string corruptJson)
    {
        await File.WriteAllTextAsync(_metadataPath, corruptJson);
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.InitializeAsync(cts.Token);

        sut.GetMetadata(UniqueResource()).Should().BeNull(
            "corrupt metadata must reset to an empty cache instead of failing initialization");
    }

    [Fact]
    public async Task UpdateMetadataAsync_PersistedMetadata_SecondInstanceLoadsItAfterInitialize()
    {
        var resource = UniqueResource();
        var expiresAt = new DateTime(2027, 3, 15, 12, 0, 0, DateTimeKind.Utc);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var writer = CreateSut();
        await writer.UpdateMetadataAsync(
            resource,
            m =>
            {
                m.ExpiresAt = expiresAt;
                m.TokenEndpoint = "https://example.invalid/token";
                m.ClientId = "client-42";
                m.AutoRefreshEnabled = true;
            },
            cts.Token);

        var reader = await CreateInitializedSutAsync();

        var metadata = reader.GetMetadata(resource);
        metadata.Should().NotBeNull("the metadata file written by the first instance must load on initialize");
        metadata!.ExpiresAt.Should().Be(expiresAt);
        metadata.TokenEndpoint.Should().Be("https://example.invalid/token");
        metadata.ClientId.Should().Be("client-42");
        metadata.AutoRefreshEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_CalledTwice_SecondCallIsNoOpAndDoesNotThrow()
    {
        var resource = UniqueResource();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var writer = CreateSut();
        await writer.UpdateMetadataAsync(resource, m => m.ClientId = "client-a", cts.Token);

        var sut = CreateSut();
        await sut.InitializeAsync(cts.Token);
        sut.GetMetadata(resource).Should().NotBeNull("the first initialize loads the persisted metadata");

        // If the second call reloaded, this corrupt payload would reset the cache to empty.
        await File.WriteAllTextAsync(_metadataPath, "corrupt {{{");
        await sut.InitializeAsync(cts.Token);

        sut.GetMetadata(resource).Should().NotBeNull(
            "the second InitializeAsync must be a no-op and keep the already-loaded metadata");
    }

    // ── Convenience wrappers ─────────────────────────────────────────

    [Fact]
    public void SaveAlpacaCredentials_RoundTrip_GetAlpacaCredentialsReturnsKeyIdAndSecret()
    {
        var sut = CreateSut();

        sut.SaveAlpacaCredentials("alpaca-key-id", "alpaca-secret");

        var credentials = sut.GetAlpacaCredentials();
        credentials.HasValue.Should().BeTrue();
        credentials.Value.KeyId.Should().Be("alpaca-key-id");
        credentials.Value.SecretKey.Should().Be("alpaca-secret");
        sut.HasAlpacaCredentials().Should().BeTrue();
        sut.HasCredential(CredentialService.AlpacaCredentialResource).Should().BeTrue();
    }

    [Fact]
    public void RemoveAlpacaCredentials_AfterSave_HasAlpacaCredentialsReturnsFalse()
    {
        var sut = CreateSut();
        sut.SaveAlpacaCredentials("alpaca-key-id", "alpaca-secret");

        sut.RemoveAlpacaCredentials();

        sut.HasAlpacaCredentials().Should().BeFalse();
        sut.GetAlpacaCredentials().Should().BeNull();
    }

    [Fact]
    public void SaveNasdaqApiKey_RoundTrip_GetNasdaqApiKeyReturnsKey()
    {
        var sut = CreateSut();
        sut.GetNasdaqApiKey().Should().BeNull("no key was stored yet");

        sut.SaveNasdaqApiKey("nasdaq-key");

        sut.GetNasdaqApiKey().Should().Be("nasdaq-key");
        sut.HasCredential(CredentialService.NasdaqApiKeyResource).Should().BeTrue();
    }

    [Fact]
    public void SaveOpenFigiApiKey_RoundTrip_GetOpenFigiApiKeyReturnsKey()
    {
        var sut = CreateSut();
        sut.GetOpenFigiApiKey().Should().BeNull("no key was stored yet");

        sut.SaveOpenFigiApiKey("openfigi-key");

        sut.GetOpenFigiApiKey().Should().Be("openfigi-key");
        sut.HasCredential(CredentialService.OpenFigiApiKeyResource).Should().BeTrue();
    }

    // ── Metadata lifecycle ───────────────────────────────────────────

    [Fact]
    public void GetMetadata_UnknownResource_ReturnsNull()
    {
        var sut = CreateSut();

        sut.GetMetadata(UniqueResource()).Should().BeNull();
    }

    [Fact]
    public async Task UpdateMetadataAsync_NewResource_CreatesEntryAndRaisesMetadataUpdated()
    {
        var sut = await CreateInitializedSutAsync();
        var resource = UniqueResource();
        var events = new List<CredentialMetadataEventArgs>();
        sut.MetadataUpdated += (_, e) => events.Add(e);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.UpdateMetadataAsync(resource, m => m.ClientId = "client-1", cts.Token);

        events.Should().ContainSingle("every metadata update must raise MetadataUpdated exactly once");
        events[0].Resource.Should().Be(resource);

        var metadata = sut.GetMetadata(resource);
        metadata.Should().NotBeNull();
        metadata!.Resource.Should().Be(resource, "a freshly created entry must have Resource preset");
        metadata.ClientId.Should().Be("client-1");
        events[0].Metadata.Should().BeSameAs(metadata);
    }

    [Fact]
    public async Task UpdateMetadataAsync_ExpiresWithinSevenDays_RaisesCredentialExpiringOnWpfAndSharedSeams()
    {
        var sut = await CreateInitializedSutAsync();
        var resource = UniqueResource();
        var expiresAt = DateTime.UtcNow.AddDays(2);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var wpfEvents = new List<CredentialExpirationEventArgs>();
        sut.CredentialExpiring += (_, e) => wpfEvents.Add(e);

        var shared = (ICredentialService)sut;
        var sharedEvents = new List<SharedCredentialExpirationEventArgs>();
        shared.CredentialExpiring += (_, e) => sharedEvents.Add(e);

        await sut.UpdateMetadataAsync(resource, m => m.ExpiresAt = expiresAt, cts.Token);

        wpfEvents.Should().ContainSingle("a credential expiring within 7 days must warn the WPF surface");
        wpfEvents[0].Resource.Should().Be(resource);
        wpfEvents[0].ExpiresAt.Should().Be(expiresAt);

        sharedEvents.Should().ContainSingle("the shared UI seam must receive the same expiration warning");
        sharedEvents[0].Resource.Should().Be(resource);
        sharedEvents[0].ExpiresAt.Should().Be(expiresAt);
    }

    [Fact]
    public async Task UpdateMetadataAsync_ExpiresBeyondSevenDays_DoesNotRaiseCredentialExpiring()
    {
        var sut = await CreateInitializedSutAsync();
        var resource = UniqueResource();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var wpfEvents = new List<CredentialExpirationEventArgs>();
        sut.CredentialExpiring += (_, e) => wpfEvents.Add(e);

        await sut.UpdateMetadataAsync(resource, m => m.ExpiresAt = DateTime.UtcNow.AddDays(30), cts.Token);

        wpfEvents.Should().BeEmpty("credentials expiring beyond the 7-day window must not warn");
    }

    [Fact]
    public async Task UpdateMetadataAsync_ExpiryAlreadyPassed_DoesNotRaiseCredentialExpiring()
    {
        var sut = await CreateInitializedSutAsync();
        var resource = UniqueResource();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var wpfEvents = new List<CredentialExpirationEventArgs>();
        sut.CredentialExpiring += (_, e) => wpfEvents.Add(e);

        await sut.UpdateMetadataAsync(resource, m => m.ExpiresAt = DateTime.UtcNow.AddDays(-1), cts.Token);

        wpfEvents.Should().BeEmpty("already-expired credentials must not spam expiring warnings");
    }

    [Fact]
    public async Task UpdateMetadataAsync_UpdateActionThrows_ExceptionPropagatesToCaller()
    {
        var sut = await CreateInitializedSutAsync();
        var resource = UniqueResource();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        Func<Task> act = () => sut.UpdateMetadataAsync(
            resource,
            _ => throw new InvalidOperationException("update failed"),
            cts.Token);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("update failed", "the update callback runs without a catch guard");
    }

    [Fact]
    public async Task RecordAuthenticationAsync_Always_SetsLastAuthenticatedAtAndSuccessStatus()
    {
        var sut = await CreateInitializedSutAsync();
        var resource = UniqueResource();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.RecordAuthenticationAsync(resource, cts.Token);

        var metadata = sut.GetMetadata(resource);
        metadata.Should().NotBeNull();
        metadata!.TestStatus.Should().Be(CredentialTestStatus.Success);
        metadata.LastAuthenticatedAt.Should().NotBeNull();
        metadata.LastAuthenticatedAt.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    // ── Disposal ─────────────────────────────────────────────────────

    [Fact]
    public void GetMetadata_AfterDispose_ThrowsObjectDisposedException()
    {
        var sut = CreateSut();
        sut.Dispose();

        var act = () => sut.GetMetadata(UniqueResource());

        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task UpdateMetadataAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var sut = CreateSut();
        sut.Dispose();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        Func<Task> act = () => sut.UpdateMetadataAsync(UniqueResource(), m => m.ClientId = "x", cts.Token);

        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var sut = CreateSut();
        sut.Dispose();

        var act = () => sut.Dispose();

        act.Should().NotThrow("Dispose must be idempotent");
    }

    // ── Aggregation and display ──────────────────────────────────────

    [Theory]
    [InlineData("Meridian.Alpaca", "Alpaca API Credentials", CredentialType.ApiKeyWithSecret)]
    [InlineData("Meridian.NasdaqDataLink", "Nasdaq Data Link API Key", CredentialType.ApiKey)]
    [InlineData("Meridian.OpenFigi", "OpenFIGI API Key", CredentialType.ApiKey)]
    [InlineData("Meridian.Custom", "Custom", CredentialType.ApiKey)]
    public void GetAllCredentialsWithMetadata_KnownResources_MapsDisplayNameAndCredentialType(
        string resource,
        string expectedName,
        CredentialType expectedType)
    {
        var sut = CreateSut();
        sut.SaveCredential(resource, "user", "pass");

        var credentials = sut.GetAllCredentialsWithMetadata();

        var info = credentials.Should().ContainSingle(c => c.Resource == resource).Subject;
        info.Name.Should().Be(expectedName);
        info.CredentialType.Should().Be(expectedType);
    }

    [Fact]
    public void GetAllCredentialsWithMetadata_NoMetadata_StatusIsActive()
    {
        var sut = CreateSut();
        var resource = UniqueResource();
        sut.SaveCredential(resource, "user", "pass");

        var credentials = sut.GetAllCredentialsWithMetadata();

        var info = credentials.Should().ContainSingle(c => c.Resource == resource).Subject;
        info.Status.Should().Be("Active", "a credential without metadata defaults to Active");
        info.TestStatus.Should().Be(CredentialTestStatus.Unknown);
        info.CanAutoRefresh.Should().BeFalse();
        info.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task GetAllCredentialsWithMetadata_ExpiredMetadata_StatusIsExpired()
    {
        var sut = await CreateInitializedSutAsync();
        var resource = UniqueResource();
        sut.SaveCredential(resource, "user", "pass");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.UpdateMetadataAsync(resource, m => m.ExpiresAt = DateTime.UtcNow.AddHours(-2), cts.Token);

        var credentials = sut.GetAllCredentialsWithMetadata();

        var info = credentials.Should().ContainSingle(c => c.Resource == resource).Subject;
        info.Status.Should().Be("Expired");
        info.IsExpired.Should().BeTrue();
    }

    [Fact]
    public async Task GetAllCredentialsWithMetadata_ExpiringWithinOneDay_StatusIsExpiresSoon()
    {
        var sut = await CreateInitializedSutAsync();
        var resource = UniqueResource();
        sut.SaveCredential(resource, "user", "pass");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.UpdateMetadataAsync(resource, m => m.ExpiresAt = DateTime.UtcNow.AddHours(2), cts.Token);

        var credentials = sut.GetAllCredentialsWithMetadata();

        var info = credentials.Should().ContainSingle(c => c.Resource == resource).Subject;
        info.Status.Should().Be("Expires soon");
    }

    [Theory]
    [InlineData(30d, "Active - Just used")]
    [InlineData(185d, "Active - Used 3h ago")]
    [InlineData(4440d, "Active - Used 3d ago")]
    public async Task GetAllCredentialsWithMetadata_LastAuthenticatedBuckets_FormatStatusByElapsedTime(
        double minutesAgo,
        string expectedStatus)
    {
        var sut = await CreateInitializedSutAsync();
        var resource = UniqueResource();
        sut.SaveCredential(resource, "user", "pass");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.UpdateMetadataAsync(
            resource,
            m => m.LastAuthenticatedAt = DateTime.UtcNow.AddMinutes(-minutesAgo),
            cts.Token);

        var credentials = sut.GetAllCredentialsWithMetadata();

        var info = credentials.Should().ContainSingle(c => c.Resource == resource).Subject;
        info.Status.Should().Be(expectedStatus);
    }

    [Fact]
    public async Task GetExpiringCredentials_MixedExpirations_ReturnsOnlyExpiredOrExpiringSoon()
    {
        var sut = await CreateInitializedSutAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var expiredResource = UniqueResource();
        var expiringResource = UniqueResource();
        var farFutureResource = UniqueResource();
        var noExpiryResource = UniqueResource();

        sut.SaveCredential(expiredResource, "user", "pass");
        sut.SaveCredential(expiringResource, "user", "pass");
        sut.SaveCredential(farFutureResource, "user", "pass");
        sut.SaveCredential(noExpiryResource, "user", "pass");

        await sut.UpdateMetadataAsync(expiredResource, m => m.ExpiresAt = DateTime.UtcNow.AddDays(-1), cts.Token);
        await sut.UpdateMetadataAsync(expiringResource, m => m.ExpiresAt = DateTime.UtcNow.AddDays(2), cts.Token);
        await sut.UpdateMetadataAsync(farFutureResource, m => m.ExpiresAt = DateTime.UtcNow.AddDays(60), cts.Token);

        var expiring = sut.GetExpiringCredentials();

        expiring.Select(c => c.Resource).Should().BeEquivalentTo(
            new[] { expiredResource, expiringResource },
            "only expired or 7-day-expiring credentials belong in the warning list");
    }

    // ── Credential tests (offline failure paths only) ────────────────

    [Fact]
    public async Task TestAlpacaCredentialsAsync_NoStoredCredentials_ReturnsFailureWithoutNetwork()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await sut.TestAlpacaCredentialsAsync(useSandbox: true, cts.Token);

        result.Success.Should().BeFalse();
        result.Message.Should().Be("No Alpaca credentials stored");
    }

    [Fact]
    public async Task TestNasdaqApiKeyAsync_NoStoredKey_ReturnsFailure()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await sut.TestNasdaqApiKeyAsync(cts.Token);

        result.Success.Should().BeFalse();
        result.Message.Should().Be("No Nasdaq Data Link API key stored");
    }

    [Fact]
    public async Task TestOpenFigiApiKeyAsync_NoStoredKey_ReturnsFailure()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await sut.TestOpenFigiApiKeyAsync(cts.Token);

        result.Success.Should().BeFalse();
        result.Message.Should().Be("No OpenFIGI API key stored");
    }

    [Fact]
    public async Task TestCredentialAsync_UnrecognizedResource_ReturnsNoTestAvailableFailure()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await sut.TestCredentialAsync("Meridian.Test.Unknown", cts.Token);

        result.Success.Should().BeFalse();
        result.Message.Should().Be("No test available for Meridian.Test.Unknown");
    }

    [Fact]
    public async Task TestAllCredentialsAsync_EmptyVault_ReturnsEmptyDictionary()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var results = await sut.TestAllCredentialsAsync(cts.Token);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task TestAllCredentialsAsync_OnlyCustomResource_ReturnsFailureEntryPerResource()
    {
        var sut = CreateSut();
        // Never store under Meridian.Alpaca / Meridian.NasdaqDataLink / Meridian.OpenFigi
        // here — those resources would route to live API endpoints.
        var resource = UniqueResource();
        sut.SaveCredential(resource, "user", "pass");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var results = await sut.TestAllCredentialsAsync(cts.Token);

        results.Should().HaveCount(1);
        results.Should().ContainKey(resource);
        results[resource].Success.Should().BeFalse();
        results[resource].Message.Should().Be($"No test available for {resource}");
    }

    // ── OAuth token storage and refresh guards ───────────────────────

    [Fact]
    public async Task SaveOAuthTokenAsync_WithRefreshToken_StoresBothTokensAndOAuthMetadata()
    {
        var sut = await CreateInitializedSutAsync();
        var providerId = UniqueProviderId();
        var resource = $"Meridian.OAuth.{providerId}";
        var expiresAt = DateTime.UtcNow.AddDays(60);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.SaveOAuthTokenAsync(
            providerId,
            "access-token-1",
            "refresh-token-1",
            expiresAt,
            "https://example.invalid/token",
            "client-1",
            cts.Token);

        sut.GetOAuthToken(providerId).Should().Be("access-token-1");

        var accessCredential = sut.GetCredential(resource);
        accessCredential.HasValue.Should().BeTrue();
        accessCredential.Value.Username.Should().Be("oauth");

        var refreshCredential = sut.GetCredential($"{resource}.refresh");
        refreshCredential.HasValue.Should().BeTrue("the refresh token must be stored alongside the access token");
        refreshCredential.Value.Username.Should().Be("oauth_refresh");
        refreshCredential.Value.Password.Should().Be("refresh-token-1");

        var metadata = sut.GetMetadata(resource);
        metadata.Should().NotBeNull();
        metadata!.CredentialType.Should().Be(CredentialType.OAuth2Token);
        metadata.ExpiresAt.Should().Be(expiresAt);
        metadata.TokenEndpoint.Should().Be("https://example.invalid/token");
        metadata.ClientId.Should().Be("client-1");
        metadata.AutoRefreshEnabled.Should().BeTrue("a refresh token was provided");
        metadata.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task SaveOAuthTokenAsync_WithoutRefreshToken_RemovesStaleRefreshTokenAndDisablesAutoRefresh()
    {
        var sut = await CreateInitializedSutAsync();
        var providerId = UniqueProviderId();
        var resource = $"Meridian.OAuth.{providerId}";
        var refreshResource = $"{resource}.refresh";
        var expiresAt = DateTime.UtcNow.AddDays(60);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await sut.SaveOAuthTokenAsync(
            providerId, "access-1", "refresh-1", expiresAt, "https://example.invalid/token", "client-1", cts.Token);
        sut.HasCredential(refreshResource).Should().BeTrue("precondition: the first save stored a refresh token");

        await sut.SaveOAuthTokenAsync(
            providerId, "access-2", null, expiresAt, "https://example.invalid/token", "client-1", cts.Token);

        sut.GetOAuthToken(providerId).Should().Be("access-2");
        sut.HasCredential(refreshResource).Should().BeFalse(
            "saving without a refresh token must remove the stale refresh token instead of leaking it");
        sut.GetMetadata(resource)!.AutoRefreshEnabled.Should().BeFalse(
            "auto-refresh cannot work without a refresh token");
    }

    [Fact]
    public void GetOAuthToken_UnknownProvider_ReturnsNull()
    {
        var sut = CreateSut();

        sut.GetOAuthToken(UniqueProviderId()).Should().BeNull();
    }

    [Theory]
    [InlineData(false, false, false)] // nothing stored at all
    [InlineData(true, true, false)]   // metadata with endpoint, but no refresh credential
    [InlineData(true, false, true)]   // refresh credential stored, but metadata has no endpoint
    public async Task RefreshOAuthTokenAsync_MissingPrerequisites_ReturnsFalseWithoutNetwork(
        bool seedMetadata,
        bool seedEndpoint,
        bool seedRefreshCredential)
    {
        var sut = await CreateInitializedSutAsync();
        var providerId = UniqueProviderId();
        var resource = $"Meridian.OAuth.{providerId}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        if (seedRefreshCredential)
        {
            sut.SaveCredential($"{resource}.refresh", "oauth_refresh", "refresh-token");
        }

        if (seedMetadata)
        {
            await sut.UpdateMetadataAsync(
                resource,
                m =>
                {
                    m.ClientId = "client";
                    if (seedEndpoint)
                    {
                        m.TokenEndpoint = "https://example.invalid/token";
                    }
                },
                cts.Token);
        }

        var refreshed = await sut.RefreshOAuthTokenAsync(providerId, cts.Token);

        refreshed.Should().BeFalse("the offline guard must reject the refresh before any network call");
        if (seedMetadata)
        {
            var metadata = sut.GetMetadata(resource)!;
            metadata.RefreshFailureCount.Should().Be(0, "the guard returns before the failure-counting try block");
            metadata.LastRefreshAttemptAt.Should().BeNull("no refresh attempt was actually started");
        }
    }

    [Fact]
    public async Task RefreshOAuthTokenAsync_InvalidTokenEndpoint_IncrementsFailureCountAndReturnsFalse()
    {
        var sut = await CreateInitializedSutAsync();
        var providerId = UniqueProviderId();
        var resource = $"Meridian.OAuth.{providerId}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        sut.SaveCredential($"{resource}.refresh", "oauth_refresh", "refresh-token");
        await sut.UpdateMetadataAsync(
            resource,
            m =>
            {
                // Relative URI + no BaseAddress on the credential-test client makes
                // PostAsync throw InvalidOperationException without touching the network.
                m.TokenEndpoint = "not-an-absolute-uri";
                m.ClientId = "client";
            },
            cts.Token);

        var refreshed = await sut.RefreshOAuthTokenAsync(providerId, cts.Token);

        refreshed.Should().BeFalse();
        var metadata = sut.GetMetadata(resource)!;
        metadata.RefreshFailureCount.Should().Be(1, "a failed refresh attempt must be counted");
        metadata.LastRefreshAttemptAt.Should().NotBeNull();
        metadata.LastRefreshAttemptAt.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task EnsureTokenValidAsync_NoMetadata_ReturnsFalse()
    {
        var sut = await CreateInitializedSutAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var valid = await sut.EnsureTokenValidAsync(UniqueProviderId(), ct: cts.Token);

        valid.Should().BeFalse("an unknown provider has no token to validate");
    }

    [Fact]
    public async Task EnsureTokenValidAsync_MetadataWithoutExpiry_ReturnsTrue()
    {
        var sut = await CreateInitializedSutAsync();
        var providerId = UniqueProviderId();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.UpdateMetadataAsync($"Meridian.OAuth.{providerId}", m => m.ClientId = "c", cts.Token);

        var valid = await sut.EnsureTokenValidAsync(providerId, ct: cts.Token);

        valid.Should().BeTrue("a token without an expiry never needs refreshing");
    }

    [Fact]
    public async Task EnsureTokenValidAsync_ExpiryBeyondThreshold_ReturnsTrueWithoutRefreshing()
    {
        var sut = await CreateInitializedSutAsync();
        var providerId = UniqueProviderId();
        var resource = $"Meridian.OAuth.{providerId}";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.UpdateMetadataAsync(
            resource,
            m =>
            {
                m.ExpiresAt = DateTime.UtcNow.AddHours(6);
                m.AutoRefreshEnabled = true;
                m.TokenEndpoint = "https://example.invalid/token";
            },
            cts.Token);

        var valid = await sut.EnsureTokenValidAsync(providerId, ct: cts.Token);

        valid.Should().BeTrue();
        var metadata = sut.GetMetadata(resource)!;
        metadata.RefreshFailureCount.Should().Be(0, "no refresh should have been attempted");
        metadata.LastRefreshAttemptAt.Should().BeNull("the token is comfortably beyond the refresh threshold");
    }

    [Fact]
    public async Task EnsureTokenValidAsync_ExpiredWithoutAutoRefresh_ReturnsFalse()
    {
        var sut = await CreateInitializedSutAsync();
        var providerId = UniqueProviderId();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.UpdateMetadataAsync(
            $"Meridian.OAuth.{providerId}",
            m => m.ExpiresAt = DateTime.UtcNow.AddHours(-2),
            cts.Token);

        var valid = await sut.EnsureTokenValidAsync(providerId, ct: cts.Token);

        valid.Should().BeFalse("an expired token without auto-refresh is unusable");
    }

    [Fact]
    public async Task EnsureTokenValidAsync_ExpiredWithAutoRefreshButNoRefreshToken_ReturnsFalse()
    {
        var sut = await CreateInitializedSutAsync();
        var providerId = UniqueProviderId();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.UpdateMetadataAsync(
            $"Meridian.OAuth.{providerId}",
            m =>
            {
                m.ExpiresAt = DateTime.UtcNow.AddHours(-2);
                m.AutoRefreshEnabled = true;
                m.TokenEndpoint = "https://example.invalid/token";
            },
            cts.Token);

        var valid = await sut.EnsureTokenValidAsync(providerId, ct: cts.Token);

        valid.Should().BeFalse(
            "auto-refresh delegates to RefreshOAuthTokenAsync, which fails its guard without a refresh token");
    }

    [Fact]
    public async Task EnsureTokenValidAsync_NearExpiryWithoutAutoRefresh_ReturnsTrue()
    {
        var sut = await CreateInitializedSutAsync();
        var providerId = UniqueProviderId();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.UpdateMetadataAsync(
            $"Meridian.OAuth.{providerId}",
            m => m.ExpiresAt = DateTime.UtcNow.AddMinutes(2),
            cts.Token);

        var valid = await sut.EnsureTokenValidAsync(providerId, refreshThresholdMinutes: 5, ct: cts.Token);

        valid.Should().BeTrue(
            "a non-refreshable token inside the threshold window is still usable until it actually expires");
    }

    // ── Shared ICredentialService seam ───────────────────────────────

    [Fact]
    public async Task SharedSeam_GetAllCredentialsWithMetadata_MapsOAuthEntriesToSharedShape()
    {
        var sut = await CreateInitializedSutAsync();
        var providerId = UniqueProviderId();
        var resource = $"Meridian.OAuth.{providerId}";
        var expiresAt = DateTime.UtcNow.AddDays(60);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.SaveOAuthTokenAsync(
            providerId, "access", "refresh", expiresAt, "https://example.invalid/token", "client", cts.Token);

        var shared = (ICredentialService)sut;
        var credentials = shared.GetAllCredentialsWithMetadata();

        // Assert by Resource filter: the ".refresh" companion resource also appears in the list.
        var entry = credentials.Should().ContainSingle(c => c.Resource == resource).Subject;
        entry.IsOAuthToken.Should().BeTrue();
        entry.ExpiresAt.Should().Be(expiresAt);
        entry.CanAutoRefresh.Should().BeTrue();
        entry.AutoRefreshEnabled.Should().BeTrue("the shared shape mirrors CanAutoRefresh into AutoRefreshEnabled");
    }

    [Fact]
    public async Task SharedSeam_GetMetadata_ReturnsMappedInfoOrNull()
    {
        var sut = await CreateInitializedSutAsync();
        var resource = UniqueResource();
        var expiresAt = DateTime.UtcNow.AddDays(60);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var shared = (ICredentialService)sut;

        shared.GetMetadata(UniqueResource()).Should().BeNull("no metadata exists for an unknown resource");

        await sut.UpdateMetadataAsync(
            resource,
            m =>
            {
                m.AutoRefreshEnabled = true;
                m.ExpiresAt = expiresAt;
            },
            cts.Token);

        var info = shared.GetMetadata(resource);
        info.Should().NotBeNull();
        info!.AutoRefreshEnabled.Should().BeTrue();
        info.ExpiresAt.Should().Be(expiresAt);
    }

    [Fact]
    public async Task SharedSeam_UpdateMetadataAsync_NullAction_ThrowsArgumentNullException()
    {
        var sut = await CreateInitializedSutAsync();
        var shared = (ICredentialService)sut;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        Func<Task> act = () => shared.UpdateMetadataAsync(UniqueResource(), null!, cts.Token);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SharedSeam_UpdateMetadataAsync_TogglesAutoRefreshEnabledOnUnderlyingMetadata()
    {
        var sut = await CreateInitializedSutAsync();
        var resource = UniqueResource();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await sut.UpdateMetadataAsync(resource, m => m.AutoRefreshEnabled = true, cts.Token);

        var shared = (ICredentialService)sut;
        var seededValue = false;
        await shared.UpdateMetadataAsync(
            resource,
            update =>
            {
                seededValue = update.AutoRefreshEnabled;
                update.AutoRefreshEnabled = false;
            },
            cts.Token);

        seededValue.Should().BeTrue("the seam must seed the update object from the existing metadata value");
        sut.GetMetadata(resource)!.AutoRefreshEnabled.Should().BeFalse(
            "the seam update must round-trip into the underlying WPF metadata");
    }

    [Fact]
    public async Task SharedSeam_RefreshOAuthTokenAsync_Failure_ReturnsErrorResultWithMessage()
    {
        var sut = await CreateInitializedSutAsync();
        var shared = (ICredentialService)sut;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var result = await shared.RefreshOAuthTokenAsync(UniqueProviderId(), cts.Token);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("OAuth token refresh failed.");
        result.NewExpiration.Should().BeNull();
    }
}
