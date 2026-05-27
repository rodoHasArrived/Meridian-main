namespace Meridian.Application.Reconciliation;

public enum ReconciliationContractVersion
{
    V1 = 1
}

public enum ReconciliationMessageKind
{
    ReconciliationRequested,
    ReconciliationCompleted,
    BreakRaised,
    BreakTransitioned,
    ApprovalPolicyEvaluated
}

public sealed record ReconciliationContractEnvelope(
    string ContractName,
    ReconciliationContractVersion ContractVersion,
    ReconciliationMessageKind MessageKind,
    string CorrelationId,
    string CausationId,
    string IdempotencyKey,
    DateTimeOffset OccurredAtUtc,
    string Producer,
    string Tenant,
    string Counterparty,
    IReadOnlyDictionary<string, string>? Tags = null);

public static class ReconciliationContractCatalog
{
    public const string RequestContract = "meridian.reconciliation.request";
    public const string ResultContract = "meridian.reconciliation.result";
    public const string BreakWorkflowContract = "meridian.reconciliation.break-workflow";
    public const string ApprovalPolicyContract = "meridian.reconciliation.approval-policy";

    public static bool IsSupported(string contractName, ReconciliationContractVersion version)
    {
        if (version != ReconciliationContractVersion.V1)
        {
            return false;
        }

        return contractName is RequestContract or ResultContract or BreakWorkflowContract or ApprovalPolicyContract;
    }
}
