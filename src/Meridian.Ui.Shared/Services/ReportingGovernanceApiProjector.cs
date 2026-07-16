using System.Collections.Immutable;
using Meridian.Contracts.Reporting;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Storage.Reporting;

namespace Meridian.Ui.Shared.Services;

/// <summary>Projects domain governance records into version-stable workstation API contracts.</summary>
public static class ReportingGovernanceApiProjector
{
    public static GovernedReportingRunDto ProjectRun(GovernedReportingRun run) =>
        ProjectRun(run, caller: null);

    public static GovernedReportingRunDto ProjectRun(
        GovernedReportingRun run,
        ReportingGovernanceCallerContext? caller)
    {
        ArgumentNullException.ThrowIfNull(run);
        var normalizedParameters = DeserializeParameters(run);

        return new GovernedReportingRunDto(
            run.RunId,
            run.SeriesId,
            run.Revision,
            run.TemplateId,
            run.TemplateVersion,
            new ReportingGovernanceOperationalScopeDto(
                run.Scope.TenantId,
                run.Scope.OrganizationId,
                run.Scope.CompanyId,
                run.Scope.FundId,
                run.Scope.BookId,
                run.Scope.PeriodId),
            new ReportingGovernanceAccessScopeDto(
                run.Access.PolicyId,
                run.Access.PolicyVersion,
                run.Access.Mode.ToString(),
                run.Access.OwnerPrincipalId,
                run.Access.AllowOwnerAccess,
                Safe(run.Access.Principals)
                    .Select(static principal => new ReportingGovernanceAccessPrincipalDto(
                        principal.Kind.ToString(),
                        principal.PrincipalId))
                    .ToArray(),
                run.Access.PolicyHash),
            new ReportingGovernanceCertifiedSnapshotDto(
                run.Snapshot.SnapshotId,
                run.Snapshot.SnapshotHash,
                run.Snapshot.ReconciliationCheckpointId,
                run.Snapshot.CapturedAtUtc,
                run.Snapshot.SourceCheckpointId,
                run.Snapshot.SourceCheckpointHash,
                run.Snapshot.ReconciliationCheckpointHash,
                run.Snapshot.ParametersCanonicalJson,
                run.Snapshot.ParametersHash),
            Project(run.CreationAuthority),
            run.CreatedAtUtc,
            run.RestatementOfRunId,
            run.ExecutionState.ToString(),
            run.GovernanceState.ToString(),
            run.Version,
            run.Readiness is null ? null : Project(run.Readiness),
            run.Approval is null
                ? null
                : new ReportingGovernanceApprovalDto(
                    Project(run.Approval.Authority),
                    run.Approval.ApprovedAtUtc,
                    run.Approval.DecisionNote),
            run.Release is null ? null : Project(run.Release),
            Safe(run.AuditTrail).Select(Project).ToArray(),
            normalizedParameters,
            caller is null ? [] : ProjectRunActions(run, caller, normalizedParameters));
    }

    public static ReportingGovernanceRestatementDto ProjectRestatement(ReportingRestatementRequest request) =>
        ProjectRestatement(request, caller: null);

    public static ReportingGovernanceRestatementDto ProjectRestatement(
        ReportingRestatementRequest request,
        ReportingGovernanceCallerContext? caller)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ReportingGovernanceRestatementDto(
            request.RequestId,
            request.PredecessorRunId,
            request.SeriesId,
            request.PredecessorRevision,
            request.PredecessorVersion,
            request.Reason,
            Safe(request.ChangedLines)
                .Select(static line => new ReportingGovernanceChangedLineDto(
                    line.LineKey,
                    line.PreviousValue,
                    line.CurrentValue,
                    Safe(line.EvidenceIds)))
                .ToArray(),
            Project(request.RequestedBy),
            request.RequestedAtUtc,
            request.State.ToString(),
            request.Version,
            request.ApprovedBy is null ? null : Project(request.ApprovedBy),
            request.ApprovedAtUtc,
            request.DraftRunId,
            Safe(request.AuditTrail).Select(Project).ToArray(),
            caller is null ? [] : ProjectRestatementActions(request, caller));
    }

    public static ReportingGovernanceRestatementApprovalDto ProjectRestatementApproval(
        ReportingRestatementApprovalResult result) =>
        ProjectRestatementApproval(result, caller: null);

    public static ReportingGovernanceRestatementApprovalDto ProjectRestatementApproval(
        ReportingRestatementApprovalResult result,
        ReportingGovernanceCallerContext? caller)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ReportingGovernanceRestatementApprovalDto(
            ProjectRestatement(result.Request, caller),
            ProjectRun(result.DraftRun, caller));
    }

    public static ReportingGovernanceSeriesHistoryDto ProjectSeriesHistory(
        ReportingGovernanceSeriesHistory history,
        ReportingGovernanceCallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(caller);
        return new ReportingGovernanceSeriesHistoryDto(
            history.SeriesId,
            history.Runs.Select(run => ProjectRun(run, caller)).ToArray(),
            history.RestatementRequests.Select(request => ProjectRestatement(request, caller)).ToArray());
    }

    public static IReadOnlyList<ReportingGovernanceRestatementDto> ProjectRestatements(
        IReadOnlyList<ReportingRestatementRequest> requests,
        ReportingGovernanceCallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(requests);
        ArgumentNullException.ThrowIfNull(caller);
        return requests.Select(request => ProjectRestatement(request, caller)).ToArray();
    }

    private static ReportingRunParametersDto? DeserializeParameters(GovernedReportingRun run)
    {
        if (string.IsNullOrWhiteSpace(run.Snapshot.ParametersCanonicalJson))
        {
            return null;
        }

        try
        {
            return ReportingRunCertificationService.DeserializeParameters(
                run.Snapshot.ParametersCanonicalJson);
        }
        catch (Exception exception)
        {
            throw new ReportingGovernancePersistenceException(
                $"Governed reporting run '{run.RunId}' contains malformed immutable normalized parameters.",
                exception);
        }
    }

    private static IReadOnlyList<ReportingGovernanceActionAvailabilityDto> ProjectRunActions(
        GovernedReportingRun run,
        ReportingGovernanceCallerContext caller,
        ReportingRunParametersDto? normalizedParameters)
    {
        var accessBlocker = ResolveAccessBlocker(run, caller);
        return
        [
            Availability(
                "ValidateRun",
                run.Version,
                accessBlocker
                ?? RequirePermission(caller, UserPermission.ManageReporting, "ManageReporting")
                ?? (run.ExecutionState != GovernedReportingExecutionState.Succeeded
                    ? "Run execution must be Succeeded before validation."
                    : run.GovernanceState != GovernedReportingState.Draft
                        ? "Only a Draft run can be validated."
                        : null)),
            Availability(
                "SubmitRun",
                run.Version,
                accessBlocker
                ?? RequirePermission(caller, UserPermission.ManageReporting, "ManageReporting")
                ?? (run.GovernanceState != GovernedReportingState.Validated
                    ? "Only a Validated run can be submitted."
                    : null)),
            Availability(
                "ApproveRun",
                run.Version,
                accessBlocker
                ?? RequirePermission(caller, UserPermission.ApproveReporting, "ApproveReporting")
                ?? RequireHuman(caller)
                ?? (run.GovernanceState != GovernedReportingState.InReview
                    ? "Only an InReview run can be approved."
                    : string.Equals(run.CreationAuthority.ActorId, caller.ActorId.Trim(), StringComparison.Ordinal)
                        ? "The run creator cannot approve the same run."
                        : run.Access.Mode == ReportingGovernanceAccessMode.Private
                          && string.Equals(run.Access.OwnerPrincipalId, caller.ActorId.Trim(), StringComparison.Ordinal)
                            ? "The private report owner cannot approve the same run."
                            : null)),
            Availability(
                "ReleaseRun",
                run.Version,
                accessBlocker
                ?? RequirePermission(caller, UserPermission.DeliverReporting, "DeliverReporting")
                ?? RequireHuman(caller)
                ?? (run.ExecutionState != GovernedReportingExecutionState.Succeeded
                    || run.GovernanceState != GovernedReportingState.Approved
                    || run.Approval is null
                        ? "Only a successfully executed and Approved run can be released."
                        : string.Equals(run.Approval.Authority.ActorId, caller.ActorId.Trim(), StringComparison.Ordinal)
                            ? "The run approver cannot release the same run."
                            : normalizedParameters is null
                              || normalizedParameters.Finality != ReportingFinalityDto.Final
                              || !normalizedParameters.IncludeEvidenceAppendix
                                ? "Release requires a Final-certified run with the immutable evidence appendix; Draft-certified bytes cannot be released."
                                : null)),
            Availability(
                "RequestRestatement",
                run.Version,
                accessBlocker
                ?? RequirePermission(caller, UserPermission.ManageReporting, "ManageReporting")
                ?? (run.GovernanceState != GovernedReportingState.Released || run.Release is null
                    ? "Only a Released run can be restated."
                    : null))
        ];
    }

    private static IReadOnlyList<ReportingGovernanceActionAvailabilityDto> ProjectRestatementActions(
        ReportingRestatementRequest request,
        ReportingGovernanceCallerContext caller)
    {
        var scopeBlocker = !string.Equals(request.RequestedBy.TenantId, caller.TenantId.Trim(), StringComparison.Ordinal)
            || !string.Equals(request.RequestedBy.CompanyId, caller.CompanyId?.Trim(), StringComparison.Ordinal)
                ? "The restatement request is outside the caller tenant/company scope."
                : null;
        return
        [
            Availability(
                "ApproveRestatement",
                request.Version,
                scopeBlocker
                ?? RequirePermission(caller, UserPermission.ApproveReporting, "ApproveReporting")
                ?? RequireHuman(caller)
                ?? (request.State != ReportingRestatementRequestState.PendingApproval
                    ? "Only a PendingApproval restatement request can be approved."
                    : string.Equals(request.RequestedBy.ActorId, caller.ActorId.Trim(), StringComparison.Ordinal)
                        ? "The restatement requester cannot approve the same request."
                        : null))
        ];
    }

    private static ReportingGovernanceActionAvailabilityDto Availability(
        string action,
        long expectedVersion,
        string? blocker) =>
        new(action, blocker is null, blocker, expectedVersion);

    private static string? RequirePermission(
        ReportingGovernanceCallerContext caller,
        UserPermission permission,
        string label) =>
        caller.Permissions.HasFlag(UserPermission.AdminMaintenance)
        || caller.Permissions.HasFlag(permission)
            ? null
            : $"The caller requires {label} permission.";

    private static string? RequireHuman(ReportingGovernanceCallerContext caller) =>
        caller.Origin == ReportingCommandOrigin.HumanOperator
            ? null
            : "This action requires a human operator.";

    private static string? ResolveAccessBlocker(
        GovernedReportingRun run,
        ReportingGovernanceCallerContext caller)
    {
        if (!string.Equals(run.Scope.TenantId, caller.TenantId.Trim(), StringComparison.Ordinal)
            || !string.Equals(run.Scope.CompanyId, caller.CompanyId?.Trim(), StringComparison.Ordinal))
        {
            return "The run is outside the caller tenant/company scope.";
        }

        var actor = caller.ActorId.Trim();
        bool Matches(ReportingAccessPrincipalScope principal) =>
            principal.Kind switch
            {
                ReportingAccessPrincipalKind.User =>
                    string.Equals(principal.PrincipalId, actor, StringComparison.OrdinalIgnoreCase),
                ReportingAccessPrincipalKind.Group =>
                    !caller.PrincipalIds.IsDefaultOrEmpty
                    && caller.PrincipalIds.Any(group =>
                        string.Equals(group?.Trim(), principal.PrincipalId, StringComparison.OrdinalIgnoreCase)),
                ReportingAccessPrincipalKind.Company =>
                    string.Equals(caller.CompanyId?.Trim(), principal.PrincipalId, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        var allowed = run.Access.Mode switch
        {
            ReportingGovernanceAccessMode.CompanyWide => true,
            ReportingGovernanceAccessMode.Private =>
                (run.Access.AllowOwnerAccess
                    && string.Equals(run.Access.OwnerPrincipalId, actor, StringComparison.OrdinalIgnoreCase))
                || Safe(run.Access.Principals).Any(principal =>
                    principal.Kind == ReportingAccessPrincipalKind.User
                    && Matches(principal)),
            ReportingGovernanceAccessMode.Restricted =>
                (run.Access.AllowOwnerAccess
                    && !string.IsNullOrWhiteSpace(run.Access.OwnerPrincipalId)
                    && string.Equals(run.Access.OwnerPrincipalId, actor, StringComparison.OrdinalIgnoreCase))
                || Safe(run.Access.Principals).Any(Matches),
            _ => false
        };
        return allowed ? null : "The caller is not included in the immutable reporting access scope.";
    }

    private static ReportingGovernanceAuthorityDto Project(ReportingAuthorityScope authority) =>
        new(
            authority.ActorId,
            authority.TenantId,
            authority.OrganizationId,
            authority.CompanyId,
            Safe(authority.Permissions).Select(static permission => permission.ToString()).ToArray(),
            authority.Origin.ToString(),
            authority.CorrelationId,
            Safe(authority.PrincipalIds));

    private static ReportingGovernanceReadinessDto Project(ReportingReadinessReceipt readiness) =>
        new(
            readiness.ReceiptId,
            readiness.ReceiptHash,
            readiness.EvaluatedAtUtc,
            readiness.IsReady,
            Safe(readiness.Checks)
                .Select(static check => new ReportingGovernanceReadinessCheckDto(
                    check.CheckId,
                    check.Passed,
                    Safe(check.EvidenceIds),
                    check.FailureReason))
                .ToArray());

    private static ReportingGovernanceReleaseDto Project(ReportingReleaseReceipt release) =>
        new(
            Project(release.Authority),
            release.ReleasedAtUtc,
            release.ManifestId,
            release.ManifestHash,
            Safe(release.Artifacts)
                .Select(static artifact => new ReportingGovernanceArtifactDto(
                    artifact.ArtifactId,
                    artifact.ArtifactHash,
                    artifact.ByteLength))
                .ToArray(),
            Safe(release.EvidenceIds));

    private static ReportingGovernanceAuditEntryDto Project(ReportingGovernanceAuditEntry entry) =>
        new(
            entry.EventId,
            entry.AggregateKind.ToString(),
            entry.AggregateId,
            entry.AggregateVersion,
            entry.OccurredAtUtc,
            entry.Action.ToString(),
            Project(entry.Authority),
            entry.PermissionUsed.ToString(),
            entry.FromExecutionState?.ToString(),
            entry.ToExecutionState?.ToString(),
            entry.FromGovernanceState?.ToString(),
            entry.ToGovernanceState?.ToString(),
            entry.FromRestatementState?.ToString(),
            entry.ToRestatementState?.ToString(),
            entry.Note,
            entry.PreviousHash,
            entry.Hash);

    private static T[] Safe<T>(System.Collections.Immutable.ImmutableArray<T> values) =>
        values.IsDefault ? [] : values.ToArray();
}
