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
        rows.Single(row => row.ProviderId == "quickbooks").Should().Match<ProviderConnectionRowDto>(row =>
            row.CredentialState == ProviderCredentialStateDto.Verified &&
            row.ExternalAccountId == "9130359087654321" &&
            row.Capability == ProviderConnectionCapabilityDto.AccountingSystem);
    }

    private static async Task<WebApplication> CreateAppAsync(
        Action<IServiceCollection> configureServices,
        UserPermission permissions = UserPermission.ManageCredentials)
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
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
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
