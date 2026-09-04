using System.Globalization;
using Meridian.Strategies.Interfaces;
using Meridian.Strategies.Live.Strategies;

namespace Meridian.Strategies.Live;

/// <summary>
/// Inputs handed to a live strategy factory: the run's stable strategy id and its
/// retained parameter set (from the originating backtest run).
/// </summary>
public sealed record LiveStrategyCreationContext(
    string StrategyId,
    IReadOnlyDictionary<string, string> Parameters)
{
    /// <summary>Reads an integer parameter with a fallback default.</summary>
    public int GetInt32(string key, int defaultValue) =>
        Parameters.TryGetValue(key, out var raw)
        && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;

    /// <summary>Reads a long parameter with a fallback default.</summary>
    public long GetInt64(string key, long defaultValue) =>
        Parameters.TryGetValue(key, out var raw)
        && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;
}

/// <summary>
/// Resolves the concrete <see cref="ILiveStrategy"/> implementation that should trade a
/// promoted paper/live run. Promotion carries only the strategy id and parameter set, so
/// this catalog is the seam that turns a governance record into runnable strategy code.
/// </summary>
public interface ILiveStrategyCatalog
{
    /// <summary>Identifiers of all registered strategy factories.</summary>
    IReadOnlyCollection<string> StrategyIds { get; }

    /// <summary>
    /// Attempts to create the strategy for a run. Resolution order: exact registered id,
    /// then the run parameter <c>liveStrategyId</c> naming a registered factory. The created
    /// strategy always reports the run's <paramref name="strategyId"/> so lifecycle and audit
    /// records stay aligned with the promoted run.
    /// </summary>
    bool TryCreate(
        string strategyId,
        IReadOnlyDictionary<string, string>? parameters,
        out ILiveStrategy? strategy,
        out string? failureReason);
}

/// <summary>
/// Resolves a strategy for a run that no registered factory id covered. Implementations return
/// <c>false</c> (optionally with a reason) to let the next fallback try.
/// </summary>
public delegate bool LiveStrategyFallbackResolver(
    LiveStrategyCreationContext context,
    out ILiveStrategy? strategy,
    out string? failureReason);

/// <summary>
/// Default in-memory <see cref="ILiveStrategyCatalog"/>. Hosts register additional factories
/// at composition time; <see cref="CreateDefault"/> ships the built-in reference strategies.
/// </summary>
public sealed class LiveStrategyCatalog : ILiveStrategyCatalog
{
    /// <summary>Run parameter key that names the registered factory to use for a run.</summary>
    public const string LiveStrategyIdParameterKey = "liveStrategyId";

    private readonly Dictionary<string, Func<LiveStrategyCreationContext, ILiveStrategy>> _factories =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<(string? SelectorKey, LiveStrategyFallbackResolver Resolver)> _fallbacks = [];
    private readonly HashSet<string> _selectorParameterKeys = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public IReadOnlyCollection<string> StrategyIds => _factories.Keys.ToArray();

    /// <summary>Registers (or replaces) a strategy factory under a stable catalog id.</summary>
    public LiveStrategyCatalog Register(string catalogId, Func<LiveStrategyCreationContext, ILiveStrategy> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogId);
        ArgumentNullException.ThrowIfNull(factory);
        _factories[catalogId.Trim()] = factory;
        return this;
    }

    /// <summary>
    /// Registers a fallback consulted (in registration order) when no factory id matches a run.
    /// This is how user-authored strategies reach live execution without a hand-written live twin.
    /// </summary>
    public LiveStrategyCatalog RegisterFallback(LiveStrategyFallbackResolver fallback) =>
        RegisterFallback(selectorParameterKey: null, fallback);

    /// <summary>
    /// Registers a fallback along with the run parameter whose presence means it owns the run.
    /// </summary>
    /// <remarks>
    /// A declared selector makes the run's choice of implementation explicit, so the catalog can
    /// refuse a run that also names a built-in factory alias instead of silently letting the alias
    /// win. Pass <c>null</c> for a source that claims runs by some other means.
    /// </remarks>
    public LiveStrategyCatalog RegisterFallback(string? selectorParameterKey, LiveStrategyFallbackResolver fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        var selector = string.IsNullOrWhiteSpace(selectorParameterKey) ? null : selectorParameterKey;
        if (selector is not null)
        {
            _selectorParameterKeys.Add(selector);
        }

        _fallbacks.Add((selector, fallback));
        return this;
    }

    /// <summary>Creates a catalog pre-populated with the built-in reference strategies.</summary>
    public static LiveStrategyCatalog CreateDefault() => new LiveStrategyCatalog()
        .Register(BuyAndHoldLiveStrategy.CatalogId, static ctx => new BuyAndHoldLiveStrategy(
            ctx.StrategyId,
            quantityPerSymbol: ctx.GetInt64("quantity", 10)))
        .Register(MovingAverageCrossoverLiveStrategy.CatalogId, static ctx => new MovingAverageCrossoverLiveStrategy(
            ctx.StrategyId,
            fastPeriod: ctx.GetInt32("fastPeriod", 10),
            slowPeriod: ctx.GetInt32("slowPeriod", 30),
            quantity: ctx.GetInt64("quantity", 10)));

    /// <inheritdoc/>
    public bool TryCreate(
        string strategyId,
        IReadOnlyDictionary<string, string>? parameters,
        out ILiveStrategy? strategy,
        out string? failureReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyId);
        var effectiveParameters = parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var runSelector = _selectorParameterKeys.FirstOrDefault(key =>
            effectiveParameters.TryGetValue(key, out var selected) && !string.IsNullOrWhiteSpace(selected));

        var factoryId = strategyId;
        var aliased = false;
        if (!_factories.ContainsKey(factoryId)
            && effectiveParameters.TryGetValue(LiveStrategyIdParameterKey, out var mappedId)
            && !string.IsNullOrWhiteSpace(mappedId))
        {
            factoryId = mappedId.Trim();
            aliased = true;
        }

        // A run that already selected an implementation cannot also resolve to a built-in factory.
        // The factory wins before any fallback is consulted, so the selected source would never
        // run: a designer or plugin run would trade a built-in strategy under its own run id,
        // without the revision, gates, sizing, or risk guards it was approved with. This is checked
        // against the resolved factory id rather than only the alias, because a run whose own id
        // happens to be a built-in id reaches the same factory without naming an alias at all.
        if (runSelector is not null && _factories.ContainsKey(factoryId))
        {
            strategy = null;
            failureReason = aliased
                ? $"Run '{strategyId}' names both the '{runSelector}' source selector and the " +
                  $"'{LiveStrategyIdParameterKey}' alias '{factoryId}'. The alias resolves first, so the selected " +
                  "implementation would never run. Remove one of the two parameters."
                : $"Run '{strategyId}' names the '{runSelector}' source selector, but its own id is the built-in " +
                  "strategy id '" + factoryId + "'. The built-in factory resolves first, so the selected " +
                  "implementation would never run. Rename the run, or remove the selector parameter.";
            return false;
        }

        if (!_factories.TryGetValue(factoryId, out var factory))
        {
            var context = new LiveStrategyCreationContext(strategyId, effectiveParameters);
            var fallbackReasons = new List<string>();
            foreach (var (selectorKey, resolver) in _fallbacks)
            {
                if (resolver(context, out strategy, out var fallbackReason) && strategy is not null)
                {
                    failureReason = null;
                    return true;
                }

                if (string.IsNullOrWhiteSpace(fallbackReason))
                {
                    continue;
                }

                // Whether a refusal ends resolution depends on whether that source owns the run,
                // not on whether it said anything: the resolver contract lets any source decline
                // with a diagnostic and expect the next one to be tried. A source whose selector
                // the run carries is the one that was chosen, so its refusal is the answer --
                // continuing would hand a plugin-backed run whose assembly is missing to whatever
                // source is registered next, executing a different implementation under the same
                // run id and discarding the refusal that explains why.
                if (selectorKey is not null
                    && effectiveParameters.TryGetValue(selectorKey, out var selected)
                    && !string.IsNullOrWhiteSpace(selected))
                {
                    strategy = null;

                    // Named, because this reason reaches the operator as the run's
                    // ActivationDeferred text and a bare refusal does not say which run deferred.
                    failureReason = $"Run '{strategyId}' could not be activated: {fallbackReason}";
                    return false;
                }

                fallbackReasons.Add(fallbackReason);
            }

            strategy = null;
            failureReason =
                $"No live strategy implementation is registered for '{strategyId}'. " +
                $"Register a factory in the live strategy catalog or set the run parameter " +
                $"'{LiveStrategyIdParameterKey}' to one of: {string.Join(", ", StrategyIds.Order(StringComparer.OrdinalIgnoreCase))}." +
                (fallbackReasons.Count > 0 ? $" Fallback sources: {string.Join(" | ", fallbackReasons)}" : string.Empty);
            return false;
        }

        strategy = factory(new LiveStrategyCreationContext(strategyId, effectiveParameters));
        failureReason = null;
        return true;
    }
}
