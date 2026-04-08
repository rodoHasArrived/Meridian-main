namespace Meridian.Application.Config;

internal static class CredentialPlaceholderDetector
{
    private static readonly string[] PlaceholderFragments =
    [
        "__SET_ME__",
        "YOUR_",
        "your-",
        "REPLACE_",
        "ENTER_",
        "INSERT_",
        "TODO",
        "change-me",
        "placeholder"
    ];

    private static readonly HashSet<string> PlaceholderValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "__SET_ME__",
        "SET_ME",
        "YOUR_KEY_HERE",
        "YOUR_API_KEY",
        "YOUR_SECRET",
        "CHANGE_ME",
        "TODO",
        "XXX",
        "PLACEHOLDER",
        "<YOUR_KEY>",
        "<API_KEY>"
    };

    public static bool ContainsPlaceholderMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        return PlaceholderValues.Contains(trimmed)
               || PlaceholderFragments.Any(fragment =>
                   trimmed.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsPlaceholderValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        return ContainsPlaceholderMarker(value);
    }

    public static bool LooksLikeRealCredential(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) && !ContainsPlaceholderMarker(value);
    }
}
