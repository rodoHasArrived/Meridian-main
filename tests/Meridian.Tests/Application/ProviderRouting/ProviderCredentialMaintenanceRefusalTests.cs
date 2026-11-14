using FluentAssertions;
using Meridian.Application.ProviderRouting;
using Meridian.Application.UI;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Lifecycle = Meridian.Ui.Shared.Services.ProviderConnectionLifecycleService;
using WorkstationConfigStore = Meridian.Ui.Shared.Services.ConfigStore;

namespace Meridian.Tests.Application.ProviderRouting;

public sealed class ProviderCredentialMaintenanceRefusalTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-maintenance-refusal", Guid.NewGuid().ToString("N"));
    private static readonly ProviderCredentialScope Scope = new("tenant-a", "owned", "account-a", "paper");
    private static ProviderCredentialOwnershipPolicy Required => new(true);
    private ConfigStore Config => new(Path.Combine(_root, "appsettings.json"), Required);

    [Theory]
    [InlineData("save")]
    [InlineData("delete")]
    [InlineData("setup")]
    [InlineData("bind")]
    public async Task RefusedMaintenance_PreservesTheEffectiveStartupSelection(string operation)
    {
        var vault = await InitializeAsync();
        await BindBeforeStartupAsync(vault);
        var tracker = new ProviderRuntimeCredentialApplicationTracker();
        var guard = new ProviderRuntimeCredentialMutationGuard(Config, Required, tracker: tracker);
        var runtime = Runtime(vault, guard, tracker);
        var status = runtime.GetApplicationStatus("alpaca", Scope.ConnectionId, Scope.TenantId);
        status.PendingRestart.Should().BeFalse("this retained selection was effective when the host started");
        var configuration = await File.ReadAllBytesAsync(Config.ConfigPath);
        var generation = (await vault.ReadScopedAsync("alpaca", Scope))!.CredentialGeneration;
        var selected = Lifecycle(vault, guard).ForConnection(Scope.ConnectionId, Scope.TenantId, "alpaca");
        Func<Task> mutate = operation switch
        {
            "save" => () => selected.SaveCredentialsAsync("alpaca", Upsert("replacement-key")),
            "delete" => () => selected.DeleteCredentialsAsync("alpaca", "operator"),
            "setup" => () => new ProviderSetupService(Config, vault,
                new ProviderSetupRegistry(DefaultProviderSetupHandlers.Create()), Required, guard)
                .ConfigureForConnectionAsync(new("alpaca", "Owned", "replacement-key", "replacement-secret", null, [], "paper"),
                    Scope.ConnectionId, Scope.TenantId, "operator"),
            _ => () => runtime.BindAsync("alpaca", Scope.ConnectionId, Scope.TenantId)
        };

        await mutate.Should().ThrowAsync<ProviderRuntimeCredentialQuiescenceException>();

        runtime.GetApplicationStatus("alpaca", Scope.ConnectionId, Scope.TenantId).Should().Be(status);
        (await File.ReadAllBytesAsync(Config.ConfigPath)).Should().Equal(configuration);
        (await vault.ReadScopedAsync("alpaca", Scope))!.CredentialGeneration.Should().Be(generation);
    }

    [Theory]
    [InlineData(" owned ", "tenant-a", false)]
    [InlineData("\tOWNED\n", "tenant-a", false)]
    [InlineData(" owned ", "tenant-b", false)]
    [InlineData(" owned ", "tenant-a", true)]
    public async Task PaddedDisable_CannotBypassDrainOrOwnershipChecks(string connectionId, string tenantId, bool cancel)
    {
        var vault = await InitializeAsync();
        await BindBeforeStartupAsync(vault);
        var tracker = new ProviderRuntimeCredentialApplicationTracker();
        var guard = new ProviderRuntimeCredentialMutationGuard(Config, Required, tracker: tracker);
        var runtime = Runtime(vault, guard, tracker);
        var status = runtime.GetApplicationStatus("alpaca", Scope.ConnectionId, Scope.TenantId);
        var configuration = await File.ReadAllBytesAsync(Config.ConfigPath);
        var generation = (await vault.ReadScopedAsync("alpaca", Scope))!.CredentialGeneration;
        using var cancellation = new CancellationTokenSource();
        if (cancel)
            cancellation.Cancel();
        Func<Task> disable = () => new ProviderConnectionService(Config, Required, guard).UpsertForTenantAsync(
            new(connectionId, "alpaca", "Disabled", Enabled: false, ExternalAccountId: Scope.ExternalAccountId),
            tenantId, Scope.Environment, cancellation.Token);

        if (cancel)
            await disable.Should().ThrowAsync<OperationCanceledException>();
        else if (tenantId != Scope.TenantId)
            await disable.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ownership*");
        else
            await disable.Should().ThrowAsync<ProviderRuntimeCredentialQuiescenceException>();

        (await File.ReadAllBytesAsync(Config.ConfigPath)).Should().Equal(configuration);
        Config.LoadRequired().ProviderConnections!.Connections!.Single().Enabled.Should().BeTrue();
        (await vault.ReadScopedAsync("alpaca", Scope))!.CredentialGeneration.Should().Be(generation);
        runtime.GetApplicationStatus("alpaca", Scope.ConnectionId, Scope.TenantId).Should().Be(status);
    }

    [Fact]
    public async Task PaddedDisable_WithDrainAuthorityStopsTheCanonicalConnectionBeforeWriting()
    {
        var vault = await InitializeAsync();
        await BindBeforeStartupAsync(vault);
        var tracker = new ProviderRuntimeCredentialApplicationTracker();
        var applier = new Mock<IProviderRuntimeCredentialApplier>(MockBehavior.Strict);
        applier.Setup(value => value.StopAndDrainAsync(It.IsAny<ProviderRuntimeCredentialBinding>(), It.IsAny<CancellationToken>()))
            .Returns((ProviderRuntimeCredentialBinding binding, CancellationToken _) =>
            {
                binding.ConnectionId.Should().Be(Scope.ConnectionId);
                Config.LoadRequired().ProviderConnections!.Connections!.Single().Enabled.Should().BeTrue();
                return Task.FromResult(true);
            });
        var guard = new ProviderRuntimeCredentialMutationGuard(Config, Required, applier.Object, tracker);

        var result = await new ProviderConnectionService(Config, Required, guard).UpsertForTenantAsync(
            new(" OWNED ", "alpaca", "Disabled", Enabled: false, ExternalAccountId: Scope.ExternalAccountId),
            Scope.TenantId, Scope.Environment);

        result.ConnectionId.Should().Be(Scope.ConnectionId);
        Config.LoadRequired().ProviderConnections!.Connections!.Single().Enabled.Should().BeFalse();
        tracker.Get("alpaca").PendingRestart.Should().BeTrue();
        applier.Verify(value => value.StopAndDrainAsync(It.IsAny<ProviderRuntimeCredentialBinding>(), It.IsAny<CancellationToken>()), Times.Once);
        applier.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("save")]
    [InlineData("delete")]
    public async Task DisabledOwnedCredentials_ReturnTheCommittedStatusWithoutEnablingRuntime(string operation)
    {
        var vault = await InitializeAsync(enabled: false);
        var configuration = await File.ReadAllBytesAsync(Config.ConfigPath);
        var guard = new ProviderRuntimeCredentialMutationGuard(Config, Required);
        var lifecycle = Lifecycle(vault, guard);
        var selected = lifecycle.ForConnection(Scope.ConnectionId, Scope.TenantId, "alpaca");

        var result = operation == "save"
            ? await selected.SaveCredentialsAsync("alpaca", Upsert("replacement-key"))
            : await selected.DeleteCredentialsAsync("alpaca", "operator");

        var expected = operation == "save" ? ProviderCredentialStateDto.Configured : ProviderCredentialStateDto.Missing;
        result.CredentialState.Should().Be(expected);
        (await lifecycle.GetConnectionStatusForTenantAsync(Scope.ConnectionId, Scope.TenantId)).CredentialState.Should().Be(expected);
        var retained = await vault.ReadScopedAsync("alpaca", Scope);
        if (operation == "save")
            retained!.Get("KeyId").Should().Be("replacement-key");
        else
            retained.Should().BeNull();
        (await File.ReadAllBytesAsync(Config.ConfigPath)).Should().Equal(configuration);
        Func<Task> verify = () => selected.VerifyAsync("alpaca");
        await verify.Should().ThrowAsync<UnauthorizedAccessException>("a disabled connection cannot dispatch a provider verification");
        Func<Task> bind = () => new ProviderRuntimeCredentialService(Config, vault, ownershipPolicy: Required)
            .BindAsync("alpaca", Scope.ConnectionId, Scope.TenantId);
        await bind.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Theory]
    [InlineData("save")]
    [InlineData("delete")]
    [InlineData("status")]
    public async Task DisabledCredentialAccess_StillRechecksTheExactTenantAndAccount(string operation)
    {
        var vault = await InitializeAsync(enabled: false);
        await vault.SaveAsync(Credentials("legacy-key"));
        var foreign = new ProviderCredentialScope("tenant-b", Scope.ConnectionId, "account-b", "paper");
        await vault.SaveScopedAsync(Credentials("foreign-key"), foreign);
        var lifecycle = Lifecycle(vault, new ProviderRuntimeCredentialMutationGuard(Config, Required));
        var selected = lifecycle.ForConnection(Scope.ConnectionId, Scope.TenantId, "alpaca");
        await Config.UpdateAsync(config => config with
        {
            ProviderConnections = config.ProviderConnections! with
            {
                Connections = [config.ProviderConnections.Connections!.Single() with
                {
                    TenantId = foreign.TenantId,
                    ExternalAccountId = foreign.ExternalAccountId
                }]
            }
        });
        var configuration = await File.ReadAllBytesAsync(Config.ConfigPath);
        var ownedGeneration = (await vault.ReadScopedAsync("alpaca", Scope))!.CredentialGeneration;
        var foreignGeneration = (await vault.ReadScopedAsync("alpaca", foreign))!.CredentialGeneration;
        var legacyGeneration = (await vault.ReadForProviderAsync("alpaca"))!.CredentialGeneration;
        Func<Task> access = operation switch
        {
            "save" => () => selected.SaveCredentialsAsync("alpaca", Upsert("forbidden-key")),
            "delete" => () => selected.DeleteCredentialsAsync("alpaca", "operator"),
            _ => () => lifecycle.GetConnectionStatusForTenantAsync(Scope.ConnectionId, Scope.TenantId)
        };

        await access.Should().ThrowAsync<UnauthorizedAccessException>();

        (await File.ReadAllBytesAsync(Config.ConfigPath)).Should().Equal(configuration);
        (await vault.ReadScopedAsync("alpaca", Scope))!.CredentialGeneration.Should().Be(ownedGeneration);
        (await vault.ReadScopedAsync("alpaca", foreign))!.CredentialGeneration.Should().Be(foreignGeneration);
        (await vault.ReadForProviderAsync("alpaca"))!.CredentialGeneration.Should().Be(legacyGeneration);
    }

    private async Task<FileProviderCredentialStore> InitializeAsync(bool enabled = true)
    {
        Directory.CreateDirectory(_root);
        await Config.SaveAsync(new AppConfig(DataRoot: _root));
        await new ProviderConnectionService(Config, Required).UpsertForTenantAsync(
            new(Scope.ConnectionId, "alpaca", "Owned", Enabled: enabled, ExternalAccountId: Scope.ExternalAccountId),
            Scope.TenantId, Scope.Environment);
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveScopedAsync(Credentials("owned-key"), Scope);
        return vault;
    }

    private async Task BindBeforeStartupAsync(FileProviderCredentialStore vault)
        => await new ProviderRuntimeCredentialService(Config, vault, ownershipPolicy: Required)
            .BindAsync("alpaca", Scope.ConnectionId, Scope.TenantId);

    private ProviderRuntimeCredentialService Runtime(FileProviderCredentialStore vault, ProviderRuntimeCredentialMutationGuard guard,
        ProviderRuntimeCredentialApplicationTracker tracker)
        => new(Config, vault, applicationTracker: tracker, mutationGuard: guard, ownershipPolicy: Required);

    private Lifecycle Lifecycle(FileProviderCredentialStore vault, ProviderRuntimeCredentialMutationGuard guard)
        => new(vault, new WorkstationConfigStore(Config.ConfigPath, Required), NullLogger<Lifecycle>.Instance,
            ownershipPolicy: Required, mutationGuard: guard);

    private static ProviderCredentialSaveRequest Credentials(string key)
        => new("alpaca", new Dictionary<string, string?> { ["KeyId"] = key, ["SecretKey"] = "synthetic-secret" }, "paper");

    private static ProviderCredentialUpsertRequestDto Upsert(string key)
        => new(Credentials(key).Credentials, "paper", "operator");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
