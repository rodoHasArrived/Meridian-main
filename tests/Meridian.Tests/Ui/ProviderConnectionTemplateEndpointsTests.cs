using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Application.ProviderRouting;
using Meridian.Contracts.Api;
using Meridian.Contracts.Configuration;
using Meridian.Core.Config;
using Meridian.DataIntegration.Credentials;
using Meridian.Identity.Auth;
using Meridian.ProviderSdk;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Meridian.Tests.Ui;

public sealed class ProviderConnectionTemplateEndpointsTests : IDisposable
{
    private const string Templates = UiApiRoutes.ProviderConnectionTemplates;
    private const UserPermission Grants = UserPermission.ManageCredentials | UserPermission.ManageProviders | UserPermission.ModifyConfig;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-template-http", Guid.NewGuid().ToString("N"));
    private Meridian.Application.UI.ConfigStore Config => new(Path.Combine(_root, "appsettings.json"));

    [Fact]
    public async Task SameTemplateCreatesEquivalentOwnedConnectionsUsingSeparateVaultRecords()
    {
        await using var app = await CreateAppAsync();
        using var http = app.GetTestClient();
        var options = await http.GetFromJsonAsync<ProviderConnectionTemplateOption[]>(Templates);
        options!.Select(option => option.Template.Name).Should().Equal("Paper brokerage", "Historical equities", "Options chain",
            "Reference data", "Research-only backfill");
        var template = options!.Single(option => option.Template.Name == "Historical equities");
        template.ReviewRows.Should().Contain(row => row.Field == "Required capabilities" && row.Value == "Historical Bars");
        template.VerificationChecks.Should().Contain(check => check.Contains("adjustments"));

        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var savedIds = new List<string>();
        for (var i = 0; i < ids.Length; i++)
        {
            var setup = await BeginAsync(http, template, ids[i]);
            var route = SetupRoute(setup.SetupId);
            var credentials = CredentialRoute(setup);
            (Config.LoadRequired().ProviderConnections!.Connections ?? []).Should().HaveCount(i);
            (await http.PostAsync(route, null)).StatusCode.Should().Be(HttpStatusCode.Conflict);

            var secret = $"test-key-{i}";
            (await http.PutAsJsonAsync(credentials, new ProviderCredentialUpsertRequestDto(new Dictionary<string, string?>
            {
                ["ApiKey"] = secret
            }, setup.Environment))).StatusCode.Should().Be(HttpStatusCode.OK);
            var tested = await (await http.PostAsync(route + "/test", null)).Content.ReadFromJsonAsync<ProviderTemplateSetupTestResult>();
            tested!.CanSave.Should().BeTrue();
            tested.Detail.Should().Contain("returned a sample");

            // Rotation after review invalidates the tested generation.
            (await http.PutAsJsonAsync(credentials, new ProviderCredentialUpsertRequestDto(new Dictionary<string, string?>
            {
                ["ApiKey"] = secret + "-revised"
            }, setup.Environment))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await http.PostAsync(route, null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await (await http.PostAsync(route + "/test", null)).Content.ReadFromJsonAsync<ProviderTemplateSetupTestResult>())!.CanSave.Should().BeTrue();

            var saved = await http.PostAsync(route, null);
            saved.StatusCode.Should().Be(HttpStatusCode.OK);
            var connectionId = (await saved.Content.ReadFromJsonAsync<SavedProviderTemplateConnection>())!.ConnectionId;
            connectionId.Should().NotBe(ids[i].ToString("N"));
            savedIds.Add(connectionId);
            (await http.PostAsync(route, null)).StatusCode.Should().Be(HttpStatusCode.OK, "an uncertain save can be retried");
        }

        var config = Config.LoadRequired().ProviderConnections!;
        config.Connections.Should().HaveCount(2);
        config.Connections![0].Should().BeEquivalentTo(config.Connections[1], options => options
            .Excluding(connection => connection.ConnectionId).Excluding(connection => connection.TemplateSetupId));
        config.Connections.Select(connection => connection.TemplateSetupId).Should().Equal(ids.Select(id => (Guid?)id));
        config.Bindings.Should().BeNull();
        config.RuntimeCredentials.Should().BeNull();
        config.TemplateSetups.Should().BeEmpty();
        File.ReadAllText(Config.ConfigPath).Should().NotContain("test-key-");
        var vault = (IScopedProviderCredentialStore)app.Services.GetRequiredService<IProviderCredentialStore>();
        for (var i = 0; i < ids.Length; i++)
        {
            var scope = new ProviderCredentialScope("tenant-a", savedIds[i], "provider-data", "production");
            var values = await vault.ReadScopedAsync("tiingo", scope);
            values!.Get("ApiKey").Should().Be($"test-key-{i}-revised");
        }
    }

    [Fact]
    public async Task IncompatibleBatchReportsBothVersionsAndLeavesStorageUntouched()
    {
        await using var app = await CreateAppAsync();
        using var http = app.GetTestClient();
        var options = await http.GetFromJsonAsync<ProviderConnectionTemplateOption[]>(Templates);
        var source = options!.Single(option => option.Template.Name == "Options chain").Template;
        var before = File.ReadAllText(Config.ConfigPath);
        var good = source with { Name = "Imported options" };
        var unsupported = source with
        {
            Name = "Unavailable options",
            ProviderKind = "unknown-provider",
            ProviderVersion = 2,
            RequestedCapabilities = [new("OptionsChain", 3), new("UnknownCapability", 1)]
        };
        var response = await http.PostAsJsonAsync(UiApiRoutes.ProviderConnectionTemplateImport, new ProviderConnectionTemplateFile(1, [good, unsupported]));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var codes = error.RootElement.GetProperty("issues").EnumerateArray().Select(issue => issue.GetProperty("code").GetString());
        codes.Should().Contain(["UnknownProvider", "UnsupportedProviderVersion", "UnknownCapability", "UnsupportedCapabilityVersion"]);
        File.ReadAllText(Config.ConfigPath).Should().Be(before);

        (await http.PostAsJsonAsync(UiApiRoutes.ProviderConnectionTemplateImport, new ProviderConnectionTemplateFile(1, [good])))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var foreign = app.GetTestClient();
        foreign.DefaultRequestHeaders.Add("X-Test-Tenant", "tenant-b");
        (await foreign.GetFromJsonAsync<ProviderConnectionTemplateOption[]>(Templates))!.Should()
            .NotContain(option => option.Template.Name == good.Name);
    }

    [Theory]
    [InlineData("\"providerKind\":\"polygon\",\"providerKind\":\"polygon\"")]
    [InlineData("\"accountId\":\"account-private\",\"providerKind\":\"polygon\"")]
    [InlineData("\"endpoint\":\"https://private.example.test\",\"providerKind\":\"polygon\"")]
    [InlineData("\"credentials\":{\"apiKey\":\"test-key\"},\"providerKind\":\"polygon\"")]
    public async Task ImportRejectsUnexpectedOrDuplicateProperties(string replacement)
    {
        await using var app = await CreateAppAsync();
        using var http = app.GetTestClient();
        var export = await http.GetStringAsync(UiApiRoutes.ProviderConnectionTemplateExport + "?name=Options%20chain");
        var tampered = export.Replace("\"providerKind\":\"polygon\"", replacement, StringComparison.Ordinal);
        tampered.Should().NotBe(export);
        var response = await http.PostAsync(UiApiRoutes.ProviderConnectionTemplateImport, new StringContent(tampered, Encoding.UTF8, "application/json"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("private.example.test").And.NotContain("test-key");
        Config.LoadRequired().ProviderConnections.Should().BeNull();
    }

    [Fact]
    public async Task SavingExistingConnectionCopiesOnlyPortableSettings()
    {
        await using var app = await CreateAppAsync();
        using var http = app.GetTestClient();
        await Config.UpdateAsync(cfg => cfg with
        {
            ProviderConnections = new(
                Connections:
                [
                    new("external-id", "polygon", "private-connection-name", CredentialReference: "vault-ref-private",
                        InstitutionId: "private-institution", ExternalAccountId: "private-account", Tags: ["private-tag"],
                        Description: "https://private.example.test", TenantId: "tenant-a", CredentialEnvironment: "production")
                ],
                Bindings: [new("private-binding", ProviderCapabilityKind.OptionsChain, "external-id", Priority: 45,
                    FailoverConnectionIds: ["private-failover"], Notes: "private-note")])
        });
        var request = new SaveProviderConnectionTemplateRequest("external-id", "Equity options feed");
        using var foreign = app.GetTestClient();
        foreign.DefaultRequestHeaders.Add("X-Test-Tenant", "tenant-b");
        (await foreign.PostAsJsonAsync(Templates, request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await http.PostAsJsonAsync(Templates, request)).StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await http.GetStringAsync(UiApiRoutes.ProviderConnectionTemplateExport + "?name=Equity%20options%20feed");
        json.Should().NotContain("private").And.NotContain("external-id").And.NotContain("tenant-a");
        var persisted = Config.LoadRequired().ProviderConnections!.Templates!.Single().Template;
        var file = JsonSerializer.Deserialize<ProviderConnectionTemplateFile>(json, JsonSerializerOptions.Web);
        file!.SchemaVersion.Should().Be(1);
        file.Templates.Single().Should().BeEquivalentTo(persisted);
        persisted.SuggestedBinding.Should().Be(new ProviderTemplateBindingDto("OptionsChain", 45));
    }

    [Fact]
    public async Task DraftRefusesForeignAccessAndDiscardLeavesLegacyCredentialOwnershipAlone()
    {
        await using var app = await CreateAppAsync();
        using var owner = app.GetTestClient();
        using var foreign = app.GetTestClient();
        foreign.DefaultRequestHeaders.Add("X-Test-Tenant", "tenant-b");
        using var reader = app.GetTestClient();
        reader.DefaultRequestHeaders.Add("X-Test-Read-Only", "true");
        (await reader.GetAsync(Templates)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var option = (await owner.GetFromJsonAsync<ProviderConnectionTemplateOption[]>(Templates))!.First();
        var draft = await BeginAsync(owner, option, Guid.NewGuid(), "paper-test-account");
        var route = SetupRoute(draft.SetupId);
        var reserved = Config.LoadRequired().ProviderConnections!.TemplateSetups!.Single().ConnectionId;
        var credentials = new ProviderCredentialUpsertRequestDto(new Dictionary<string, string?>
        {
            ["KeyId"] = "synthetic-key",
            ["SecretKey"] = "synthetic-secret"
        }, "paper");
        (await foreign.PutAsJsonAsync(CredentialRoute(draft), credentials)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await reader.PutAsJsonAsync(CredentialRoute(draft), credentials)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await foreign.PostAsync(route, null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.PutAsJsonAsync(CredentialRoute(draft), credentials)).StatusCode.Should().Be(HttpStatusCode.OK);
        var attemptedClaim = new CreateOwnedProviderConnectionRequest(new(reserved, "alpaca", "Claim draft",
            ExternalAccountId: "paper-test-account"), "paper");
        (await owner.PostAsJsonAsync(UiApiRoutes.ProviderRoutingConnections, attemptedClaim)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await owner.DeleteAsync(route)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.PutAsJsonAsync(CredentialRoute(draft), credentials)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var section = Config.LoadRequired().ProviderConnections!;
        section.TemplateSetups.Should().BeEmpty();
        section.Connections.Should().BeNull();
        section.OwnershipRequiredProviders.Should().BeNull();
        var vault = (IScopedProviderCredentialStore)app.Services.GetRequiredService<IProviderCredentialStore>();
        (await vault.ReadScopedAsync("alpaca", new("tenant-a", reserved, "paper-test-account", "paper"))).Should().BeNull();
    }

    private static string SetupRoute(Guid id) => UiApiRoutes.WithParam(UiApiRoutes.ProviderConnectionTemplateSetup, "setupId", id.ToString("N"));

    private static string CredentialRoute(ProviderTemplateSetupDraft draft) => UiApiRoutes.WithQuery(
        UiApiRoutes.WithParam(UiApiRoutes.ProviderCredentialMutation, "providerId", draft.ProviderKind), "templateSetupId=" + draft.SetupId.ToString("N"));

    private static async Task<ProviderTemplateSetupDraft> BeginAsync(HttpClient http, ProviderConnectionTemplateOption option, Guid id, string? account = null)
    {
        var response = await http.PostAsJsonAsync(UiApiRoutes.ProviderConnectionTemplateSetups,
            new BeginProviderTemplateSetupRequest(id, option.Template.Name, option.Revision, account));
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProviderTemplateSetupDraft>())!;
    }

    private async Task<WebApplication> CreateAppAsync()
    {
        Directory.CreateDirectory(_root);
        await Config.SaveAsync(new AppConfig(DataRoot: _root));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(Config);
        builder.Services.AddSingleton(new ConfigStore(Config.ConfigPath));
        builder.Services.AddSingleton<IProviderCredentialStore>(new FileProviderCredentialStore(_root));
        builder.Services.AddSingleton<ProviderConnectionTemplateService>();
        builder.Services.AddSingleton<ProviderConnectionLifecycleService>();
        builder.Services.AddSingleton<ProviderConnectionService>();
        builder.Services.AddSingleton<ProviderRuntimeCredentialService>();
        builder.Services.AddSingleton<IHttpClientFactory, TemplateProbeHttpFactory>();
        builder.Services.AddRateLimiter(options => options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ => RateLimitPartition.GetNoLimiter("test")));
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            var tenant = context.Request.Headers["X-Test-Tenant"].FirstOrDefault() ?? "tenant-a";
            context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = context.Request.Headers.ContainsKey("X-Test-Read-Only")
                ? UserPermission.ManageCredentials : Grants;
            context.Items[LoginSessionMiddleware.CurrentUserKey] = "operator";
            context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = tenant;
            context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = tenant;
            await next();
        });
        app.UseRateLimiter();
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        app.MapProviderConnectionTemplateEndpoints(json);
        app.MapProviderConnectionEndpoints(json);
        app.MapProviderCredentialOwnershipEndpoints(json);
        await app.StartAsync();
        return app;
    }

    private sealed class TemplateProbeHttpFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new TemplateProbeHandler());
    }

    private sealed class TemplateProbeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri!.Host.Should().Be("api.tiingo.com");
            request.RequestUri.Query.Should().NotContain("test-key-");
            request.Headers.Authorization!.Scheme.Should().Be("Token");
            request.Headers.Authorization.Parameter.Should().StartWith("test-key-");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new[] { new { close = 101.25m } })
            });
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }
}
