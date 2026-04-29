using Meridian.Contracts.Workstation;

namespace Meridian.Strategies.Models;

/// <summary>
/// Repository-oriented query for strategy run history and drill-in lookups.
/// </summary>
public sealed record StrategyRunRepositoryQuery(
    string? StrategyId = null,
    IReadOnlyList<RunType>? RunTypes = null,
    StrategyRunStatus? Status = null,
    int Limit = 0);
