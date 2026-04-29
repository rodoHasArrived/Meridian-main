using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// Label on the price axis overlay drawn over the heatmap.
/// </summary>
public sealed class PriceLabelViewModel
{
    public string Price { get; init; } = string.Empty;
    /// <summary>Canvas.Top position in pixels, computed from control height.</summary>
    public double Y { get; init; }
}

/// <summary>
/// Holds live L2 order book state and renders a color-density heatmap via direct pixel
/// manipulation. All data is stored in pre-allocated arrays so <see cref="RenderFrame"/>
/// is completely allocation-free on every call.
/// </summary>
/// <remarks>
/// Color scheme (Bgra32):
///   Bid side - mint:  intensity-scaled #26BF86
///   Ask side - coral: intensity-scaled #DE5878
///   Mid price - amber #D69E38
///   Background - chart plot navy #08101A
/// </remarks>
public sealed class OrderBookHeatmapViewModel : BindableBase
{
    private const int MaxLevels = 200;

    // ── Pre-allocated render data — no GC in hot path ──────────────────────────

    private readonly double[] _bidPrices = new double[MaxLevels];
    private readonly double[] _bidDepthRatios = new double[MaxLevels];
    private readonly double[] _askPrices = new double[MaxLevels];
    private readonly double[] _askDepthRatios = new double[MaxLevels];
    private int _bidsCount;
    private int _asksCount;
    private double _midPrice;
    private double _priceMin;
    private double _priceMax;
    private double _controlHeight = 200.0;

    // ── Packed Bgra32 color constants: (A<<24)|(R<<16)|(G<<8)|B ───────────────

    // #08101A  R=8,   G=16,  B=26  -> Bgra32 int = 0xFF08101A
    private static readonly int ColorBackground = unchecked((int)0xFF08101A);
    // #D69E38  R=214, G=158, B=56  -> Bgra32 int = 0xFFD69E38
    private static readonly int ColorMidLine = unchecked((int)0xFFD69E38);

    // ── Public surface ─────────────────────────────────────────────────────────

    public ObservableCollection<PriceLabelViewModel> PriceLabels { get; } = new();

    /// <summary>
    /// Called by the UserControl code-behind whenever the control is resized so
    /// price-label Y positions stay accurate.
    /// </summary>
    public void SetControlHeight(double height)
    {
        _controlHeight = height > 0 ? height : 200.0;
        UpdatePriceLabels();
    }

    /// <summary>
    /// Updates the snapshot from the UI thread.
    /// <paramref name="bids"/> should be sorted descending (best bid first).
    /// <paramref name="asks"/> should be sorted ascending (best ask first).
    /// The method re-sorts internally so callers need not guarantee order.
    /// </summary>
    public void UpdateFromSnapshot(
        IReadOnlyList<OrderBookDisplayLevel> bids,
        IReadOnlyList<OrderBookDisplayLevel> asks)
    {
        double maxSize = 0;

        // ── Copy bids ────────────────────────────────────────────────────────
        _bidsCount = Math.Min(bids.Count, MaxLevels);
        for (int i = 0; i < _bidsCount; i++)
        {
            double sz = (double)bids[i].RawSize;
            _bidPrices[i] = (double)bids[i].RawPrice;
            _bidDepthRatios[i] = sz;
            if (sz > maxSize)
                maxSize = sz;
        }

        // ── Copy asks ────────────────────────────────────────────────────────
        _asksCount = Math.Min(asks.Count, MaxLevels);
        for (int i = 0; i < _asksCount; i++)
        {
            double sz = (double)asks[i].RawSize;
            _askPrices[i] = (double)asks[i].RawPrice;
            _askDepthRatios[i] = sz;
            if (sz > maxSize)
                maxSize = sz;
        }

        // ── Normalize depth ratios to [0, 1] ─────────────────────────────────
        if (maxSize > 0)
        {
            for (int i = 0; i < _bidsCount; i++)
                _bidDepthRatios[i] /= maxSize;
            for (int i = 0; i < _asksCount; i++)
                _askDepthRatios[i] /= maxSize;
        }

        // ── Sort arrays in-place (allocation-free) ───────────────────────────
        // Bids: descending (best bid = highest price at index 0)
        Array.Sort<double, double>(_bidPrices, _bidDepthRatios, 0, _bidsCount);
        Array.Reverse(_bidPrices, 0, _bidsCount);
        Array.Reverse(_bidDepthRatios, 0, _bidsCount);

        // Asks: ascending (best ask = lowest price at index 0)
        Array.Sort<double, double>(_askPrices, _askDepthRatios, 0, _asksCount);

        // ── Mid price ────────────────────────────────────────────────────────
        if (_bidsCount > 0 && _asksCount > 0)
            _midPrice = (_bidPrices[0] + _askPrices[0]) * 0.5;
        else if (_bidsCount > 0)
            _midPrice = _bidPrices[0];
        else if (_asksCount > 0)
            _midPrice = _askPrices[0];
        else
            _midPrice = 0;

        // ── Visible price range with 5 % margin on each side ────────────────
        double lowestBid = _bidsCount > 0 ? _bidPrices[_bidsCount - 1] : _midPrice - 1.0;
        double highestAsk = _asksCount > 0 ? _askPrices[_asksCount - 1] : _midPrice + 1.0;
        double span = highestAsk - lowestBid;
        double margin = span > 0 ? span * 0.05 : 0.5;
        _priceMin = lowestBid - margin;
        _priceMax = highestAsk + margin;

        UpdatePriceLabels();
    }

    // ── Hot path — NO allocations, NO LINQ ────────────────────────────────────

    /// <summary>
    /// Fills <paramref name="pixelBuffer"/> with Bgra32 pixel data for the current
    /// order book snapshot. Called every frame from the DispatcherTimer on the UI thread.
    /// Expected: &lt; 1 ms for a 400 × 200 buffer with 20 levels.
    /// </summary>
    public void RenderFrame(int[] pixelBuffer, int width, int height)
    {
        int bg = ColorBackground;
        int mid = ColorMidLine;

        double priceRange = _priceMax - _priceMin;

        // ── No data — fill background and return ─────────────────────────────
        if ((_bidsCount == 0 && _asksCount == 0) || priceRange <= 0 || _midPrice <= 0)
        {
            int total = width * height;
            for (int i = 0; i < total; i++)
                pixelBuffer[i] = bg;
            return;
        }

        // ── Pre-compute mid-price row (clamped) ───────────────────────────────
        int midRow = (int)((_priceMax - _midPrice) / priceRange * height);
        if (midRow < 0)
            midRow = 0;
        if (midRow >= height)
            midRow = height - 1;

        // ── Render each row ───────────────────────────────────────────────────
        for (int y = 0; y < height; y++)
        {
            // y=0 → priceMax (top / highest ask), y=height-1 → priceMin (bottom / lowest bid)
            double price = _priceMax - (double)y / height * priceRange;
            int rowOffset = y * width;
            int pixelColor;

            if (y == midRow)
            {
                // Mid-price divider line - amber
                pixelColor = mid;
            }
            else if (price < _midPrice)
            {
                // ── Bid side (green) ─────────────────────────────────────────
                // Find deepest bid level at or above this price.
                // _bidPrices sorted descending: scan forward, keep updating while >= price.
                int matchIdx = -1;
                for (int i = 0; i < _bidsCount; i++)
                {
                    if (_bidPrices[i] >= price)
                        matchIdx = i;
                    else
                        break;
                }

                if (matchIdx < 0)
                {
                    pixelColor = bg; // in the spread gap above best bid
                }
                else
                {
                    double ratio = _bidDepthRatios[matchIdx];
                    if (ratio < 0.01)
                    {
                        pixelColor = bg;
                    }
                    else
                    {
                        // Mint bid: #26BF86 scaled by depth ratio.
                        int r = (int)(ratio * 38);
                        int g = (int)(ratio * 191);
                        int b = (int)(ratio * 134);
                        pixelColor = unchecked((int)((255u << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b));
                    }
                }
            }
            else
            {
                // ── Ask side (red) ───────────────────────────────────────────
                // Find deepest ask level at or below this price.
                // _askPrices sorted ascending: scan forward, keep updating while <= price.
                int matchIdx = -1;
                for (int i = 0; i < _asksCount; i++)
                {
                    if (_askPrices[i] <= price)
                        matchIdx = i;
                    else
                        break;
                }

                if (matchIdx < 0)
                {
                    pixelColor = bg; // in the spread gap below best ask
                }
                else
                {
                    double ratio = _askDepthRatios[matchIdx];
                    if (ratio < 0.01)
                    {
                        pixelColor = bg;
                    }
                    else
                    {
                        // Coral ask: #DE5878 scaled by depth ratio.
                        int r = (int)(ratio * 222);
                        int g = (int)(ratio * 88);
                        int b = (int)(ratio * 120);
                        pixelColor = unchecked((int)((255u << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b));
                    }
                }
            }

            // ── Fill entire row with computed color ───────────────────────────
            for (int x = 0; x < width; x++)
                pixelBuffer[rowOffset + x] = pixelColor;
        }
    }

    // ── Price labels ───────────────────────────────────────────────────────────

    private void UpdatePriceLabels()
    {
        const int count = 5;
        PriceLabels.Clear();

        if (_priceMax <= _priceMin || _controlHeight <= 0)
            return;

        double range = _priceMax - _priceMin;
        double usableHeight = _controlHeight - 14.0; // reserve one label-height at bottom

        for (int i = 0; i < count; i++)
        {
            double fraction = (double)i / (count - 1);
            double price = _priceMax - fraction * range;
            PriceLabels.Add(new PriceLabelViewModel
            {
                Price = price.ToString("N2"),
                Y = Math.Max(0, fraction * usableHeight)
            });
        }
    }
}
