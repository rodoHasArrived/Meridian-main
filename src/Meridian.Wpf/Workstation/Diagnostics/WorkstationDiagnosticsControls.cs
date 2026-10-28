using System.Windows.Controls;

namespace Meridian.Wpf.Workstation.Diagnostics;

/// <summary>
/// Provides an items-control surface for audit timeline entries.
/// </summary>
public sealed class AuditTimelineControl : ItemsControl { }

/// <summary>
/// Provides an items-control surface for diagnostic checklist entries.
/// </summary>
public sealed class DiagnosticsChecklistControl : ItemsControl { }

/// <summary>
/// Provides a combo-box surface for selecting a provider.
/// </summary>
public sealed class ProviderSelectorControl : ComboBox { }

/// <summary>
/// Provides a content surface for data-lineage detail.
/// </summary>
public sealed class DataLineagePanelControl : ContentControl { }

/// <summary>
/// Provides a content surface for data-quality detail.
/// </summary>
public sealed class DataQualityPanelControl : ContentControl { }

/// <summary>
/// Provides a data-grid surface for workflow routing rows.
/// </summary>
public sealed class RoutingMatrixControl : DataGrid { }
