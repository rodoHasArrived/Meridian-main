using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Meridian.Contracts.AccountingSystem;
using Meridian.Identity.Auth;
using Meridian.Contracts.Configuration;
using Meridian.ProviderSdk.AccountingSystem;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Meridian.Tests.Ui;

[Collection(AlpacaCredentialEnvironmentCollection.Name)]
public sealed class ProviderConnectionEndpointsTests
{
    [Fact]
    public async Task GetProviderConnections_WithoutManageCredentials_ReturnsForbidden()
    {
        await using var app = await CreateAppAsync(_ => { }, UserPermission.ViewTrades);

        var response = await app.GetTestClient().GetAsync("/api/providers/connections");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetProviderConnections_WithoutTenantScope_ReturnsForbidden()
    {
        await using var app = await CreateAppAsync(
            _ => { },
            UserPermission.ManageCredentials,
            includeTenantScope: false);

        var response = await app.GetTestClient().GetAsync("/api/providers/connections");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PutProviderCredentials_SavesEncryptedCredentialAndReturnsMaskedStatus()
    {
        using var env = AlpacaEnvScope.Clear();
        await using var app = await CreateAppAsync(_ => { });
        var store = (FileProviderCredentialStore)app.Services.GetRequiredService<IProviderCredentialStore>();

        var response = await app.GetTestClient().PutAsync(
            "/api/providers/alpaca/credentials",
            JsonContent(new
            {
                credentials = new
                {
                    KeyId = "endpoint-key",
                    SecretKey = "endpoint-secret"
                },
                environment = "paper"
            }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadAsync<ProviderCredentialMutationResultDto>(response);
        result.ProviderId.Should().Be("alpaca");
        result.CredentialSource.Should().Be(ProviderCredentialSourceDto.LocalEncryptedStore);
        result.MaskedKeyPreview.Should().NotContain("endpoint-key");
        result.Warnings.Should().Contain(warning => warning.Contains("environment variables were not changed", StringComparison.OrdinalIgnoreCase));
        Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.KeyIdName).Should().NotBe("endpoint-key");
        Environment.GetEnvironmentVariable(AlpacaCredentialEnvironment.SecretKeyName).Should().NotBe("endpoint-secret");

        var rawVault = await File.ReadAllTextAsync(store.VaultPath);
        rawVault.Should().NotContain("endpoint-key");
        rawVault.Should().NotContain("endpoint-secret");

        var listResponse = await app.GetTestClient().GetAsync("/api/providers/connections");
        var listJson = await listResponse.Content.ReadAsStringAsync();
        listJson.Should().Contain("\"providerId\":\"alpaca\"");
        listJson.Should().NotContain("endpoint-key");
        listJson.Should().NotContain("endpoint-secret");
    }

    [Fact]
    public async Task PutProviderCredentials_UnknownCredentialFields_ReturnsBadRequestWithoutPersistingValues()
    {
        await using var app = await CreateAppAsync(_ => { });
        var store = (FileProviderCredentialStore)app.Services.GetRequiredService<IProviderCredentialStore>();
        await app.GetTestClient().PutAsync(
            "/api/providers/alpaca/credentials",
            JsonContent(new { credentials = new { KeyId = "safe-key", SecretKey = "safe-secret" }, environment = "paper" }));

        var response = await app.GetTestClient().PutAsync(
            "/api/providers/alpaca/credentials",
            JsonContent(new { credentials = new { KeyId = "replacement-key", AccessToken = "unknown-secret" }, environment = "paper" }));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("AccessToken");
        var read = await store.ReadForProviderAsync("alpaca");
        read.Should().NotBeNull();
        read!.Get("KeyId").Should().Be("safe-key");
        read.Get("AccessToken").Should().BeNull();

        var vaultText = await File.ReadAllTextAsync(store.VaultPath);
        var auditText = await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(store.VaultPath)!, "provider-credentials.audit.jsonl"));
        vaultText.Should().NotContain("unknown-secret");
        auditText.Should().NotContain("AccessToken");
        auditText.Should().NotContain("unknown-secret");
    }

    [Fact]
    public async Task PutProviderCredentials_KnownCredentialFields_AreCaseInsensitive()
    {
        await using var app = await CreateAppAsync(_ => { });
        var store = (FileProviderCredentialStore)app.Services.GetRequiredService<IProviderCredentialStore>();

        var response = await app.GetTestClient().PutAsync(
            "/api/providers/alpaca/credentials",
            JsonContent(new { credentials = new { keyid = "case-key", SECRETKEY = "case-secret" }, environment = "paper" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var read = await store.ReadForProviderAsync("alpaca");
        read.Should().NotBeNull();
        read!.Credentials.Keys.Should().BeEquivalentTo(["KeyId", "SecretKey"]);
        read.Get("KeyId").Should().Be("case-key");
        read.Get("SecretKey").Should().Be("case-secret");
    }

    [Fact]
    public async Task PutProviderCredentials_BlankKnownCredentialField_RemovesExistingValue()
    {
        await using var app = await CreateAppAsync(_ => { });
        var store = (FileProviderCredentialStore)app.Services.GetRequiredService<IProviderCredentialStore>();
        await app.GetTestClient().PutAsync(
            "/api/providers/alpaca/credentials",
            JsonContent(new { credentials = new { KeyId = "keep-key", SecretKey = "remove-secret" }, environment = "paper" }));

        var response = await app.GetTestClient().PutAsync(
            "/api/providers/alpaca/credentials",
            JsonContent(new { credentials = new { secretkey = "" }, environment = "paper" }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var read = await store.ReadForProviderAsync("alpaca");
        read.Should().NotBeNull();
        read!.Get("KeyId").Should().Be("keep-key");
        read.Get("SecretKey").Should().BeNull();
        var result = await ReadAsync<ProviderCredentialMutationResultDto>(response);
        result.CredentialState.Should().Be(ProviderCredentialStateDto.Partial);
    }


    [Fact]
    public async Task PostProviderVerify_UsesStoredAlpacaCredentialsAndRecordsAccount()
    {
        using var env = AlpacaEnvScope.Clear();
        HttpRequestMessage? capturedRequest = null;
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IHttpClientFactory>(_ => new StubHttpClientFactory(new CapturingStubHandler(
                request => capturedRequest = request,
                new StringContent("{\"account_number\":\"PA-CENTER\"}", Encoding.UTF8, "application/json"))));
        });

        await app.GetTestClient().PutAsync(
            "/api/providers/alpaca/credentials",
            JsonContent(new
            {
                credentials = new
                {
                    KeyId = "verify-key",
                    SecretKey = "verify-secret"
                },
                environment = "paper"
            }));

        var response = await app.GetTestClient().PostAsync("/api/providers/alpaca/verify", JsonContent(new { }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadAsync<ProviderCredentialVerificationResultDto>(response);
        result.Success.Should().BeTrue();
        result.VerificationState.Should().Be(ProviderVerificationStateDto.Verified);
        result.ExternalAccountId.Should().Be("PA-CENTER");
        capturedRequest.Should().NotBeNull();
        capturedRequest!.Headers.GetValues("APCA-API-KEY-ID").Should().ContainSingle().Which.Should().Be("verify-key");

        var rows = await ReadAsync<ProviderConnectionRowDto[]>(
            await app.GetTestClient().GetAsync("/api/providers/connections"));
        rows.Single(row => row.ProviderId == "alpaca").Should().Match<ProviderConnectionRowDto>(row =>
            row.CredentialState == ProviderCredentialStateDto.Verified &&
            row.ExternalAccountId == "PA-CENTER");
    }

    [Fact]
    public async Task DeleteProviderCredentials_RemovesLocalStore()
    {
        await using var app = await CreateAppAsync(_ => { });
        await app.GetTestClient().PutAsync(
            "/api/providers/polygon/credentials",
            JsonContent(new { credentials = new { ApiKey = "polygon-secret" } }));

        var response = await app.GetTestClient().DeleteAsync("/api/providers/polygon/credentials");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadAsync<ProviderCredentialMutationResultDto>(response);
        result.CredentialState.Should().Be(ProviderCredentialStateDto.Missing);
        result.CredentialSource.Should().Be(ProviderCredentialSourceDto.None);
    }

    [Fact]
    public async Task PostProviderVerify_DelegatesQuickBooksOnlineAccountingVerifier()
    {
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IAccountingSystemProvider>(sp =>
                new FakeQuickBooksAccountingVerifier(sp.GetRequiredService<IProviderCredentialStore>()));
        });

        await app.GetTestClient().PutAsync(
            "/api/providers/quickbooks/credentials",
            JsonContent(new
            {
                credentials = new
                {
                    ClientId = "qbo-client-id",
                    ClientSecret = "qbo-client-secret",
                    RefreshToken = "qbo-refresh-token",
                    RealmId = "9130359087654321",
                    CompanyName = "Meridian-Dev"
                },
                environment = "sandbox"
            }));

        var response = await app.GetTestClient().PostAsync("/api/providers/quickbooks/verify", JsonContent(new { }));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadAsync<ProviderCredentialVerificationResultDto>(response);
        result.Success.Should().BeTrue();
        result.VerificationState.Should().Be(ProviderVerificationStateDto.Verified);
        result.ExternalAccountId.Should().Be("9130359087654321");

        var rows = await ReadAsync<ProviderConnectionRowDto[]>(
            await app.GetTestClient().GetAsync("/api/providers/connections"));
        var quickBooks = rows.Single(row => row.ProviderId == "quickbooks");
        quickBooks.Should().Match<ProviderConnectionRowDto>(row =>
            row.CredentialState == ProviderCredentialStateDto.Verified &&
            row.ExternalAccountId == "9130359087654321" &&
            row.Capability == ProviderConnectionCapabilityDto.AccountingSystem);
        quickBooks.CredentialFields.Should().Contain(field =>
            field.Name == "ClientId" &&
            field.Required &&
            field.InputKind == ProviderCredentialInputKindDto.Password);
        quickBooks.CredentialFields.Should().Contain(field =>
            field.Name == "ClientSecret" &&
            field.Required &&
            field.InputKind == ProviderCredentialInputKindDto.Password);
        quickBooks.CredentialFields.Should().Contain(field =>
            field.Name == "RefreshToken" &&
            field.Required &&
            field.InputKind == ProviderCredentialInputKindDto.Password);
        quickBooks.CredentialFields.Should().Contain(field =>
            field.Name == "RealmId" &&
            field.Required &&
            field.InputKind == ProviderCredentialInputKindDto.Text);
        quickBooks.CredentialFields.Should().Contain(field =>
            field.Name == "CompanyName" &&
            !field.Required &&
            field.InputKind == ProviderCredentialInputKindDto.Text);
        quickBooks.EnvironmentOptions.Should().Contain(option => option.Value == "sandbox" && option.IsDefault);
        quickBooks.EnvironmentOptions.Should().Contain(option => option.Value == "production");

        var rawConnections = await app.GetTestClient().GetStringAsync("/api/providers/connections");
        rawConnections.Should().NotContain("qbo-client-id");
        rawConnections.Should().NotContain("qbo-client-secret");
        rawConnections.Should().NotContain("qbo-refresh-token");
    }

    [Fact]
    public async Task CredentialMutationAudit_UsesAuthenticatedActorForSaveVerifyAndDelete()
    {
        await using var app = await CreateAppAsync(_ => { });
        var client = app.GetTestClient();
        var save = await client.PutAsync("/api/providers/polygon/credentials", new StringContent(
            """{"credentials":{"apiKey":"audit-test-key"},"requestedBy":"forged-operator"}""", Encoding.UTF8, "application/json"));
        save.StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync("/api/providers/polygon/verify", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.DeleteAsync("/api/providers/polygon/credentials")).StatusCode.Should().Be(HttpStatusCode.OK);
        var store = app.Services.GetRequiredService<IProviderCredentialStore>();
        var auditPath = Path.Combine(Path.GetDirectoryName(store.VaultPath)!, "provider-credentials.audit.jsonl");
        var entries = (await File.ReadAllLinesAsync(auditPath)).Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
        entries.Should().HaveCount(3);
        entries.Should().OnlyContain(entry => entry.GetProperty("actor").GetString() == "provider-ops");
        entries.Select(entry => entry.GetProperty("action").GetString()).Should().Equal("save", "verify-success", "delete");
    }

    [Fact]
    public async Task CredentialSave_WithoutAuthenticatedActorCannotUseRequestedByAsIdentity()
    {
        await using var app = await CreateAppAsync(_ => { }, includeActor: false);
        var response = await app.GetTestClient().PutAsync("/api/providers/polygon/credentials", new StringContent(
            """{"credentials":{"apiKey":"audit-test-key"},"requestedBy":"forged-operator"}""", Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        File.Exists(app.Services.GetRequiredService<IProviderCredentialStore>().VaultPath).Should().BeFalse();
    }

    [Theory]
    [InlineData("http")]
    [InlineData("transport")]
    [InlineData("json")]
    [InlineData("missing-account")]
    public async Task VerificationFailure_RedactsProviderDetailsRejectsMissingIdentityAndCanRetry(string failureKind)
    {
        using var env = AlpacaEnvScope.Clear();
        const string secret = "provider-echoed-secret";
        var logger = new VerificationLogger();
        using var handler = new VerificationRecoveryHandler(failureKind, secret);
        await using var app = await CreateAppAsync(services =>
        {
            services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(handler));
            services.AddSingleton<ILogger<ProviderConnectionLifecycleService>>(logger);
        });
        var client = app.GetTestClient();
        (await client.PutAsync("/api/providers/alpaca/credentials", JsonContent(new
        {
            credentials = new { KeyId = "verify-key", SecretKey = "verify-secret" },
            environment = "paper"
        }))).StatusCode.Should().Be(HttpStatusCode.OK);

        var failedResponse = await client.PostAsync("/api/providers/alpaca/verify", null);
        var failedJson = await failedResponse.Content.ReadAsStringAsync();
        failedJson.Should().NotContain(secret);
        var failed = await ReadAsync<ProviderCredentialVerificationResultDto>(failedResponse);
        failed.Success.Should().BeFalse();
        failed.VerificationState.Should().Be(ProviderVerificationStateDto.Failed);
        failed.Health.Should().Be(ProviderContinuityHealthDto.Blocked);
        failed.LastError.Should().Be("Alpaca account verification failed.");
        failed.ExternalAccountId.Should().BeNull();
        var store = app.Services.GetRequiredService<IProviderCredentialStore>();
        var retained = await store.ReadForProviderAsync("alpaca");
        retained!.LastError.Should().Be(failed.LastError);
        retained.ExternalAccountId.Should().BeNull();
        logger.Messages.Should().NotBeEmpty().And.OnlyContain(message => !message.Contains(secret));
        logger.Exceptions.Should().OnlyContain(exception => exception == null);

        var recovered = await ReadAsync<ProviderCredentialVerificationResultDto>(await client.PostAsync("/api/providers/alpaca/verify", null));
        recovered.Success.Should().BeTrue();
        recovered.ExternalAccountId.Should().Be("PA-RECOVERED");
        (await store.GetStatusAsync("alpaca")).AuditMetadata["lastVerifiedBy"].Should().Be("provider-ops");
    }

    private sealed class VerificationRecoveryHandler(string failureKind, string secret) : HttpMessageHandler
    {
        private int _calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _calls) > 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("""{"account_number":"PA-RECOVERED"}""", Encoding.UTF8, "application/json") });
            if (failureKind == "transport")
                throw new HttpRequestException(secret, new IOException(secret));
            return Task.FromResult(new HttpResponseMessage(failureKind == "http" ? HttpStatusCode.Unauthorized : HttpStatusCode.OK)
            {
                ReasonPhrase = secret,
                Content = new StringContent(failureKind == "json" ? "{\"id\":{\"" + secret + "\":1}}" : "{}", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class VerificationLogger : ILogger<ProviderConnectionLifecycleService>
    {
        public List<string> Messages { get; } = [];
        public List<Exception?> Exceptions { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ScopedCredentialRoute_VerifiesOnlyRetainedAccountAndDeletesOnlyItsRecord(bool accountMatches)
    {
        await using var app = await CreateAppAsync(services => services.AddSingleton<IHttpClientFactory>(
            new StubHttpClientFactory(new CapturingStubHandler(_ => { },
                new StringContent(JsonSerializer.Serialize(new { account_number = accountMatches ? "account-a" : "account-b" }), Encoding.UTF8, "application/json")))));
        await RetainConnectionAsync(app, "owned", "provider-tenant", "alpaca", "account-a", "paper");
        var vault = (FileProviderCredentialStore)app.Services.GetRequiredService<IProviderCredentialStore>();
        await vault.SaveAsync(new ProviderCredentialSaveRequest("alpaca", new Dictionary<string, string?> { ["KeyId"] = "legacy-key", ["SecretKey"] = "legacy-secret" }, "paper"));
        var scope = new ProviderCredentialScope("provider-tenant", "owned", "account-a", "paper");
        var client = app.GetTestClient();
        var saved = await client.PutAsync("/api/providers/alpaca/credentials?connectionId=owned", JsonContent(new
        {
            credentials = new { KeyId = "scoped-key", SecretKey = "scoped-secret" },
            environment = "paper",
            requestedBy = "spoofed-actor"
        }));
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        (await vault.ReadScopedAsync("alpaca", scope))!.Get("KeyId").Should().Be("scoped-key");
        var rows = await ReadAsync<ProviderConnectionRowDto[]>(await client.GetAsync("/api/providers/connections?connectionId=owned"));
        rows.Should().ContainSingle();
        rows[0].ExternalAccountId.Should().Be("account-a");
        rows[0].CredentialState.Should().Be(ProviderCredentialStateDto.Configured);
        rows[0].FallbackActive.Should().BeFalse();
        var verification = await ReadAsync<ProviderCredentialVerificationResultDto>(await client.PostAsync("/api/providers/alpaca/verify?connectionId=owned", JsonContent(new { })));
        verification.Success.Should().Be(accountMatches);
        (await vault.ReadScopedAsync("alpaca", scope))!.ExternalAccountId.Should().Be("account-a");
        (await client.DeleteAsync("/api/providers/alpaca/credentials?connectionId=owned")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await vault.ReadScopedAsync("alpaca", scope)).Should().BeNull();
        var deletedRows = await ReadAsync<ProviderConnectionRowDto[]>(await client.GetAsync("/api/providers/connections?connectionId=owned"));
        deletedRows.Single().CredentialState.Should().Be(ProviderCredentialStateDto.Missing);
        deletedRows.Single().ExternalAccountId.Should().Be("account-a");
        (await vault.ReadForProviderAsync("alpaca"))!.Get("KeyId").Should().Be("legacy-key");
        var audit = await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(vault.VaultPath)!, "provider-credentials.audit.jsonl"));
        audit.Should().Contain("provider-ops").And.NotContain("spoofed-actor").And.NotContain("scoped-secret");
    }

    [Theory]
    [InlineData("other")]
    [InlineData("missing")]
    [InlineData("wrong-provider")]
    [InlineData("")]
    [InlineData("owned&connectionId=other")]
    public async Task ScopedCredentialRoute_RefusesUnownedOrAmbiguousConnectionBeforeMutation(string query)
    {
        await using var app = await CreateAppAsync(_ => { });
        await RetainConnectionAsync(app, "owned", "provider-tenant", "alpaca", "account-a", "paper");
        await RetainConnectionAsync(app, "other", "another-tenant", "alpaca", "account-b", "paper");
        await RetainConnectionAsync(app, "wrong-provider", "provider-tenant", "polygon", "account-c", "default");
        var vault = (FileProviderCredentialStore)app.Services.GetRequiredService<IProviderCredentialStore>();
        var client = app.GetTestClient();
        var route = "/api/providers/alpaca/credentials?connectionId=" + query;
        (await client.PutAsync(route, JsonContent(new { credentials = new { KeyId = "refused", SecretKey = "refused" } }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.DeleteAsync(route)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync("/api/providers/alpaca/verify?connectionId=" + query, JsonContent(new { }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        if (query != "wrong-provider")
            (await client.GetAsync("/api/providers/connections?connectionId=" + query)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        File.Exists(vault.VaultPath).Should().BeFalse();
    }

    [Theory]
    [InlineData("provider-tenant")]
    [InlineData("another-tenant")]
    public async Task ScopedCredentialRoute_RejectsDuplicateRetainedIds(string duplicateTenant)
    {
        await using var app = await CreateAppAsync(_ => { });
        await RetainConnectionAsync(app, "owned", "provider-tenant", "alpaca", "account-a", "paper");
        var store = new Meridian.Application.UI.ConfigStore(app.Services.GetRequiredService<ConfigStore>().ConfigPath);
        var config = store.Load();
        var original = config.ProviderConnections!.Connections!.Single();
        config = config with
        {
            ProviderConnections = config.ProviderConnections with
            {
                Connections = [original, original with { ConnectionId = "OWNED", TenantId = duplicateTenant, ExternalAccountId = "account-b" }]
            }
        };
        await File.WriteAllTextAsync(store.ConfigPath, JsonSerializer.Serialize(config));
        var before = await File.ReadAllTextAsync(store.ConfigPath);
        var service = new Meridian.Application.ProviderRouting.ProviderConnectionService(store);
        (await service.GetCredentialScopeForTenantAsync("owned", "provider-tenant")).Should().BeNull();
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteForTenantAsync("owned", "provider-tenant"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.UpsertForTenantAsync(
            new Meridian.Contracts.Api.CreateProviderConnectionRequest("owned", "alpaca", "Owned", ExternalAccountId: "account-a"), "provider-tenant", "paper"));
        var client = app.GetTestClient();
        (await client.GetAsync("/api/providers/connections?connectionId=owned")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsync("/api/providers/alpaca/credentials?connectionId=owned", JsonContent(new { credentials = new { KeyId = "refused", SecretKey = "refused" } }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.DeleteAsync("/api/providers/alpaca/credentials?connectionId=owned")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync("/api/providers/alpaca/verify?connectionId=owned", JsonContent(new { }))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await File.ReadAllTextAsync(store.ConfigPath)).Should().Be(before);
        File.Exists(((FileProviderCredentialStore)app.Services.GetRequiredService<IProviderCredentialStore>()).VaultPath).Should().BeFalse();
    }

    [Fact]
    public async Task ScopedCredentialVerification_DoesNotInvokeProviderWideAccountingVerifier()
    {
        await using var app = await CreateAppAsync(services => services.AddSingleton<IAccountingSystemProvider>(sp =>
            new FakeQuickBooksAccountingVerifier(sp.GetRequiredService<IProviderCredentialStore>())));
        await RetainConnectionAsync(app, "books", "provider-tenant", "quickbooks", "realm-a", "sandbox");
        var client = app.GetTestClient();
        var saved = await client.PutAsync("/api/providers/quickbooks/credentials?connectionId=books", JsonContent(new
        {
            credentials = new { ClientId = "client", ClientSecret = "secret", RefreshToken = "refresh", RealmId = "realm-a" },
            environment = "sandbox"
        }));
        saved.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ReadAsync<ProviderCredentialVerificationResultDto>(await client.PostAsync("/api/providers/quickbooks/verify?connectionId=books", JsonContent(new { })));
        result.Success.Should().BeFalse();
        result.VerificationState.Should().Be(ProviderVerificationStateDto.NotVerified);
        var vault = (FileProviderCredentialStore)app.Services.GetRequiredService<IProviderCredentialStore>();
        (await vault.ReadScopedAsync("quickbooks", new ProviderCredentialScope("provider-tenant", "books", "realm-a", "sandbox")))!.LastVerifiedAt.Should().BeNull();
        var audit = await File.ReadAllTextAsync(Path.Combine(Path.GetDirectoryName(vault.VaultPath)!, "provider-credentials.audit.jsonl"));
        audit.Should().NotContain("test-quickbooks-verifier");
    }

    private static Task<Meridian.Contracts.Api.ProviderConnectionDto> RetainConnectionAsync(WebApplication app, string id, string tenant, string provider, string account, string environment)
        => new Meridian.Application.ProviderRouting.ProviderConnectionService(
            new Meridian.Application.UI.ConfigStore(app.Services.GetRequiredService<ConfigStore>().ConfigPath))
            .UpsertForTenantAsync(new Meridian.Contracts.Api.CreateProviderConnectionRequest(id, provider, id, ExternalAccountId: account), tenant, environment);

    private static async Task<WebApplication> CreateAppAsync(
        Action<IServiceCollection> configureServices,
        UserPermission permissions = UserPermission.ManageCredentials,
        bool includeTenantScope = true,
        bool includeActor = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "provider-connection-endpoints", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, JsonSerializer.Serialize(new { dataRoot = Path.Combine(root, "data") }));

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton<IProviderCredentialStore>(_ => new FileProviderCredentialStore(Path.Combine(root, "data")));
        builder.Services.AddSingleton(new ConfigStore(configPath));
        builder.Services.AddSingleton(NullLogger<ProviderConnectionLifecycleService>.Instance);
        builder.Services.AddSingleton<ProviderConnectionLifecycleService>();
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ =>
                RateLimitPartition.GetNoLimiter<string>("test"));
        });
        configureServices(builder.Services);

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (includeActor)
                context.Items[LoginSessionMiddleware.CurrentUserKey] = "provider-ops";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
            if (includeTenantScope)
            {
                context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "provider-tenant";
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "provider-tenant";
            }

            await next();
        });
        app.UseRateLimiter();
        app.MapProviderConnectionEndpoints(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        await app.StartAsync();
        return app;
    }

    private static StringContent JsonContent(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        result.Should().NotBeNull($"expected {typeof(T).Name}, got {json}");
        return result!;
    }

    private sealed class CapturingStubHandler : HttpMessageHandler
    {
        private readonly Action<HttpRequestMessage> _capture;
        private readonly HttpContent _content;

        public CapturingStubHandler(Action<HttpRequestMessage> capture, HttpContent content)
        {
            _capture = capture;
            _content = content;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _capture(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = CloneContent(_content)
            });
        }
    }

    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeQuickBooksAccountingVerifier : IAccountingSystemProvider, IAccountingSystemConnectionVerifier
    {
        private readonly IProviderCredentialStore _credentialStore;

        public FakeQuickBooksAccountingVerifier(IProviderCredentialStore credentialStore)
        {
            _credentialStore = credentialStore;
        }

        public string ProviderId => "quickbooks";

        public string DisplayName => "QuickBooks Online";

        public AccountingSystemProviderCapabilities Capabilities { get; } = new(
            SupportsChartOfAccounts: true,
            SupportsJournalEntries: true,
            SupportsTrialBalance: true,
            SupportsPosting: false,
            EvidenceKinds: ["QuickBooksAccount", "QuickBooksJournalEntry", "QuickBooksTrialBalance"],
            RequiresCredentials: true);

        public Task<AccountingSystemImportDetailDto> ImportAsync(
            AccountingSystemImportRequestDto request,
            CancellationToken ct = default)
            => throw new NotSupportedException("Verification test does not import QuickBooks evidence.");

        public async Task<AccountingSystemConnectionVerificationResult> VerifyConnectionAsync(CancellationToken ct = default)
        {
            await _credentialStore.RecordVerificationAsync(
                new ProviderCredentialVerificationUpdate(
                    "quickbooks",
                    Success: true,
                    ExternalAccountId: "9130359087654321",
                    VerifiedAt: DateTimeOffset.UtcNow,
                    Actor: "test-quickbooks-verifier"),
                ct).ConfigureAwait(false);

            return new AccountingSystemConnectionVerificationResult(
                Success: true,
                ExternalCompanyId: "9130359087654321",
                LastError: null,
                VerifiedAtUtc: DateTimeOffset.UtcNow,
                Warnings: ["QuickBooks Online read-only token exchange succeeded."]);
        }
    }

    private sealed class AlpacaEnvScope : IDisposable
    {
        private static readonly string[] Names =
        [
            AlpacaCredentialEnvironment.KeyIdName,
            AlpacaCredentialEnvironment.SecretKeyName,
            AlpacaCredentialEnvironment.TradingEnvironmentName,
            "APCA_API_KEY_ID",
            "APCA_API_SECRET_KEY"
        ];

        private readonly Dictionary<string, string?> _values;

        private AlpacaEnvScope()
        {
            _values = Names.ToDictionary(static name => name, static name => Environment.GetEnvironmentVariable(name));
        }

        public static AlpacaEnvScope Clear()
        {
            var scope = new AlpacaEnvScope();
            foreach (var name in Names)
            {
                Environment.SetEnvironmentVariable(name, null);
            }

            return scope;
        }

        public void Dispose()
        {
            foreach (var (name, value) in _values)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    private static HttpContent CloneContent(HttpContent content)
    {
        var raw = content.ReadAsStringAsync().GetAwaiter().GetResult();
        var mediaType = content.Headers.ContentType?.MediaType ?? "application/json";
        return new StringContent(raw, Encoding.UTF8, mediaType);
    }
}
