using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.FundStructure;
using Meridian.Contracts.Integrity;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.FixedIncome;
using NSubstitute;

namespace Meridian.Tests.FixedIncome;

[Trait("Category", "Unit")]
public sealed class BondPortfolioWorkflowRealPricingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedHoldingQuote_DrivesRealSpreadRiskAndEveryScenario_AtActualCurrentFace(bool isShort)
    {
        var account = Guid.NewGuid(); var securityId = Guid.NewGuid(); var batch = Guid.NewGuid();
        var asOf = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var date = new DateOnly(2026, 1, 1); var maturity = new DateOnly(2028, 1, 1);
        var hash = Sha256Digest.ComputeUtf8("Synthetic accepted 0.8 factor holding at 103.125 per 100 current face");
        var scope = new AccountCashForecastScopeDto("synthetic-tenant", "synthetic-company", account.ToString("D"), Guid.NewGuid().ToString("D"), "USD");
        var security = new SecurityDetailDto(securityId, "Bond", SecurityStatusDto.Active, "Synthetic fixed bond", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(new
            {
                assetClassTermsVersion = 2, issueDate = "2026-01-01", maturity = "2028-01-01",
                couponType = "Fixed", couponRate = 5m, dayCount = "30/360", par = 100m,
                paymentFrequency = "Annual", isCallable = false, currentFactor = 0.8m, factorDate = "2026-01-01",
                scheduleConventions = new { accrualAdjusted = false, couponRateUnit = "Percent", couponCashBasis = "Level" }
            }), [], [], 9, asOf, null);
        var securities = Substitute.For<ISecurityMasterQueryService>();
        securities.GetRecordedByIdAsOfAsync(securityId, asOf, Arg.Any<CancellationToken>()).Returns(security);
        var curveId = Guid.NewGuid(); var versionId = Guid.NewGuid();
        var definition = new SecurityCurveDefinitionDto(curveId, "USD-SYNTHETIC-ACCEPTED-MARK", SecurityCurveKind.Discount,
            "USD", null, null, "ACT/365", SecurityCurveInterpolation.Linear, "synthetic", null, "operator", asOf);
        SecurityCurvePointDto[] points = [new(date, 1m), new(date.AddYears(1), 0.95m), new(maturity, 0.9m)];
        var curve = new SecurityCurveVersionDto(versionId, curveId, definition.CurveKey, 1, date, asOf, asOf,
            "synthetic", "operator", points, SecurityCurveRules.ContentSha256(date, points));
        var curves = Substitute.For<ISecurityCurveStore>();
        curves.GetDefinitionAsync(curveId, Arg.Any<CancellationToken>()).Returns(definition);
        curves.GetVersionByIdAsync(curveId, versionId, Arg.Any<CancellationToken>()).Returns(curve);
        var context = new BondCalculationContext(asOf, date, asOf, 9, [new("Discount", curveId, versionId)]);
        var workflow = new RecordedWorkflow(new BondWorkflowService(securities,
            new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]), curves));
        var source = Substitute.For<IAccountBondHoldingsSource>();
        var snapshot = new AccountBondHoldingsSnapshotDto(account, date, batch, hash, "synthetic-reviewed-statement",
            [new(Guid.NewGuid(), batch, account, date, "SYNTHETIC", "CUSIP", 1000m, 825m, "USD", "Synthetic fixed bond", "Bond", isShort,
                SecurityId: securityId, Bond: new(1000m, 800m, 0.8m, 1m, BondHoldingQuoteBasis.PercentOfCurrentFace,
                    103.125m, false, 0m, InvoiceBasis: BondHoldingInvoiceBasis.Nominal))]);
        source.GetHoldingsAsync(account, date, asOf, Arg.Any<CancellationToken>()).Returns(snapshot);
        var reference = Substitute.For<IBondReferenceService>();
        reference.GetReferenceAsOfAsync(securityId, date, Arg.Any<CancellationToken>()).Returns(new BondReferenceDto(securityId,
            "Synthetic fixed bond", "USD", "Synthetic issuer", null, "SYNTHETIC",
            new(securityId, BondLifecycleStat.Active, date, null, maturity, false, 9), null, 9));

        var result = await new BondPortfolioRiskService(source, workflow, reference).CalculateAsync(scope, new(context));

        result.IsComplete.Should().BeTrue();
        var position = result.Positions.Should().ContainSingle().Which;
        var sign = isShort ? -1m : 1m;
        position.SignedOriginalFace.Should().Be(sign * 1000m);
        position.PresentValue.Should().BeApproximately(sign * 825m, 0.000001m);
        position.Cs01.Should().NotBeNull();
        Math.Sign(position.Cs01!.Value).Should().Be((int)sign);
        var widened = position.Scenarios.Single(scenario => scenario.Scenario == "spread-widening");
        widened.PresentValue.Should().NotBeNull();
        (sign * widened.PresentValue!.Value).Should().BeLessThan(sign * position.PresentValue!.Value);
        workflow.Calls.Should().HaveCount(5);
        workflow.Calls.Should().OnlyContain(call => call.OriginalFace == 1000m && call.CleanPricePer100 == 103.125m
            && call.QuoteSettlementDate == snapshot.AsOfDate && call.AccountId == scope.AccountId
            && call.ConfirmationEvidenceReference != null && call.ConfirmationEvidenceReference.Contains(batch.ToString("D"), StringComparison.Ordinal)
            && call.ConfirmationEvidenceReference.Contains(hash, StringComparison.Ordinal));
        workflow.Results.Should().OnlyContain(model => !model.IsBlocked && model.CurrentFace == 800m && model.Cs01 != null
            && model.CalibratedSpread != null && model.Request!.CleanPricePer100 == 103.125m);
        var baselineSpread = workflow.Results[0].CalibratedSpread!.Value;
        foreach (var model in workflow.Results)
        {
            model.CalibratedSpread!.Value.Should().BeApproximately(baselineSpread, 0.0000000001m);
            model.Inputs.Should().Contain(input => input.Kind == "Curve:Discount" && input.VersionId == versionId
                && input.ContentSha256 == curve.ContentSha256);
        }
    }

    private sealed class RecordedWorkflow(IBondWorkflowService inner) : IBondWorkflowService
    {
        public List<BondWorkflowRequest> Calls { get; } = [];
        public List<BondWorkflowResult> Results { get; } = [];
        public async Task<BondWorkflowResult?> CalculateAsync(BondWorkflowRequest request, CancellationToken ct = default)
        {
            Calls.Add(request);
            var result = await inner.CalculateAsync(request, ct);
            if (result is not null) Results.Add(result);
            return result;
        }
        public Task<BondWorkflowResult?> CalculateIncomeScheduleAsync(BondWorkflowRequest request, DateOnly freezeIndexAt,
            CancellationToken ct = default) => inner.CalculateIncomeScheduleAsync(request, freezeIndexAt, ct);
    }
}
