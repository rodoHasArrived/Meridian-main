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
/// Classifies a data-source candidate relative to the registry snapshot used for discovery.
/// </summary>
public enum DataSourceRegistrationDisposition
{
    /// <summary>The provider family/capability identity was not present and can be added.</summary>
    Added,

    /// <summary>The same provider metadata and implementation type were already registered.</summary>
    Duplicate,

    /// <summary>An overlapping capability in the provider family was claimed by different metadata or an implementation type.</summary>
    Conflict
}

/// <summary>
/// The classification of one discovered data-source candidate.
/// </summary>
public sealed record DataSourceRegistrationOutcome(
    DataSourceMetadata Candidate,
    DataSourceRegistrationDisposition Disposition,
    DataSourceMetadata? Existing = null,
    string? Reason = null);

/// <summary>
/// Transactional discovery result for one assembly. Candidate dispositions describe what was
/// found; <see cref="Committed"/> is authoritative about whether any <see cref="DataSourceRegistrationDisposition.Added"/>
/// candidates were published to the registry.
/// </summary>
public sealed record DataSourceAssemblyRegistrationResult(
    string AssemblyName,
    bool Committed,
    IReadOnlyList<DataSourceRegistrationOutcome> Outcomes)
{
    public bool HasConflicts => Outcomes.Any(static outcome =>
        outcome.Disposition == DataSourceRegistrationDisposition.Conflict);

    public int AddedCount => Committed
        ? Outcomes.Count(static outcome => outcome.Disposition == DataSourceRegistrationDisposition.Added)
        : 0;

    public int DuplicateCount => Outcomes.Count(static outcome =>
        outcome.Disposition == DataSourceRegistrationDisposition.Duplicate);

    public int ConflictCount => Outcomes.Count(static outcome =>
        outcome.Disposition == DataSourceRegistrationDisposition.Conflict);
}

/// <summary>
/// Raised by the compatibility discovery API when an assembly attempts to claim an overlapping
/// capability within a provider family using different metadata or an implementation type.
/// </summary>
public sealed class DataSourceRegistrationConflictException : InvalidOperationException
{
    public DataSourceRegistrationConflictException(DataSourceAssemblyRegistrationResult result)
        : base(BuildMessage(result))
    {
        Result = result;
    }

    public DataSourceAssemblyRegistrationResult Result { get; }

    private static string BuildMessage(DataSourceAssemblyRegistrationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var conflicts = result.Outcomes
            .Where(static outcome => outcome.Disposition == DataSourceRegistrationDisposition.Conflict)
            .Select(outcome =>
                $"'{outcome.Candidate.Id}' [{string.Join("|", outcome.Candidate.CapabilityKeys)}] ({outcome.Candidate.ImplementationType.FullName})")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return $"Data-source discovery for assembly '{result.AssemblyName}' was rejected transactionally because provider identity conflicts were found: {string.Join(", ", conflicts)}.";
    }
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
    /// Discovers data sources from the provided assemblies. Each assembly is committed
    /// transactionally. Exact duplicates and disjoint capabilities in the same provider family are
    /// harmless; an overlapping conflicting capability rejects the entire assembly and raises
    /// <see cref="DataSourceRegistrationConflictException"/>.
    /// </summary>
    public void DiscoverFromAssemblies(params Assembly[] assemblies)
    {
        ValidateAssemblies(assemblies);
        foreach (var assembly in assemblies)
        {
            var result = DiscoverFromAssemblyWithResult(assembly);
            if (result.HasConflicts)
            {
                throw new DataSourceRegistrationConflictException(result);
            }
        }
    }

    /// <summary>
    /// Discovers data sources and returns an explicit result for every assembly. Discovery is
    /// transactional per assembly: if any candidate conflicts, no candidate from that assembly is
    /// published. A provider ID is a family identity: implementations with disjoint recognized
    /// service-contract capability keys may coexist. Exact duplicates require a case-insensitive ID
    /// match and otherwise identical <see cref="DataSourceMetadata"/>, including capability keys and
    /// implementation type. Different implementations whose capability keys overlap conflict.
    /// </summary>
    public IReadOnlyList<DataSourceAssemblyRegistrationResult> DiscoverFromAssembliesWithResults(
        params Assembly[] assemblies)
    {
        ValidateAssemblies(assemblies);
        return assemblies
            .Select(DiscoverFromAssemblyWithResult)
            .ToArray();
    }

    private static void ValidateAssemblies(Assembly[] assemblies)
    {
        if (assemblies is null || assemblies.Length == 0)
        {
            throw new ArgumentException("At least one assembly must be provided.", nameof(assemblies));
        }

        if (assemblies.Any(static assembly => assembly is null))
        {
            throw new ArgumentException("Assemblies cannot contain null entries.", nameof(assemblies));
        }
    }

    /// <summary>
    /// Discovers and transactionally classifies the data sources in one assembly.
    /// </summary>
    public DataSourceAssemblyRegistrationResult DiscoverFromAssemblyWithResult(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var assemblyName = assembly.GetName().Name ?? assembly.FullName ?? "unknown";
        var candidates = GetLoadableTypes(assembly)
            .Where(static type => type.IsDataSource())
            .Select(static type => type.GetDataSourceMetadata())
            .Where(static metadata => metadata is not null)
            .Cast<DataSourceMetadata>()
            .OrderBy(static metadata => metadata.Id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static metadata => metadata.ImplementationType.FullName, StringComparer.Ordinal)
            .ToArray();

        lock (_sync)
        {
            var outcomes = ClassifyCandidates(candidates);
            var hasConflicts = outcomes.Any(static outcome =>
                outcome.Disposition == DataSourceRegistrationDisposition.Conflict);
            if (!hasConflicts)
            {
                _sources.AddRange(outcomes
                    .Where(static outcome =>
                        outcome.Disposition == DataSourceRegistrationDisposition.Added)
                    .Select(static outcome => outcome.Candidate));
            }

            return new DataSourceAssemblyRegistrationResult(
                assemblyName,
                Committed: !hasConflicts,
                Array.AsReadOnly(outcomes));
        }
    }

    private DataSourceRegistrationOutcome[] ClassifyCandidates(
        IReadOnlyList<DataSourceMetadata> candidates)
    {
        var outcomes = new List<DataSourceRegistrationOutcome>(candidates.Count);
        foreach (var group in candidates.GroupBy(
                     static metadata => metadata.Id,
                     StringComparer.OrdinalIgnoreCase))
        {
            var groupedCandidates = group.ToArray();
            foreach (var candidate in groupedCandidates)
            {
                var conflictingCandidate = groupedCandidates.FirstOrDefault(other =>
                    !ReferenceEquals(other, candidate)
                    && !IsExactDuplicate(candidate, other)
                    && CapabilitiesOverlap(candidate, other));
                if (conflictingCandidate is not null)
                {
                    outcomes.Add(new DataSourceRegistrationOutcome(
                        candidate,
                        DataSourceRegistrationDisposition.Conflict,
                        conflictingCandidate,
                        "The assembly contains different implementations or metadata definitions with an overlapping provider capability."));
                    continue;
                }

                var family = _sources.Where(source =>
                    source.Id.Equals(candidate.Id, StringComparison.OrdinalIgnoreCase));
                var exact = family.FirstOrDefault(existing =>
                    IsExactDuplicate(existing, candidate));
                var conflict = exact is null
                    ? family.FirstOrDefault(existing => CapabilitiesOverlap(existing, candidate))
                    : null;
                var disposition = exact is not null
                    ? DataSourceRegistrationDisposition.Duplicate
                    : conflict is not null
                        ? DataSourceRegistrationDisposition.Conflict
                        : DataSourceRegistrationDisposition.Added;
                outcomes.Add(new DataSourceRegistrationOutcome(
                    candidate,
                    disposition,
                    exact ?? conflict,
                    disposition == DataSourceRegistrationDisposition.Conflict
                        ? "An overlapping provider capability is already registered by different metadata or an implementation type."
                        : null));
            }
        }

        return outcomes.ToArray();
    }

    private static bool IsExactDuplicate(
        DataSourceMetadata left,
        DataSourceMetadata right) =>
        left.Id.Equals(right.Id, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.DisplayName, right.DisplayName, StringComparison.Ordinal)
        && string.Equals(left.Description, right.Description, StringComparison.Ordinal)
        && left.Type == right.Type
        && left.Category == right.Category
        && left.Priority == right.Priority
        && left.EnabledByDefault == right.EnabledByDefault
        && string.Equals(left.ConfigSection, right.ConfigSection, StringComparison.Ordinal)
        && left.ImplementationType == right.ImplementationType
        && left.CapabilityKeys.SequenceEqual(right.CapabilityKeys, StringComparer.Ordinal);

    private static bool CapabilitiesOverlap(
        DataSourceMetadata left,
        DataSourceMetadata right) =>
        left.CapabilityKeys.Any(capability =>
            right.CapabilityKeys.Contains(capability, StringComparer.Ordinal));

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
