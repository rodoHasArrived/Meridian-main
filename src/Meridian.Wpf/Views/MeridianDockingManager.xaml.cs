using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AvalonDock;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;
using Meridian.Ui.Services;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Views;

/// <summary>
/// A UserControl that wraps an AvalonDock <see cref="DockingManager"/> to provide
/// IDE-style floating, pinnable, and tear-off panes for the Trading and Research
/// workspace shells.
///
/// Key surface:
/// <list type="bullet">
///   <item><c>LoadPage</c> — adds or activates a FrameworkElement in a named document pane.</item>
///   <item><see cref="SaveLayout"/> — serialises the current dock layout to XML.</item>
///   <item><see cref="LoadLayout"/> — restores a previously serialised layout.</item>
/// </list>
///
/// Drag-and-drop page-tag protocol is preserved: drop a string tagged with
/// <c>SplitPaneHostControl.PageTagFormat</c> to call <c>LoadPage</c>.
/// </summary>
public partial class MeridianDockingManager : UserControl
{
    private sealed class DockedPageDescriptor
    {
        public required string PageKey { get; init; }
        public required string PageTag { get; init; }
        public required string Title { get; init; }
        public required PaneDropAction Action { get; set; }
        public required LayoutDocument Document { get; init; }
    }

    /// <summary>
    /// Raised when a page-tag string is dropped onto this host.
    /// The handler should call <c>LoadPage</c> with the resolved content.
    /// </summary>
    public event EventHandler<PaneDropEventArgs>? PaneDropRequested;

    // Track open documents by page key so we can activate rather than duplicate and
    // preserve requested dock zones for layout persistence.
    private readonly Dictionary<string, DockedPageDescriptor> _openDocuments = new(StringComparer.OrdinalIgnoreCase);

    public MeridianDockingManager()
    {
        InitializeComponent();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a new document pane containing <paramref name="content"/> with the specified
    /// <paramref name="title"/>, or activates an existing one with that title.
    /// </summary>
    /// <param name="title">Display name shown in the document tab.</param>
    /// <param name="content">The <see cref="FrameworkElement"/> to host (typically a Page).</param>
    public void LoadPage(string title, FrameworkElement content)
    {
        LoadPage(title, title, content, PaneDropAction.OpenTab);
    }

    /// <summary>
    /// Adds or activates a workstation page using the richer dock action surface used by
    /// the workstation shell pages.
    /// </summary>
    public void LoadPage(string pageKey, string title, FrameworkElement content, PaneDropAction action)
    {
        var documentContent = NormalizeDocumentContent(content);

        if (_openDocuments.TryGetValue(pageKey, out var existing))
        {
            if (WorkspaceShellFallbackContentFactory.IsFallbackContent(existing.Document.Content))
            {
                existing.Document.Title = title;
                existing.Document.Content = documentContent;
            }

            existing.Document.IsActive = true;
            existing.Action = action;
            return;
        }

        if (action == PaneDropAction.Replace)
        {
            ClearDocuments();
        }

        var document = new LayoutDocument
        {
            Title = title,
            Content = documentContent,
            CanClose = true,
            IsActive = true
        };

        document.Closed += (_, _) => _openDocuments.Remove(pageKey);

        PrimaryDocumentPane.Children.Add(document);
        _openDocuments[pageKey] = new DockedPageDescriptor
        {
            PageKey = pageKey,
            PageTag = ExtractPageTag(pageKey),
            Title = title,
            Action = action,
            Document = document
        };
    }

    /// <summary>
    /// Serialises the current dock layout to an XML string suitable for persistence.
    /// Returns <c>null</c> if serialisation fails.
    /// </summary>
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

    /// <summary>
    /// Restores a dock layout from a previously serialised XML string.
    /// No-ops gracefully if <paramref name="layoutXml"/> is null or empty.
    /// </summary>
    public void LoadLayout(string? layoutXml)
    {
        if (string.IsNullOrWhiteSpace(layoutXml))
            return;

        try
        {
            using var reader = new StringReader(layoutXml);
            var serializer = new XmlLayoutSerializer(DockManager);
            serializer.Deserialize(reader);
        }
        catch
        {
            // Silently fall back to default layout on corrupt / stale XML.
        }
    }

    /// <summary>
    /// Captures the current workstation docking state into the shared persistence model.
    /// </summary>
    public WorkstationLayoutState CaptureLayoutState(string layoutId, string displayName)
    {
        var panes = _openDocuments.Values
            .Select((descriptor, index) => new WorkstationPaneState
            {
                PaneId = descriptor.PageKey,
                PageTag = descriptor.PageTag,
                Title = descriptor.Title,
                DockZone = ToDockZone(descriptor.Action),
                IsActive = descriptor.Document.IsActive,
                Order = index
            })
            .ToList();

        return new WorkstationLayoutState
        {
            LayoutId = layoutId,
            DisplayName = displayName,
            ActivePaneId = panes.FirstOrDefault(static pane => pane.IsActive)?.PaneId ?? panes.FirstOrDefault()?.PaneId ?? "pane-1",
            DockLayoutXml = SaveLayout(),
            Panes = panes,
            FloatingWindows = _openDocuments.Values
                .Where(static descriptor => descriptor.Action == PaneDropAction.FloatWindow)
                .Select(descriptor => new FloatingWorkspaceWindowState
                {
                    WindowId = descriptor.PageKey,
                    PaneId = descriptor.PageKey,
                    Title = descriptor.Title,
                    IsOpen = true
                })
                .ToList(),
            SavedAt = DateTime.UtcNow
        };
    }

    // ── Drag-and-drop ─────────────────────────────────────────────────────────

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(SplitPaneHostControl.PageTagFormat))
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
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;

        if (e.Data.GetData(SplitPaneHostControl.PageTagFormat) is string pageTag)
        {
            PaneDropRequested?.Invoke(this, new PaneDropEventArgs(pageTag, 0, PaneDropAction.Replace));
        }

        e.Handled = true;
    }

    private void ClearDocuments()
    {
        PrimaryDocumentPane.Children.Clear();
        _openDocuments.Clear();
    }

    private static string ExtractPageTag(string pageKey)
    {
        var separatorIndex = pageKey.IndexOf(':');
        return separatorIndex >= 0 ? pageKey[..separatorIndex] : pageKey;
    }

    private static FrameworkElement NormalizeDocumentContent(FrameworkElement content)
    {
        if (content is not Page page)
        {
            return content;
        }

        var frame = new Frame
        {
            NavigationUIVisibility = System.Windows.Navigation.NavigationUIVisibility.Hidden,
            Focusable = false,
            Background = Brushes.Transparent
        };

        frame.Navigate(page);
        return frame;
    }

    private static string ToDockZone(PaneDropAction action) => action switch
    {
        PaneDropAction.SplitLeft => "left",
        PaneDropAction.SplitRight => "right",
        PaneDropAction.SplitBelow => "bottom",
        PaneDropAction.FloatWindow => "floating",
        _ => "document"
    };
}
