namespace Meridian.Tests.TestHelpers;

/// <summary>
/// Enumerates checked-in files under a repository directory without following symbolic links
/// or junctions.
/// </summary>
/// <remarks>
/// <c>npm ci</c> in the browser dashboard links the local <c>file:</c> dependency
/// <c>meridian-tools</c> back to the repository root, so
/// <see cref="SearchOption.AllDirectories"/> loops until the OS reports "Too many levels of
/// symbolic links". Skipping reparse points stops recursion before it descends, unlike a
/// filter on the returned paths. Inaccessible entries still throw, so a real IO fault fails
/// the scan instead of silently shrinking it.
/// </remarks>
public static class RepositoryFileWalker
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
    };

    public static IEnumerable<string> EnumerateFiles(string directory, string searchPattern)
        => Directory.EnumerateFiles(directory, searchPattern, Options);
}
