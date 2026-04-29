namespace Meridian.Wpf.Models;

public static class ShellNavigationTextStyleGuide
{
    /// <summary>
    /// Navigation text style guide:
    /// 1) Use sentence case for titles and subtitles.
    /// 2) Prefer concrete user tasks (monitor, reconcile, export, review, configure).
    /// 3) Avoid metaphor-heavy or abstract jargon.
    /// 4) Keep subtitles concise to preserve scanability in the shell and command palette.
    /// </summary>
    public static IReadOnlyList<string> PreferredTaskVerbs { get; } =
    [
        "monitor",
        "reconcile",
        "export",
        "review",
        "configure"
    ];

    public static IReadOnlyList<string> BannedJargonTerms { get; } =
    [
        "cockpit",
        "workbench",
        "rails",
        "posture",
        "control surface",
        "control tower"
    ];

    public const int SubtitleMaxLength = 88;
}
