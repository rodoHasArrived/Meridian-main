using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Meridian.Wpf.Models;

/// <summary>
/// Display model for watchlist cards.
/// </summary>
public sealed class WatchlistDisplayModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SymbolCount { get; set; } = string.Empty;
    public int SymbolTotal { get; set; }
    public string? Color { get; set; }
    public Color ColorValue { get; set; }
    public bool IsPinned { get; set; }
    public Visibility PinnedBadgeVisibility => IsPinned ? Visibility.Visible : Visibility.Collapsed;
    public List<string> SymbolsPreview { get; set; } = new();
    public string ModifiedText { get; set; } = string.Empty;
}
