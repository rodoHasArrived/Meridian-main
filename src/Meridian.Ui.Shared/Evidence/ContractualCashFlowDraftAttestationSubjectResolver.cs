using System.Text.Json;
using Meridian.Contracts.CashFlows;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Ui.Shared.Evidence;

/// <summary>Exact, scoped preparer checks over immutable calculation-only Security Master runs.</summary>
public sealed class ContractualCashFlowDraftAttestationSubjectResolver(
    IContractualCashFlowRunStore? runs = null, ISecurityMasterQueryService? securities = null)
{
    public const string SubjectKind = "contractual-cashflow-draft";
    public const string SourceSubjectKind = "contractual-cashflow-security";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<SinglePrincipalAttestationSubjectSnapshotDto?> ResolveAsync(
        Guid securityId, Guid runId, string tenantId, string companyId, string actor, CancellationToken ct = default)
    {
        if (runs is null || securities is null || securityId == Guid.Empty || runId == Guid.Empty || string.IsNullOrWhiteSpace(tenantId)
            || string.IsNullOrWhiteSpace(companyId) || string.IsNullOrWhiteSpace(actor))
            return null;

        var run = await runs.GetAsync(ContractualCashFlowSubjectKinds.Security, securityId, runId, ct).ConfigureAwait(false);
        if (run is null || run.SubjectKind != ContractualCashFlowSubjectKinds.Security
            || run.SubjectId != securityId || run.RunId != runId
            || run.AttestationContext is not { SchemaVersion: 1 } binding
            || !Same(binding.TenantId, tenantId) || !Same(binding.CompanyId, companyId)
            || !Same(binding.PreparedBy, actor) || !Same(binding.PreparedBy, run.Actor)
            || binding.Origin != OperationsActionOriginDto.HumanOperator
            || run.Result.IsPostable || !run.Result.IsCalculated || !ContractualCashFlowEngine.Verify(run))
            return null;

        var source = await securities.GetRecordedByIdAsOfAsync(securityId, run.Request.KnowledgeCutoff, ct).ConfigureAwait(false);
        if (source is null
            || run.Result.Evidence.SourceVersion != $"security:{securityId}:version:{source.Version}"
            || !Sha256Digest.FixedEquals(run.Result.Evidence.SourceTermsHash,
                ContractualCashFlowEngine.Hash(new SecurityTermsDigest(source.CommonTerms, source.AssetSpecificTerms))))
            return null;

        var runJson = JsonSerializer.Serialize(run, ContractualCashFlowJsonContext.Default.ContractualCashFlowRun);
        var sourceHash = Sha256Digest.ComputeUtf8(runJson);
        var snapshotJson = JsonSerializer.Serialize(new { SchemaVersion = 1, IsPostable = false, Run = run }, Json);
        var revision = Sha256Digest.ComputeUtf8(snapshotJson);
        return new(SubjectKind, $"Security/{securityId:D}/{runId:D}", revision, revision,
            $"contractual-cashflow-run:{runId:D}", sourceHash, run.Actor, "Unclassified", true,
            ["Exact immutable run and replayed results verified.",
             "Run creation bound this tenant, company and authenticated human preparer.",
             "Calculation has no blockers and is not postable; checking it grants no approval, close or release authority."],
            SourceSubjectKind, securityId.ToString("D"), snapshotJson, binding.PreparedBy,
            SinglePrincipalAttestationPolicy.DraftConfirmation);
    }

    public static bool TryParseSubjectId(string? subjectId, out Guid securityId, out Guid runId)
    {
        securityId = runId = Guid.Empty;
        return subjectId?.Split('/') is ["Security", var security, var run]
            && Guid.TryParseExact(security, "D", out securityId) && securityId != Guid.Empty
            && Guid.TryParseExact(run, "D", out runId) && runId != Guid.Empty;
    }

    private static bool Same(string? left, string? right) => !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right) && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
