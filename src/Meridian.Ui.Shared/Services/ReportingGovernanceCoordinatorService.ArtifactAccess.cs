using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using static Meridian.Contracts.Text.TextPrimitives;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ReportingGovernanceCoordinatorService
{
    private static string ResolveBookId(ReportingRunParametersDto parameters) =>
        parameters.LedgerBook.LedgerBookId?.ToString("D")
        ?? NormalizeOptional(parameters.LedgerBook.LedgerBookCode)
        ?? throw new ReportingGovernanceException("Reporting readiness has no resolved ledger book.");

    private static int ResolveTemplateMajorVersion(string version)
    {
        var token = version.Split('.', 2, StringSplitOptions.TrimEntries)[0];
        return int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : throw new ReportingGovernanceException($"Reporting template version '{version}' is invalid.");
    }

    private static void EnsureRetainedArtifactsAreDiscoverable(GovernedReportingRun run)
    {
        if (run.ExecutionState != GovernedReportingExecutionState.Succeeded)
        {
            throw new ReportingGovernanceException(
                $"Reporting run '{run.RunId}' has not reached the retained-artifact Succeeded postcondition.");
        }
    }

    private static void EnsureArtifactDownloadIsAuthorized(
        GovernedReportingRun run,
        ReportingDeclaredArtifact declaration,
        ReportingGovernanceCallerContext caller)
    {
        if (declaration.Kind == ReportingDeclaredArtifactKind.Preview)
        {
            return;
        }

        if (run.GovernanceState != GovernedReportingState.Released)
        {
            throw new ReportingGovernanceException(
                $"Reporting artifact '{run.RunId}/{declaration.ArtifactId}' is release-gated; only the preview is available before release.");
        }

        if (!caller.Permissions.HasFlag(UserPermission.DeliverReporting)
            && !caller.Permissions.HasFlag(UserPermission.AdminMaintenance))
        {
            throw new ReportingGovernanceAuthorizationException(
                "Authenticated caller lacks explicit 'DeliverReporting' permission for released report bytes.");
        }
    }

    private static ReportingArtifactAccessContext BuildArtifactAccessContext(
        GovernedReportingRun run,
        ReportingGovernanceCallerContext caller) =>
        new(
            caller.ActorId.Trim(),
            run.Scope.TenantId,
            run.Scope.OrganizationId,
            run.Scope.CompanyId,
            run.Scope.FundId,
            run.Scope.BookId,
            run.Scope.PeriodId,
            caller.PrincipalIds.IsDefault
                ? ImmutableArray<string>.Empty
                : caller.PrincipalIds,
            caller.CorrelationId.Trim());

    private static ReportingGovernedArtifactDescriptor BuildArtifactDescriptor(
        GovernedReportingRun run,
        ReportingRetainedArtifactRecord record,
        ReportingDeclaredArtifact declaration)
    {
        if (!string.Equals(record.PackageId, BuildPackageId(run), StringComparison.Ordinal)
            || !string.Equals(record.RunId, run.RunId, StringComparison.Ordinal)
            || !string.Equals(record.SeriesId, run.SeriesId, StringComparison.Ordinal)
            || record.Revision != run.Revision
            || !Equals(record.Scope, run.Scope)
            || !AccessScopesEqual(record.Access, run.Access)
            || !Equals(record.Snapshot, run.Snapshot)
            || !string.Equals(record.ArtifactId, declaration.ArtifactId, StringComparison.Ordinal)
            || !string.Equals(record.FileName, declaration.FileName, StringComparison.Ordinal)
            || !string.Equals(record.ContentType, declaration.ContentType, StringComparison.Ordinal)
            || record.ByteLength <= 0
            || !Sha256Digest.IsWellFormed(record.Identity.ContentHashSha256)
            || !string.Equals(
                record.Identity.ContentHashSha256,
                record.Identity.ContentHashSha256.ToLowerInvariant(),
                StringComparison.Ordinal))
        {
            throw new ReportingArtifactCatalogIntegrityException(
                $"Retained artifact '{run.RunId}/{record.ArtifactId}' conflicts with its immutable run or declaration.");
        }

        return new ReportingGovernedArtifactDescriptor(
            record.ArtifactId,
            record.FileName,
            record.ContentType,
            record.ByteLength,
            record.Identity.ContentHashSha256,
            declaration.Kind,
            declaration.Kind == ReportingDeclaredArtifactKind.Preview,
            $"/api/fund-structure/reporting/runs/{Uri.EscapeDataString(run.RunId)}/artifacts/{ReportingArtifactRouteToken.Encode(record.ArtifactId)}");
    }

    private static string BuildPackageId(GovernedReportingRun run)
    {
        var canonical = $"{run.Scope.TenantId}\n{run.RunId}\n{run.Revision.ToString(CultureInfo.InvariantCulture)}";
        return $"report-package-{ComputeSha256(Encoding.UTF8.GetBytes(canonical))}";
    }

    private static string BuildSourceCheckpointEvidence(string checkpointId, string checkpointHash) =>
        $"reporting-source-checkpoint:{checkpointId}:{checkpointHash.ToLowerInvariant()}";

    private static string ComputeSha256(ReadOnlySpan<byte> content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private static bool SameOptional(string? left, string? right) =>
        string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right)
        || string.Equals(left?.Trim(), right?.Trim(), StringComparison.Ordinal);

    private static void RequireText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }
    }
}
