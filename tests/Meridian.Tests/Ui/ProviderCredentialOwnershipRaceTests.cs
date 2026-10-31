using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.Identity.Auth;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ApplicationConfigStore = Meridian.Application.UI.ConfigStore;

namespace Meridian.Tests.Ui;

/// <summary>
/// Operator ownership changes during a provider outage must invalidate a late verification
/// response without borrowing credentials or publishing success for the replacement owner.
/// </summary>
public sealed class ProviderCredentialOwnershipRaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-ownership-races", Guid.NewGuid().ToString("N"));
    private ApplicationConfigStore Config => new(Path.Combine(_root, "appsettings.json"));
    private static ProviderCredentialScope Scope => new("tenant-a", "owned", "account-a", "paper");

    [Theory]
    [InlineData("deleted")]
    [InlineData("tenant")]
    [InlineData("account")]
    [InlineData("environment")]
    [InlineData("provider")]
    [InlineData("disabled")]
    public async Task Verification_OwnershipChangesDuringProviderRequest_RefusesLateSuccess(string change)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new HeldAccountResponse();
        await using var app = await CreateAppAsync(handler, timeout.Token);
        await new ProviderConnectionService(Config).UpsertForTenantAsync(
            new("owned", "alpaca", "Owned paper account", ExternalAccountId: "account-a"), "tenant-a", "paper", timeout.Token);
        var store = app.Services.GetRequiredService<FileProviderCredentialStore>();
        await store.SaveScopedAsync(Credentials("selected-key"), Scope, timeout.Token);
        var generation = (await store.ReadScopedAsync("alpaca", Scope, timeout.Token))!.CredentialGeneration;

        using var client = app.GetTestClient();
        var pending = client.PostAsJsonAsync("/api/providers/alpaca/verify?connectionId=owned", new { }, timeout.Token);
        await handler.RequestStarted.Task.WaitAsync(timeout.Token);

        // A distinct configuration authority can commit while the provider call is pending.
        // No sleep is needed: the response remains held until that ownership write finishes.
        await Config.UpdateAsync(config =>
        {
            var section = config.ProviderConnections!;
            var connection = section.Connections!.Single();
            var replacement = change switch
            {
                "tenant" => connection with { TenantId = "tenant-b" },
                "account" => connection with { ExternalAccountId = "account-b" },
                "environment" => connection with { CredentialEnvironment = "live" },
                "provider" => connection with { ProviderFamilyId = "polygon" },
                "disabled" => connection with { Enabled = false },
                _ => connection
            };
            return config with { ProviderConnections = section with { Connections = change == "deleted" ? [] : [replacement] } };
        }, timeout.Token);
        handler.Release.TrySetResult();

        using var response = await pending;
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var unchanged = (await store.ReadScopedAsync("alpaca", Scope, timeout.Token))!;
        unchanged.CredentialGeneration.Should().Be(generation);
        unchanged.LastVerifiedAt.Should().BeNull();
        unchanged.LastSuccessfulAt.Should().BeNull();
        (await store.GetScopedStatusAsync("alpaca", Scope, timeout.Token)).VerificationState
            .Should().Be(ProviderVerificationStateDto.NotVerified);
        handler.PresentedKeys.Should().Equal("selected-key");
    }

    [Fact]
    public async Task Verification_OwnershipAppearsDuringLegacyProviderRequest_RefusesLateLegacySuccess()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new HeldAccountResponse();
        await using var app = await CreateAppAsync(handler, timeout.Token);
        var store = app.Services.GetRequiredService<FileProviderCredentialStore>();
        await store.SaveAsync(Credentials("legacy-key"), timeout.Token);
        var generation = (await store.ReadForProviderAsync("alpaca", timeout.Token))!.CredentialGeneration;

        using var client = app.GetTestClient();
        var pending = client.PostAsJsonAsync("/api/providers/alpaca/verify", new { }, timeout.Token);
        await handler.RequestStarted.Task.WaitAsync(timeout.Token);
        await new ProviderConnectionService(Config).UpsertForTenantAsync(
            new("owned", "alpaca", "Owned paper account", ExternalAccountId: "account-a"), "tenant-a", "paper", timeout.Token);
        handler.Release.TrySetResult();

        using var response = await pending;
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var unchanged = (await store.ReadForProviderAsync("alpaca", timeout.Token))!;
        unchanged.CredentialGeneration.Should().Be(generation);
        unchanged.LastVerifiedAt.Should().BeNull();
        (await store.ReadScopedAsync("alpaca", Scope, timeout.Token)).Should().BeNull();
        handler.PresentedKeys.Should().Equal("legacy-key");
    }

    [Fact]
    public async Task Verification_CanceledWhileWaitingForOwnershipLease_NeverContactsProvider()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new HeldAccountResponse();
        await using var app = await CreateAppAsync(handler, timeout.Token);
        await new ProviderConnectionService(Config).UpsertForTenantAsync(
            new("owned", "alpaca", "Owned paper account", ExternalAccountId: "account-a"), "tenant-a", "paper", timeout.Token);
        var store = app.Services.GetRequiredService<FileProviderCredentialStore>();
        await store.SaveScopedAsync(Credentials("selected-key"), Scope, timeout.Token);
        var selected = app.Services.GetRequiredService<ProviderConnectionLifecycleService>()
            .ForConnection("owned", "tenant-a", "alpaca");
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        using var lease = await Config.AcquireWriteLeaseAsync(timeout.Token);
        var pending = selected.VerifyAsync("alpaca", requestCancellation.Token, "operator-a");
        requestCancellation.Cancel();

        Func<Task> verify = () => pending.WaitAsync(timeout.Token);
        await verify.Should().ThrowAsync<OperationCanceledException>();
        handler.PresentedKeys.Should().BeEmpty();
        (await store.ReadScopedAsync("alpaca", Scope, timeout.Token))!.LastVerifiedAt.Should().BeNull();
    }

    [Fact]
    public async Task LegacyVerification_ConfigMissingAfterRollback_RefusesBeforeReadingRestoredLegacyCredentials()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new HeldAccountResponse();
        handler.Release.TrySetResult();
        await using var app = await CreateAppAsync(handler, timeout.Token);
        var store = app.Services.GetRequiredService<FileProviderCredentialStore>();
        await store.SaveAsync(Credentials("legacy-key"), timeout.Token);
        var generation = (await store.ReadForProviderAsync("alpaca", timeout.Token))!.CredentialGeneration;
        await new ProviderConnectionService(Config).UpsertForTenantAsync(
            new("owned", "alpaca", "Owned paper account", ExternalAccountId: "account-a"), "tenant-a", "paper", timeout.Token);
        var ownership = new ProviderRuntimeCredentialService(Config, store);
        await ownership.TransferAsync("alpaca", "owned", "tenant-a", "migration-a", generation, "operator-a", false, timeout.Token);
        await ownership.BindAsync("alpaca", "owned", "tenant-a", timeout.Token);
        await ownership.TransferAsync("alpaca", "owned", "tenant-a", "migration-a", null, "operator-a", true, timeout.Token);
        var vaultBefore = await File.ReadAllBytesAsync(store.VaultPath, timeout.Token);
        var auditPath = Path.Combine(Path.GetDirectoryName(store.VaultPath)!, "provider-credentials.audit.jsonl");
        var auditBefore = await File.ReadAllBytesAsync(auditPath, timeout.Token);
        var observedStore = new Mock<IProviderCredentialStore>(MockBehavior.Strict);
        observedStore.Setup(value => value.ReadForProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string providerId, CancellationToken ct) => store.ReadForProviderAsync(providerId, ct));
        observedStore.Setup(value => value.GetStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string providerId, CancellationToken ct) => store.GetStatusAsync(providerId, ct));
        observedStore.Setup(value => value.RecordVerificationAsync(It.IsAny<ProviderCredentialVerificationUpdate>(), It.IsAny<CancellationToken>()))
            .Returns((ProviderCredentialVerificationUpdate update, CancellationToken ct) => store.RecordVerificationAsync(update, ct));
        var lifecycle = new ProviderConnectionLifecycleService(observedStore.Object, new ConfigStore(Config.ConfigPath),
            NullLogger<ProviderConnectionLifecycleService>.Instance, new HeldHttpClientFactory(handler));

        // A lost configuration file cannot prove that this provider is an entirely legacy family.
        File.Delete(Config.ConfigPath);
        Func<Task> verify = () => lifecycle.VerifyAsync("alpaca", timeout.Token, "operator-a");

        await verify.Should().ThrowAsync<IOException>();
        observedStore.Verify(value => value.ReadForProviderAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        observedStore.Verify(value => value.RecordVerificationAsync(It.IsAny<ProviderCredentialVerificationUpdate>(), It.IsAny<CancellationToken>()), Times.Never);
        handler.PresentedKeys.Should().BeEmpty();
        (await File.ReadAllBytesAsync(store.VaultPath, timeout.Token)).Should().Equal(vaultBefore);
        (await File.ReadAllBytesAsync(auditPath, timeout.Token)).Should().Equal(auditBefore);
    }

    [Fact]
    public async Task LegacyVerification_DanglingRuntimeBinding_RefusesBeforeProviderRequest()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var handler = new HeldAccountResponse();
        handler.Release.TrySetResult();
        await using var app = await CreateAppAsync(handler, timeout.Token);
        var store = app.Services.GetRequiredService<FileProviderCredentialStore>();
        await store.SaveAsync(Credentials("legacy-key"), timeout.Token);
        await Config.UpdateAsync(config => config with
        {
            ProviderConnections = new ProviderConnectionsConfig
            {
                RuntimeCredentials = [new("ALPACA", "tenant-a", "removed-owner")]
            }
        }, timeout.Token);
        var vaultBefore = await File.ReadAllBytesAsync(store.VaultPath, timeout.Token);
        var auditPath = Path.Combine(Path.GetDirectoryName(store.VaultPath)!, "provider-credentials.audit.jsonl");
        var auditBefore = await File.ReadAllBytesAsync(auditPath, timeout.Token);
        Func<Task> verify = () => app.Services.GetRequiredService<ProviderConnectionLifecycleService>()
            .VerifyAsync("alpaca", timeout.Token, "operator-a");

        await verify.Should().ThrowAsync<UnauthorizedAccessException>();
        handler.PresentedKeys.Should().BeEmpty();
        (await File.ReadAllBytesAsync(store.VaultPath, timeout.Token)).Should().Equal(vaultBefore);
        (await File.ReadAllBytesAsync(auditPath, timeout.Token)).Should().Equal(auditBefore);
    }

    private async Task<WebApplication> CreateAppAsync(HeldAccountResponse handler, CancellationToken ct)
    {
        Directory.CreateDirectory(_root);
        await Config.SaveAsync(new AppConfig(DataRoot: _root), ct);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(new FileProviderCredentialStore(_root));
        builder.Services.AddSingleton<IProviderCredentialStore>(services => services.GetRequiredService<FileProviderCredentialStore>());
        builder.Services.AddSingleton(new ConfigStore(Config.ConfigPath));
        builder.Services.AddSingleton<IHttpClientFactory>(new HeldHttpClientFactory(handler));
        builder.Services.AddSingleton(NullLogger<ProviderConnectionLifecycleService>.Instance);
        builder.Services.AddSingleton<ProviderConnectionLifecycleService>();
        builder.Services.AddRateLimiter(options => options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ => RateLimitPartition.GetNoLimiter("test")));
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ManageCredentials;
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "operator-a";
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "tenant-a";
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-a";
            await next();
        });
        app.UseRateLimiter();
        app.MapProviderConnectionEndpoints(new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await app.StartAsync(ct);
        return app;
    }

    private static ProviderCredentialSaveRequest Credentials(string key)
        => new("alpaca", new Dictionary<string, string?> { ["KeyId"] = key, ["SecretKey"] = "synthetic-secret" }, "paper");

    private sealed class HeldHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class HeldAccountResponse : HttpMessageHandler
    {
        public TaskCompletionSource RequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> PresentedKeys { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            request.RequestUri!.AbsoluteUri.Should().Be("https://paper-api.alpaca.markets/v2/account");
            PresentedKeys.Add(request.Headers.GetValues("APCA-API-KEY-ID").Single());
            RequestStarted.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { id = "provider-account-id", account_number = "account-a" })
            };
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
