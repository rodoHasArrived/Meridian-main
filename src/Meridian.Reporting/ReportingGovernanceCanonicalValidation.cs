using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;

namespace Meridian.Reporting;

/// <summary>
/// Canonical semantic validation shared by the reporting domain and every persistence boundary.
/// A valid checksum proves only that bytes were retained unchanged; these checks prove that the
/// retained bytes still describe a valid, tenant-bound reporting lifecycle.
/// </summary>
public static class ReportingGovernanceCanonicalValidation
{
    public static void ValidateCreationRequest(ReportingRunCreationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireText(request.RunId, nameof(request.RunId));
        RequireText(request.SeriesId, nameof(request.SeriesId));
        RequireText(request.TemplateId, nameof(request.TemplateId));
        RequireText(request.TemplateVersion, nameof(request.TemplateVersion));
        ValidateScope(request.Scope);
        ValidateAccess(request.Access);
        ValidateSnapshot(request.Snapshot, request.Scope);
    }

    public static void ValidateImmutableRunShape(GovernedReportingRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        RequireText(run.RunId, nameof(run.RunId));
        RequireText(run.SeriesId, nameof(run.SeriesId));
        RequireText(run.TemplateId, nameof(run.TemplateId));
        RequireText(run.TemplateVersion, nameof(run.TemplateVersion));
        ValidateScope(run.Scope);
        ValidateAccess(run.Access);
        ValidateSnapshot(run.Snapshot, run.Scope);

        if (run.Revision == 1)
        {
            if (!string.IsNullOrWhiteSpace(run.RestatementOfRunId)
                || !string.IsNullOrWhiteSpace(run.RestatementRequestId))
            {
                throw new ReportingGovernanceException(
                    "The first reporting revision cannot have restatement lineage.");
            }

            ValidateAuthority(
                run.CreationAuthority,
                run.Scope,
                ReportingGovernancePermission.CreateRun,
                humanRequired: false,
                "creation authority");
        }
        else
        {
            RequireText(run.RestatementOfRunId, nameof(run.RestatementOfRunId));
            RequireText(run.RestatementRequestId, nameof(run.RestatementRequestId));
            ValidateAuthority(
                run.CreationAuthority,
                run.Scope,
                ReportingGovernancePermission.ApproveRestatement,
                humanRequired: true,
                "restatement draft creator authority");
        }

        ValidateUtc(run.CreatedAtUtc, "governed reporting run creation timestamp");
    }

    /// <summary>
    /// Validates the complete current aggregate, including receipt bindings and the exact audit
    /// transition sequence. Persistence implementations call this after hydrating audit history.
    /// </summary>
    public static void ValidateGovernedRun(
        GovernedReportingRun run,
        bool requireAuditTrail = true)
    {
        ValidateImmutableRunShape(run);

        if (run.Revision <= 0 || run.Version <= 0
            || !Enum.IsDefined(run.ExecutionState)
            || !Enum.IsDefined(run.GovernanceState))
        {
            throw new ReportingGovernanceException(
                "The reporting revision, aggregate version, or lifecycle state is invalid.");
        }

        if (run.GovernanceState >= GovernedReportingState.Validated
            && run.ExecutionState != GovernedReportingExecutionState.Succeeded)
        {
            throw new ReportingGovernanceException(
                "Validated reporting lifecycle state requires a successfully completed execution.");
        }

        switch (run.GovernanceState)
        {
            case GovernedReportingState.Draft:
                RequireAbsent(run.Readiness, run.Approval, run.Release, "Draft");
                break;
            case GovernedReportingState.Validated:
            case GovernedReportingState.InReview:
                if (run.Readiness is null || run.Approval is not null || run.Release is not null)
                {
                    throw new ReportingGovernanceException(
                        $"{run.GovernanceState} state requires readiness and forbids approval or release receipts.");
                }
                break;
            case GovernedReportingState.Approved:
                if (run.Readiness is null || run.Approval is null || run.Release is not null)
                {
                    throw new ReportingGovernanceException(
                        "Approved state requires readiness and approval, and forbids a release receipt.");
                }
                break;
            case GovernedReportingState.Released:
                if (run.Readiness is null || run.Approval is null || run.Release is null)
                {
                    throw new ReportingGovernanceException(
                        "Released state requires readiness, approval, and release receipts.");
                }
                break;
            default:
                throw new ReportingGovernanceException("The governed reporting state is invalid.");
        }

        if (run.Readiness is not null)
        {
            ValidateReadiness(run.Readiness, run);
        }

        if (run.Approval is not null)
        {
            ValidateApproval(run.Approval, run);
        }

        if (run.Release is not null)
        {
            ValidateRelease(run.Release, run);
        }

        if (requireAuditTrail)
        {
            ValidateRunAudit(run);
        }
    }

    public static void ValidateScope(ReportingOperationalScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        RequireText(scope.TenantId, nameof(scope.TenantId));
        RequireText(scope.OrganizationId, nameof(scope.OrganizationId));
        RequireText(scope.CompanyId, nameof(scope.CompanyId));
        RequireText(scope.FundId, nameof(scope.FundId));
        RequireText(scope.BookId, nameof(scope.BookId));
        RequireText(scope.PeriodId, nameof(scope.PeriodId));
    }

    public static void ValidateAccess(ReportingAccessScope access)
    {
        ArgumentNullException.ThrowIfNull(access);
        RequireText(access.PolicyId, nameof(access.PolicyId));
        RequireText(access.PolicyVersion, nameof(access.PolicyVersion));
        RequireSha256(access.PolicyHash, nameof(access.PolicyHash));

        if (!Enum.IsDefined(access.Mode))
        {
            throw new ReportingGovernanceException("Reporting access mode is invalid.");
        }

        if (access.Mode == ReportingGovernanceAccessMode.Private)
        {
            if (access.AllowOwnerAccess)
            {
                RequireText(access.OwnerPrincipalId, nameof(access.OwnerPrincipalId));
            }
            if (!access.AllowOwnerAccess && access.Principals.IsDefaultOrEmpty)
            {
                throw new ReportingGovernanceException(
                    "Private reporting access requires enabled owner access or a named user principal.");
            }
            if (!access.Principals.IsDefaultOrEmpty
                && access.Principals.Any(static principal =>
                    principal.Kind != ReportingAccessPrincipalKind.User))
            {
                throw new ReportingGovernanceException(
                    "Private reporting access can retain only named user principals.");
            }
        }

        if (access.Mode == ReportingGovernanceAccessMode.Restricted
            && (!access.AllowOwnerAccess || string.IsNullOrWhiteSpace(access.OwnerPrincipalId))
            && access.Principals.IsDefaultOrEmpty)
        {
            throw new ReportingGovernanceException(
                "Restricted reporting access requires an enabled owner or at least one typed principal.");
        }

        if (!access.Principals.IsDefaultOrEmpty
            && (access.Principals.Any(static principal =>
                    principal is null
                    || !Enum.IsDefined(principal.Kind)
                    || string.IsNullOrWhiteSpace(principal.PrincipalId))
                || access.Principals.Any(principal => access.Principals.Count(candidate =>
                    candidate.Kind == principal.Kind
                    && StringComparer.OrdinalIgnoreCase.Equals(
                        candidate.PrincipalId,
                        principal.PrincipalId)) > 1)))
        {
            throw new ReportingGovernanceException(
                "Reporting access principals must retain a valid kind, non-empty identity, and unique kind/identity pair.");
        }
    }

    public static void ValidateSnapshot(
        ReportingCertifiedSnapshotScope snapshot,
        ReportingOperationalScope scope)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(scope);
        RequireText(snapshot.SnapshotId, nameof(snapshot.SnapshotId));
        RequireSha256(snapshot.SnapshotHash, nameof(snapshot.SnapshotHash));
        RequireText(snapshot.ReconciliationCheckpointId, nameof(snapshot.ReconciliationCheckpointId));
        RequireText(snapshot.SourceCheckpointId, nameof(snapshot.SourceCheckpointId));
        RequireSha256(snapshot.SourceCheckpointHash, nameof(snapshot.SourceCheckpointHash));
        RequireSha256(snapshot.ReconciliationCheckpointHash, nameof(snapshot.ReconciliationCheckpointHash));
        RequireText(snapshot.ParametersCanonicalJson, nameof(snapshot.ParametersCanonicalJson));
        RequireSha256(snapshot.ParametersHash, nameof(snapshot.ParametersHash));
        ValidateUtc(snapshot.CapturedAtUtc, "certified snapshot capture timestamp");

        if (StringComparer.Ordinal.Equals(
                snapshot.SourceCheckpointId,
                snapshot.ReconciliationCheckpointId))
        {
            throw new ReportingGovernanceException(
                "The source and reconciliation checkpoint identities must be distinct.");
        }

        var parameterHash = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(snapshot.ParametersCanonicalJson!)))
            .ToLowerInvariant();
        if (!StringComparer.Ordinal.Equals(snapshot.ParametersHash, parameterHash))
        {
            throw new ReportingGovernanceException(
                "The immutable normalized parameter JSON does not match its declared SHA-256 hash.");
        }

        var parameters = ParseCanonicalParameters(snapshot.ParametersCanonicalJson!);
        if (snapshot.RequiresCertifiedLedgerPresentation
            != parameters.RequiresCertifiedLedgerPresentation)
        {
            throw new ReportingGovernanceException(
                "The certified ledger-presentation requirement does not match the immutable normalized parameter receipt.");
        }
        if (!StringComparer.Ordinal.Equals(parameters.FundId, scope.FundId)
            || !StringComparer.Ordinal.Equals(parameters.BookId, scope.BookId)
            || !StringComparer.Ordinal.Equals(parameters.PeriodId, scope.PeriodId))
        {
            throw new ReportingGovernanceException(
                "The immutable normalized parameters escaped the reporting fund, book, or period scope.");
        }

        if (!StringComparer.Ordinal.Equals(snapshot.TenantId, scope.TenantId)
            || !StringComparer.Ordinal.Equals(snapshot.OrganizationId, scope.OrganizationId)
            || !StringComparer.Ordinal.Equals(snapshot.CompanyId, scope.CompanyId)
            || !StringComparer.Ordinal.Equals(snapshot.FundId, scope.FundId)
            || !StringComparer.Ordinal.Equals(snapshot.BookId, scope.BookId)
            || !StringComparer.Ordinal.Equals(snapshot.PeriodId, scope.PeriodId))
        {
            throw new ReportingGovernanceException(
                "The certified snapshot scope must exactly match the reporting operational scope.");
        }
    }

    public static void ValidateReadiness(
        ReportingReadinessReceipt readiness,
        GovernedReportingRun run)
    {
        ArgumentNullException.ThrowIfNull(readiness);
        ArgumentNullException.ThrowIfNull(run);
        RequireText(readiness.ReceiptId, nameof(readiness.ReceiptId));
        RequireSha256(readiness.ReceiptHash, nameof(readiness.ReceiptHash));
        ValidateUtc(readiness.EvaluatedAtUtc, "readiness evaluation timestamp");

        var computedReceiptHash = ComputeReadinessReceiptHash(readiness);
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(readiness.ReceiptHash),
                Convert.FromHexString(computedReceiptHash)))
        {
            throw new ReportingGovernanceException(
                "The readiness receipt hash does not match its retained identity, snapshot, timestamp, checks, and evidence.");
        }

        if (!StringComparer.Ordinal.Equals(readiness.RunId, run.RunId)
            || !StringComparer.Ordinal.Equals(readiness.TenantId, run.Scope.TenantId)
            || !StringComparer.Ordinal.Equals(readiness.OrganizationId, run.Scope.OrganizationId)
            || !StringComparer.Ordinal.Equals(readiness.CompanyId, run.Scope.CompanyId)
            || !StringComparer.Ordinal.Equals(readiness.SnapshotId, run.Snapshot.SnapshotId)
            || !StringComparer.Ordinal.Equals(readiness.SnapshotHash, run.Snapshot.SnapshotHash))
        {
            throw new ReportingGovernanceException(
                "The readiness receipt is not bound to the exact run, tenant, organization, company, and certified snapshot.");
        }

        if (!readiness.IsReady
            || readiness.Checks.Any(static check =>
                string.IsNullOrWhiteSpace(check.CheckId)
                || !check.Passed
                || !string.IsNullOrWhiteSpace(check.FailureReason)
                || check.EvidenceIds.IsDefaultOrEmpty
                || check.EvidenceIds.Any(string.IsNullOrWhiteSpace)
                || check.EvidenceIds.Distinct(StringComparer.Ordinal).Count() != check.EvidenceIds.Length)
            || readiness.Checks.Select(static check => check.CheckId)
                .Distinct(StringComparer.Ordinal).Count() != readiness.Checks.Length)
        {
            throw new ReportingGovernanceException(
                "The readiness receipt must contain unique passing checks with unique retained evidence and no failure reason.");
        }
    }

    public static string ComputeReadinessReceiptHash(ReportingReadinessReceipt readiness)
    {
        ArgumentNullException.ThrowIfNull(readiness);
        var canonical = new StringBuilder();
        AppendCanonical(canonical, readiness.ReceiptId);
        AppendCanonical(canonical, readiness.RunId);
        AppendCanonical(canonical, readiness.TenantId);
        AppendCanonical(canonical, readiness.OrganizationId);
        AppendCanonical(canonical, readiness.CompanyId);
        AppendCanonical(canonical, readiness.SnapshotId);
        AppendCanonical(canonical, readiness.SnapshotHash);
        AppendCanonical(canonical, readiness.EvaluatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        var checks = readiness.Checks.IsDefault
            ? ImmutableArray<ReportingReadinessCheck>.Empty
            : readiness.Checks;
        AppendCanonical(canonical, checks.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var check in checks.OrderBy(static item => item.CheckId, StringComparer.Ordinal))
        {
            AppendCanonical(canonical, check.CheckId);
            AppendCanonical(canonical, check.Passed ? "true" : "false");
            AppendCanonical(canonical, check.FailureReason);
            var evidenceIds = check.EvidenceIds.IsDefault
                ? ImmutableArray<string>.Empty
                : check.EvidenceIds;
            AppendCanonical(canonical, evidenceIds.Length.ToString(CultureInfo.InvariantCulture));
            foreach (var evidenceId in evidenceIds.Order(StringComparer.Ordinal))
            {
                AppendCanonical(canonical, evidenceId);
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    public static string ComputeImmutableRunFingerprint(GovernedReportingRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var canonical = new StringBuilder();
        AppendCanonical(canonical, run.RunId);
        AppendCanonical(canonical, run.SeriesId);
        AppendCanonical(canonical, run.Revision.ToString(CultureInfo.InvariantCulture));
        AppendCanonical(canonical, run.TemplateId);
        AppendCanonical(canonical, run.TemplateVersion);
        AppendScope(canonical, run.Scope);
        AppendAccess(canonical, run.Access);
        AppendSnapshot(canonical, run.Snapshot);
        AppendAuthority(canonical, run.CreationAuthority);
        AppendCanonical(canonical, run.CreatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        AppendCanonical(canonical, run.RestatementOfRunId);
        AppendCanonical(canonical, run.RestatementRequestId);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    public static string BuildInitialRunAuditNote(GovernedReportingRun run) =>
        $"series={run.SeriesId};revision={run.Revision};creation={ComputeImmutableRunFingerprint(run)}";

    public static string BuildRestatementDraftAuditNote(GovernedReportingRun run) =>
        $"predecessor={run.RestatementOfRunId};request={run.RestatementRequestId};draft={run.RunId};revision={run.Revision};creation={ComputeImmutableRunFingerprint(run)}";

    public static string ComputeRestatementRequestFingerprint(
        ReportingRestatementRequest request,
        bool includeApproval)
    {
        ArgumentNullException.ThrowIfNull(request);
        var canonical = new StringBuilder();
        AppendCanonical(canonical, request.RequestId);
        AppendCanonical(canonical, request.PredecessorRunId);
        AppendCanonical(canonical, request.SeriesId);
        AppendCanonical(canonical, request.PredecessorRevision.ToString(CultureInfo.InvariantCulture));
        AppendCanonical(canonical, request.PredecessorVersion.ToString(CultureInfo.InvariantCulture));
        AppendCanonical(canonical, request.Reason);
        AppendChangedLines(canonical, request.RequestedChangedLines);
        AppendAuthority(canonical, request.RequestedBy);
        AppendCanonical(canonical, request.RequestedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        if (includeApproval)
        {
            AppendChangedLines(canonical, request.ChangedLines);
            AppendCanonical(canonical, ((int)request.State).ToString(CultureInfo.InvariantCulture));
            AppendCanonical(canonical, request.Version.ToString(CultureInfo.InvariantCulture));
            if (request.ApprovedBy is not null)
            {
                AppendAuthority(canonical, request.ApprovedBy);
            }
            else
            {
                AppendCanonical(canonical, null);
            }
            AppendCanonical(canonical, request.ApprovedAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            AppendCanonical(canonical, request.DraftRunId);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    public static string BuildRestatementRequestAuditNote(ReportingRestatementRequest request) =>
        $"request={ComputeRestatementRequestFingerprint(request, includeApproval: false)}";

    public static string BuildRestatementApprovalAuditNote(ReportingRestatementRequest request) =>
        $"draft={request.DraftRunId};approval={ComputeRestatementRequestFingerprint(request, includeApproval: true)}";

    public static string ComputeReleaseReceiptHash(ReportingReleaseReceipt release)
    {
        ArgumentNullException.ThrowIfNull(release);
        var canonical = new StringBuilder();
        AppendAuthority(canonical, release.Authority);
        AppendCanonical(canonical, release.ReleasedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        AppendCanonical(canonical, release.ManifestId);
        AppendCanonical(canonical, release.ManifestHash);
        var artifacts = release.Artifacts.IsDefault
            ? ImmutableArray<ReportingArtifactReference>.Empty
            : release.Artifacts;
        AppendCanonical(canonical, artifacts.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var artifact in artifacts.OrderBy(static item => item.ArtifactId, StringComparer.Ordinal))
        {
            AppendCanonical(canonical, artifact.ArtifactId);
            AppendCanonical(canonical, artifact.ArtifactHash);
            AppendCanonical(canonical, artifact.ByteLength.ToString(CultureInfo.InvariantCulture));
        }
        var evidenceIds = release.EvidenceIds.IsDefault
            ? ImmutableArray<string>.Empty
            : release.EvidenceIds;
        AppendCanonical(canonical, evidenceIds.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var evidenceId in evidenceIds.Order(StringComparer.Ordinal))
        {
            AppendCanonical(canonical, evidenceId);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    public static string BuildReleaseAuditNote(ReportingReleaseReceipt release) =>
        $"manifest={release.ManifestId};hash={release.ManifestHash};receipt={ComputeReleaseReceiptHash(release)}";

    public static void ValidateReleaseEvidence(ReportingReleaseEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ValidateReleasePayload(
            evidence.ManifestId,
            evidence.ManifestHash,
            evidence.Artifacts,
            evidence.EvidenceIds);
    }

    public static void ValidateFinalReleaseSnapshot(ReportingCertifiedSnapshotScope snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        RequireText(snapshot.ParametersCanonicalJson, nameof(snapshot.ParametersCanonicalJson));
        var parameters = ParseCanonicalParameters(snapshot.ParametersCanonicalJson!);
        if (parameters.Finality != ReportingFinalityDto.Final
            || !parameters.IncludeEvidenceAppendix)
        {
            throw new ReportingGovernanceException(
                "Release requires immutable Final parameters and the final evidence appendix; Draft-certified bytes cannot be released.");
        }
    }

    public static bool IsClientPackage(ReportingCertifiedSnapshotScope snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        RequireText(snapshot.ParametersCanonicalJson, nameof(snapshot.ParametersCanonicalJson));
        return ParseCanonicalParameters(snapshot.ParametersCanonicalJson!).OutputFormat
            == ReportingOutputFormatDto.ClientPackage;
    }

    public static void ValidateCompleteClientPackageRelease(
        string runId,
        ReportingCertifiedSnapshotScope snapshot,
        ImmutableArray<ReportingArtifactReference> artifacts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!IsClientPackage(snapshot))
        {
            return;
        }

        var normalizedRunId = runId.Trim();
        var pdfArtifactId = $"{normalizedRunId}.pdf";
        var workbookArtifactId = $"{normalizedRunId}.xlsx";
        if (artifacts.IsDefaultOrEmpty
            || artifacts.Count(artifact =>
                StringComparer.Ordinal.Equals(artifact.ArtifactId, pdfArtifactId)) != 1
            || artifacts.Count(artifact =>
                StringComparer.Ordinal.Equals(artifact.ArtifactId, workbookArtifactId)) != 1)
        {
            throw new ReportingGovernanceException(
                $"ClientPackage release for run '{normalizedRunId}' requires both immutable PDF and XLSX primary artifacts from the same certified package.");
        }
    }

    public static void ValidateChangedLines(
        ImmutableArray<ReportingRestatementChangedLine> changedLines)
    {
        if (changedLines.IsDefaultOrEmpty
            || changedLines.Any(static line =>
                string.IsNullOrWhiteSpace(line.LineKey)
                || line.PreviousValue is null
                || line.CurrentValue is null
                || StringComparer.Ordinal.Equals(line.PreviousValue, line.CurrentValue)
                || line.EvidenceIds.IsDefaultOrEmpty
                || line.EvidenceIds.Any(string.IsNullOrWhiteSpace)
                || line.EvidenceIds.Distinct(StringComparer.Ordinal).Count() != line.EvidenceIds.Length)
            || changedLines.Select(static line => line.LineKey)
                .Distinct(StringComparer.Ordinal).Count() != changedLines.Length)
        {
            throw new ReportingGovernanceException(
                "A restatement requires unique changed lines, different values, and unique retained evidence for every line.");
        }
    }

    /// <summary>Validates request-local state before any predecessor/draft lookup.</summary>
    public static void ValidateRestatementRequest(
        ReportingRestatementRequest request,
        bool requireAuditTrail = true)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireText(request.RequestId, nameof(request.RequestId));
        RequireText(request.PredecessorRunId, nameof(request.PredecessorRunId));
        RequireText(request.SeriesId, nameof(request.SeriesId));
        RequireText(request.Reason, nameof(request.Reason));
        ValidateChangedLines(request.ChangedLines);
        ValidateChangedLines(request.RequestedChangedLines);
        ValidateUtc(request.RequestedAtUtc, "restatement request timestamp");
        ValidateStandaloneAuthority(
            request.RequestedBy,
            ReportingGovernancePermission.RequestRestatement,
            humanRequired: true,
            "restatement requester authority");

        if (request.PredecessorRevision <= 0 || request.PredecessorVersion <= 0
            || !Enum.IsDefined(request.State))
        {
            throw new ReportingGovernanceException(
                "The restatement predecessor revision, predecessor version, or state is invalid.");
        }

        if (request.State == ReportingRestatementRequestState.PendingApproval)
        {
            if (request.Version != 1 || request.ApprovedBy is not null
                || request.ApprovedAtUtc is not null || request.DraftRunId is not null)
            {
                throw new ReportingGovernanceException(
                    "Pending restatement state must be version 1 and cannot retain approval or draft fields.");
            }
            if (!SameChangedLines(request.ChangedLines, request.RequestedChangedLines))
            {
                throw new ReportingGovernanceException(
                    "Pending restatement changed lines must exactly match the originally requested evidence.");
            }
        }
        else if (request.State == ReportingRestatementRequestState.Approved)
        {
            if (request.Version != 2 || request.ApprovedBy is null
                || request.ApprovedAtUtc is null || string.IsNullOrWhiteSpace(request.DraftRunId))
            {
                throw new ReportingGovernanceException(
                    "Approved restatement state must be version 2 and retain approval and draft binding fields.");
            }

            ValidateStandaloneAuthority(
                request.ApprovedBy,
                ReportingGovernancePermission.ApproveRestatement,
                humanRequired: true,
                "restatement approval authority");
            ValidateSameAuthorityScope(request.RequestedBy, request.ApprovedBy, "restatement authorities");
            if (StringComparer.OrdinalIgnoreCase.Equals(request.RequestedBy.ActorId, request.ApprovedBy.ActorId))
            {
                throw new ReportingGovernanceException(
                    "The restatement requester and approver must be independent human operators.");
            }

            ValidateUtc(request.ApprovedAtUtc.Value, "restatement approval timestamp");
            if (request.ApprovedAtUtc.Value < request.RequestedAtUtc)
            {
                throw new ReportingGovernanceException(
                    "Restatement approval cannot predate the request.");
            }
        }
        else
        {
            throw new ReportingGovernanceException("The restatement request state is invalid.");
        }

        if (requireAuditTrail)
        {
            ValidateRestatementAudit(request);
        }
    }

    private static bool SameChangedLines(
        ImmutableArray<ReportingRestatementChangedLine> left,
        ImmutableArray<ReportingRestatementChangedLine> right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < left.Length; index++)
        {
            var leftLine = left[index];
            var rightLine = right[index];
            if (!StringComparer.Ordinal.Equals(leftLine.LineKey, rightLine.LineKey)
                || !StringComparer.Ordinal.Equals(leftLine.PreviousValue, rightLine.PreviousValue)
                || !StringComparer.Ordinal.Equals(leftLine.CurrentValue, rightLine.CurrentValue)
                || !leftLine.EvidenceIds.SequenceEqual(rightLine.EvidenceIds, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Validates the durable relationship between a request, its immutable Released predecessor,
    /// and (after approval) the new revision created in the same transaction.
    /// </summary>
    public static void ValidateRestatementBinding(
        ReportingRestatementRequest request,
        GovernedReportingRun predecessor,
        GovernedReportingRun? draftRun)
    {
        ValidateRestatementRequest(request);
        ValidateGovernedRun(predecessor);

        if (predecessor.GovernanceState != GovernedReportingState.Released
            || predecessor.Release is null
            || !StringComparer.Ordinal.Equals(request.PredecessorRunId, predecessor.RunId)
            || !StringComparer.Ordinal.Equals(request.SeriesId, predecessor.SeriesId)
            || request.PredecessorRevision != predecessor.Revision
            || request.PredecessorVersion != predecessor.Version)
        {
            throw new ReportingGovernanceException(
                "The restatement request is not bound to the exact immutable Released predecessor revision.");
        }

        ValidateAuthority(
            request.RequestedBy,
            predecessor.Scope,
            ReportingGovernancePermission.RequestRestatement,
            humanRequired: true,
            "restatement requester authority");
        EnsureAccess(predecessor.Access, request.RequestedBy, "restatement requester");
        if (request.RequestedAtUtc < predecessor.Release.ReleasedAtUtc)
        {
            throw new ReportingGovernanceException(
                "A restatement request cannot predate release of its predecessor.");
        }

        if (request.State == ReportingRestatementRequestState.PendingApproval)
        {
            if (draftRun is not null)
            {
                throw new ReportingGovernanceException(
                    "A pending restatement request cannot be bound to a draft revision.");
            }
            return;
        }

        ValidateAuthority(
            request.ApprovedBy!,
            predecessor.Scope,
            ReportingGovernancePermission.ApproveRestatement,
            humanRequired: true,
            "restatement approval authority");
        EnsureAccess(predecessor.Access, request.ApprovedBy!, "restatement approver");
        if (draftRun is null)
        {
            throw new ReportingGovernanceException(
                "An approved restatement request must resolve its retained draft revision.");
        }

        ValidateGovernedRun(draftRun);
        if (!StringComparer.Ordinal.Equals(request.DraftRunId, draftRun.RunId)
            || !StringComparer.Ordinal.Equals(draftRun.RestatementRequestId, request.RequestId)
            || !StringComparer.Ordinal.Equals(draftRun.RestatementOfRunId, predecessor.RunId)
            || !StringComparer.Ordinal.Equals(draftRun.SeriesId, predecessor.SeriesId)
            || draftRun.Revision != checked(predecessor.Revision + 1)
            || !Equals(draftRun.Scope, predecessor.Scope)
            || !SameAccess(draftRun.Access, predecessor.Access)
            || !StringComparer.Ordinal.Equals(draftRun.TemplateId, predecessor.TemplateId)
            || !StringComparer.Ordinal.Equals(draftRun.TemplateVersion, predecessor.TemplateVersion)
            || !SameAuthority(draftRun.CreationAuthority, request.ApprovedBy!)
            || draftRun.CreatedAtUtc != request.ApprovedAtUtc)
        {
            throw new ReportingGovernanceException(
                "The approved restatement request and retained new revision lineage do not match exactly.");
        }

        if (StringComparer.Ordinal.Equals(draftRun.Snapshot.SnapshotId, predecessor.Snapshot.SnapshotId)
            || StringComparer.Ordinal.Equals(draftRun.Snapshot.SnapshotHash, predecessor.Snapshot.SnapshotHash)
            || draftRun.Snapshot.CapturedAtUtc > request.ApprovedAtUtc)
        {
            throw new ReportingGovernanceException(
                "The approved restatement draft requires a distinct certified snapshot captured no later than approval.");
        }

        var draftCreated = draftRun.AuditTrail[0];
        if (draftCreated.Action != ReportingGovernanceAuditAction.RestatementDraftCreated
            || !SameAuthority(draftCreated.Authority, request.ApprovedBy!)
            || !StringComparer.Ordinal.Equals(
                draftCreated.Note,
                BuildRestatementDraftAuditNote(draftRun)))
        {
            throw new ReportingGovernanceException(
                "The new reporting revision is not audit-bound to the approved restatement request and predecessor.");
        }
    }

    private static void ValidateApproval(
        ReportingApprovalReceipt approval,
        GovernedReportingRun run)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ValidateAuthority(
            approval.Authority,
            run.Scope,
            ReportingGovernancePermission.ApproveRun,
            humanRequired: true,
            "run approval authority");
        ValidateUtc(approval.ApprovedAtUtc, "run approval timestamp");
        RequireText(approval.DecisionNote, nameof(approval.DecisionNote));

        if (StringComparer.OrdinalIgnoreCase.Equals(run.CreationAuthority.ActorId, approval.Authority.ActorId)
            || (run.Access.Mode == ReportingGovernanceAccessMode.Private
                && StringComparer.OrdinalIgnoreCase.Equals(run.Access.OwnerPrincipalId, approval.Authority.ActorId)))
        {
            throw new ReportingGovernanceException(
                "Run approval violates maker-checker or private-owner independence.");
        }
    }

    private static void ValidateRelease(
        ReportingReleaseReceipt release,
        GovernedReportingRun run)
    {
        ArgumentNullException.ThrowIfNull(release);
        ValidateAuthority(
            release.Authority,
            run.Scope,
            ReportingGovernancePermission.ReleaseRun,
            humanRequired: true,
            "run release authority");
        ValidateUtc(release.ReleasedAtUtc, "run release timestamp");
        ValidateFinalReleaseSnapshot(run.Snapshot);
        ValidateReleasePayload(
            release.ManifestId,
            release.ManifestHash,
            release.Artifacts,
            release.EvidenceIds);
        ValidateCompleteClientPackageRelease(run.RunId, run.Snapshot, release.Artifacts);

        if (run.Approval is null
            || release.ReleasedAtUtc < run.Approval.ApprovedAtUtc
            || StringComparer.OrdinalIgnoreCase.Equals(
                release.Authority.ActorId,
                run.Approval.Authority.ActorId))
        {
            throw new ReportingGovernanceException(
                "Run release must follow approval and use an independent human release authority.");
        }
    }

    private static void ValidateReleasePayload(
        string manifestId,
        string manifestHash,
        ImmutableArray<ReportingArtifactReference> artifacts,
        ImmutableArray<string> evidenceIds)
    {
        RequireText(manifestId, nameof(manifestId));
        RequireSha256(manifestHash, nameof(manifestHash));
        if (artifacts.IsDefaultOrEmpty
            || artifacts.Any(static artifact =>
                string.IsNullOrWhiteSpace(artifact.ArtifactId)
                || !Sha256Digest.IsCanonical(artifact.ArtifactHash)
                || artifact.ByteLength <= 0)
            || artifacts.Select(static artifact => artifact.ArtifactId)
                .Distinct(StringComparer.Ordinal).Count() != artifacts.Length)
        {
            throw new ReportingGovernanceException(
                "Release artifacts must have unique identities, exact SHA-256 hashes, and positive byte lengths.");
        }

        ValidateUniqueText(evidenceIds, "release evidence", allowEmpty: false);
    }

    private static void ValidateRunAudit(GovernedReportingRun run)
    {
        if (run.AuditTrail.IsDefaultOrEmpty
            || run.AuditTrail.Length != run.Version
            || !ReportingGovernanceAuditChain.Verify(run.AuditTrail))
        {
            throw new ReportingGovernanceException(
                "The governed reporting audit chain is incomplete or invalid.");
        }

        GovernedReportingExecutionState? execution = null;
        GovernedReportingState? governance = null;
        DateTimeOffset? priorTimestamp = null;
        for (var index = 0; index < run.AuditTrail.Length; index++)
        {
            var entry = run.AuditTrail[index];
            ValidateAuditEntryCommon(
                entry,
                ReportingGovernanceAuditAggregateKind.Run,
                run.RunId,
                run.Scope,
                priorTimestamp);
            priorTimestamp = entry.OccurredAtUtc;

            var expectedPermission = PermissionFor(entry.Action);
            var humanRequired = entry.Action is ReportingGovernanceAuditAction.RunApproved
                or ReportingGovernanceAuditAction.RunReleased
                or ReportingGovernanceAuditAction.RestatementDraftCreated;
            ValidateAuthority(
                entry.Authority,
                run.Scope,
                expectedPermission,
                humanRequired,
                $"{entry.Action} audit authority");
            if (entry.PermissionUsed != expectedPermission)
            {
                throw new ReportingGovernanceException(
                    $"Audit action '{entry.Action}' did not retain its exact permission.");
            }

            switch (entry.Action)
            {
                case ReportingGovernanceAuditAction.RunCreated:
                    if (index != 0 || run.Revision != 1
                        || !SameAuthority(entry.Authority, run.CreationAuthority)
                        || !StringComparer.Ordinal.Equals(
                            entry.Note,
                            BuildInitialRunAuditNote(run)))
                    {
                        throw new ReportingGovernanceException(
                            "RunCreated must be the first event and match the first revision creation authority.");
                    }
                    RequireTransition(entry, null, GovernedReportingExecutionState.Queued, null, GovernedReportingState.Draft);
                    execution = GovernedReportingExecutionState.Queued;
                    governance = GovernedReportingState.Draft;
                    break;
                case ReportingGovernanceAuditAction.RestatementDraftCreated:
                    if (index != 0 || run.Revision <= 1
                        || string.IsNullOrWhiteSpace(run.RestatementOfRunId)
                        || string.IsNullOrWhiteSpace(run.RestatementRequestId)
                        || !SameAuthority(entry.Authority, run.CreationAuthority)
                        || !StringComparer.Ordinal.Equals(entry.Note, BuildRestatementDraftAuditNote(run)))
                    {
                        throw new ReportingGovernanceException(
                            "RestatementDraftCreated must be the first event of a later revision.");
                    }
                    RequireTransition(entry, null, GovernedReportingExecutionState.Queued, null, GovernedReportingState.Draft);
                    execution = GovernedReportingExecutionState.Queued;
                    governance = GovernedReportingState.Draft;
                    break;
                case ReportingGovernanceAuditAction.ExecutionStarted:
                    if (execution is not (GovernedReportingExecutionState.Queued or GovernedReportingExecutionState.Failed)
                        || governance != GovernedReportingState.Draft)
                    {
                        throw InvalidAuditTransition(entry.Action);
                    }
                    RequireTransition(entry, execution, GovernedReportingExecutionState.Running, null, null);
                    RequireNoNote(entry);
                    execution = GovernedReportingExecutionState.Running;
                    break;
                case ReportingGovernanceAuditAction.ExecutionSucceeded:
                    if (execution != GovernedReportingExecutionState.Running
                        || governance != GovernedReportingState.Draft)
                    {
                        throw InvalidAuditTransition(entry.Action);
                    }
                    RequireTransition(entry, execution, GovernedReportingExecutionState.Succeeded, null, null);
                    RequireNoNote(entry);
                    execution = GovernedReportingExecutionState.Succeeded;
                    break;
                case ReportingGovernanceAuditAction.ExecutionFailed:
                    if (execution != GovernedReportingExecutionState.Running
                        || governance != GovernedReportingState.Draft)
                    {
                        throw InvalidAuditTransition(entry.Action);
                    }
                    RequireTransition(entry, execution, GovernedReportingExecutionState.Failed, null, null);
                    RequireText(entry.Note, "execution failure reason");
                    execution = GovernedReportingExecutionState.Failed;
                    break;
                case ReportingGovernanceAuditAction.ExecutionCancelled:
                    if (execution is not (GovernedReportingExecutionState.Queued or GovernedReportingExecutionState.Running)
                        || governance != GovernedReportingState.Draft)
                    {
                        throw InvalidAuditTransition(entry.Action);
                    }
                    RequireTransition(entry, execution, GovernedReportingExecutionState.Cancelled, null, null);
                    RequireText(entry.Note, "execution cancellation reason");
                    execution = GovernedReportingExecutionState.Cancelled;
                    break;
                case ReportingGovernanceAuditAction.RunValidated:
                    if (execution != GovernedReportingExecutionState.Succeeded
                        || governance != GovernedReportingState.Draft
                        || run.Readiness is null
                        || entry.OccurredAtUtc < run.Readiness.EvaluatedAtUtc
                        || !StringComparer.Ordinal.Equals(
                            entry.Note,
                            $"readiness={run.Readiness.ReceiptId};hash={run.Readiness.ReceiptHash}"))
                    {
                        throw InvalidAuditTransition(entry.Action);
                    }
                    RequireTransition(entry, null, null, governance, GovernedReportingState.Validated);
                    governance = GovernedReportingState.Validated;
                    break;
                case ReportingGovernanceAuditAction.RunSubmitted:
                    if (governance != GovernedReportingState.Validated)
                    {
                        throw InvalidAuditTransition(entry.Action);
                    }
                    RequireTransition(entry, null, null, governance, GovernedReportingState.InReview);
                    RequireNoNote(entry);
                    governance = GovernedReportingState.InReview;
                    break;
                case ReportingGovernanceAuditAction.RunApproved:
                    if (governance != GovernedReportingState.InReview || run.Approval is null
                        || entry.OccurredAtUtc != run.Approval.ApprovedAtUtc
                        || !SameAuthority(entry.Authority, run.Approval.Authority)
                        || !StringComparer.Ordinal.Equals(entry.Note, run.Approval.DecisionNote))
                    {
                        throw InvalidAuditTransition(entry.Action);
                    }
                    RequireTransition(entry, null, null, governance, GovernedReportingState.Approved);
                    governance = GovernedReportingState.Approved;
                    break;
                case ReportingGovernanceAuditAction.RunReleased:
                    if (governance != GovernedReportingState.Approved || run.Release is null
                        || entry.OccurredAtUtc != run.Release.ReleasedAtUtc
                        || !SameAuthority(entry.Authority, run.Release.Authority)
                        || !StringComparer.Ordinal.Equals(
                            entry.Note,
                            BuildReleaseAuditNote(run.Release)))
                    {
                        throw InvalidAuditTransition(entry.Action);
                    }
                    RequireTransition(entry, null, null, governance, GovernedReportingState.Released);
                    governance = GovernedReportingState.Released;
                    break;
                default:
                    throw new ReportingGovernanceException(
                        $"Run audit contains invalid action '{entry.Action}'.");
            }
        }

        if (execution != run.ExecutionState || governance != run.GovernanceState
            || run.AuditTrail[0].OccurredAtUtc != run.CreatedAtUtc)
        {
            throw new ReportingGovernanceException(
                "The reporting audit sequence does not reconstruct the retained current state and creation timestamp.");
        }
    }

    private static void ValidateRestatementAudit(ReportingRestatementRequest request)
    {
        if (request.AuditTrail.IsDefaultOrEmpty
            || request.AuditTrail.Length != request.Version
            || !ReportingGovernanceAuditChain.Verify(request.AuditTrail))
        {
            throw new ReportingGovernanceException(
                "The restatement audit chain is incomplete or invalid.");
        }

        DateTimeOffset? priorTimestamp = null;
        ReportingRestatementRequestState? state = null;
        for (var index = 0; index < request.AuditTrail.Length; index++)
        {
            var entry = request.AuditTrail[index];
            ValidateAuditEntryCommon(
                entry,
                ReportingGovernanceAuditAggregateKind.RestatementRequest,
                request.RequestId,
                request.RequestedBy,
                priorTimestamp);
            priorTimestamp = entry.OccurredAtUtc;
            if (entry.FromExecutionState is not null || entry.ToExecutionState is not null
                || entry.FromGovernanceState is not null || entry.ToGovernanceState is not null)
            {
                throw new ReportingGovernanceException(
                    "Restatement audit events cannot contain run lifecycle transitions.");
            }

            if (entry.Action == ReportingGovernanceAuditAction.RestatementRequested)
            {
                if (index != 0 || entry.PermissionUsed != ReportingGovernancePermission.RequestRestatement
                    || entry.FromRestatementState is not null
                    || entry.ToRestatementState != ReportingRestatementRequestState.PendingApproval
                    || entry.OccurredAtUtc != request.RequestedAtUtc
                    || !SameAuthority(entry.Authority, request.RequestedBy)
                    || !StringComparer.Ordinal.Equals(entry.Note, BuildRestatementRequestAuditNote(request)))
                {
                    throw InvalidAuditTransition(entry.Action);
                }
                state = ReportingRestatementRequestState.PendingApproval;
            }
            else if (entry.Action == ReportingGovernanceAuditAction.RestatementApproved)
            {
                if (index != 1 || state != ReportingRestatementRequestState.PendingApproval
                    || request.ApprovedBy is null || request.ApprovedAtUtc is null
                    || entry.PermissionUsed != ReportingGovernancePermission.ApproveRestatement
                    || entry.FromRestatementState != ReportingRestatementRequestState.PendingApproval
                    || entry.ToRestatementState != ReportingRestatementRequestState.Approved
                    || entry.OccurredAtUtc != request.ApprovedAtUtc
                    || !SameAuthority(entry.Authority, request.ApprovedBy)
                    || !StringComparer.Ordinal.Equals(entry.Note, BuildRestatementApprovalAuditNote(request)))
                {
                    throw InvalidAuditTransition(entry.Action);
                }
                state = ReportingRestatementRequestState.Approved;
            }
            else
            {
                throw new ReportingGovernanceException(
                    $"Restatement audit contains invalid action '{entry.Action}'.");
            }
        }

        if (state != request.State)
        {
            throw new ReportingGovernanceException(
                "The restatement audit sequence does not reconstruct the retained current state.");
        }
    }

    private static void ValidateAuditEntryCommon(
        ReportingGovernanceAuditEntry entry,
        ReportingGovernanceAuditAggregateKind kind,
        string aggregateId,
        ReportingOperationalScope scope,
        DateTimeOffset? priorTimestamp)
    {
        if (entry.AggregateKind != kind
            || !StringComparer.Ordinal.Equals(entry.AggregateId, aggregateId))
        {
            throw new ReportingGovernanceException(
                "A reporting audit event escaped its aggregate identity.");
        }

        ValidateUtc(entry.OccurredAtUtc, "reporting audit timestamp");
        if (priorTimestamp is not null && entry.OccurredAtUtc < priorTimestamp.Value)
        {
            throw new ReportingGovernanceException(
                "Reporting audit timestamps must be monotonic.");
        }

        ValidateAuthority(entry.Authority, scope, entry.PermissionUsed, false, "audit authority");
    }

    private static void ValidateAuditEntryCommon(
        ReportingGovernanceAuditEntry entry,
        ReportingGovernanceAuditAggregateKind kind,
        string aggregateId,
        ReportingAuthorityScope requestScope,
        DateTimeOffset? priorTimestamp)
    {
        if (entry.AggregateKind != kind
            || !StringComparer.Ordinal.Equals(entry.AggregateId, aggregateId))
        {
            throw new ReportingGovernanceException(
                "A restatement audit event escaped its aggregate identity.");
        }

        ValidateUtc(entry.OccurredAtUtc, "restatement audit timestamp");
        if (priorTimestamp is not null && entry.OccurredAtUtc < priorTimestamp.Value)
        {
            throw new ReportingGovernanceException(
                "Restatement audit timestamps must be monotonic.");
        }

        ValidateStandaloneAuthority(entry.Authority, entry.PermissionUsed, false, "restatement audit authority");
        ValidateSameAuthorityScope(requestScope, entry.Authority, "restatement audit authority");
    }

    private static ReportingGovernancePermission PermissionFor(ReportingGovernanceAuditAction action) =>
        action switch
        {
            ReportingGovernanceAuditAction.RunCreated => ReportingGovernancePermission.CreateRun,
            ReportingGovernanceAuditAction.RestatementDraftCreated => ReportingGovernancePermission.ApproveRestatement,
            ReportingGovernanceAuditAction.ExecutionStarted
                or ReportingGovernanceAuditAction.ExecutionSucceeded
                or ReportingGovernanceAuditAction.ExecutionFailed
                or ReportingGovernanceAuditAction.ExecutionCancelled => ReportingGovernancePermission.ExecuteRun,
            ReportingGovernanceAuditAction.RunValidated => ReportingGovernancePermission.ValidateRun,
            ReportingGovernanceAuditAction.RunSubmitted => ReportingGovernancePermission.SubmitRun,
            ReportingGovernanceAuditAction.RunApproved => ReportingGovernancePermission.ApproveRun,
            ReportingGovernanceAuditAction.RunReleased => ReportingGovernancePermission.ReleaseRun,
            _ => throw new ReportingGovernanceException(
                $"Audit action '{action}' is not valid for a governed reporting run.")
        };

    private static void ValidateAuthority(
        ReportingAuthorityScope authority,
        ReportingOperationalScope scope,
        ReportingGovernancePermission requiredPermission,
        bool humanRequired,
        string label)
    {
        ValidateStandaloneAuthority(authority, requiredPermission, humanRequired, label);
        if (!StringComparer.Ordinal.Equals(authority.TenantId, scope.TenantId)
            || !StringComparer.Ordinal.Equals(authority.OrganizationId, scope.OrganizationId)
            || !StringComparer.Ordinal.Equals(authority.CompanyId, scope.CompanyId))
        {
            throw new ReportingGovernanceException(
                $"The {label} escaped the immutable tenant, organization, or company scope.");
        }
    }

    private static void ValidateStandaloneAuthority(
        ReportingAuthorityScope authority,
        ReportingGovernancePermission requiredPermission,
        bool humanRequired,
        string label)
    {
        ArgumentNullException.ThrowIfNull(authority);
        RequireText(authority.ActorId, nameof(authority.ActorId));
        RequireText(authority.TenantId, nameof(authority.TenantId));
        RequireText(authority.OrganizationId, nameof(authority.OrganizationId));
        RequireText(authority.CompanyId, nameof(authority.CompanyId));
        RequireText(authority.CorrelationId, nameof(authority.CorrelationId));
        ValidateUniqueText(authority.PrincipalIds, "authority principals", allowEmpty: true);

        if (!Enum.IsDefined(authority.Origin)
            || authority.Permissions.IsDefaultOrEmpty
            || authority.Permissions.Any(static permission => !Enum.IsDefined(permission))
            || authority.Permissions.Distinct().Count() != authority.Permissions.Length
            || !authority.Permissions.Contains(requiredPermission))
        {
            throw new ReportingGovernanceException(
                $"The {label} is invalid or lacks exact '{requiredPermission}' permission.");
        }

        if (humanRequired && authority.Origin != ReportingCommandOrigin.HumanOperator)
        {
            throw new ReportingGovernanceException(
                $"The {label} must originate from a human operator.");
        }
    }

    private static void ValidateSameAuthorityScope(
        ReportingAuthorityScope left,
        ReportingAuthorityScope right,
        string label)
    {
        if (!StringComparer.Ordinal.Equals(left.TenantId, right.TenantId)
            || !StringComparer.Ordinal.Equals(left.OrganizationId, right.OrganizationId)
            || !StringComparer.Ordinal.Equals(left.CompanyId, right.CompanyId))
        {
            throw new ReportingGovernanceException(
                $"The {label} do not share the same tenant, organization, and company scope.");
        }
    }

    private static void EnsureAccess(
        ReportingAccessScope access,
        ReportingAuthorityScope authority,
        string label)
    {
        var allowed = access.Mode switch
        {
            ReportingGovernanceAccessMode.CompanyWide => true,
            ReportingGovernanceAccessMode.Private =>
                (access.AllowOwnerAccess
                    && StringComparer.OrdinalIgnoreCase.Equals(access.OwnerPrincipalId, authority.ActorId))
                || (!access.Principals.IsDefaultOrEmpty
                    && access.Principals.Any(principal =>
                        principal.Kind == ReportingAccessPrincipalKind.User
                        && authority.Matches(principal))),
            ReportingGovernanceAccessMode.Restricted =>
                access.AllowOwnerAccess
                && !string.IsNullOrWhiteSpace(access.OwnerPrincipalId)
                && StringComparer.OrdinalIgnoreCase.Equals(access.OwnerPrincipalId, authority.ActorId)
                || (!access.Principals.IsDefaultOrEmpty && access.Principals.Any(authority.Matches)),
            _ => false
        };
        if (!allowed)
        {
            throw new ReportingGovernanceException(
                $"The {label} is outside the immutable reporting access scope.");
        }
    }

    private static void RequireTransition(
        ReportingGovernanceAuditEntry entry,
        GovernedReportingExecutionState? fromExecution,
        GovernedReportingExecutionState? toExecution,
        GovernedReportingState? fromGovernance,
        GovernedReportingState? toGovernance)
    {
        if (entry.FromExecutionState != fromExecution
            || entry.ToExecutionState != toExecution
            || entry.FromGovernanceState != fromGovernance
            || entry.ToGovernanceState != toGovernance
            || entry.FromRestatementState is not null
            || entry.ToRestatementState is not null)
        {
            throw InvalidAuditTransition(entry.Action);
        }
    }

    private static ReportingGovernanceException InvalidAuditTransition(
        ReportingGovernanceAuditAction action) =>
        new($"Audit action '{action}' is inconsistent with the retained lifecycle sequence or receipt.");

    private static void RequireNoNote(ReportingGovernanceAuditEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Note))
        {
            throw new ReportingGovernanceException(
                $"Audit action '{entry.Action}' cannot retain an unexpected note.");
        }
    }

    private static void RequireAbsent(
        ReportingReadinessReceipt? readiness,
        ReportingApprovalReceipt? approval,
        ReportingReleaseReceipt? release,
        string state)
    {
        if (readiness is not null || approval is not null || release is not null)
        {
            throw new ReportingGovernanceException(
                $"{state} state cannot retain later lifecycle receipts.");
        }
    }

    private static bool SameAccess(ReportingAccessScope left, ReportingAccessScope right) =>
        StringComparer.Ordinal.Equals(left.PolicyId, right.PolicyId)
        && StringComparer.Ordinal.Equals(left.PolicyVersion, right.PolicyVersion)
        && left.Mode == right.Mode
        && StringComparer.Ordinal.Equals(left.OwnerPrincipalId, right.OwnerPrincipalId)
        && left.AllowOwnerAccess == right.AllowOwnerAccess
        && left.Principals.SequenceEqual(right.Principals)
        && StringComparer.Ordinal.Equals(left.PolicyHash, right.PolicyHash);

    private static bool SameAuthority(ReportingAuthorityScope left, ReportingAuthorityScope right) =>
        StringComparer.Ordinal.Equals(left.ActorId, right.ActorId)
        && StringComparer.Ordinal.Equals(left.TenantId, right.TenantId)
        && StringComparer.Ordinal.Equals(left.OrganizationId, right.OrganizationId)
        && StringComparer.Ordinal.Equals(left.CompanyId, right.CompanyId)
        && Normalize(left.Permissions).SequenceEqual(Normalize(right.Permissions))
        && left.Origin == right.Origin
        && StringComparer.Ordinal.Equals(left.CorrelationId, right.CorrelationId)
        && Normalize(left.PrincipalIds).SequenceEqual(
            Normalize(right.PrincipalIds),
            StringComparer.Ordinal);

    private static ImmutableArray<T> Normalize<T>(ImmutableArray<T> values) =>
        values.IsDefault ? ImmutableArray<T>.Empty : values;

    private static CanonicalParameterBinding ParseCanonicalParameters(string canonicalJson)
    {
        try
        {
            using var document = JsonDocument.Parse(canonicalJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new ReportingGovernanceException(
                    "The immutable normalized parameters must be a JSON object.");
            }

            var parameterScope = root.GetProperty("scope");
            var fundId = RequireJsonString(parameterScope, "fundProfileId");
            _ = ParseEnum<ReportingEntityScopeKindDto>(parameterScope, "entityScopeKind");
            var periodId = RequireJsonString(root, "periodId");
            if (!DateOnly.TryParseExact(
                    RequireJsonString(root, "asOfDate"),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _))
            {
                throw new ReportingGovernanceException(
                    "The immutable normalized reporting as-of date is invalid.");
            }

            var ledgerBookId = ReadOptionalJsonString(root, "ledgerBookId");
            var ledgerBookCode = ReadOptionalJsonString(root, "ledgerBookCode");
            var bookId = ledgerBookId ?? ledgerBookCode;
            RequireText(bookId, "ledgerBookId or ledgerBookCode");
            _ = ParseEnum<ReportingAccountingBasisDto>(root, "accountingBasis");
            RequireText(RequireJsonString(root, "presentationCurrency"), "presentationCurrency");
            _ = ParseEnum<ReportingConsolidationLevelDto>(root, "consolidationLevel");
            var outputFormat = ParseEnum<ReportingOutputFormatDto>(root, "outputFormat");
            var finality = ParseEnum<ReportingFinalityDto>(root, "finality");
            _ = root.GetProperty("includeSupportingSchedules").GetBoolean();
            var includeEvidenceAppendix = root.GetProperty("includeEvidenceAppendix").GetBoolean();
            var requiresCertifiedLedgerPresentation =
                root.TryGetProperty("requiresCertifiedLedgerPresentation", out var requirement)
                && requirement.GetBoolean();
            if (root.GetProperty("templateParameters").ValueKind != JsonValueKind.Object)
            {
                throw new ReportingGovernanceException(
                    "The immutable normalized template parameters must be a JSON object.");
            }

            return new CanonicalParameterBinding(
                fundId,
                bookId!,
                periodId,
                outputFormat,
                finality,
                includeEvidenceAppendix,
                requiresCertifiedLedgerPresentation);
        }
        catch (ReportingGovernanceException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ReportingGovernanceException(
                $"The immutable normalized reporting parameters are malformed: {exception.Message}");
        }
    }

    private static TEnum ParseEnum<TEnum>(JsonElement root, string propertyName)
        where TEnum : struct, Enum
    {
        var value = RequireJsonString(root, propertyName);
        if (!Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            throw new ReportingGovernanceException(
                $"The immutable normalized reporting parameter '{propertyName}' is invalid.");
        }
        return parsed;
    }

    private static string RequireJsonString(JsonElement root, string propertyName)
    {
        var value = ReadOptionalJsonString(root, propertyName);
        RequireText(value, propertyName);
        return value!;
    }

    private static string? ReadOptionalJsonString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return null;
        }
        return property.ValueKind == JsonValueKind.Null ? null : property.GetString();
    }

    private static void ValidateUniqueText(
        ImmutableArray<string> values,
        string label,
        bool allowEmpty)
    {
        if ((!allowEmpty && values.IsDefaultOrEmpty)
            || (!values.IsDefaultOrEmpty
                && (values.Any(string.IsNullOrWhiteSpace)
                    || values.Distinct(StringComparer.Ordinal).Count() != values.Length)))
        {
            throw new ReportingGovernanceException(
                $"The {label} must be non-empty and unique.");
        }
    }

    private static void ValidateUtc(DateTimeOffset value, string label)
    {
        if (value == default || value.Offset != TimeSpan.Zero)
        {
            throw new ReportingGovernanceException(
                $"The {label} must be a non-default UTC value.");
        }
    }

    private static void RequireText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ReportingGovernanceException($"'{name}' is required.");
        }
    }

    private static void AppendCanonical(StringBuilder target, string? value)
    {
        var normalized = value ?? string.Empty;
        target.Append(normalized.Length.ToString(CultureInfo.InvariantCulture));
        target.Append(':');
        target.Append(normalized);
        target.Append('|');
    }

    private static void AppendScope(StringBuilder target, ReportingOperationalScope scope)
    {
        AppendCanonical(target, scope.TenantId);
        AppendCanonical(target, scope.OrganizationId);
        AppendCanonical(target, scope.CompanyId);
        AppendCanonical(target, scope.FundId);
        AppendCanonical(target, scope.BookId);
        AppendCanonical(target, scope.PeriodId);
    }

    private static void AppendAccess(StringBuilder target, ReportingAccessScope access)
    {
        AppendCanonical(target, access.PolicyId);
        AppendCanonical(target, access.PolicyVersion);
        AppendCanonical(target, ((int)access.Mode).ToString(CultureInfo.InvariantCulture));
        AppendCanonical(target, access.OwnerPrincipalId);
        AppendCanonical(target, access.AllowOwnerAccess ? "true" : "false");
        var principals = access.Principals.IsDefault
            ? ImmutableArray<ReportingAccessPrincipalScope>.Empty
            : access.Principals;
        AppendCanonical(target, principals.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var principal in principals
                     .OrderBy(static item => item.Kind)
                     .ThenBy(static item => item.PrincipalId, StringComparer.Ordinal))
        {
            AppendCanonical(target, ((int)principal.Kind).ToString(CultureInfo.InvariantCulture));
            AppendCanonical(target, principal.PrincipalId);
        }
        AppendCanonical(target, access.PolicyHash);
    }

    private static void AppendSnapshot(StringBuilder target, ReportingCertifiedSnapshotScope snapshot)
    {
        AppendCanonical(target, snapshot.TenantId);
        AppendCanonical(target, snapshot.OrganizationId);
        AppendCanonical(target, snapshot.CompanyId);
        AppendCanonical(target, snapshot.FundId);
        AppendCanonical(target, snapshot.BookId);
        AppendCanonical(target, snapshot.PeriodId);
        AppendCanonical(target, snapshot.SnapshotId);
        AppendCanonical(target, snapshot.SnapshotHash);
        AppendCanonical(target, snapshot.ReconciliationCheckpointId);
        AppendCanonical(target, snapshot.CapturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        AppendCanonical(target, snapshot.SourceCheckpointId);
        AppendCanonical(target, snapshot.SourceCheckpointHash);
        AppendCanonical(target, snapshot.ReconciliationCheckpointHash);
        AppendCanonical(target, snapshot.ParametersCanonicalJson);
        AppendCanonical(target, snapshot.ParametersHash);
        if (snapshot.RequiresCertifiedLedgerPresentation)
        {
            AppendCanonical(target, "requires-certified-ledger-presentation");
        }
    }

    private static void AppendAuthority(StringBuilder target, ReportingAuthorityScope authority)
    {
        AppendCanonical(target, authority.ActorId);
        AppendCanonical(target, authority.TenantId);
        AppendCanonical(target, authority.OrganizationId);
        AppendCanonical(target, authority.CompanyId);
        var permissions = authority.Permissions.IsDefault
            ? ImmutableArray<ReportingGovernancePermission>.Empty
            : authority.Permissions;
        AppendCanonical(target, permissions.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var permission in permissions.Order())
        {
            AppendCanonical(target, ((int)permission).ToString(CultureInfo.InvariantCulture));
        }
        AppendCanonical(target, ((int)authority.Origin).ToString(CultureInfo.InvariantCulture));
        AppendCanonical(target, authority.CorrelationId);
        var principals = authority.PrincipalIds.IsDefault
            ? ImmutableArray<string>.Empty
            : authority.PrincipalIds;
        AppendCanonical(target, principals.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var principal in principals.Order(StringComparer.Ordinal))
        {
            AppendCanonical(target, principal);
        }
    }

    private static void AppendChangedLines(
        StringBuilder target,
        ImmutableArray<ReportingRestatementChangedLine> changedLines)
    {
        var lines = changedLines.IsDefault
            ? ImmutableArray<ReportingRestatementChangedLine>.Empty
            : changedLines;
        AppendCanonical(target, lines.Length.ToString(CultureInfo.InvariantCulture));
        foreach (var line in lines.OrderBy(static item => item.LineKey, StringComparer.Ordinal))
        {
            AppendCanonical(target, line.LineKey);
            AppendCanonical(target, line.PreviousValue);
            AppendCanonical(target, line.CurrentValue);
            var evidenceIds = line.EvidenceIds.IsDefault
                ? ImmutableArray<string>.Empty
                : line.EvidenceIds;
            AppendCanonical(target, evidenceIds.Length.ToString(CultureInfo.InvariantCulture));
            foreach (var evidenceId in evidenceIds.Order(StringComparer.Ordinal))
            {
                AppendCanonical(target, evidenceId);
            }
        }
    }

    private static void RequireSha256(string? value, string name)
    {
        if (!Sha256Digest.IsCanonical(value))
        {
            throw new ReportingGovernanceException(
                $"'{name}' must be a lowercase SHA-256 value.");
        }
    }

    private sealed record CanonicalParameterBinding(
        string FundId,
        string BookId,
        string PeriodId,
        ReportingOutputFormatDto OutputFormat,
        ReportingFinalityDto Finality,
        bool IncludeEvidenceAppendix,
        bool RequiresCertifiedLedgerPresentation);
}
