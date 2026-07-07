using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Meridian.Wpf.ViewModels;

namespace Meridian.Wpf.Views;

/// <summary>
/// Code-behind for the L2 order-book depth heatmap.
///
/// Architecture notes:
/// • <see cref="WriteableBitmap"/> is created once (and recreated only on resize) — no GC pressure.
/// • <see cref="_pixelBuffer"/> (int[]) is reused every frame; filled by
///   <see cref="OrderBookHeatmapViewModel.RenderFrame"/> and then bulk-copied to the
///   BackBuffer via <c>Marshal.Copy</c>.
/// • The render loop runs at a modest cadence on the UI thread via <see cref="DispatcherTimer"/>
///   and only redraws when the view-model render version changes, so idle ticks avoid bitmap work.
/// • Business logic lives entirely in <see cref="OrderBookHeatmapViewModel"/>;
///   this file is limited to lifecycle + bitmap plumbing.
/// </summary>
public partial class OrderBookHeatmapControl : System.Windows.Controls.UserControl
{
    private WriteableBitmap? _bitmap;
    private int[]? _pixelBuffer;
    private long _lastRenderedVersion = -1;

    private readonly DispatcherTimer _renderTimer;

    public OrderBookHeatmapControl()
    {
        InitializeComponent();

        _renderTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(66) // ~15 fps; dirty-version gated for idle snapshots
        };
        _renderTimer.Tick += OnRenderTick;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Create the initial bitmap if the control is already sized.
        if (ActualWidth > 1 && ActualHeight > 1)
            RecreateBuffer((int)ActualWidth, (int)ActualHeight);

        UpdateRenderTimerState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _renderTimer.Stop();
        _bitmap = null;
        _pixelBuffer = null;
        _lastRenderedVersion = -1;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        int w = (int)e.NewSize.Width;
        int h = (int)e.NewSize.Height;
        if (w > 1 && h > 1)
            RecreateBuffer(w, h);
        else
            ReleaseBuffer();

        UpdateRenderTimerState();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        UpdateRenderTimerState();
    }

    // ── Buffer management ─────────────────────────────────────────────────────

    private void RecreateBuffer(int w, int h)
    {
        if (_bitmap?.PixelWidth == w && _bitmap?.PixelHeight == h)
            return;

        _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        _pixelBuffer = new int[w * h];
        _lastRenderedVersion = -1;
        HeatmapImage.Source = _bitmap;

        (DataContext as OrderBookHeatmapViewModel)?.SetControlHeight(h);
    }

    private void ReleaseBuffer()
    {
        _bitmap = null;
        _pixelBuffer = null;
        _lastRenderedVersion = -1;
        HeatmapImage.Source = null;
    }

    private void UpdateRenderTimerState()
    {
        if (IsLoaded && IsVisible && HasValidBitmapSize())
            _renderTimer.Start();
        else
            _renderTimer.Stop();
    }

    private bool HasValidBitmapSize()
    {
        return _bitmap is { PixelWidth: > 1, PixelHeight: > 1 } && _pixelBuffer is { Length: > 0 };
    }

    internal static bool TryObserveRenderVersion(OrderBookHeatmapViewModel vm, ref long lastRenderedVersion)
    {
        long currentVersion = vm.RenderVersion;
        if (currentVersion == lastRenderedVersion)
            return false;

        lastRenderedVersion = currentVersion;
        return true;
    }

    // ── Render loop ───────────────────────────────────────────────────────────

    private void OnRenderTick(object? sender, EventArgs e)
    {
        var vm = DataContext as OrderBookHeatmapViewModel;
        if (vm is null || _bitmap is null || _pixelBuffer is null || !IsVisible || !HasValidBitmapSize())
        {
            UpdateRenderTimerState();
            return;
        }

        if (!TryObserveRenderVersion(vm, ref _lastRenderedVersion))
            return;

        int w = _bitmap.PixelWidth;
        int h = _bitmap.PixelHeight;

        // Fill pixel buffer — allocation-free, < 1 ms for typical sizes
        vm.RenderFrame(_pixelBuffer, w, h);

        // Bulk-copy int[] → BackBuffer and mark entire surface dirty
        _bitmap.Lock();
        try
        {
            Marshal.Copy(_pixelBuffer, 0, _bitmap.BackBuffer, _pixelBuffer.Length);
            _bitmap.AddDirtyRect(new Int32Rect(0, 0, w, h));
        }
        finally
        {
            _bitmap.Unlock();
        }
    }
}
