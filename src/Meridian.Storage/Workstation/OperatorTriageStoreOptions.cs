namespace Meridian.Storage.Workstation;

public sealed class OperatorTriageStoreOptions
{
    public required string ConnectionString { get; init; }
    public string Schema { get; init; } = "operator_triage";
}
