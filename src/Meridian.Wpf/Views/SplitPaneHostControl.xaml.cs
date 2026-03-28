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
    }
}
