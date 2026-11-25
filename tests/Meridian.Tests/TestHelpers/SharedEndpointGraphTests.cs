using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Meridian.Tests.TestHelpers;

/// <summary>
/// Pins the rules that make sharing a compiled route graph invisible to the tests using it.
/// </summary>
public sealed class SharedEndpointGraphTests
{
    [Fact]
    public async Task TestsWithTheSameServiceTypes_ShareAGraphButKeepTheirOwnInstances()
    {
        var variant = NewVariant();
        var mapped = 0;

        await using var first = await CreateAsync(variant, new Counter("first"), onMap: () => mapped++);
        await using var second = await CreateAsync(variant, new Counter("second"), onMap: () => mapped++);

        first.SharesEndpointGraph.Should().BeTrue();
        second.SharesEndpointGraph.Should().BeTrue();
        mapped.Should().Be(1, "the second test must reuse the routes the first one compiled");

        (await first.GetTestClient().GetStringAsync("/injected")).Should().Be("first:1");
        (await second.GetTestClient().GetStringAsync("/injected")).Should().Be("second:1");
        (await first.GetTestClient().GetStringAsync("/injected")).Should().Be("first:2");

        first.Services.GetRequiredService<Counter>().Value.Should().Be(2);
        second.Services.GetRequiredService<Counter>().Value.Should().Be(1);
    }

    [Fact]
    public async Task HandlersReadingTheHostRootProvider_ReachTheCallingTestsInstances()
    {
        var variant = NewVariant();

        await using var first = await CreateAsync(variant, new Counter("first"));
        await using var second = await CreateAsync(variant, new Counter("second"));

        (await second.GetTestClient().GetStringAsync("/host-root")).Should().Be("second:1");
        (await first.GetTestClient().GetStringAsync("/host-root")).Should().Be("first:1");
    }

    [Fact]
    public async Task ScopedServices_AreCreatedPerRequestFromTheTestsOwnProvider()
    {
        var variant = NewVariant();
        var created = 0;

        await using var app = await CreateAsync(
            variant,
            new Counter("scoped"),
            configureServices: services => services.AddScoped(_ => new RequestMarker(Interlocked.Increment(ref created))));

        var client = app.GetTestClient();
        (await client.GetStringAsync("/scoped")).Should().Be("1");
        (await client.GetStringAsync("/scoped")).Should().Be("2");
    }

    [Fact]
    public async Task CallerIdentity_IsAppliedPerTestOnASharedGraph()
    {
        var variant = NewVariant();

        await using var first = await CreateAsync(variant, new Counter("a"), actor: "alice");
        await using var second = await CreateAsync(variant, new Counter("b"), actor: "bob");

        second.SharesEndpointGraph.Should().BeTrue();
        (await first.GetTestClient().GetStringAsync("/actor")).Should().Be("alice");
        (await second.GetTestClient().GetStringAsync("/actor")).Should().Be("bob");
    }

    [Fact]
    public async Task RouteMetadata_IsReadableFromTheTestsOwnServices()
    {
        await using var app = await CreateAsync(NewVariant(), new Counter("metadata"));

        app.SharesEndpointGraph.Should().BeTrue();
        app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .Should().Contain("/injected");
    }

    [Fact]
    public async Task DifferentServiceTypes_DoNotShareAGraph()
    {
        var variant = NewVariant();
        var mapped = 0;

        await using var counterOnly = await CreateAsync(variant, new Counter("one"), onMap: () => mapped++);
        await using var counterAndMarker = await CreateAsync(
            variant,
            new Counter("two"),
            onMap: () => mapped++,
            configureServices: services => services.AddScoped(_ => new RequestMarker(7)));

        mapped.Should().Be(2, "registered service types decide how handler parameters bind, so they select the graph");
        (await counterOnly.GetTestClient().GetStringAsync("/injected")).Should().Be("one:1");
        (await counterAndMarker.GetTestClient().GetStringAsync("/scoped")).Should().Be("7");
    }

    [Fact]
    public async Task MapperThatResolvesATestServiceWhileMapping_GetsItsOwnHost()
    {
        var variant = NewVariant();

        await using var first = await CreateAsync(variant, new Counter("first"), captureWhileMapping: true);
        await using var second = await CreateAsync(variant, new Counter("second"), captureWhileMapping: true);

        first.SharesEndpointGraph.Should().BeFalse();
        second.SharesEndpointGraph.Should().BeFalse();
        (await first.GetTestClient().GetStringAsync("/captured")).Should().Be("first");
        (await second.GetTestClient().GetStringAsync("/captured")).Should().Be("second");
    }

    [Fact]
    public async Task FrameworkExtensionPoints_GetTheirOwnHost()
    {
        await using var app = await CreateAsync(
            NewVariant(),
            new Counter("options"),
            configureServices: services => services.Configure<RouteOptions>(options => options.LowercaseUrls = true));

        app.SharesEndpointGraph.Should().BeFalse();
        (await app.GetTestClient().GetStringAsync("/injected")).Should().Be("options:1");
    }

    [Fact]
    public async Task ApplicationOptionsAndReplacedFrameworkServices_AreClassifiedSeparately()
    {
        await using var applicationOptions = await CreateAsync(
            NewVariant(),
            new Counter("app-options"),
            configureServices: services => services.AddSingleton(Microsoft.Extensions.Options.Options.Create(new Counter("wrapped"))));
        await using var replacedFramework = await CreateAsync(
            NewVariant(),
            new Counter("replaced"),
            configureServices: services => services.Replace(ServiceDescriptor.Singleton<IHostLifetime, QuietHostLifetime>()));

        applicationOptions.SharesEndpointGraph.Should().BeTrue();
        replacedFramework.SharesEndpointGraph.Should().BeFalse();
    }

    private static string NewVariant() => $"shared-endpoint-graph-tests:{Guid.NewGuid():N}";

    private static Task<SharedEndpointGraph.EndpointTestApp> CreateAsync(
        string variant,
        Counter counter,
        string actor = "tester",
        bool captureWhileMapping = false,
        Action? onMap = null,
        Action<IServiceCollection>? configureServices = null)
        => SharedEndpointGraph.CreateAsync(
            graphVariant: $"{variant}:capture={captureWhileMapping}",
            createBuilder: static () => WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development
            }),
            configureServices: services =>
            {
                services.AddSingleton(counter);
                configureServices?.Invoke(services);
            },
            configureRequest: context => context.Items["actor"] = actor,
            configureGraph: app =>
            {
                onMap?.Invoke();
                app.MapGet("/injected", (Counter injected) => injected.Next());
                app.MapGet("/host-root", () => app.Services.GetRequiredService<Counter>().Next());
                app.MapGet("/scoped", (HttpContext context) =>
                    context.RequestServices.GetRequiredService<RequestMarker>().Sequence.ToString());
                app.MapGet("/actor", (HttpContext context) => (string)context.Items["actor"]!);
                if (captureWhileMapping)
                {
                    var captured = app.Services.GetRequiredService<Counter>();
                    app.MapGet("/captured", () => captured.Name);
                }
            });

    private sealed class Counter(string name)
    {
        private int _value;

        public string Name { get; } = name;

        public int Value => Volatile.Read(ref _value);

        public string Next() => $"{Name}:{Interlocked.Increment(ref _value)}";
    }

    private sealed record RequestMarker(int Sequence);

    private sealed class QuietHostLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
