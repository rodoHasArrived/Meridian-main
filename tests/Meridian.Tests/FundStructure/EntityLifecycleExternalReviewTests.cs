using System.Text.Json;
using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.Services;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.FundStructure;
using Moq;
using Xunit;

namespace Meridian.Tests.FundStructure;

public sealed class EntityLifecycleExternalReviewTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task LifecycleExternalReview_BindsExactTimelineAndSameOperatorAttestation()
    {
        var fixture = new Fixture();
        var command = fixture.Request();
        var revision = await fixture.Service.TransitionAsync(command, "operator");
        Assert.Equal("operator", revision.RecordedBy);
        Assert.Equal("operator", revision.ExternalReview!.Operator);
        Assert.Equal("External counsel", revision.ExternalReview.ExternalReviewer);
        Assert.Equal(EntityLifecycleExternalReviewIntegrity.SubmittedHash(command), revision.ExternalReview.SubmittedHash);
        Assert.Equal(fixture.Evidence.ContentHash, revision.ExternalReview.Evidence.ContentHash);
        Assert.Equal(revision.ContentHash, EntityLifecycleRevisionIntegrity.Seal(revision).ContentHash);
        Assert.Null(EntityLifecyclePolicyStatus(revision, Now.AddDays(-1)));
        Assert.Equal(LegalEntityLifecycleStatusDto.WindingDown, EntityLifecyclePolicyStatus(revision, Now.AddDays(2)));
    }

    [Fact]
    public async Task LifecycleExternalReview_ChangedTimelineAndMissingAuthorityCannotAppend()
    {
        var fixture = new Fixture();
        var command = fixture.Request();
        await Assert.ThrowsAsync<IntercompanyAgreementConflictException>(() => fixture.Service.TransitionAsync(command with
        {
            Timeline = [command.Timeline[0] with { Status = LegalEntityLifecycleStatusDto.Dissolved }]
        }, "operator"));
        var unavailable = new EntityLifecycleService(fixture.Structure.Object, fixture.Store,
            new TenantAccessor(), timeProvider: new FixedClock());
        await Assert.ThrowsAsync<InvalidOperationException>(() => unavailable.TransitionAsync(command, "operator"));
        Assert.Empty(await fixture.Store.GetHistoryAsync("tenant-a", fixture.EntityId));
    }

    [Fact]
    public async Task LifecycleExternalReview_ExactRetryReturnsOriginalRevisionAfterLaterHistory()
    {
        var fixture = new Fixture();
        var command = fixture.Request();
        var original = await fixture.Service.TransitionAsync(command, "operator");
        var second = command with { ExpectedRevision = 1, Reason = "Later legal correction", ExternalReview = null };
        await fixture.Service.TransitionAsync(second, "another-operator");
        var replay = await fixture.Service.TransitionAsync(command, "operator");
        Assert.Equal(original.Revision, replay.Revision);
        Assert.Equal(original.ContentHash, replay.ContentHash);
        Assert.Equal(2, (await fixture.Store.GetHistoryAsync("tenant-a", fixture.EntityId)).Count);
    }

    [Fact]
    public async Task LifecycleExternalReview_StorageRejectsChangedReceiptSubjectAndLegacyHashOmitsNullMetadata()
    {
        var fixture = new Fixture();
        var revision = await fixture.Service.TransitionAsync(fixture.Request(), "operator");
        Assert.Throws<InvalidOperationException>(() => EntityLifecycleRevisionIntegrity.Seal(revision with
        {
            ExternalReview = revision.ExternalReview! with { SubmittedHash = Sha256Digest.ComputeUtf8("different") }
        }));
        var ordinary = revision with { ExternalReview = null };
        Assert.DoesNotContain("externalReview", JsonSerializer.Serialize(ordinary, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static LegalEntityLifecycleStatusDto? EntityLifecyclePolicyStatus(EntityLifecycleRevisionDto revision, DateTimeOffset asOf)
        => revision.Timeline.SingleOrDefault(state => state.EffectiveFrom <= asOf && (!state.EffectiveTo.HasValue || asOf < state.EffectiveTo))?.Status;

    private sealed class Fixture
    {
        public readonly Guid EntityId = Guid.NewGuid();
        public readonly InMemoryEntityLifecycleStore Store = new();
        public readonly Mock<IFundStructureService> Structure = new();
        public readonly IntercompanyAgreementEvidenceDto Evidence = new("vault:entity-review", "artifact-entity-v1", Sha256Digest.ComputeUtf8("legal-review-source"));
        public readonly EntityLifecycleService Service;

        public Fixture()
        {
            var entity = new LegalEntitySummaryDto(EntityId, LegalEntityTypeDto.Vehicle, "ENTITY", "Synthetic entity", "US", "USD", true,
                Now, null);
            Structure.Setup(x => x.GetOrganizationStructureAsync(It.IsAny<OrganizationStructureQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OrganizationStructureGraphDto([], [], [], [], [], [], [entity], [], [], [], [], []));
            var authority = new Mock<IEntityLifecycleExternalReviewEvidenceAuthority>();
            authority.Setup(x => x.ResolveAsync("tenant-a", "operator", EntityId, It.IsAny<IntercompanyAgreementEvidenceDto>(),
                It.IsAny<CancellationToken>())).ReturnsAsync(Evidence);
            Service = new(Structure.Object, Store, new TenantAccessor(), authority.Object, new FixedClock());
        }

        public AppendEntityLifecycleRevisionRequestDto Request()
        {
            var request = new AppendEntityLifecycleRevisionRequestDto(EntityId, 0,
                [new(Guid.NewGuid(), Now.AddDays(1), null, LegalEntityLifecycleStatusDto.WindingDown, LegalEntityLifecycleEventKindDto.Restructure)],
                "Externally reviewed wind-down", Evidence.Reference);
            return request with
            {
                ExternalReview = new("External counsel", "counsel:entity-1", new(2026, 11, 12), Evidence,
                    "I attest the retained external review covers this exact legal status timeline.", EntityLifecycleExternalReviewIntegrity.SubmittedHash(request))
            };
        }
    }
    private sealed class TenantAccessor : IFundScopeTenantAccessor { public string? ResolveCallerTenant() => "tenant-a"; }
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
}
