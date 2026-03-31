using Meridian.Infrastructure.Contracts;

namespace Meridian.Infrastructure.Adapters.Core;

/// <summary>
/// Fluent builder that produces a fully functional <see cref="IHistoricalDataProvider"/>
/// from delegate functions, without requiring inheritance from
/// <see cref="BaseHistoricalDataProvider"/>.
/// </summary>
/// <remarks>
/// Use this builder for new provider implementations where the full HTTP/rate-limit
/// infrastructure of <see cref="BaseHistoricalDataProvider"/> is not needed, or when
/// wrapping a third-party SDK that manages its own HTTP layer.
///
/// Existing providers built on <see cref="BaseHistoricalDataProvider"/> remain unchanged
/// until the module migration is complete; the builder is the preferred path for any
/// provider added from this point onward.
///
/// Minimal example:
/// <code>
/// IHistoricalDataProvider provider = ProviderBehaviorBuilder.Create()
///     .WithName("my-provider")
///     .WithDisplayName("My Provider")
///     .WithCapabilities(HistoricalDataCapabilities.BarsOnly)
///     .WithDailyBars(async (symbol, from, to, ct) =>
///     {
///         var bars = await _sdk.FetchBarsAsync(symbol, from, to, ct);
///         return bars.Select(MapBar).ToList();
///     })
///     .Build();
/// </code>
/// </remarks>
[ImplementsAdr("ADR-001", "Delegate-based IHistoricalDataProvider factory alternative to base-class inheritance")]
[ImplementsAdr("ADR-004", "All delegate signatures carry CancellationToken")]
public sealed class ProviderBehaviorBuilder
{
    private string _name = "custom";
    private string _displayName = "Custom Provider";
    private string _description = "";
    private int _priority = 100;
    private HistoricalDataCapabilities _capabilities = HistoricalDataCapabilities.None;
    private TimeSpan _rateLimitDelay = TimeSpan.Zero;
    private int _maxRequestsPerWindow = int.MaxValue;
    private TimeSpan _rateLimitWindow = TimeSpan.FromHours(1);

    private Func<string, DateOnly?, DateOnly?, CancellationToken,
        Task<IReadOnlyList<HistoricalBar>>>? _dailyBarsFunc;

    private Func<string, DateOnly?, DateOnly?, CancellationToken,
        Task<IReadOnlyList<AdjustedHistoricalBar>>>? _adjustedDailyBarsFunc;

    private Func<CancellationToken, Task<bool>>? _availabilityFunc;

    // -----------------------------------------------------------------------
    // Factory
    // -----------------------------------------------------------------------

    /// <summary>
    /// Creates a new <see cref="ProviderBehaviorBuilder"/> with default settings.
    /// </summary>
    public static ProviderBehaviorBuilder Create() => new();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    /// <summary>Sets the unique provider identifier (e.g., "my-provider").</summary>
    public ProviderBehaviorBuilder WithName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _name = name;
        return this;
    }

    /// <summary>Sets the human-readable display name (e.g., "My Data Provider").</summary>
    public ProviderBehaviorBuilder WithDisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        _displayName = displayName;
        return this;
    }

    /// <summary>Sets the description of the provider's capabilities.</summary>
    public ProviderBehaviorBuilder WithDescription(string description)
    {
        _description = description ?? string.Empty;
        return this;
    }

    // -----------------------------------------------------------------------
    // Routing
    // -----------------------------------------------------------------------

    /// <summary>Sets the routing priority (lower = higher priority, tried first).</summary>
    public ProviderBehaviorBuilder WithPriority(int priority)
    {
        _priority = priority;
        return this;
    }

    // -----------------------------------------------------------------------
    // Capabilities
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sets the consolidated capability flags that determine which data types this
    /// provider supports.
    /// </summary>
    public ProviderBehaviorBuilder WithCapabilities(HistoricalDataCapabilities capabilities)
    {
        _capabilities = capabilities;
        return this;
    }

    // -----------------------------------------------------------------------
    // Rate limiting
    // -----------------------------------------------------------------------

    /// <summary>
    /// Configures the provider's rate limit parameters.
    /// </summary>
    /// <param name="maxRequestsPerWindow">Maximum number of requests per <paramref name="window"/>.</param>
    /// <param name="window">Length of the rate-limit time window.</param>
    /// <param name="minDelayBetweenRequests">
    /// Optional minimum delay inserted before each request.
    /// Defaults to <see cref="TimeSpan.Zero"/> (no forced delay).
    /// </param>
    public ProviderBehaviorBuilder WithRateLimit(
        int maxRequestsPerWindow,
        TimeSpan window,
        TimeSpan minDelayBetweenRequests = default)
    {
        if (maxRequestsPerWindow <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRequestsPerWindow), "Must be > 0.");

        _maxRequestsPerWindow = maxRequestsPerWindow;
        _rateLimitWindow = window;
        _rateLimitDelay = minDelayBetweenRequests;
        return this;
    }

    // -----------------------------------------------------------------------
    // Behaviour delegates
    // -----------------------------------------------------------------------

    /// <summary>
    /// Provides the implementation for
    /// <see cref="IHistoricalDataProvider.GetDailyBarsAsync"/>. Required before
    /// calling <see cref="Build"/>.
    /// </summary>
    public ProviderBehaviorBuilder WithDailyBars(
        Func<string, DateOnly?, DateOnly?, CancellationToken, Task<IReadOnlyList<HistoricalBar>>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _dailyBarsFunc = handler;
        return this;
    }

    /// <summary>
    /// Provides the implementation for
    /// <see cref="IHistoricalDataProvider.GetAdjustedDailyBarsAsync"/>.
    /// When omitted the default implementation wraps the daily-bars delegate,
    /// mapping each <see cref="HistoricalBar"/> to an unadjusted
    /// <see cref="AdjustedHistoricalBar"/>.
    /// </summary>
    public ProviderBehaviorBuilder WithAdjustedDailyBars(
        Func<string, DateOnly?, DateOnly?, CancellationToken, Task<IReadOnlyList<AdjustedHistoricalBar>>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _adjustedDailyBarsFunc = handler;
        return this;
    }

    /// <summary>
    /// Provides the implementation for
    /// <see cref="IHistoricalDataProvider.IsAvailableAsync"/>.
    /// When omitted the built provider always reports itself as available.
    /// </summary>
    public ProviderBehaviorBuilder WithAvailabilityCheck(
        Func<CancellationToken, Task<bool>> check)
    {
        ArgumentNullException.ThrowIfNull(check);
        _availabilityFunc = check;
        return this;
    }

    // -----------------------------------------------------------------------
    // Build
    // -----------------------------------------------------------------------

    /// <summary>
    /// Constructs and returns the configured <see cref="IHistoricalDataProvider"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="WithDailyBars"/> has not been called.
    /// </exception>
    public IHistoricalDataProvider Build()
    {
        if (_dailyBarsFunc is null)
            throw new InvalidOperationException(
                "WithDailyBars must be called before Build. " +
                "A GetDailyBarsAsync implementation is required.");

        return new BuiltHistoricalDataProvider(
            _name,
            _displayName,
            _description,
            _priority,
            _capabilities,
            _rateLimitDelay,
            _maxRequestsPerWindow,
            _rateLimitWindow,
            _dailyBarsFunc,
            _adjustedDailyBarsFunc,
            _availabilityFunc);
    }
}

// -----------------------------------------------------------------------
// Built provider implementation (internal — callers depend on IHistoricalDataProvider)
// -----------------------------------------------------------------------

/// <summary>
/// Concrete <see cref="IHistoricalDataProvider"/> produced by <see cref="ProviderBehaviorBuilder"/>.
/// All behaviour is supplied as delegates; no HTTP infrastructure is included.
/// </summary>
internal sealed class BuiltHistoricalDataProvider : IHistoricalDataProvider
{
    private readonly Func<string, DateOnly?, DateOnly?, CancellationToken,
        Task<IReadOnlyList<HistoricalBar>>> _dailyBarsFunc;

    private readonly Func<string, DateOnly?, DateOnly?, CancellationToken,
        Task<IReadOnlyList<AdjustedHistoricalBar>>>? _adjustedDailyBarsFunc;

    private readonly Func<CancellationToken, Task<bool>>? _availabilityFunc;

    // IHistoricalDataProvider identity
    public string Name { get; }
    public string DisplayName { get; }
    public string Description { get; }

    // IHistoricalDataProvider routing
    public int Priority { get; }

    // IHistoricalDataProvider capabilities
    public HistoricalDataCapabilities Capabilities { get; }

    // IHistoricalDataProvider rate-limit metadata
    public TimeSpan RateLimitDelay { get; }
    public int MaxRequestsPerWindow { get; }
    public TimeSpan RateLimitWindow { get; }

    // IProviderMetadata bridge — delegate to IHistoricalDataProvider defaults
    string IProviderMetadata.ProviderId => Name;
    string IProviderMetadata.ProviderDisplayName => DisplayName;
    string IProviderMetadata.ProviderDescription => Description;
    int IProviderMetadata.ProviderPriority => Priority;
    ProviderCapabilities IProviderMetadata.ProviderCapabilities =>
        ProviderCapabilities.FromHistoricalCapabilities(
            Capabilities,
            MaxRequestsPerWindow == int.MaxValue ? null : MaxRequestsPerWindow,
            RateLimitWindow,
            RateLimitDelay == TimeSpan.Zero ? null : RateLimitDelay);

    internal BuiltHistoricalDataProvider(
        string name,
        string displayName,
        string description,
        int priority,
        HistoricalDataCapabilities capabilities,
        TimeSpan rateLimitDelay,
        int maxRequestsPerWindow,
        TimeSpan rateLimitWindow,
        Func<string, DateOnly?, DateOnly?, CancellationToken, Task<IReadOnlyList<HistoricalBar>>> dailyBarsFunc,
        Func<string, DateOnly?, DateOnly?, CancellationToken, Task<IReadOnlyList<AdjustedHistoricalBar>>>? adjustedDailyBarsFunc,
        Func<CancellationToken, Task<bool>>? availabilityFunc)
    {
        Name = name;
        DisplayName = displayName;
        Description = description;
        Priority = priority;
        Capabilities = capabilities;
        RateLimitDelay = rateLimitDelay;
        MaxRequestsPerWindow = maxRequestsPerWindow;
        RateLimitWindow = rateLimitWindow;
        _dailyBarsFunc = dailyBarsFunc;
        _adjustedDailyBarsFunc = adjustedDailyBarsFunc;
        _availabilityFunc = availabilityFunc;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
        => _dailyBarsFunc(symbol, from, to, ct);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AdjustedHistoricalBar>> GetAdjustedDailyBarsAsync(
        string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        if (_adjustedDailyBarsFunc is not null)
            return await _adjustedDailyBarsFunc(symbol, from, to, ct).ConfigureAwait(false);

        // Default: project unadjusted daily bars to AdjustedHistoricalBar without ratio data.
        var bars = await _dailyBarsFunc(symbol, from, to, ct).ConfigureAwait(false);
        return bars
            .Select(b => new AdjustedHistoricalBar(
                b.Symbol, b.SessionDate, b.Open, b.High, b.Low, b.Close, b.Volume,
                b.Source, b.SequenceNumber))
            .ToList();
    }

    /// <inheritdoc/>
    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
        => _availabilityFunc is not null
            ? _availabilityFunc(ct)
            : Task.FromResult(true);

    void IDisposable.Dispose() { /* delegates own their resources */ }
}
