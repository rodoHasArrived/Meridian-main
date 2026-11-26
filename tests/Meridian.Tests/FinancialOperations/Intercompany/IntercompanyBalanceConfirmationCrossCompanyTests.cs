using Meridian.Contracts.Ledger;
using Meridian.Contracts.Tenancy;
using Meridian.Tests.TestSupport;
using static Meridian.Tests.TestSupport.IntercompanyBalanceConfirmationFixture;

namespace Meridian.Tests.FinancialOperations.Intercompany;

/// <summary>
/// June month-end for a loan between a lender held by company-a and a borrower held by company-b of the same tenant
/// (DEC-IC-CONFIRM-003). The approved, tenant-scoped profile links the two books; each company sees the confirmation and
/// acts only on its own side, and neither company can settle the other's position in a dispute. Synthetic only.
/// </summary>
public sealed class IntercompanyBalanceConfirmationCrossCompanyTests
{
    private const string Borrowers = "company-b";
    private const string Outsider = "company-c";

    [Fact]
    public async Task EachCompanySeesThePairAndConfirmsOnlyItsOwnSide()
    {
        using var fixture = await CrossCompanyAsync();

        var proposed = await fixture.Service.ProposeAsync(Tenant, Company, "lender-operations", new(Guid.NewGuid(), Profile, MonthEnd));

        Assert.True(proposed.IsCrossCompany);
        Assert.Equal((Company, Company, Borrowers), (proposed.CompanyId, proposed.PartyOneCompanyId, proposed.PartyTwoCompanyId));
        Assert.Equal(0m, proposed.NetTransactionBalance);
        Assert.NotNull(await fixture.Service.GetAsync(Tenant, Borrowers, proposed.ConfirmationId));
        Assert.Null(await fixture.Service.GetAsync(Tenant, Outsider, proposed.ConfirmationId));

        // The borrower's company cannot sign off the lender's side, and the lender's company cannot sign off the borrower's.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ActAsync(Tenant, Borrowers, "borrower-controller",
            proposed.ConfirmationId, Confirm(proposed, IntercompanyBalanceConfirmationPartyDto.PartyOne)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ActAsync(Tenant, Company, "lender-controller",
            proposed.ConfirmationId, Confirm(proposed, IntercompanyBalanceConfirmationPartyDto.PartyTwo)));

        var lender = await fixture.Service.ActAsync(Tenant, Company, "lender-controller", proposed.ConfirmationId,
            Confirm(proposed, IntercompanyBalanceConfirmationPartyDto.PartyOne));
        var both = await fixture.Service.ActAsync(Tenant, Borrowers, "borrower-controller", proposed.ConfirmationId,
            Confirm(lender, IntercompanyBalanceConfirmationPartyDto.PartyTwo));

        Assert.Equal(IntercompanyBalanceConfirmationStatusDto.Confirmed, both.Status);
        Assert.True((await fixture.Service.SummaryAsync(Tenant, Company, LenderBook, MonthEnd)).AllConfirmed);
        Assert.True((await fixture.Service.SummaryAsync(Tenant, Borrowers, BorrowerBook, MonthEnd)).AllConfirmed);
    }

    [Fact]
    public async Task OnePersonHoldingBothCompanies_StillCannotConfirmBothSides()
    {
        using var fixture = await CrossCompanyAsync();
        var proposed = await fixture.Service.ProposeAsync(Tenant, Company, "lender-operations", new(Guid.NewGuid(), Profile, MonthEnd));
        var lender = await fixture.Service.ActAsync(Tenant, Company, "group-controller", proposed.ConfirmationId,
            Confirm(proposed, IntercompanyBalanceConfirmationPartyDto.PartyOne));

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ActAsync(Tenant, Borrowers, "group-controller",
            proposed.ConfirmationId, Confirm(lender, IntercompanyBalanceConfirmationPartyDto.PartyTwo)));
        Assert.Contains("same person cannot confirm both sides", refusal.Message);
    }

    [Fact]
    public async Task ACrossCompanyDispute_IsResolvedOnlyWhenEachSideRecordsItsOwnResolution()
    {
        using var fixture = await CrossCompanyAsync();
        var proposed = await fixture.Service.ProposeAsync(Tenant, Company, "lender-operations", new(Guid.NewGuid(), Profile, MonthEnd));
        var disputed = await fixture.Service.ActAsync(Tenant, Borrowers, "borrower-controller", proposed.ConfirmationId,
            Step(proposed, IntercompanyBalanceConfirmationActionDto.Dispute, IntercompanyBalanceConfirmationPartyDto.PartyTwo, "June interest missing."));

        // Neither company resolves the other's side.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ActAsync(Tenant, Company, "lender-controller",
            proposed.ConfirmationId, Step(disputed, IntercompanyBalanceConfirmationActionDto.Resolve, IntercompanyBalanceConfirmationPartyDto.PartyTwo, "Agreed.")));
        // A resolution naming no side needs authority over both books, which no single company holds here.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.ActAsync(Tenant, Company, "lender-controller",
            proposed.ConfirmationId, Step(disputed, IntercompanyBalanceConfirmationActionDto.Resolve, null, "Agreed.")));

        var lenderSide = await fixture.Service.ActAsync(Tenant, Company, "lender-controller", proposed.ConfirmationId,
            Step(disputed, IntercompanyBalanceConfirmationActionDto.Resolve, IntercompanyBalanceConfirmationPartyDto.PartyOne, "Lender will book the accrual."));
        Assert.Equal(IntercompanyBalanceConfirmationStatusDto.Disputed, lenderSide.Status);
        Assert.Contains("party one recorded its resolution",
            Assert.Single((await fixture.Service.SummaryAsync(Tenant, Borrowers, BorrowerBook, MonthEnd)).Findings).Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ActAsync(Tenant, Company, "lender-reviewer",
            proposed.ConfirmationId, Step(lenderSide, IntercompanyBalanceConfirmationActionDto.Resolve, IntercompanyBalanceConfirmationPartyDto.PartyOne, "Again.")));

        // The borrower who raised the dispute cannot resolve it; a colleague can.
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ActAsync(Tenant, Borrowers, "borrower-controller",
            proposed.ConfirmationId, Step(lenderSide, IntercompanyBalanceConfirmationActionDto.Resolve, IntercompanyBalanceConfirmationPartyDto.PartyTwo, "Fine.")));
        var resolved = await fixture.Service.ActAsync(Tenant, Borrowers, "borrower-reviewer", proposed.ConfirmationId,
            Step(lenderSide, IntercompanyBalanceConfirmationActionDto.Resolve, IntercompanyBalanceConfirmationPartyDto.PartyTwo, "Accepted the lender's plan."));

        Assert.Equal(IntercompanyBalanceConfirmationStatusDto.Resolved, resolved.Status);
        Assert.Equal([IntercompanyBalanceConfirmationPartyDto.PartyOne, IntercompanyBalanceConfirmationPartyDto.PartyTwo],
            resolved.Events.Where(item => item.Action == IntercompanyBalanceConfirmationActionDto.Resolve).Select(item => item.Party!.Value));
    }

    [Fact]
    public async Task ACompanyHoldingNeitherBook_CannotProposeOrReadThePair()
    {
        using var fixture = await CrossCompanyAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.ProposeAsync(Tenant, Outsider, "outsider", new(Guid.NewGuid(), Profile, MonthEnd)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.SummaryAsync(Tenant, Outsider, BorrowerBook, MonthEnd));
    }

    [Fact]
    public async Task AnotherTenantsBook_IsNeverLinkedByTheProfile()
    {
        using var fixture = await CrossCompanyAsync();
        fixture.Owners["borrower-fund"] = new("borrower-fund", "tenant-b", Borrowers);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.ProposeAsync(Tenant, Company, "lender-operations", new(Guid.NewGuid(), Profile, MonthEnd)));
    }

    [Fact]
    public async Task TheBorrowersCompanyMayProposeAgain_AndTheRecordKeepsItsFirstProposer()
    {
        using var fixture = await CrossCompanyAsync();
        var first = await fixture.Service.ProposeAsync(Tenant, Company, "lender-operations", new(Guid.NewGuid(), Profile, MonthEnd));
        fixture.Balances.Lender = 1_004_166.67m;
        fixture.Balances.Borrower = -1_004_166.67m;

        var again = await fixture.Service.ProposeAsync(Tenant, Borrowers, "borrower-operations", new(Guid.NewGuid(), Profile, MonthEnd));

        Assert.Equal((first.ConfirmationId, Company, 2L), (again.ConfirmationId, again.CompanyId, again.Version));
        Assert.Equal(1_004_166.67m, again.PartyOne.TransactionSignedBalance);
    }

    private static async Task<IntercompanyBalanceConfirmationFixture> CrossCompanyAsync()
    {
        var fixture = await CreateAsync();
        fixture.Owners["borrower-fund"] = new FundProfileOwnership("borrower-fund", Tenant, Borrowers);
        return fixture;
    }

    private static IntercompanyBalanceConfirmationActionRequest Step(IntercompanyBalanceConfirmationDto current,
        IntercompanyBalanceConfirmationActionDto action, IntercompanyBalanceConfirmationPartyDto? party, string reason)
        => new(Guid.NewGuid(), action, party, current.Version, current.BalanceDigest, reason);
}
