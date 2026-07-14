using System.Reflection;
using Meridian.Infrastructure.Adapters.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Infrastructure.DataSources;

/// <summary>
/// A discovery or registration failure captured during data source and module scanning.
/// </summary>
/// <param name="Stage">Where the failure occurred: "type-load", "activate", "configure", or "register".</param>
/// <param name="Subject">The type or assembly the failure relates to.</param>
/// <param name="ModuleId">Module ID when known, otherwise null.</param>
/// <param name="ErrorType">Exception type name.</param>
/// <param name="ErrorMessage">Exception message.</param>
public sealed record DataSourceDiscoveryFailure(
    string Stage,
    string Subject,
    string? ModuleId,
    string ErrorType,
    string ErrorMessage);

/// <summary>
/// Immutable cumulative snapshot of provider discovery and module registration activity.
/// </summary>
public sealed record ProviderRegistrationReport(
    DateTimeOffset GeneratedAt,
    int DiscoveredSourceCount,
    int ModuleCandidateCount,
    int ModuleActivationAttemptCount,
    int ModuleRegistrationAttemptCount,
    int RegisteredModuleCount,
    int SkippedModuleCount,
    IReadOnlyList<DataSourceDiscoveryFailure> Failures)
{
    public int FailedModuleCount => Failures.Count(failure =>
        failure.Stage is "activate" or "configure" or "register");

    public bool IsHealthy => Failures.Count == 0;
}

/// <summary>
/// Registry for discovering and registering data source providers.
/// </summary>
public sealed class DataSourceRegistry
{
    private readonly object _sync = new();
    private readonly List<DataSourceMetadata> _sources = new();
    private readonly List<DataSourceDiscoveryFailure> _failures = new();
    private readonly Dictionary<string, ProviderModuleContext> _moduleContexts
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger _log;
    private readonly TimeProvider _timeProvider;
    private int _moduleCandidateCount;
    private int _moduleActivationAttemptCount;
    private int _moduleRegistrationAttemptCount;
    private int _registeredModuleCount;
    private int _skippedModuleCount;

    public DataSourceRegistry(ILogger? log = null, TimeProvider? timeProvider = null)
    {
        _log = log ?? NullLogger.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Gets the discovered data source metadata entries.
    /// </summary>
    public IReadOnlyList<DataSourceMetadata> Sources
    {
        get
        {
            lock (_sync)
                return Array.AsReadOnly(_sources.ToArray());
        }
    }

    /// <summary>
    /// Failures captured while scanning, activating, or registering modules.
    /// Empty when everything registered cleanly. Scanning continues past
    /// individual failures so one broken module cannot block the rest.
    /// </summary>
    public IReadOnlyList<DataSourceDiscoveryFailure> Failures => GetRegistrationReport().Failures;

    /// <summary>
    /// Returns an immutable, point-in-time registration report. The counters are cumulative
    /// for the lifetime of this registry so repeated scans remain observable.
    /// </summary>
    public ProviderRegistrationReport GetRegistrationReport()
    {
        lock (_sync)
        {
            return new ProviderRegistrationReport(
                _timeProvider.GetUtcNow(),
                _sources.Count,
                _moduleCandidateCount,
                _moduleActivationAttemptCount,
                _moduleRegistrationAttemptCount,
                _registeredModuleCount,
                _skippedModuleCount,
                Array.AsReadOnly(_failures.ToArray()));
        }
    }

    /// <summary>
    /// Discover data sources from the provided assemblies.
    /// </summary>
    public void DiscoverFromAssemblies(params Assembly[] assemblies)
    {
        if (assemblies is null || assemblies.Length == 0)
        {
            throw new ArgumentException("At least one assembly must be provided.", nameof(assemblies));
        }

        foreach (var assembly in assemblies)
        {
            var types = GetLoadableTypes(assembly);
            foreach (var type in types)
            {
                if (!type.IsDataSource())
                {
                    continue;
                }

                var metadata = type.GetDataSourceMetadata();
                if (metadata is not null)
                {
                    lock (_sync)
                    {
                        if (_sources.All(s => !s.Id.Equals(metadata.Id, StringComparison.OrdinalIgnoreCase)))
                            _sources.Add(metadata);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Registers discovered data sources into the service collection.
    /// </summary>
    public void RegisterServices(IServiceCollection services, ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var source in Sources)
        {
            services.Add(new ServiceDescriptor(source.ImplementationType, source.ImplementationType, lifetime));
            // Only register as IDataSource if the type actually implements it
            if (typeof(IDataSource).IsAssignableFrom(source.ImplementationType))
            {
                services.Add(new ServiceDescriptor(typeof(IDataSource),
                    sp => (IDataSource)sp.GetRequiredService(source.ImplementationType),
                    lifetime));
            }

            RegisterProviderInterface(
                services,
                source.ImplementationType,
                "Meridian.Infrastructure.Adapters.Core.ICorporateActionProvider",
                lifetime);
        }
    }

    private static void RegisterProviderInterface(
        IServiceCollection services,
        Type implementationType,
        string serviceTypeFullName,
        ServiceLifetime lifetime)
    {
        var serviceType = implementationType
            .GetInterfaces()
            .FirstOrDefault(type => string.Equals(type.FullName, serviceTypeFullName, StringComparison.Ordinal));
        if (serviceType is null)
            return;

        services.Add(new ServiceDescriptor(
            serviceType,
            sp => sp.GetRequiredService(implementationType),
            lifetime));
    }

    /// <summary>
    /// Pre-configures a module with a <see cref="ProviderModuleContext"/> that will be
    /// injected via <see cref="IProviderModule.Configure"/> before
    /// <see cref="IProviderModule.Register"/> is called.
    /// </summary>
    /// <param name="moduleId">Module ID matching <see cref="IProviderModule.ModuleId"/> (case-insensitive).</param>
    /// <param name="context">The resolved context to inject.</param>
    public DataSourceRegistry ConfigureModule(string moduleId, ProviderModuleContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);
        ArgumentNullException.ThrowIfNull(context);
        lock (_sync)
            _moduleContexts[moduleId] = context;
        return this;
    }

    /// <summary>
    /// Pre-configures multiple modules at once from a dictionary keyed by module ID.
    /// </summary>
    public DataSourceRegistry ConfigureModules(IReadOnlyDictionary<string, ProviderModuleContext> contexts)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        lock (_sync)
        {
            foreach (var (id, ctx) in contexts)
                _moduleContexts[id] = ctx;
        }
        return this;
    }

    /// <summary>
    /// Discovers provider modules and executes their registrations.
    /// When a matching <see cref="ProviderModuleContext"/> exists (registered via
    /// <see cref="ConfigureModule"/>), it is injected before <see cref="IProviderModule.Register"/>.
    /// Modules with <see cref="IProviderModule.RequiresExternalConfig"/> set to true are
    /// skipped when no context was registered for their ID.
    /// </summary>
    public void RegisterModules(IServiceCollection services, params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);

        foreach (var assembly in assemblies)
        {
            var types = GetLoadableTypes(assembly);
            foreach (var type in types)
            {
                if (type.IsAbstract || type.IsInterface)
                    continue;

                if (!typeof(IProviderModule).IsAssignableFrom(type))
                    continue;

                Increment(ref _moduleCandidateCount);

                if (type.GetConstructor(Type.EmptyTypes) is null)
                {
                    Increment(ref _skippedModuleCount);
                    continue;
                }

                IProviderModule module;
                try
                {
                    Increment(ref _moduleActivationAttemptCount);
                    if (Activator.CreateInstance(type) is not IProviderModule m)
                    {
                        Increment(ref _skippedModuleCount);
                        continue;
                    }
                    module = m;
                }
                catch (Exception ex)
                {
                    var activationError = ex is TargetInvocationException { InnerException: { } inner }
                        ? inner
                        : ex;
                    RecordFailure("activate", type.FullName ?? type.Name, moduleId: null, activationError);
                    continue;
                }

                var moduleId = module.ModuleId;

                ProviderModuleContext? context;
                lock (_sync)
                    _moduleContexts.TryGetValue(moduleId, out context);

                if (context is not null)
                {
                    if (!context.Enabled)
                    {
                        Increment(ref _skippedModuleCount);
                        continue;
                    }

                    try
                    {
                        module.Configure(context);
                    }
                    catch (Exception ex)
                    {
                        RecordFailure("configure", type.FullName ?? type.Name, moduleId, ex);
                        continue;
                    }
                }
                else if (module.RequiresExternalConfig)
                {
                    Increment(ref _skippedModuleCount);
                    continue;
                }

                try
                {
                    Increment(ref _moduleRegistrationAttemptCount);
                    module.Register(services, this);
                    Increment(ref _registeredModuleCount);
                }
                catch (Exception ex)
                {
                    RecordFailure("register", type.FullName ?? type.Name, moduleId, ex);
                    continue;
                }
            }
        }
    }

    private IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            var assemblyName = assembly.GetName().Name ?? assembly.FullName ?? "unknown";
            foreach (var loaderError in ex.LoaderExceptions)
            {
                if (loaderError is not null)
                    RecordFailure("type-load", assemblyName, moduleId: null, loaderError);
            }

            return ex.Types.Where(t => t is not null)!;
        }
    }

    private void RecordFailure(string stage, string subject, string? moduleId, Exception error)
    {
        lock (_sync)
        {
            _failures.Add(new DataSourceDiscoveryFailure(
                stage, subject, moduleId, error.GetType().Name, error.Message));
        }

        _log.LogError(
            error,
            "Data source {Stage} failure for {Subject} (module {ModuleId})",
            stage, subject, moduleId ?? "n/a");
    }

    private void Increment(ref int counter)
    {
        lock (_sync)
            counter++;
    }
}
