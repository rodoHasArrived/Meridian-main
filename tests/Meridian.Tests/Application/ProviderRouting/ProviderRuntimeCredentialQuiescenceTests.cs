using FluentAssertions;
using Meridian.Application.ProviderRouting;
using Meridian.Application.UI;
using Meridian.Contracts.Api;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.DataIntegration.AccountingSystem.QuickBooks;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Lifecycle = Meridian.Ui.Shared.Services.ProviderConnectionLifecycleService;
using WorkstationConfigStore = Meridian.Ui.Shared.Services.ConfigStore;

namespace Meridian.Tests.Application.ProviderRouting;

public sealed class ProviderRuntimeCredentialQuiescenceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-credential-quiescence", Guid.NewGuid().ToString("N"));
    private ConfigStore Config => new(Path.Combine(_root, "appsettings.json"));
    private static readonly ProviderCredentialScope Scope = new("tenant-a", "owned", "account-a", "paper");
    private static ProviderCredentialOwnershipPolicy Required => new(true);

    [Theory]
    [InlineData("credentials")]
    [InlineData("connection")]
    [InlineData("setup")]
    [InlineData("selection")]
    public async Task DefaultHost_RefusesBoundChangesWithoutClaimingThatOldInstancesStopped(string change)
    {
        var vault = await InitializeAsync();
        var tracker = new ProviderRuntimeCredentialApplicationTracker();
        var guard = new ProviderRuntimeCredentialMutationGuard(Config, Required, tracker: tracker);
        var runtime = new ProviderRuntimeCredentialService(Config, vault, applicationTracker: tracker,
            mutationGuard: guard, ownershipPolicy: Required);
        await runtime.BindAsync("alpaca", Scope.ConnectionId, Scope.TenantId);
        var before = await File.ReadAllBytesAsync(Config.ConfigPath);
        var generation = (await vault.ReadScopedAsync("alpaca", Scope))!.CredentialGeneration;
        var lifecycle = Lifecycle(vault, guard).ForConnection(Scope.ConnectionId, Scope.TenantId, "alpaca");
        Func<Task> mutation = change switch
        {
            "connection" => () => new ProviderConnectionService(Config, Required, guard).DeleteForTenantAsync(Scope.ConnectionId, Scope.TenantId),
            "setup" => () => new ProviderSetupService(Config, vault, new ProviderSetupRegistry(DefaultProviderSetupHandlers.Create()), Required, guard)
                .ConfigureForConnectionAsync(new("alpaca", "Owned", "replacement", "replacement-secret", null, [], "paper"), Scope.ConnectionId, Scope.TenantId, "operator"),
            "selection" => () => runtime.BindAsync("alpaca", Scope.ConnectionId, Scope.TenantId),
            _ => () => lifecycle.DeleteCredentialsAsync("alpaca", "operator")
        };

        await mutation.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot be stopped and drained*requested change was refused*");
        (await File.ReadAllBytesAsync(Config.ConfigPath)).Should().Equal(before);
        (await vault.ReadScopedAsync("alpaca", Scope))!.CredentialGeneration.Should().Be(generation);
        runtime.GetApplicationStatus("alpaca", Scope.ConnectionId, Scope.TenantId).PendingRestart.Should().BeTrue();
    }

    [Fact]
    public async Task Removal_DrainsCapturedKeyInstanceAndSerializesApplyBeforeAcknowledgingDeletion()
    {
        var vault = await InitializeAsync();
        var applier = new CapturedKeyRuntime(vault);
        var tracker = new ProviderRuntimeCredentialApplicationTracker();
        var guard = new ProviderRuntimeCredentialMutationGuard(Config, Required, applier, tracker);
        var runtime = new ProviderRuntimeCredentialService(Config, vault, applicationTracker: tracker,
            applier: applier, mutationGuard: guard, ownershipPolicy: Required);
        await runtime.BindAsync("alpaca", Scope.ConnectionId, Scope.TenantId);
        (await runtime.ApplyAsync("alpaca", Scope.ConnectionId, Scope.TenantId, "operator")).PendingRestart.Should().BeFalse();
        var oldInstance = applier.Active!;
        oldInstance.CapturedKey.Should().Be("account-a-key");
        var inFlight = oldInstance.DispatchAsync();
        var removal = Lifecycle(vault, guard).ForConnection(Scope.ConnectionId, Scope.TenantId, "alpaca")
            .DeleteCredentialsAsync("alpaca", "operator");
        await applier.Stopping.Task.WaitAsync(TimeSpan.FromSeconds(10));
        removal.IsCompleted.Should().BeFalse("in-flight requests still hold the captured key");
        (await vault.ReadScopedAsync("alpaca", Scope)).Should().NotBeNull();
        var application = runtime.ApplyAsync("alpaca", Scope.ConnectionId, Scope.TenantId, "operator");
        application.IsCompleted.Should().BeFalse("application cannot recreate an instance between drain and deletion");
        oldInstance.CompleteDispatch.SetResult();
        await inFlight;
        await removal.WaitAsync(TimeSpan.FromSeconds(10));
        (await application.WaitAsync(TimeSpan.FromSeconds(10))).PendingRestart.Should().BeTrue("there are no remaining selected credentials to apply");
        oldInstance.Disposed.Should().BeTrue();
        var staleDispatch = () => oldInstance.DispatchAsync();
        await staleDispatch.Should().ThrowAsync<InvalidOperationException>();
        (await new FileProviderCredentialStore(_root).ReadScopedAsync("alpaca", Scope)).Should().BeNull();
    }

    [Fact]
    public async Task SelectingAnotherAccount_StopsThePriorCapturedInstanceAndRefusesForeignTenantInterruption()
    {
        var vault = await InitializeAsync();
        var second = new ProviderCredentialScope(Scope.TenantId, "second", "account-b", "paper");
        var foreign = new ProviderCredentialScope("tenant-b", "foreign", "foreign-account", "paper");
        foreach (var owner in new[] { second, foreign })
        {
            await new ProviderConnectionService(Config).UpsertForTenantAsync(new(owner.ConnectionId, "alpaca", "Synthetic account",
                ExternalAccountId: owner.ExternalAccountId), owner.TenantId, owner.Environment);
            await vault.SaveScopedAsync(new("alpaca", new Dictionary<string, string?>
            {
                ["KeyId"] = owner.ExternalAccountId + "-key",
                ["SecretKey"] = owner.ExternalAccountId + "-secret"
            }, owner.Environment), owner);
        }
        var applier = new CapturedKeyRuntime(vault);
        var tracker = new ProviderRuntimeCredentialApplicationTracker();
        var guard = new ProviderRuntimeCredentialMutationGuard(Config, Required, applier, tracker);
        var runtime = new ProviderRuntimeCredentialService(Config, vault, applicationTracker: tracker,
            applier: applier, mutationGuard: guard, ownershipPolicy: Required);
        await runtime.BindAsync("alpaca", Scope.ConnectionId, Scope.TenantId);
        await runtime.ApplyAsync("alpaca", Scope.ConnectionId, Scope.TenantId, "operator");
        var oldInstance = applier.Active!;
        var foreignSelection = () => runtime.BindAsync("alpaca", foreign.ConnectionId, foreign.TenantId);
        await foreignSelection.Should().ThrowAsync<UnauthorizedAccessException>();
        applier.Stopping.Task.IsCompleted.Should().BeFalse("a foreign tenant cannot stop the retained owner's runtime");
        await runtime.BindAsync("alpaca", second.ConnectionId, second.TenantId);
        oldInstance.Disposed.Should().BeTrue();
        (await runtime.ApplyAsync("alpaca", second.ConnectionId, second.TenantId, "operator")).PendingRestart.Should().BeFalse();
        applier.Active!.CapturedKey.Should().Be("account-b-key");
        var staleDispatch = () => oldInstance.DispatchAsync();
        await staleDispatch.Should().ThrowAsync<InvalidOperationException>();
        (await vault.ReadScopedAsync("alpaca", foreign))!.Get("KeyId").Should().Be("foreign-account-key");
    }

    [Fact]
    public async Task CancellationDuringDrain_DoesNotDeleteCredentialsOrLeaveAnAppliedClaim()
    {
        var vault = await InitializeAsync();
        var applier = new CapturedKeyRuntime(vault);
        var tracker = new ProviderRuntimeCredentialApplicationTracker(new FakeTimeProvider(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        var guard = new ProviderRuntimeCredentialMutationGuard(Config, Required, applier, tracker);
        var runtime = new ProviderRuntimeCredentialService(Config, vault, applicationTracker: tracker,
            applier: applier, mutationGuard: guard, ownershipPolicy: Required);
        await runtime.BindAsync("alpaca", Scope.ConnectionId, Scope.TenantId);
        await runtime.ApplyAsync("alpaca", Scope.ConnectionId, Scope.TenantId, "operator");
        var oldInstance = applier.Active!;
        var inFlight = oldInstance.DispatchAsync();
        using var cancellation = new CancellationTokenSource();
        var removal = Lifecycle(vault, guard).ForConnection(Scope.ConnectionId, Scope.TenantId, "alpaca")
            .DeleteCredentialsAsync("alpaca", "operator", cancellation.Token);
        await applier.Stopping.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        var cancelled = () => removal;
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        (await vault.ReadScopedAsync("alpaca", Scope))!.Get("KeyId").Should().Be("account-a-key");
        runtime.GetApplicationStatus("alpaca", Scope.ConnectionId, Scope.TenantId).PendingRestart.Should().BeTrue();
        var staleDispatch = () => oldInstance.DispatchAsync();
        await staleDispatch.Should().ThrowAsync<InvalidOperationException>();
        oldInstance.CompleteDispatch.SetResult();
        await inFlight;
        await Lifecycle(vault, guard).ForConnection(Scope.ConnectionId, Scope.TenantId, "alpaca").DeleteCredentialsAsync("alpaca", "operator");
        oldInstance.Disposed.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderOwnedRefresh_BoundProductionScopeRetainsRotationWithoutDrainingItself(bool cancelAfterExchange)
    {
        var vault = await InitializeAsync();
        var scope = new ProviderCredentialScope("tenant-a", "accounting", "realm-a", "sandbox");
        await new ProviderConnectionService(Config).UpsertForTenantAsync(new(scope.ConnectionId, "quickbooks", "Accounting",
            ExternalAccountId: scope.ExternalAccountId), scope.TenantId, scope.Environment);
        await vault.SaveScopedAsync(new("quickbooks", new Dictionary<string, string?>
        {
            ["ClientId"] = "synthetic-client",
            ["ClientSecret"] = "synthetic-secret",
            ["RefreshToken"] = "original-refresh",
            ["RealmId"] = scope.ExternalAccountId
        }, scope.Environment), scope);
        var original = (await vault.ReadScopedAsync("quickbooks", scope))!;
        using var cancellation = new CancellationTokenSource();
        var client = new Mock<IQuickBooksOnlineClient>(MockBehavior.Strict);
        client.Setup(value => value.RefreshAccessTokenAsync(It.IsAny<QuickBooksOnlineConnection>(), It.IsAny<CancellationToken>()))
            .Returns((QuickBooksOnlineConnection connection, CancellationToken ct) =>
            {
                connection.RefreshToken.Should().Be("original-refresh");
                if (cancelAfterExchange)
                    cancellation.Cancel();
                return Task.FromResult(new QuickBooksOnlineTokenExchangeResult("synthetic-access", "rotated-refresh", DateTimeOffset.UtcNow.AddHours(1), []));
            });
        client.Setup(value => value.VerifyCompanyAsync(It.IsAny<QuickBooksOnlineConnection>(), "synthetic-access", It.IsAny<CancellationToken>()))
            .Returns((QuickBooksOnlineConnection connection, string access, CancellationToken ct) =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });
        var applier = new Mock<IProviderRuntimeCredentialApplier>(MockBehavior.Strict);
        var tracker = new ProviderRuntimeCredentialApplicationTracker();
        var guard = new ProviderRuntimeCredentialMutationGuard(Config, Required, applier.Object, tracker);
        var runtime = new ProviderRuntimeCredentialService(Config, vault, applicationTracker: tracker,
            applier: applier.Object, mutationGuard: guard, ownershipPolicy: Required);
        await runtime.BindAsync("quickbooks", scope.ConnectionId, scope.TenantId);
        var accounting = new QuickBooksOnlineAccountingProvider(new QuickBooksOnlineProviderCredentialConnectionStore(vault), client.Object);
        var lifecycle = new Lifecycle(vault, new WorkstationConfigStore(Config.ConfigPath), NullLogger<Lifecycle>.Instance,
            accountingSystemProviders: [accounting], ownershipPolicy: Required, mutationGuard: guard)
            .ForConnection(scope.ConnectionId, scope.TenantId, "quickbooks");
        var verification = () => lifecycle.VerifyAsync("quickbooks", cancellation.Token, "operator");
        if (cancelAfterExchange)
            await verification.Should().ThrowAsync<OperationCanceledException>();
        else
            (await verification()).Success.Should().BeTrue();
        var retained = (await new FileProviderCredentialStore(_root).ReadScopedAsync("quickbooks", scope))!;
        retained.Get("RefreshToken").Should().Be("rotated-refresh");
        retained.CredentialGeneration.Should().NotBe(original.CredentialGeneration);
        applier.VerifyNoOtherCalls();
    }

    private Lifecycle Lifecycle(FileProviderCredentialStore vault, ProviderRuntimeCredentialMutationGuard guard)
        => new(vault, new WorkstationConfigStore(Config.ConfigPath), NullLogger<Lifecycle>.Instance,
            ownershipPolicy: Required, mutationGuard: guard);

    private async Task<FileProviderCredentialStore> InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        await Config.SaveAsync(new AppConfig(DataRoot: _root));
        await new ProviderConnectionService(Config).UpsertForTenantAsync(new(Scope.ConnectionId, "alpaca", "Owned",
            ExternalAccountId: Scope.ExternalAccountId), Scope.TenantId, Scope.Environment);
        var vault = new FileProviderCredentialStore(_root);
        await vault.SaveScopedAsync(new("alpaca", new Dictionary<string, string?>
        {
            ["KeyId"] = "account-a-key",
            ["SecretKey"] = "account-a-secret"
        }, "paper"), Scope);
        return vault;
    }

    private sealed class CapturedKeyRuntime(FileProviderCredentialStore vault) : IProviderRuntimeCredentialApplier
    {
        public CapturedKeyInstance? Active { get; private set; }
        public TaskCompletionSource Stopping { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ProviderRuntimeCredentialApplyOutcome> ApplyAsync(ProviderRuntimeCredentialBinding binding, string actor, CancellationToken ct = default)
        {
            var selected = await vault.ReadScopedAsync(binding.ProviderId, new(binding.TenantId, binding.ConnectionId,
                binding.ExternalAccountId!, binding.Environment!), ct);
            if (selected is null)
                return ProviderRuntimeCredentialApplyOutcome.PendingRestart;
            if (Active is not null)
                await StopAndDrainAsync(binding, ct);
            Active = new CapturedKeyInstance(selected.Get("KeyId")!);
            return ProviderRuntimeCredentialApplyOutcome.Applied;
        }
        public async Task<bool> StopAndDrainAsync(ProviderRuntimeCredentialBinding binding, CancellationToken ct = default)
        {
            if (Active is { } active)
            {
                active.Accepting = false;
                Stopping.TrySetResult();
                if (active.InFlight)
                    await active.Drained.Task.WaitAsync(ct);
                active.Disposed = true;
                Active = null;
            }
            return true;
        }
    }

    private sealed class CapturedKeyInstance(string key)
    {
        public string CapturedKey { get; } = key;
        public bool Accepting { get; set; } = true;
        public bool Disposed { get; set; }
        public bool InFlight { get; private set; }
        public TaskCompletionSource CompleteDispatch { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Drained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task DispatchAsync()
        {
            if (!Accepting || Disposed)
                throw new InvalidOperationException("Provider instance no longer admits dispatches.");
            InFlight = true;
            try
            { await CompleteDispatch.Task; }
            finally { InFlight = false; Drained.TrySetResult(); }
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
