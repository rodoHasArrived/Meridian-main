namespace Meridian.Application.Config;

/// <summary>
/// Resolves the default configuration file by searching the current directory and its ancestors.
/// This keeps host-specific working directories from hiding the repository-level config folder.
/// </summary>
internal static class DefaultConfigPathResolver
{
    internal const string DefaultConfigFileName = "appsettings.json";
    internal const string ConfigDirectoryDefaultConfigFileName = "config/appsettings.json";

    public static string Resolve()
        => ResolveFrom(Environment.CurrentDirectory);

    internal static string ResolveFrom(string? startDirectory)
    {
        var directory = string.IsNullOrWhiteSpace(startDirectory)
            ? Environment.CurrentDirectory
            : Path.GetFullPath(startDirectory);

        foreach (var candidateDirectory in EnumerateDirectories(directory))
        {
            var configDirectoryPath = Path.Combine(candidateDirectory, "config", DefaultConfigFileName);
            if (File.Exists(configDirectoryPath))
            {
                return configDirectoryPath;
            }

            var rootConfigPath = Path.Combine(candidateDirectory, DefaultConfigFileName);
            if (File.Exists(rootConfigPath))
            {
                return rootConfigPath;
            }
        }

        return Path.Combine(directory, DefaultConfigFileName);
    }

    private static IEnumerable<string> EnumerateDirectories(string startDirectory)
    {
        for (var current = new DirectoryInfo(startDirectory); current is not null; current = current.Parent)
        {
            yield return current.FullName;
        }
    }
}
