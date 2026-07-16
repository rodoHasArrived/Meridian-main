namespace Meridian.Contracts.Workstation;

public sealed record FirstRunStatusDto(
    bool IsComplete,
    string? Goal,
    string? StarterKitId,
    string? DataChoice,
    WorkspaceModeDto Workspace,
    IReadOnlyList<StarterWorkspaceDto> StarterKits,
    IReadOnlyList<ActivationOutcomeDto> Outcomes,
    IReadOnlyList<RecommendedActionDto> RecommendedActions);

public sealed record WorkspaceModeDto(
    string Id,
    string Name,
    bool IsSample,
    string Badge,
    string SafetyMessage,
    string SamplePackVersion);

public sealed record StarterWorkspaceDto(
    string Id,
    string Name,
    string Goal,
    string Description,
    string DefaultRoute);

public sealed record ActivationOutcomeDto(
    string Key,
    string Label,
    string ActionLabel,
    string Route,
    bool IsComplete,
    DateTimeOffset? CompletedAtUtc);

public sealed record RecommendedActionDto(string Label, string Route, string Description);

public sealed record CompleteFirstRunRequestDto(
    string Goal,
    string StarterKitId,
    string DataChoice,
    bool UseSampleData);

public sealed record CompleteActivationOutcomeRequestDto(string Key);

public sealed record DesktopLaunchTicketRedemptionDto(
    string Username,
    string Page,
    DateTimeOffset ExpiresAtUtc);
