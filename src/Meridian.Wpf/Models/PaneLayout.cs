using System.Collections.Generic;
using System.Windows;

namespace Meridian.Wpf.Models;

public enum PaneLayoutKind { Single, ResearchData, TradingCockpit }

public sealed record PaneLayout(
    PaneLayoutKind Kind,
    string Label,
    string Icon,
    int PaneCount,
    GridLength[] ColumnWidths);

public static class PaneLayouts
{
    public static readonly PaneLayout Single = new(
        PaneLayoutKind.Single, "Single", "□", 1,
        new[] { new GridLength(1, GridUnitType.Star) });

    public static readonly PaneLayout ResearchData = new(
        PaneLayoutKind.ResearchData, "Research + Data", "⬛⬜", 2,
        new[] { new GridLength(1, GridUnitType.Star), new GridLength(1, GridUnitType.Star) });

    public static readonly PaneLayout TradingCockpit = new(
        PaneLayoutKind.TradingCockpit, "Trading Cockpit", "⬛⬜⬜", 3,
        new[] { new GridLength(2, GridUnitType.Star), new GridLength(1, GridUnitType.Star), new GridLength(1, GridUnitType.Star) });

    public static readonly IReadOnlyList<PaneLayout> All = new[] { Single, ResearchData, TradingCockpit };
}
