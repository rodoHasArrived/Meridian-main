using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using FluentAssertions;
using Meridian.Application.Composition.Features;
using Meridian.Contracts.Api;
using Meridian.Contracts.Catalog;
using Meridian.Identity.Auth;
using Meridian.Infrastructure.Adapters.Core.SymbolResolution;
using Meridian.Storage.Services;
using Meridian.Ui.Shared.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class SymbolMappingOverrideEndpointTests
{
    [Fact]
    public async Task OperatorEditUpdatesTheLiveResolverAndRemovalPreservesSourceMappings()
    {
        using var fixture = new Fixture();
        await fixture.Registry.RegisterAsync(new CanonicalSymbolDefinition { Canonical = "BRK.B" });
        await fixture.Registry.SetProviderSymbolAsync("BRK.B", "nyse", "BRK B", SymbolMappingSources.SecurityMaster, isOverride: false);
        using var resolver = new CanonicalRegistrySymbolResolver(fixture.CollectorRegistry, mode: SymbolResolutionMode.Compare,
            providerModes: new Dictionary<string, SymbolResolutionMode>
            {
                ["stooq"] = SymbolResolutionMode.Canonical,
                ["ib"] = SymbolResolutionMode.Canonical
            });
        await using var app = await fixture.CreateAppAsync(UserPermission.ModifyConfig);
        using var client = app.GetTestClient();

        using var save = await client.PutAsJsonAsync("/api/symbols/mappings/BRK.B/providers/Stooq",
            new SymbolProviderOverrideRequest("brk-b.us"));
        save.StatusCode.Should().Be(HttpStatusCode.OK, await save.Content.ReadAsStringAsync());
        (await resolver.MapSymbolAsync("BRK.B", "input", "stooq")).Should().Be("brk-b.us");
        using var saveIb = await client.PutAsJsonAsync("/api/symbols/mappings/BRK.B/providers/IB",
            new SymbolProviderOverrideRequest("BRKB"));
        saveIb.StatusCode.Should().Be(HttpStatusCode.OK, await saveIb.Content.ReadAsStringAsync());
        (await resolver.MapSymbolAsync("BRK.B", "input", "ibkr")).Should().Be("BRKB");

        var response = await client.GetFromJsonAsync<SymbolMappingResponse>("/api/symbols/mappings/BRK.B");
        response!.ProviderSymbols.Should().Contain("stooq", "brk-b.us");
        response.ProviderSymbols.Should().NotContainKey("nyse");
        response.UpdatedAt.Should().NotBeNull();

        using var protectedRemoval = await client.DeleteAsync("/api/symbols/mappings/BRK.B/providers/nyse");
        protectedRemoval.StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var removal = await client.DeleteAsync("/api/symbols/mappings/BRK.B");
        removal.StatusCode.Should().Be(HttpStatusCode.OK);
        fixture.Registry.GetProviderSymbol("BRK.B", "stooq").Should().BeNull();
        fixture.Registry.GetProviderSymbol("BRK.B", "nyse").Should().Be("BRK B");
    }

    [Fact]
    public async Task CsvAcceptsDesktopColumnsAndExportsEscapedOverrides()
    {
        using var fixture = new Fixture();
        await using var app = await fixture.CreateAppAsync(UserPermission.ModifyConfig);
        using var client = app.GetTestClient();
        using var csv = new MultipartFormDataContent
        {
            { new StringContent("CanonicalSymbol,Name,Stooq,Alpha Vantage\r\nBRK.B,\"Berkshire, \"\"Class B\"\"\",brk-b.us,BRK-B\r\n", Encoding.UTF8, "text/csv"), "file", "mappings.csv" }
        };

        using var import = await client.PostAsync("/api/symbols/mappings/import", csv);
        import.StatusCode.Should().Be(HttpStatusCode.OK, await import.Content.ReadAsStringAsync());
        fixture.Registry.GetProviderSymbol("BRK.B", "stooq").Should().Be("brk-b.us");
        fixture.Registry.GetProviderSymbol("BRK.B", "alphavantage").Should().Be("BRK-B");

        var exported = await client.GetStringAsync("/api/symbols/mappings/csv/export");
        exported.Should().Contain("\"Berkshire, \"\"Class B\"\"\"").And.Contain("\"brk-b.us\"").And.Contain("\"BRK-B\"");
    }

    [Fact]
    public async Task AFailedOverrideWriteDoesNotPublishTheCandidate()
    {
        var directory = Path.Combine(Path.GetTempPath(), "meridian-mapping-write", Guid.NewGuid().ToString("N"));
        try
        {
            var fail = false;
            var store = new SymbolRegistryService(directory, (path, data, ct) => fail
                ? Task.FromException(new IOException("Synthetic write failure"))
                : File.WriteAllTextAsync(path, data, ct));
            var registry = new CanonicalSymbolRegistry(store);
            await registry.RegisterAsync(new CanonicalSymbolDefinition { Canonical = "AAPL" });
            var path = Path.Combine(directory, "_catalog", "symbols.json");
            var before = await File.ReadAllTextAsync(path);
            fail = true;
            var candidate = new CanonicalSymbolDefinition
            {
                Canonical = "AAPL",
                ProviderSymbols = new Dictionary<string, ProviderSymbolDefinition>
                {
                    ["stooq"] = new("aapl.us", SymbolMappingSources.Operator, IsOverride: true)
                }
            };

            var write = () => registry.SaveOperatorMappingsAsync([candidate]);

            await write.Should().ThrowAsync<IOException>();
            registry.GetProviderSymbol("AAPL", "stooq").Should().BeNull();
            (await File.ReadAllTextAsync(path)).Should().Be(before);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "meridian-mapping-endpoints", Guid.NewGuid().ToString("N"));
        public CanonicalSymbolRegistry Registry { get; }
        public CanonicalSymbolRegistry CollectorRegistry { get; }

        public Fixture()
        {
            Registry = ProcessSymbolRegistries.ResolverFor(ProcessSymbolRegistries.ForStorageRoot(_directory));
            CollectorRegistry = ProcessSymbolRegistries.ResolverFor(ProcessSymbolRegistries.ForStorageRoot(_directory + Path.DirectorySeparatorChar));
        }

        public async Task<WebApplication> CreateAppAsync(UserPermission permissions)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
            builder.WebHost.UseTestServer();
            builder.Services.AddSingleton<ICanonicalSymbolRegistry>(Registry);
            builder.Services.AddRateLimiter(options =>
                options.AddPolicy(UiEndpoints.MutationRateLimitPolicy, _ => RateLimitPartition.GetNoLimiter("test")));
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                context.Items[LoginSessionMiddleware.CurrentUserKey] = "mapping-operator";
                context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = "mapping-tenant";
                context.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
                await next();
            });
            app.UseRateLimiter();
            app.MapSymbolMappingEndpoints(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            await app.StartAsync();
            return app;
        }

        public void Dispose() => Directory.Delete(_directory, true);
    }
}
