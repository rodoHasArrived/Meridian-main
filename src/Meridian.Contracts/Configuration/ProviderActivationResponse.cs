namespace Meridian.Contracts.Configuration;

/// <summary>Provider runtime evidence and host restart eligibility for the current operator.</summary>
public sealed record ProviderActivationResponse(
    IReadOnlyList<ProviderActivationEntry> Providers,
    bool CanRestart,
    IReadOnlyList<string> PendingPaths,
    string? RestartDetail,
    string? RuntimeSessionId,
    bool IsReady);

/// <summary>Activation evidence for one provider in a streaming, backfill, or module surface.</summary>
public sealed record ProviderActivationEntry(
    string ProviderId,
    string Surface,
    string State,
    DateTimeOffset? ActiveSinceUtc,
    string? Reason);

public static class ProviderActivationStates
{
    public const string Active = "active";
    public const string SavedPending = "saved-pending";
    public const string Disabled = "disabled";
    public const string FailedToStart = "failed-to-start";
    public const string Unavailable = "unavailable";
}
