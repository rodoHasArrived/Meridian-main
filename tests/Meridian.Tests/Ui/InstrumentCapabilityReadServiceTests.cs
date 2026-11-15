using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Ui.Shared.Services;
using NSubstitute;

namespace Meridian.Tests.Ui;

public sealed class InstrumentCapabilityReadServiceTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);
    private static readonly Guid SecurityId = Guid.Parse("74c3fb0a-b6e3-465e-abbb-5ec2ecfdaef5");

    [Theory]
    [InlineData(StructuredCashFlowSourceKind.CalculatedBullet)]
    [InlineData(StructuredCashFlowSourceKind.CalculatedSinker)]
    public async Task SelectedTerms_PassiveCashFlowCheckRetainsVersionAndNeverCapturesReceipts(StructuredCashFlowSourceKind sourceKind)
    {
        var query = Substitute.For<ISecurityMasterQueryService>();
        var flows = Substitute.For<ISecurityMasterCashFlowService>();
        var selected = Security();
        flows.GetCashFlowSourceAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(Source() with { SourceKind = sourceKind });
        flows.GetProjectionBlockerAsync(selected, Arg.Any<BondCalculationContext>(), Arg.Any<CancellationToken>())
            .Returns((SecurityCashFlowBlockerDto?)null);
        var service = new InstrumentCapabilityReadService(query, flows);

        var result = await service.AssessSelectedAsync(selected, AsOf);

        result.Should().HaveCount(6).And.OnlyContain(item => item.SecurityVersion == 42 && item.AsOfUtc == AsOf);
        result.Single(item => item.Capability == "cash-flows").Status.Should().Be("Ready");
        result.Single(item => item.Capability == "accrual").Status.Should().Be("Review");
        await flows.Received(1).GetProjectionBlockerAsync(selected,
            Arg.Is<BondCalculationContext>(context => context.SecurityVersion == 42 && context.AsOf == AsOf), Arg.Any<CancellationToken>());
        await flows.DidNotReceiveWithAnyArgs().CaptureProjectionAsync(default, default, default!, default);
        await flows.DidNotReceiveWithAnyArgs().GetProjectionAsOfAsync(default, default, default, default);
        await query.DidNotReceiveWithAnyArgs().GetByIdAsync(default, default);
    }

    [Fact]
    public async Task UndeclaredRate_IsBlockedWithGovernedRepairLink()
    {
        var flows = Substitute.For<ISecurityMasterCashFlowService>();
        flows.GetCashFlowSourceAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(Source());
        flows.GetProjectionBlockerAsync(Arg.Any<SecurityDetailDto>(), Arg.Any<BondCalculationContext>(), Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowBlockerDto(RateUnitNormalizer.UndeclaredCode, "Coupon rate unit is undeclared."));

        var result = await new InstrumentCapabilityReadService(Substitute.For<ISecurityMasterQueryService>(), flows)
            .AssessSelectedAsync(Security(), AsOf);

        foreach (var row in result.Where(item => item.Capability is "cash-flows" or "accrual"))
        {
            row.Status.Should().Be("Blocked");
            row.Reasons.Should().ContainSingle().Which.RemediationPath.Should().Contain("rateRepair=queue");
        }
    }

    [Fact]
    public async Task UnsupportedClass_UsesTheSameRefusalAsRecurringExecution()
    {
        var result = await new InstrumentCapabilityReadService(Substitute.For<ISecurityMasterQueryService>())
            .AssessSelectedAsync(Security("Equity"), AsOf);

        var accrual = result.Single(item => item.Capability == "accrual");
        accrual.Status.Should().Be("Blocked");
        accrual.Reasons.Should().ContainSingle().Which.Code.Should().Be("RecurringAccrualClassUnsupported");
        result.Single(item => item.Capability == "cash-flows").Applicability.Should().Be("Applicable");
    }

    [Fact]
    public async Task MissingServiceAndOperationalContext_NeverProduceReady()
    {
        var result = await new InstrumentCapabilityReadService(Substitute.For<ISecurityMasterQueryService>())
            .AssessSelectedAsync(Security(), AsOf);

        result.Where(item => item.Capability != "terms").Should().NotContain(item => item.Status == "Ready");
        result.Single(item => item.Capability == "cash-flows").EvaluationState.Should().Be("Unavailable");
    }

    [Fact]
    public async Task MissingSource_IsAnExplicitBlockerEvenWhenThePassiveApiWouldReturnNull()
    {
        var flows = Substitute.For<ISecurityMasterCashFlowService>();
        flows.GetCashFlowSourceAsync(SecurityId, Arg.Any<CancellationToken>()).Returns((SecurityCashFlowSourceDto?)null);

        var result = await new InstrumentCapabilityReadService(Substitute.For<ISecurityMasterQueryService>(), flows)
            .AssessSelectedAsync(Security(), AsOf);

        var row = result.Single(item => item.Capability == "cash-flows");
        row.Status.Should().Be("Blocked");
        row.Reasons.Should().ContainSingle().Which.Code.Should().Be("CashFlowSourceMissing");
        await flows.DidNotReceiveWithAnyArgs().GetProjectionBlockerAsync(default!, default!, default);
    }

    [Theory]
    [InlineData(StructuredCashFlowSourceKind.ClientProvided, false)]
    [InlineData(StructuredCashFlowSourceKind.ClientProvided, true)]
    [InlineData(StructuredCashFlowSourceKind.MIAC, false)]
    [InlineData(StructuredCashFlowSourceKind.MoodysAnalytics, false)]
    public async Task ExternalSchedule_RequiresItsOwnEvidence_AndRetainsTheRecurringSourceRefusal(
        StructuredCashFlowSourceKind kind, bool maintained)
    {
        var flows = Substitute.For<ISecurityMasterCashFlowService>();
        flows.GetCashFlowSourceAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowSourceDto(SecurityId, kind, AsOf, maintained, "source-operator", null));
        // A calculated-terms refusal has no authority over an externally supplied schedule.
        flows.GetProjectionBlockerAsync(Arg.Any<SecurityDetailDto>(), Arg.Any<BondCalculationContext>(), Arg.Any<CancellationToken>())
            .Returns(new SecurityCashFlowBlockerDto("SyntheticCalculatedTermsRefusal", "This calculation must not run."));

        var result = await new InstrumentCapabilityReadService(Substitute.For<ISecurityMasterQueryService>(), flows)
            .AssessSelectedAsync(Security(), AsOf);

        var cashFlow = result.Single(item => item.Capability == "cash-flows");
        cashFlow.Status.Should().Be("Review");
        cashFlow.Operation.Should().Be("ExternalCashFlowScheduleReview");
        cashFlow.EvaluationState.Should().Be("RequiresContext");
        cashFlow.Reasons.Should().ContainSingle().Which.Code.Should().Be("ExternalScheduleContextRequired");
        var accrual = result.Single(item => item.Capability == "accrual");
        accrual.Status.Should().Be("Blocked", "the recurring execution owner accepts calculated schedules only");
        accrual.EvaluationState.Should().Be("Complete");
        accrual.Reasons.Should().ContainSingle().Which.Code.Should().Be("AccrualCalculatedSourceRequired");
        await flows.DidNotReceiveWithAnyArgs().GetProjectionBlockerAsync(default!, default!, default);
        await flows.DidNotReceiveWithAnyArgs().CaptureProjectionAsync(default, default, default!, default);
        await flows.DidNotReceiveWithAnyArgs().GetProjectionAsOfAsync(default, default, default, default);
    }

    [Fact]
    public async Task UnknownSourceKind_RemainsUnevaluatedWithoutInventingACalculatedRefusal()
    {
        var flows = Substitute.For<ISecurityMasterCashFlowService>();
        flows.GetCashFlowSourceAsync(SecurityId, Arg.Any<CancellationToken>())
            .Returns(Source() with { SourceKind = StructuredCashFlowSourceKind.Unknown });

        var result = await new InstrumentCapabilityReadService(Substitute.For<ISecurityMasterQueryService>(), flows)
            .AssessSelectedAsync(Security(), AsOf);

        foreach (var row in result.Where(item => item.Capability is "cash-flows" or "accrual"))
        {
            row.Status.Should().Be("Review");
            row.EvaluationState.Should().Be("Unavailable");
            row.Reasons.Should().ContainSingle().Which.Code.Should().Be("CashFlowSourceEvaluationUnavailable");
        }
        await flows.DidNotReceiveWithAnyArgs().GetProjectionBlockerAsync(default!, default!, default);
    }

    [Fact]
    public async Task BatchMissingAndFailedIdentity_StayVisibleAsReview()
    {
        var query = Substitute.For<ISecurityMasterQueryService>();
        var missingId = Guid.NewGuid();
        query.GetByIdAsync(missingId, Arg.Any<CancellationToken>()).Returns((SecurityDetailDto?)null);
        query.GetByIdAsync(SecurityId, Arg.Any<CancellationToken>()).Returns(Task.FromException<SecurityDetailDto?>(new IOException("Synthetic outage")));

        var response = await new InstrumentCapabilityReadService(query).GetBatchAsync([missingId, SecurityId, missingId]);

        response.Securities.Should().HaveCount(2);
        response.Securities.SelectMany(item => item.Capabilities).Should().HaveCount(12)
            .And.OnlyContain(item => item.Status == "Review" && item.EvaluationState == "Unavailable" && item.SecurityVersion == null);
        response.Securities.Should().OnlyContain(item => item.SecurityVersion == null);
    }

    [Fact]
    public async Task BatchRejectsUnboundedOrEmptyRequests()
    {
        var service = new InstrumentCapabilityReadService(Substitute.For<ISecurityMasterQueryService>());
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetBatchAsync([]));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetBatchAsync([Guid.Empty]));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetBatchAsync(Enumerable.Range(0, 201).Select(_ => Guid.NewGuid()).ToArray()));
    }

    private static SecurityDetailDto Security(string assetClass = "Bond")
        => new(SecurityId, assetClass, SecurityStatusDto.Active, "Synthetic security", "USD",
            JsonSerializer.SerializeToElement(new { }),
            SecurityTermsEnvelope.StampDeclaredFormat(JsonSerializer.SerializeToElement(new { maturity = "2030-01-01", couponRate = 5m })), [], [], 42, AsOf.AddYears(-1), null);

    private static SecurityCashFlowSourceDto Source()
        => new(SecurityId, StructuredCashFlowSourceKind.CalculatedBullet, AsOf, false, null, null);
}
