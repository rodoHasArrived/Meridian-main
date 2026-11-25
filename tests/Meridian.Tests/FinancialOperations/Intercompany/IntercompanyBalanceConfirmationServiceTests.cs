using Meridian.Contracts.Ledger;
using Meridian.Tests.TestSupport;
using static Meridian.Tests.TestSupport.IntercompanyBalanceConfirmationFixture;

namespace Meridian.Tests.FinancialOperations.Intercompany;

/// <summary>
/// June month-end for an approved intercompany loan profile. The lender's book holds a 1,000,000 receivable and the
/// borrower's the matching payable; another agreement's rows sit in the same books. Each side's balance is read from
/// its own posted schedule, never entered, and each side signs off through its own book.
/// </summary>
public sealed class IntercompanyBalanceConfirmationServiceTests
{
    [Fact]
    public async Task ProposalPinsEachSidesPostedBalanceForThisProfile_AndBothSidesConfirmThroughTheirOwnBooks()
    {
        using var fixture = await IntercompanyBalanceConfirmationFixture.CreateAsync();
        Assert.Equal("NotProposed", Assert.Single((await fixture.Service.SummaryAsync(Tenant, Company, LenderBook, MonthEnd)).Findings).Code);

        var proposed = await fixture.Service.ProposeAsync(Tenant, Company, "accounting-operations", new(Guid.NewGuid(), Profile, MonthEnd));

        Assert.Equal((LenderBook, 1_000_000m), (proposed.PartyOne.LedgerBookId, proposed.PartyOne.TransactionSignedBalance));
        Assert.Equal((BorrowerBook, -1_000_000m), (proposed.PartyTwo.LedgerBookId, proposed.PartyTwo.TransactionSignedBalance));
        // The other agreement's 250 in the same books is not part of this pair.
        Assert.Equal(0m, proposed.NetTransactionBalance);
        Assert.Equal("AwaitingConfirmation", Assert.Single((await fixture.Service.SummaryAsync(Tenant, Company, BorrowerBook, MonthEnd)).Findings).Code);

        var lender = await fixture.Service.ActAsync(Tenant, Company, "lender-controller", proposed.ConfirmationId, Request(proposed, IntercompanyBalanceConfirmationPartyDto.PartyOne));
        var both = await fixture.Service.ActAsync(Tenant, Company, "borrower-controller", lender.ConfirmationId, Request(lender, IntercompanyBalanceConfirmationPartyDto.PartyTwo));

        Assert.Equal(IntercompanyBalanceConfirmationStatusDto.Confirmed, both.Status);
        var summary = await fixture.Service.SummaryAsync(Tenant, Company, LenderBook, MonthEnd);
        Assert.True(summary.AllConfirmed);
        Assert.Equal(both.Version, Assert.Single(summary.Confirmations).Version);
    }

    [Fact]
    public async Task AnIdenticalRetryReturnsTheRetainedStep_AndAReusedIdentityForADifferentStepIsRefused()
    {
        using var fixture = await IntercompanyBalanceConfirmationFixture.CreateAsync();
        var proposal = new ProposeIntercompanyBalanceConfirmationRequest(Guid.NewGuid(), Profile, MonthEnd);
        var proposed = await fixture.Service.ProposeAsync(Tenant, Company, "accounting-operations", proposal);
        Assert.Equal(proposed.Version, (await fixture.Service.ProposeAsync(Tenant, Company, "accounting-operations", proposal)).Version);

        var confirm = Request(proposed, IntercompanyBalanceConfirmationPartyDto.PartyOne);
        var lender = await fixture.Service.ActAsync(Tenant, Company, "lender-controller", proposed.ConfirmationId, confirm);
        Assert.Equal(lender.Version, (await fixture.Service.ActAsync(Tenant, Company, "lender-controller", proposed.ConfirmationId, confirm)).Version);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.ActAsync(Tenant, Company, "lender-controller", proposed.ConfirmationId,
            confirm with { Action = IntercompanyBalanceConfirmationActionDto.Dispute, Reason = "Changed my mind" }));
    }

    [Fact]
    public async Task PostingAfterThePinMovesTheBalances_AndTheSummaryReportsThemChanged()
    {
        using var fixture = await IntercompanyBalanceConfirmationFixture.CreateAsync();
        var proposed = await fixture.Service.ProposeAsync(Tenant, Company, "accounting-operations", new(Guid.NewGuid(), Profile, MonthEnd));
        await fixture.Service.ActAsync(Tenant, Company, "lender-controller", proposed.ConfirmationId, Request(proposed, IntercompanyBalanceConfirmationPartyDto.PartyOne));

        // A late interest accrual posts to both books for June.
        fixture.Balances.Lender = 1_004_166.67m;
        fixture.Balances.Borrower = -1_004_166.67m;

        Assert.Equal("BalancesChanged", Assert.Single((await fixture.Service.SummaryAsync(Tenant, Company, LenderBook, MonthEnd)).Findings).Code);
        var refreshed = await fixture.Service.ProposeAsync(Tenant, Company, "accounting-operations", new(Guid.NewGuid(), Profile, MonthEnd));
        Assert.Equal((IntercompanyBalanceConfirmationStatusDto.Proposed, 1_004_166.67m), (refreshed.Status, refreshed.PartyOne.TransactionSignedBalance));
    }

    [Fact]
    public async Task AnUnreadableSchedule_IsReportedUnavailable_NotConfirmed()
    {
        using var fixture = await IntercompanyBalanceConfirmationFixture.CreateAsync();
        fixture.Balances.Available = false;

        var finding = Assert.Single((await fixture.Service.SummaryAsync(Tenant, Company, LenderBook, MonthEnd)).Findings);
        Assert.Equal("Unavailable", finding.Code);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Service.ProposeAsync(Tenant, Company, "accounting-operations", new(Guid.NewGuid(), Profile, MonthEnd)));
    }

    [Theory]
    [InlineData("tenant-b", Company)]
    [InlineData(Tenant, "company-b")]
    public async Task ABorrowerBookOutsideTheCallersTenantOrCompany_RefusesTheProposalAndTheBorrowersSignOff(string tenant, string company)
    {
        using var fixture = await IntercompanyBalanceConfirmationFixture.CreateAsync();
        var proposed = await fixture.Service.ProposeAsync(Tenant, Company, "accounting-operations", new(Guid.NewGuid(), Profile, MonthEnd));
        fixture.Owners["borrower-fund"] = new("borrower-fund", tenant, company);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            fixture.Service.ActAsync(Tenant, Company, "borrower-controller", proposed.ConfirmationId, Request(proposed, IntercompanyBalanceConfirmationPartyDto.PartyTwo)));
        // The lender's own side is still its own to confirm.
        var lender = await fixture.Service.ActAsync(Tenant, Company, "lender-controller", proposed.ConfirmationId,
            Request(proposed, IntercompanyBalanceConfirmationPartyDto.PartyOne));
        Assert.Equal(IntercompanyBalanceConfirmationStatusDto.ConfirmedByPartyOne, lender.Status);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Service.SummaryAsync(Tenant, Company, BorrowerBook, MonthEnd));
    }

    [Fact]
    public async Task AConfirmationIsInvisibleToAnotherCompanyOfTheTenant()
    {
        using var fixture = await IntercompanyBalanceConfirmationFixture.CreateAsync();
        var proposed = await fixture.Service.ProposeAsync(Tenant, Company, "accounting-operations", new(Guid.NewGuid(), Profile, MonthEnd));

        Assert.Null(await fixture.Service.GetAsync(Tenant, "company-b", proposed.ConfirmationId));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.Service.ActAsync(Tenant, "company-b", "lender-controller",
            proposed.ConfirmationId, Request(proposed, IntercompanyBalanceConfirmationPartyDto.PartyOne)));
    }

    private static IntercompanyBalanceConfirmationActionRequest Request(IntercompanyBalanceConfirmationDto current, IntercompanyBalanceConfirmationPartyDto party)
        => new(Guid.NewGuid(), IntercompanyBalanceConfirmationActionDto.Confirm, party, current.Version, current.BalanceDigest, null);
}
