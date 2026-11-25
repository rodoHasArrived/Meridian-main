namespace Meridian.Mcp.Tools;

/// <summary>
/// Picks the Python executable name for repository scripts. Windows installs expose
/// <c>python</c>; Linux and macOS commonly expose only <c>python3</c>.
/// </summary>
internal static class PythonLauncher
{
    public static string Executable => OperatingSystem.IsWindows() ? "python" : "python3";
}
