using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Views;

public partial class SplitPaneHostControl : UserControl
{
    private readonly List<Frame> _paneFrames = new();
    public const string PageTagFormat = "Meridian.PageTag";

    /// <summary>
    /// Raised when a page-tag string is dropped onto this host.
    /// </summary>
    public event EventHandler<PaneDropEventArgs>? PaneDropRequested;

    public PaneLayout Layout
    {
        get => (PaneLayout)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public static readonly DependencyProperty LayoutProperty =
        DependencyProperty.Register(nameof(Layout), typeof(PaneLayout),
            typeof(SplitPaneHostControl),
            new PropertyMetadata(PaneLayouts.Single, OnLayoutChanged));

    public int ActivePaneIndex { get; set; }

    public SplitPaneHostControl()
    {
        InitializeComponent();
        RebuildPanes(PaneLayouts.Single);
    }

    public Frame? GetPaneFrame(int index) =>
        index >= 0 && index < _paneFrames.Count ? _paneFrames[index] : null;

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is PaneLayout layout)
            ((SplitPaneHostControl)d).RebuildPanes(layout);
    }

    private void RebuildPanes(PaneLayout layout)
    {
        ContentGrid.ColumnDefinitions.Clear();
        ContentGrid.Children.Clear();
        _paneFrames.Clear();

        for (int i = 0; i < layout.PaneCount; i++)
        {
            ContentGrid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = layout.ColumnWidths[i] });

            var frame = new Frame
            {
                NavigationUIVisibility = NavigationUIVisibility.Hidden,
                Background = new SolidColorBrush(Colors.Transparent)
            };
            Grid.SetColumn(frame, ContentGrid.ColumnDefinitions.Count - 1);
            ContentGrid.Children.Add(frame);
            _paneFrames.Add(frame);

            if (i < layout.PaneCount - 1)
            {
                ContentGrid.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(4) });

                var splitter = new GridSplitter
                {
                    Width = 4,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Background = TryFindResource("CommandBarBorderBrush") as Brush
                        ?? new SolidColorBrush(Color.FromRgb(64, 64, 64)),
                    Cursor = System.Windows.Input.Cursors.SizeWE,
                    ShowsPreview = true
                };
                Grid.SetColumn(splitter, ContentGrid.ColumnDefinitions.Count - 1);
                ContentGrid.Children.Add(splitter);
            }
        }

        // Re-span the overlay across all columns after rebuilding panes
        Grid.SetColumnSpan(DropOverlay, Math.Max(1, ContentGrid.ColumnDefinitions.Count));
    }

    // ── Drag-and-drop ────────────────────────────────────────────────────────

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (TryGetPageTag(e.Data, out _))
        {
            e.Effects = DragDropEffects.Move;
            DropOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }

        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;

        if (!TryGetPageTag(e.Data, out var pageTag))
        {
            e.Handled = true;
            return;
        }

        var dropPosition = e.GetPosition(this);
        var targetIndex = HitTestPaneIndex(dropPosition);
        PaneDropRequested?.Invoke(this, new PaneDropEventArgs(pageTag, targetIndex));
        e.Handled = true;
    }

    /// <summary>
    /// Returns the zero-based pane index that contains <paramref name="position"/>.
    /// Falls back to <see cref="ActivePaneIndex"/> when no frame is hit.
    /// </summary>
    private int HitTestPaneIndex(Point position)
    {
        for (int i = 0; i < _paneFrames.Count; i++)
        {
            var frame = _paneFrames[i];
            var bounds = new Rect(
                frame.TranslatePoint(new Point(0, 0), this),
                new Size(frame.ActualWidth, frame.ActualHeight));

            if (bounds.Contains(position))
                return i;
        }
        return ActivePaneIndex;
    }

    private static bool TryGetPageTag(IDataObject data, out string pageTag)
    {
        if (TryReadString(data, PageTagFormat, out pageTag) ||
            TryReadString(data, DataFormats.StringFormat, out pageTag))
        {
            return true;
        }

        pageTag = string.Empty;
        return false;
    }

    private static bool TryReadString(IDataObject data, string format, out string value)
    {
        if (data.GetDataPresent(format) &&
            data.GetData(format) is string text &&
            !string.IsNullOrWhiteSpace(text))
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
