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

    private static ResolvedIntercompanyAccountingArrangementDto Profile() => IntercompanyArrangementTestData.Create(
        Guid.NewGuid(), Fixture.OrganizationId, IntercompanyAccountingTemplateKindDto.Loan, Fixture.FirstId, Fixture.SecondId,
        "USD", [new(Fixture.FirstId, Fixture.FirstBookId, "ar", "ap"), new(Fixture.SecondId, Fixture.SecondBookId, "ar", "ap")],
        Guid.NewGuid(), Fixture.Time.AddYears(-1));
}
