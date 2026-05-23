namespace Meridian.Ui.Shared.Services;

public sealed class ChiefOfStaffOptions
{
    public const string SectionName = "ChiefOfStaff";

    public string RuntimeBaseUrl { get; init; } = "http://localhost:8090";

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public int MaxConcurrentSessions { get; init; } = 8;

    public bool EnableTraceRetention { get; init; } = true;

    public bool EnableDecisionRouting { get; init; } = true;

    public IReadOnlyList<string> AllowedIntentKinds { get; init; } = [];

    public IReadOnlyList<string> AllowedMcpServers { get; init; } = [];
}
