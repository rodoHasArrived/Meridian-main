using System.Text.Json;
using FluentAssertions;
using Meridian.Application.SecurityMaster;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Contracts.Workstation;
using Meridian.Ledger;
using Meridian.Storage.SecurityMaster;
using Meridian.Tests.SecurityMaster;
using Meridian.Ui.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Meridian.Tests.Ui;

public sealed partial class RecurringJournalCalculationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CertificateOfDeposit_ReviewedProfileRetainsReceiptAndUsesExistingApprovalPostingFlow(bool maturesInWindow)
    {
        await SeedAsync(FixedIncomeAccrualCalculation.CalculationKind, new(),
            new() { ["securityId"] = SecurityId.ToString("D"), ["ticker"] = Ticker, ["bookPositionId"] = Position },
            endsOn: JanuaryEnd);
        var security = maturesInWindow
            ? SecurityRecurringAccrualPolicyTests.Security("maturity", "\"2026-01-15\"")
            : SecurityRecurringAccrualPolicyTests.Security();
        security = security with { Identifiers = [new(SecurityIdentifierKind.Ticker, Ticker, true, Registered)] };
        var query = new Mock<ISecurityMasterQueryService>(MockBehavior.Strict);
        query.Setup(value => value.GetByIdAsync(SecurityId, It.IsAny<CancellationToken>())).ReturnsAsync(security);
        query.Setup(value => value.GetByIdAsOfAsync(SecurityId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(security);
        var source = new Mock<ISecurityMasterCashFlowStore>(MockBehavior.Strict);
        source.Setup(value => value.GetSourceAsync(SecurityId, It.IsAny<CancellationToken>())).ReturnsAsync(
            new SecurityCashFlowSourceDto(SecurityId, StructuredCashFlowSourceKind.CalculatedBullet,
                DateTimeOffset.UtcNow, false, null, null));
        var receipts = new FileSecurityCashFlowReceiptStore(Path.Combine(_root, "cd-receipts"));
        var flows = new SecurityMasterCashFlowService(source.Object, [], query.Object,
            NullLogger<SecurityMasterCashFlowService>.Instance, receiptStore: receipts);
        var lot = new FaceValueLot("cd-par-lot", SecurityId, new(2025, 12, 31), 10_000m, 100m);
        var holdings = Enumerable.Range(0, 32).Select(day => new FaceValueAccrualHolding(
            new DateOnly(2025, 12, 31).AddDays(day), maturesInWindow && day >= 15 ? lot.WithFactor(0m) : lot, 1m, 1m,
            AmortizationMethod: BondAmortizationMethod.NoAmortization)).ToArray();
        var fixture = await CreateFixtureAsync(cashFlows: flows, securities: query.Object, accrualHoldings: holdings);

        var result = (await fixture.Runner.RunDueAsync(Due(JanuaryEnd))).Should().ContainSingle().Subject;

        result.State.Should().Be(AutomatedJournalScheduleStateDto.DraftReady);
        var draft = (await fixture.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        // 10,000 actual held face x 3.65% x 31 / 365 = 31.00, independent of source reference face.
        var expectedInterest = maturesInWindow ? 15m : 31m;
        draft.TotalDebits.Should().Be(expectedInterest);
        draft.TotalCredits.Should().Be(expectedInterest);
        draft.Lines.Should().HaveCount(2, "principal cash and amortization are outside this profile");
        _ledger.Journal.Should().BeEmpty();
        var provenance = RecurringJournalEvidenceGuard.Deserialize(draft.RecurringJournalEvidenceJson!)!;
        var evidence = provenance.SourceEvidence.Should().ContainSingle(reference => reference.Kind == "cash-flow-calculation-receipt").Subject;
        var receipt = (await receipts.GetAsync(evidence.ContentHash!))!;
        receipt.ModelVersion.Should().Be(SecurityMasterCashFlowService.ReviewedAccrualContextCalculationVersion);
        receipt.Security.AssetSpecificTerms.GetProperty("principal").GetDecimal().Should().Be(100_000m);
        receipt.Projection.Schedule.Should().Contain(row => row.InterestSegments != null);
        var replay = await flows.ReplayProjectionAsync(receipt.ContentSha256);
        replay!.Schedule.Should().BeEquivalentTo(receipt.Projection.Schedule);

        var unavailable = new Mock<ISecurityMasterCashFlowService>(MockBehavior.Strict);
        var restarted = await CreateFixtureAsync(cashFlows: unavailable.Object, securities: query.Object);
        await restarted.Runner.RunDueAsync(Due(JanuaryEnd).AddHours(1));
        var restored = (await restarted.Drafts.ListAsync(Fund, Book)).Should().ContainSingle().Subject;
        restored.RecurringJournalEvidenceDigest.Should().Be(draft.RecurringJournalEvidenceDigest);
        await ReviewAndPostRetainedCalculationAsync(restarted, restored);
        restarted.Posted.Should().ContainSingle();
        unavailable.VerifyNoOtherCalls();
    }
}
