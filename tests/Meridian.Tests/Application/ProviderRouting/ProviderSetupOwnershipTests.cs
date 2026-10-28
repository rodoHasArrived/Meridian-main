using System.Text.Json;
using FluentAssertions;
using Meridian.Application.ProviderRouting;
using Meridian.Application.UI;
using Meridian.Contracts.Api;
using Meridian.DataIntegration.Credentials;
using Moq;

namespace Meridian.Tests.Application.ProviderRouting;

public sealed class ProviderSetupOwnershipTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-tests", "setup-ownership", Guid.NewGuid().ToString("N"));

    private string ConfigPath => Path.Combine(_root, "appsettings.json");

    [Fact]
    public async Task ConfigureOwnedConnection_RechecksRemovalCommittedBeforeItAcquiresTheLease()
    {
        await CreateOwnedConnectionAsync();
        var vault = new FileProviderCredentialStore(_root);
        var service = CreateService(vault);
        Task<ProviderSetupResult> pending;
        using (await new ConfigStore(ConfigPath).AcquireWriteLeaseAsync())
        {
            pending = service.ConfigureForConnectionAsync(Request(), "owned", "tenant-a", "operator");
            // A separate writer owns the lease and publishes its own serialized document.
            await File.WriteAllTextAsync(ConfigPath, "{}");
        }

        Func<Task> configure = async () => await pending.WaitAsync(TimeSpan.FromSeconds(10));
        await configure.Should().ThrowAsync<UnauthorizedAccessException>();
        File.Exists(vault.VaultPath).Should().BeFalse("removed ownership must not authorize a vault write");
        (await File.ReadAllTextAsync(ConfigPath)).Should().Be("{}");
    }

    [Fact]
    public async Task ConfigureOwnedConnection_CancellationWhileWaitingNeverReachesTheVault()
    {
        await CreateOwnedConnectionAsync();
        var vault = new Mock<IScopedProviderCredentialStore>(MockBehavior.Strict);
        var service = CreateService(vault.Object);
        using var lease = await new ConfigStore(ConfigPath).AcquireWriteLeaseAsync();
        using var cancellation = new CancellationTokenSource();
        var pending = service.ConfigureForConnectionAsync(Request(), "owned", "tenant-a", "operator", cancellation.Token);
        cancellation.Cancel();

        Func<Task> configure = async () => await pending.WaitAsync(TimeSpan.FromSeconds(10));
        await configure.Should().ThrowAsync<OperationCanceledException>();
        vault.VerifyNoOtherCalls();
        (await new ProviderConnectionService(new ConfigStore(ConfigPath)).GetConnectionAsync("owned")).Should().NotBeNull();
    }

    [Theory]
    [InlineData("save")]
    [InlineData("status")]
    public async Task ConfigureOwnedConnection_HoldsOwnershipUntilTheScopedVaultOperationCompletes(string phase)
    {
        await CreateOwnedConnectionAsync();
        var realVault = new FileProviderCredentialStore(_root);
        var vault = new Mock<IScopedProviderCredentialStore>(MockBehavior.Strict);
        var enteredOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vault.Setup(store => store.SaveScopedAsync(It.IsAny<ProviderCredentialSaveRequest>(), It.IsAny<ProviderCredentialScope>(), It.IsAny<CancellationToken>()))
            .Returns(async (ProviderCredentialSaveRequest request, ProviderCredentialScope scope, CancellationToken ct) =>
            {
                if (phase == "save")
                {
                    enteredOperation.SetResult();
                    await releaseOperation.Task.WaitAsync(ct);
                }
                await realVault.SaveScopedAsync(request, scope, ct);
            });
        vault.Setup(store => store.GetScopedStatusAsync(It.IsAny<string>(), It.IsAny<ProviderCredentialScope>(), It.IsAny<CancellationToken>()))
            .Returns(async (string provider, ProviderCredentialScope scope, CancellationToken ct) =>
            {
                if (phase == "status")
                {
                    enteredOperation.SetResult();
                    await releaseOperation.Task.WaitAsync(ct);
                }
                return await realVault.GetScopedStatusAsync(provider, scope, ct);
            });
        var setup = CreateService(vault.Object).ConfigureForConnectionAsync(Request(), "owned", "tenant-a", "operator");
        Task<IDisposable>? competingLease = null;
        try
        {
            await enteredOperation.Task.WaitAsync(TimeSpan.FromSeconds(10));
            competingLease = new ConfigStore(ConfigPath).AcquireWriteLeaseAsync();
            competingLease.IsCompleted.Should().BeFalse("a connection writer must wait until the credential operation releases ownership");
            (await new ProviderConnectionService(new ConfigStore(ConfigPath)).GetConnectionAsync("owned")).Should().NotBeNull();
        }
        finally
        {
            releaseOperation.TrySetResult();
            await setup.WaitAsync(TimeSpan.FromSeconds(10));
            if (competingLease is not null)
                (await competingLease.WaitAsync(TimeSpan.FromSeconds(10))).Dispose();
        }

        (await setup).Success.Should().BeTrue();
        (await new ProviderConnectionService(new ConfigStore(ConfigPath)).DeleteForTenantAsync("owned", "tenant-a")).Should().BeTrue();
        var retained = await realVault.ReadScopedAsync("alpaca", new ProviderCredentialScope("tenant-a", "owned", "account-a", "paper"));
        retained!.Get("KeyId").Should().Be("owned-key");
        (await new ProviderConnectionService(new ConfigStore(ConfigPath)).GetConnectionAsync("owned")).Should().BeNull();
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    public async Task ConfigureOwnedConnection_RequiresReadableOwnershipBeforeAnyVaultCall(string body)
    {
        await CreateOwnedConnectionAsync();
        await File.WriteAllTextAsync(ConfigPath, body);
        var vault = new Mock<IScopedProviderCredentialStore>(MockBehavior.Strict);
        Func<Task> configure = () => CreateService(vault.Object).ConfigureForConnectionAsync(Request(), "owned", "tenant-a", "operator");

        var exception = await Record.ExceptionAsync(configure);
        exception.Should().NotBeNull();
        exception!.GetType().Should().Be(body == "null" ? typeof(InvalidDataException) : typeof(JsonException));
        vault.VerifyNoOtherCalls();
        (await File.ReadAllTextAsync(ConfigPath)).Should().Be(body);
    }

    private async Task CreateOwnedConnectionAsync()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(ConfigPath, "{}");
        await new ProviderConnectionService(new ConfigStore(ConfigPath)).UpsertForTenantAsync(
            new CreateProviderConnectionRequest("owned", "alpaca", "Owned", ExternalAccountId: "account-a"), "tenant-a", "paper");
    }

    private ProviderSetupService CreateService(IProviderCredentialStore vault)
        => new(new ConfigStore(ConfigPath), vault, new ProviderSetupRegistry(DefaultProviderSetupHandlers.Create()));

    private static ProviderSetupRequest Request()
        => new("alpaca", "Owned", "owned-key", "owned-secret", null, ["streaming"], "paper");

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
