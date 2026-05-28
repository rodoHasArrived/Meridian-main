namespace Meridian.Application.Reconciliation;

public enum ReconciliationContractVersion : byte
{
    V1 = 1
}

public static class ReconciliationContractCatalog
{
    public const string BreakWorkflowContract = "meridian.reconciliation.break-workflow";

    private static readonly HashSet<(string Contract, ReconciliationContractVersion Version)> Supported = new()
    {
        (BreakWorkflowContract, ReconciliationContractVersion.V1)
    };

    public static bool IsSupported(string contract, ReconciliationContractVersion version)
        => Supported.Contains((contract, version));
}
