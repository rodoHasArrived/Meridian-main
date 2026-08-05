namespace Meridian.Core.IO;

/// <summary>
/// Resolves portable path segments beneath one configured root and refuses paths that cross the
/// root boundary or pass through an existing symbolic link or reparse point.
/// </summary>
public sealed class RootedPathGuard
{
    private const string PortableInvalidFileNameCharacters = "<>:\"/\\|?*";

    private static readonly HashSet<string> ReservedDeviceNames = new(
        [
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "COM1",
            "COM2",
            "COM3",
            "COM4",
            "COM5",
            "COM6",
            "COM7",
            "COM8",
            "COM9",
            "LPT1",
            "LPT2",
            "LPT3",
            "LPT4",
            "LPT5",
            "LPT6",
            "LPT7",
            "LPT8",
            "LPT9"
        ],
        StringComparer.OrdinalIgnoreCase);

    public RootedPathGuard(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        RootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
    }

    /// <summary>The normalized lexical root used for all descendant resolution.</summary>
    public string RootPath { get; }

    /// <summary>
    /// Resolves validated, single-component path segments beneath <see cref="RootPath"/>.
    /// </summary>
    public string ResolvePath(params string[] pathSegments)
    {
        ArgumentNullException.ThrowIfNull(pathSegments);
        if (pathSegments.Length == 0)
            throw new ArgumentException("At least one path segment is required.", nameof(pathSegments));

        var candidate = RootPath;
        foreach (var segment in pathSegments)
        {
            ValidatePathSegment(segment, nameof(pathSegments));
            candidate = Path.Combine(candidate, segment);
        }

        candidate = Path.GetFullPath(candidate);
        EnsurePath(candidate);
        return candidate;
    }

    /// <summary>
    /// Verifies that an already-composed path remains beneath the configured root and that every
    /// existing descendant from the root to that path is not a symbolic link or reparse point.
    /// Callers should invoke this again immediately after creating a parent directory and before a
    /// destructive operation.
    /// </summary>
    public void EnsurePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var candidate = Path.GetFullPath(path);
        var relativePath = Path.GetRelativePath(RootPath, candidate);

        if (Path.IsPathRooted(relativePath) ||
            string.Equals(relativePath, "..", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Path '{candidate}' is outside the configured root '{RootPath}'.");
        }

        EnsureNotReparsePoint(RootPath, candidate);

        if (string.Equals(relativePath, ".", StringComparison.Ordinal))
            return;

        var currentPath = RootPath;
        foreach (var segment in relativePath.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, segment);
            if (!EnsureNotReparsePoint(currentPath, candidate))
                break;
        }
    }

    private static bool EnsureNotReparsePoint(string currentPath, string candidate)
    {
        try
        {
            if (File.GetAttributes(currentPath).HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException(
                    $"Path '{candidate}' crosses symbolic link or reparse point '{currentPath}'.");
            }

            return true;
        }
        catch (FileNotFoundException)
        {
            // Once an ancestor does not exist, no deeper descendant can currently be a link.
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Rejects values that are not one portable, identity-preserving file-name component.
    /// </summary>
    public static void ValidatePathSegment(string segment, string? parameterName = null)
    {
        ArgumentNullException.ThrowIfNull(segment, parameterName);
        if (string.IsNullOrWhiteSpace(segment))
            throw new ArgumentException("Path segments cannot be empty or whitespace.", parameterName);
        if (!string.Equals(segment, segment.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Path segments cannot contain leading or trailing whitespace.", parameterName);
        if (string.Equals(segment, ".", StringComparison.Ordinal) ||
            string.Equals(segment, "..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Dot path segments are not allowed.", parameterName);
        }
        if (Path.IsPathRooted(segment))
            throw new ArgumentException("Rooted path segments are not allowed.", parameterName);
        if (segment.EndsWith('.') || segment.EndsWith(' '))
            throw new ArgumentException("Path segments cannot end with a dot or space.", parameterName);
        if (segment.Any(static character =>
                char.IsControl(character) ||
                PortableInvalidFileNameCharacters.Contains(character)))
        {
            throw new ArgumentException("Path segments contain a path separator or invalid file-name character.", parameterName);
        }

        var deviceName = segment.Split('.', 2, StringSplitOptions.None)[0];
        if (ReservedDeviceNames.Contains(deviceName))
            throw new ArgumentException("Reserved device names are not allowed as path segments.", parameterName);
    }
}
