using Meridian.Core.Logging;
using Serilog;

namespace Meridian.DataIntegration.Credentials;

/// <summary>
/// Keeps plaintext credential files left behind by earlier releases owner-only. Their contents are
/// never read, imported or erased; an operator removes them after rotating the credentials.
/// </summary>
public static class RetainedCredentialFileAccess
{
    private static readonly ILogger Log = LoggingSetup.ForContext(typeof(RetainedCredentialFileAccess));
    private const UnixFileMode OwnerOnlyFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>
    /// Restricts a retained plaintext credential file and its interrupted-import snapshot without
    /// claiming their contents. Permission failures propagate to the caller's startup boundary.
    /// </summary>
    public static void RestrictAccess(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (OperatingSystem.IsWindows())
            return;
        sourcePath = Path.GetFullPath(sourcePath);
        foreach (var path in new[] { sourcePath, sourcePath + ".migrated" })
        {
            try
            {
                RestrictOwnerOnly(path, File.GetUnixFileMode, File.SetUnixFileMode);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    internal static void RestrictOwnerOnly(string path,
        Func<string, UnixFileMode> readMode, Action<string, UnixFileMode> writeMode)
    {
        var exposed = readMode(path) & ~OwnerOnlyFileMode;
        if (exposed == UnixFileMode.None)
            return;
        writeMode(path, OwnerOnlyFileMode);
        if ((readMode(path) & ~OwnerOnlyFileMode) != UnixFileMode.None)
            throw new UnauthorizedAccessException("Legacy credential permissions remain reachable beyond their owner.");
        Log.Warning("Legacy credentials at {CredentialPath} were reachable beyond their owner ({ExposedMode}); tightened to owner-only. Treat the credentials as disclosed and rotate them.", path, exposed);
    }
}
