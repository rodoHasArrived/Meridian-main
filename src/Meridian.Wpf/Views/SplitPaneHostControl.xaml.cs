using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Views;

public partial class SplitPaneHostControl : UserControl
{
    private readonly List<Frame> _paneFrames = [];
    private readonly List<Border> _paneHosts = [];
    private readonly Dictionary<PaneDropAction, Border> _dropTargets = [];

    public const string PageTagFormat = "Meridian.PageTag";

    public event EventHandler<PaneDropEventArgs>? PaneDropRequested;
    public event EventHandler<int>? PaneActivated;

    public PaneLayout Layout
    {
        get => (PaneLayout)GetValue(LayoutProperty);
        set => SetValue(LayoutProperty, value);
    }

    public static readonly DependencyProperty LayoutProperty =
        DependencyProperty.Register(
            nameof(Layout),
            typeof(PaneLayout),
            typeof(SplitPaneHostControl),
            new PropertyMetadata(PaneLayouts.Single, OnLayoutChanged));

    public int ActivePaneIndex { get; set; }

    public SplitPaneHostControl()
    {
        InitializeComponent();
        _dropTargets[PaneDropAction.Replace] = ReplaceTarget;
        _dropTargets[PaneDropAction.SplitLeft] = SplitLeftTarget;
        _dropTargets[PaneDropAction.SplitRight] = SplitRightTarget;
        _dropTargets[PaneDropAction.SplitBelow] = SplitBelowTarget;
        _dropTargets[PaneDropAction.OpenTab] = OpenTabTarget;
        _dropTargets[PaneDropAction.FloatWindow] = FloatTarget;
        RebuildPanes(PaneLayouts.Single);
    }

    public Frame? GetPaneFrame(int index) =>
        index >= 0 && index < _paneFrames.Count ? _paneFrames[index] : null;

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is PaneLayout layout)
        {
            ((SplitPaneHostControl)d).RebuildPanes(layout);
        }
    }

    private void RebuildPanes(PaneLayout layout)
    {
        ContentGrid.RowDefinitions.Clear();
        ContentGrid.ColumnDefinitions.Clear();
        ContentGrid.Children.Clear();
        _paneFrames.Clear();
        _paneHosts.Clear();

        foreach (var rowHeight in layout.RowHeights)
        {
            ContentGrid.RowDefinitions.Add(new RowDefinition { Height = rowHeight });
        }

        foreach (var columnWidth in layout.ColumnWidths)
        {
            ContentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = columnWidth });
        }

        for (var columnIndex = 1; columnIndex < ContentGrid.ColumnDefinitions.Count; columnIndex++)
        {
            var splitter = new GridSplitter
            {
                Width = 4,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = TryFindResource("CommandBarBorderBrush") as Brush
                    ?? new SolidColorBrush(Color.FromRgb(64, 64, 64)),
                Cursor = Cursors.SizeWE,
                ShowsPreview = true,
                Margin = new Thickness(-2, 0, 0, 0)
            };
            Grid.SetColumn(splitter, columnIndex);
            Grid.SetRowSpan(splitter, Math.Max(1, ContentGrid.RowDefinitions.Count));
            Panel.SetZIndex(splitter, 10);
            ContentGrid.Children.Add(splitter);
        }

        for (var rowIndex = 1; rowIndex < ContentGrid.RowDefinitions.Count; rowIndex++)
        {
            var splitter = new GridSplitter
            {
                Height = 4,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top,
                Background = TryFindResource("CommandBarBorderBrush") as Brush
                    ?? new SolidColorBrush(Color.FromRgb(64, 64, 64)),
                Cursor = Cursors.SizeNS,
                ShowsPreview = true,
                Margin = new Thickness(0, -2, 0, 0)
            };
            Grid.SetRow(splitter, rowIndex);
            Grid.SetColumnSpan(splitter, Math.Max(1, ContentGrid.ColumnDefinitions.Count));
            Panel.SetZIndex(splitter, 10);
            ContentGrid.Children.Add(splitter);
        }

        foreach (var (slot, index) in layout.Slots.Select((slot, index) => (slot, index)))
        {
            var frame = new Frame
            {
                NavigationUIVisibility = NavigationUIVisibility.Hidden,
                Background = Brushes.Transparent
            };

            var host = new Border
            {
                Margin = new Thickness(8),
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(12),
                Background = TryFindResource("ConsoleBackgroundMediumBrush") as Brush
                    ?? Brushes.White,
                BorderBrush = index == ActivePaneIndex
                    ? (TryFindResource("ConsoleAccentBlueBrush") as Brush ?? Brushes.DodgerBlue)
                    : (TryFindResource("ConsoleBorderBrush") as Brush ?? Brushes.Gray),
                BorderThickness = new Thickness(index == ActivePaneIndex ? 2 : 1),
                Child = frame
            };
            host.MouseLeftButtonDown += (_, _) => ActivatePane(index);

            Grid.SetRow(host, slot.Row);
            Grid.SetColumn(host, slot.Column);
            Grid.SetRowSpan(host, slot.RowSpan);
            Grid.SetColumnSpan(host, slot.ColumnSpan);

            ContentGrid.Children.Add(host);
            _paneFrames.Add(frame);
            _paneHosts.Add(host);
        }
    }

    private void ActivatePane(int index)
    {
        ActivePaneIndex = index;
        for (var i = 0; i < _paneHosts.Count; i++)
        {
            _paneHosts[i].BorderBrush = i == index
                ? (TryFindResource("ConsoleAccentBlueBrush") as Brush ?? Brushes.DodgerBlue)
                : (TryFindResource("ConsoleBorderBrush") as Brush ?? Brushes.Gray);
            _paneHosts[i].BorderThickness = new Thickness(i == index ? 2 : 1);
        }

        PaneActivated?.Invoke(this, index);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (TryGetPageTag(e.Data, out _))
        {
            e.Effects = DragDropEffects.Move;
            DropOverlay.Visibility = Visibility.Visible;
            DropOverlay.IsHitTestVisible = true;
            HighlightDropTarget(HitTestDropAction(e.GetPosition(this)));
        }
        else
        {
            e.Effects = DragDropEffects.None;
            DropOverlay.Visibility = Visibility.Collapsed;
            DropOverlay.IsHitTestVisible = false;
            HighlightDropTarget(null);
        }

        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        DropOverlay.IsHitTestVisible = false;
        HighlightDropTarget(null);
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        DropOverlay.IsHitTestVisible = false;
        HighlightDropTarget(null);

        if (!TryGetPageTag(e.Data, out var pageTag))
        {
            e.Handled = true;
            return;
        }

        var dropPosition = e.GetPosition(this);
        var targetIndex = HitTestPaneIndex(dropPosition);
        var action = HitTestDropAction(dropPosition);
        PaneDropRequested?.Invoke(this, new PaneDropEventArgs(pageTag, targetIndex, action));
        e.Handled = true;
    }

    private int HitTestPaneIndex(Point position)
    {
        for (var i = 0; i < _paneFrames.Count; i++)
        {
            var frame = _paneFrames[i];
            var bounds = new Rect(
                frame.TranslatePoint(new Point(0, 0), this),
                new Size(frame.ActualWidth, frame.ActualHeight));

            if (bounds.Contains(position))
            {
                return i;
            }
        }

        return ActivePaneIndex;
    }

    private PaneDropAction HitTestDropAction(Point position)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return PaneDropAction.Replace;
        }

        var x = position.X / ActualWidth;
        var y = position.Y / ActualHeight;

        if (y < 0.22 && x > 0.58)
        {
            return PaneDropAction.FloatWindow;
        }

        if (y < 0.22 && x is > 0.32 and < 0.68)
        {
            return PaneDropAction.OpenTab;
        }

        if (x < 0.24)
        {
            return PaneDropAction.SplitLeft;
        }

        if (x > 0.76)
        {
            return PaneDropAction.SplitRight;
        }

        if (y > 0.72)
        {
            return PaneDropAction.SplitBelow;
        }

        return PaneDropAction.Replace;
    }

    private void HighlightDropTarget(PaneDropAction? action)
    {
        foreach (var (dropAction, target) in _dropTargets)
        {
            var isActive = action.HasValue && dropAction == action.Value;
            target.Background = isActive ? new SolidColorBrush(Color.FromArgb(255, 42, 92, 160)) : new SolidColorBrush(Color.FromArgb(255, 16, 38, 61));
            target.BorderBrush = isActive ? new SolidColorBrush(Color.FromArgb(255, 111, 181, 255)) : new SolidColorBrush(Color.FromArgb(255, 54, 90, 132));
        }
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
