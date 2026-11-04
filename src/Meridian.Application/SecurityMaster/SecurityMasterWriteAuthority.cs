namespace Meridian.Application.SecurityMaster;

/// <summary>
/// Source-system identifiers that name an operator surface rather than a provider or system feed.
/// The system-ingest path (<c>ISecurityMasterAmender</c>) refuses them so operator edits stay on the
/// operator-direct path (governed by <see cref="SecurityMasterWorkbenchOptions.RequireGovernedTermAmendments"/>)
/// or the governed publication path.
/// </summary>
public static class SecurityMasterWriteAuthority
{
    /// <summary>Source system the governed workbench publication stamps on its merged amendment.</summary>
    public const string WorkbenchSourceSystem = "operator-workbench";

    /// <summary>Source system the WPF Security Master editor stamps on a direct amendment.</summary>
    public const string DesktopEditorSourceSystem = "WPF-UI";

    /// <summary>Source system the workstation HTTP surface stamps on writes it originates.</summary>
    public const string WorkstationHttpSourceSystem = "workstation-http";

    private static readonly HashSet<string> OperatorSourceSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        WorkbenchSourceSystem,
        DesktopEditorSourceSystem,
        WorkstationHttpSourceSystem
    };

    /// <summary>True when <paramref name="sourceSystem"/> names an operator surface.</summary>
    public static bool IsOperatorSourceSystem(string? sourceSystem)
        => sourceSystem is not null && OperatorSourceSystems.Contains(sourceSystem.Trim());
}
