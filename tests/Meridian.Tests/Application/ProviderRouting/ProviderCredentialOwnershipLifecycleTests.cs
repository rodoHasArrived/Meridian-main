using FluentAssertions;
using Meridian.Application.ProviderRouting;
using Meridian.Application.Services;
using Meridian.Application.UI;
using Meridian.Contracts.Api;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.Infrastructure.Adapters.Alpaca;
using Meridian.Infrastructure.Adapters.Core;
using Moq;

namespace Meridian.Tests.Application.ProviderRouting;

public sealed class ProviderCredentialOwnershipLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-ownership-lifecycle", Guid.NewGuid().ToString("N"));
    private ConfigStore Config => new(Path.Combine(_root, "appsettings.json"));
    private static readonly ProviderCredentialScope OwnerA = new("tenant-a", "connection-a", "account-a", "paper");
    private static readonly ProviderCredentialScope OwnerB = new("tenant-b", "connection-b", "account-b", "paper");

    [Fact]
    public async Task TwoTenants_SetupSelectionRotationCancellationRestartAndRemovalKeepExactOwners()
    {
        await InitializeAsync();
        var vault = new FileProviderCredentialStore(_root);
        var setup = new ProviderSetupService(Config, vault, new ProviderSetupRegistry(DefaultProviderSetupHandlers.Create()));
        foreach (var owner in new[] { OwnerA, OwnerB })
            (await setup.ConfigureForConnectionAsync(SetupRequest(owner.ExternalAccountId), owner.ConnectionId,
                owner.TenantId, "operator")).Success.Should().BeTrue();
        var fallback = new Mock<IProviderCredentialResolver>(MockBehavior.Strict);
        var runtime = new ProviderRuntimeCredentialService(Config, vault);
        await runtime.BindAsync("alpaca", OwnerA.ConnectionId, OwnerA.TenantId);
        var selected = new StoredProviderCredentialResolver(vault, fallback.Object, Config);
        selected.CreateContext(typeof(AlpacaHistoricalDataProvider)).Get("ALPACA_KEY_ID").Should().Be("account-a-key");
        var foreignSelection = () => runtime.BindAsync("alpaca", OwnerB.ConnectionId, OwnerB.TenantId);
        await foreignSelection.Should().ThrowAsync<UnauthorizedAccessException>();
        var foreignSetup = () => setup.ConfigureForConnectionAsync(SetupRequest("foreign"), OwnerA.ConnectionId,
            OwnerB.TenantId, "operator");
        await foreignSetup.Should().ThrowAsync<UnauthorizedAccessException>();

        var beforeRotation = (await vault.ReadScopedAsync("alpaca", OwnerA))!;
        var otherBefore = (await vault.ReadScopedAsync("alpaca", OwnerB))!;
        await vault.SaveScopedRotatedCredentialsAsync(Credentials("rotated-a"), OwnerA, beforeRotation);
        var staleRotation = () => vault.SaveScopedRotatedCredentialsAsync(Credentials("stale-a"), OwnerA, beforeRotation);
        await staleRotation.Should().ThrowAsync<ProviderCredentialConflictException>();
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var cancelledSetup = () => setup.ConfigureForConnectionAsync(SetupRequest("cancelled"), OwnerA.ConnectionId,
                OwnerA.TenantId, "operator", cancelled.Token);
            await cancelledSetup.Should().ThrowAsync<OperationCanceledException>();
        }

        // Reconstruct the vault and all credential consumers, as setup and execution do on restart.
        vault = new FileProviderCredentialStore(_root);
        selected = new StoredProviderCredentialResolver(vault, fallback.Object, Config);
        selected.CreateContext(typeof(AlpacaHistoricalDataProvider)).Get("ALPACA_KEY_ID").Should().Be("rotated-a");
        (await vault.ReadScopedAsync("alpaca", OwnerB))!.CredentialGeneration.Should().Be(otherBefore.CredentialGeneration);
        new StoredProviderCredentialResolver(vault, fallback.Object, OwnerB)
            .CreateContext(typeof(AlpacaHistoricalDataProvider)).Get("ALPACA_KEY_ID").Should().Be("account-b-key");
        await vault.DeleteScopedAsync("alpaca", OwnerA, "operator");
        selected.CreateContext(typeof(AlpacaHistoricalDataProvider)).IsConfigured("ALPACA_KEY_ID").Should().BeFalse();
        await new ProviderConnectionService(Config).DeleteForTenantAsync(OwnerA.ConnectionId, OwnerA.TenantId);
        Action removedSelection = () => selected.CreateContext(typeof(AlpacaHistoricalDataProvider));
        removedSelection.Should().Throw<UnauthorizedAccessException>();
        (await vault.ReadScopedAsync("alpaca", OwnerB))!.Get("KeyId").Should().Be("account-b-key");
        fallback.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("replacement-account", "paper")]
    [InlineData("account-a", "live")]
    public async Task RemovedConnectionId_RecreatedWithDifferentOwnerCannotRetargetTheRetainedSelection(string account, string environment)
    {
        await InitializeAsync();
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveScopedAsync(Credentials("original"), OwnerA);
        await new ProviderRuntimeCredentialService(Config, vault).BindAsync("alpaca", OwnerA.ConnectionId, OwnerA.TenantId);
        var connections = new ProviderConnectionService(Config);
        await connections.DeleteForTenantAsync(OwnerA.ConnectionId, OwnerA.TenantId);
        await connections.UpsertForTenantAsync(new(OwnerA.ConnectionId, "alpaca", "Replacement", ExternalAccountId: account),
            OwnerA.TenantId, environment);
        var replacement = new ProviderCredentialScope(OwnerA.TenantId, OwnerA.ConnectionId, account, environment);
        await vault.SaveScopedAsync(Credentials("replacement") with { Environment = environment }, replacement);

        Action resolve = () => new StoredProviderCredentialResolver(new FileProviderCredentialStore(_root),
            new Mock<IProviderCredentialResolver>(MockBehavior.Strict).Object, Config)
            .CreateContext(typeof(AlpacaHistoricalDataProvider));
        resolve.Should().Throw<InvalidDataException>().WithMessage("*Bind the current connection explicitly*");
        await new ProviderRuntimeCredentialService(Config, vault).BindAsync("alpaca", OwnerA.ConnectionId, OwnerA.TenantId);
        new StoredProviderCredentialResolver(vault, new EnvironmentCredentialResolver(), Config)
            .CreateContext(typeof(AlpacaHistoricalDataProvider)).Get("ALPACA_KEY_ID").Should().Be("replacement");
    }

    [Fact]
    public async Task LegacyUnpinnedSelection_RequiresExplicitRebindBeforeReadingSecrets()
    {
        await InitializeAsync();
        await Config.UpdateAsync(config => config with
        {
            ProviderConnections = config.ProviderConnections! with
            {
                RuntimeCredentials = [new("alpaca", OwnerA.TenantId, OwnerA.ConnectionId)]
            }
        });
        var vault = new Mock<IScopedProviderCredentialStore>(MockBehavior.Strict);
        var resolver = new StoredProviderCredentialResolver(vault.Object, new EnvironmentCredentialResolver(), Config);
        Action resolve = () => resolver.CreateContext(typeof(AlpacaHistoricalDataProvider));
        resolve.Should().Throw<InvalidDataException>();
        vault.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ApplyingOldSelection_CannotMarkANewerSelectionAppliedEvenWhenItReturnsToTheSameAccount(bool selectOriginalAgain)
    {
        await InitializeAsync();
        await new ProviderConnectionService(Config).UpsertForTenantAsync(new("alternative", "alpaca", "Alternative",
            ExternalAccountId: "alternative-account"), OwnerA.TenantId, "paper");
        var applier = new DelayedApplier();
        var runtime = new ProviderRuntimeCredentialService(Config, new FileProviderCredentialStore(_root), applier: applier);
        var original = await runtime.BindAsync("alpaca", OwnerA.ConnectionId, OwnerA.TenantId);
        var pending = runtime.ApplyAsync("alpaca", OwnerA.ConnectionId, OwnerA.TenantId, "operator");
        await applier.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await runtime.BindAsync("alpaca", "alternative", OwnerA.TenantId);
        if (selectOriginalAgain)
            await runtime.BindAsync("alpaca", OwnerA.ConnectionId, OwnerA.TenantId);
        applier.Release.SetResult();
        var applied = () => pending;
        await applied.Should().ThrowAsync<ProviderCredentialConflictException>();
        var currentConnection = selectOriginalAgain ? OwnerA.ConnectionId : "alternative";
        var status = runtime.GetApplicationStatus("alpaca", currentConnection, OwnerA.TenantId);
        status.PendingRestart.Should().BeTrue();
        status.AppliedAt.Should().BeNull();
        Config.LoadRequired().ProviderConnections!.RuntimeCredentials!.Single().SelectionId.Should().NotBe(original.SelectionId);
    }

    private async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        await Config.SaveAsync(new AppConfig(DataRoot: _root));
        var connections = new ProviderConnectionService(Config);
        foreach (var owner in new[] { OwnerA, OwnerB })
            await connections.UpsertForTenantAsync(new(owner.ConnectionId, "alpaca", owner.ExternalAccountId,
                ExternalAccountId: owner.ExternalAccountId), owner.TenantId, owner.Environment);
    }

    private static ProviderSetupRequest SetupRequest(string account)
        => new("alpaca", "Synthetic account", account + "-key", account + "-secret", null, ["streaming"], "paper");
    private static ProviderCredentialSaveRequest Credentials(string key)
        => new("alpaca", new Dictionary<string, string?> { ["KeyId"] = key, ["SecretKey"] = key + "-secret" }, "paper", "operator");

    private sealed class DelayedApplier : IProviderRuntimeCredentialApplier
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ProviderRuntimeCredentialApplyOutcome> ApplyAsync(ProviderRuntimeCredentialBinding binding, string actor, CancellationToken ct = default)
        {
            Started.SetResult();
            await Release.Task.WaitAsync(ct);
            return ProviderRuntimeCredentialApplyOutcome.Applied;
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
