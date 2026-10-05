using System.Net;
using System.Net.Http.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Core.Config;
using Meridian.Application.Config.Credentials;
using Meridian.DataIntegration.Credentials;
using Meridian.DataIntegration.Monitoring;
using Meridian.Contracts.Api;
using Meridian.Identity.Auth;
using Meridian.Contracts.Configuration;
using Meridian.Contracts.Plaid;
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

public sealed class ProviderReadinessEndpointTests
{
    [Theory]
    [InlineData("interactive-brokers", "")]
    [InlineData("interactive-brokers", "Streaming")]
    [InlineData("named-gateway", " IB ")]
    public async Task GetProviderReadiness_AliasedMetricsRetainConnectionFailureEvidence(string providerId, string providerType)
    {
        using var env = ProviderConnectionEnvironmentScope.Clear();
        await using var app = await CreateAppAsync();
        var configStore = app.Services.GetRequiredService<ConfigStore>();
        var observedAt = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var metrics = new ProviderMetricsStatus(observedAt,
        [
            new ProviderMetrics(providerId, providerType, IsConnected: false,
                TradesReceived: 0, DepthUpdatesReceived: 0, QuotesReceived: 0,
                ConnectionAttempts: 1, ConnectionFailures: 1, MessagesDropped: 0,
                ActiveSubscriptions: 0, AverageLatencyMs: 0, MinLatencyMs: 0, MaxLatencyMs: 0,
                DataQualityScore: 0, ConnectionSuccessRate: 0, Timestamp: observedAt)
        ], TotalProviders: 1, HealthyProviders: 0);
        var statusDirectory = Path.Combine(configStore.GetDataRoot(), "_status");
        Directory.CreateDirectory(statusDirectory);
        await File.WriteAllTextAsync(Path.Combine(statusDirectory, "providers.json"),
            System.Text.Json.JsonSerializer.Serialize(metrics, JsonOptions));

        var connections = await app.Services.GetRequiredService<ProviderConnectionLifecycleService>().GetConnectionsAsync();
        connections.Should().ContainSingle(row => row.ProviderId == "ibkr").Subject.Health
            .Should().Be(ProviderContinuityHealthDto.Degraded);
        connections.Should().ContainSingle(row => row.ProviderId == "alpaca").Subject.Health
            .Should().NotBe(ProviderContinuityHealthDto.Degraded);

        var readiness = await app.GetTestClient().GetFromJsonAsync<ProviderReadinessSummaryDto>(UiApiRoutes.ProviderReadiness, JsonOptions);

        var row = readiness!.Providers.Should().ContainSingle(provider => provider.ProviderId == "ibkr").Subject;
        row.Status.Should().Be(ProviderReadinessStatusDto.Degraded);
        row.ConnectionHealth.Should().Be(ProviderContinuityHealthDto.Degraded);
        row.FallbackActive.Should().BeTrue();
        row.LastFailureAt.Should().Be(observedAt);
    }

    [Theory]
    [InlineData("IB", true)]
    [InlineData("IB", false)]
    [InlineData("Streaming", true)]
    public async Task GetProviderReadiness_ConnectionMetricsDoNotCrossProviderFamilies(string providerType, bool configureSource)
    {
        using var env = ProviderConnectionEnvironmentScope.Clear();
        await using var app = await CreateAppAsync();
        var configStore = app.Services.GetRequiredService<ConfigStore>();
        if (configureSource)
        {
            await configStore.SaveAsync(configStore.Load() with
            {
                DataSources = new DataSourcesConfig(Sources:
                [
                    new DataSourceConfig("alpaca", "Named IB connection", DataSourceKind.IB, Enabled: true)
                ])
            });
        }

        var observedAt = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        var metrics = new ProviderMetricsStatus(observedAt,
        [
            new ProviderMetrics("alpaca", providerType, IsConnected: false,
                TradesReceived: 0, DepthUpdatesReceived: 0, QuotesReceived: 0,
                ConnectionAttempts: 1, ConnectionFailures: 1, MessagesDropped: 0,
                ActiveSubscriptions: 0, AverageLatencyMs: 0, MinLatencyMs: 0, MaxLatencyMs: 0,
                DataQualityScore: 0, ConnectionSuccessRate: 0, Timestamp: observedAt)
        ], TotalProviders: 1, HealthyProviders: 0);
        var statusDirectory = Path.Combine(configStore.GetDataRoot(), "_status");
        Directory.CreateDirectory(statusDirectory);
        await File.WriteAllTextAsync(Path.Combine(statusDirectory, "providers.json"),
            System.Text.Json.JsonSerializer.Serialize(metrics, JsonOptions));

        var connections = await app.Services.GetRequiredService<ProviderConnectionLifecycleService>().GetConnectionsAsync();
        connections.Should().ContainSingle(row => row.ProviderId == "ibkr").Subject.Health
            .Should().Be(ProviderContinuityHealthDto.Degraded);
        connections.Should().ContainSingle(row => row.ProviderId == "alpaca").Subject.Health
            .Should().NotBe(ProviderContinuityHealthDto.Degraded);

        var readiness = await app.GetTestClient().GetFromJsonAsync<ProviderReadinessSummaryDto>(UiApiRoutes.ProviderReadiness, JsonOptions);

        var ibkr = readiness!.Providers.Should().ContainSingle(provider => provider.ProviderId == "ibkr").Subject;
        ibkr.Status.Should().Be(ProviderReadinessStatusDto.Degraded);
        ibkr.ConnectionHealth.Should().Be(ProviderContinuityHealthDto.Degraded);
        ibkr.FallbackActive.Should().BeTrue();
        ibkr.LastFailureAt.Should().Be(observedAt);
        var alpaca = readiness.Providers.Should().ContainSingle(provider => provider.ProviderId == "alpaca").Subject;
        alpaca.ConnectionHealth.Should().NotBe(ProviderContinuityHealthDto.Degraded);
        alpaca.FallbackActive.Should().BeFalse();
        alpaca.LastFailureAt.Should().BeNull();
        alpaca.Evidence.Should().NotContain(evidence =>
            evidence.Kind == ProviderReadinessEvidenceKindDto.Connection &&
            evidence.Detail.Contains("1 failure(s)", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("primary-account")]
    [InlineData("alpaca")]
    public async Task GetProviderReadiness_ConfiguredConnectionIdDoesNotCreateOrShadowProviderFamily(string connectionId)
    {
        using var env = ProviderConnectionEnvironmentScope.Clear();
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();
        var baseline = await client.GetFromJsonAsync<ProviderReadinessSummaryDto>(UiApiRoutes.ProviderReadiness, JsonOptions);
        var configStore = app.Services.GetRequiredService<ConfigStore>();
        await configStore.SaveAsync(configStore.Load() with
        {
            DataSources = new DataSourcesConfig(Sources:
            [
                new DataSourceConfig(connectionId, "Named IB connection", DataSourceKind.IB, Enabled: false)
            ])
        });

        var readiness = await client.GetFromJsonAsync<ProviderReadinessSummaryDto>(UiApiRoutes.ProviderReadiness, JsonOptions);

        readiness.Should().NotBeNull();
        readiness!.Providers.Select(row => row.ProviderId).Should()
            .BeEquivalentTo(baseline!.Providers.Select(row => row.ProviderId));
        readiness.Providers.Should().ContainSingle(row => row.ProviderId == "ibkr")
            .Subject.IsEnabled.Should().BeFalse();
        readiness.Providers.Should().ContainSingle(row => row.ProviderId == "alpaca")
            .Subject.IsEnabled.Should().BeTrue("an IB connection ID cannot disable the Alpaca family");
    }

    [Theory]
    [InlineData("interactive-brokers")]
    [InlineData(" IB ")]
    public async Task GetProviderReadiness_DisabledModuleAliasOverridesHealthyConnectionAndEnabledSource(string moduleAlias)
    {
        using var env = ProviderConnectionEnvironmentScope.Clear();
        await using var app = await CreateAppAsync();
        var configStore = app.Services.GetRequiredService<ConfigStore>();
        await configStore.SaveAsync(configStore.Load() with
        {
            DataSources = new DataSourcesConfig(Sources:
            [
                new DataSourceConfig("ibkr", "Configured IB connection", DataSourceKind.IB, Enabled: true)
            ]),
            ProviderModules = new ProviderModulesConfig(new()
            {
                [moduleAlias] = new(Enabled: false)
            })
        });
        var connections = await app.Services.GetRequiredService<ProviderConnectionLifecycleService>().GetConnectionsAsync();
        connections.Should().ContainSingle(row => row.ProviderId == "ibkr").Subject.Health
            .Should().Be(ProviderContinuityHealthDto.Healthy);

        var readiness = await app.GetTestClient().GetFromJsonAsync<ProviderReadinessSummaryDto>(UiApiRoutes.ProviderReadiness, JsonOptions);

        readiness.Should().NotBeNull();
        var row = readiness!.Providers.Should().ContainSingle(provider => provider.ProviderId == "ibkr").Subject;
        row.IsEnabled.Should().BeFalse();
        row.Status.Should().Be(ProviderReadinessStatusDto.Review,
            "retained healthy connection evidence cannot override an explicitly disabled provider family");
    }

    [Theory]
    [InlineData("ib")]
    [InlineData("interactive-brokers")]
    [InlineData(" INTERACTIVEBROKERS ")]
    public async Task GetProviderReadiness_ConfiguredAliasProjectsOneCanonicalFamily(string configuredAlias)
    {
        using var env = ProviderConnectionEnvironmentScope.Clear();
        await using var app = await CreateAppAsync();
        var configStore = app.Services.GetRequiredService<ConfigStore>();
        await configStore.SaveAsync(configStore.Load() with
        {
            DataSources = new DataSourcesConfig(Sources:
            [
                new DataSourceConfig(configuredAlias, "Configured IB connection", DataSourceKind.IB, Enabled: false)
            ])
        });

        var readiness = await app.GetTestClient().GetFromJsonAsync<ProviderReadinessSummaryDto>(UiApiRoutes.ProviderReadiness, JsonOptions);

        readiness.Should().NotBeNull();
        var row = readiness!.Providers.Should().ContainSingle(provider => provider.ProviderId == "ibkr").Subject;
        row.IsEnabled.Should().BeFalse();
        readiness.Providers.Should().NotContain(provider =>
            provider.ProviderId == "ib" || provider.ProviderId == "interactive-brokers" || provider.ProviderId == "interactivebrokers");
    }

    [Fact]
    public async Task GetProviderReadiness_NamedConnectionJoinsMetricsWithoutCreatingAnotherProviderFamily()
    {
        using var env = ProviderConnectionEnvironmentScope.Clear();
        await using var app = await CreateAppAsync();
        var configStore = app.Services.GetRequiredService<ConfigStore>();
        const string connectionId = "paper-trading-session-1";
        var timestamp = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        await configStore.SaveAsync(configStore.Load() with
        {
            DataSources = new DataSourcesConfig(Sources:
            [
                new DataSourceConfig(connectionId, "Configured IB connection", DataSourceKind.IB, Enabled: true)
            ])
        });
        var metricsPath = new Meridian.Application.UI.ConfigStore(configStore.ConfigPath).GetProviderMetricsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(metricsPath)!);
        var metrics = new ProviderMetricsStatus(timestamp,
        [
            new ProviderMetrics(connectionId, "Streaming", true, 0, 0, 0, 1, 0, 0, 1, 5, 5, 5, 1, 1, timestamp)
        ], 1, 1);
        await File.WriteAllTextAsync(metricsPath, System.Text.Json.JsonSerializer.Serialize(metrics, JsonOptions));

        var readiness = await app.GetTestClient().GetFromJsonAsync<ProviderReadinessSummaryDto>(UiApiRoutes.ProviderReadiness, JsonOptions);

        var row = readiness!.Providers.Should().ContainSingle(provider => provider.ProviderId == "ibkr").Subject;
        row.IsConnected.Should().BeTrue();
        row.LastSuccessfulAt.Should().Be(timestamp);
        readiness.Providers.Should().NotContain(provider => provider.ProviderId == connectionId);
        configStore.Load().DataSources!.Sources!.Should().ContainSingle().Subject.Id.Should().Be(connectionId);
    }

    [Fact]
    public async Task GetProviderReadiness_ComposesCredentialAndPlaidEvidenceWithoutSecrets()
    {
        using var env = ProviderConnectionEnvironmentScope.Clear();
        await using var app = await CreateAppAsync();
        var store = app.Services.GetRequiredService<IProviderCredentialStore>();
        await store.SaveAsync(new ProviderCredentialSaveRequest(
            "plaid",
            new Dictionary<string, string?>
            {
                ["ClientId"] = "plaid-client-id",
                ["Secret"] = "plaid-secret"
            },
            Environment: "sandbox",
            Actor: "test"));
        await store.RecordVerificationAsync(new ProviderCredentialVerificationUpdate(
            "plaid",
            Success: true,
            ExternalAccountId: "item-1",
            Actor: "test"));

        var response = await app.GetTestClient().GetAsync(UiApiRoutes.ProviderReadiness);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var readiness = await response.Content.ReadFromJsonAsync<ProviderReadinessSummaryDto>(JsonOptions);
        readiness.Should().NotBeNull();
        var plaid = readiness!.Providers.Should().ContainSingle(row => row.ProviderId == "plaid").Subject;
        plaid.Status.Should().Be(ProviderReadinessStatusDto.Ready);
        plaid.CredentialState.Should().Be(ProviderCredentialStateDto.Verified);
        plaid.CredentialFields.Should().Contain(field =>
            field.Name == "ClientId" &&
            field.Label == "Client ID" &&
            field.Required &&
            field.InputKind == ProviderCredentialInputKindDto.Password);
        plaid.CredentialFields.Should().Contain(field =>
            field.Name == "Secret" &&
            field.Required &&
            field.InputKind == ProviderCredentialInputKindDto.Password);
        plaid.EnvironmentOptions.Should().Contain(option =>
            option.Value == "sandbox" &&
            option.Label == "Sandbox" &&
            option.IsDefault);
        plaid.EnvironmentOptions.Should().Contain(option => option.Value == "development");
        plaid.EnvironmentOptions.Should().Contain(option => option.Value == "production");
        plaid.Evidence.Should().Contain(evidence =>
            evidence.Kind == ProviderReadinessEvidenceKindDto.Plaid &&
            evidence.Detail.Contains("1 linked item", StringComparison.OrdinalIgnoreCase));

        var alpaca = readiness.Providers.Should().ContainSingle(row => row.ProviderId == "alpaca").Subject;
        alpaca.CredentialFields.Should().Contain(field =>
            field.Name == "KeyId" &&
            field.Label == "Key ID" &&
            field.Required &&
            field.InputKind == ProviderCredentialInputKindDto.Password);
        alpaca.CredentialFields.Should().Contain(field =>
            field.Name == "SecretKey" &&
            field.Required &&
            field.InputKind == ProviderCredentialInputKindDto.Password);
        alpaca.EnvironmentOptions.Should().Contain(option => option.Value == "paper" && option.IsDefault);
        alpaca.EnvironmentOptions.Should().Contain(option => option.Value == "live");

        var quickBooks = readiness.Providers.Should().ContainSingle(row => row.ProviderId == "quickbooks").Subject;
        quickBooks.CredentialFields.Should().Contain(field =>
            field.Name == "ClientId" &&
            field.Label == "Client ID" &&
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

        var quickBooksFixture = readiness.Providers.Should().ContainSingle(row => row.ProviderId == "quickbooks-fixture").Subject;
        quickBooksFixture.CredentialFields.Should().BeEmpty();
        quickBooksFixture.EnvironmentOptions.Should().BeEmpty();

        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("plaid-client-id");
        raw.Should().NotContain("plaid-secret");
    }

    private static async Task<WebApplication> CreateAppAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "meridian-tests", "provider-readiness", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var configPath = Path.Combine(root, "appsettings.json");
        await File.WriteAllTextAsync(configPath, $$"""{"dataRoot":"{{Path.Combine(root, "data").Replace("\\", "\\\\")}}"}""");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development
        });
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        builder.Services.AddSingleton<IProviderCredentialStore>(_ => new FileProviderCredentialStore(Path.Combine(root, "data")));
        builder.Services.AddSingleton(new ConfigStore(configPath));
        builder.Services.AddSingleton<IPlaidConnectionRepository>(new FakePlaidConnectionRepository());
        builder.Services.AddSingleton(NullLogger<ProviderConnectionLifecycleService>.Instance);
        builder.Services.AddSingleton<ProviderConnectionLifecycleService>();
        builder.Services.AddSingleton<ProviderReadinessService>();
        builder.Services.AddRateLimiter(options =>
        {
            options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ =>
                RateLimitPartition.GetNoLimiter<string>("test"));
        });

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = UserPermission.ManageCredentials;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-test";
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-test";
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "provider-readiness-test-operator";
            await next();
        });
        app.UseRateLimiter();
        app.MapProviderEndpoints(JsonOptions);

        await app.StartAsync();
        return app;
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private sealed class FakePlaidConnectionRepository : IPlaidConnectionRepository
    {
        public Task<IReadOnlyList<PlaidItemDto>> ListItemsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PlaidItemDto>>(
            [
                new(
                    ItemId: "item-1",
                    InstitutionId: "ins_1",
                    InstitutionName: "Plaid Test Bank",
                    Environment: PlaidEnvironmentDto.Sandbox,
                    Status: PlaidItemStatusDto.Linked,
                    AccessTokenKey: "AccessToken:item-1",
                    TransactionsCursor: null,
                    LinkedAt: DateTimeOffset.UtcNow.AddDays(-1),
                    LastSyncedAt: DateTimeOffset.UtcNow,
                    ConsentExpiresAt: null,
                    LastWebhookType: null,
                    LastWebhookCode: null,
                    LastError: null)
            ]);

        public Task<PlaidItemDto?> GetItemAsync(string itemId, CancellationToken ct = default)
            => Task.FromResult<PlaidItemDto?>(null);

        public Task<IReadOnlyList<PlaidAccountDto>> ListAccountsAsync(string? itemId = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PlaidAccountDto>>(
            [
                new(
                    PlaidAccountId: "account-1",
                    ItemId: "item-1",
                    Name: "Operating",
                    OfficialName: null,
                    Mask: "0000",
                    Type: "depository",
                    Subtype: "checking",
                    PersistentAccountId: null,
                    MeridianAccountId: null,
                    EntityId: null,
                    VerificationStatus: "verified")
            ]);

        public Task UpsertItemAsync(PlaidItemDto item, IReadOnlyList<PlaidAccountDto> accounts, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UpdateTransactionsCursorAsync(string itemId, string? cursor, DateTimeOffset syncedAt, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UpdateItemStatusAsync(string itemId, PlaidItemStatusDto status, string? webhookType, string? webhookCode, string? error, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<bool> TryAppendWebhookAsync(PlaidWebhookEventDto webhook, CancellationToken ct = default)
            => Task.FromResult(true);

        public Task RecordTransferAsync(PlaidTransferResult result, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class ProviderConnectionEnvironmentScope : IDisposable
    {
        private static readonly string[] Names =
        [
            AlpacaCredentialEnvironment.KeyIdName,
            AlpacaCredentialEnvironment.SecretKeyName,
            "PLAID_CLIENT_ID",
            "PLAID_SECRET",
            "PLAID_SANDBOX_SECRET",
            "PLAID_DEVELOPMENT_SECRET"
        ];

        private readonly Dictionary<string, string?> _original = new(StringComparer.Ordinal);

        private ProviderConnectionEnvironmentScope()
        {
            foreach (var name in Names)
            {
                _original[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, null);
            }
        }

        public static ProviderConnectionEnvironmentScope Clear() => new();

        public void Dispose()
        {
            foreach (var (name, value) in _original)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }
}
