namespace Meridian.Ui.Shared;

/// <summary>
/// Resolves the static asset root for hosts that may be launched from the repository root,
/// the project directory, or the compiled output directory.
/// </summary>
internal static class StaticAssetPathResolver
{
    public static string ResolveWebRootPath(string? existingWebRootPath, string contentRootPath, string appBaseDirectory)
    {
        foreach (var candidate in EnumerateCandidates(existingWebRootPath, contentRootPath, appBaseDirectory))
        {
            if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        var fallbackRoot = string.IsNullOrWhiteSpace(contentRootPath)
            ? appBaseDirectory
            : contentRootPath;

        return Path.GetFullPath(Path.Combine(fallbackRoot, "wwwroot"));
    }

    private static IEnumerable<string> EnumerateCandidates(string? existingWebRootPath, string contentRootPath, string appBaseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(existingWebRootPath))
        {
            yield return existingWebRootPath;
        }

        if (!string.IsNullOrWhiteSpace(contentRootPath))
        {
            yield return Path.Combine(contentRootPath, "wwwroot");
        }

        if (!string.IsNullOrWhiteSpace(appBaseDirectory))
        {
            yield return Path.Combine(appBaseDirectory, "wwwroot");
        }

        foreach (var directory in EnumerateDirectories(contentRootPath))
        {
            yield return Path.Combine(directory, "src", "Meridian", "wwwroot");
        }
    }

    private static IEnumerable<string> EnumerateDirectories(string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            yield break;
        }

        var fullPath = Path.GetFullPath(startDirectory);
        for (var current = new DirectoryInfo(fullPath); current is not null; current = current.Parent)
        {
            yield return current.FullName;
        }
    }
}
