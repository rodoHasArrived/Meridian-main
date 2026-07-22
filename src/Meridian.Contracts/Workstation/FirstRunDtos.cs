namespace Meridian.Contracts.Workstation;

public sealed record FirstRunStatusDto(
    bool IsComplete,
    string? Goal,
    string? StarterKitId,
    string? DataChoice,
    WorkspaceModeDto Workspace,
    IReadOnlyList<StarterWorkspaceDto> StarterKits,
    IReadOnlyList<ActivationOutcomeDto> Outcomes,
    IReadOnlyList<RecommendedActionDto> RecommendedActions,
    SampleWorkspaceDto? SampleWorkspace = null);

/// <summary>
/// Populated snapshot of the sample demo tenant loaded when a user chooses "Use sample data".
/// Present only for sample workspaces; lets onboarding surfaces show a believable, populated
/// workspace (holdings, breaks, a report, a paper strategy) instead of only a mode badge.
/// </summary>
/// <remarks>
/// <see cref="Provenance"/> carries the Blueprint-1 provenance label ("Seeded") that every
/// artifact in this workspace shares, so onboarding can render it without inferring the source.
/// </remarks>
public sealed record SampleWorkspaceDto(
    string Headline,
    string Summary,
    string PortfolioName,
    decimal PortfolioValue,
    decimal Cash,
    decimal UnrealizedPnl,
    IReadOnlyList<SampleHoldingDto> Holdings,
    IReadOnlyList<string> Watchlist,
    IReadOnlyList<SampleBreakDto> ReconciliationBreaks,
    SampleArtifactDto Report,
    SampleArtifactDto Strategy,
    SampleMarketHistoryDto MarketHistory,
    IReadOnlyList<SampleHighlightDto> Highlights,
    string Provenance = "Seeded");

public sealed record SampleHoldingDto(
    string Symbol,
    decimal Quantity,
    decimal AveragePrice,
    decimal LastPrice,
    decimal MarketValue,
    decimal UnrealizedPnl);

public sealed record SampleBreakDto(
    string Id,
    string Title,
    string Category,
    string Severity,
    decimal Variance,
    string Summary,
    string Route);

public sealed record SampleArtifactDto(string Name, string Status, string Detail, string Route);

public sealed record SampleMarketHistoryDto(IReadOnlyList<string> Symbols, int Sessions, string Route);

public sealed record SampleHighlightDto(string Label, string Value, string Detail, string Route);

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
