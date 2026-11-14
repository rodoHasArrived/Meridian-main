using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Application.UI;
using Meridian.Contracts.Catalog;
using Meridian.Core.Config;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Core.SymbolResolution;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class SymbolMappingRolloutEndpointTests
{
    [Fact]
    public async Task PromoteWithoutAcknowledgement_DoesNotWriteConfig()
    {
        using var fixture = new Fixture();
        await using var app = await fixture.CreateAppAsync();
        var before = await File.ReadAllTextAsync(fixture.Store.ConfigPath);

        var response = await app.GetTestClient().PutAsJsonAsync("/api/symbols/mappings/rollout/yahoo",
            new SymbolMappingRolloutRequest(SymbolResolutionMode.Canonical, SymbolResolutionMode.Compare, 0));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await File.ReadAllTextAsync(fixture.Store.ConfigPath)).Should().Be(before);
    }

    [Fact]
    public async Task PromoteOneProvider_PreservesOthers_AndReportsPendingRestart()
    {
        using var fixture = new Fixture();
        await using var app = await fixture.CreateAppAsync();

        var response = await app.GetTestClient().PutAsJsonAsync("/api/symbols/mappings/rollout/yahoo", Confirmed());

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var modes = fixture.Store.LoadRequired().DataSources!.SymbolMappings!.ProviderResolutionModes!;
        modes.Should().ContainSingle();
        modes.Should().ContainKey("yahoo");
        modes["yahoo"].Should().Be(SymbolResolutionMode.Canonical);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("pendingRestart").GetBoolean().Should().BeTrue();
        document.RootElement.GetProperty("impact").GetString().Should().Be("RestartHost");

        var snapshot = await app.GetTestClient().GetAsync("/api/symbols/mappings/rollout");
        using var rollout = JsonDocument.Parse(await snapshot.Content.ReadAsStringAsync());
        var alpaca = rollout.RootElement.GetProperty("providers").EnumerateArray().Single(row => row.GetProperty("provider").GetString() == "alpaca");
        alpaca.GetProperty("savedMode").GetString().Should().Be("Compare");
    }

    [Fact]
    public async Task StaleComparisonEvidence_RefusesPromotion()
    {
        using var fixture = new Fixture();
        fixture.MismatchReader.SetupGet(reader => reader.TotalCount).Returns(2);
        await using var app = await fixture.CreateAppAsync();

        var response = await app.GetTestClient().PutAsJsonAsync("/api/symbols/mappings/rollout/yahoo", Confirmed());

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        fixture.Store.LoadRequired().DataSources.Should().BeNull();
    }

    [Fact]
    public async Task ViewOnlyCaller_CannotChangeRollout()
    {
        using var fixture = new Fixture();
        await using var app = await fixture.CreateAppAsync(UserPermission.ViewConfig);
        var response = await app.GetTestClient().PutAsJsonAsync("/api/symbols/mappings/rollout/yahoo", Confirmed());
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fixture.Store.LoadRequired().DataSources.Should().BeNull();
    }

    private static SymbolMappingRolloutRequest Confirmed() => new(
        SymbolResolutionMode.Canonical, SymbolResolutionMode.Compare, 0,
        EvidenceReviewed: true, ConfirmArchivesNotRewritten: true, ConfirmSeriesRemainSeparate: true,
        Reason: "Synthetic Yahoo comparison reviewed");

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-symbol-rollout", Guid.NewGuid().ToString("N"));
        public ConfigStore Store { get; }
        public Mock<ISymbolResolutionMismatchReader> MismatchReader { get; } = new();

        public Fixture()
        {
            Directory.CreateDirectory(_root);
            Store = new ConfigStore(Path.Combine(_root, "appsettings.json"));
            File.WriteAllText(Store.ConfigPath, "{}");
            MismatchReader.Setup(reader => reader.GetRecent()).Returns(Array.Empty<SymbolResolutionMismatch>());
        }

        public async Task<WebApplication> CreateAppAsync(UserPermission permissions = UserPermission.ModifyConfig)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton(Store);
            var registry = new Mock<ICanonicalSymbolRegistry>();
            builder.Services.AddSingleton(registry.Object);
            builder.Services.AddSingleton<ISymbolResolver>(new CanonicalRegistrySymbolResolver(registry.Object, mode: SymbolResolutionMode.Compare));
            builder.Services.AddSingleton(MismatchReader.Object);
            builder.Services.AddRateLimiter(options => options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ => RateLimitPartition.GetNoLimiter("test")));
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items[LoginSessionMiddleware.CurrentUserKey] = "symbol-rollout-test";
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "tenant-symbol-test";
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
                await next();
            });
            app.UseRateLimiter();
            app.MapSymbolMappingEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await app.StartAsync();
            return app;
        }

        public void Dispose() => Directory.Delete(_root, true);
    }
}
