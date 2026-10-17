namespace Meridian.Launcher;

internal static class LauncherCommandPolicy
{
    public static bool TryResolveStartupCommand(IReadOnlyList<string> arguments, out string command)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        command = "start";
        if (arguments.Count == 0)
            return true;
        if (arguments.Count != 1)
            return false;

        var requestedCommand = arguments[0]?.ToLowerInvariant();
        if (requestedCommand is not ("start" or "open"))
            return false;
        command = requestedCommand;
        return true;
    }
}
