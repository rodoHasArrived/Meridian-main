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

    private readonly List<LiveStrategyFallbackResolver> _fallbacks = [];

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
    public LiveStrategyCatalog RegisterFallback(LiveStrategyFallbackResolver fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        _fallbacks.Add(fallback);
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

        var factoryId = strategyId;
        if (!_factories.ContainsKey(factoryId)
            && effectiveParameters.TryGetValue(LiveStrategyIdParameterKey, out var mappedId)
            && !string.IsNullOrWhiteSpace(mappedId))
        {
            factoryId = mappedId.Trim();
        }

        if (!_factories.TryGetValue(factoryId, out var factory))
        {
            var context = new LiveStrategyCreationContext(strategyId, effectiveParameters);
            foreach (var fallback in _fallbacks)
            {
                if (fallback(context, out strategy, out var fallbackReason) && strategy is not null)
                {
                    failureReason = null;
                    return true;
                }

                // A source states a reason only when it recognised the run and could not build it;
                // one that does not recognise the run declines silently so the next source can try.
                // Continuing past a stated reason would hand an explicitly plugin-backed run whose
                // assembly is missing to whatever source is registered next, executing a different
                // implementation under the same run id and discarding the refusal that explains
                // why. A run that has been claimed stops here, refused.
                if (!string.IsNullOrWhiteSpace(fallbackReason))
                {
                    strategy = null;

                    // Named, because this reason reaches the operator as the run's
                    // ActivationDeferred text and a bare refusal does not say which run deferred.
                    failureReason = $"Run '{strategyId}' could not be activated: {fallbackReason}";
                    return false;
                }
            }

            strategy = null;
            failureReason =
                $"No live strategy implementation is registered for '{strategyId}'. " +
                $"Register a factory in the live strategy catalog or set the run parameter " +
                $"'{LiveStrategyIdParameterKey}' to one of: {string.Join(", ", StrategyIds.Order(StringComparer.OrdinalIgnoreCase))}.";
            return false;
        }

        strategy = factory(new LiveStrategyCreationContext(strategyId, effectiveParameters));
        failureReason = null;
        return true;
    }
}
