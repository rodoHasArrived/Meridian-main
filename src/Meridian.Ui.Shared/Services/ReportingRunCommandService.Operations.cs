using System.Collections.Immutable;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Reporting;
using Meridian.Storage.Reporting;

namespace Meridian.Ui.Shared.Services;

internal sealed record ReportingRunOperation(string RunId, string RequestHash);

/// <summary>A verified failed outcome, distinct from an uncertain or unauthorized operation.</summary>
internal sealed class ReportingNoticeOperationFailedException(string runId)
    : ReportingGovernanceException("This retained notice operation failed. Inspect it and explicitly generate a new notice if needed.")
{
    public string RunId { get; } = runId;
}

public sealed partial class ReportingRunCommandService
{
    /// <summary>Trusted application seam. The generic Reporting HTTP request cannot select this mode.</summary>
    internal Task<ReportingRunResultDto> RunOperationAsync(ReportingRunRequestDto request,
        ReportingRunOperation operation, string actor, ReportAccessQueryContext access,
        ReportingGovernanceCallerContext caller, CancellationToken ct = default)
    {
        RequireOperationAuthority(operation, actor, access, caller);
        var parameters = request.Parameters
            ?? throw new ReportingGovernanceException("A reporting operation requires explicit source parameters.");
        var bindings = parameters.TemplateParameters.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        bindings[ReportingOperationIdentity.OperationIdParameter] = operation.RunId;
        bindings[ReportingOperationIdentity.RequestHashParameter] = operation.RequestHash;
        return RunCoreAsync(request with { Parameters = parameters with { TemplateParameters = bindings } },
            actor, access, caller, operation, ct);
    }

    /// <summary>
    /// A replay reads the durable original before reevaluating mutable inputs. Partial artifact or
    /// governance creation resumes from that frozen manifest; approved/released states are retained.
    /// </summary>
    internal async Task<ReportingRunResultDto?> TryRecoverOperationAsync(string templateId,
        ReportingRunOperation operation, string actor, ReportAccessQueryContext access,
        ReportingGovernanceCallerContext caller, CancellationToken ct = default)
    {
        RequireOperationAuthority(operation, actor, access, caller);
        EnsureReportingDeploymentReady();
        var manifest = _runStore!.GetManifest(access.TenantId!, operation.RunId);
        if (manifest is null)
            return null;
        ReportingOperationIdentity.RequireMatches(manifest, operation.RunId, operation.RequestHash,
            templateId, access.TenantId!, access.CompanyId);
        if (!ReportAccessPolicyEvaluator.Evaluate(manifest, access).IsAccessible)
            throw new ReportingGovernanceAuthorizationException("The retained operation is not accessible in the current authenticated scope.");
        if (manifest.Status == ReportingRunStatus.Failed)
            throw new ReportingNoticeOperationFailedException(manifest.RunId);
        if (!ReportPackRunReadService.HasCertifiedImmutableGovernanceBinding(manifest))
            throw new ReportingGovernanceAuthorizationException("The retained operation is not accessible in the current authenticated scope.");

        var waitForConcurrentCreation = false;
        for (var attempt = 0; ; attempt++)
        {
            GovernedReportingRun? governed;
            try
            { governed = await _governanceCoordinator!.GetAsync(manifest.RunId, caller, ct).ConfigureAwait(false); }
            catch (ReportingGovernanceNotFoundException) { governed = null; }
            if (governed?.ExecutionState == GovernedReportingExecutionState.Succeeded)
            {
                RequireOperationGovernanceBinding(governed, manifest);
                // Verify the retained package remains linked to the exact governed snapshot.
                _ = await _governanceCoordinator!.ListRetainedArtifactsAsync(manifest.RunId, caller, ct).ConfigureAwait(false);
                // Notice metadata and all output details come from the retained run, even if the
                // current catalog no longer exposes the version that originally created it.
                var retainedTemplate = new ReportingTemplateMetadata(manifest.TemplateId,
                    ReportingTemplateFamily.InvestorStatement, "Capital Call Notice",
                    manifest.ResolvedTemplate!.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    manifest.Sections.Select(section => section.SectionId).ToImmutableArray(),
                    System.Collections.Immutable.ImmutableDictionary<string, string>.Empty);
                var result = ProjectRun(manifest, _runStore.GetAudit(access.TenantId!, manifest.RunId), retainedTemplate);
                return new ReportingRunResultDto(result with
                {
                    Status = governed.GovernanceState.ToString(),
                    NextActions = result.NextActions?.Select(action => action with
                    {
                        IsEnabled = governed.GovernanceState == GovernedReportingState.Draft,
                        DisabledReason = governed.GovernanceState == GovernedReportingState.Draft
                            ? null : "The retained reporting run has already advanced beyond Draft."
                    }).ToArray()
                });
            }
            if (governed is not null)
                RequireOperationGovernanceBinding(governed, manifest);
            if (waitForConcurrentCreation)
            {
                if (attempt >= 100)
                    throw new ReportingGovernanceConcurrencyException("The original notice operation is still retaining its artifacts; retry the same operation identity.");
                await Task.Delay(100, ct).ConfigureAwait(false);
                continue;
            }
            try
            {
                governed = await _governanceCoordinator!.CreateFromCompletedCertifiedManifestAsync(
                    manifest.RunId, caller, ct).ConfigureAwait(false);
                RequireOperationGovernanceBinding(governed, manifest);
                if (governed.ExecutionState != GovernedReportingExecutionState.Succeeded)
                    throw new ReportingGovernancePersistenceException("The retained notice operation has not completed artifact retention.");
            }
            catch (ReportingGovernanceConcurrencyException) when (attempt < 100)
            {
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
            catch (ReportingGovernanceException exception) when (attempt < 100)
            {
                // A second host can finish governance or advance approval between the read and
                // create calls. Only a freshly read, exactly bound Succeeded outcome is recovered.
                var concurrent = await _governanceCoordinator!.GetAsync(manifest.RunId, caller, ct).ConfigureAwait(false);
                RequireOperationGovernanceBinding(concurrent, manifest);
                if (concurrent.ExecutionState == GovernedReportingExecutionState.Succeeded)
                    continue;
                // CreateRun's duplicate identity is the one non-concurrency exception that can
                // mean another host still owns this exact governance creation. Other failures
                // (including artifact/integrity/authorization failures) remain visible.
                if (governed is null && exception.GetType() == typeof(ReportingGovernanceException)
                    && exception.Message == $"Reporting run '{manifest.RunId}' already exists."
                    && concurrent.GovernanceState == GovernedReportingState.Draft
                    && concurrent.ExecutionState is GovernedReportingExecutionState.Queued or GovernedReportingExecutionState.Running)
                {
                    waitForConcurrentCreation = true;
                    await Task.Delay(100, ct).ConfigureAwait(false);
                }
                else
                    throw;
            }
        }
    }

    private void RequireOperationAuthority(ReportingRunOperation operation, string actor,
        ReportAccessQueryContext access, ReportingGovernanceCallerContext caller)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ReportingOperationIdentity.Validate(operation.RunId, operation.RequestHash);
        if (!access.RequireBoundScope || _runStore?.SupportsDurableCreateClaims != true || _governanceCoordinator is null)
            throw new ReportingGovernancePersistenceException("Stable notice operations require PostgreSQL Reporting run claims and canonical governance.");
        ValidateGovernanceCaller(access, caller, actor);
        if (!caller.Permissions.HasFlag(UserPermission.ManageReporting) && !caller.Permissions.HasFlag(UserPermission.AdminMaintenance))
            throw new ReportingGovernanceAuthorizationException("Notice operation recovery requires current reporting management permission.");
    }

    private static void RequireOperationGovernanceBinding(GovernedReportingRun run, ReportingOutputManifest manifest)
    {
        if (run.Scope != manifest.OperationalScope
            || run.Snapshot.SnapshotHash != manifest.CertifiedSnapshot?.SnapshotHash
            || run.Snapshot.ParametersHash != manifest.CertifiedSnapshot?.ParametersHash
            || run.Access.PolicyHash != manifest.ImmutableAccessScope?.PolicyHash)
            throw new ReportingGovernanceException("The notice operation's governed run does not match its original certified manifest.");
    }
}
