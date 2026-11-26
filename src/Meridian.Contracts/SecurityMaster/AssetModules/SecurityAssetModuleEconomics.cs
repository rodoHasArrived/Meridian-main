namespace Meridian.Contracts.SecurityMaster;

/// <summary>A flat key and its economic-module sources, retaining priority, labels and unit scaling.</summary>
internal sealed record SecurityEconomicTermFlattening(
    string FlatKey,
    (string Module, string Field)[] Sources,
    string? EqualsLabel = null,
    (string Module, string Field, string Label)[]? LabelFromFlags = null,
    decimal? Scale = null);

internal static class SecurityAssetModuleEconomics
{
    internal static readonly (string Module, string Field) MaturityDate = ("maturity", "maturityDate");

    internal static SecurityEconomicTermFlattening Map(string flatKey, params (string Module, string Field)[] sources)
        => new(flatKey, sources);
}
