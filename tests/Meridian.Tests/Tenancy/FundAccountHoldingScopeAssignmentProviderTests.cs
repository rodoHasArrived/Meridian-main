using FluentAssertions;
using Meridian.Application.Tenancy;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Tenancy;
using Meridian.Storage.FundAccounts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Meridian.Tests.Tenancy;

/// <summary>
/// Attribution behaviour for the custodied-holdings authority. The scenarios below are the ones
/// that decide whether a corporate-action decision is safe to apply: who the security reaches,
/// and — just as important — when the answer is not known well enough to act on.
/// </summary>
public sealed class FundAccountHoldingScopeAssignmentProviderTests
{
    private static readonly Guid SecurityId = Guid.Parse("2f3a9b1c-5d47-4a86-9f10-6c0b2e7d8a53");
    private static readonly DateOnly RecordDate = new(2026, 8, 14);
    private static readonly Guid FundA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");
    private static readonly Guid FundB = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000002");

    private static ScopeFanOutRequest Request(string identifierType = "Cusip", string value = "037833100")
        => new(SecurityId, [new ScopeFanOutIdentifier(identifierType, value)], RecordDate);

    private static AccountSummaryDto Account(
        Guid accountId,
        Guid? fundId,
        AccountTypeDto accountType = AccountTypeDto.Custody,
        DateTimeOffset? effectiveFrom = null,
        DateTimeOffset? effectiveTo = null) =>
        new(
            accountId,
            accountType,
            EntityId: null,
            FundId: fundId,
            SleeveId: null,
            VehicleId: null,
            AccountCode: $"ACC-{accountId.ToString("D")[..4]}",
            DisplayName: "Custody account",
            BaseCurrency: "USD",
            Institution: "Custodian",
            IsActive: true,
            EffectiveFrom: effectiveFrom ?? new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo: effectiveTo,
            PortfolioId: "portfolio-1",
            LedgerReference: "book-1",
            StrategyId: null,
            RunId: null);

    private static CustodianStatementBatchDto Batch(Guid accountId) =>
        new(
            Guid.NewGuid(),
            accountId,
            RecordDate,
            CustodianName: "Custodian",
            SourceFormat: "csv",
            LineCount: 1,
            IngestedAt: new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero),
            LoadedBy: "ops");

    private static CustodianPositionLineDto PositionLine(
        Guid accountId,
        string identifier,
        string identifierType = "Cusip",
        decimal quantity = 100m) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            accountId,
            RecordDate,
            Identifier: identifier,
            IdentifierType: identifierType,
            Quantity: quantity,
            MarketValue: 1_000m,
            Currency: "USD",
            SecurityName: "Meridian Corp",
            AssetClass: "Equity",
            IsShort: false);

    private sealed class Fixture
    {
        public IFundAccountStore Accounts { get; } = Substitute.For<IFundAccountStore>();

        public IFundProfileTenancyRegistry Tenancy { get; } = Substitute.For<IFundProfileTenancyRegistry>();

        public FundAccountHoldingScopeAssignmentProvider Provider =>
            new(Accounts, Tenancy, NullLogger<FundAccountHoldingScopeAssignmentProvider>.Instance);

        public void WithAccounts(params AccountSummaryDto[] accounts)
            => Accounts.QueryAccountsAcrossTenantsAsync(
                    Arg.Any<AccountStructureQuery>(), Arg.Any<CancellationToken>())
                .Returns(accounts);

        public void WithStatement(Guid accountId, params CustodianPositionLineDto[] lines)
        {
            Accounts.GetCustodianStatementBatchesAsync(accountId, RecordDate, Arg.Any<CancellationToken>())
                .Returns(new[] { Batch(accountId) });
            Accounts.GetCustodianPositionsAsync(accountId, RecordDate, Arg.Any<CancellationToken>())
                .Returns(lines);
        }

        public void WithoutStatement(Guid accountId)
            => Accounts.GetCustodianStatementBatchesAsync(accountId, RecordDate, Arg.Any<CancellationToken>())
                .Returns([]);

        public void WithOwner(Guid fundId, string? tenantId, string? companyId)
            => Tenancy.ResolveAsync(fundId.ToString("D"), Arg.Any<CancellationToken>())
                .Returns(tenantId is null
                    ? null
                    : new FundProfileOwnership(fundId.ToString("D"), tenantId, companyId));
    }

    [Fact]
    public async Task Resolve_WithoutAFundAccountStore_IsNotAuthoritative()
    {
        var provider = new FundAccountHoldingScopeAssignmentProvider(
            accounts: null,
            tenancy: Substitute.For<IFundProfileTenancyRegistry>(),
            NullLogger<FundAccountHoldingScopeAssignmentProvider>.Instance);

        var result = await provider.ResolveAsync(Request());

        result.IsAuthoritative.Should().BeFalse();
        result.Blockers.Should().ContainMatch("*fund-account store*");
    }

    [Fact]
    public async Task Resolve_WithoutATenancyRegistry_IsNotAuthoritative()
    {
        var provider = new FundAccountHoldingScopeAssignmentProvider(
            Substitute.For<IFundAccountStore>(),
            tenancy: null,
            NullLogger<FundAccountHoldingScopeAssignmentProvider>.Instance);

        var result = await provider.ResolveAsync(Request());

        result.IsAuthoritative.Should().BeFalse();
        result.Blockers.Should().ContainMatch("*tenancy registry*");
    }

    [Fact]
    public async Task Resolve_WhenTheStoreCannotReadAcrossTenants_IsNotAuthoritative()
    {
        // A caller-scoped answer would look complete while omitting every other tenant, so the
        // store's refusal to read across tenants must degrade the whole slice.
        var fixture = new Fixture();
        fixture.Accounts.QueryAccountsAcrossTenantsAsync(
                Arg.Any<AccountStructureQuery>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<AccountSummaryDto>>(_ => throw new NotSupportedException());

        var result = await fixture.Provider.ResolveAsync(Request());

        result.IsAuthoritative.Should().BeFalse();
        result.Blockers.Should().ContainMatch("*across tenants*");
    }

    [Fact]
    public async Task Resolve_AttributesAHoldingToItsOwningTenantAndNarrowScope()
    {
        var fixture = new Fixture();
        var accountId = Guid.Parse("11111111-0000-4000-8000-000000000001");
        fixture.WithAccounts(Account(accountId, FundA));
        fixture.WithStatement(accountId, PositionLine(accountId, "037833100"));
        fixture.WithOwner(FundA, "tenant-a", "company-a");

        var result = await fixture.Provider.ResolveAsync(Request());

        result.IsAuthoritative.Should().BeTrue();
        var scope = result.Scopes.Should().ContainSingle().Subject;
        scope.TenantId.Should().Be("tenant-a");
        scope.CompanyId.Should().Be("company-a");
        scope.FundProfileId.Should().Be(FundA.ToString("D"));
        scope.FinancialAccountId.Should().Be(accountId.ToString("D"));
        scope.PortfolioId.Should().Be("portfolio-1");
        scope.LedgerBookId.Should().Be("book-1");
        scope.FunctionalCurrency.Should().Be("USD");
        scope.AuthorityId.Should().Be(FundAccountHoldingScopeAssignmentProvider.Authority);
    }

    [Fact]
    public async Task Resolve_MatchesIdentifiersThroughSecurityMasterNormalization()
    {
        var fixture = new Fixture();
        var accountId = Guid.Parse("11111111-0000-4000-8000-000000000002");
        fixture.WithAccounts(Account(accountId, FundA));
        // Same CUSIP, custodian formatting: lowercase with separators.
        fixture.WithStatement(accountId, PositionLine(accountId, " 037-833-100 ", "cusip"));
        fixture.WithOwner(FundA, "tenant-a", "company-a");

        var result = await fixture.Provider.ResolveAsync(Request());

        result.IsAuthoritative.Should().BeTrue();
        result.Scopes.Should().ContainSingle();
    }

    [Fact]
    public async Task Resolve_EnumeratesEveryAffectedTenantNotJustOne()
    {
        var fixture = new Fixture();
        var accountA = Guid.Parse("11111111-0000-4000-8000-00000000000a");
        var accountB = Guid.Parse("11111111-0000-4000-8000-00000000000b");
        fixture.WithAccounts(Account(accountA, FundA), Account(accountB, FundB));
        fixture.WithStatement(accountA, PositionLine(accountA, "037833100"));
        fixture.WithStatement(accountB, PositionLine(accountB, "037833100"));
        fixture.WithOwner(FundA, "tenant-a", "company-a");
        fixture.WithOwner(FundB, "tenant-b", "company-b");

        var result = await fixture.Provider.ResolveAsync(Request());

        result.IsAuthoritative.Should().BeTrue();
        result.Scopes.Select(scope => scope.TenantId).Should().BeEquivalentTo("tenant-a", "tenant-b");
    }

    [Fact]
    public async Task Resolve_WhenAnAccountHasNoStatementForTheDate_IsNotAuthoritative()
    {
        // The account may or may not hold the security; treating "unobserved" as "not a holder"
        // would shrink the affected set without saying so.
        var fixture = new Fixture();
        var holder = Guid.Parse("11111111-0000-4000-8000-00000000000c");
        var unobserved = Guid.Parse("11111111-0000-4000-8000-00000000000d");
        fixture.WithAccounts(Account(holder, FundA), Account(unobserved, FundB));
        fixture.WithStatement(holder, PositionLine(holder, "037833100"));
        fixture.WithoutStatement(unobserved);
        fixture.WithOwner(FundA, "tenant-a", "company-a");

        var result = await fixture.Provider.ResolveAsync(Request());

        result.IsAuthoritative.Should().BeFalse();
        result.Blockers.Should().ContainMatch("*no custodian statement*");
    }

    [Fact]
    public async Task Resolve_WhenAHoldingFundHasNoBoundTenant_IsNotAuthoritative()
    {
        var fixture = new Fixture();
        var accountId = Guid.Parse("11111111-0000-4000-8000-00000000000e");
        fixture.WithAccounts(Account(accountId, FundA));
        fixture.WithStatement(accountId, PositionLine(accountId, "037833100"));
        fixture.WithOwner(FundA, tenantId: null, companyId: null);

        var result = await fixture.Provider.ResolveAsync(Request());

        result.IsAuthoritative.Should().BeFalse();
        result.Scopes.Should().BeEmpty();
        result.Blockers.Should().ContainMatch("*no bound tenant owner*");
    }

    [Fact]
    public async Task Resolve_WhenAHoldingAccountHasNoFund_IsNotAuthoritative()
    {
        var fixture = new Fixture();
        var accountId = Guid.Parse("11111111-0000-4000-8000-00000000000f");
        fixture.WithAccounts(Account(accountId, fundId: null));
        fixture.WithStatement(accountId, PositionLine(accountId, "037833100"));

        var result = await fixture.Provider.ResolveAsync(Request());

        result.IsAuthoritative.Should().BeFalse();
        result.Blockers.Should().ContainMatch("*not assigned to a fund*");
    }

    [Fact]
    public async Task Resolve_IgnoresAccountKindsThatCannotCustodySecurities()
    {
        var fixture = new Fixture();
        var bankAccount = Guid.Parse("11111111-0000-4000-8000-000000000010");
        fixture.WithAccounts(Account(bankAccount, FundA, AccountTypeDto.Bank));

        var result = await fixture.Provider.ResolveAsync(Request());

        result.IsAuthoritative.Should().BeTrue();
        result.Scopes.Should().BeEmpty();
        await fixture.Accounts.DidNotReceive().GetCustodianStatementBatchesAsync(
            bankAccount, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolve_IgnoresAccountsThatWereNotEffectiveOnTheDate()
    {
        var fixture = new Fixture();
        var closed = Guid.Parse("11111111-0000-4000-8000-000000000011");
        fixture.WithAccounts(Account(
            closed,
            FundA,
            effectiveTo: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        var result = await fixture.Provider.ResolveAsync(Request());

        result.IsAuthoritative.Should().BeTrue();
        result.Scopes.Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_TreatsAClosedOutZeroQuantityLineAsNotHeld()
    {
        var fixture = new Fixture();
        var accountId = Guid.Parse("11111111-0000-4000-8000-000000000012");
        fixture.WithAccounts(Account(accountId, FundA));
        fixture.WithStatement(accountId, PositionLine(accountId, "037833100", quantity: 0m));
        fixture.WithOwner(FundA, "tenant-a", "company-a");

        var result = await fixture.Provider.ResolveAsync(Request());

        result.IsAuthoritative.Should().BeTrue();
        result.Scopes.Should().BeEmpty();
    }
}
