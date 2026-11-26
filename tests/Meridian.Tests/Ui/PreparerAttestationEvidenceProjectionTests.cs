using System.Text;
using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Evidence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Meridian.Tests.Ui;

public sealed class PreparerAttestationEvidenceProjectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-attestation-projection-" + Guid.NewGuid().ToString("N"));
    private static readonly SinglePrincipalAttestationScopeDto Scope = new("tenant-a", "company-a");
    private static readonly SinglePrincipalAttestationAuthority Principal = new("preparer-a", OperationsActionOriginDto.HumanOperator,
        CanRead: true, CanAdministerDeclaration: true, CanAttest: true, CanModifyTarget: true);
    private readonly FileEvidenceArtifactStore _artifacts;
    private readonly EvidenceAttestationSubjectResolver _resolver;
    private readonly SinglePrincipalAttestationStore _attestations;
    private readonly PreparerAttestationEvidenceProjectionService _projection;

    public PreparerAttestationEvidenceProjectionTests()
    {
        _artifacts = new(_root, NullLogger<FileEvidenceArtifactStore>.Instance);
        _resolver = new(_artifacts);
        _attestations = new(_root, async (scope, _, request, ct) =>
        {
            var parts = request.SubjectId.Split('/');
            var source = await _resolver.ResolveAsync(parts[0], parts[1], scope.TenantId, scope.CompanyId, ct);
            return source is null ? null : new(source.SubjectKind, source.SubjectId, source.Revision, source.Hash,
                source.SourceArtifactId, source.SourceHash, source.SourceActor, source.SourceProvenance,
                source.Eligible, source.Checks, source.SourceSubjectKind, source.SourceSubjectId, source.SubjectSnapshotJson);
        }, policyEnabled: true);
        _projection = new(_attestations, _artifacts, _resolver);
    }

    private static PreparerAttestationEvidenceReadContext Context(bool canRead = true,
        Func<string, string, bool>? sourceReadable = null) =>
        new(Scope, Principal with { CanRead = canRead }, sourceReadable ?? ((_, _) => true));

    [Fact]
    public async Task ExactVisibleDocumentGetsSeparateTaskCountsAndOriginalPolicyWithoutChangingReadiness()
    {
        var intake = await RetainAsync();
        var receipt = await AttestAsync(intake);
        var packet = Packet(intake);
        var before = JsonSerializer.Serialize(packet);

        var result = (await _projection.ProjectAsync(packet, Context()))!;

        result.Availability.Should().Be("Available");
        result.RecordedReceiptCount.Should().Be(1);
        result.UnrevokedReceiptCount.Should().Be(1);
        result.RevokedReceiptCount.Should().Be(0);
        result.SyntheticReceiptCount.Should().Be(1);
        result.UnclassifiedReceiptCount.Should().Be(0);
        result.Acknowledgments.Should().ContainSingle().Which.Applicability.Should().Be("ExactSource");
        result.Acknowledgments[0].Receipt.Should().BeEquivalentTo(receipt);
        result.Acknowledgments[0].OriginalDeclaration.Revision.Should().Be(receipt.DeclarationRevision);
        result.Disclosure.Should().Be(SinglePrincipalAttestationPolicy.Disclosure);
        result.Boundary.Should().Contain("no financial approval").And.Contain("Verified Coverage");
        JsonSerializer.Serialize(packet).Should().Be(before, "task disclosure cannot change evidence or amount readiness");
        packet.Completeness.Status.Should().Be(EvidenceStatusDto.ReviewRequired);
    }

    [Fact]
    public async Task IdenticalBytesInAnotherDocumentDoNotPropagateAcknowledgment()
    {
        var first = await RetainAsync("account-a");
        var other = await RetainAsync("account-b");
        first.ContentHashSha256.Should().Be(other.ContentHashSha256);
        await AttestAsync(first);

        var result = (await _projection.ProjectAsync(Packet(other), Context()))!;

        result.Availability.Should().Be("Available");
        result.RecordedReceiptCount.Should().Be(0);
        result.Acknowledgments.Should().BeEmpty();
    }

    [Fact]
    public async Task SourcePathAndHashMatchStillRequiresExactCanonicalSubject()
    {
        var intake = await RetainAsync();
        await AttestAsync(intake);
        var packet = Packet(intake);
        var actual = intake.VaultIdentity.Artifacts.Single();
        var falseLink = new EvidenceArtifactRefDto("same-bytes", "source", actual.RelativePath, null,
            actual.RetainedAt, actual.ContentHashSha256, false, "account", "another-account");
        packet = packet with { Nodes = [packet.Nodes[0] with { ArtifactRefs = [falseLink] }] };

        var result = (await _projection.ProjectAsync(packet, Context()))!;

        result.RecordedReceiptCount.Should().Be(0);
        result.Acknowledgments.Should().BeEmpty();
    }

    [Fact]
    public async Task DeniedOrUnavailableReadDoesNotClaimAnEmptyReceiptInventory()
    {
        var intake = await RetainAsync();
        await AttestAsync(intake);
        var denied = (await _projection.ProjectAsync(Packet(intake), Context(canRead: false)))!;
        denied.Availability.Should().Be("Unavailable");
        denied.RecordedReceiptCount.Should().BeNull();
        denied.Acknowledgments.Should().BeEmpty();

        var sourceDenied = (await _projection.ProjectAsync(Packet(intake), Context(sourceReadable: (_, _) => false)))!;
        sourceDenied.Availability.Should().Be("Unavailable");
        sourceDenied.RecordedReceiptCount.Should().BeNull();
        sourceDenied.Acknowledgments.Should().BeEmpty();

        var statePath = Directory.GetFiles(Path.Combine(_root, "single-principal-attestations"), "*.json").Single();
        await File.WriteAllTextAsync(statePath, "corrupt history");
        var corrupt = (await _projection.ProjectAsync(Packet(intake), Context()))!;
        corrupt.Availability.Should().Be("Unavailable");
        corrupt.RecordedReceiptCount.Should().BeNull();
    }

    [Fact]
    public async Task LaterDocumentReviewMakesApplicabilityStaleWhileOriginalReceiptSurvives()
    {
        var intake = await RetainAsync();
        var receipt = await AttestAsync(intake);
        await _artifacts.ReviewDocumentAsync(intake.VaultIdentity.VaultId, intake.Document!.DocumentId,
            Scope.TenantId, Scope.CompanyId, new(EvidenceDocumentReviewStatusDto.Accepted, "ordinary-reviewer")
            {
                ConfirmedFields = [new("amount", "100", "ordinary-reviewer", DateTimeOffset.UtcNow)]
            });

        var result = (await _projection.ProjectAsync(Packet(intake), Context()))!;

        result.Acknowledgments.Should().ContainSingle().Which.Applicability.Should().Be("StaleSource");
        result.Acknowledgments[0].Receipt.Should().BeEquivalentTo(receipt);
        result.RecordedReceiptCount.Should().Be(1);
    }

    [Fact]
    public async Task CorruptSourceCannotBecomeExactSupportButDoesNotEraseHistoricalTask()
    {
        var intake = await RetainAsync();
        await AttestAsync(intake);
        await File.WriteAllTextAsync(Path.Combine(_root, intake.VaultIdentity.Artifacts.Single().RelativePath), "corrupt source");

        var result = (await _projection.ProjectAsync(Packet(intake), Context()))!;

        result.RecordedReceiptCount.Should().Be(1);
        result.Acknowledgments.Should().ContainSingle().Which.Applicability.Should().Be("Unavailable");
    }

    [Fact]
    public async Task RetainedExportReproducesOriginalDisclosureAndBytesAfterWithdrawalAndRevocation()
    {
        var intake = await RetainAsync();
        var receipt = await AttestAsync(intake);
        var packet = Packet(intake);
        var original = (await _projection.ProjectAsync(packet, Context()))!;
        var exported = await _artifacts.WriteManifestAsync(packet with { PreparerAttestations = original },
            new(Principal.Actor, "Freeze separate evidence-task disclosure") { TenantId = Scope.TenantId, Scope = Scope.CompanyId });
        var before = await OpenExportAsync(exported.VaultIdentity!.VaultId);
        await _attestations.ChangeDeclarationAsync(Scope, Principal,
            new(Guid.NewGuid(), 1, false, Principal.Actor, SinglePrincipalAttestationPolicy.Version, "Withdraw new assertions"));
        await _attestations.RevokeAsync(Scope, Principal, new(Guid.NewGuid(), receipt.ReceiptId, 1, "Correct the old assertion"));
        var after = await OpenExportAsync(exported.VaultIdentity.VaultId);

        after.Should().Be(before);
        Sha256Digest.ComputeUtf8(after).Should().Be(Sha256Digest.ComputeUtf8(before));
        using var frozen = JsonDocument.Parse(after);
        var disclosure = frozen.RootElement.GetProperty("preparerAttestations");
        disclosure.GetProperty("disclosure").GetString().Should().Be(SinglePrincipalAttestationPolicy.Disclosure);
        disclosure.GetProperty("recordedReceiptCount").GetInt32().Should().Be(1);
        disclosure.GetProperty("revokedReceiptCount").GetInt32().Should().Be(0);

        var current = (await _projection.ProjectAsync(packet, Context()))!;
        current.RevokedReceiptCount.Should().Be(1);
        current.UnrevokedReceiptCount.Should().Be(0);
        current.Acknowledgments[0].Applicability.Should().Be("Revoked");
        current.Acknowledgments[0].OriginalDeclaration.Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task ExportRejectsForeignScopeAndFalseReceiptCountsBeforePublication()
    {
        var intake = await RetainAsync();
        await AttestAsync(intake);
        var packet = Packet(intake);
        var projection = (await _projection.ProjectAsync(packet, Context()))!;
        var request = new EvidencePacketExportRequest(Principal.Actor, "Export") { TenantId = Scope.TenantId, Scope = Scope.CompanyId };
        await Assert.ThrowsAsync<InvalidOperationException>(() => _artifacts.WriteManifestAsync(
            packet with { PreparerAttestations = projection with { Scope = Scope with { TenantId = "foreign" } } }, request));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _artifacts.WriteManifestAsync(
            packet with { PreparerAttestations = projection with { RecordedReceiptCount = 100 } }, request));
    }

    [Fact]
    public async Task MissingTrustedContextPreservesLegacyOptionalContract()
    {
        var intake = await RetainAsync();
        (await _projection.ProjectAsync(Packet(intake), null)).Should().BeNull();
    }

    private Task<EvidenceVaultIntakeResponseDto> RetainAsync(string accountId = "account-a") =>
        _artifacts.WriteIntakeArtifactAsync(new("account", accountId, "ManualUpload", "source.json",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"amount\":100}")), "application/json", "external-provider",
            "source-a", "external-importer", ExtractedFields:
            [new("amount", "100", "100", 1m, "Fixture", EvidenceStatusDto.Ready, null, "account", accountId)])
        {
            Actor = "external-importer",
            TenantId = Scope.TenantId,
            Scope = Scope.CompanyId,
            ExtractionStatus = EvidenceExtractionStatusDto.Extracted
        });

    private async Task<PreparerAttestationReceiptDto> AttestAsync(EvidenceVaultIntakeResponseDto intake)
    {
        await _attestations.ChangeDeclarationAsync(Scope, Principal,
            new(Guid.NewGuid(), 0, true, Principal.Actor, SinglePrincipalAttestationPolicy.Version, "One principal controls this scope"));
        var subject = (await _resolver.ResolveAsync(intake.VaultIdentity.VaultId, intake.Document!.DocumentId,
            Scope.TenantId, Scope.CompanyId))!;
        return await _attestations.AttestAsync(Scope, Principal, new(Guid.NewGuid(), 1,
            SinglePrincipalAttestationPolicy.EvidenceAcknowledgment, subject.SubjectKind, subject.SubjectId,
            subject.Revision, subject.Hash, "Inspected exact source and checks", SinglePrincipalAttestationPolicy.Confirmation));
    }

    private static EvidencePacketDto Packet(EvidenceVaultIntakeResponseDto intake)
    {
        var identity = intake.VaultIdentity;
        var subject = new EvidenceSubjectDto(identity.SubjectId, identity.SubjectKind, "Source review", "Reporting", null, "EvidenceWorkbench");
        var node = new EvidenceNodeDto("source", subject, "retained-source", EvidenceStatusDto.ReviewRequired,
            new(identity.RetainedAt, false, null), "external-provider", "Retained external document",
            [new("manifest", "source-manifest", null, identity.ManifestRoute, identity.RetainedAt,
                identity.ContentHashSha256, false, identity.SubjectKind, identity.SubjectId)], []);
        return new(subject, DateTimeOffset.UtcNow, [node], [],
            new(0, EvidenceStatusDto.ReviewRequired, ["ordinary-review"], [], ["ordinary-review"], [], []), [], []);
    }

    private async Task<string> OpenExportAsync(string vaultId)
    {
        var retained = (await _artifacts.TryOpenManifestByVaultIdAsync(vaultId, Scope.TenantId, Scope.CompanyId))!;
        await using var stream = retained.Content;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
