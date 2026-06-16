namespace Meridian.Modularity;

/// <summary>
/// Collects <see cref="IMeridianModule"/> instances and composes them in dependency order.
/// Replaces the several hand-maintained module arrays and ad hoc ordering comments scattered
/// across the composition surfaces with one deterministic loader.
/// </summary>
/// <remarks>
/// Ordering is a stable topological sort: a module's <see cref="IMeridianModule.DependsOn"/>
/// targets are always registered before it, and modules with no ordering relationship keep their
/// insertion order. This preserves the behaviour of the existing ordered arrays while making the
/// ordering rules explicit and testable.
/// </remarks>
public sealed class ModuleLoader
{
    private readonly List<IMeridianModule> _modules = new();

    /// <summary>Adds a single module. Returns this loader for chaining.</summary>
    public ModuleLoader Add(IMeridianModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        _modules.Add(module);
        return this;
    }

    /// <summary>Adds a range of modules in order. Returns this loader for chaining.</summary>
    public ModuleLoader AddRange(IEnumerable<IMeridianModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        foreach (var module in modules)
            Add(module);
        return this;
    }

    /// <summary>
    /// Returns the modules in registration order (dependencies first, otherwise insertion order).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown on a duplicate module id, an unknown dependency, or a dependency cycle.
    /// </exception>
    public IReadOnlyList<IMeridianModule> Order()
    {
        var byId = new Dictionary<string, IMeridianModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in _modules)
        {
            if (string.IsNullOrWhiteSpace(module.ModuleId))
                throw new InvalidOperationException($"Module of type '{module.GetType().Name}' has a null or empty module id.");
            if (!byId.TryAdd(module.ModuleId, module))
                throw new InvalidOperationException($"Duplicate module id '{module.ModuleId}'.");
        }

        var ordered = new List<IMeridianModule>(_modules.Count);
        // 0 = unvisited, 1 = visiting (on the current path), 2 = done.
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void Visit(IMeridianModule module)
        {
            if (state.TryGetValue(module.ModuleId, out var visitState))
            {
                if (visitState == 1)
                    throw new InvalidOperationException($"Module dependency cycle detected involving '{module.ModuleId}'.");
                if (visitState == 2)
                    return;
            }

            state[module.ModuleId] = 1;
            foreach (var dependencyId in module.DependsOn)
            {
                if (string.IsNullOrWhiteSpace(dependencyId))
                    throw new InvalidOperationException($"Module '{module.ModuleId}' declares a null or empty dependency id.");
                if (!byId.TryGetValue(dependencyId, out var dependency))
                    throw new InvalidOperationException($"Module '{module.ModuleId}' depends on unknown module '{dependencyId}'.");
                Visit(dependency);
            }

            state[module.ModuleId] = 2;
            ordered.Add(module);
        }

        // Iterate the original list so independent modules keep their insertion order.
        foreach (var module in _modules)
            Visit(module);

        return ordered;
    }

    /// <summary>
    /// Registers every module into the supplied context in dependency order.
    /// </summary>
    public void RegisterAll(ModuleRegistrationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (var module in Order())
            module.Register(context);
    }

    /// <summary>
    /// Validates every module in dependency order and returns the results in that order.
    /// </summary>
    public async ValueTask<IReadOnlyList<ModuleValidationResult>> ValidateAllAsync(CancellationToken cancellationToken = default)
    {
        var ordered = Order();
        var results = new List<ModuleValidationResult>(ordered.Count);
        foreach (var module in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await module.ValidateAsync(cancellationToken).ConfigureAwait(false));
        }

        return results;
    }
}
