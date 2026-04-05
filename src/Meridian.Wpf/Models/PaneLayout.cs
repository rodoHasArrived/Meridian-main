using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace Meridian.Wpf.Models;

public enum PaneLayoutKind
{
    Single,
    ResearchStudio,
    TradingCockpit,
    Custom
}

public sealed record PaneLayoutSlot(
    string PaneId,
    int Row,
    int Column,
    int RowSpan = 1,
    int ColumnSpan = 1,
    string? Header = null);

public sealed record PaneLayout(
    PaneLayoutKind Kind,
    string LayoutId,
    string Label,
    string Icon,
    GridLength[] ColumnWidths,
    GridLength[] RowHeights,
    IReadOnlyList<PaneLayoutSlot> Slots)
{
    public int PaneCount => Slots.Count;

    public PaneLayout CloneAs(
        PaneLayoutKind? kind = null,
        string? layoutId = null,
        string? label = null,
        string? icon = null,
        GridLength[]? columnWidths = null,
        GridLength[]? rowHeights = null,
        IReadOnlyList<PaneLayoutSlot>? slots = null)
    {
        return new PaneLayout(
            kind ?? Kind,
            layoutId ?? LayoutId,
            label ?? Label,
            icon ?? Icon,
            columnWidths ?? ColumnWidths.ToArray(),
            rowHeights ?? RowHeights.ToArray(),
            slots ?? Slots.Select(static slot => slot with { }).ToArray());
    }
}

public static class PaneLayouts
{
    public static readonly PaneLayout Single = new(
        PaneLayoutKind.Single,
        "single",
        "Single",
        "□",
        [new GridLength(1, GridUnitType.Star)],
        [new GridLength(1, GridUnitType.Star)],
        [new PaneLayoutSlot("pane-1", 0, 0, Header: "Primary Pane")]);

    public static readonly PaneLayout ResearchStudio = new(
        PaneLayoutKind.ResearchStudio,
        "research-studio",
        "Research Studio",
        "◫",
        [
            new GridLength(1.15, GridUnitType.Star),
            new GridLength(1.85, GridUnitType.Star),
            new GridLength(1.1, GridUnitType.Star)
        ],
        [
            new GridLength(3, GridUnitType.Star),
            new GridLength(1.35, GridUnitType.Star)
        ],
        [
            new PaneLayoutSlot("pane-1", 0, 0, Header: "Scenario"),
            new PaneLayoutSlot("pane-2", 0, 1, Header: "Run Studio"),
            new PaneLayoutSlot("pane-3", 0, 2, Header: "Inspector"),
            new PaneLayoutSlot("pane-4", 1, 0, 1, 3, "History Rail")
        ]);

    public static readonly PaneLayout TradingCockpit = new(
        PaneLayoutKind.TradingCockpit,
        "trading-cockpit",
        "Trading Cockpit",
        "⬒",
        [
            new GridLength(1.15, GridUnitType.Star),
            new GridLength(1.8, GridUnitType.Star),
            new GridLength(1.2, GridUnitType.Star)
        ],
        [
            new GridLength(3, GridUnitType.Star),
            new GridLength(1.2, GridUnitType.Star)
        ],
        [
            new PaneLayoutSlot("pane-1", 0, 0, Header: "Strategies"),
            new PaneLayoutSlot("pane-2", 0, 1, Header: "Market Core"),
            new PaneLayoutSlot("pane-3", 0, 2, Header: "Risk Rail"),
            new PaneLayoutSlot("pane-4", 1, 0, 1, 3, "Execution Activity")
        ]);

    public static readonly IReadOnlyList<PaneLayout> All = [Single, ResearchStudio, TradingCockpit];
}
