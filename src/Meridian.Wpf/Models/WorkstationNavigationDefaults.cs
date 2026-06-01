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

    public static IReadOnlyDictionary<string, string> WorkspaceCompatibilityAliases { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["governance"] = AccountingWorkspaceId,
            ["research"] = StrategyWorkspaceId,
            ["dataoperations"] = DataWorkspaceId,
            ["data-operations"] = DataWorkspaceId,
            ["data operations"] = DataWorkspaceId
        };

    public static string NormalizeWorkspaceId(string? workspaceId, string fallbackWorkspaceId = StrategyWorkspaceId)
    {
        var value = string.IsNullOrWhiteSpace(workspaceId)
            ? fallbackWorkspaceId
            : workspaceId.Trim();

        return WorkspaceCompatibilityAliases.TryGetValue(value, out var canonicalWorkspaceId)
            ? canonicalWorkspaceId
            : value;
    }

    public static string NormalizeWorkspaceTitle(string? workspaceTitle)
    {
        if (string.IsNullOrWhiteSpace(workspaceTitle))
        {
            return string.Empty;
        }

        var value = workspaceTitle.Trim();
        var normalizedWorkspaceId = NormalizeWorkspaceId(value, value);

        if (string.Equals(normalizedWorkspaceId, AccountingWorkspaceId, StringComparison.OrdinalIgnoreCase))
        {
            return "Accounting";
        }

        if (string.Equals(normalizedWorkspaceId, DataWorkspaceId, StringComparison.OrdinalIgnoreCase))
        {
            return "Data";
        }

        if (string.Equals(normalizedWorkspaceId, StrategyWorkspaceId, StringComparison.OrdinalIgnoreCase))
        {
            return "Strategy";
        }

        return value;
    }

    public static string NormalizePageTag(string? pageTag, string fallbackPageTag = StrategyPageTag)
    {
        var value = string.IsNullOrWhiteSpace(pageTag)
            ? fallbackPageTag
            : pageTag.Trim();

        return ShellNavigationCatalog.GetCanonicalPageTag(value);
    }
}
