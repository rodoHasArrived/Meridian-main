namespace Meridian.FinancialOperations.OperationsContinuity;

/// <summary>Server-resolved revision and retained repair evidence used to start a subject approval lane.</summary>
public sealed record SecurityMasterRevisionWorkflowAuthority(
    Guid WorkflowId, Guid RevisionId, Guid FundAccountId, Guid LedgerBookId, string PeriodId,
    string Maker, string EvidenceId, string EvidenceSha256);

public interface ISecurityMasterRevisionWorkflowAuthority
{
    Task<SecurityMasterRevisionWorkflowAuthority?> ResolveAsync(Guid revisionId, string actor, CancellationToken ct = default);
}
