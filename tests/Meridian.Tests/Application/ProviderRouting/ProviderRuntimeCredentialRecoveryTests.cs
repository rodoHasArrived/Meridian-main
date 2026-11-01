using FluentAssertions;
using Meridian.Application.ProviderRouting;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Moq;

namespace Meridian.Tests.Application.ProviderRouting;

public sealed class ProviderRuntimeCredentialRecoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-runtime-recovery", Guid.NewGuid().ToString("N"));
    private static readonly ProviderCredentialScope Owner = new("tenant-a", "owned", "account-a", "paper");
    private ConfigStore Config => new(Path.Combine(_root, "appsettings.json"));

    [Fact]
    public async Task Rollback_ReconstructedHostStaysUnavailableUntilAnExplicitNewTransfer()
    {
        var store = await SetupAsync();
        var commands = new ProviderRuntimeCredentialService(Config, store);
        var preview = await commands.GetMigrationPreviewAsync("alpaca", "owned", "tenant-a");
        var generation = preview.AuditMetadata["credentialGeneration"];
        await commands.TransferAsync("alpaca", "owned", "tenant-a", "original-transfer", generation, "operator", false);
        await commands.BindAsync("alpaca", "owned", "tenant-a");

        var fallback = new Mock<IProviderCredentialResolver>(MockBehavior.Strict);
        var resolver = new StoredProviderCredentialResolver(store, fallback.Object, Config);
        resolver.CreateContext(typeof(AlpacaHistoricalDataProvider)).Get("ALPACA_KEY_ID").Should().Be("retained-key");
        await commands.TransferAsync("alpaca", "owned", "tenant-a", "original-transfer", null, "operator", true);

        // Reconstruct both vault and resolver as a host restart would; no provider is activated.
        store = NewStore();
        commands = new ProviderRuntimeCredentialService(Config, store);
        resolver = new StoredProviderCredentialResolver(store, fallback.Object, Config);
        var config = Config.LoadRequired().ProviderConnections!;
        config.RuntimeCredentials.Should().ContainSingle().Which.Should()
            .Be(new ProviderRuntimeCredentialBinding("alpaca", "tenant-a", "owned"));
        config.OwnershipRequiredProviders.Should().Contain("alpaca");
        (await store.ReadForProviderAsync("alpaca"))!.Get("KeyId").Should().Be("retained-key");
        (await store.ReadScopedAsync("alpaca", Owner)).Should().BeNull();
        var unavailable = resolver.CreateContext(typeof(AlpacaHistoricalDataProvider), new Dictionary<string, string?>
        {
            ["ALPACA_KEY_ID"] = "unselected-config-key",
            ["ALPACA_SECRET_KEY"] = "unselected-config-secret"
        });
        unavailable.IsAuthoritative.Should().BeTrue();
        unavailable.IsConfigured("ALPACA_KEY_ID").Should().BeFalse();
        unavailable.IsConfigured("ALPACA_SECRET_KEY").Should().BeFalse();
        unavailable.CredentialEnvironment.Should().Be("paper");

        // A retry of the completed operation cannot undo its later rollback.
        (await commands.TransferAsync("alpaca", "owned", "tenant-a", "original-transfer", generation, "operator", false))
            .RolledBack.Should().BeTrue();
        resolver.CreateContext(typeof(AlpacaHistoricalDataProvider)).IsConfigured("ALPACA_KEY_ID").Should().BeFalse();

        // Recovery is a new reviewed operation using a fresh preview, while retaining host ownership.
        var recoveryPreview = await commands.GetMigrationPreviewAsync("alpaca", "owned", "tenant-a");
        await commands.TransferAsync("alpaca", "owned", "tenant-a", "reviewed-recovery",
            recoveryPreview.AuditMetadata["credentialGeneration"], "operator", false);
        resolver = new StoredProviderCredentialResolver(NewStore(), fallback.Object, Config);
        resolver.CreateContext(typeof(AlpacaHistoricalDataProvider)).Get("ALPACA_KEY_ID").Should().Be("retained-key");
        (await NewStore().GetScopedStatusAsync("alpaca", Owner)).VerificationState.Should().Be(ProviderVerificationStateDto.NotVerified);
        fallback.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RotatedTransfer_RefusedRollbackPreservesSelectedReplacementAcrossRestart()
    {
        var store = await SetupAsync();
        var commands = new ProviderRuntimeCredentialService(Config, store);
        var preview = await commands.GetMigrationPreviewAsync("alpaca", "owned", "tenant-a");
        await commands.TransferAsync("alpaca", "owned", "tenant-a", "transfer",
            preview.AuditMetadata["credentialGeneration"], "operator", false);
        await commands.BindAsync("alpaca", "owned", "tenant-a");
        var transferred = (await store.ReadScopedAsync("alpaca", Owner))!;
        await store.SaveScopedRotatedCredentialsAsync(Credentials("replacement"), Owner, transferred);

        Func<Task> rollback = () => commands.TransferAsync("alpaca", "owned", "tenant-a", "transfer", null, "operator", true);
        await rollback.Should().ThrowAsync<ProviderCredentialConflictException>();

        store = NewStore();
        var fallback = new Mock<IProviderCredentialResolver>(MockBehavior.Strict);
        var resolver = new StoredProviderCredentialResolver(store, fallback.Object, Config);
        var selected = resolver.CreateContext(typeof(AlpacaHistoricalDataProvider));
        selected.Get("ALPACA_KEY_ID").Should().Be("replacement");
        selected.Get("ALPACA_SECRET_KEY").Should().Be("replacement-secret");
        (await store.ReadForProviderAsync("alpaca")).Should().BeNull();
        fallback.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HostResolution_HoldsOwnershipThroughVaultReadAndRechecksAfterRemoval()
    {
        var actualStore = await SetupAsync();
        await actualStore.SaveScopedAsync(Credentials("owned"), Owner);
        await new ProviderRuntimeCredentialService(Config, actualStore).BindAsync("alpaca", "owned", "tenant-a");
        var credential = (await actualStore.ReadScopedAsync("alpaca", Owner))!;
        var enteredVault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseVault = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new Mock<IScopedProviderCredentialStore>(MockBehavior.Strict);
        store.Setup(value => value.ReadScopedAsync("alpaca", Owner, It.IsAny<CancellationToken>())).Returns(async () =>
        {
            enteredVault.TrySetResult();
            await releaseVault.Task;
            return credential;
        });
        var fallback = new Mock<IProviderCredentialResolver>(MockBehavior.Strict);
        var resolver = new StoredProviderCredentialResolver(store.Object, fallback.Object, Config);
        var resolving = Task.Run(() => resolver.CreateContext(typeof(AlpacaHistoricalDataProvider)));
        Task<bool>? deletion = null;
        try
        {
            await enteredVault.Task.WaitAsync(TimeSpan.FromSeconds(5));
            deletion = new ProviderConnectionService(Config).DeleteForTenantAsync("owned", "tenant-a");
            deletion.IsCompleted.Should().BeFalse("the vault read still holds the retained-ownership lease");
        }
        finally
        {
            releaseVault.TrySetResult();
        }

        (await resolving.WaitAsync(TimeSpan.FromSeconds(5))).Get("ALPACA_KEY_ID").Should().Be("owned");
        (await deletion!.WaitAsync(TimeSpan.FromSeconds(5))).Should().BeTrue();
        Action resolveAgain = () => resolver.CreateContext(typeof(AlpacaHistoricalDataProvider));
        resolveAgain.Should().Throw<UnauthorizedAccessException>();
        store.Verify(value => value.ReadScopedAsync("alpaca", Owner, It.IsAny<CancellationToken>()), Times.Once);
        store.VerifyNoOtherCalls();
        fallback.VerifyNoOtherCalls();
    }

    private async Task<FileProviderCredentialStore> SetupAsync()
    {
        Directory.CreateDirectory(_root);
        await Config.SaveAsync(new AppConfig(DataRoot: _root));
        await new ProviderConnectionService(Config).UpsertForTenantAsync(new CreateProviderConnectionRequest(
            "owned", "alpaca", "Owned account", ExternalAccountId: "account-a"), "tenant-a", "paper");
        var store = NewStore();
        await store.SaveAsync(Credentials("retained-key"));
        return store;
    }

    private FileProviderCredentialStore NewStore() => new(_root, name => name switch
    {
        "MDC_PROVIDER_ALLOW_ENV_FALLBACK" => "true",
        "ALPACA_KEY_ID" => "unselected-environment-key",
        "ALPACA_SECRET_KEY" => "unselected-environment-secret",
        _ => null
    });

    private static ProviderCredentialSaveRequest Credentials(string key) => new("alpaca",
        new Dictionary<string, string?> { ["KeyId"] = key, ["SecretKey"] = key + "-secret" }, "paper", "operator");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
