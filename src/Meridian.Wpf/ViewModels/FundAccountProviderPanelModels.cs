namespace Meridian.Wpf.ViewModels;

/// <summary>
/// One scoped provider binding shown for the selected account.
/// </summary>
public sealed record FundAccountProviderBindingItem(
    string Capability,
    string ConnectionLabel,
    string ConnectionType,
    string ScopeLabel,
    string SafetyMode,
    string TrustLabel,
    string StatusLabel);

/// <summary>
/// One effective route preview shown for the selected account.
/// </summary>
public sealed record FundAccountRoutePreviewItem(
    string Capability,
    string SelectedConnectionLabel,
    string SafetyMode,
    string StatusLabel,
    string ReasonSummary,
    string FallbackSummary);
