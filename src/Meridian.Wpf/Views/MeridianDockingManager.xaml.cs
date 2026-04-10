using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using AvalonDock;
using AvalonDock.Layout;
using AvalonDock.Layout.Serialization;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.Views;

/// <summary>
/// A UserControl that wraps an AvalonDock <see cref="DockingManager"/> to provide
/// IDE-style floating, pinnable, and tear-off panes for the Trading and Research
/// workspace shells.
///
/// Key surface:
/// <list type="bullet">
///   <item><see cref="LoadPage"/> — adds or activates a FrameworkElement in a named document pane.</item>
///   <item><see cref="SaveLayout"/> — serialises the current dock layout to XML.</item>
///   <item><see cref="LoadLayout"/> — restores a previously serialised layout.</item>
/// </list>
///
/// Drag-and-drop page-tag protocol is preserved: drop a string tagged with
/// <c>SplitPaneHostControl.PageTagFormat</c> to call <see cref="LoadPage"/>.
/// </summary>
public partial class MeridianDockingManager : UserControl
{
    /// <summary>
    /// Raised when a page-tag string is dropped onto this host.
    /// The handler should call <see cref="LoadPage"/> with the resolved content.
    /// </summary>
    public event EventHandler<PaneDropEventArgs>? PaneDropRequested;

    // Track open documents by title so we can activate rather than duplicate.
    private readonly Dictionary<string, LayoutDocument> _openDocuments = new(StringComparer.OrdinalIgnoreCase);

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
        if (_openDocuments.TryGetValue(title, out var existing))
        {
            existing.IsActive = true;
            return;
        }

        var document = new LayoutDocument
        {
            Title = title,
            Content = content,
            CanClose = true,
            IsActive = true
        };

        document.Closed += (_, _) => _openDocuments.Remove(title);

        PrimaryDocumentPane.Children.Add(document);
        _openDocuments[title] = document;
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
            PaneDropRequested?.Invoke(this, new PaneDropEventArgs(pageTag, 0));
        }

        e.Handled = true;
    }
}
