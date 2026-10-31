using Meridian.Contracts.FundStructure;
using Meridian.Entities.FundStructure;
using Xunit;

namespace Meridian.FundStructure.Tests;

public abstract partial class FundStructureScopeContractTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AccountingNarrowedQuery_ReturnsOnlyRelevantAssignmentsAndExplicitContext(int selector)
    {
        var fixture = await SeedAsync();
        AssertScope(await QueryAsync(fixture.Service),
            [fixture.A.OrganizationId, fixture.B.OrganizationId], fixture.A, fixture.B);
        var selectedFundAssignment = Guid.NewGuid();
        var selectedPortfolioAssignment = Guid.NewGuid();
        foreach (var (assignmentId, nodeId, reference) in new[]
        {
            (Guid.NewGuid(), fixture.A.FundId, "FUND-A"),
            (Guid.NewGuid(), fixture.A.PortfolioId, "PORTFOLIO-A"),
            (selectedFundAssignment, fixture.B.FundId, "FUND-B"),
            (selectedPortfolioAssignment, fixture.B.PortfolioId, "PORTFOLIO-B")
        })
            await fixture.Service.AssignNodeAsync(new AssignFundStructureNodeRequest(
                assignmentId, nodeId, "LedgerGroup", reference, EffectiveFrom, "contract-test", IsPrimary: true));

        var query = selector switch
        {
            0 => new AccountingStructureQuery(FundId: fixture.B.FundId, AsOf: AsOf),
            1 => new AccountingStructureQuery(InvestmentPortfolioId: fixture.B.PortfolioId, AsOf: AsOf),
            _ => new AccountingStructureQuery(LedgerReference: "BOOK-B", AsOf: AsOf)
        };
        var view = await fixture.Service.GetAccountingViewAsync(query);
        var expectedAccountIds = selector switch
        {
            0 => new[] { fixture.B.DirectAccountId, fixture.B.LinkedAccountId },
            1 => new[] { fixture.B.LinkedAccountId },
            _ => new[] { fixture.B.DirectAccountId, fixture.B.EntityAccountId }
        };
        var expectedAssignmentIds = selector switch
        {
            0 => new[] { fixture.B.AssignmentId, selectedFundAssignment, selectedPortfolioAssignment },
            1 => new[] { fixture.B.AssignmentId, selectedPortfolioAssignment },
            _ => new[] { selectedFundAssignment }
        };

        AssertIds(expectedAccountIds, view.Accounts.Select(account => account.AccountId));
        Assert.NotNull(view.Assignments);
        AssertIds(expectedAssignmentIds, view.Assignments.Select(assignment => assignment.AssignmentId));
        Assert.Null(view.Organization);
        Assert.Null(view.Business);
        if (selector is 0 or 1)
            Assert.Equal(fixture.B.PortfolioId, Assert.Single(view.InvestmentPortfolios).InvestmentPortfolioId);

        var workbench = LedgerMappingWorkbenchService.Build(view, AsOf);
        if (selector is 0 or 2)
        {
            var direct = Assert.Single(workbench.Accounts, account => account.AccountId == fixture.B.DirectAccountId);
            Assert.Equal(LedgerMappingSourceDto.FundAssignment, direct.Mapping.Source);
            Assert.Equal("FUND-B", direct.Mapping.LedgerGroupId.Value);
            Assert.Contains(view.LedgerGroups, group =>
                group.DisplayName == "FUND-B" && group.AccountIds.Contains(fixture.B.DirectAccountId));
        }
        if (selector is 0 or 1)
        {
            var linked = Assert.Single(workbench.Accounts, account => account.AccountId == fixture.B.LinkedAccountId);
            Assert.Equal(LedgerMappingSourceDto.AccountAssignment, linked.Mapping.Source);
            Assert.Equal("ASSIGNED-B", linked.Mapping.LedgerGroupId.Value);
        }

        // An explicitly requested retained parent remains useful context; a descendant-only
        // query must not choose the first unrelated organization/business in the tenant input.
        var explicitContext = await fixture.Service.GetAccountingViewAsync(query with
        {
            OrganizationId = fixture.B.OrganizationId,
            BusinessId = fixture.B.BusinessId
        });
        Assert.Equal(fixture.B.OrganizationId, explicitContext.Organization?.OrganizationId);
        Assert.Equal(fixture.B.BusinessId, explicitContext.Business?.BusinessId);
        AssertIds(expectedAccountIds, explicitContext.Accounts.Select(account => account.AccountId));
        Assert.NotNull(explicitContext.Assignments);
        AssertIds(expectedAssignmentIds, explicitContext.Assignments.Select(assignment => assignment.AssignmentId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AccountingEmptyNarrowedQuery_DoesNotRetainUnrelatedAssignmentsOrImplicitContext(int selector)
    {
        var fixture = await SeedAsync();
        AssertScope(await QueryAsync(fixture.Service),
            [fixture.A.OrganizationId, fixture.B.OrganizationId], fixture.A, fixture.B);
        var query = selector switch
        {
            0 => new AccountingStructureQuery(FundId: Guid.NewGuid(), AsOf: AsOf),
            1 => new AccountingStructureQuery(FundId: fixture.B.FundId,
                InvestmentPortfolioId: fixture.A.PortfolioId, AsOf: AsOf),
            2 => new AccountingStructureQuery(FundId: fixture.B.FundId,
                SleeveId: fixture.A.SleeveId, AsOf: AsOf),
            _ => new AccountingStructureQuery(LedgerReference: "UNKNOWN-LEDGER", AsOf: AsOf)
        };
        var view = await fixture.Service.GetAccountingViewAsync(query);

        Assert.Empty(view.Accounts);
        Assert.Empty(view.LedgerGroups);
        Assert.NotNull(view.Assignments);
        Assert.Empty(view.Assignments);
        Assert.Null(view.Organization);
        Assert.Null(view.Business);
        // LedgerReference filters accounts; its existing portfolio-list behavior is independent.
        if (selector is not 3)
            Assert.Empty(view.InvestmentPortfolios);
    }
}
