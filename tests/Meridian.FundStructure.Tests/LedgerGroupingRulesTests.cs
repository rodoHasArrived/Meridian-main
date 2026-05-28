using Meridian.Application.FundStructure;
using Meridian.Contracts.FundStructure;
using Xunit;

namespace Meridian.FundStructure.Tests;

public sealed class LedgerGroupingRulesTests
{
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

    private static AccountSummaryDto CreateAccount(string? ledgerReference = null) =>
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
            PortfolioId: null,
            LedgerReference: ledgerReference,
            StrategyId: null,
            RunId: null);
}
