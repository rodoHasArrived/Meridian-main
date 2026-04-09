using System;
using System.Collections.Generic;
using System.Linq;
using Meridian.Wpf.Models;

namespace Meridian.Wpf.ViewModels;

public sealed partial class SplitPaneViewModel : BindableBase
{
    private readonly List<PaneLayout> _layouts = [.. PaneLayouts.All];
    private readonly List<string?> _panePageTags = [null];
    private readonly HashSet<string> _floatingPaneIds = new(StringComparer.OrdinalIgnoreCase);

    private PaneLayout _selectedLayout = PaneLayouts.Single;
    private int _activePaneIndex;
    private int _customLayoutCounter;

    public PaneLayout SelectedLayout
    {
        get => _selectedLayout;
        private set => SetProperty(ref _selectedLayout, value);
    }

    public int ActivePaneIndex
    {
        get => _activePaneIndex;
        private set => SetProperty(ref _activePaneIndex, value);
    }

    public IReadOnlyList<PaneLayout> Layouts => _layouts;

    public event EventHandler<PaneLayout>? LayoutChanged;
    public event EventHandler<int>? ActivePaneChanged;
    public event EventHandler? PaneAssignmentsChanged;

    [RelayCommand]
    private void SelectLayout(PaneLayout? layout)
    {
        if (layout is null)
        {
            return;
        }

        ApplyLayout(layout.CloneAs());
    }

    [RelayCommand]
    private void SetActivePane(int? index)
    {
        if (index is null || index < 0 || index >= SelectedLayout.PaneCount)
        {
            return;
        }

        ActivePaneIndex = index.Value;
        ActivePaneChanged?.Invoke(this, index.Value);
    }

    [RelayCommand]
    private void SplitPane(string? direction)
    {
        direction = direction?.Trim();
        if (string.IsNullOrWhiteSpace(direction))
        {
            return;
        }

        var activeSlot = SelectedLayout.Slots[Math.Clamp(ActivePaneIndex, 0, SelectedLayout.PaneCount - 1)];
        bool isSplitLeft = direction.Equals("Left", StringComparison.OrdinalIgnoreCase);

        PaneLayout nextLayout = direction.Equals("Below", StringComparison.OrdinalIgnoreCase)
            ? SplitBelow(activeSlot)
            : isSplitLeft
                ? SplitLeft(activeSlot)
                : SplitRight(activeSlot);

        // For SplitLeft the new pane is inserted BEFORE the active pane; for all others it goes after.
        var insertIndex = isSplitLeft
            ? ActivePaneIndex
            : Math.Min(SelectedLayout.PaneCount, ActivePaneIndex + 1);

        _panePageTags.Insert(insertIndex, null);
        ApplyLayout(nextLayout);
        SetActivePane(insertIndex);
    }

    [RelayCommand]
    private void ClosePane(string? paneId)
    {
        if (SelectedLayout.PaneCount <= 1)
        {
            return;
        }

        var targetIndex = ResolvePaneIndex(paneId);
        if (targetIndex < 0)
        {
            return;
        }

        var slots = SelectedLayout.Slots.ToList();
        slots.RemoveAt(targetIndex);
        _panePageTags.RemoveAt(targetIndex);

        var normalized = NormalizeLayout(
            SelectedLayout.CloneAs(
                kind: PaneLayoutKind.Custom,
                layoutId: $"custom-{++_customLayoutCounter}",
                label: "Custom Layout",
                icon: "◫",
                slots: slots));

        ApplyLayout(normalized);
        SetActivePane(Math.Clamp(targetIndex - 1, 0, normalized.PaneCount - 1));
    }

    [RelayCommand]
    private void FocusPane(int? index) => SetActivePane(index);

    [RelayCommand]
    private void MoveTab(string? paneId)
    {
        var targetIndex = ResolvePaneIndex(paneId);
        if (targetIndex >= 0)
        {
            SetActivePane(targetIndex);
        }
    }

    [RelayCommand]
    private void FloatPane(string? paneId)
    {
        var resolvedPaneId = ResolvePaneId(paneId);
        if (resolvedPaneId is null)
        {
            return;
        }

        _floatingPaneIds.Add(resolvedPaneId);
        PaneAssignmentsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void DockPane(string? paneId)
    {
        var resolvedPaneId = ResolvePaneId(paneId);
        if (resolvedPaneId is null)
        {
            return;
        }

        _floatingPaneIds.Remove(resolvedPaneId);
        PaneAssignmentsChanged?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void SaveLayoutPreset()
    {
        var savedLayout = SelectedLayout.CloneAs(
            kind: PaneLayoutKind.Custom,
            layoutId: $"preset-{++_customLayoutCounter}",
            label: $"Custom {_customLayoutCounter}",
            icon: "⧉");

        _layouts.Add(savedLayout);
        RaisePropertyChanged(nameof(Layouts));
    }

    [RelayCommand]
    private void RestoreLayoutPreset(PaneLayout? layout)
    {
        if (layout is null)
        {
            return;
        }

        ApplyLayout(layout.CloneAs());
    }

    [RelayCommand]
    private void ResetLayout()
    {
        _panePageTags.Clear();
        _panePageTags.Add(null);
        ApplyLayout(PaneLayouts.Single.CloneAs());
        SetActivePane(0);
    }

    public void AssignPageToPane(string pageTag, int? index = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageTag);

        var targetIndex = index ?? ActivePaneIndex;
        if (targetIndex < 0)
        {
            targetIndex = 0;
        }

        EnsurePaneAssignments(SelectedLayout.PaneCount);
        _panePageTags[targetIndex] = pageTag;
        SetActivePane(targetIndex);
        PaneAssignmentsChanged?.Invoke(this, EventArgs.Empty);
    }

    public string? GetAssignedPageTag(int index)
    {
        EnsurePaneAssignments(SelectedLayout.PaneCount);
        return index >= 0 && index < _panePageTags.Count
            ? _panePageTags[index]
            : null;
    }

    public string? GetActivePaneId()
        => ActivePaneIndex >= 0 && ActivePaneIndex < SelectedLayout.Slots.Count
            ? SelectedLayout.Slots[ActivePaneIndex].PaneId
            : null;

    public bool IsPaneFloating(string paneId) => _floatingPaneIds.Contains(paneId);

    private void ApplyLayout(PaneLayout layout)
    {
        var normalized = NormalizeLayout(layout);
        EnsurePaneAssignments(normalized.PaneCount);
        SelectedLayout = normalized;
        LayoutChanged?.Invoke(this, normalized);
        PaneAssignmentsChanged?.Invoke(this, EventArgs.Empty);
    }

    private PaneLayout SplitLeft(PaneLayoutSlot activeSlot)
    {
        var columnInsertIndex = activeSlot.Column;
        var slots = new List<PaneLayoutSlot>();

        foreach (var slot in SelectedLayout.Slots)
        {
            var adjusted = slot.Column >= columnInsertIndex
                ? slot with { Column = slot.Column + 1 }
                : slot;
            slots.Add(adjusted);
        }

        slots.Insert(
            Math.Max(0, ActivePaneIndex),
            new PaneLayoutSlot(
                PaneId: $"pane-{slots.Count + 1}",
                Row: activeSlot.Row,
                Column: columnInsertIndex,
                RowSpan: activeSlot.RowSpan,
                ColumnSpan: 1,
                Header: "Split Pane"));

        var columnWidths = Enumerable.Range(0, SelectedLayout.ColumnWidths.Length + 1)
            .Select(_ => new System.Windows.GridLength(1, System.Windows.GridUnitType.Star))
            .ToArray();

        return SelectedLayout.CloneAs(
            kind: PaneLayoutKind.Custom,
            layoutId: $"custom-{++_customLayoutCounter}",
            label: "Custom Layout",
            icon: "◫",
            columnWidths: columnWidths,
            slots: slots);
    }

    private PaneLayout SplitRight(PaneLayoutSlot activeSlot)
    {
        var columnInsertIndex = activeSlot.Column + activeSlot.ColumnSpan;
        var slots = new List<PaneLayoutSlot>();

        foreach (var slot in SelectedLayout.Slots)
        {
            var adjusted = slot.Column >= columnInsertIndex
                ? slot with { Column = slot.Column + 1 }
                : slot;
            slots.Add(adjusted);
        }

        slots.Insert(
            Math.Min(ActivePaneIndex + 1, slots.Count),
            new PaneLayoutSlot(
                PaneId: $"pane-{slots.Count + 1}",
                Row: activeSlot.Row,
                Column: columnInsertIndex,
                RowSpan: activeSlot.RowSpan,
                ColumnSpan: 1,
                Header: "Split Pane"));

        var columnWidths = Enumerable.Range(0, SelectedLayout.ColumnWidths.Length + 1)
            .Select(_ => new System.Windows.GridLength(1, System.Windows.GridUnitType.Star))
            .ToArray();

        return SelectedLayout.CloneAs(
            kind: PaneLayoutKind.Custom,
            layoutId: $"custom-{++_customLayoutCounter}",
            label: "Custom Layout",
            icon: "◫",
            columnWidths: columnWidths,
            slots: slots);
    }

    private PaneLayout SplitBelow(PaneLayoutSlot activeSlot)
    {
        var rowInsertIndex = activeSlot.Row + activeSlot.RowSpan;
        var slots = new List<PaneLayoutSlot>();

        foreach (var slot in SelectedLayout.Slots)
        {
            var adjusted = slot.Row >= rowInsertIndex
                ? slot with { Row = slot.Row + 1 }
                : slot;
            slots.Add(adjusted);
        }

        slots.Insert(
            Math.Min(ActivePaneIndex + 1, slots.Count),
            new PaneLayoutSlot(
                PaneId: $"pane-{slots.Count + 1}",
                Row: rowInsertIndex,
                Column: activeSlot.Column,
                RowSpan: 1,
                ColumnSpan: activeSlot.ColumnSpan,
                Header: "Bottom Split"));

        var rowHeights = Enumerable.Range(0, SelectedLayout.RowHeights.Length + 1)
            .Select(_ => new System.Windows.GridLength(1, System.Windows.GridUnitType.Star))
            .ToArray();

        return SelectedLayout.CloneAs(
            kind: PaneLayoutKind.Custom,
            layoutId: $"custom-{++_customLayoutCounter}",
            label: "Custom Layout",
            icon: "◫",
            rowHeights: rowHeights,
            slots: slots);
    }

    private PaneLayout NormalizeLayout(PaneLayout layout)
    {
        if (layout.Slots.Count == 0)
        {
            return PaneLayouts.Single.CloneAs();
        }

        var orderedRows = layout.Slots
            .Select(static slot => slot.Row)
            .Distinct()
            .OrderBy(static row => row)
            .ToArray();
        var orderedColumns = layout.Slots
            .Select(static slot => slot.Column)
            .Distinct()
            .OrderBy(static column => column)
            .ToArray();

        var rowMap = orderedRows
            .Select((row, index) => (row, index))
            .ToDictionary(static pair => pair.row, static pair => pair.index);
        var columnMap = orderedColumns
            .Select((column, index) => (column, index))
            .ToDictionary(static pair => pair.column, static pair => pair.index);

        var normalizedSlots = layout.Slots
            .Select(static slot => slot with { })
            .Select(slot => slot with
            {
                Row = rowMap[slot.Row],
                Column = columnMap[slot.Column]
            })
            .ToArray();

        var rowHeights = Enumerable.Range(0, Math.Max(1, orderedRows.Length))
            .Select(_ => new System.Windows.GridLength(1, System.Windows.GridUnitType.Star))
            .ToArray();
        var columnWidths = Enumerable.Range(0, Math.Max(1, orderedColumns.Length))
            .Select(_ => new System.Windows.GridLength(1, System.Windows.GridUnitType.Star))
            .ToArray();

        return layout.CloneAs(
            rowHeights: rowHeights,
            columnWidths: columnWidths,
            slots: normalizedSlots);
    }

    private void EnsurePaneAssignments(int paneCount)
    {
        while (_panePageTags.Count < paneCount)
        {
            _panePageTags.Add(null);
        }

        while (_panePageTags.Count > paneCount)
        {
            _panePageTags.RemoveAt(_panePageTags.Count - 1);
        }
    }

    private int ResolvePaneIndex(string? paneId)
    {
        if (string.IsNullOrWhiteSpace(paneId))
        {
            return ActivePaneIndex;
        }

        return SelectedLayout.Slots
            .Select((slot, index) => new { slot.PaneId, Index = index })
            .FirstOrDefault(slot => string.Equals(slot.PaneId, paneId, StringComparison.OrdinalIgnoreCase))
            ?.Index ?? -1;
    }

    private string? ResolvePaneId(string? paneId)
    {
        if (!string.IsNullOrWhiteSpace(paneId))
        {
            return paneId;
        }

        return GetActivePaneId();
    }
}
