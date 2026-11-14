using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Services;
using Meridian.FinancialOperations.Intercompany;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Tests.TestSupport;
using Meridian.Ui.Shared.Services;
using Moq;
using Fixture = Meridian.Tests.FinancialOperations.Consolidation.ConsolidationServiceTests.Fixture;

namespace Meridian.Tests.Ui;

public sealed class EntityTimelineServiceTests
{
    [Fact]
    public async Task MissingSelectedEntityGrant_RejectsBeforeReadingAnyBusinessState()
    {
        var structures = new Mock<IFundStructureService>(MockBehavior.Strict);
        var service = new EntityTimelineService(structures.Object, Mock.Of<IEntityLifecycleStore>(),
            IntercompanyArrangementTestData.Service(Mock.Of<IIntercompanyAccountingArrangementStore>(), Profile()),
            Mock.Of<ILedgerJournalStore>(), Mock.Of<IIntercompanyAgreementService>(), Mock.Of<IEntityTimelineAccessAuthority>());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync("tenant", Fixture.OrganizationId,
            Fixture.FirstId, Fixture.Time, actor: "reader"));
        structures.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AccountingReader_ProfileRequiresBothPartyGrants_AndDoesNotRevealRestrictedLegalHistory(bool bothParties)
    {
        var fixture = new Fixture();
        var profile = Profile();
        var store = new Mock<IIntercompanyAccountingArrangementStore>();
        store.Setup(x => x.ListAsync("tenant", Fixture.OrganizationId, It.IsAny<CancellationToken>())).ReturnsAsync([profile.Arrangement]);
        var lifecycle = new Mock<IEntityLifecycleStore>();
        lifecycle.Setup(x => x.GetHistoryAsync("tenant", Fixture.FirstId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        fixture.Store.Setup(x => x.ListLedgerBooksAsync(null, Fixture.FirstId, FundStructureNodeKindDto.Entity,
            It.IsAny<CancellationToken>())).ReturnsAsync([]);
        var legal = new Mock<IIntercompanyAgreementService>();
        legal.Setup(x => x.ListAsync(new("tenant", "reader"), It.IsAny<IntercompanyAgreementQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntercompanyAgreementListDto([], null));
        var access = new Mock<IEntityTimelineAccessAuthority>();
        access.Setup(x => x.CanAccessEntitiesAsync(new("tenant", "reader"), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .Returns((IntercompanyAgreementCallContext _, IReadOnlyList<Guid> ids, CancellationToken _) => Task.FromResult(ids.Count == 1 || bothParties));
        var service = new EntityTimelineService(fixture.Structure.Object, lifecycle.Object,
            IntercompanyArrangementTestData.Service(store.Object, profile), fixture.Store.Object, legal.Object, access.Object);
        var result = await service.GetAsync("tenant", Fixture.OrganizationId, Fixture.FirstId, Fixture.Time, actor: "reader");
        Assert.Empty(result.Agreements);
        Assert.Equal(bothParties ? 1 : 0, result.AccountingArrangements.Count);
        access.Verify(x => x.CanAccessEntitiesAsync(new("tenant", "reader"),
            It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 2 && ids.Contains(Fixture.FirstId) && ids.Contains(Fixture.SecondId)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// An entity merged into its sibling still has books, ownership and mappings to review. Guards
    /// against the timeline dropping revisions, the successor link or the residual book because of
    /// the entity's status, and against missing or legacy-only history looking established.
    /// </summary>
    [Theory]
    [InlineData("none", "has not been established")]
    [InlineData("legacy", "legacy snapshot")]
    [InlineData("governed", null)]
    public async Task Timeline_RetainsRevisionsSuccessorOwnershipMappingsAndBooks_AndFlagsUnestablishedHistory(
        string history, string? expectedWarning)
    {
        var fixture = new Fixture();
        var profile = Profile();
        var formed = Fixture.Time.AddYears(-1);
        var mergedAt = Fixture.Time.AddDays(-10);
        var active = new EntityLifecycleStateDto(Guid.NewGuid(), formed, null, LegalEntityLifecycleStatusDto.Active,
            LegalEntityLifecycleEventKindDto.Formation);
        var first = new EntityLifecycleRevisionDto("tenant", Fixture.FirstId, 1, formed, "preparer", "Formation",
            "retained://formation/approved", "", "hash-1", [active], LegacySnapshot: history == "legacy");
        var merged = first with
        {
            Revision = 2,
            PreviousHash = "hash-1",
            ContentHash = "hash-2",
            Reason = "Merged into sibling",
            LegacySnapshot = false,
            Timeline = [active with { EffectiveTo = mergedAt },
                active with { TransitionId = Guid.NewGuid(), EffectiveFrom = mergedAt, Status = LegalEntityLifecycleStatusDto.Merged, SuccessorEntityId = Fixture.SecondId }]
        };
        IReadOnlyList<EntityLifecycleRevisionDto> retained = history switch
        {
            "none" => [],
            "legacy" => [first],
            _ => [first, merged]
        };
        var store = new Mock<IIntercompanyAccountingArrangementStore>();
        store.Setup(x => x.ListAsync("tenant", Fixture.OrganizationId, It.IsAny<CancellationToken>())).ReturnsAsync([profile.Arrangement]);
        var lifecycle = new Mock<IEntityLifecycleStore>();
        lifecycle.Setup(x => x.GetHistoryAsync("tenant", Fixture.FirstId, It.IsAny<CancellationToken>())).ReturnsAsync(retained);
        fixture.Store.Setup(x => x.ListLedgerBooksAsync(null, Fixture.FirstId, FundStructureNodeKindDto.Entity, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LedgerBookRecord(Fixture.FirstBookId, Fixture.Profile, Fixture.FirstId, FundStructureNodeKindDto.Entity,
                "Residual entity book", "USD", Fixture.Time, Fixture.Time)]);
        var legal = new Mock<IIntercompanyAgreementService>();
        legal.Setup(x => x.ListAsync(new("tenant", "reader"), It.IsAny<IntercompanyAgreementQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IntercompanyAgreementListDto([], null));
        var access = new Mock<IEntityTimelineAccessAuthority>();
        access.Setup(x => x.CanAccessEntitiesAsync(new("tenant", "reader"), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var service = new EntityTimelineService(fixture.Structure.Object, lifecycle.Object,
            IntercompanyArrangementTestData.Service(store.Object, profile), fixture.Store.Object, legal.Object, access.Object);

        var result = await service.GetAsync("tenant", Fixture.OrganizationId, Fixture.FirstId, Fixture.Time, actor: "reader");

        Assert.Equal(retained.Select(x => (x.Revision, x.ContentHash)), result.LifecycleRevisions.Select(x => (x.Revision, x.ContentHash)));
        if (history == "governed")
        {
            var latest = result.LifecycleRevisions[^1];
            Assert.Equal("hash-1", latest.PreviousHash);
            Assert.Equal(2, latest.Timeline.Count);
            Assert.Equal(Fixture.SecondId, Assert.Single(latest.Timeline, x => x.Status == LegalEntityLifecycleStatusDto.Merged).SuccessorEntityId);
            Assert.Empty(result.Warnings);
        }
        else
        {
            Assert.Contains(expectedWarning!, Assert.Single(result.Warnings));
        }
        Assert.Equal(Fixture.FirstId, result.Entity.EntityId);
        var ownership = Assert.Single(result.OwnershipLinks);
        Assert.Equal(Fixture.RootId, ownership.ParentNodeId);
        Assert.Equal(Fixture.FirstId, ownership.ChildNodeId);
        Assert.Equal(100m, ownership.OwnershipPercent);
        Assert.Equal(profile.ArrangementId, Assert.Single(result.AccountingArrangements).ArrangementId);
        var book = Assert.Single(result.Books);
        Assert.Equal(Fixture.FirstBookId, book.LedgerBookId);
        Assert.Equal(Fixture.FirstId, book.FundStructureNodeId);
    }

    private static ResolvedIntercompanyAccountingArrangementDto Profile() => IntercompanyArrangementTestData.Create(
        Guid.NewGuid(), Fixture.OrganizationId, IntercompanyAccountingTemplateKindDto.Loan, Fixture.FirstId, Fixture.SecondId,
        "USD", [new(Fixture.FirstId, Fixture.FirstBookId, "ar", "ap"), new(Fixture.SecondId, Fixture.SecondBookId, "ar", "ap")],
        Guid.NewGuid(), Fixture.Time.AddYears(-1));
}
