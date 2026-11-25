using System.Text;
using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Evidence;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.Ui.Evidence;

public sealed class EvidenceAttestationSubjectResolverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-tests", "evidence-attestation", Guid.NewGuid().ToString("N"));
    private const string Tenant = "tenant-one";
    private const string Company = "company-one";

    [Fact]
    public async Task AcknowledgmentResolvesExternalSourceWithoutChangingOrdinaryReviewOrImporter()
    {
        var intake = await RetainAsync();
        var before = await Store().TryGetVaultIdentityAsync(intake.VaultIdentity.VaultId, Tenant, Company);
        var snapshot = await ResolveAsync(intake);
        var after = await Store().TryGetVaultIdentityAsync(intake.VaultIdentity.VaultId, Tenant, Company);

        snapshot.Should().NotBeNull();
        snapshot!.Eligible.Should().BeTrue();
        snapshot.SubjectId.Should().Be($"{intake.VaultIdentity.VaultId}/{intake.Document!.DocumentId}");
        snapshot.SourceActor.Should().Be("provider-importer", "the assertion preparer is separate from the source importer");
        snapshot.SourceHash.Should().Be(intake.ContentHashSha256);
        snapshot.SourceSubjectKind.Should().Be("account");
        snapshot.SourceSubjectId.Should().Be("account-one");
        snapshot.SourceProvenance.Should().Be("Unclassified", "missing source provenance never proves live data");
        Sha256Digest.IsCanonical(snapshot.Revision).Should().BeTrue();
        snapshot.SubjectSnapshotJson.Should().NotBeNullOrWhiteSpace();
        Sha256Digest.ComputeUtf8(snapshot.SubjectSnapshotJson!).Should().Be(snapshot.Hash);
        snapshot.SubjectSnapshotJson.Should().Contain("\"document\":").And.Contain("\"artifact\":");
        after.Should().BeEquivalentTo(before, "an evidence acknowledgment resolver grants no ordinary review authority");
        after!.Documents.Single().ReviewerState.Status.Should().Be(EvidenceDocumentReviewStatusDto.Unreviewed);
    }

    [Theory]
    [InlineData("foreign-tenant", Company)]
    [InlineData(Tenant, "foreign-company")]
    public async Task ForeignScopeCannotResolveRetainedDocument(string tenant, string company)
    {
        var intake = await RetainAsync();
        var snapshot = await new EvidenceAttestationSubjectResolver(Store()).ResolveAsync(
            intake.VaultIdentity.VaultId, intake.Document!.DocumentId, tenant, company);
        snapshot.Should().BeNull();
    }

    [Fact]
    public async Task TamperedSourceBytesCannotBeAcknowledgedFromValidManifestMetadata()
    {
        var intake = await RetainAsync();
        var path = Path.Combine(_root, intake.VaultIdentity.Artifacts.Single().RelativePath);
        await File.WriteAllTextAsync(path, "corrupt retained bytes");

        (await ResolveAsync(intake)).Should().BeNull();
    }

    [Theory]
    [InlineData(EvidenceStatusDto.Blocked, EvidenceExtractionStatusDto.Extracted)]
    [InlineData(EvidenceStatusDto.Stale, EvidenceExtractionStatusDto.Extracted)]
    [InlineData(EvidenceStatusDto.ReviewRequired, EvidenceExtractionStatusDto.Extracted)]
    [InlineData(EvidenceStatusDto.Ready, EvidenceExtractionStatusDto.Pending)]
    [InlineData(EvidenceStatusDto.Ready, EvidenceExtractionStatusDto.Rejected)]
    [InlineData(EvidenceStatusDto.Ready, (EvidenceExtractionStatusDto)999)]
    public async Task IncompleteOrUnsupportedChecksCannotBecomeSuccessfullyAttested(
        EvidenceStatusDto validation, EvidenceExtractionStatusDto extraction)
    {
        var intake = await RetainAsync(validation, extraction);
        var snapshot = await ResolveAsync(intake);

        snapshot.Should().NotBeNull();
        snapshot!.Eligible.Should().BeFalse();
    }

    [Theory]
    [InlineData("Conflict", "100")]
    [InlineData("Unsupported", "100")]
    [InlineData("Stale", "100")]
    [InlineData(" Pending ", "100")]
    [InlineData("Rejected", "100")]
    [InlineData("Unknown", "100")]
    [InlineData("Blocked", "100")]
    [InlineData("Missing", "100")]
    [InlineData("NeedsReview", "100")]
    [InlineData("ReviewRequired", "100")]
    [InlineData("", "100")]
    [InlineData("Manual", "101")]
    public async Task ReadyLabelCannotHideConflictOrMismatchedExpectedValue(string reviewState, string expected)
    {
        var intake = await RetainAsync(reviewState: reviewState, expected: expected);
        (await ResolveAsync(intake))!.Eligible.Should().BeFalse();
    }

    [Fact]
    public async Task FixtureEvidenceKeepsSyntheticProvenanceEvenWhenSupportIsReady()
    {
        var intake = await RetainAsync(reviewState: "Fixture");
        var snapshot = await ResolveAsync(intake);
        snapshot!.Eligible.Should().BeTrue();
        snapshot.SourceProvenance.Should().Be("Synthetic");
    }

    [Fact]
    public async Task UnknownDocumentReviewStatusCannotBePromotedByReadyExtractionFields()
    {
        var intake = await RetainAsync();
        await Store().ReviewDocumentAsync(intake.VaultIdentity.VaultId, intake.Document!.DocumentId, Tenant, Company,
            new((EvidenceDocumentReviewStatusDto)999, "ordinary-reviewer"));

        var snapshot = await ResolveAsync(intake);

        snapshot.Should().NotBeNull();
        snapshot!.Eligible.Should().BeFalse();
    }

    [Fact]
    public async Task ChangedReviewOperandsChangeRevisionEvenWhenSourceBytesAreIdentical()
    {
        var intake = await RetainAsync();
        var original = await ResolveAsync(intake);
        await Store().ReviewDocumentAsync(intake.VaultIdentity.VaultId, intake.Document!.DocumentId, Tenant, Company,
            new(EvidenceDocumentReviewStatusDto.Accepted, "ordinary-reviewer")
            {
                ConfirmedFields = [new("amount", "100", "ordinary-reviewer", DateTimeOffset.UtcNow)]
            });
        var changed = await ResolveAsync(intake);

        changed.Should().NotBeNull();
        changed!.SourceHash.Should().Be(original!.SourceHash);
        changed.Revision.Should().NotBe(original.Revision, "a prior preview cannot confirm a newer review revision");
    }

    [Fact]
    public async Task ConcurrentDocumentChangeDuringSourceReadRefusesUnstableSnapshot()
    {
        var intake = await RetainAsync();
        var identity = await Store().TryGetVaultIdentityAsync(intake.VaultIdentity.VaultId, Tenant, Company);
        var artifacts = new Mock<IEvidenceArtifactStore>();
        artifacts.SetupSequence(store => store.TryGetVaultIdentityAsync(identity!.VaultId, Tenant, Company, It.IsAny<CancellationToken>()))
            .ReturnsAsync(identity)
            .ReturnsAsync(identity! with { ContentHashSha256 = Sha256Digest.ComputeUtf8("newer document operands") });
        artifacts.Setup(store => store.TryOpenDocumentContentAsync(identity!.VaultId, intake.Document!.DocumentId,
                Tenant, Company, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EvidenceManifestFile(new MemoryStream(Encoding.UTF8.GetBytes("{\"amount\":100}")),
                "application/json", "source.json", DateTimeOffset.UtcNow));

        var snapshot = await new EvidenceAttestationSubjectResolver(artifacts.Object).ResolveAsync(
            identity!.VaultId, intake.Document!.DocumentId, Tenant, Company);
        snapshot.Should().BeNull();
    }

    private Task<EvidenceAttestationSubjectSnapshot?> ResolveAsync(EvidenceVaultIntakeResponseDto intake) =>
        new EvidenceAttestationSubjectResolver(Store()).ResolveAsync(intake.VaultIdentity.VaultId, intake.Document!.DocumentId, Tenant, Company);

    private Task<EvidenceVaultIntakeResponseDto> RetainAsync(
        EvidenceStatusDto validation = EvidenceStatusDto.Ready,
        EvidenceExtractionStatusDto extraction = EvidenceExtractionStatusDto.Extracted,
        string reviewState = "Manual", string expected = "100") =>
        Store().WriteIntakeArtifactAsync(new("account", "account-one", "ManualUpload", "source.json",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"amount\":100}")), "application/json",
            "external-provider", "external-source-one", "provider-importer",
            ExtractedFields: [new("amount", "100", expected, 1m, reviewState, validation, null, "account", "account-one")])
        {
            Actor = "provider-importer",
            TenantId = Tenant,
            Scope = Company,
            ExtractionStatus = extraction
        });

    private FileEvidenceArtifactStore Store() => new(_root, NullLogger<FileEvidenceArtifactStore>.Instance);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
