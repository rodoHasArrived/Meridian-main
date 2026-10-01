using FluentAssertions;
using Meridian.Contracts.Api;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Moq;

namespace Meridian.Tests.Storage;

[Trait("Category", "Integration")]
public sealed class RecurringJournalPeriodAuthorityPostgresTests
{
    [LedgerDatabaseFact]
    public async Task Restart_UsesPersistedLockOwnerAndGovernedReopen_NotAnEmptyLockBook()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var ct = timeout.Token;
        await using var database = await LedgerPostgresTestDatabase.CreateAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var book = new LedgerBookRecord(Guid.NewGuid(), "recurring-fund", Guid.NewGuid(),
            FundStructureNodeKindDto.Fund, "Recurring book", "USD", now, now);
        await database.JournalStore.SaveLedgerBookAsync(book, ct);
        var period = await database.JournalStore.SavePeriodAsync(new LedgerAccountingPeriod(Guid.NewGuid(),
            book.LedgerBookId, 2026, 10, "October", new(2026, 10, 1), new(2026, 10, 31),
            "Open", now, null, 0)
        { MutationActor = "period-controller" }, 0, ct: ct);
        var closed = await database.JournalStore.SavePeriodAsync(period with { Status = "SoftClosed", ClosedAt = now },
            period.Version, new(Guid.NewGuid(), period.PeriodId, "Open", "SoftClosed", "close-owner",
                "Reviewed close evidence", now), ct);
        var scope = new RecurringJournalScope(book.FundProfileId, book.LedgerBookId, "entity", "USD", "tenant", "company");
        var tenancy = new Mock<IFundProfileTenancyRegistry>(MockBehavior.Strict);
        tenancy.Setup(t => t.ResolveAsync(book.FundProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FundProfileOwnership(book.FundProfileId, scope.TenantId, scope.CompanyId));
        var restartedStore = new PostgresLedgerJournalStore(database.Options);
        var restarted = new RecurringJournalPeriodAuthority(restartedStore, tenancy.Object, new VerifiedSubject());
        var blocked = await restarted.ResolveAsync(scope, new(2026, 10, 1), ct);
        blocked.IsOpen.Should().BeFalse();
        blocked.LockOwner.Should().Be("close-owner");
        blocked.ReopenPath.Should().Be(UiApiRoutes.LedgerCloseManagementPeriodReopen);
        blocked.Version.Should().Be(closed.Version);

        await restartedStore.SavePeriodAsync(closed with { Status = "Open", ClosedAt = null }, closed.Version,
            new(Guid.NewGuid(), period.PeriodId, "SoftClosed", "Open", "reopen-controller",
                "Approved reopen evidence", now.AddMinutes(1)), ct);
        var reopened = await new RecurringJournalPeriodAuthority(new PostgresLedgerJournalStore(database.Options), tenancy.Object, new VerifiedSubject())
            .ResolveAsync(scope, new(2026, 10, 1), ct);
        reopened.IsOpen.Should().BeTrue();
        reopened.Version.Should().BeGreaterThan(blocked.Version);
        (await restartedStore.GetByPeriodAsync(period.PeriodId, ct)).Should().BeEmpty("preparation and period checks never post journals");
        var missing = () => restarted.ResolveAsync(scope, new(2026, 11, 1), ct);
        await missing.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Exactly one authoritative*");
    }

    private sealed class VerifiedSubject : IRecurringJournalSubjectAuthority
    {
        public Task VerifyAsync(RecurringJournalScope scope, LedgerBookRecord book, DateOnly date, CancellationToken ct)
            => Task.CompletedTask;
    }

    [Fact]
    public async Task MissingDurableLedgerAuthority_RefusesGeneration()
    {
        var authority = new RecurringJournalPeriodAuthority();
        await Assert.ThrowsAsync<InvalidOperationException>(() => authority.ResolveAsync(
            new("fund", Guid.NewGuid(), "entity", "USD", "tenant", "company"), new(2026, 10, 1), default));
    }
}
