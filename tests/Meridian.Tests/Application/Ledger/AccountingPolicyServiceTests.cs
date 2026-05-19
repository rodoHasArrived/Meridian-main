using FluentAssertions;
using Meridian.Application.Ledger;
using Meridian.Contracts.Ledger;
using Meridian.Ledger;

namespace Meridian.Tests.Application.Ledger;

public sealed class AccountingPolicyServiceTests
{
    [Fact]
    public async Task ResolvePolicyAsync_ReturnsDefaultPolicyForEachAccountingBasis()
    {
        var service = new AccountingPolicyService();

        var policies = await service.ListPoliciesAsync();

        policies.Should().Contain(policy => policy.AccountingBasis == AccountingBasisKindDto.Primary && policy.PolicyId == "legacy-v1");
        policies.Should().Contain(policy => policy.AccountingBasis == AccountingBasisKindDto.Gaap && policy.PolicyId == "gaap-default-v1");
        policies.Should().Contain(policy => policy.AccountingBasis == AccountingBasisKindDto.Cash && policy.PolicyId == "cash-default-v1");
        policies.Should().Contain(policy => policy.AccountingBasis == AccountingBasisKindDto.Tax && policy.PolicyId == "tax-default-v1");
        policies.Should().Contain(policy => policy.AccountingBasis == AccountingBasisKindDto.Statutory && policy.PolicyId == "stat-default-v1");
    }

    [Fact]
    public async Task ProjectAsync_StampsPolicyRuleAndSourceLineageOnJournalWrite()
    {
        var policyService = new AccountingPolicyService();
        var projectionService = new AccountingBasisProjectionService(policyService);
        var periodId = Guid.NewGuid();
        var aggregateId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        var sourceJournalEntryId = Guid.NewGuid();

        var result = await projectionService.ProjectAsync(new AccountingBasisProjectionRequest(
            SourceEntry: BuildBalancedJournalEntry(),
            AggregateId: aggregateId,
            PeriodId: periodId,
            AccountingBasis: AccountingBasisKindDto.Gaap,
            EffectiveDate: new DateOnly(2026, 5, 13),
            SourceEventId: sourceEventId,
            SourceJournalEntryId: sourceJournalEntryId,
            RuleId: "direct-lending.daily-accrual",
            RuleVersion: "rule-v2"));

        result.Policy.AccountingBasis.Should().Be(AccountingBasisKindDto.Gaap);
        result.Write.AggregateId.Should().Be(aggregateId);
        result.Write.PeriodId.Should().Be(periodId);
        result.Write.AccountingBasis.Should().Be(AccountingBasisKindDto.Gaap);
        result.Write.AccountingPolicyId.Should().Be("gaap-default-v1");
        result.Write.AccountingPolicyVersion.Should().Be("v1");
        result.Write.RuleId.Should().Be("direct-lending.daily-accrual");
        result.Write.RuleVersion.Should().Be("rule-v2");
        result.Write.SourceEventId.Should().Be(sourceEventId);
        result.Write.SourceJournalEntryId.Should().Be(sourceJournalEntryId);
        result.Write.PostingKind.Should().Be(LedgerPostingKindDto.Originating);
    }

    [Fact]
    public async Task ProjectAsync_PreservesAdjustmentPostingKindOnJournalWrite()
    {
        var policyService = new AccountingPolicyService();
        var projectionService = new AccountingBasisProjectionService(policyService);
        var approval = BuildApprovedAdjustmentApproval();

        var result = await projectionService.ProjectAsync(new AccountingBasisProjectionRequest(
            SourceEntry: BuildBalancedJournalEntry(),
            AggregateId: Guid.NewGuid(),
            PeriodId: Guid.NewGuid(),
            PostingKind: LedgerPostingKindDto.Adjustment,
            AdjustmentApproval: approval));

        result.Write.PostingKind.Should().Be(LedgerPostingKindDto.Adjustment);
        result.Write.AdjustmentApproval.Should().BeEquivalentTo(approval);
    }

    [Fact]
    public async Task ResolvePolicyAsync_PrefersMatchingScopedPolicyOverGlobalDefault()
    {
        var service = new AccountingPolicyService();
        var nodeId = Guid.NewGuid();
        var sourceEventId = Guid.NewGuid();
        await service.CreatePolicyAsync(new CreateAccountingPolicyRequest(
            AccountingBasisKindDto.Tax,
            PolicyId: "tax-fund-a-lifo",
            Version: "v3",
            DisplayName: "Fund A tax lot policy",
            EffectiveFrom: new DateOnly(2026, 1, 1),
            RulesJson: """{"lotRelief":"LIFO"}""",
            FundProfileId: "fund-a",
            FundStructureNodeId: nodeId,
            InstrumentId: "AAPL",
            SourceEventId: sourceEventId));

        var resolved = await service.ResolvePolicyAsync(new AccountingPolicyQuery(
            AccountingBasisKindDto.Tax,
            EffectiveDate: new DateOnly(2026, 5, 13),
            FundProfileId: "fund-a",
            FundStructureNodeId: nodeId,
            InstrumentId: "AAPL",
            SourceEventId: sourceEventId));

        resolved.PolicyId.Should().Be("tax-fund-a-lifo");
        resolved.Version.Should().Be("v3");
        resolved.RulesJson.Should().Contain("LIFO");
    }

    private static JournalEntry BuildBalancedJournalEntry()
    {
        var journalEntryId = Guid.NewGuid();
        var timestamp = DateTimeOffset.Parse("2026-05-13T12:00:00Z");
        return new JournalEntry(
            journalEntryId,
            timestamp,
            "GAAP accrual projection",
            [
                new LedgerEntry(
                    Guid.NewGuid(),
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("AccruedInterestReceivable", LedgerAccountType.Asset),
                    debit: 42m,
                    credit: 0m,
                    "GAAP accrual projection"),
                new LedgerEntry(
                    Guid.NewGuid(),
                    journalEntryId,
                    timestamp,
                    new LedgerAccount("InterestIncome", LedgerAccountType.Revenue),
                    debit: 0m,
                    credit: 42m,
                    "GAAP accrual projection")
            ]);
    }

    private static LedgerAdjustmentApprovalMetadataDto BuildApprovedAdjustmentApproval() =>
        new(
            ApprovalId: "approval-gaap-adjustment-1",
            Status: LedgerAdjustmentApprovalStatusDto.Approved,
            ApprovedBy: "fund-controller",
            ApprovedAt: DateTimeOffset.Parse("2026-05-13T13:00:00Z"),
            ReasonCode: "basis-adjustment",
            GovernanceCaseId: "case-gaap-approval-1",
            EvidenceLink: "evidence://ledger/adjustment/gaap-approval-1");
}
