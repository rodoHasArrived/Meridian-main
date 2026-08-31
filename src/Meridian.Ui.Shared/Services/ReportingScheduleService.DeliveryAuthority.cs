using System.Collections.Immutable;
using System.Text;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Identity.Auth;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ReportingScheduleService
{
    private static bool IsPendingReleaseHandoffBoundToCurrentTargets(
        ReportingScheduleRecordDto schedule,
        ReportingScheduledReleaseHandoffDto handoff)
    {
        if (handoff.State != ReportingScheduledReleaseHandoffStateDto.PendingRelease
            || !HasValidDeliveryTargetsSnapshot(schedule)
            || string.IsNullOrWhiteSpace(schedule.TenantId)
            || string.IsNullOrWhiteSpace(schedule.CompanyId)
            || string.IsNullOrWhiteSpace(handoff.DeliveryTargetsSnapshotHash)
            || !string.Equals(
                handoff.DeliveryTargetsSnapshotHash,
                schedule.DeliveryTargetsSnapshotHash,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(handoff.TenantId, schedule.TenantId, StringComparison.Ordinal)
            || !string.Equals(handoff.CompanyId, schedule.CompanyId, StringComparison.Ordinal)
            || !string.Equals(handoff.ScheduleId, schedule.ScheduleId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(handoff.TemplateId, schedule.TemplateId, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var target in schedule.DeliveryTargets ?? [])
        {
            if (!string.Equals(
                    target.DistributionId,
                    handoff.TargetDistributionId,
                    StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(target.RecipientPrincipalId)
                || target.RecipientPrincipalKind is not { } recipientKind
                || !Enum.IsDefined(recipientKind)
                || target.DeliveryMode is not { } deliveryMode
                || !Enum.IsDefined(deliveryMode))
            {
                continue;
            }

            var reportingRecipientKind = ToReportingPrincipalKind(recipientKind);
            var expectedTransportId = deliveryMode == ReportPackDeliveryModeDto.EmailLink
                ? "http-relay"
                : "secure-portal";
            var selectedArtifactCount = (handoff.ArtifactIds ?? [])
                .Distinct(StringComparer.Ordinal)
                .Count();
            if (!string.Equals(handoff.DistributionId, $"scheduled:{handoff.HandoffId}", StringComparison.Ordinal)
                || !string.Equals(handoff.TransportId, expectedTransportId, StringComparison.Ordinal)
                || !string.Equals(
                    handoff.RecipientPrincipalId,
                    target.RecipientPrincipalId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    handoff.RecipientPrincipalKind,
                    reportingRecipientKind.ToString(),
                    StringComparison.Ordinal)
                || expectedTransportId == "http-relay"
                    && (handoff.GrantLifetimeSeconds != 1_800
                        || selectedArtifactCount == 0
                        || handoff.GrantMaxUses != selectedArtifactCount)
                || expectedTransportId != "http-relay"
                    && (handoff.GrantLifetimeSeconds is not null || handoff.GrantMaxUses is not null))
            {
                continue;
            }

            if (target.Formats is { Count: > 0 })
            {
                var retainedFormats = (handoff.RequestedFormats ?? [])
                    .Distinct()
                    .OrderBy(static format => format);
                var targetFormats = target.Formats
                    .Distinct()
                    .OrderBy(static format => format);
                if (!targetFormats.SequenceEqual(retainedFormats))
                {
                    continue;
                }
            }

            return true;
        }

        return false;
    }

    internal static ReportingScheduledArtifactSelection ResolveScheduledArtifactSelection(
        ReportingScheduleDeliveryTargetDto target,
        ReportingOutputManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(manifest);
        var declarations = ReportingArtifactDeclaration.Build(manifest);
        var primaryOutputs = declarations
            .Where(static artifact =>
                artifact.Kind == ReportingDeclaredArtifactKind.PrimaryOutput)
            .Select(artifact => new
            {
                Artifact = artifact,
                Format = ResolveArtifactFormat(artifact)
            })
            .ToArray();
        if (primaryOutputs.Length == 0
            || primaryOutputs.Select(static output => output.Format).Distinct().Count()
            != primaryOutputs.Length)
        {
            throw new InvalidDataException(
                $"Reporting run '{manifest.RunId}' must declare at least one uniquely formatted primary output.");
        }

        var availableFormats = primaryOutputs
            .Select(static output => output.Format)
            .ToArray();
        GovernanceReportArtifactFormatDto[] requestedFormats = target.Formats is { Count: > 0 }
            ? target.Formats
                .Distinct()
                .ToArray()
            : [];
        if (requestedFormats.Any(static format => !Enum.IsDefined(format)))
        {
            throw new InvalidDataException(
                $"Scheduled distribution '{target.DistributionId}' requests an unknown artifact format.");
        }

        if (requestedFormats.Length > 0
            && (requestedFormats.Length != availableFormats.Length
                || requestedFormats.Except(availableFormats).Any()))
        {
            throw new InvalidDataException(
                $"Scheduled distribution '{target.DistributionId}' requests unavailable output format(s) for run '{manifest.RunId}'; the exact primary outputs are {string.Join(", ", availableFormats)}.");
        }

        return new ReportingScheduledArtifactSelection(
            availableFormats,
            primaryOutputs.Select(static output => output.Artifact.ArtifactId).ToArray());
    }

    private static GovernanceReportArtifactFormatDto ResolveArtifactFormat(
        ReportingDeclaredArtifact artifact) =>
        artifact.ContentType switch
        {
            "application/pdf" => GovernanceReportArtifactFormatDto.Pdf,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" =>
                GovernanceReportArtifactFormatDto.Xlsx,
            "text/csv" => GovernanceReportArtifactFormatDto.Csv,
            "text/html" => GovernanceReportArtifactFormatDto.Html,
            _ when artifact.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase) =>
                GovernanceReportArtifactFormatDto.Json,
            _ => throw new InvalidDataException(
                $"Scheduled primary artifact '{artifact.ArtifactId}' has unsupported content type '{artifact.ContentType}'.")
        };

    private static bool SameReleaseHandoffDeclaration(
        ReportingScheduledReleaseHandoffDto retained,
        ReportingScheduledReleaseHandoffDto candidate) =>
        string.Equals(retained.HandoffId, candidate.HandoffId, StringComparison.Ordinal)
        && string.Equals(retained.TenantId, candidate.TenantId, StringComparison.Ordinal)
        && string.Equals(retained.CompanyId, candidate.CompanyId, StringComparison.Ordinal)
        && string.Equals(retained.ScheduleId, candidate.ScheduleId, StringComparison.Ordinal)
        && string.Equals(retained.RunId, candidate.RunId, StringComparison.Ordinal)
        && string.Equals(retained.TemplateId, candidate.TemplateId, StringComparison.Ordinal)
        && string.Equals(retained.DistributionId, candidate.DistributionId, StringComparison.Ordinal)
        && string.Equals(retained.TargetDistributionId, candidate.TargetDistributionId, StringComparison.Ordinal)
        && string.Equals(retained.TransportId, candidate.TransportId, StringComparison.Ordinal)
        && string.Equals(retained.RecipientPrincipalId, candidate.RecipientPrincipalId, StringComparison.Ordinal)
        && string.Equals(
            retained.RecipientPrincipalKind,
            candidate.RecipientPrincipalKind,
            StringComparison.Ordinal)
        && string.Equals(
            retained.DeliveryTargetsSnapshotHash,
            candidate.DeliveryTargetsSnapshotHash,
            StringComparison.OrdinalIgnoreCase)
        && string.Equals(retained.Destination, candidate.Destination, StringComparison.Ordinal)
        && string.Equals(retained.Subject, candidate.Subject, StringComparison.Ordinal)
        && string.Equals(retained.Body, candidate.Body, StringComparison.Ordinal)
        && (retained.RequestedFormats ?? []).SequenceEqual(candidate.RequestedFormats ?? [])
        && (retained.ArtifactIds ?? []).SequenceEqual(candidate.ArtifactIds ?? [], StringComparer.Ordinal)
        && retained.GrantLifetimeSeconds == candidate.GrantLifetimeSeconds
        && retained.GrantMaxUses == candidate.GrantMaxUses
        && retained.MaxAttempts == candidate.MaxAttempts;

    private static string BuildReleaseHandoffId(
        string tenantId,
        string companyId,
        string scheduleId,
        string runId,
        string targetDistributionId,
        ReportingAccessPrincipalKind recipientKind,
        string recipientPrincipalId)
    {
        var canonical = string.Join(
            "\n",
            tenantId,
            companyId,
            scheduleId,
            runId,
            targetDistributionId,
            recipientKind.ToString(),
            recipientPrincipalId.Trim());
        return Sha256Digest.ComputeUtf8(canonical);
    }

    private static ReportingAccessPrincipalScope ResolveScheduledRecipientPrincipal(
        ReportingScheduleDeliveryTargetDto target,
        ReportingAccessScope access)
    {
        if (string.IsNullOrWhiteSpace(target.RecipientPrincipalId)
            || target.RecipientPrincipalKind is null)
        {
            throw new InvalidDataException(
                $"Scheduled distribution '{target.DistributionId}' has no explicit typed recipient.");
        }

        var recipient = new ReportingAccessPrincipalScope(
            ToReportingPrincipalKind(target.RecipientPrincipalKind.Value),
            target.RecipientPrincipalId.Trim());
        if (access.Mode == ReportingGovernanceAccessMode.CompanyWide)
        {
            return recipient;
        }

        var allowed = (access.Principals.IsDefault
                ? Enumerable.Empty<ReportingAccessPrincipalScope>()
                : access.Principals)
            .Concat(access.AllowOwnerAccess && !string.IsNullOrWhiteSpace(access.OwnerPrincipalId)
                ? [new ReportingAccessPrincipalScope(
                    ReportingAccessPrincipalKind.User,
                    access.OwnerPrincipalId.Trim())]
                : []);
        if (!allowed.Any(candidate =>
                candidate.Kind == recipient.Kind
                && string.Equals(
                    candidate.PrincipalId,
                    recipient.PrincipalId,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                $"Scheduled distribution '{target.DistributionId}' recipient is outside the immutable run access policy.");
        }

        return recipient;
    }

    internal static ReportingAccessPrincipalScope? ResolveScheduledRecipientPrincipal(
        ReportingAccessScope access,
        string policyOwner)
    {
        var candidates = (access.Principals.IsDefault
                ? Enumerable.Empty<ReportingAccessPrincipalScope>()
                : access.Principals)
            .Concat(access.AllowOwnerAccess && !string.IsNullOrWhiteSpace(access.OwnerPrincipalId)
                ? [new ReportingAccessPrincipalScope(
                    ReportingAccessPrincipalKind.User,
                    access.OwnerPrincipalId.Trim())]
                : [])
            .Where(principal =>
                access.Mode != ReportingGovernanceAccessMode.Private
                || principal.Kind == ReportingAccessPrincipalKind.User)
            .DistinctBy(
                static principal => $"{(int)principal.Kind}:{principal.PrincipalId.Trim()}",
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!string.IsNullOrWhiteSpace(policyOwner))
        {
            var ownerMatches = candidates
                .Where(candidate => string.Equals(
                    candidate.PrincipalId,
                    policyOwner.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (ownerMatches.Length == 1)
            {
                return ownerMatches[0];
            }
            if (ownerMatches.Length > 1)
            {
                return null;
            }
        }

        return candidates.Length == 1 ? candidates[0] : null;
    }

    private static ReportAccessQueryContext BuildScheduledExecutionAccessContext(
        ReportingScheduleRecordDto schedule,
        string actor,
        ReportAccessQueryContext? requestContext)
    {
        if (string.IsNullOrWhiteSpace(schedule.TenantId)
            && string.IsNullOrWhiteSpace(schedule.CompanyId))
        {
            return requestContext ?? new ReportAccessQueryContext(actor);
        }

        if (!HasValidAccessPolicySnapshot(schedule)
            || !HasValidScheduledExecutionPrincipal(schedule))
        {
            throw new InvalidDataException(
                "The governed reporting schedule has no valid immutable execution authority snapshot.");
        }

        var immutableGroups = schedule.AccessPolicySnapshot!.Mode == ReportAccessModeDto.Restricted
            ? (schedule.AccessPolicySnapshot.Principals ?? [])
                .Where(static principal => principal.Kind == ReportAccessPrincipalKindDto.Group)
                .Select(static principal => principal.PrincipalId.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static principal => principal, StringComparer.Ordinal)
                .ToArray()
            : [];
        return new ReportAccessQueryContext(
            ActorPrincipalId: actor,
            GroupPrincipalIds: immutableGroups,
            CompanyId: schedule.CompanyId,
            HasGlobalOverride: false,
            TenantId: schedule.TenantId,
            RequireBoundScope: true);
    }

    private static ReportingGovernanceCallerContext BuildScheduledGovernanceCaller(
        ReportingScheduleRecordDto schedule,
        CertifiedReportingRunContext certified,
        string runId)
    {
        var actor = schedule.RequestedBy.Trim();
        var access = certified.AccessScope;
        var actorIsOwner = access.AllowOwnerAccess
            && !string.IsNullOrWhiteSpace(access.OwnerPrincipalId)
            && string.Equals(access.OwnerPrincipalId, actor, StringComparison.OrdinalIgnoreCase);
        var actorIsRestrictedPrincipal = access.Mode == ReportingGovernanceAccessMode.Restricted
            && access.Principals.Any(principal =>
                principal.Kind == ReportingAccessPrincipalKind.User
                && string.Equals(principal.PrincipalId, actor, StringComparison.OrdinalIgnoreCase));
        var actorIsPrivatePrincipal = access.Mode == ReportingGovernanceAccessMode.Private
            && access.Principals.Any(principal =>
                principal.Kind == ReportingAccessPrincipalKind.User
                && string.Equals(principal.PrincipalId, actor, StringComparison.OrdinalIgnoreCase));
        var authorized = access.Mode switch
        {
            ReportingGovernanceAccessMode.Private => actorIsOwner || actorIsPrivatePrincipal,
            ReportingGovernanceAccessMode.Restricted => actorIsOwner || actorIsRestrictedPrincipal,
            ReportingGovernanceAccessMode.CompanyWide => true,
            _ => false
        };
        if (!authorized)
        {
            throw new ReportingGovernanceAuthorizationException(
                "The persisted schedule actor is not the immutable report owner or a named restricted principal.");
        }

        return new ReportingGovernanceCallerContext(
            actor,
            certified.OperationalScope.TenantId,
            certified.OperationalScope.CompanyId,
            UserPermission.ManageReporting,
            ReportingCommandOrigin.ServicePrincipal,
            $"reporting-schedule:{schedule.ScheduleId}:{runId}",
            ImmutableArray<string>.Empty);
    }

    private static void ValidateScheduledGovernedRun(
        ReportingOutputManifest manifest,
        CertifiedReportingRunContext certified,
        GovernedReportingRun governedRun,
        bool requireInitialDraft)
    {
        ArgumentNullException.ThrowIfNull(governedRun);
        if (!string.Equals(governedRun.RunId, manifest.RunId, StringComparison.Ordinal)
            || governedRun.ExecutionState != GovernedReportingExecutionState.Succeeded
            || (requireInitialDraft && governedRun.GovernanceState != GovernedReportingState.Draft)
            || !string.Equals(governedRun.Scope.TenantId, certified.OperationalScope.TenantId, StringComparison.Ordinal)
            || !string.Equals(governedRun.Scope.CompanyId, certified.OperationalScope.CompanyId, StringComparison.Ordinal)
            || !string.Equals(governedRun.Snapshot.SnapshotId, certified.Snapshot.SnapshotId, StringComparison.Ordinal)
            || !string.Equals(governedRun.Snapshot.SnapshotHash, certified.Snapshot.SnapshotHash, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(governedRun.Access.PolicyHash, certified.AccessScope.PolicyHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new ReportingGovernanceException(
                "Canonical governance did not retain the scheduled run as the exact certified Succeeded/Draft aggregate.");
        }
    }

    private static ReportPackDeliveryModeDto ResolveScheduledDeliveryMode(string channel)
    {
        if (channel.Contains("email", StringComparison.OrdinalIgnoreCase))
        {
            return ReportPackDeliveryModeDto.EmailLink;
        }

        if (channel.Contains("portal", StringComparison.OrdinalIgnoreCase))
        {
            return ReportPackDeliveryModeDto.SecurePortal;
        }

        if (channel.Contains("vault", StringComparison.OrdinalIgnoreCase))
        {
            return ReportPackDeliveryModeDto.EvidenceVault;
        }

        return ReportPackDeliveryModeDto.SecurePortal; // non-email scheduled handoffs use the secure-portal transport
    }
}
