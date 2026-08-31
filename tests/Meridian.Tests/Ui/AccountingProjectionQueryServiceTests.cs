using System.Collections.Immutable;
using FluentAssertions;
using Meridian.Contracts.Ledger;
using Meridian.FinancialOperations.AccountingClose;
using Meridian.Ui.Services.Services.Accounting;

namespace Meridian.Tests.Ui;

public sealed class AccountingProjectionQueryServiceTests
{
    [Fact]
    public void GetTrialBalance_WithDimensionScope_ReturnsOnlyMatchingLedgerRows()
    {
        var service = CreateServiceWithDimensionedLedger();

        var scoped = service.GetTrialBalance(
            "ledger-gaap",
            new LedgerDimensionSetDto(
                FundId: "fund-alpha",
                EntityId: "entity-alpha",
                CostCenterId: "investment-ops",
                ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Department"] = "Investments"
                }));

        scoped.Should().HaveCount(2);
        scoped.Select(static line => line.Dimensions?.EntityId).Should().OnlyContain(entityId => entityId == "entity-alpha");
        scoped.Sum(static line => line.Debit).Should().Be(125m);
        scoped.Sum(static line => line.Credit).Should().Be(125m);
        scoped.SelectMany(static line => line.SourceEventIds).Should().OnlyContain(source => source == "evt-alpha");
        scoped.Select(static line => line.Dimensions?.ExternalGlDimensions.GetValueOrDefault("Department"))
            .Should()
            .OnlyContain(department => department == "Investments");
    }

    [Fact]
    public void GetRollForward_WithDimensionScope_PreservesScopedActivityRows()
    {
        var service = CreateServiceWithDimensionedLedger();

        var rollForward = service.GetRollForward(
            "ledger-gaap",
            new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-beta"));

        rollForward.Should().HaveCount(2);
        rollForward.Select(static line => line.Dimensions?.EntityId).Should().OnlyContain(entityId => entityId == "entity-beta");
        rollForward.Sum(static line => line.Activity).Should().Be(150m - 150m);
        rollForward.Single(line => line.AccountCode == "Cash").Activity.Should().Be(150m);
        rollForward.Single(line => line.AccountCode == "Revenue").Activity.Should().Be(-150m);
        rollForward.SelectMany(static line => line.SourceEventIds).Should().OnlyContain(source => source == "evt-beta");
    }

    [Fact]
    public void GetCloseProjection_WithDimensionScope_UsesOnlyScopedCloseData()
    {
        var service = CreateServiceWithDimensionedLedger();

        var projection = service.GetCloseProjection(
            "ledger-gaap",
            new DateOnly(2026, 5, 31),
            new CloseEvidence(true, true, true),
            new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-alpha"));

        projection.TrialBalanceBalanced.Should().BeTrue();
        projection.TrialBalance.Should().HaveCount(2);
        projection.RollForward.Should().HaveCount(2);
        projection.TrialBalance.Select(static line => line.Dimensions?.EntityId).Should().OnlyContain(entityId => entityId == "entity-alpha");
        projection.RollForward.Select(static line => line.Dimensions?.EntityId).Should().OnlyContain(entityId => entityId == "entity-alpha");
        projection.AuditTrail.Select(static line => line.SourceEventId).Should().BeEquivalentTo(["evt-alpha"]);
        projection.EvidencePackage.SourceEventIds.Should().BeEquivalentTo(["evt-alpha"]);
        projection.ClosePeriod.Dimensions.Should().BeEquivalentTo(new LedgerDimensionSetDto(FundId: "fund-alpha", EntityId: "entity-alpha"));
        projection.ClosePeriod.State.Should().Be(ClosePeriodState.Closed);
    }

    [Fact]
    public void GetCloseProjection_BuildsEvidencePackageWithApprovalHistoryAndStableHash()
    {
        var posting = new AccountingPostingService();
        var service = new AccountingProjectionQueryService(posting, new TrialBalanceProjectionService());
        posting.Post("ledger-a", [NewEntry("ledger-a", "evt-post", "approval-post", 100m, new LedgerDimensionSetDto())]);
        var evidence = new CloseEvidence(
            TrialBalanceSignedOff: true,
            ReconciliationSignedOff: true,
            ApprovalsCompleted: true,
            Checks:
            [
                new CloseEvidenceCheck(
                    "controller-packet",
                    "Controller close packet",
                    Required: true,
                    Passed: true,
                    SourceEventId: "evt-close",
                    ApprovalId: "approval-close",
                    Detail: "Controller package approved.")
            ]);

        var first = service.GetCloseProjection("ledger-a", new DateOnly(2026, 03, 31), evidence);
        var replay = service.GetCloseProjection("ledger-a", new DateOnly(2026, 03, 31), evidence);

        first.ClosePeriod.State.Should().Be(ClosePeriodState.Closed);
        first.EvidencePackage.PackageId.Should().StartWith("close-evidence-ledger-a-20260331-");
        first.EvidencePackage.EvidenceHash.Should().MatchRegex("^[a-f0-9]{64}$");
        first.EvidencePackage.EvidenceHash.Should().Be(replay.EvidencePackage.EvidenceHash);
        first.EvidencePackage.PackageId.Should().Be(replay.EvidencePackage.PackageId);
        first.EvidencePackage.SourceEventIds.Should().BeEquivalentTo(["evt-close", "evt-post"]);
        first.EvidencePackage.ApprovalIds.Should().BeEquivalentTo(["approval-close", "approval-post"]);
        first.ApprovalHistory.Should().Contain(row =>
            row.Label == "Controller close packet" &&
            row.ApprovalId == "approval-close" &&
            row.Completed);
        first.ApprovalHistory.Should().Contain(row =>
            row.JournalEntryId.HasValue &&
            row.SourceEventId == "evt-post" &&
            row.ApprovalId == "approval-post" &&
            row.AccountCodes.Contains("Cash"));
    }

    private static AccountingProjectionQueryService CreateServiceWithDimensionedLedger()
    {
        var posting = new AccountingPostingService();
        var projection = new TrialBalanceProjectionService();
        var service = new AccountingProjectionQueryService(posting, projection);
        var alpha = new LedgerDimensionSetDto(
            FundId: "fund-alpha",
            EntityId: "entity-alpha",
            CostCenterId: "investment-ops",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Investments"
            });
        var beta = new LedgerDimensionSetDto(
            FundId: "fund-alpha",
            EntityId: "entity-beta",
            CostCenterId: "fund-admin",
            ExternalGlDimensions: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Department"] = "Operations"
            });

        posting.Post("ledger-gaap", [
            NewEntry("ledger-gaap", "evt-alpha", "approval-alpha", 125m, alpha),
            NewEntry("ledger-gaap", "evt-beta", "approval-beta", 150m, beta)
        ]);
        return service;
    }

    private static JournalEntry NewEntry(
        string ledgerId,
        string sourceEventId,
        string approvalId,
        decimal amount,
        LedgerDimensionSetDto dimensions)
        => new(
            Guid.NewGuid(),
            ledgerId,
            new DateOnly(2026, 5, 31),
            sourceEventId,
            "dimensioned close activity",
            ImmutableArray.Create(
                new JournalLine("Cash", amount, "USD", true, sourceEventId, approvalId, Dimensions: dimensions),
                new JournalLine("Revenue", amount, "USD", false, sourceEventId, approvalId, Dimensions: dimensions)));
}
