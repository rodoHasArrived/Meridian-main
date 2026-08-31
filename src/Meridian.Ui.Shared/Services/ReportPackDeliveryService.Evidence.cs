using Meridian.Contracts.Workstation;
using Meridian.Reporting;

namespace Meridian.Ui.Shared.Services;

public sealed partial class ReportPackDeliveryService
{
    private static ReportPackDeliveryEvidencePacketDto BuildReportingRunDeliveryEvidencePacket(
        ReportingOutputManifest manifest,
        ReportPackRunReadService.ReportPackDistributionPolicy policy,
        Guid reportId,
        string packageId,
        ReportPackDeliveryModeDto deliveryMode,
        DateTimeOffset deliveredAtUtc,
        IReadOnlyList<ReportPackDeliveryArtifactDto> artifacts,
        IReadOnlyList<ReportPackEvidenceLinkDto> artifactEvidenceLinks)
    {
        var sourceArtifacts = DistinctValues(manifest.Artifacts.IsDefault ? [] : manifest.Artifacts);
        var packageContents = DistinctValues(
            artifacts.Select(static artifact => artifact.ArtifactName)
                .Concat(sourceArtifacts.Select(static artifact => $"source-artifact:{artifact}")));
        var supportEvidenceIds = DistinctValues(
            artifactEvidenceLinks.Select(static link => link.EvidenceId)
                .Concat(sourceArtifacts.Select(static artifact => $"reporting-run-source:{artifact}")));

        return new ReportPackDeliveryEvidencePacketDto(
            PacketId: $"reporting-run-delivery:{packageId}",
            PacketKind: "ReportingRunDelivery",
            PackageId: packageId,
            ReportId: reportId,
            FundProfileId: "reporting-run",
            FundAccountId: manifest.TemplateId,
            Period: manifest.AsOfDate.ToString("yyyy-MM-dd"),
            PackageContents: packageContents,
            SupportEvidenceIds: supportEvidenceIds,
            RecipientList:
            [
                new ReportPackDeliveryRecipientDto(
                    policy.DistributionId,
                    policy.Recipient,
                    policy.RecipientRole,
                    policy.Channel)
            ],
            EntitlementScope: BuildEntitlementScope(manifest.AccessPolicy),
            ApprovalChain: [],
            DatasetVersion: manifest.RunId,
            TemplateVersion: manifest.TemplateId,
            DeliveryChannel: $"{deliveryMode} via {policy.Channel}",
            DeliveredAtUtc: deliveredAtUtc,
            DeliveryEvidence: artifactEvidenceLinks,
            RequestHistory:
            [
                $"reporting-run:{manifest.RunId}:{manifest.Trigger}:{manifest.Status}",
                $"schedule:{manifest.ScheduleId ?? "adhoc"}",
                $"delivery-request:{policy.DistributionId}"
            ],
            AuditEventReferences:
            [
                $"{manifest.RunId}:{manifest.AttemptCount}:RunGenerated"
            ],
            BlockedDownstreamOutputs: []);
    }
}
