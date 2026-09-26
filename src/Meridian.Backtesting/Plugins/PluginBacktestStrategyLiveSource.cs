using System.Collections.Concurrent;
using System.Globalization;
using Meridian.Backtesting.Sdk;
using Meridian.Strategies.Live;
using Microsoft.Extensions.Logging;

namespace Meridian.Backtesting.Plugins;

/// <summary>
/// Resolves user-authored <see cref="IBacktestStrategy"/> implementations from plugin assemblies
/// under a host-configured directory, so promoted runs can reach paper/live execution through
/// <see cref="BacktestStrategyLiveAdapter"/> without a hand-written live strategy.
/// </summary>
/// <remarks>
/// Run parameters drive resolution:
/// <list type="bullet">
///   <item><c>pluginAssembly</c> — bare DLL file name inside the configured plugin directory
///     (path segments are rejected so runs cannot load arbitrary assemblies).</item>
///   <item><c>pluginType</c> — optional strategy type (full or simple name); required when the
///     assembly contains more than one strategy.</item>
/// </list>
/// Remaining run parameters are bound to matching <see cref="StrategyParameterAttribute"/>
/// properties on the strategy instance.
/// </remarks>
public sealed class PluginBacktestStrategyLiveSource : IBacktestStrategyLiveSource
{
    /// <summary>Run parameter naming the plugin assembly file inside the plugin directory.</summary>
    public const string PluginAssemblyParameterKey = "pluginAssembly";

    /// <summary>Run parameter naming the strategy type inside the plugin assembly.</summary>
    public const string PluginTypeParameterKey = "pluginType";

    private readonly string? _pluginDirectory;
    private readonly ILogger<PluginBacktestStrategyLiveSource>? _logger;
    private readonly StrategyPluginLoader _loader = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<Type>> _typeCache =
        new(StringComparer.OrdinalIgnoreCase);

    public PluginBacktestStrategyLiveSource(
        string? pluginDirectory,
        ILogger<PluginBacktestStrategyLiveSource>? logger = null)
    {
        _pluginDirectory = string.IsNullOrWhiteSpace(pluginDirectory) ? null : pluginDirectory.Trim();
        _logger = logger;
    }

    /// <inheritdoc/>
    /// <inheritdoc/>
    public string? SelectorParameterKey => PluginAssemblyParameterKey;

    public bool TryCreate(
        LiveStrategyCreationContext context,
        out IBacktestStrategy? strategy,
        out string? failureReason)
    {
        strategy = null;
        failureReason = null;

        if (!context.Parameters.TryGetValue(PluginAssemblyParameterKey, out var assemblyName)
            || string.IsNullOrWhiteSpace(assemblyName))
        {
            // Not a plugin-backed run; stay silent so other fallbacks can try.
            return false;
        }

        if (_pluginDirectory is null)
        {
            failureReason =
                $"Run '{context.StrategyId}' names plugin assembly '{assemblyName}' but no strategy " +
                "plugin directory is configured (Execution:LiveTradingEngine:StrategyPluginDirectory).";
            return false;
        }

        assemblyName = assemblyName.Trim();
        if (assemblyName.Contains("..", StringComparison.Ordinal)
            || assemblyName.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0
            || assemblyName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            failureReason =
                $"Plugin assembly '{assemblyName}' is invalid: expected a bare .dll file name inside " +
                "the configured plugin directory.";
            return false;
        }

        var assemblyPath = Path.Combine(_pluginDirectory, assemblyName);
        if (!File.Exists(assemblyPath))
        {
            failureReason = $"Plugin assembly '{assemblyName}' was not found in the strategy plugin directory.";
            return false;
        }

        IReadOnlyList<Type> strategyTypes;
        try
        {
            strategyTypes = _typeCache.GetOrAdd(assemblyPath, path => _loader.LoadStrategyTypes(path));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to load strategy plugin assembly {AssemblyPath}", assemblyPath);
            failureReason = $"Plugin assembly '{assemblyName}' could not be loaded: {ex.Message}";
            return false;
        }

        var strategyType = ResolveStrategyType(context, strategyTypes, assemblyName, out failureReason);
        if (strategyType is null)
        {
            return false;
        }

        try
        {
            var instance = _loader.Instantiate(strategyType);
            BindParameters(instance, strategyType, context);
            strategy = instance;
            failureReason = null;
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(
                ex,
                "Failed to instantiate plugin strategy {StrategyType} for run {StrategyId}",
                strategyType.FullName,
                context.StrategyId);
            failureReason = $"Plugin strategy '{strategyType.FullName}' could not be instantiated: {ex.Message}";
            strategy = null;
            return false;
        }
    }

    private static Type? ResolveStrategyType(
        LiveStrategyCreationContext context,
        IReadOnlyList<Type> strategyTypes,
        string assemblyName,
        out string? failureReason)
    {
        if (strategyTypes.Count == 0)
        {
            failureReason = $"Plugin assembly '{assemblyName}' contains no IBacktestStrategy implementations.";
            return null;
        }

        if (context.Parameters.TryGetValue(PluginTypeParameterKey, out var typeName)
            && !string.IsNullOrWhiteSpace(typeName))
        {
            typeName = typeName.Trim();
            var match = strategyTypes.FirstOrDefault(type =>
                string.Equals(type.FullName, typeName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type.Name, typeName, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                failureReason =
                    $"Plugin assembly '{assemblyName}' has no strategy type '{typeName}'. Available: " +
                    string.Join(", ", strategyTypes.Select(static type => type.FullName));
                return null;
            }

            failureReason = null;
            return match;
        }

        if (strategyTypes.Count > 1)
        {
            failureReason =
                $"Plugin assembly '{assemblyName}' contains {strategyTypes.Count} strategy types; set the " +
                $"run parameter '{PluginTypeParameterKey}' to one of: " +
                string.Join(", ", strategyTypes.Select(static type => type.FullName));
            return null;
        }

        failureReason = null;
        return strategyTypes[0];
    }

    private void BindParameters(
        IBacktestStrategy instance,
        Type strategyType,
        LiveStrategyCreationContext context)
    {
        foreach (var parameter in _loader.GetParameters(strategyType))
        {
            if (!context.Parameters.TryGetValue(parameter.PropertyName, out var raw)
                || string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var property = strategyType.GetProperty(parameter.PropertyName);
            if (property is null || !property.CanWrite)
            {
                continue;
            }

            // Fail closed: silently running a live strategy with a default value in place of an
            // unconvertible operator-supplied parameter is worse than refusing to launch.
            try
            {
                var targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                var converted = targetType.IsEnum
                    ? Enum.Parse(targetType, raw, ignoreCase: true)
                    : Convert.ChangeType(raw, targetType, CultureInfo.InvariantCulture);
                property.SetValue(instance, converted);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Run parameter '{parameter.PropertyName}'='{raw}' could not be converted to " +
                    $"{property.PropertyType.Name} for plugin strategy '{strategyType.FullName}'.",
                    ex);
            }
        }
    }
}
