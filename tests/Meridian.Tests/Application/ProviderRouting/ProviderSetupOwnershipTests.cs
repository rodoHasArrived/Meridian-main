using System.Text.Json;
using FluentAssertions;
using Meridian.Application.ProviderRouting;
using Meridian.Application.UI;
using Meridian.Contracts.Api;
using Meridian.Core.Config;
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

    [Theory]
    [InlineData("owned")]
    [InlineData("incomplete")]
    [InlineData("duplicate")]
    [InlineData("case-variant")]
    [InlineData("alias")]
    [InlineData("missing-tenant")]
    [InlineData("missing-tenant-and-account")]
    [InlineData("empty-environment-without-tenant")]
    public async Task ConfigureLegacy_RefusesOwnedOrAmbiguousFamilyBeforeAnyVaultCall(string ownership)
    {
        await CreateOwnedConnectionAsync();
        var config = new ConfigStore(ConfigPath).LoadRequired();
        var connection = config.ProviderConnections!.Connections!.Single();
        connection = ownership switch
        {
            "incomplete" => connection with { TenantId = "", ExternalAccountId = null },
            "duplicate" => connection with { TenantId = null, CredentialEnvironment = null },
            "case-variant" => connection with { ProviderFamilyId = "ALPACA" },
            "alias" => connection with { ProviderFamilyId = "alpaca-api" },
            "missing-tenant" => connection with { TenantId = null },
            "missing-tenant-and-account" => connection with { TenantId = null, ExternalAccountId = null },
            "empty-environment-without-tenant" => connection with { TenantId = null, CredentialEnvironment = "" },
            _ => connection
        };
        var retained = config with
        {
            ProviderConnections = config.ProviderConnections with
            {
                Connections = ownership == "duplicate"
                    ? [connection, connection with { ConnectionId = "OWNED" }]
                    : [connection]
            }
        };
        await File.WriteAllTextAsync(ConfigPath, JsonSerializer.Serialize(retained, AppConfigJsonOptions.Write));
        var before = await File.ReadAllTextAsync(ConfigPath);
        var vault = new Mock<IProviderCredentialStore>(MockBehavior.Strict);

        var result = await CreateService(vault.Object).ConfigureAsync(Request());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("authorized connection");
        vault.VerifyNoOtherCalls();
        (await File.ReadAllTextAsync(ConfigPath)).Should().Be(before);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    public async Task ConfigureLegacy_RequiresReadableOwnershipBeforeAnyVaultCall(string body)
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(ConfigPath, body);
        var vault = new Mock<IProviderCredentialStore>(MockBehavior.Strict);

        var result = await CreateService(vault.Object).ConfigureAsync(Request());

        result.Success.Should().BeFalse();
        vault.VerifyNoOtherCalls();
        (await File.ReadAllTextAsync(ConfigPath)).Should().Be(body);
    }

    [Theory]
    [InlineData("save")]
    [InlineData("status")]
    public async Task ConfigureLegacy_HoldsOwnershipLeaseThroughVaultSaveAndStatus(string phase)
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(ConfigPath, "{}");
        var realVault = new FileProviderCredentialStore(_root);
        var vault = new Mock<IProviderCredentialStore>(MockBehavior.Strict);
        var enteredOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOperation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vault.Setup(store => store.SaveAsync(It.IsAny<ProviderCredentialSaveRequest>(), It.IsAny<CancellationToken>()))
            .Returns(async (ProviderCredentialSaveRequest request, CancellationToken ct) =>
            {
                if (phase == "save")
                {
                    enteredOperation.SetResult();
                    await releaseOperation.Task.WaitAsync(ct);
                }
                await realVault.SaveAsync(request, ct);
            });
        vault.Setup(store => store.GetStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string provider, CancellationToken ct) =>
            {
                if (phase == "status")
                {
                    enteredOperation.SetResult();
                    await releaseOperation.Task.WaitAsync(ct);
                }
                return await realVault.GetStatusAsync(provider, ct);
            });
        var setup = CreateService(vault.Object).ConfigureAsync(Request());
        Task<IDisposable>? competingLease = null;
        try
        {
            await enteredOperation.Task.WaitAsync(TimeSpan.FromSeconds(10));
            competingLease = new ConfigStore(ConfigPath).AcquireWriteLeaseAsync();
            competingLease.IsCompleted.Should().BeFalse("connection ownership cannot change during a legacy vault operation");
        }
        finally
        {
            releaseOperation.TrySetResult();
            if (competingLease is not null)
                (await competingLease.WaitAsync(TimeSpan.FromSeconds(10))).Dispose();
        }

        (await setup.WaitAsync(TimeSpan.FromSeconds(10))).Success.Should().BeTrue();
        (await realVault.ReadForProviderAsync("alpaca"))!.Get("KeyId").Should().Be("owned-key");
    }

    [Fact]
    public async Task ConfigureLegacy_RefusesPublishingAReferenceWhenOwnershipArrivesAfterVaultPhase()
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(ConfigPath, "{}");
        var realHandler = new AlpacaProviderSetupHandler();
        var handler = new Mock<IProviderSetupHandler>(MockBehavior.Strict);
        handler.SetupGet(value => value.Descriptor).Returns(realHandler.Descriptor);
        handler.Setup(value => value.CanHandle("alpaca")).Returns(true);
        handler.Setup(value => value.Validate(It.IsAny<ProviderSetupContext>()))
            .Returns((ProviderSetupContext context) => realHandler.Validate(context));
        handler.Setup(value => value.BuildExecution(It.IsAny<ProviderSetupContext>(), It.IsAny<ProviderCredentialStoreStatus>()))
            .Returns((ProviderSetupContext context, ProviderCredentialStoreStatus status) =>
            {
                // The vault phase has finished and released the connection lease. Publish a new
                // owner through the real configuration writer before setup attempts its commit.
                new ProviderConnectionService(new ConfigStore(ConfigPath)).UpsertForTenantAsync(
                    new CreateProviderConnectionRequest("owned", "alpaca", "Owned", ExternalAccountId: "account-a"),
                    "tenant-a", "paper").GetAwaiter().GetResult();
                return realHandler.BuildExecution(context, status);
            });
        var vault = new FileProviderCredentialStore(_root);
        var service = new ProviderSetupService(new ConfigStore(ConfigPath), vault, new ProviderSetupRegistry([handler.Object]));

        var result = await service.ConfigureAsync(Request());

        result.Success.Should().BeFalse();
        var config = new ConfigStore(ConfigPath).LoadRequired();
        config.ProviderConnections!.Connections.Should().ContainSingle(connection => connection.ConnectionId == "owned");
        config.ProviderConnections.Connections!.Single().CredentialReference.Should().BeNull();
        config.DataSources?.Sources.Should().NotContain(source => source.Id == "alpaca");
        (await vault.ReadScopedAsync("alpaca", new ProviderCredentialScope("tenant-a", "owned", "account-a", "paper")))
            .Should().BeNull("the earlier legacy save never becomes evidence for the new owner's vault key");
    }

    [Fact]
    public async Task ConfigureOwnedConnection_RefusesForeignTenantBeforeAnyVaultCall()
    {
        await CreateOwnedConnectionAsync();
        var before = await File.ReadAllTextAsync(ConfigPath);
        var vault = new Mock<IScopedProviderCredentialStore>(MockBehavior.Strict);

        Func<Task> configure = () => CreateService(vault.Object).ConfigureForConnectionAsync(Request(), "owned", "tenant-b", "operator");

        await configure.Should().ThrowAsync<UnauthorizedAccessException>();
        vault.VerifyNoOtherCalls();
        (await File.ReadAllTextAsync(ConfigPath)).Should().Be(before);
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
