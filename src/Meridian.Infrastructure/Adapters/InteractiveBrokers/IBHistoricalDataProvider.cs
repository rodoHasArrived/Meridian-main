#if IBAPI
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using IBApi;
using Meridian.Application.Config;
using Meridian.Application.Logging;
using Meridian.Contracts.Domain.Models;
using Meridian.Domain.Models;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.DataSources;
using Serilog;
using DataSourceType = Meridian.Infrastructure.DataSources.DataSourceType;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>
/// Historical data provider using Interactive Brokers TWS API.
/// Implements rate limiting based on IB's pacing rules:
/// - Max 60 requests per 10-minute window
/// - Max 6 requests per 2 seconds for same contract
/// - Min 15 seconds between identical requests
/// - Max 50 concurrent requests
/// </summary>
/// <remarks>
/// Historical data requires active Level 1 streaming subscription for US equities.
/// Free streaming data is available via Cboe One + IEX (non-consolidated).
/// </remarks>
[DataSource("ibkr", "Interactive Brokers", DataSourceType.Historical, DataSourceCategory.Broker,
    Priority = 10, Description = "Historical OHLCV data via Interactive Brokers TWS API")]
[ImplementsAdr("ADR-001", "Interactive Brokers historical data provider implementation")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class IBHistoricalDataProvider : IHistoricalDataProvider, IRateLimitAwareProvider, IDisposable
{
    private readonly EnhancedIBConnectionManager _connectionManager;
    private readonly RateLimiter _globalRateLimiter;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastRequestByContract = new();
    private readonly SemaphoreSlim _concurrentRequestsSemaphore;
    private readonly ILogger _log;
    private readonly int _priority;
    private int _requestCount;
    private DateTimeOffset _windowStart;
    private DateTimeOffset? _rateLimitResetsAt;
    private bool _isRateLimited;
    private bool _disposed;

    public string Name => "ibkr";
    public string DisplayName => "Interactive Brokers";
    public string Description => "Historical OHLCV data via TWS API. Requires active streaming subscription for US equities.";

    // IB-mandated cooldown after a pacing-violation response (error 162). Centralised here so both
    // GetDailyBarsAsync / GetAdjustedDailyBarsAsync and GetIntradayBarsAsync use the same value. [P1]
    private static readonly TimeSpan PacingViolationCooldown = TimeSpan.FromSeconds(30);

    public int Priority => _priority;
    public TimeSpan RateLimitDelay => TimeSpan.FromSeconds(IBApiLimits.MinSecondsBetweenIdenticalRequests);
    public int MaxRequestsPerWindow => IBApiLimits.MaxHistoricalRequestsPer10Min;
    public TimeSpan RateLimitWindow => IBApiLimits.HistoricalRequestWindow;

    /// <summary>
    /// IB supports adjusted bars with intraday data for global markets.
    /// </summary>
    public HistoricalDataCapabilities Capabilities { get; } = (HistoricalDataCapabilities.BarsOnly with
    {
        Intraday = true
    }).WithMarkets("US", "EU", "APAC");

    /// <summary>
    /// Event raised when the provider hits a rate limit (pacing violation).
    /// </summary>
    public event Action<RateLimitInfo>? OnRateLimitHit;

    /// <summary>
    /// Creates a new IB historical data provider.
    /// </summary>
    /// <param name="connectionManager">The IB connection manager.</param>
    /// <param name="priority">Priority in fallback chain (lower = tried first).</param>
    /// <param name="log">Optional logger instance.</param>
    public IBHistoricalDataProvider(
        EnhancedIBConnectionManager connectionManager,
        int priority = 10,
        ILogger? log = null)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _priority = priority;
        _log = log ?? LoggingSetup.ForContext<IBHistoricalDataProvider>();

        // Global rate limiter: 60 requests per 10 minutes
        _globalRateLimiter = new RateLimiter(
            IBApiLimits.MaxHistoricalRequestsPer10Min,
            IBApiLimits.HistoricalRequestWindow,
            TimeSpan.FromMilliseconds(200), // Min delay between requests
            _log);

        // Concurrent requests semaphore
        _concurrentRequestsSemaphore = new SemaphoreSlim(
            IBApiLimits.MaxConcurrentHistoricalRequests,
            IBApiLimits.MaxConcurrentHistoricalRequests);

        _windowStart = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Get current rate limit usage information.
    /// </summary>
    public RateLimitInfo GetRateLimitInfo()
    {
        // Reset window if expired
        if (DateTimeOffset.UtcNow - _windowStart > RateLimitWindow)
        {
            _requestCount = 0;
            _windowStart = DateTimeOffset.UtcNow;
            _isRateLimited = false;
            _rateLimitResetsAt = null;
        }

        return new RateLimitInfo(
            Name,
            _requestCount,
            MaxRequestsPerWindow,
            RateLimitWindow,
            _rateLimitResetsAt,
            _isRateLimited,
            _rateLimitResetsAt.HasValue ? _rateLimitResetsAt.Value - DateTimeOffset.UtcNow : null
        );
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        if (!_connectionManager.IsConnected)
        {
            _log.Warning("IB connection not established");
            return false;
        }

        try
        {
            // Quick health check with a known symbol
            var cfg = new SymbolConfig(
                Symbol: "SPY",
                SecurityType: "STK",
                Exchange: "SMART",
                Currency: "USD");

            // Request just 1 day of data as a health check
            var bars = await RequestHistoricalBarsWithPacingAsync(
                cfg,
                DateTime.UtcNow.ToString("yyyyMMdd-HH:mm:ss"),
                "1 D",
                IBBarSizes.Day1,
                IBWhatToShow.Trades,
                true,
                ct).ConfigureAwait(false);

            return bars.Count > 0;
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "IB availability check failed");
            return false;
        }
    }

    public async Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        var adjustedBars = await GetAdjustedDailyBarsAsync(symbol, from, to, ct).ConfigureAwait(false);
        return adjustedBars.Select(b => b.ToHistoricalBar(preferAdjusted: true)).ToList();
    }

    public async Task<IReadOnlyList<AdjustedHistoricalBar>> GetAdjustedDailyBarsAsync(
        string symbol,
        DateOnly? from,
        DateOnly? to,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol is required", nameof(symbol));

        if (!_connectionManager.IsConnected)
            throw new InvalidOperationException("Not connected to IB Gateway/TWS. Call ConnectAsync first.");

        var cfg = CreateSymbolConfig(symbol);
        var allBars = new List<AdjustedHistoricalBar>();

        // Calculate duration based on date range
        var endDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var startDate = from ?? endDate.AddYears(-1);
        var totalDays = endDate.DayNumber - startDate.DayNumber;

        // IB has maximum duration limits per bar size
        // For daily bars: max 1 year per request
        const int maxDaysPerRequest = 365;
        var currentEnd = endDate;

        while (currentEnd > startDate && !ct.IsCancellationRequested)
        {
            var daysToFetch = Math.Min(maxDaysPerRequest, currentEnd.DayNumber - startDate.DayNumber + 1);
            var durationStr = $"{daysToFetch} D";
            var endDateTime = currentEnd.ToDateTime(new TimeOnly(23, 59, 59)).ToString("yyyyMMdd-HH:mm:ss");

            try
            {
                var bars = await RequestHistoricalBarsWithPacingAsync(
                    cfg,
                    endDateTime,
                    durationStr,
                    IBBarSizes.Day1,
                    IBWhatToShow.AdjustedLast, // Use adjusted prices
                    true, // RTH only
                    ct).ConfigureAwait(false);

                foreach (var bar in bars)
                {
                    var sessionDate = ParseBarDate(bar.Time);
                    if (sessionDate < startDate || sessionDate > endDate) continue;

                    // Validate OHLC
                    if (bar.Open <= 0 || bar.High <= 0 || bar.Low <= 0 || bar.Close <= 0)
                        continue;

                    allBars.Add(new AdjustedHistoricalBar(
                        Symbol: symbol.ToUpperInvariant(),
                        SessionDate: sessionDate,
                        Open: (decimal)bar.Open,
                        High: (decimal)bar.High,
                        Low: (decimal)bar.Low,
                        Close: (decimal)bar.Close,
                        Volume: ConvertVolume(bar.Volume),
                        Source: Name,
                        SequenceNumber: sessionDate.DayNumber,
                        // IB returns adjusted prices when using ADJUSTED_LAST
                        AdjustedOpen: (decimal)bar.Open,
                        AdjustedHigh: (decimal)bar.High,
                        AdjustedLow: (decimal)bar.Low,
                        AdjustedClose: (decimal)bar.Close,
                        AdjustedVolume: ConvertVolume(bar.Volume)
                    ));
                }

                // Move to earlier period
                if (bars.Count > 0)
                {
                    var earliestBar = bars.OrderBy(b => b.Time).First();
                    var earliestDate = ParseBarDate(earliestBar.Time);
                    currentEnd = earliestDate.AddDays(-1);
                }
                else
                {
                    break; // No more data available
                }
            }
            catch (Exception ex) when (ex.Message.Contains("pacing", StringComparison.OrdinalIgnoreCase))
            {
                // Pacing violation - wait and retry using the shared cooldown constant [P1]
                _isRateLimited = true;
                _rateLimitResetsAt = DateTimeOffset.UtcNow + PacingViolationCooldown;
                OnRateLimitHit?.Invoke(GetRateLimitInfo());

                _log.Warning("IB pacing violation for {Symbol}, waiting {Seconds}s",
                    symbol, (int)PacingViolationCooldown.TotalSeconds);
                await Task.Delay(PacingViolationCooldown, ct).ConfigureAwait(false);
                // Don't move currentEnd, retry same period
            }
        }

        _log.Information("Fetched {Count} bars for {Symbol} from IB", allBars.Count, symbol);
        return allBars.OrderBy(b => b.SessionDate).ToList();
    }

    /// <summary>
    /// Request historical bars with proper pacing to avoid violations.
    /// </summary>
    private async Task<List<Bar>> RequestHistoricalBarsWithPacingAsync(
        SymbolConfig cfg,
        string endDateTime,
        string durationStr,
        string barSize,
        string whatToShow,
        bool useRTH,
        CancellationToken ct)
    {
        // Wait for concurrent request slot
        await _concurrentRequestsSemaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Wait for global rate limit
            await _globalRateLimiter.WaitForSlotAsync(ct).ConfigureAwait(false);

            // Enforce per-contract rate limiting
            var contractKey = $"{cfg.Symbol}:{cfg.Exchange}:{whatToShow}";
            await EnforcePerContractPacingAsync(contractKey, ct).ConfigureAwait(false);

            // Track request count
            Interlocked.Increment(ref _requestCount);

            // Make the request
            var bars = await _connectionManager.RequestHistoricalDataAsync(
                cfg,
                endDateTime,
                durationStr,
                barSize,
                whatToShow,
                useRTH,
                ct).ConfigureAwait(false);

            // Record successful request
            _lastRequestByContract[contractKey] = DateTimeOffset.UtcNow;
            _isRateLimited = false;

            return bars;
        }
        finally
        {
            _concurrentRequestsSemaphore.Release();
        }
    }

    /// <summary>
    /// Enforce per-contract pacing: max 6 requests per 2 seconds for same contract.
    /// </summary>
    private async Task EnforcePerContractPacingAsync(string contractKey, CancellationToken ct)
    {
        if (_lastRequestByContract.TryGetValue(contractKey, out var lastRequest))
        {
            var elapsed = DateTimeOffset.UtcNow - lastRequest;
            var minDelay = IBApiLimits.SameContractWindow / IBApiLimits.MaxSameContractRequestsPer2Sec;

            if (elapsed < minDelay)
            {
                var waitTime = minDelay - elapsed;
                _log.Debug("Enforcing per-contract pacing for {Contract}, waiting {WaitMs}ms",
                    contractKey, waitTime.TotalMilliseconds);
                await Task.Delay(waitTime, ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Request intraday bars for a symbol.
    /// </summary>
    public async Task<IReadOnlyList<IntradayBar>> GetIntradayBarsAsync(
        string symbol,
        DateTimeOffset start,
        DateTimeOffset end,
        string barSize = "1 min",
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_connectionManager.IsConnected)
            throw new InvalidOperationException("Not connected to IB Gateway/TWS");

        var cfg = CreateSymbolConfig(symbol);
        var bars = new List<IntradayBar>();

        // IB has duration limits for small bars:
        // - 1 min bars: max 1 day per request
        // - 5 min bars: max 1 week per request
        var maxDuration = barSize switch
        {
            IBBarSizes.Min1 or IBBarSizes.Secs30 => TimeSpan.FromDays(1),
            IBBarSizes.Mins5 or IBBarSizes.Mins3 => TimeSpan.FromDays(7),
            IBBarSizes.Mins15 or IBBarSizes.Mins30 => TimeSpan.FromDays(30),
            _ => TimeSpan.FromDays(30)
        };

        var currentEnd = end;
        while (currentEnd > start && !ct.IsCancellationRequested)
        {
            var chunkStart = currentEnd - maxDuration;
            if (chunkStart < start) chunkStart = start;

            var duration = currentEnd - chunkStart;
            var durationStr = FormatDuration(duration);
            var endDateTime = currentEnd.ToString("yyyyMMdd-HH:mm:ss");

            try
            {
                var ibBars = await RequestHistoricalBarsWithPacingAsync(
                    cfg,
                    endDateTime,
                    durationStr,
                    barSize,
                    IBWhatToShow.Trades,
                    false, // Include extended hours
                    ct).ConfigureAwait(false);

                foreach (var bar in ibBars)
                {
                    var timestamp = ParseBarTimestamp(bar.Time);
                    if (timestamp < start || timestamp > end) continue;

                    bars.Add(new IntradayBar(
                        Symbol: symbol.ToUpperInvariant(),
                        Timestamp: timestamp,
                        Open: (decimal)bar.Open,
                        High: (decimal)bar.High,
                        Low: (decimal)bar.Low,
                        Close: (decimal)bar.Close,
                        Volume: ConvertVolume(bar.Volume),
                        VWAP: bar.WAP,
                        TradeCount: bar.Count,
                        Source: Name
                    ));
                }

                currentEnd = chunkStart;
            }
            catch (Exception ex) when (ex.Message.Contains("pacing", StringComparison.OrdinalIgnoreCase))
            {
                _isRateLimited = true;
                _rateLimitResetsAt = DateTimeOffset.UtcNow + PacingViolationCooldown;
                OnRateLimitHit?.Invoke(GetRateLimitInfo());

                _log.Warning("IB pacing violation for {Symbol} intraday, waiting {Seconds}s",
                    symbol, (int)PacingViolationCooldown.TotalSeconds);
                await Task.Delay(PacingViolationCooldown, ct).ConfigureAwait(false);
            }
        }

        return bars.OrderBy(b => b.Timestamp).ToList();
    }

    private static SymbolConfig CreateSymbolConfig(string symbol)
    {
        return new SymbolConfig(
            Symbol: symbol.ToUpperInvariant(),
            SecurityType: "STK",
            Exchange: "SMART",
            Currency: "USD");
    }

    private static DateOnly ParseBarDate(string ibDate)
    {
        // IB returns dates in format: "yyyyMMdd" for daily bars
        if (ibDate.Length >= 8 && DateOnly.TryParseExact(
            ibDate.Substring(0, 8),
            "yyyyMMdd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date))
        {
            return date;
        }

        throw new FormatException($"Cannot parse IB date: {ibDate}");
    }

    private static DateTimeOffset ParseBarTimestamp(string ibDateTime)
    {
        // IB returns timestamps in various formats depending on bar size
        // Common formats: "yyyyMMdd  HH:mm:ss" or "yyyyMMdd HH:mm:ss" or epoch
        if (long.TryParse(ibDateTime, out var epoch))
        {
            return DateTimeOffset.FromUnixTimeSeconds(epoch);
        }

        var formats = new[]
        {
            "yyyyMMdd  HH:mm:ss",
            "yyyyMMdd HH:mm:ss",
            "yyyyMMdd-HH:mm:ss"
        };

        foreach (var format in formats)
        {
            if (DateTimeOffset.TryParseExact(
                ibDateTime,
                format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var dt))
            {
                return dt;
            }
        }

        throw new FormatException($"Cannot parse IB timestamp: {ibDateTime}");
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalSeconds < 3600)
            return $"{(int)duration.TotalSeconds} S";
        if (duration.TotalDays < 1)
            return $"{(int)duration.TotalSeconds} S";
        if (duration.TotalDays <= 7)
            return $"{(int)duration.TotalDays} D";
        if (duration.TotalDays <= 30)
            return $"{(int)Math.Ceiling(duration.TotalDays / 7)} W";
        if (duration.TotalDays <= 365)
            return $"{(int)Math.Ceiling(duration.TotalDays / 30)} M";
        return $"{(int)Math.Ceiling(duration.TotalDays / 365)} Y";
    }

    private static long ConvertVolume(decimal volume)
        => volume <= 0 ? 0L : decimal.ToInt64(decimal.Truncate(volume));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _globalRateLimiter.Dispose();
        _concurrentRequestsSemaphore.Dispose();
    }
}

/// <summary>
/// Intraday bar data from IB.
/// </summary>
public sealed record IntradayBar(
    string Symbol,
    DateTimeOffset Timestamp,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    decimal VWAP,
    int TradeCount,
    string Source = "ibkr"
);
#else

using Meridian.Application.Logging;
using Meridian.Contracts.Domain.Models;
using Meridian.Infrastructure.Adapters.Core;
using Meridian.Infrastructure.Contracts;
using Meridian.Infrastructure.DataSources;

namespace Meridian.Infrastructure.Adapters.InteractiveBrokers;

/// <summary>
/// Stub IB historical data provider for non-IBAPI builds.
/// Registers in the provider list so users can see IB is available but requires IBAPI.
/// </summary>
[DataSource("ibkr", "Interactive Brokers", DataSourceType.Historical, DataSourceCategory.Broker,
    Priority = 80, Description = "Stub provider — build with IBAPI to enable real IB historical data")]
[ImplementsAdr("ADR-001", "Interactive Brokers historical data provider stub")]
[ImplementsAdr("ADR-004", "All async methods support CancellationToken")]
[ImplementsAdr("ADR-005", "Attribute-based provider discovery")]
public sealed class IBHistoricalDataProvider : IHistoricalDataProvider
{
    private static readonly Serilog.ILogger _log = LoggingSetup.ForContext<IBHistoricalDataProvider>();

    public IBHistoricalDataProvider() { }

    public IBHistoricalDataProvider(
        EnhancedIBConnectionManager connectionManager,
        int priority = 10,
        Serilog.ILogger? log = null)
    {
        if (log is not null)
            _ = log;
    }

    public string Name => "ibkr";
    public string DisplayName => "Interactive Brokers (requires IBAPI build)";
    public string Description =>
        "Historical OHLCV data via TWS API. "
        + IBBuildGuidance.BuildRealProviderMessage("IB historical data");

    public int Priority => 80;
    public TimeSpan RateLimitDelay => TimeSpan.FromSeconds(15);
    public int MaxRequestsPerWindow => 60;
    public TimeSpan RateLimitWindow => TimeSpan.FromMinutes(10);

    public HistoricalDataCapabilities Capabilities { get; } =
        (HistoricalDataCapabilities.BarsOnly with { Intraday = true }).WithMarkets("US", "EU", "APAC");

    // IProviderMetadata
    public string ProviderId => "ibkr";
    public string ProviderDisplayName => DisplayName;
    public string ProviderDescription => Description;
    public int ProviderPriority => Priority;

    public ProviderCapabilities ProviderCapabilities { get; } = new()
    {
        SupportedMarkets = new[] { "US", "EU", "APAC" },
        MaxRequestsPerWindow = 60,
        RateLimitWindow = TimeSpan.FromMinutes(10),
        MinRequestDelay = TimeSpan.FromSeconds(15)
    };

    public ProviderCredentialField[] ProviderCredentialFields => new[]
    {
        new ProviderCredentialField("Host", null, "TWS/Gateway Host", false, "127.0.0.1"),
        new ProviderCredentialField("Port", null, "TWS/Gateway Port", false, "7497")
    };

    public string[] ProviderNotes => new[]
    {
        "Requires IBAPI build flag to enable full functionality.",
        IBBuildGuidance.BuildRealProviderMessage("IB historical data")
    };

    public string[] ProviderWarnings => new[]
    {
        "IBAPI not compiled — historical data operations will return empty results.",
        $"Reference the official IBApi surface and rebuild to enable. See {IBBuildGuidance.SetupGuidePath}."
    };

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        _log.Debug("IB historical provider not available — IBAPI not compiled");
        return Task.FromResult(false);
    }

    public Task<IReadOnlyList<HistoricalBar>> GetDailyBarsAsync(
        string symbol, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        _log.Warning("IB historical data requested for {Symbol} but IBAPI is not compiled. Returning empty result", symbol);
        return Task.FromResult<IReadOnlyList<HistoricalBar>>(Array.Empty<HistoricalBar>());
    }

    public void Dispose() { }
}
#endif
