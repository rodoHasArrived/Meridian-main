namespace Meridian.Backtesting;

/// <summary>
/// Immutable, content-versioned corporate-action adjustment prepared from a symbol's complete
/// historical bar series at a pinned Security Master as-of time.
/// </summary>
public sealed class CorporateActionAdjustmentPlan
{
    private readonly AdjustmentStep[] _steps;
    private readonly IReadOnlyDictionary<BarKey, HistoricalBar>? _legacyAdjustedBars;

    internal CorporateActionAdjustmentPlan(
        string ticker,
        DateTimeOffset asOfUtc,
        string contentVersion,
        int barCount,
        IEnumerable<(DateOnly ExDate, decimal SplitDivisor, decimal DividendFactor)> steps)
    {
        Ticker = ticker;
        AsOfUtc = asOfUtc.ToUniversalTime();
        ContentVersion = contentVersion;
        BarCount = barCount;
        _steps = steps
            .Select(static step => new AdjustmentStep(step.ExDate, step.SplitDivisor, step.DividendFactor))
            .OrderBy(static step => step.ExDate)
            .ToArray();
    }

    private CorporateActionAdjustmentPlan(
        string ticker,
        DateTimeOffset asOfUtc,
        string contentVersion,
        IReadOnlyList<HistoricalBar> originalBars,
        IReadOnlyList<HistoricalBar> adjustedBars)
        : this(ticker, asOfUtc, contentVersion, originalBars.Count, [])
    {
        var adjustedByBar = new Dictionary<BarKey, HistoricalBar>();
        for (var index = 0; index < originalBars.Count; index++)
            adjustedByBar[BarKey.From(originalBars[index])] = adjustedBars[index];
        _legacyAdjustedBars = adjustedByBar;
    }

    public string Ticker { get; }
    public DateTimeOffset AsOfUtc { get; }
    public string ContentVersion { get; }
    public int BarCount { get; }

    /// <summary>Applies the prepared immutable adjustment to one bar from the same history.</summary>
    public HistoricalBar Apply(HistoricalBar bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        if (_legacyAdjustedBars is not null)
        {
            return _legacyAdjustedBars.TryGetValue(BarKey.From(bar), out var adjusted)
                ? adjusted
                : bar;
        }

        var splitDivisor = 1m;
        var dividendFactor = 1m;
        foreach (var step in _steps)
        {
            if (step.ExDate <= bar.SessionDate)
                continue;

            splitDivisor *= step.SplitDivisor;
            dividendFactor *= step.DividendFactor;
        }

        if (splitDivisor == 1m && dividendFactor == 1m)
            return bar;

        return new HistoricalBar(
            Symbol: bar.Symbol,
            SessionDate: bar.SessionDate,
            Open: bar.Open * dividendFactor / splitDivisor,
            High: bar.High * dividendFactor / splitDivisor,
            Low: bar.Low * dividendFactor / splitDivisor,
            Close: bar.Close * dividendFactor / splitDivisor,
            Volume: (long)Math.Round(bar.Volume * splitDivisor, MidpointRounding.AwayFromZero),
            Source: bar.Source,
            SequenceNumber: bar.SequenceNumber,
            IsAdjusted: true);
    }

    internal static CorporateActionAdjustmentPlan FromLegacyAdjustedBars(
        string ticker,
        DateTimeOffset asOfUtc,
        IReadOnlyList<HistoricalBar> originalBars,
        IReadOnlyList<HistoricalBar> adjustedBars)
    {
        if (adjustedBars.Count != originalBars.Count)
        {
            throw new InvalidOperationException(
                $"Legacy corporate-action adjuster returned {adjustedBars.Count} bars for {originalBars.Count} inputs.");
        }

        using var hash = CorporateActionContentHasher.Create();
        CorporateActionContentHasher.AppendValue(hash, ticker.Trim().ToUpperInvariant());
        CorporateActionContentHasher.AppendValue(
            hash,
            asOfUtc.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        for (var index = 0; index < originalBars.Count; index++)
        {
            CorporateActionContentHasher.AppendBar(hash, originalBars[index]);
            CorporateActionContentHasher.AppendBar(hash, adjustedBars[index]);
        }

        return new CorporateActionAdjustmentPlan(
            ticker.Trim().ToUpperInvariant(),
            asOfUtc,
            CorporateActionContentHasher.Complete(hash),
            originalBars,
            adjustedBars);
    }

    private readonly record struct AdjustmentStep(
        DateOnly ExDate,
        decimal SplitDivisor,
        decimal DividendFactor);

    private readonly record struct BarKey(
        string Symbol,
        DateOnly SessionDate,
        decimal Open,
        decimal High,
        decimal Low,
        decimal Close,
        long Volume,
        string? Source,
        long SequenceNumber)
    {
        public static BarKey From(HistoricalBar bar) => new(
            bar.Symbol,
            bar.SessionDate,
            bar.Open,
            bar.High,
            bar.Low,
            bar.Close,
            bar.Volume,
            bar.Source,
            bar.SequenceNumber);
    }
}

/// <summary>Incremental deterministic SHA-256 writer shared by native and legacy plans.</summary>
internal static class CorporateActionContentHasher
{
    private static readonly byte[] FieldSeparator = [(byte)'|'];
    private static readonly byte[] RecordSeparator = [(byte)';'];

    public static System.Security.Cryptography.IncrementalHash Create()
        => System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);

    public static void AppendBar(
        System.Security.Cryptography.IncrementalHash hash,
        HistoricalBar bar)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        AppendValue(hash, bar.Symbol);
        AppendValue(hash, bar.SessionDate.ToString("yyyy-MM-dd", invariant));
        AppendValue(hash, bar.Open.ToString("G29", invariant));
        AppendValue(hash, bar.High.ToString("G29", invariant));
        AppendValue(hash, bar.Low.ToString("G29", invariant));
        AppendValue(hash, bar.Close.ToString("G29", invariant));
        AppendValue(hash, bar.Volume.ToString(invariant));
        AppendValue(hash, bar.Source);
        AppendValue(hash, bar.SequenceNumber.ToString(invariant), endRecord: true);
    }

    public static void AppendValue(
        System.Security.Cryptography.IncrementalHash hash,
        string? value,
        bool endRecord = false)
    {
        ArgumentNullException.ThrowIfNull(hash);
        if (!string.IsNullOrEmpty(value))
        {
            var encoding = System.Text.Encoding.UTF8;
            var maxByteCount = encoding.GetMaxByteCount(value.Length);
            byte[]? rented = null;
            try
            {
                Span<byte> buffer = maxByteCount <= 512
                    ? stackalloc byte[maxByteCount]
                    : (rented = System.Buffers.ArrayPool<byte>.Shared.Rent(maxByteCount));
                var written = encoding.GetBytes(value, buffer);
                hash.AppendData(buffer[..written]);
            }
            finally
            {
                if (rented is not null)
                    System.Buffers.ArrayPool<byte>.Shared.Return(rented);
            }
        }

        hash.AppendData(endRecord ? RecordSeparator : FieldSeparator);
    }

    public static string Complete(System.Security.Cryptography.IncrementalHash hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        return $"sha256:{Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()}";
    }
}

/// <summary>
/// Service for adjusting historical bar prices and volumes for corporate actions (stock splits and dividends).
/// </summary>
public interface ICorporateActionAdjustmentService
{
    /// <summary>
    /// Prepares one immutable adjustment from complete price history at a caller-pinned as-of time.
    /// The default implementation preserves compatibility with legacy adjusters that only implement
    /// the batch API.
    /// </summary>
    async Task<CorporateActionAdjustmentPlan> PrepareAsync(
        IReadOnlyList<HistoricalBar> bars,
        string ticker,
        DateTimeOffset asOfUtc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bars);
        ArgumentException.ThrowIfNullOrWhiteSpace(ticker);
        ct.ThrowIfCancellationRequested();
        var adjusted = await AdjustAsync(bars, ticker, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return CorporateActionAdjustmentPlan.FromLegacyAdjustedBars(
            ticker,
            asOfUtc,
            bars,
            adjusted);
    }

    /// <summary>
    /// Adjusts historical bars for stock splits and dividends using Security Master data.
    /// </summary>
    /// <param name="bars">Original historical bars (not modified).</param>
    /// <param name="ticker">Ticker symbol to resolve to security ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>New list of adjusted bars, or original bars if security not found or no actions recorded.</returns>
    Task<IReadOnlyList<HistoricalBar>> AdjustAsync(
        IReadOnlyList<HistoricalBar> bars,
        string ticker,
        CancellationToken ct = default);

    /// <summary>
    /// Adjusts a single historical bar without requiring the caller to buffer a replay window.
    /// </summary>
    /// <param name="bar">Original historical bar.</param>
    /// <param name="ticker">Ticker symbol to resolve to security ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Adjusted bar, or the original bar if security not found or no actions recorded.</returns>
    async Task<HistoricalBar> AdjustBarAsync(
        HistoricalBar bar,
        string ticker,
        CancellationToken ct = default)
    {
        var adjusted = await AdjustAsync([bar], ticker, ct).ConfigureAwait(false);
        return adjusted.Count == 0 ? bar : adjusted[0];
    }

    /// <summary>
    /// Adjusts historical bars as a stream, yielding adjusted bars incrementally.
    /// </summary>
    /// <param name="bars">Source historical bars stream.</param>
    /// <param name="ticker">Ticker symbol to resolve to security ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Adjusted bars stream.</returns>
    async IAsyncEnumerable<HistoricalBar> AdjustAsync(
        IAsyncEnumerable<HistoricalBar> bars,
        string ticker,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var buffered = new List<HistoricalBar>();
        await foreach (var bar in bars.WithCancellation(ct).ConfigureAwait(false))
        {
            buffered.Add(bar);
        }

        var adjusted = await AdjustAsync(buffered, ticker, ct).ConfigureAwait(false);
        foreach (var bar in adjusted)
        {
            yield return bar;
        }
    }
}
