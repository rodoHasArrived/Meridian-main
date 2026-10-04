using FluentAssertions;
using Meridian.Contracts.FundStructure;
using Meridian.FinancialOperations.FundAdministration;
using Meridian.Storage.FundStructure;
using Meridian.Storage.Ledger;
using Meridian.Ui.Shared.Services;
using Xunit;

namespace Meridian.Tests.Ui;

public sealed class RecurringJournalSubjectAuthorityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid EntityId = Guid.Parse("7b1b4b07-6098-432a-90ae-bda3d1921c88");
    private static readonly Guid FundId = Guid.Parse("735f81a5-ecb6-4e30-8698-096e9ab9f82c");
    private static readonly Guid BookId = Guid.Parse("8f0f1c61-39b1-41c6-bebc-76e3b8a9291f");
    private static readonly Guid AccountId = Guid.Parse("a7357f99-d955-4621-a7c2-8c9ee31f5dcb");
    private static RecurringJournalScope Scope => new("fund-a", BookId, EntityId.ToString("D"), "USD", "tenant-a", "company-a");
    private static LedgerBookRecord Book => new(BookId, "fund-a", FundId, FundStructureNodeKindDto.Fund, "Fund", "USD", Now, Now);
    private static LegalEntitySummaryDto Entity => new(EntityId, LegalEntityTypeDto.Fund, "FUND-A", "Fund entity", "US", "USD", true, Now.AddYears(-1), null);
    private static FundSummaryDto Fund => new(FundId, null, "FUND-A", "Fund", "USD", true, Now.AddYears(-1), null, [], [], [EntityId], [], [AccountId]);
    private static AccountSummaryDto Account => new(AccountId, AccountTypeDto.Bank, EntityId, FundId, null, null, "BANK", "Fund bank", "USD", "Bank", true,
        Now.AddYears(-1), null, null, null, null, null);
    private static FundStructureTenantMap Tenants => new(true, new Dictionary<Guid, string>
    { [EntityId] = "tenant-a", [FundId] = "tenant-a", [AccountId] = "tenant-a" });

    [Fact]
    public void FundBook_UsesRetainedEntityMembershipRatherThanEquatingFundAndEntityIds()
    {
        Action verify = () => RecurringJournalSubjectAuthority.ValidateSubject(Scope, Book, Entity, Fund, null, null, null, Tenants, Now);
        verify.Should().NotThrow();
        Action unrelated = () => RecurringJournalSubjectAuthority.ValidateSubject(Scope, Book, Entity, Fund with { EntityIds = [] }, null, null, null, Tenants, Now);
        unrelated.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EntityBook_RequiresItsExactRetainedLegalEntity()
    {
        var book = Book with { FundStructureNodeId = EntityId, FundStructureNodeKind = FundStructureNodeKindDto.Entity };
        Action verify = () => RecurringJournalSubjectAuthority.ValidateSubject(Scope, book, Entity, null, null, null, null, Tenants, Now);
        verify.Should().NotThrow();
        Action mismatch = () => RecurringJournalSubjectAuthority.ValidateSubject(Scope, Book with { FundStructureNodeKind = FundStructureNodeKindDto.Entity }, Entity, null, null, null, null, Tenants, Now);
        mismatch.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void OptionalAccount_WhenProvidedMustBelongToSameEntityAndBookOwner()
    {
        var scope = Scope with { FundAccountId = AccountId.ToString("D") };
        Action valid = () => RecurringJournalSubjectAuthority.ValidateSubject(scope, Book, Entity, Fund, null, null, Account, Tenants, Now);
        valid.Should().NotThrow();
        foreach (var account in new[] { Account with { EntityId = Guid.NewGuid() }, Account with { FundId = Guid.NewGuid() }, Account with { IsActive = false } })
        {
            Action invalid = () => RecurringJournalSubjectAuthority.ValidateSubject(scope, Book, Entity, Fund, null, null, account, Tenants, Now);
            invalid.Should().Throw<InvalidOperationException>();
        }
    }

    [Fact]
    public void MissingInactiveForeignOrUnattributedOwnership_FailsClosed()
    {
        Action missing = () => RecurringJournalSubjectAuthority.ValidateSubject(Scope, Book, null, Fund, null, null, null, Tenants, Now);
        Action inactive = () => RecurringJournalSubjectAuthority.ValidateSubject(Scope, Book, Entity with { IsActive = false }, Fund, null, null, null, Tenants, Now);
        Action expired = () => RecurringJournalSubjectAuthority.ValidateSubject(Scope, Book, Entity, Fund with { EffectiveTo = Now }, null, null, null, Tenants, Now);
        Action unpartitioned = () => RecurringJournalSubjectAuthority.ValidateSubject(Scope, Book, Entity, Fund, null, null, null, FundStructureTenantMap.Unpartitioned, Now);
        Action foreign = () => RecurringJournalSubjectAuthority.ValidateSubject(Scope with { TenantId = "tenant-b" }, Book, Entity, Fund, null, null, null, Tenants, Now);
        foreach (var check in new[] { missing, inactive, expired, unpartitioned, foreign })
            check.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AccountBook_CannotInferAnAccountFromAnOmittedScope()
    {
        var book = Book with { FundStructureNodeId = AccountId, FundStructureNodeKind = FundStructureNodeKindDto.Account };
        Action verify = () => RecurringJournalSubjectAuthority.ValidateSubject(Scope, book, Entity, null, null, null, Account, Tenants, Now);
        verify.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task MissingDurableStructureAuthority_IsUnavailableRatherThanUnscoped()
    {
        var authority = new RecurringJournalSubjectAuthority();
        Func<Task> verify = () => authority.VerifyAsync(Scope, Book, new DateOnly(2026, 10, 1), CancellationToken.None);
        await verify.Should().ThrowAsync<InvalidOperationException>();
    }
}
