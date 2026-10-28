using System.Text.Json;
using Meridian.Application.SecurityMaster.Validation;
using Meridian.Contracts.SecurityMaster;

namespace Meridian.Application.SecurityMaster;

// Called before canonical events, projections or conflict records are written. This is capture
// admission only; descriptive accounting policies never authorize posting or manufacture evidence.
internal static class SecurityAssetPackAdmission
{
    internal static void EnsureSubmittedFields(string assetClass, JsonElement terms)
    {
        // Preserve the mapping's existing unsupported-class refusal and its repair guidance.
        if (!SecurityAssetClassCatalog.TryGet(assetClass, out _))
            return;
        var descriptor = RequirePackSchemas(assetClass);
        var issues = new SchemaTermsAssetClassValidator(descriptor.AssetClass).ValidateSubmittedFieldsForWrite(terms);
        RefuseErrors(issues, $"submitted {descriptor.AssetClass} terms");
        ScheduleTermsWriteRules.Validate(descriptor.AssetClass, terms);
    }

    internal static void EnsureAdmitted(SecurityProjectionRecord projection, DateTimeOffset effectiveAt)
    {
        var descriptor = RequirePackSchemas(projection.AssetClass);

        // The approved profile validator remains authoritative for dynamic profile fields. The
        // generated flat-class walk deliberately excludes a profile envelope (ADR-024).
        var issues = new SchemaTermsAssetClassValidator(descriptor.AssetClass)
            .ValidateForWrite(new SecurityValidationContext(projection, effectiveAt));
        RefuseErrors(issues, projection.SecurityId.ToString("D"));
    }

    private static SecurityAssetClassDescriptor RequirePackSchemas(string assetClass)
    {
        if (!SecurityAssetClassCatalog.TryGet(assetClass, out var descriptor))
            throw new InvalidOperationException($"Asset class '{assetClass}' is not supported by this node's admission catalog.");
        var packs = SecurityAssetPackRegistry.FindByAssetClass(descriptor.AssetClass);
        if (packs.Count == 0)
            throw new InvalidOperationException($"Asset class '{assetClass}' has no registered admission pack.");
        foreach (var pack in packs)
        {
            var validation = SecurityAssetPackRegistry.ValidateDescriptor(pack);
            if (!validation.IsValid)
                throw new InvalidOperationException($"Asset pack '{pack.PackId}' cannot admit canonical writes: "
                    + string.Join(" ", validation.Issues.Select(issue => issue.Message)));
        }
        return descriptor;
    }

    private static void RefuseErrors(IReadOnlyList<SecurityValidationIssueDto> issues, string subject)
    {
        var errors = issues.Where(issue => issue.Severity == SecurityValidationSeverityDto.Error).ToArray();
        if (errors.Length > 0)
            throw new InvalidOperationException($"Asset pack admission refused '{subject}': "
                + string.Join(" ", errors.Select(issue => $"{issue.Code} ({string.Join(", ", issue.AffectedFields)}): {issue.Message}")));
    }
}
