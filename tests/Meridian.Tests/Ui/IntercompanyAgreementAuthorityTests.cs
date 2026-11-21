using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Workstation;
using Meridian.Identity;
using Meridian.Identity.Auth;
using Meridian.Storage.FundStructure;
using Meridian.Ui.Shared.Endpoints;
using Meridian.Ui.Shared.Evidence;
using Meridian.Ui.Shared.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Meridian.Tests.Ui;

public sealed class IntercompanyAgreementAuthorityTests
{
    private const string VaultId = "ev-aaaaaaaaaaaaaaaaaaaaaaaa";
    private static readonly string ContentHash = new('a', 64);
    private static readonly IntercompanyAgreementCallContext Call = new("tenant-a", "operator-a");

    [Fact]
    public async Task Access_OneEntityGrantCannotDiscloseTheOtherParty()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var authorization = new Mock<IScopedAuthorizationService>();
        IReadOnlyDictionary<Guid, ScopedAuthorizationDecisionDto> decisions = new Dictionary<Guid, ScopedAuthorizationDecisionDto>
        {
            [first] = new(true, Call.Actor, UserPermission.ManageFundStructure, AccessScopeKindDto.LegalEntity, first, "Granted"),
            [second] = new(false, Call.Actor, UserPermission.ManageFundStructure, AccessScopeKindDto.LegalEntity, second, "Denied")
        };
        authorization.Setup(service => service.AuthorizeManyAsync(Call.Actor, UserPermission.ManageFundStructure,
            AccessScopeKindDto.LegalEntity, It.IsAny<IReadOnlyCollection<Guid>>(), UserPermission.ManageFundStructure,
            It.IsAny<CancellationToken>())).ReturnsAsync(decisions);
        var services = new ServiceCollection().AddSingleton(authorization.Object).BuildServiceProvider();
        var http = Http(services);
        var structure = new Mock<IFundStructureStore>();
        structure.Setup(store => store.GetNodeTenantsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new FundStructureTenantMap(
            true, new Dictionary<Guid, string> { [first] = Call.TenantId, [second] = Call.TenantId }));
        var authority = new HttpContextIntercompanyAgreementAccessAuthority(new HttpContextAccessor { HttpContext = http }, structure.Object);

        (await authority.CanAccessEntitiesAsync(Call, [first, second])).Should().BeFalse();
    }

    [Fact]
    public async Task Access_AdminOverrideDoesNotCrossTenantOwnership()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var structure = new Mock<IFundStructureStore>();
        structure.Setup(store => store.GetNodeTenantsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new FundStructureTenantMap(
            true, new Dictionary<Guid, string> { [first] = Call.TenantId, [second] = "foreign-tenant" }));
        var http = Http(new ServiceCollection().BuildServiceProvider(), UserPermission.AdminMaintenance);
        var authority = new HttpContextIntercompanyAgreementAccessAuthority(new HttpContextAccessor { HttpContext = http }, structure.Object);

        (await authority.CanAccessEntitiesAsync(Call, [first, second])).Should().BeFalse();
    }

    [Fact]
    public async Task Access_UnpartitionedGraphAndForgedCallContextFailClosed()
    {
        var entity = Guid.NewGuid();
        var structure = new Mock<IFundStructureStore>();
        structure.Setup(store => store.GetNodeTenantsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(FundStructureTenantMap.Unpartitioned);
        var http = Http(new ServiceCollection().BuildServiceProvider(), UserPermission.AdminMaintenance);
        var authority = new HttpContextIntercompanyAgreementAccessAuthority(new HttpContextAccessor { HttpContext = http }, structure.Object);

        (await authority.CanAccessEntitiesAsync(Call, [entity])).Should().BeFalse();
        (await authority.CanAccessEntitiesAsync(Call with { Actor = "another-user" }, [entity])).Should().BeFalse();
        (await authority.CanAccessEntitiesAsync(Call with { TenantId = "foreign-tenant" }, [entity])).Should().BeFalse();
    }

    [Fact]
    public async Task Evidence_ResolvesExactScopedAcceptedArtifact_AndVerifiesBytes()
    {
        var store = EvidenceStore(Identity());
        var authority = EvidenceAuthority(store.Object);

        var resolved = await authority.ResolveAsync(Call, LegalAgreementId, Guid.NewGuid(),
            [new(EvidenceVaultReference.CanonicalPrefix + VaultId, "artifact-a")]);

        resolved.Should().ContainSingle().Which.Should().Be(new IntercompanyAgreementEvidenceDto(
            EvidenceVaultReference.CanonicalPrefix + VaultId, "artifact-a", ContentHash));
        store.Verify(s => s.VerifyDocumentContentAsync(VaultId, "document-a", "artifact-a", ContentHash,
            Call.TenantId, "company-a", It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.VerifyRetainedContentAsync(VaultId, Call.TenantId, "company-a", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Evidence_ChangedHashAndUnacceptedDocumentAreRefused()
    {
        var accepted = EvidenceAuthority(EvidenceStore(Identity()).Object);
        var changedHash = () => accepted.ResolveAsync(Call, LegalAgreementId, Guid.NewGuid(),
            [new(EvidenceVaultReference.CanonicalPrefix + VaultId, "artifact-a", new string('b', 64))]);
        await changedHash.Should().ThrowAsync<IntercompanyAgreementValidationException>();

        var unreviewed = EvidenceAuthority(EvidenceStore(Identity(EvidenceDocumentReviewStatusDto.NeedsReview)).Object);
        var changedReview = () => unreviewed.ResolveAsync(Call, LegalAgreementId, Guid.NewGuid(),
            [new(EvidenceVaultReference.CanonicalPrefix + VaultId, "artifact-a")]);
        await changedReview.Should().ThrowAsync<IntercompanyAgreementValidationException>();
    }

    [Fact]
    public async Task Evidence_ForeignOrArbitraryLinkCannotBecomeAuthority()
    {
        var store = EvidenceStore(null);
        var authority = EvidenceAuthority(store.Object);
        var foreign = () => authority.ResolveAsync(Call, LegalAgreementId, Guid.NewGuid(),
            [new(EvidenceVaultReference.CanonicalPrefix + VaultId, "artifact-a")]);
        await foreign.Should().ThrowAsync<IntercompanyAgreementValidationException>();
        var arbitrary = () => authority.ResolveAsync(Call, LegalAgreementId, Guid.NewGuid(), [new("https://example.test/agreement.pdf")]);
        await arbitrary.Should().ThrowAsync<IntercompanyAgreementValidationException>();
        store.Verify(s => s.VerifyRetainedContentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static readonly Guid LegalAgreementId = Guid.Parse("c7111111-1111-1111-1111-111111111111");

    private static Mock<IEvidenceArtifactStore> EvidenceStore(EvidenceVaultIdentityDto? identity)
    {
        var store = new Mock<IEvidenceArtifactStore>(MockBehavior.Strict);
        store.Setup(s => s.TryGetVaultIdentityAsync(VaultId, Call.TenantId, "company-a", It.IsAny<CancellationToken>())).ReturnsAsync(identity);
        store.Setup(s => s.VerifyDocumentContentAsync(VaultId, "document-a", "artifact-a", ContentHash,
            Call.TenantId, "company-a", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        store.Setup(s => s.VerifyRetainedContentAsync(VaultId, Call.TenantId, "company-a", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return store;
    }

    private static EvidenceVaultIdentityDto Identity(EvidenceDocumentReviewStatusDto review = EvidenceDocumentReviewStatusDto.Accepted)
    {
        var date = DateTimeOffset.UtcNow;
        var document = new EvidenceDocumentDto("document-a", "synthetic-agreement.pdf", EvidenceDocumentClassificationDto.Agreement,
            ContentHash, date, "Upload", "operator-a", Call.TenantId, "company-a", EvidenceExtractionStatusDto.Accepted,
            [], new(review, "reviewer-a", date), [])
        { ArtifactId = "artifact-a", ObjectLinks = [new(EvidenceDocumentLinkKindDto.IntercompanyAgreement, LegalAgreementId.ToString("D"))] };
        return new EvidenceVaultIdentityDto(VaultId, "agreement", "source-a", "manifest.json", "/manifest", date,
            ContentHash, 1, "file-manifest")
        {
            TenantId = Call.TenantId,
            Scope = "company-a",
            Artifacts = [new("artifact-a", "document", "source.pdf", ContentHash, 10, date, null, null, "agreement", "source-a") { Document = document }]
        };
    }

    private static WorkstationIntercompanyAgreementEvidenceAuthority EvidenceAuthority(IEvidenceArtifactStore store)
        => new(new HttpContextAccessor { HttpContext = Http(new ServiceCollection().BuildServiceProvider()) }, store);

    private static DefaultHttpContext Http(IServiceProvider services, UserPermission permissions = UserPermission.ManageFundStructure)
    {
        var http = new DefaultHttpContext { RequestServices = services };
        http.Items[LoginSessionMiddleware.CurrentUserKey] = Call.Actor;
        http.Items[LoginSessionMiddleware.CurrentUserPermissionsKey] = permissions;
        http.Items[LoginSessionMiddleware.CurrentTenantIdKey] = Call.TenantId;
        http.Items[LoginSessionMiddleware.CurrentUserCompanyIdKey] = "company-a";
        return http;
    }
}
