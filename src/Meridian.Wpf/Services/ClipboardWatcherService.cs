using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;

namespace Meridian.Wpf.Services;

/// <summary>
/// Event arguments carrying detected ticker-symbol candidates from clipboard text.
/// </summary>
public sealed class SymbolsDetectedEventArgs : EventArgs
{
    public IReadOnlyList<string> Symbols { get; init; } = [];
    /// <summary>First 200 characters of the source text, for context.</summary>
    public string SourceText { get; init; } = string.Empty;
}

/// <summary>
/// Watches the system clipboard for uppercase ticker-symbol patterns using
/// WM_CLIPBOARDUPDATE (Vista+). Raises <see cref="SymbolsDetected"/> when
/// plausible ticker candidates are found after filtering common English words.
/// </summary>
public sealed class ClipboardWatcherService : IDisposable
{
    private static readonly Lazy<ClipboardWatcherService> _instance =
        new(() => new ClipboardWatcherService());

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private static readonly Regex _tickerRegex = new(@"\b[A-Z]{1,5}\b", RegexOptions.Compiled);

    // Common English words and market-context noise words excluded from symbol detection.
    private static readonly HashSet<string> _stopWords = new(StringComparer.Ordinal)
    {
        // 2-letter
        "IT", "US", "AI", "OR", "AT", "TO", "IN", "BY", "OF", "AS", "IF", "IS",
        "ON", "NO", "SO", "DO", "BE", "AN", "UP", "AM", "HE", "ME", "MY", "GO",

        // 3-letter
        "OUT", "FOR", "THE", "AND", "BUT", "NOT", "ARE", "WAS", "HAS", "CAN",
        "ALL", "NEW", "GET", "ONE", "TWO", "MAY", "OUR", "ITS", "WHO", "HOW",
        "WHY", "HIM", "HER", "SHE", "HIS", "OWN", "NOW", "ANY", "USE", "DID",
        "TRY", "SAY", "LET", "PUT", "SET", "RAN", "GOT", "YES", "YET", "AGO",
        "BUY", "LOW", "IPO", "SEC", "ETF", "LLC", "INC",

        // 4-letter
        "WHAT", "THAT", "WITH", "FROM", "HAVE", "THIS", "WILL", "YOUR", "BEEN",
        "THEY", "THAN", "THEN", "SOME", "ALSO", "INTO", "OVER", "JUST", "LIKE",
        "EACH", "MANY", "MOST", "YEAR", "MUCH", "SUCH", "BOTH", "ONLY", "WELL",
        "BACK", "GOOD", "KNOW", "EVEN", "VERY", "LONG", "COME", "SAID", "LAST",
        "HELP", "MADE", "MAKE", "TAKE", "LOOK", "USED", "DAYS", "AREA", "HIGH",
        "OPEN", "RISK", "FAIR", "HOLD", "SELL", "CALL", "PUTS", "RATE", "BEST",
        "NEXT", "SAME", "TYPE", "BASE", "CASE", "FREE", "MORE", "TIME", "NEED",
        "MOVE", "MUST", "SHOW", "PLAN", "TERM", "FUND", "BOND", "DEAL", "GOLD",
        "CASH", "CORP", "STOP", "GAIN", "LOSS", "WENT", "SEES", "DOES", "SAYS",
        "WEEK", "CITY", "LIFE", "FIRM", "COST", "GETS", "SAID", "WHEN", "THEM",
        "WERE", "SAID", "REAL", "FULL", "LESS", "PUTS", "GIVE", "KEEP", "TOLD",
        "DOWN",

        // 5-letter
        "ABOUT", "ABOVE", "AFTER", "AGAIN", "BEING", "BELOW", "COULD", "EVERY",
        "FIRST", "FOUND", "GREAT", "HOUSE", "LARGE", "LATER", "LEAST", "LIGHT",
        "LOCAL", "MAYBE", "MIGHT", "MONEY", "NEVER", "OFTEN", "OTHER", "PLACE",
        "POINT", "RIGHT", "ROUND", "SMALL", "SOUND", "STILL", "STUDY", "THEIR",
        "THERE", "THESE", "THING", "THINK", "THREE", "THOSE", "UNDER", "UNTIL",
        "WATCH", "WHERE", "WHICH", "WHILE", "WORLD", "WOULD", "WRITE", "YOUNG",
        "YEARS", "SHARE", "STOCK", "PRICE", "TRADE", "RATES", "INDEX", "TOTAL",
        "GIVEN", "SINCE", "TODAY", "MEDIA", "BOARD", "GROUP", "BASED", "STATE",
        "MONTH", "TOTAL", "VALUE", "LOWER", "GAINS", "FALLS", "CLOSE", "RANGE",
        "ISSUE", "AHEAD", "EARLY", "KNOWN", "ENDED", "SALES", "BEATS",
    };

    private HwndSource? _hwndSource;
    private bool _disposed;

    /// <summary>Gets the singleton instance.</summary>
    public static ClipboardWatcherService Instance => _instance.Value;

    private ClipboardWatcherService() { }

    /// <summary>
    /// Raised on the UI thread when ticker-symbol candidates are found in clipboard text.
    /// </summary>
    public event EventHandler<SymbolsDetectedEventArgs>? SymbolsDetected;

    /// <summary>
    /// Registers <c>WM_CLIPBOARDUPDATE</c> for <paramref name="hwnd"/> and installs
    /// the internal WndProc hook. Must be called on the UI thread after the window handle
    /// is valid (i.e. from <c>Window.Loaded</c>).
    /// </summary>
    public void Initialize(IntPtr hwnd)
    {
        if (_disposed) return;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        if (_hwndSource is null) return;
        _hwndSource.AddHook(WndProc);
        AddClipboardFormatListener(hwnd);
    }

    /// <summary>
    /// Reads the current clipboard text, extracts symbol candidates, and fires
    /// <see cref="SymbolsDetected"/> if any are found. Safe to call on the UI thread;
    /// clipboard access is dispatched via <c>Application.Current.Dispatcher</c>.
    /// </summary>
    public void HandleClipboardChanged()
    {
        if (_disposed) return;
        try
        {
            var text = System.Windows.Application.Current.Dispatcher.Invoke(static () =>
            {
                try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
                catch { return null; }
            });

            if (string.IsNullOrWhiteSpace(text)) return;

            var symbols = ExtractSymbols(text);
            if (symbols.Count == 0) return;

            SymbolsDetected?.Invoke(this, new SymbolsDetectedEventArgs
            {
                Symbols = symbols,
                SourceText = text.Length > 200 ? text[..200] : text,
            });
        }
        catch (Exception ex)
        {
        }
    }

    /// <summary>
    /// Extracts up to 10 unique ticker candidates from <paramref name="text"/>.
    /// Text is uppercased before matching so mixed-case clipboard content is handled.
    /// </summary>
    internal static List<string> ExtractSymbols(string text)
    {
        var upper = text.ToUpperInvariant();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<string>(10);

        foreach (Match m in _tickerRegex.Matches(upper))
        {
            var candidate = m.Value;
            if (_stopWords.Contains(candidate)) continue;
            if (!seen.Add(candidate)) continue;
            results.Add(candidate);
            if (results.Count >= 10) break;
        }

        return results;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE)
            HandleClipboardChanged();
        return IntPtr.Zero;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwndSource is not null)
        {
            try
            {
                RemoveClipboardFormatListener(_hwndSource.Handle);
                _hwndSource.RemoveHook(WndProc);
            }
            catch (Exception ex)
            {
            }
            _hwndSource = null;
        }
    }
}
