namespace Meridian.Contracts.Workstation;

/// <summary>
/// The fixed definition of every workstation headline figure: the label it is shown under, the
/// population it counts and what a change in it is measured against. The browser workstation's
/// <c>lib/kpi-registry.ts</c> and the WPF desktop read the same catalog, so a figure with the same
/// name always covers the same group on every screen.
/// </summary>
public sealed record WorkstationKpiDefinitionsDto(IReadOnlyList<WorkstationKpiDefinitionDto> Definitions);

/// <summary>One headline figure's identity. <c>ComparisonBasis</c> is null when the figure carries no change.</summary>
public sealed record WorkstationKpiDefinitionDto(string Id, string Label, string Population, string? ComparisonBasis);
