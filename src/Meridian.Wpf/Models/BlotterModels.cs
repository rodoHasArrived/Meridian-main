using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace Meridian.Wpf.Models;

/// <summary>
/// Represents a single trade/position row in the Position Blotter grid.
/// </summary>
public sealed class BlotterEntry : INotifyPropertyChanged
{
    private static readonly SolidColorBrush PnlPositiveBrush =
        new(Color.FromRgb(0x4C, 0xAF, 0x50));

    private static readonly SolidColorBrush PnlNegativeBrush =
        new(Color.FromRgb(0xF4, 0x43, 0x36));
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isSelected;

    /// <summary>Underlying asset group (e.g., "AAPL", "SPY").</summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>Human-readable product description (e.g., "AAPL 150 Call 17Sep25").</summary>
    public string ProductDescription { get; set; } = string.Empty;

    /// <summary>Unique trade/order identifier.</summary>
    public string TradeId { get; set; } = string.Empty;

    /// <summary>Executed unit price of the position.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Formatted unit price string for display.</summary>
    public string UnitPriceText => UnitPrice > 0 ? UnitPrice.ToString("N4") : "—";

    /// <summary>Signed quantity (positive = long, negative = short).</summary>
    public long Quantity { get; set; }

    /// <summary>Formatted quantity for display.</summary>
    public string QuantityText => Quantity.ToString("+#;-#;0");

    /// <summary>Side of the trade ("Buy" or "Sell").</summary>
    public string Side { get; set; } = string.Empty;

    /// <summary>Current order/position status.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Expiry date for options positions.</summary>
    public DateOnly? Expiry { get; set; }

    /// <summary>Formatted expiry for display (e.g., "17Sep25").</summary>
    public string ExpiryText => Expiry.HasValue ? Expiry.Value.ToString("dMMMyyy") : string.Empty;

    /// <summary>Market time of last update.</summary>
    public TimeOnly? MarketTime { get; set; }

    /// <summary>Unrealised P&amp;L for this position.</summary>
    public decimal UnrealisedPnl { get; set; }

    /// <summary>Formatted unrealised P&amp;L for display.</summary>
    public string UnrealisedPnlText => UnrealisedPnl.ToString("+#,0.00;-#,0.00;0.00");

    /// <summary>Brush applied to the unrealised P&amp;L cell.</summary>
    public SolidColorBrush PnlBrush => UnrealisedPnl >= 0 ? PnlPositiveBrush : PnlNegativeBrush;

    /// <summary>Whether the row is currently selected (checkbox).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}

/// <summary>
/// Represents a grouped set of blotter entries under one underlying symbol.
/// </summary>
public sealed class BlotterGroup : INotifyPropertyChanged
{
    private static readonly SolidColorBrush PnlPositiveBrush =
        new(Color.FromRgb(0x4C, 0xAF, 0x50));

    private static readonly SolidColorBrush PnlNegativeBrush =
        new(Color.FromRgb(0xF4, 0x43, 0x36));
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isExpanded = true;
    private bool _isSelected;

    /// <summary>Group name (underlying symbol).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Number of positions in this group.</summary>
    public int Count => Entries.Count;

    /// <summary>Net quantity across all positions in the group.</summary>
    public long NetQuantity => Entries.Sum(e => e.Quantity);

    /// <summary>Total unrealised P&amp;L across all positions in the group.</summary>
    public decimal TotalPnl => Entries.Sum(e => e.UnrealisedPnl);

    /// <summary>Formatted total P&amp;L for display.</summary>
    public string TotalPnlText => TotalPnl.ToString("+#,0.00;-#,0.00;0.00");

    /// <summary>Colour brush for the total P&amp;L — green for gains, red for losses.</summary>
    public SolidColorBrush TotalPnlBrush => TotalPnl >= 0 ? PnlPositiveBrush : PnlNegativeBrush;

    /// <summary>Whether the group row is expanded to show child entries.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExpandIcon)));
        }
    }

    /// <summary>Icon character to indicate expand/collapse state.</summary>
    public string ExpandIcon => _isExpanded ? "\uE70D" : "\uE76C"; // ChevronDown / ChevronRight

    /// <summary>Whether the group header checkbox is checked (selects all children).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            foreach (var entry in Entries)
            {
                entry.IsSelected = value;
            }
        }
    }

    /// <summary>Child entries belonging to this group.</summary>
    public ObservableCollection<BlotterEntry> Entries { get; } = [];
}

/// <summary>
/// An active filter chip displayed in the filter bar.
/// </summary>
public sealed record BlotterFilterChip(string Label, string Value);
