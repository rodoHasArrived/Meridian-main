using System.Collections.ObjectModel;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Data Export page.
/// Owns the symbol selection and export history collections so that
/// XAML bindings do not require <c>DataContext = this</c> on the Page.
/// </summary>
public sealed class DataExportViewModel : BindableBase
{
    // ── Collections (bound in XAML) ─────────────────────────────────────
    public ObservableCollection<string> SelectedSymbols { get; } = new();
    public ObservableCollection<ExportHistoryItem> ExportHistory { get; } = new();
}

/// <summary>Represents a single completed export in the export history list.</summary>
public sealed class ExportHistoryItem
{
    public string Timestamp { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string SymbolCount { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
}
