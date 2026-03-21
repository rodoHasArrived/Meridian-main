namespace Meridian.Ui.Shared.Endpoints;

/// <summary>
/// Utility methods for validating and sanitizing file system paths in API inputs.
/// Prevents path traversal attacks (e.g., ../../etc/passwd).
/// </summary>
internal static class PathValidation
{
    /// <summary>
    /// Validates that a resolved path stays within the expected base directory.
    /// </summary>
    /// <param name="userInput">The user-provided path or directory string.</param>
    /// <param name="allowedBase">The base directory that the resolved path must stay within.</param>
    /// <param name="resolvedPath">The resolved full path if validation passes.</param>
    /// <returns>True if the path is safe; false if it escapes the allowed base.</returns>
    public static bool IsWithinBase(string userInput, string allowedBase, out string resolvedPath)
    {
        resolvedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(userInput) || string.IsNullOrWhiteSpace(allowedBase))
            return false;

        try
        {
            var fullBase = Path.GetFullPath(allowedBase);
            if (!fullBase.EndsWith(Path.DirectorySeparatorChar))
                fullBase += Path.DirectorySeparatorChar;

            var fullPath = Path.GetFullPath(Path.Combine(allowedBase, userInput));
            resolvedPath = fullPath;

            return fullPath.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if a path contains path traversal sequences.
    /// </summary>
    public static bool ContainsTraversal(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        return path.Contains("..") || path.Contains('\0');
    }

    /// <summary>
    /// Sanitizes a data root path, ensuring it's a relative path without traversal.
    /// Returns null if the path is invalid.
    /// </summary>
    public static string? SanitizeDataRoot(string? dataRoot)
    {
        if (string.IsNullOrWhiteSpace(dataRoot))
            return "data";

        // Reject absolute paths and traversal
        if (Path.IsPathRooted(dataRoot) || ContainsTraversal(dataRoot))
            return null;

        return dataRoot;
    }
}
