using System.Collections.Concurrent;
using System.IO;
using Meridian.Application.Config;
using Meridian.Contracts.Api;
using Meridian.Infrastructure;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.ProviderSdk;

namespace Meridian.Application.ProviderRouting;

/// <summary>
/// Relationship-aware capability router.
/// </summary>
public sealed class ProviderRoutingService : ICapabilityRouter
{
    private readonly UI.ConfigStore _store;
    private readonly IProviderConnectionHealthSource _healthSource;
    private readonly IProviderFamilyCatalogService _catalog;
    private readonly ConcurrentQueue<ProviderRouteResult> _history = new();
    private readonly object _snapshotSync = new();
    private volatile ProviderRoutingSnapshot? _snapshot;

    public ProviderRoutingService(
        UI.ConfigStore store,
        IProviderConnectionHealthSource healthSource,
        IProviderFamilyCatalogService catalog)
    {
        _store = store;
        _healthSource = healthSource;
        _catalog = catalog;
    }

    public async ValueTask<ProviderRouteResult> RouteAsync(ProviderRouteContext context, CancellationToken ct = default)
    {
        var snapshot = GetSnapshot();
        var connections = snapshot.ConnectionsById;
        var bindings = snapshot.GetBindings(context.Capability);
        var policy = snapshot.GetPolicy(context.Capability);

        var candidates = new List<ProviderRouteDecision>();
        var skipped = new List<string>();
        var healthCache = new Dictionary<string, ProviderConnectionHealthSnapshot>(StringComparer.OrdinalIgnoreCase);
        var adapterCache = new Dictionary<string, IProviderFamilyAdapter?>(StringComparer.OrdinalIgnoreCase);

        async ValueTask<ProviderConnectionHealthSnapshot> GetHealthAsync(ProviderConnectionConfig connection)
        {
            if (healthCache.TryGetValue(connection.ConnectionId, out var cachedHealth))
                return cachedHealth;

            var health = await _healthSource
                .GetHealthAsync(connection.ConnectionId, connection.ProviderFamilyId, ct)
                .ConfigureAwait(false);

            healthCache[connection.ConnectionId] = health;
            return health;
        }

        IProviderFamilyAdapter? GetAdapter(string providerFamilyId)
        {
            if (adapterCache.TryGetValue(providerFamilyId, out var cachedAdapter))
                return cachedAdapter;

            var adapter = _catalog.GetFamily(providerFamilyId);
            adapterCache[providerFamilyId] = adapter;
            return adapter;
        }

        foreach (var binding in bindings)
        {
            var scopeRank = (binding.Target ?? new ProviderBindingTarget()).GetMatchScore(context);
            if (scopeRank < 0)
                continue;

            if (!connections.TryGetValue(binding.ConnectionId, out var connection))
            {
                skipped.Add($"Binding '{binding.BindingId}' references missing connection '{binding.ConnectionId}'.");
                continue;
            }

            var connectionScopeRank = (connection.Scope ?? new ProviderConnectionScope()).GetMatchScore(context);
            if (connectionScopeRank < 0)
            {
                skipped.Add($"Connection '{connection.ConnectionId}' scope does not match the requested route.");
                continue;
            }

            var adapter = GetAdapter(connection.ProviderFamilyId);
            if (!SupportsCapability(connection, adapter, context.Capability))
            {
                skipped.Add($"Provider family '{connection.ProviderFamilyId}' does not support capability '{context.Capability}'.");
                continue;
            }

            var health = await GetHealthAsync(connection).ConfigureAwait(false);
            var effectivePolicy = binding.SafetyModeOverride is ProviderSafetyMode safetyOverride
                ? policy with { Mode = safetyOverride }
                : policy;

            var reasons = new List<string>
            {
                $"Matched binding '{binding.BindingId}'.",
                $"Matched scope rank {Math.Max(scopeRank, connectionScopeRank)}.",
                health.IsHealthy
                    ? $"Health status is {health.Status}."
                    : $"Health status is degraded: {health.Status}."
            };

            var fallbackConnectionIds = ResolveFallbacks(binding, connection, connections, effectivePolicy);
            var policyGate = DeterminePolicyGate(context, connection, binding, effectivePolicy);

            candidates.Add(new ProviderRouteDecision(
                ConnectionId: connection.ConnectionId,
                ProviderFamilyId: connection.ProviderFamilyId,
                Capability: context.Capability,
                SafetyMode: effectivePolicy.Mode,
                ScopeRank: Math.Max(scopeRank, connectionScopeRank),
                Priority: binding.Priority,
                IsHealthy: health.IsHealthy,
                ReasonCodes: reasons,
                FallbackConnectionIds: fallbackConnectionIds,
                PolicyGate: policyGate));

            foreach (var fallbackConnectionId in fallbackConnectionIds)
            {
                if (!connections.TryGetValue(fallbackConnectionId, out var fallbackConnection))
                {
                    skipped.Add($"Fallback connection '{fallbackConnectionId}' is missing.");
                    continue;
                }

                if (!SupportsCapability(fallbackConnection, GetAdapter(fallbackConnection.ProviderFamilyId), context.Capability))
                {
                    skipped.Add($"Fallback connection '{fallbackConnectionId}' does not support capability '{context.Capability}'.");
                    continue;
                }

                var fallbackHealth = await GetHealthAsync(fallbackConnection).ConfigureAwait(false);

                candidates.Add(new ProviderRouteDecision(
                    ConnectionId: fallbackConnection.ConnectionId,
                    ProviderFamilyId: fallbackConnection.ProviderFamilyId,
                    Capability: context.Capability,
                    SafetyMode: effectivePolicy.Mode,
                    ScopeRank: Math.Max(scopeRank, connectionScopeRank),
                    Priority: binding.Priority + 1,
                    IsHealthy: fallbackHealth.IsHealthy,
                    ReasonCodes:
                    [
                        $"Fallback candidate for binding '{binding.BindingId}'.",
                        $"Primary connection is '{connection.ConnectionId}'.",
                        fallbackHealth.IsHealthy
                            ? $"Health status is {fallbackHealth.Status}."
                            : $"Health status is degraded: {fallbackHealth.Status}."
                    ],
                    FallbackConnectionIds: Array.Empty<string>(),
                    PolicyGate: DeterminePolicyGate(context, fallbackConnection, binding, effectivePolicy)));
            }
        }

        var orderedCandidates = candidates
            .OrderByDescending(c => c.ScopeRank)
            .ThenBy(c => c.Priority)
            .ThenByDescending(c => c.IsHealthy)
            .ToList();

        ProviderRouteDecision? selected = null;
        string? resultGate = null;
        var requiresManualApproval = false;

        foreach (var candidate in orderedCandidates)
        {
            if (!candidate.IsHealthy && candidate.SafetyMode == ProviderSafetyMode.HealthAwareFailover)
            {
                var fallback = orderedCandidates.FirstOrDefault(other =>
                    candidate.FallbackConnectionIds.Contains(other.ConnectionId, StringComparer.OrdinalIgnoreCase) &&
                    other.IsHealthy);

                if (fallback is not null)
                {
                    selected = fallback with
                    {
                        ReasonCodes = fallback.ReasonCodes.Concat(new[] { $"Selected as failover for '{candidate.ConnectionId}'." }).ToArray()
                    };
                    break;
                }
            }

            if (!string.IsNullOrWhiteSpace(candidate.PolicyGate))
            {
                resultGate = candidate.PolicyGate;
                if (candidate.SafetyMode == ProviderSafetyMode.ManualApprovalRequired)
                    requiresManualApproval = true;

                continue;
            }

            if (!candidate.IsHealthy &&
                (candidate.SafetyMode == ProviderSafetyMode.NoAutomaticFailover ||
                 candidate.SafetyMode == ProviderSafetyMode.SameInstitutionOnly))
            {
                resultGate = $"Primary connection '{candidate.ConnectionId}' is unhealthy and automatic failover is blocked by policy.";
                continue;
            }

            if (candidate.SafetyMode == ProviderSafetyMode.ManualApprovalRequired)
            {
                requiresManualApproval = true;
                resultGate = $"Capability '{candidate.Capability}' requires manual approval before routing.";
            }

            selected = candidate;
            break;
        }

        if (selected is null && orderedCandidates.Count == 0 && policy.RequireExplicitBinding)
        {
            resultGate = $"Capability '{context.Capability}' requires an explicit scoped binding.";
        }

        var result = new ProviderRouteResult(
            Context: context,
            SelectedDecision: selected,
            Candidates: orderedCandidates,
            SkippedCandidates: skipped,
            RequiresManualApproval: requiresManualApproval,
            PolicyGate: resultGate);

        _history.Enqueue(result);
        while (_history.Count > 50 && _history.TryDequeue(out _))
        {
        }

        return result;
    }

    public IReadOnlyList<ProviderRouteResult> GetRouteHistory() => _history.ToArray();

    private ProviderRoutingSnapshot GetSnapshot()
    {
        var stamp = ProviderRoutingSnapshotStamp.ForPath(_store.ConfigPath);
        var cached = _snapshot;
        if (cached is not null && cached.Stamp.Equals(stamp))
            return cached;

        lock (_snapshotSync)
        {
            cached = _snapshot;
            if (cached is not null && cached.Stamp.Equals(stamp))
                return cached;

            var cfg = _store.Load();
            cached = ProviderRoutingSnapshot.Build(cfg, stamp);
            _snapshot = cached;
            return cached;
        }
    }

    private static bool SupportsCapability(
        ProviderConnectionConfig connection,
        IProviderFamilyAdapter? adapter,
        ProviderCapabilityKind capability)
    {
        if (adapter?.SupportsCapability(capability) == true)
            return true;

        return connection.ConnectionType switch
        {
            ProviderConnectionType.Brokerage => capability is
                ProviderCapabilityKind.OrderExecution or
                ProviderCapabilityKind.ExecutionHistory or
                ProviderCapabilityKind.AccountBalances or
                ProviderCapabilityKind.AccountPositions or
                ProviderCapabilityKind.ReconciliationFeed,

            ProviderConnectionType.Bank => capability is
                ProviderCapabilityKind.CashTransactions or
                ProviderCapabilityKind.BankStatements or
                ProviderCapabilityKind.AccountBalances or
                ProviderCapabilityKind.ReconciliationFeed,

            ProviderConnectionType.Custodian => capability is
                ProviderCapabilityKind.AccountPositions or
                ProviderCapabilityKind.ExecutionHistory or
                ProviderCapabilityKind.ReconciliationFeed,

            ProviderConnectionType.DataVendor or ProviderConnectionType.Exchange => capability is
                ProviderCapabilityKind.RealtimeMarketData or
                ProviderCapabilityKind.HistoricalBars or
                ProviderCapabilityKind.HistoricalTrades or
                ProviderCapabilityKind.HistoricalQuotes or
                ProviderCapabilityKind.SymbolSearch or
                ProviderCapabilityKind.ReferenceData or
                ProviderCapabilityKind.SecurityMasterSeed or
                ProviderCapabilityKind.CorporateActions or
                ProviderCapabilityKind.OptionsChain,

            _ => false
        };
    }

    private static string? DeterminePolicyGate(
        ProviderRouteContext context,
        ProviderConnectionConfig connection,
        ProviderBindingConfig binding,
        ProviderSafetyPolicy policy)
    {
        var strictAccountCapability =
            policy.Capability is ProviderCapabilityKind.OrderExecution or
            ProviderCapabilityKind.ExecutionHistory or
            ProviderCapabilityKind.AccountBalances or
            ProviderCapabilityKind.AccountPositions or
            ProviderCapabilityKind.CashTransactions or
            ProviderCapabilityKind.BankStatements;

        if (policy.RequireExplicitBinding && strictAccountCapability && context.AccountId is not null && binding.Target?.AccountId is null)
            return $"Capability '{policy.Capability}' requires an account-scoped binding.";

        if (policy.RequireProductionReady && !connection.ProductionReady)
            return $"Connection '{connection.ConnectionId}' is not production ready.";

        return null;
    }

    private static IReadOnlyList<string> ResolveFallbacks(
        ProviderBindingConfig binding,
        ProviderConnectionConfig primary,
        IReadOnlyDictionary<string, ProviderConnectionConfig> connections,
        ProviderSafetyPolicy policy)
    {
        var fallbacks = (binding.FailoverConnectionIds ?? Array.Empty<string>()).ToList();
        if (policy.AllowedFailoverConnectionIds is { Count: > 0 })
        {
            fallbacks = fallbacks
                .Where(id => policy.AllowedFailoverConnectionIds.Contains(id, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        if (policy.Mode == ProviderSafetyMode.SameInstitutionOnly)
        {
            fallbacks = fallbacks
                .Where(id => connections.TryGetValue(id, out var fallback) &&
                    (string.Equals(primary.InstitutionId, fallback.InstitutionId, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(primary.ProviderFamilyId, fallback.ProviderFamilyId, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        if (policy.AllowedProviderFamilies is { Count: > 0 })
        {
            fallbacks = fallbacks
                .Where(id => connections.TryGetValue(id, out var fallback) &&
                    policy.AllowedProviderFamilies.Contains(fallback.ProviderFamilyId, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        return fallbacks;
    }

    private readonly record struct ProviderRoutingSnapshotStamp(bool Exists, long Length, DateTime LastWriteUtc)
    {
        public static ProviderRoutingSnapshotStamp ForPath(string configPath)
        {
            if (!File.Exists(configPath))
                return new ProviderRoutingSnapshotStamp(false, 0, DateTime.MinValue);

            var file = new FileInfo(configPath);
            return new ProviderRoutingSnapshotStamp(
                Exists: true,
                Length: file.Length,
                LastWriteUtc: file.LastWriteTimeUtc);
        }
    }

    private sealed class ProviderRoutingSnapshot
    {
        private readonly IReadOnlyDictionary<ProviderCapabilityKind, ProviderBindingConfig[]> _bindingsByCapability;
        private readonly IReadOnlyDictionary<ProviderCapabilityKind, ProviderSafetyPolicy> _policiesByCapability;

        private ProviderRoutingSnapshot(
            ProviderRoutingSnapshotStamp stamp,
            IReadOnlyDictionary<string, ProviderConnectionConfig> connectionsById,
            IReadOnlyDictionary<ProviderCapabilityKind, ProviderBindingConfig[]> bindingsByCapability,
            IReadOnlyDictionary<ProviderCapabilityKind, ProviderSafetyPolicy> policiesByCapability)
        {
            Stamp = stamp;
            ConnectionsById = connectionsById;
            _bindingsByCapability = bindingsByCapability;
            _policiesByCapability = policiesByCapability;
        }

        public ProviderRoutingSnapshotStamp Stamp { get; }

        public IReadOnlyDictionary<string, ProviderConnectionConfig> ConnectionsById { get; }

        public ProviderBindingConfig[] GetBindings(ProviderCapabilityKind capability)
            => _bindingsByCapability.TryGetValue(capability, out var bindings)
                ? bindings
                : Array.Empty<ProviderBindingConfig>();

        public ProviderSafetyPolicy GetPolicy(ProviderCapabilityKind capability)
            => _policiesByCapability.TryGetValue(capability, out var policy)
                ? policy
                : ProviderSafetyPolicy.DefaultFor(capability);

        public static ProviderRoutingSnapshot Build(AppConfig cfg, ProviderRoutingSnapshotStamp stamp)
        {
            var connectionsById = ProviderRoutingConfigExtensions
                .GetEffectiveConnections(cfg)
                .ToDictionary(connection => connection.ConnectionId, StringComparer.OrdinalIgnoreCase);

            var bindingsByCapability = ProviderRoutingConfigExtensions
                .GetEffectiveBindings(cfg)
                .Where(binding => binding.Enabled)
                .GroupBy(binding => binding.Capability)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(binding => binding.Priority)
                        .ToArray());

            var policiesByCapability = ProviderRoutingConfigExtensions
                .GetEffectivePolicies(cfg)
                .ToDictionary(policy => policy.Capability, ProviderRoutingMapper.ToPolicy);

            return new ProviderRoutingSnapshot(
                stamp,
                connectionsById,
                bindingsByCapability,
                policiesByCapability);
        }
    }
}

public interface IProviderFamilyCatalogService
{
    IReadOnlyList<IProviderFamilyAdapter> GetFamilies();

    IProviderFamilyAdapter? GetFamily(string providerFamilyId);
}

internal sealed class ProviderFamilyCatalogService : IProviderFamilyCatalogService
{
    private readonly ProviderRegistry _providerRegistry;
    private readonly IEnumerable<IOptionsChainProvider> _optionsProviders;
    private readonly IEnumerable<ICorporateActionProvider> _corporateActionProviders;

    public ProviderFamilyCatalogService(
        ProviderRegistry providerRegistry,
        IEnumerable<IOptionsChainProvider> optionsProviders,
        IEnumerable<ICorporateActionProvider> corporateActionProviders)
    {
        _providerRegistry = providerRegistry;
        _optionsProviders = optionsProviders;
        _corporateActionProviders = corporateActionProviders;
    }

    public IReadOnlyList<IProviderFamilyAdapter> GetFamilies()
    {
        var families = new Dictionary<string, MutableProviderFamilyAdapter>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in ProviderCatalog.GetAll())
        {
            var family = GetOrCreate(families, entry.ProviderId, entry.DisplayName, entry.Description);
            foreach (var descriptor in Describe(entry))
            {
                family.RegisterCapability(descriptor, resolver: null);
            }
        }

        foreach (var sourceId in _providerRegistry.SupportedStreamingSources)
        {
            var entry = ProviderCatalog.Get(sourceId);
            var family = GetOrCreate(
                families,
                sourceId,
                entry?.DisplayName ?? sourceId,
                entry?.Description ?? "Legacy streaming provider family");

            family.RegisterCapability(
                new ProviderCapabilityDescriptor(ProviderCapabilityKind.RealtimeMarketData, "Legacy streaming market data", SupportsFailover: true),
                resolver: () => _providerRegistry.CreateStreamingClient(sourceId),
                tester: static (_, _) => Task.FromResult(new ProviderConnectionTestResult(true, ["Streaming factory is registered."], DateTimeOffset.UtcNow, "registered")));
        }

        foreach (var historical in _providerRegistry.GetProviders<IHistoricalDataProvider>())
        {
            var family = GetOrCreate(families, historical.ProviderId, historical.ProviderDisplayName, historical.ProviderDescription);
            family.RegisterCapability(new ProviderCapabilityDescriptor(ProviderCapabilityKind.HistoricalBars, "Legacy historical bars provider"), () => historical, (_, ct) => TestAvailabilityAsync(historical.IsAvailableAsync, ct));
            if (historical.SupportsTrades)
                family.RegisterCapability(new ProviderCapabilityDescriptor(ProviderCapabilityKind.HistoricalTrades, "Legacy historical trades provider"), () => historical);
            if (historical.SupportsQuotes)
                family.RegisterCapability(new ProviderCapabilityDescriptor(ProviderCapabilityKind.HistoricalQuotes, "Legacy historical quotes provider"), () => historical);
            if (historical.SupportsDividends || historical.SupportsSplits)
                family.RegisterCapability(new ProviderCapabilityDescriptor(ProviderCapabilityKind.CorporateActions, "Corporate action history provider"), () => historical);

            family.RegisterCapability(new ProviderCapabilityDescriptor(ProviderCapabilityKind.ReferenceData, "Reference data inferred from historical provider"), () => historical);
            family.RegisterCapability(new ProviderCapabilityDescriptor(ProviderCapabilityKind.SecurityMasterSeed, "Security master seeding inferred from historical provider"), () => historical);
        }

        foreach (var search in _providerRegistry.GetProviders<ISymbolSearchProvider>())
        {
            var family = GetOrCreate(families, search.ProviderId, search.ProviderDisplayName, search.ProviderDescription);
            family.RegisterCapability(new ProviderCapabilityDescriptor(ProviderCapabilityKind.SymbolSearch, "Legacy symbol search provider"), () => search, (_, ct) => TestAvailabilityAsync(search.IsAvailableAsync, ct));
            family.RegisterCapability(new ProviderCapabilityDescriptor(ProviderCapabilityKind.ReferenceData, "Reference data inferred from symbol search"), () => search);
            family.RegisterCapability(new ProviderCapabilityDescriptor(ProviderCapabilityKind.SecurityMasterSeed, "Security master seed provider inferred from symbol search"), () => search);
        }

        foreach (var options in _optionsProviders)
        {
            var family = GetOrCreate(families, options.ProviderId, options.ProviderDisplayName, options.ProviderDescription);
            family.RegisterCapability(new ProviderCapabilityDescriptor(ProviderCapabilityKind.OptionsChain, "Options chain provider"), () => options);
        }

        foreach (var corporateActions in _corporateActionProviders)
        {
            var family = GetOrCreate(families, corporateActions.ProviderId, corporateActions.ProviderId, "Corporate action provider");
            family.RegisterCapability(new ProviderCapabilityDescriptor(ProviderCapabilityKind.CorporateActions, "Corporate action provider"), () => corporateActions);
        }

        return families.Values.Cast<IProviderFamilyAdapter>().ToArray();
    }

    public IProviderFamilyAdapter? GetFamily(string providerFamilyId)
        => GetFamilies().FirstOrDefault(f => string.Equals(f.ProviderFamilyId, providerFamilyId, StringComparison.OrdinalIgnoreCase));

    private static MutableProviderFamilyAdapter GetOrCreate(
        IDictionary<string, MutableProviderFamilyAdapter> families,
        string providerFamilyId,
        string displayName,
        string description)
    {
        if (!families.TryGetValue(providerFamilyId, out var adapter))
        {
            adapter = new MutableProviderFamilyAdapter(providerFamilyId, displayName, description);
            families[providerFamilyId] = adapter;
        }

        return adapter;
    }

    private static IEnumerable<ProviderCapabilityDescriptor> Describe(ProviderCatalogEntry entry)
    {
        if (entry.Capabilities.SupportsStreaming)
            yield return new ProviderCapabilityDescriptor(ProviderCapabilityKind.RealtimeMarketData, "Catalog-derived realtime market data");

        if (entry.Capabilities.SupportsAdjustedPrices || entry.Capabilities.SupportsIntraday)
            yield return new ProviderCapabilityDescriptor(ProviderCapabilityKind.HistoricalBars, "Catalog-derived historical bars");

        if (entry.Capabilities.SupportsTrades)
            yield return new ProviderCapabilityDescriptor(ProviderCapabilityKind.HistoricalTrades, "Catalog-derived historical trades");

        if (entry.Capabilities.SupportsQuotes)
            yield return new ProviderCapabilityDescriptor(ProviderCapabilityKind.HistoricalQuotes, "Catalog-derived historical quotes");

        if (entry.Capabilities.SupportsDividends || entry.Capabilities.SupportsSplits)
            yield return new ProviderCapabilityDescriptor(ProviderCapabilityKind.CorporateActions, "Catalog-derived corporate actions");
    }

    private static async Task<ProviderConnectionTestResult> TestAvailabilityAsync(
        Func<CancellationToken, Task<bool>> availability,
        CancellationToken ct)
    {
        var available = await availability(ct).ConfigureAwait(false);
        return new ProviderConnectionTestResult(
            Success: available,
            Checks: available ? ["Availability probe passed."] : ["Availability probe failed."],
            TestedAt: DateTimeOffset.UtcNow,
            Status: available ? "healthy" : "unavailable");
    }
}

internal sealed class MutableProviderFamilyAdapter : IProviderFamilyAdapter
{
    private readonly Dictionary<ProviderCapabilityKind, ProviderCapabilityDescriptor> _descriptors = new();
    private readonly Dictionary<ProviderCapabilityKind, Func<object?>?> _resolvers = new();
    private Func<string, CancellationToken, Task<ProviderConnectionTestResult>>? _tester;

    public MutableProviderFamilyAdapter(string providerFamilyId, string displayName, string description)
    {
        ProviderFamilyId = providerFamilyId;
        DisplayName = displayName;
        Description = description;
    }

    public string ProviderFamilyId { get; }

    public string DisplayName { get; }

    public string Description { get; }

    public IReadOnlyList<ProviderCapabilityDescriptor> CapabilityDescriptors => _descriptors.Values.ToArray();

    public bool SupportsCapability(ProviderCapabilityKind capability) => _descriptors.ContainsKey(capability);

    public Task InitializeConnectionAsync(string connectionId, ProviderConnectionScope scope, CancellationToken ct = default)
        => Task.CompletedTask;

    public async Task<ProviderConnectionTestResult> TestConnectionAsync(string connectionId, CancellationToken ct = default)
    {
        if (_tester is null)
        {
            return new ProviderConnectionTestResult(
                Success: true,
                Checks: ["No provider-specific test is registered for this family yet."],
                TestedAt: DateTimeOffset.UtcNow,
                Status: "unverified");
        }

        return await _tester(connectionId, ct).ConfigureAwait(false);
    }

    public ValueTask<object?> ResolveCapabilityAsync(ProviderCapabilityKind capability, CancellationToken ct = default)
        => ValueTask.FromResult(_resolvers.TryGetValue(capability, out var resolver) ? resolver?.Invoke() : null);

    public void RegisterCapability(
        ProviderCapabilityDescriptor descriptor,
        Func<object?>? resolver,
        Func<string, CancellationToken, Task<ProviderConnectionTestResult>>? tester = null)
    {
        if (!_descriptors.ContainsKey(descriptor.Kind))
            _descriptors[descriptor.Kind] = descriptor;

        if (!_resolvers.ContainsKey(descriptor.Kind))
            _resolvers[descriptor.Kind] = resolver;

        _tester ??= tester;
    }
}

internal sealed class DefaultProviderConnectionHealthSource : IProviderConnectionHealthSource
{
    private readonly UI.ConfigStore _store;

    public DefaultProviderConnectionHealthSource(UI.ConfigStore store)
    {
        _store = store;
    }

    public ValueTask<ProviderConnectionHealthSnapshot> GetHealthAsync(
        string connectionId,
        string providerFamilyId,
        CancellationToken ct = default)
    {
        var metrics = _store.TryLoadProviderMetrics();
        var match = metrics?.Providers.FirstOrDefault(p =>
            string.Equals(p.ProviderId, connectionId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.ProviderId, providerFamilyId, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            return ValueTask.FromResult(new ProviderConnectionHealthSnapshot(
                ConnectionId: connectionId,
                ProviderFamilyId: providerFamilyId,
                IsHealthy: true,
                Status: "unknown",
                Score: 100,
                CheckedAt: DateTimeOffset.UtcNow));
        }

        return ValueTask.FromResult(new ProviderConnectionHealthSnapshot(
            ConnectionId: connectionId,
            ProviderFamilyId: providerFamilyId,
            IsHealthy: match.IsConnected,
            Status: match.IsConnected ? "healthy" : "degraded",
            Score: Math.Clamp(match.DataQualityScore, 0, 100),
            CheckedAt: match.Timestamp));
    }
}

internal sealed class DefaultProviderCertificationRunner : IProviderCertificationRunner
{
    public async Task<ProviderCertificationRunResult> RunAsync(
        string connectionId,
        IProviderFamilyAdapter adapter,
        CancellationToken ct = default)
    {
        var test = await adapter.TestConnectionAsync(connectionId, ct).ConfigureAwait(false);
        return new ProviderCertificationRunResult(
            ConnectionId: connectionId,
            Success: test.Success,
            Status: test.Success ? "Passed" : "Failed",
            Checks: test.Checks,
            RanAt: test.TestedAt);
    }
}
