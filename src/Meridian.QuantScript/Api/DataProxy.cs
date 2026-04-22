using Meridian.Contracts.SecurityMaster;

namespace Meridian.QuantScript.Api;

/// <summary>
/// Async-first façade over <see cref="IQuantDataContext"/> for ergonomic script use.
/// Synchronous methods are retained as compatibility wrappers for existing scripts.
/// <para>
/// <b>Important:</b> Sync members must only be called from a background thread (never the UI thread)
/// to avoid deadlocks. <see cref="Compilation.ScriptRunner"/> ensures scripts run via Task.Run.
/// </para>
/// </summary>
public sealed class DataProxy
{
    private readonly IQuantDataContext _context;
    private Func<CancellationToken> _ctProvider;

    public DataProxy(IQuantDataContext context, Func<CancellationToken> ctProvider)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _ctProvider = ctProvider ?? throw new ArgumentNullException(nameof(ctProvider));
    }

    // ── DateOnly overloads (preferred) ────────────────────────────────────────

    public PriceSeries Prices(string symbol, DateOnly from, DateOnly to)
        => PricesAsync(symbol, from, to).GetAwaiter().GetResult();

    public PriceSeries Prices(string symbol, DateOnly from, DateOnly to, string? provider)
        => PricesAsync(symbol, from, to, provider).GetAwaiter().GetResult();

    public Task<PriceSeries> PricesAsync(string symbol, DateOnly from, DateOnly to)
        => _context.PricesAsync(symbol, from, to, _ctProvider());

    public Task<PriceSeries> PricesAsync(string symbol, DateOnly from, DateOnly to, string? provider)
        => _context.PricesAsync(symbol, from, to, provider, _ctProvider());

    // ── DateTime convenience overloads ────────────────────────────────────────

    /// <inheritdoc cref="Prices(string,DateOnly,DateOnly)"/>
    public PriceSeries Prices(string symbol, DateTime from, DateTime to)
        => Prices(symbol, DateOnly.FromDateTime(from), DateOnly.FromDateTime(to));

    /// <inheritdoc cref="Prices(string,DateOnly,DateOnly,string?)"/>
    public PriceSeries Prices(string symbol, DateTime from, DateTime to, string? provider)
        => Prices(symbol, DateOnly.FromDateTime(from), DateOnly.FromDateTime(to), provider);

    /// <inheritdoc cref="PricesAsync(string,DateOnly,DateOnly)"/>
    public Task<PriceSeries> PricesAsync(string symbol, DateTime from, DateTime to)
        => PricesAsync(symbol, DateOnly.FromDateTime(from), DateOnly.FromDateTime(to));

    /// <inheritdoc cref="PricesAsync(string,DateOnly,DateOnly,string?)"/>
    public Task<PriceSeries> PricesAsync(string symbol, DateTime from, DateTime to, string? provider)
        => PricesAsync(symbol, DateOnly.FromDateTime(from), DateOnly.FromDateTime(to), provider);

    // ── Trades & order book ───────────────────────────────────────────────────

    public IReadOnlyList<ScriptTrade> Trades(string symbol, DateOnly date)
        => TradesAsync(symbol, date).GetAwaiter().GetResult();

    public Task<IReadOnlyList<ScriptTrade>> TradesAsync(string symbol, DateOnly date)
        => _context.TradesAsync(symbol, date, _ctProvider());

    public ScriptOrderBook? OrderBook(string symbol, DateTimeOffset timestamp)
        => OrderBookAsync(symbol, timestamp).GetAwaiter().GetResult();

    public Task<ScriptOrderBook?> OrderBookAsync(string symbol, DateTimeOffset timestamp)
        => _context.OrderBookAsync(symbol, timestamp, _ctProvider());

    // ── Security Master ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the Security Master detail record for <paramref name="symbol"/> (ticker),
    /// including asset class, economic terms, and all registered identifiers.
    /// Returns <see langword="null"/> when the symbol is unknown or no Security Master is configured.
    /// </summary>
    public SecurityDetailDto? SecMaster(string symbol)
        => SecMasterAsync(symbol).GetAwaiter().GetResult();

    public Task<SecurityDetailDto?> SecMasterAsync(string symbol)
        => _context.SecMasterAsync(symbol, _ctProvider());

    /// <summary>
    /// Returns the time-ordered list of corporate action events for <paramref name="symbol"/>.
    /// Returns an empty list when no corporate actions are recorded or no Security Master is configured.
    /// </summary>
    public IReadOnlyList<CorporateActionDto> CorporateActions(string symbol)
        => CorporateActionsAsync(symbol).GetAwaiter().GetResult();

    public Task<IReadOnlyList<CorporateActionDto>> CorporateActionsAsync(string symbol)
        => _context.CorporateActionsAsync(symbol, _ctProvider());

    internal void UpdateCancellationTokenProvider(Func<CancellationToken> cancellationTokenProvider)
        => _ctProvider = cancellationTokenProvider ?? throw new ArgumentNullException(nameof(cancellationTokenProvider));
}
