namespace Meridian.Workflow.Runbooks;

public sealed record RunbookDefinition(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<RunbookStep> Steps,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record RunbookStep(string Kind, string Payload);

public sealed record RunbookExecutionResult(
    string RunbookId,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    bool Success,
    IReadOnlyList<string> Messages);
