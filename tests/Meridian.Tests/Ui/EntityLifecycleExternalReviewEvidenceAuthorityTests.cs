using System.Text;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class EntityLifecycleExternalReviewEvidenceAuthorityTests : IDisposable
{
    private const string Tenant = "entity-review-tenant";
    private const string Company = "entity-review-company";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "meridian-tests", "entity-legal-review", Guid.NewGuid().ToString("N"));
    private FileEvidenceArtifactStore Store() => new(_root, NullLogger<FileEvidenceArtifactStore>.Instance);

    [Fact]
    public async Task ExactAcceptedEntityDocumentRemainsResolvableAfterOrdinaryReviewMetadataChanges()
    {
        var entityId = Guid.NewGuid();
        var retained = await RetainAsync(entityId);
        var authority = Authority();
        var first = await authority.ResolveAsync(Tenant, "operator", entityId, retained.Reference);
        await Store().ReviewDocumentAsync(retained.VaultId, retained.DocumentId, Tenant, Company,
            new(EvidenceDocumentReviewStatusDto.Accepted, "operator", "Added a review note without changing legal source bytes")
            {
                ConfirmedFields = [new("sourceHashSha256", retained.Reference.ContentHash!, "operator", DateTimeOffset.UtcNow)]
            });
        var again = await authority.ResolveAsync(Tenant, "operator", entityId, retained.Reference);
        Assert.Equal(first, again);
        Assert.Equal(retained.Reference.ContentHash, again.ContentHash);
    }

    [Theory]
    [InlineData("unreviewed")]
    [InlineData("entity")]
    [InlineData("company")]
    [InlineData("hash")]
    [InlineData("bytes")]
    public async Task ExternalLifecycleEvidenceRefusesUnreviewedUnrelatedForeignOrChangedDocument(string defect)
    {
        var entityId = Guid.NewGuid();
        var retained = await RetainAsync(defect == "entity" ? Guid.NewGuid() : entityId, accepted: defect != "unreviewed");
        var reference = defect == "hash" ? retained.Reference with { ContentHash = new string('b', 64) } : retained.Reference;
        if (defect == "bytes")
            await File.WriteAllTextAsync(Assert.Single(Directory.GetFiles(_root, "*.pdf", SearchOption.AllDirectories)), "Changed legal source bytes");
        await Assert.ThrowsAsync<IntercompanyAgreementValidationException>(() => Authority(defect == "company" ? "another-company" : Company)
            .ResolveAsync(Tenant, "operator", entityId, reference));
    }

    private async Task<(IntercompanyAgreementEvidenceDto Reference, string VaultId, string DocumentId)> RetainAsync(Guid entityId, bool accepted = true)
    {
        var source = Encoding.UTF8.GetBytes("%PDF-1.7\nSynthetic external legal review of entity wind-down.\n");
        var store = Store();
        var intake = await store.WriteIntakeArtifactAsync(new("accounting-record", "current", "upload", "entity-review.pdf",
            Convert.ToBase64String(source), "application/pdf", ReceivedBy: "operator")
        {
            Actor = "operator",
            TenantId = Tenant,
            Scope = Company,
            Classification = EvidenceDocumentClassificationDto.Agreement,
            ObjectLinks = [new(EvidenceDocumentLinkKindDto.LegalEntity, entityId.ToString("D"), Relationship: "supports")]
        });
        var document = intake.Document!;
        if (accepted)
        {
            var response = await store.ReviewDocumentAsync(intake.VaultIdentity.VaultId, document.DocumentId, Tenant, Company,
                new(EvidenceDocumentReviewStatusDto.Accepted, "operator", "Reviewed legal source")
                {
                    ConfirmedFields = [new("sourceHashSha256", document.SourceHashSha256, "operator", DateTimeOffset.UtcNow)]
                });
            document = response!.Entry.Document;
        }
        return (new(EvidenceVaultReference.CanonicalPrefix + intake.VaultIdentity.VaultId, document.ArtifactId!, document.SourceHashSha256),
            intake.VaultIdentity.VaultId, document.DocumentId);
    }

    private WorkstationEntityLifecycleExternalReviewEvidenceAuthority Authority(string company = Company)
    {
        var context = new DefaultHttpContext();
        context.Items[LoginSessionMiddleware.CurrentUserKey] = "operator";
        context.Items[LoginSessionMiddleware.CurrentTenantIdKey] = Tenant;
        context.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = company;
        return new(new HttpContextAccessor { HttpContext = context }, Store());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
