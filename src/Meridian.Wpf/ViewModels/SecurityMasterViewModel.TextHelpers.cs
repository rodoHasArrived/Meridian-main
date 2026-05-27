namespace Meridian.Wpf.ViewModels;

internal static class SecurityMasterTextHelpers
{
    public static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
}
