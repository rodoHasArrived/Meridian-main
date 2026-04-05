using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Views;

public partial class MeridianDockingManager : UserControl
{
    private readonly Dictionary<string, LayoutContent> _openContents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<PaneDropAction, Border> _dropTargets = new();

    public event EventHandler<PaneDropEventArgs>? PaneDropRequested;

    public MeridianDockingManager()
    {
        InitializeComponent();
        _dropTargets[PaneDropAction.Replace] = ReplaceTarget;
        _dropTargets[PaneDropAction.SplitLeft] = SplitLeftTarget;
        _dropTargets[PaneDropAction.SplitRight] = SplitRightTarget;
        _dropTargets[PaneDropAction.SplitBelow] = SplitBelowTarget;
        _dropTargets[PaneDropAction.OpenTab] = OpenTabTarget;
        _dropTargets[PaneDropAction.FloatWindow] = FloatTarget;
    }

    public void LoadPage(string key, string title, FrameworkElement content, PaneDropAction action = PaneDropAction.Replace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(content);

        var hostedContent = PrepareDockContent(content);

        if (_openContents.TryGetValue(key, out var existingContent))
        {
            existingContent.IsActive = true;
            if (action == PaneDropAction.FloatWindow)
            {
                TryFloat(existingContent);
            }

            return;
        }

        LayoutContent createdContent = action switch
        {
            PaneDropAction.SplitLeft => AddAnchorable(LeftToolPane, key, title, hostedContent),
            PaneDropAction.SplitRight => AddAnchorable(RightToolPane, key, title, hostedContent),
            PaneDropAction.SplitBelow => AddAnchorable(BottomToolPane, key, title, hostedContent),
            PaneDropAction.FloatWindow => AddFloatingDocument(key, title, hostedContent),
            PaneDropAction.OpenTab => AddDocument(key, title, hostedContent),
            _ => AddDocument(key, title, hostedContent)
        };

        _openContents[key] = createdContent;
    }

    public bool ContainsPage(string key) => _openContents.ContainsKey(key);

    public WorkstationLayoutState CaptureLayoutState(string layoutId, string displayName)
    {
        return new WorkstationLayoutState
        {
            LayoutId = layoutId,
            DisplayName = displayName,
            ActivePaneId = _openContents
                .FirstOrDefault(static pair => pair.Value.IsActive)
                .Key ?? "document",
            DockLayoutXml = SaveLayout(),
            SavedAt = DateTime.UtcNow,
            Panes = _openContents
                .Select((pair, order) => new WorkstationPaneState
                {
                    PaneId = pair.Key,
                    PageTag = pair.Value.ContentId ?? pair.Key,
                    Title = pair.Value.Title ?? pair.Key,
                    DockZone = ResolveDockZone(pair.Value),
                    IsToolPane = pair.Value is LayoutAnchorable,
                    IsPinned = pair.Value is LayoutAnchorable anchorable && anchorable.CanAutoHide,
                    IsActive = pair.Value.IsActive,
                    Order = order
                })
                .ToList(),
            FloatingWindows = _openContents
                .Where(static pair => pair.Value.IsFloating)
                .Select(static pair => new FloatingWorkspaceWindowState
                {
                    WindowId = $"window-{pair.Key}",
                    PaneId = pair.Key,
                    Title = pair.Value.Title ?? pair.Key,
                    Bounds = new WindowBounds
                    {
                        Width = pair.Value.FloatingWidth,
                        Height = pair.Value.FloatingHeight,
                        X = pair.Value.FloatingLeft,
                        Y = pair.Value.FloatingTop
                    }
                })
                .ToList()
        };
    }

    public string? SaveLayout()
    {
        try
        {
            using var writer = new StringWriter();
            var serializer = new XmlLayoutSerializer(DockManager);
            serializer.Serialize(writer);
            return writer.ToString();
        }
        catch
        {
            return null;
        }
    }

    public void LoadLayout(string? layoutXml)
    {
        if (string.IsNullOrWhiteSpace(layoutXml))
        {
            return;
        }

        try
        {
            using var reader = new StringReader(layoutXml);
            var serializer = new XmlLayoutSerializer(DockManager);
            serializer.Deserialize(reader);
        }
        catch
        {
            // Fall back to the currently loaded docking state when saved XML is stale.
        }
    }

    private LayoutDocument AddDocument(string key, string title, FrameworkElement content)
    {
        var document = new LayoutDocument
        {
            Title = title,
            ContentId = key,
            Content = content,
            CanClose = true,
            CanFloat = true,
            CanMove = true,
            IsActive = true
        };
        document.Closed += (_, _) => _openContents.Remove(key);
        PrimaryDocumentPane.Children.Add(document);
        return document;
    }

    private LayoutAnchorable AddAnchorable(
        ILayoutContainer targetPane,
        string key,
        string title,
        FrameworkElement content)
    {
        var anchorable = new LayoutAnchorable
        {
            Title = title,
            ContentId = key,
            Content = content,
            CanClose = true,
            CanFloat = true,
            CanDockAsTabbedDocument = true,
            CanHide = false,
            IsActive = true
        };
        anchorable.Closed += (_, _) => _openContents.Remove(key);

        switch (targetPane)
        {
            case LayoutAnchorablePane pane:
                pane.Children.Add(anchorable);
                break;
            case LayoutAnchorablePaneGroup group when group.Children.FirstOrDefault() is LayoutAnchorablePane groupPane:
                groupPane.Children.Add(anchorable);
                break;
        }

        return anchorable;
    }

    private LayoutDocument AddFloatingDocument(string key, string title, FrameworkElement content)
    {
        var document = AddDocument(key, title, content);
        TryFloat(document);
        return document;
    }

    private static void TryFloat(LayoutContent content)
    {
        try
        {
            content.FloatingWidth = Math.Max(840, content.FloatingWidth);
            content.FloatingHeight = Math.Max(560, content.FloatingHeight);
            content.Float();
        }
        catch
        {
        }
    }

    private static FrameworkElement PrepareDockContent(FrameworkElement content)
    {
        if (content is not Page page)
        {
            return content;
        }

        var frame = new Frame
        {
            NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden,
            Background = Brushes.Transparent
        };
        frame.Navigate(page);
        return frame;
    }

    private static string ResolveDockZone(LayoutContent content)
    {
        if (content.IsFloating)
        {
            return "floating";
        }

        return content.Parent switch
        {
            LayoutAnchorablePane pane when string.Equals(pane.Name, nameof(LeftToolPane), StringComparison.Ordinal) => "left",
            LayoutAnchorablePane pane when string.Equals(pane.Name, nameof(RightToolPane), StringComparison.Ordinal) => "right",
            LayoutAnchorablePane pane when string.Equals(pane.Name, nameof(BottomToolPane), StringComparison.Ordinal) => "bottom",
            _ => "document"
        };
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(SplitPaneHostControl.PageTagFormat))
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
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        DropOverlay.IsHitTestVisible = false;
        HighlightDropTarget(null);

        if (e.Data.GetData(SplitPaneHostControl.PageTagFormat) is string pageTag)
        {
            PaneDropRequested?.Invoke(this, new PaneDropEventArgs(pageTag, 0, HitTestDropAction(e.GetPosition(this))));
        }

        e.Handled = true;
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
}
