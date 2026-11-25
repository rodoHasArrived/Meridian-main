using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Meridian.Tests.TestHelpers;

/// <summary>
/// Hands endpoint tests an in-memory host without rebuilding the endpoint graph for every test.
/// </summary>
/// <remarks>
/// <para>
/// Minimal APIs compile one request delegate per mapped route the first time the endpoint list is
/// read. For the workstation surface that is roughly 0.8 seconds per host, paid by every test even
/// though a test usually calls one route. The graph depends on which service types are registered
/// (that decides whether a handler parameter binds from services or the body), not on the service
/// instances, so tests that register the same service types can use one compiled graph.
/// </para>
/// <para>
/// A test still builds and starts its own <see cref="WebApplication"/> from its own registrations;
/// that host maps no routes and costs milliseconds. Requests go to the graph host, whose first
/// middleware replaces <see cref="HttpContext.RequestServices"/> with a scope from the test's host.
/// The graph host registers each test-owned service type as a forwarder to the calling test's
/// provider, so code that reads <c>app.Services</c> inside a handler reaches the same instances.
/// </para>
/// <para>
/// Sharing is refused, and the test gets a host of its own exactly as before, when it could change
/// what a test observes: a test-owned service is resolved while the graph is built (a mapper that
/// captures an instance), the test replaces framework registrations, or it registers framework
/// extension points, keyed services or open generics.
/// </para>
/// </remarks>
internal static class SharedEndpointGraph
{
    private static readonly object RequestStateKey = new();
    private static readonly RequestState ProbeState = new(static _ => { }, TestServices: null);
    private static readonly AsyncLocal<IServiceProvider?> CallingTestServices = new();
    private static readonly ConcurrentDictionary<string, Lazy<Task<WebApplication?>>> Graphs = new(StringComparer.Ordinal);
    // Set to "off" to give every test its own host, for example to rule sharing out while
    // investigating a failure that only appears when tests run together.
    private static readonly bool SharingDisabled = string.Equals(
        Environment.GetEnvironmentVariable("MERIDIAN_ENDPOINT_GRAPH_SHARING"), "off", StringComparison.OrdinalIgnoreCase);
    // Set to a file path to record why each host was or was not shared.
    private static readonly string? DecisionLogPath = Environment.GetEnvironmentVariable("MERIDIAN_ENDPOINT_GRAPH_LOG");
    private static readonly object DecisionLogGate = new();
    private static int _graphsBuilt;
    private static int _sharedHosts;
    private static int _isolatedHosts;

    /// <summary>Counts since process start: compiled graphs, tests served by one, tests given their own host.</summary>
    internal static (int GraphsBuilt, int SharedHosts, int IsolatedHosts) Statistics =>
        (Volatile.Read(ref _graphsBuilt), Volatile.Read(ref _sharedHosts), Volatile.Read(ref _isolatedHosts));

    /// <summary>Creates a started host for one test.</summary>
    /// <param name="graphVariant">
    /// Identifies everything <paramref name="configureGraph"/> does that is not derived from the
    /// registered service types, such as which optional route groups it maps.
    /// </param>
    /// <param name="createBuilder">Creates the builder; called for the test's host and for a new graph host.</param>
    /// <param name="configureServices">Adds the test's registrations.</param>
    /// <param name="configureRequest">Applies the test's caller identity to each request before the routes run.</param>
    /// <param name="configureGraph">Adds middleware and maps routes. Must not depend on per-test values.</param>
    /// <param name="allowSharing">False gives the test a host of its own.</param>
    public static async Task<EndpointTestApp> CreateAsync(
        string graphVariant,
        Func<WebApplicationBuilder> createBuilder,
        Action<IServiceCollection> configureServices,
        Action<HttpContext> configureRequest,
        Action<WebApplication> configureGraph,
        bool allowSharing = true)
    {
        var builder = createBuilder();
        builder.WebHost.UseTestServer();
        var frameworkDescriptors = builder.Services.ToArray();
        configureServices(builder.Services);

        var declined = "sharing is disabled";
        var testDescriptors = allowSharing && !SharingDisabled
            ? ShareableTestDescriptors(builder.Services, frameworkDescriptors, out declined)
            : null;
        if (testDescriptors is not null)
        {
            var key = GraphKey(graphVariant, testDescriptors);
            var graph = await Graphs
                .GetOrAdd(key, _ => new Lazy<Task<WebApplication?>>(
                    () => BuildGraphAsync(createBuilder, testDescriptors, configureGraph, graphVariant)))
                .Value
                .ConfigureAwait(false);
            if (graph is not null)
            {
                // Tests that inspect route metadata read the endpoint list from their own services.
                // Registered as an instance so disposing the test's host leaves the graph's list alone.
                builder.Services.AddSingleton(graph.Services.GetRequiredService<EndpointDataSource>());
                var testHost = builder.Build();
                await testHost.StartAsync().ConfigureAwait(false);
                Interlocked.Increment(ref _sharedHosts);
                LogDecision(graphVariant, "shared");
                return new EndpointTestApp(testHost, graph, new RequestState(configureRequest, testHost.Services));
            }

            declined = "the graph for these service types cannot be shared";
        }

        LogDecision(graphVariant, $"isolated: {declined}");
        var isolated = builder.Build();
        AddRequestStateMiddleware(isolated);
        configureGraph(isolated);
        await isolated.StartAsync().ConfigureAwait(false);
        Interlocked.Increment(ref _isolatedHosts);
        return new EndpointTestApp(isolated, isolated, new RequestState(configureRequest, TestServices: null));
    }

    private static async Task<WebApplication?> BuildGraphAsync(
        Func<WebApplicationBuilder> createBuilder,
        IReadOnlyList<ServiceDescriptor> testDescriptors,
        Action<WebApplication> configureGraph,
        string graphVariant)
    {
        // The graph must not inherit the ambient provider of whichever caller happens to build it.
        Task<WebApplication?> build;
        using (ExecutionContext.SuppressFlow())
        {
            build = Task.Run(Build);
        }

        return await build.ConfigureAwait(false);

        async Task<WebApplication?> Build()
        {
            var builder = createBuilder();
            builder.WebHost.UseTestServer();
            var resolvedWhileBuilding = new ConcurrentBag<Type>();
            var registrations = testDescriptors
                .GroupBy(descriptor => descriptor.ServiceType)
                .ToDictionary(group => group.Key, group => group.Count());
            var seen = new Dictionary<Type, int>();
            foreach (var descriptor in testDescriptors)
            {
                var serviceType = descriptor.ServiceType;
                var index = seen.GetValueOrDefault(serviceType);
                seen[serviceType] = index + 1;
                // A type registered several times is read as a list; position keeps each forwarder distinct.
                int? position = registrations[serviceType] > 1 ? index : null;
                // Transient, so the graph host never caches one test's instance for the next test.
                builder.Services.Add(ServiceDescriptor.Describe(
                    serviceType,
                    _ => Forward(serviceType, position, resolvedWhileBuilding),
                    ServiceLifetime.Transient));
            }

            var graph = builder.Build();
            try
            {
                AddRequestStateMiddleware(graph);
                configureGraph(graph);
                await graph.StartAsync().ConfigureAwait(false);
                // Routing compiles every route when it handles its first request, so send one now:
                // a mapper or filter that resolves a test-owned service while routes are built is
                // caught here, before any test depends on the graph. Reading the EndpointDataSource
                // service instead would compile a second copy that routing never uses.
                var server = graph.GetTestServer();
                using var probe = new HttpClient(server.CreateHandler(context => context.Items[RequestStateKey] = ProbeState))
                {
                    BaseAddress = server.BaseAddress
                };
                using var response = await probe.GetAsync("/").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Whatever went wrong belongs to the test: its own host reproduces it where it can assert on it.
                await graph.DisposeAsync().ConfigureAwait(false);
                LogDecision(graphVariant, resolvedWhileBuilding.IsEmpty
                    ? $"graph refused: {ex.GetType().Name}: {ex.Message}"
                    : $"graph refused: resolved while building: {string.Join(", ", resolvedWhileBuilding.Distinct())}");
                return null;
            }

            if (!resolvedWhileBuilding.IsEmpty)
            {
                await graph.DisposeAsync().ConfigureAwait(false);
                LogDecision(graphVariant, $"graph refused: resolved while building: {string.Join(", ", resolvedWhileBuilding.Distinct())}");
                return null;
            }

            Interlocked.Increment(ref _graphsBuilt);
            LogDecision(graphVariant, $"graph built for {testDescriptors.Count} test registrations");
            return graph;
        }
    }

    private static object Forward(Type serviceType, int? position, ConcurrentBag<Type> resolvedWhileBuilding)
    {
        var testServices = CallingTestServices.Value;
        if (testServices is null)
        {
            resolvedWhileBuilding.Add(serviceType);
            throw new InvalidOperationException(
                $"{serviceType} was resolved from the shared endpoint graph host outside a test request. " +
                "A mapper that needs an instance while routes are built cannot share its graph.");
        }

        return position is { } index
            ? testServices.GetServices(serviceType).ElementAt(index)!
            : testServices.GetRequiredService(serviceType);
    }

    private static void AddRequestStateMiddleware(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (context.Items[RequestStateKey] is not RequestState state)
            {
                throw new InvalidOperationException(
                    "Requests to an endpoint test host must come from EndpointTestApp.GetTestClient().");
            }

            if (ReferenceEquals(state, ProbeState))
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }

            state.ConfigureRequest(context);
            if (state.TestServices is null)
            {
                await next().ConfigureAwait(false);
                return;
            }

            var graphServices = context.RequestServices;
            var previous = CallingTestServices.Value;
            var scope = state.TestServices.CreateAsyncScope();
            try
            {
                context.RequestServices = scope.ServiceProvider;
                CallingTestServices.Value = state.TestServices;
                await next().ConfigureAwait(false);
            }
            finally
            {
                CallingTestServices.Value = previous;
                context.RequestServices = graphServices;
                await scope.DisposeAsync().ConfigureAwait(false);
            }
        });
    }

    private static IReadOnlyList<ServiceDescriptor>? ShareableTestDescriptors(
        IServiceCollection services,
        ServiceDescriptor[] frameworkDescriptors,
        out string declined)
    {
        declined = "the test replaced or removed framework registrations";
        if (services.Count < frameworkDescriptors.Length)
        {
            return null;
        }

        for (var i = 0; i < frameworkDescriptors.Length; i++)
        {
            if (!ReferenceEquals(services[i], frameworkDescriptors[i]))
            {
                return null;
            }
        }

        var testDescriptors = new List<ServiceDescriptor>(services.Count - frameworkDescriptors.Length);
        for (var i = frameworkDescriptors.Length; i < services.Count; i++)
        {
            var descriptor = services[i];
            if (descriptor.IsKeyedService ||
                descriptor.ServiceType.IsGenericTypeDefinition ||
                IsFrameworkExtensionPoint(descriptor.ServiceType))
            {
                declined = $"{descriptor.ServiceType} is keyed, an open generic or a framework extension point";
                return null;
            }

            testDescriptors.Add(descriptor);
        }

        return testDescriptors;
    }

    /// <summary>
    /// True for types the framework itself consumes, such as <c>IConfigureOptions&lt;JsonOptions&gt;</c> or
    /// <c>IHostedService</c>: the graph host would read them once and keep the result. A framework
    /// generic over an application type, such as <c>IOptions&lt;StorageOptions&gt;</c>, is only read by handlers.
    /// </summary>
    private static bool IsFrameworkExtensionPoint(Type serviceType)
    {
        if (!IsFrameworkType(serviceType))
        {
            return false;
        }

        return !serviceType.IsGenericType || serviceType.GetGenericArguments().All(IsFrameworkOrPrimitive);

        static bool IsFrameworkType(Type type) =>
            type.Namespace is { } ns && ns.StartsWith("Microsoft.", StringComparison.Ordinal);

        static bool IsFrameworkOrPrimitive(Type type) =>
            type.Namespace is not { } ns ||
            ns.StartsWith("Microsoft.", StringComparison.Ordinal) ||
            ns.StartsWith("System", StringComparison.Ordinal);
    }

    private static void LogDecision(string graphVariant, string decision)
    {
        if (string.IsNullOrWhiteSpace(DecisionLogPath))
        {
            return;
        }

        lock (DecisionLogGate)
        {
            File.AppendAllText(DecisionLogPath, $"{graphVariant}\t{decision}{Environment.NewLine}");
        }
    }

    private static string GraphKey(string graphVariant, IReadOnlyList<ServiceDescriptor> testDescriptors)
    {
        // Count matters as well as presence: IEnumerable<T> resolved from the graph host yields one
        // forwarder per registration.
        var key = new StringBuilder(graphVariant);
        foreach (var group in testDescriptors
            .GroupBy(descriptor => descriptor.ServiceType)
            .Select(group => (Name: group.Key.AssemblyQualifiedName ?? group.Key.ToString(), Count: group.Count()))
            .OrderBy(entry => entry.Name, StringComparer.Ordinal))
        {
            key.Append('\n').Append(group.Name).Append('*').Append(group.Count);
        }

        return key.ToString();
    }

    internal sealed record RequestState(Action<HttpContext> ConfigureRequest, IServiceProvider? TestServices);

    /// <summary>The host a test works with: its own services, and a client for the routes.</summary>
    internal sealed class EndpointTestApp : IAsyncDisposable
    {
        private readonly WebApplication _testHost;
        private readonly WebApplication _graphHost;
        private readonly RequestState _state;

        internal EndpointTestApp(WebApplication testHost, WebApplication graphHost, RequestState state)
        {
            _testHost = testHost;
            _graphHost = graphHost;
            _state = state;
        }

        /// <summary>The test's own root provider, as <c>WebApplication.Services</c> was.</summary>
        public IServiceProvider Services => _testHost.Services;

        /// <summary>True when the routes are served by a graph compiled once for several tests.</summary>
        public bool SharesEndpointGraph => !ReferenceEquals(_testHost, _graphHost);

        public HttpClient GetTestClient()
        {
            var server = _graphHost.GetTestServer();
            var state = _state;
            return new HttpClient(server.CreateHandler(context => context.Items[RequestStateKey] = state))
            {
                BaseAddress = server.BaseAddress
            };
        }

        // The graph host outlives the test; only the test's own host and its services are disposed.
        public ValueTask DisposeAsync() => _testHost.DisposeAsync();
    }
}
