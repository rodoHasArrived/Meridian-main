using System.Collections.ObjectModel;
using Meridian.Contracts.Configuration;

namespace Meridian.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Data Sources configuration page.
/// Owns the <see cref="DataSources"/> collection so that XAML bindings
/// do not require <c>DataContext = this</c> on the Page.
/// </summary>
public sealed class DataSourcesViewModel : BindableBase
{
    // ── Collection (bound in XAML) ──────────────────────────────────────
    public ObservableCollection<DataSourceConfigDto> DataSources { get; } = new();
}
