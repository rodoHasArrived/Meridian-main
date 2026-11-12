using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Ledger;
using Meridian.Contracts.Services;
using Meridian.Ledger;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Moq;

namespace Meridian.Tests.Storage;

public sealed class EntityPostingAuthorityTests
{
    private static readonly Guid EntityId = Guid.Parse("c2222222-2222-2222-2222-222222222222");
    private static readonly Guid BookId = Guid.Parse("c3333333-3333-3333-3333-333333333333");
    private static readonly Guid JournalId = Guid.Parse("c4444444-4444-4444-4444-444444444444");
    private static readonly DateTimeOffset Start = DateTimeOffset.UtcNow.AddYears(-1);

    [Fact]
    public async Task BackdatedNewPosting_CannotBypassCurrentWindingDownRestriction_LeaseLivesUntilDisposed()
    {
        var revision = new EntityLifecycleRevisionDto("tenant-a", EntityId, 2, Start, "operator", "wind down", "evidence", "", "hash",
            [new(Guid.NewGuid(), Start, DateTimeOffset.UtcNow.AddDays(-1), LegalEntityLifecycleStatusDto.Active, LegalEntityLifecycleEventKindDto.Formation),
             new(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-1), null, LegalEntityLifecycleStatusDto.WindingDown, LegalEntityLifecycleEventKindDto.Amendment)]);
        var lease = new TrackingLease();
        var service = Service(revision, lease);
        var context = Context(Write());

        var retained = await service.AcquireAsync([context]);

        Assert.False(lease.Disposed);
        Assert.Contains(context.Decisions, item => item.Control == "entity-lifecycle" && item.Allowed);
        Assert.Contains(context.Decisions, item => item.Control == "entity-lifecycle-current" && !item.Allowed);
        await retained.DisposeAsync();
        Assert.True(lease.Disposed);
    }

    [Fact]
    public async Task ControlledClose_RejectsReceiptForChangedEconomics_AndReleasesAuthority()
    {
        var write = Write();
        var receipt = new ControlledCloseAuthorizationDto(Guid.NewGuid(), JournalId, BookId, "fund-a", 4, "tenant-a",
            EntityLifecycleOperationDto.Tax, ControlledCloseFingerprint.Compute(write), "approval", "obligation", "close", "reviewer", Start,
            EntityLifecycleRevisions: new Dictionary<string, string> { [EntityId.ToString("D")] = "hash" });
        var store = new Mock<IControlledCloseAuthorizationStore>();
        store.Setup(item => item.GetCloseAuthorizationAsync(receipt.AuthorizationId, It.IsAny<CancellationToken>())).ReturnsAsync(receipt);
        var lease = new TrackingLease();
        var service = Service(Revision(LegalEntityLifecycleStatusDto.WindingDown), lease, store.Object);
        var changed = Write(200m) with { PostingCommand = write.PostingCommand! with { ControlledCloseAuthorizationId = receipt.AuthorizationId } };

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcquireAsync([Context(changed)]));
        Assert.True(lease.Disposed);
    }

    [Fact]
    public async Task ControlledClose_ExactAuthorizedTaxJournalIsAllowedInWindingDown()
    {
        var write = Write();
        var receipt = new ControlledCloseAuthorizationDto(Guid.NewGuid(), JournalId, BookId, "fund-a", 4, "tenant-a",
            EntityLifecycleOperationDto.Tax, ControlledCloseFingerprint.Compute(write), "approval", "obligation", "close", "reviewer", Start,
            EntityLifecycleRevisions: new Dictionary<string, string> { [EntityId.ToString("D")] = "hash" });
        var store = new Mock<IControlledCloseAuthorizationStore>();
        store.Setup(item => item.GetCloseAuthorizationAsync(receipt.AuthorizationId, It.IsAny<CancellationToken>())).ReturnsAsync(receipt);
        var context = Context(write with { PostingCommand = write.PostingCommand! with { ControlledCloseAuthorizationId = receipt.AuthorizationId } });

        await using var lease = await Service(Revision(LegalEntityLifecycleStatusDto.WindingDown), new TrackingLease(), store.Object).AcquireAsync([context]);

        Assert.All(context.Decisions, item => Assert.True(item.Allowed, item.Message));
    }

    [Fact]
    public void Fingerprint_AllowsPostingTimestampAndPeriodVersionChange_ButBindsEconomics()
    {
        var write = Write();
        var retry = write with { PostingCommand = write.PostingCommand! with { PostingDate = Start.AddDays(1), ExpectedVersion = 5, Actor = "another-poster" } };
        Assert.Equal(ControlledCloseFingerprint.Compute(write), ControlledCloseFingerprint.Compute(retry));
        Assert.NotEqual(ControlledCloseFingerprint.Compute(write), ControlledCloseFingerprint.Compute(Write(101m)));
    }

    [Fact]
    public async Task ControlledClose_LifecycleRevisionChanged_RequiresRenewedReview()
    {
        var write = Write();
        var receipt = new ControlledCloseAuthorizationDto(Guid.NewGuid(), JournalId, BookId, "fund-a", 4, "tenant-a",
            EntityLifecycleOperationDto.Tax, ControlledCloseFingerprint.Compute(write), "approval", "obligation", "close", "reviewer", Start,
            EntityLifecycleRevisions: new Dictionary<string, string> { [EntityId.ToString("D")] = "prior-hash" });
        var store = new Mock<IControlledCloseAuthorizationStore>();
        store.Setup(item => item.GetCloseAuthorizationAsync(receipt.AuthorizationId, It.IsAny<CancellationToken>())).ReturnsAsync(receipt);
        var context = Context(write with { PostingCommand = write.PostingCommand! with { ControlledCloseAuthorizationId = receipt.AuthorizationId } });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Service(Revision(LegalEntityLifecycleStatusDto.WindingDown),
            new TrackingLease(), store.Object).AcquireAsync([context]));
    }

    private static LifecycleLedgerPostingAuthority Service(EntityLifecycleRevisionDto revision, TrackingLease lease,
        IControlledCloseAuthorizationStore? close = null)
    {
        var structure = new Mock<IFundStructureService>();
        var graph = new OrganizationStructureGraphDto([], [], [], [], [], [],
            [new(EntityId, LegalEntityTypeDto.Fund, "E", "Entity", "US", "USD", true, Start, null)], [], [],
            [new(EntityId, FundStructureNodeKindDto.Entity, "E", "Entity", null, true, Start, null)], [], []);
        structure.Setup(item => item.GetOrganizationStructureAsync(It.IsAny<OrganizationStructureQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(graph);
        var history = new Mock<IEntityLifecycleStore>();
        history.Setup(item => item.GetLatestAsync("tenant-a", EntityId, It.IsAny<CancellationToken>())).ReturnsAsync(revision);
        history.Setup(item => item.AcquireReadLeaseAsync("tenant-a", It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync(lease);
        return new(structure.Object, history.Object, () => close);
    }

    private static EntityLifecycleRevisionDto Revision(LegalEntityLifecycleStatusDto status)
        => new("tenant-a", EntityId, 1, Start, "operator", "baseline", "evidence", "", "hash",
            [new(Guid.NewGuid(), Start, null, status, LegalEntityLifecycleEventKindDto.Formation)]);

    private static LedgerPostingAuthorityContext Context(LedgerJournalEntryWrite write)
        => new(write, new(BookId, "fund-a", EntityId, FundStructureNodeKindDto.Entity, "Entity", "USD", Start, Start),
            new(BookId, LedgerBookControlModeDto.Enforced, 1, "entity-intercompany-v1", Start), "tenant-a");

    private static LedgerJournalEntryWrite Write(decimal amount = 100m)
    {
        var timestamp = Start.AddDays(2);
        var period = Guid.Parse("c5555555-5555-5555-5555-555555555555");
        var entry = new JournalEntry(JournalId, timestamp, "Tax close", [
            new(Guid.Parse("c6666666-6666-6666-6666-666666666666"), JournalId, timestamp, new("Tax", LedgerAccountType.Expense), amount, 0m, "Tax close"),
            new(Guid.Parse("c7777777-7777-7777-7777-777777777777"), JournalId, timestamp, new("Cash", LedgerAccountType.Asset), 0m, amount, "Tax close")]);
        var command = new AccountingPostingCommandDto(Guid.Parse("c8888888-8888-8888-8888-888888888888"), BookId, period,
            DateOnly.FromDateTime(timestamp.UtcDateTime), timestamp, "close-tax", ApprovalState: AccountingPostingApprovalStateDto.Approved,
            ApprovalId: "approval", OperatorRationale: "Tax close", LedgerBookId: BookId) { Actor = "poster" };
        return new(entry, BookId, period, command.CommandId, null, PostingCommand: command, LedgerBookId: BookId);
    }

    private sealed class TrackingLease : IAsyncDisposable
    {
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
