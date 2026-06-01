namespace Meridian.Wpf.Models;

/// <summary>
/// Canonical workspace ids and page tags emitted by the desktop shell.
/// Compatibility aliases continue to resolve through <see cref="ShellNavigationCatalog"/>.
/// </summary>
public static class WorkstationNavigationDefaults
{
    public const string AccountingWorkspaceId = "accounting";
    public const string AccountingPageTag = "AccountingShell";
    public const string DataWorkspaceId = "data";
    public const string DataPageTag = "DataShell";
    public const string StrategyWorkspaceId = "strategy";
    public const string StrategyPageTag = "StrategyShell";

    public static string NormalizeWorkspaceId(string? workspaceId, string fallbackWorkspaceId = StrategyWorkspaceId)
    {
        var value = string.IsNullOrWhiteSpace(workspaceId)
            ? fallbackWorkspaceId
            : workspaceId.Trim();

        return value.ToLowerInvariant() switch
        {
            "governance" => AccountingWorkspaceId,
            "research" => StrategyWorkspaceId,
            "dataoperations" or "data-operations" or "data operations" => DataWorkspaceId,
            _ => value
        };
    }

    public static string NormalizePageTag(string? pageTag, string fallbackPageTag = StrategyPageTag)
    {
        var value = string.IsNullOrWhiteSpace(pageTag)
            ? fallbackPageTag
            : pageTag.Trim();

        return ShellNavigationCatalog.GetCanonicalPageTag(value);
    }
}
