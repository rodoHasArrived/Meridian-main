using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Xunit;

namespace Meridian.Tests.Application.FundStructure;

public sealed class LedgerGroupingRulesTests
{
    [Fact]
    public void ResolveLedgerGroupId_PrefersAccountAssignmentOverPortfolioAndMetadata()
    {
        var portfolioId = Guid.NewGuid();
        var account = CreateAccount(portfolioId: portfolioId, ledgerReference: "ACCOUNT-LEDGER");
        var ledgerAssignments = new Dictionary<Guid, LedgerGroupId>
        {
            [portfolioId] = LedgerGroupId.Create("PORTFOLIO-GROUP"),
            [account.AccountId] = LedgerGroupId.Create("ACCOUNT-GROUP")
        };

        var resolved = LedgerGroupingRules.ResolveLedgerGroupId(account, ledgerAssignments);

        Assert.Equal(LedgerGroupId.Create("ACCOUNT-GROUP"), resolved);
    }

    [Fact]
    public void ResolveLedgerGroupId_UsesPortfolioAssignmentBeforeLedgerReference()
    {
        var portfolioId = Guid.NewGuid();
        var account = CreateAccount(portfolioId: portfolioId, ledgerReference: "ACCOUNT-LEDGER");
        var ledgerAssignments = new Dictionary<Guid, LedgerGroupId>
        {
            [portfolioId] = LedgerGroupId.Create("PORTFOLIO-GROUP")
        };

        var resolved = LedgerGroupingRules.ResolveLedgerGroupId(account, ledgerAssignments);

        Assert.Equal(LedgerGroupId.Create("PORTFOLIO-GROUP"), resolved);
    }

    [Fact]
    public void ResolveLedgerGroupId_InvalidLedgerReferenceWithoutAssignments_ReturnsUnassigned()
    {
        var account = CreateAccount(ledgerReference: "BAD/GROUP");

        var resolved = LedgerGroupingRules.ResolveLedgerGroupId(account, new Dictionary<Guid, LedgerGroupId>());

        Assert.Equal(LedgerGroupId.Unassigned, resolved);
    }

    [Fact]
    public void BuildLedgerAssignments_InvalidLedgerGroupReference_ThrowsFormatException()
    {
        var assignments = new[]
        {
            new FundStructureAssignmentDto(
                Guid.NewGuid(),
                Guid.NewGuid(),
                LedgerGroupingRules.LedgerGroupAssignmentType,
                " BAD/GROUP ",
                DateTimeOffset.UtcNow,
                EffectiveTo: null,
                IsPrimary: true)
        };

        Assert.Throws<FormatException>(() => LedgerGroupingRules.BuildLedgerAssignments(assignments));
    }

    [Fact]
    public void NormalizeAssignmentReference_LedgerGroup_TrimsWhitespace()
    {
        var normalized = LedgerGroupingRules.NormalizeAssignmentReference(
            LedgerGroupingRules.LedgerGroupAssignmentType,
            "  FUND.OPS:PRIMARY  ");

        Assert.Equal("FUND.OPS:PRIMARY", normalized);
    }

    [Fact]
    public void ResolveLedgerGroupMapping_ReportsAssignmentSourceAndGroup()
    {
        var account = CreateAccount(ledgerReference: "ACCOUNT-LEDGER");
        var assignment = new FundStructureAssignmentDto(
            Guid.NewGuid(),
            account.AccountId,
            LedgerGroupingRules.LedgerGroupAssignmentType,
            "FUND.OPS:PRIMARY",
            DateTimeOffset.UtcNow,
            EffectiveTo: null,
            IsPrimary: true);

        var mapping = LedgerGroupingRules.ResolveLedgerGroupMapping(account, [assignment]);

        Assert.Equal(LedgerGroupId.Create("FUND.OPS:PRIMARY"), mapping.LedgerGroupId);
        Assert.Equal(LedgerMappingSourceDto.AccountAssignment, mapping.Source);
        Assert.Equal(FundStructureNodeKindDto.Account, mapping.SourceNodeKind);
        Assert.False(mapping.RequiresUserMapping);
        Assert.Empty(mapping.IssueCodes);
    }

    [Fact]
    public void ResolveLedgerGroupMapping_InvalidLedgerReferenceRequiresUserMapping()
    {
        var account = CreateAccount(ledgerReference: "BAD/GROUP");

        var mapping = LedgerGroupingRules.ResolveLedgerGroupMapping(account, []);

        Assert.Equal(LedgerGroupId.Unassigned, mapping.LedgerGroupId);
        Assert.Equal(LedgerMappingSourceDto.Unassigned, mapping.Source);
        Assert.True(mapping.RequiresUserMapping);
        Assert.Contains("ledger-mapping.invalid-ledger-reference", mapping.IssueCodes);
    }

    private static AccountSummaryDto CreateAccount(Guid? portfolioId = null, string? ledgerReference = null) =>
        new(
            AccountId: Guid.NewGuid(),
            AccountType: AccountTypeDto.Bank,
            EntityId: null,
            FundId: Guid.NewGuid(),
            SleeveId: null,
            VehicleId: null,
            AccountCode: "ACC-001",
            DisplayName: "Operating Cash",
            BaseCurrency: "USD",
            Institution: null,
            IsActive: true,
            EffectiveFrom: new DateTimeOffset(2026, 04, 22, 0, 0, 0, TimeSpan.Zero),
            EffectiveTo: null,
            PortfolioId: portfolioId?.ToString("D"),
            LedgerReference: ledgerReference,
            StrategyId: null,
            RunId: null);
}
