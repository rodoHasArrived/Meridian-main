using Meridian.Contracts.Integrity;

namespace Meridian.Reporting;

/// <summary>Request bindings retained in the certified parameters of an idempotent reporting operation.</summary>
public static class ReportingOperationIdentity
{
    public const string OperationIdParameter = "reportingOperationId";
    public const string RequestHashParameter = "reportingOperationRequestHash";

    public static void Validate(string operationId, string requestHash)
    {
        if (string.IsNullOrWhiteSpace(operationId) || operationId.Length > 160
            || operationId.Any(char.IsWhiteSpace) || operationId.Any(char.IsControl)
            || !Sha256Digest.IsWellFormed(requestHash))
            throw new ReportingGovernanceException("A canonical operation identity and exact request hash are required.");
    }

    public static void RequireMatches(ReportingOutputManifest manifest, string operationId,
        string requestHash, string templateId, string tenantId, string? companyId)
    {
        Validate(operationId, requestHash);
        var parameters = manifest.ResolvedParameters?.TemplateParameters;
        if (manifest.RunId != operationId || manifest.TemplateId != templateId
            || manifest.OperationalScope is not { } scope || scope.TenantId != tenantId || scope.CompanyId != companyId
            || parameters is null
            || !parameters.TryGetValue(OperationIdParameter, out var retainedId) || retainedId != operationId
            || !parameters.TryGetValue(RequestHashParameter, out var retainedHash)
            || !Sha256Digest.FixedEquals(retainedHash, requestHash))
            throw new ReportingGovernanceException("This notice operation identity already belongs to a different request or authority scope.");
    }
}
