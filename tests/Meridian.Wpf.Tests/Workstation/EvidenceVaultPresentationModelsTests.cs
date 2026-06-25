using FluentAssertions;
using Meridian.Contracts.Workstation;
using Meridian.Wpf.Models;
using Meridian.Wpf.Workstation.Models;

namespace Meridian.Wpf.Tests.Workstation;

public sealed class EvidenceVaultPresentationModelsTests
{
    [Fact]
    public void BuildDocumentQueue_ShouldProjectVaultDocumentsForDesktopReviewWithoutMutationCommands()
    {
        var document = new EvidenceDocumentDto(
            DocumentId: "doc:cash-support",
            FileName: "operating-bank-statement.csv",
            Classification: EvidenceDocumentClassificationDto.BankEvidence,
            SourceHashSha256: new string('a', 64),
            ReceivedAt: DateTimeOffset.Parse("2026-05-09T13:00:00Z"),
            SourceChannel: "upload",
            Actor: "fund-controller",
            TenantId: "tenant-alpha",
            Scope: "fund-alpha",
            ExtractionStatus: EvidenceExtractionStatusDto.NeedsReview,
            ObjectLinks:
            [
                new EvidenceDocumentLinkDto(
                    EvidenceDocumentLinkKindDto.CloseTask,
                    "close-task:cash-support",
                    "Cash support",
                    "/workstation/accounting/close/tasks/cash-support",
                    "blocks-close-readiness")
            ],
            ReviewerState: new EvidenceDocumentReviewStateDto(
                EvidenceDocumentReviewStatusDto.NeedsReview,
                "fund-controller",
                null,
                "Statement amount needs review."),
            AuditTrail:
            [
                new EvidenceDocumentAuditEventDto(
                    DateTimeOffset.Parse("2026-05-09T13:00:00Z"),
                    "fund-controller",
                    "DocumentIntakeRetained",
                    "Retained BankEvidence document.")
            ])
        {
            ManifestRoute = "/workstation/evidence/_vault/ev-close-2026-05/manifest.json",
            ExtractorId = "manual-metadata-v1"
        };
        var entry = new EvidenceVaultDocumentEntryDto(
            Document: document,
            VaultId: "ev-close-2026-05",
            SubjectKind: "accounting-record",
            SubjectId: "workflow-2026-05",
            ManifestRoute: "/workstation/evidence/_vault/ev-close-2026-05/manifest.json",
            RetainedAt: DateTimeOffset.Parse("2026-05-09T13:00:00Z"),
            StorageKind: "file-bundle",
            OpenRequestCount: 1,
            SupportRequests:
            [
                new EvidenceSupportRequestDto(
                    "support-request:validationissue:cash-support",
                    "ValidationIssue",
                    document.DocumentId,
                    "document",
                    EvidenceValidationSeverityDto.Warning,
                    "Open",
                    "Statement amount needs review.",
                    "manual-metadata-v1",
                    "workflow-2026-05",
                    null)
            ]);
        var manifest = new EvidenceManifestDto(
            ManifestId: "ev-close-2026-05",
            FrozenAt: DateTimeOffset.Parse("2026-05-09T13:01:00Z"),
            PackageKind: "accounting-record",
            PackageId: "workflow-2026-05",
            ContentHashSha256: new string('c', 64),
            Documents: [document],
            Requests:
            [
                new EvidenceRequestDto(
                    "support-request:validationissue:cash-support",
                    "ValidationIssue",
                    EvidenceValidationSeverityDto.Warning,
                    "Open",
                    "Statement amount needs review.",
                    "close",
                    "workflow-2026-05")
            ],
            ObjectLinks: document.ObjectLinks);

        var presentation = EvidenceVaultPresentationMapper.BuildDocumentQueue([entry], manifest);

        presentation.SummaryText.Should().Be("1 document retained");
        presentation.ReadinessText.Should().Be("1 document needs review; 1 open request");
        presentation.ReadinessTone.Should().Be(WorkstationReadinessTone.SignoffRequired);
        presentation.Tone.Should().Be(WorkspaceTone.Warning);
        presentation.HasFrozenManifestSnapshot.Should().BeTrue();
        presentation.Documents.Should().ContainSingle();

        var row = presentation.Documents[0];
        row.DocumentId.Should().Be(document.DocumentId);
        row.FileName.Should().Be("operating-bank-statement.csv");
        row.ClassificationText.Should().Be("Bank Evidence");
        row.ExtractionText.Should().Be("Needs Review");
        row.ReviewText.Should().Be("Needs Review by fund-controller");
        row.SourceHashText.Should().Be(new string('a', 12));
        row.SourceText.Should().Be("upload");
        row.ActorText.Should().Be("fund-controller");
        row.TenantScopeText.Should().Be("tenant-alpha / fund-alpha");
        row.ObjectLinksText.Should().Contain("Cash support (close-task:cash-support)");
        row.OpenRequestCountText.Should().Be("1 open request");
        row.ManifestRoute.Should().Be("/workstation/evidence/_vault/ev-close-2026-05/manifest.json");
        row.EvidenceLinks.Should().Contain(link => link.Label == "Frozen manifest");
        row.EvidenceLinks.Should().Contain(link => link.Target == "/workstation/accounting/close/tasks/cash-support");
    }

    [Fact]
    public void ToManifestSnapshot_ShouldExposeFrozenPackageCountsAndImmutableHash()
    {
        var document = new EvidenceDocumentDto(
            "doc:capital-notice",
            "capital-call-notice.pdf",
            EvidenceDocumentClassificationDto.CapitalNotice,
            new string('b', 64),
            DateTimeOffset.Parse("2026-05-10T15:00:00Z"),
            "imported-file-reference",
            "fund-ops",
            "tenant-alpha",
            "fund-alpha",
            EvidenceExtractionStatusDto.Accepted,
            [new EvidenceDocumentLinkDto(EvidenceDocumentLinkKindDto.Portfolio, "portfolio:alpha")],
            new EvidenceDocumentReviewStateDto(EvidenceDocumentReviewStatusDto.Accepted, "fund-controller"),
            []);
        var manifest = new EvidenceManifestDto(
            "manifest:close-2026-05",
            DateTimeOffset.Parse("2026-05-10T15:05:00Z"),
            "close-package",
            "close-package-2026-05",
            new string('d', 64),
            [document],
            [],
            document.ObjectLinks);

        var snapshot = EvidenceVaultPresentationMapper.ToManifestSnapshot(manifest);

        snapshot.ManifestId.Should().Be("manifest:close-2026-05");
        snapshot.PackageText.Should().Be("close-package close-package-2026-05");
        snapshot.FrozenText.Should().Be("Frozen 2026-05-10 15:05 UTC");
        snapshot.ContentHashText.Should().Be(new string('d', 12));
        snapshot.DocumentCountText.Should().Be("1 document");
        snapshot.RequestCountText.Should().Be("No open requests");
        snapshot.ObjectLinkCountText.Should().Be("1 object link");
        snapshot.ReadinessTone.Should().Be(WorkstationReadinessTone.EvidenceLinked);
        snapshot.Tone.Should().Be(WorkspaceTone.Success);
    }
}
