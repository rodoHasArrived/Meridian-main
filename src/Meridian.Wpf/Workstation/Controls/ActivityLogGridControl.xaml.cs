using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Workstation.Controls;

public partial class ActivityLogGridControl : UserControl
{
    public static readonly DependencyProperty TimelineProperty =
        DependencyProperty.Register(
            nameof(Timeline),
            typeof(AuditTimelineModel),
            typeof(ActivityLogGridControl),
            new PropertyMetadata(null, OnTimelineChanged));

    public static readonly DependencyProperty IsTimelineEmptyProperty =
        DependencyProperty.Register(
            nameof(IsTimelineEmpty),
            typeof(bool),
            typeof(ActivityLogGridControl),
            new PropertyMetadata(true));

    private INotifyCollectionChanged? _entries;

    public ActivityLogGridControl()
    {
        InitializeComponent();
    }

    public AuditTimelineModel? Timeline
    {
        get => (AuditTimelineModel?)GetValue(TimelineProperty);
        set => SetValue(TimelineProperty, value);
    }

    public bool IsTimelineEmpty
    {
        get => (bool)GetValue(IsTimelineEmptyProperty);
        private set => SetValue(IsTimelineEmptyProperty, value);
    }

    private static void OnTimelineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ActivityLogGridControl control)
        {
            control.AttachEntries();
            control.NotifyEmptyChanged();
        }
    }

    private void AttachEntries()
    {
        if (_entries is not null)
        {
            _entries.CollectionChanged -= OnEntriesChanged;
        }

        _entries = Timeline?.Entries;
        if (_entries is not null)
        {
            _entries.CollectionChanged += OnEntriesChanged;
        }
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => NotifyEmptyChanged();

    private void NotifyEmptyChanged()
        => IsTimelineEmpty = Timeline?.Entries.Count is null or 0;
}
