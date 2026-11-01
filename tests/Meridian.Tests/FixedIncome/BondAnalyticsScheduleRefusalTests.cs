using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.Calendars;
using Meridian.Contracts.FixedIncome;
using Meridian.Contracts.SecurityMaster;
using Meridian.Instruments.FixedIncome;
using Moq;

namespace Meridian.Tests.FixedIncome;

/// <summary>Shared schedule validation remains an actionable refusal for both retained analytics versions.</summary>
public sealed class BondAnalyticsScheduleRefusalTests
{
    private static readonly Guid SecurityId = Guid.Parse("c2e43955-bbcf-419c-84fc-de93070e465d");
    private static readonly DateTimeOffset AsOf = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("bond-analytics-v1", "factorScheduleEntries", "SM_SCHEDULE_DUPLICATE_DATE", "[1].asOfDate")]
    [InlineData("bond-analytics-v2", "factorScheduleEntries", "SM_SCHEDULE_DUPLICATE_DATE", "[1].asOfDate")]
    [InlineData("bond-analytics-v1", "principalSchedule", "SM_SCHEDULE_RANGE_INVALID", "[0].amount")]
    [InlineData("bond-analytics-v2", "principalSchedule", "SM_SCHEDULE_RANGE_INVALID", "[0].amount")]
    [InlineData("bond-analytics-v1", "stepSchedule", "SM_SCHEDULE_DUPLICATE_DATE", "[1].effectiveDate")]
    [InlineData("bond-analytics-v2", "stepSchedule", "SM_SCHEDULE_DUPLICATE_DATE", "[1].effectiveDate")]
    public async Task MalformedOwnedSchedule_ReturnsBlockedResultWithOriginalDiagnostics(
        string version, string key, string diagnosticCode, string rowPath)
    {
        var (service, query) = Service(Bond(key));

        var result = await service.CalculateAsync(Request(version));

        AssertBlocked(result!, version, diagnosticCode, $"assetSpecificTerms.profileFields.{key}{rowPath}");
        query.Verify(value => value.GetByIdAsync(SecurityId, It.IsAny<CancellationToken>()), Times.Once);
        query.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("bond-analytics-v1")]
    [InlineData("bond-analytics-v2")]
    public async Task ScheduleRefusalFromCalculationPolicy_ReturnsTheSameBlockedContract(string version)
    {
        var (service, _) = Service(Bond("principalSchedule"), new ValidatingPolicy());

        var result = await service.CalculateAsync(Request(version));

        AssertBlocked(result!, version, "SM_SCHEDULE_RANGE_INVALID",
            "assetSpecificTerms.profileFields.principalSchedule[0].amount");
    }

    [Theory]
    [InlineData("bond-analytics-v1", false)]
    [InlineData("bond-analytics-v2", false)]
    [InlineData("bond-analytics-v1", true)]
    [InlineData("bond-analytics-v2", true)]
    public async Task QueryFailure_IsNotReclassifiedAsAnInvalidSchedule(string version, bool storageFailure)
    {
        Exception failure = storageFailure
            ? new IOException("Synthetic retained-store failure.")
            : new InvalidOperationException("Synthetic query runtime failure.");
        var query = new Mock<ISecurityMasterQueryService>(MockBehavior.Strict);
        query.Setup(value => value.GetByIdAsync(SecurityId, It.IsAny<CancellationToken>())).ThrowsAsync(failure);
        var service = new BondAnalyticsService(query.Object);

        Func<Task> calculate = () => service.CalculateAsync(Request(version));

        (await calculate.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(failure);
        query.Verify(value => value.GetByIdAsync(SecurityId, It.IsAny<CancellationToken>()), Times.Once);
        query.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("bond-analytics-v1")]
    [InlineData("bond-analytics-v2")]
    public async Task ValidParBond_PreservesPricingAndRepeatedReadIdentity(string version)
    {
        var (service, _) = Service(Bond());

        var first = (await service.CalculateAsync(Request(version)))!;
        var second = (await service.CalculateAsync(Request(version)))!;

        first.ModelVersion.Should().Be(version);
        first.Blockers.Should().BeEmpty();
        first.CleanPrice.Should().BeApproximately(100m, 0.00000001m);
        first.DirtyPrice.Should().BeApproximately(100m, 0.00000001m);
        first.AccruedInterest.Should().Be(0m);
        first.YieldToMaturity.Should().Be(0.05m);
        first.CashFlows.Should().HaveCount(4);
        first.CashFlows.Sum(flow => flow.Interest).Should().Be(10m);
        first.CashFlows.Sum(flow => flow.Principal).Should().Be(100m);
        second.CashFlows.Should().Equal(first.CashFlows);
        second.Inputs.Should().Equal(first.Inputs);
        second.YieldToMaturity.Should().Be(first.YieldToMaturity);
    }

    private static void AssertBlocked(BondAnalyticsResult result, string version, string code, string path)
    {
        result.Should().NotBeNull();
        result.SecurityId.Should().Be(SecurityId);
        result.ModelVersion.Should().Be(version);
        result.IsBlocked.Should().BeTrue();
        var blocker = result.Blockers.Should().ContainSingle().Which;
        blocker.Code.Should().Be("InvalidEconomicSchedule");
        blocker.Reason.Should().Contain(code).And.Contain(path);
        new decimal?[]
        {
            result.CleanPrice, result.DirtyPrice, result.AccruedInterest, result.YieldToMaturity,
            result.CurrentYield, result.MacaulayDuration, result.ModifiedDuration, result.Convexity,
            result.Dv01, result.YieldToWorst
        }.Should().OnlyContain(value => value == null, "invalid schedules cannot publish partial analytics");
        result.CashFlows.Should().BeEmpty();
        result.ExerciseYields.Should().BeEmpty();
        result.Capabilities.Single(value => value.Operation == "Analytics").Supported.Should().BeFalse();
        result.Capabilities.Single(value => value.Operation == "Projection").Supported.Should().BeFalse();
    }

    private static (BondAnalyticsService Service, Mock<ISecurityMasterQueryService> Query) Service(
        SecurityDetailDto security, IBondCalculationPolicy? policy = null)
    {
        var query = new Mock<ISecurityMasterQueryService>(MockBehavior.Strict);
        query.Setup(value => value.GetByIdAsync(SecurityId, It.IsAny<CancellationToken>())).ReturnsAsync(security);
        return (new BondAnalyticsService(query.Object,
            new StaticBusinessDayCalendarSource([BusinessDayCalendars.WeekendsOnly]), policy), query);
    }

    private static BondAnalyticsRequest Request(string version) =>
        new(SecurityId, new BondCalculationContext(AsOf), Yield: 0.05m) { CalculationVersion = version };

    private static SecurityDetailDto Bond(string? invalidSchedule = null)
    {
        var terms = new Dictionary<string, object?>
        {
            ["assetClassTermsVersion"] = 2,
            ["issueDate"] = "2026-01-15",
            ["maturity"] = "2028-01-15",
            ["couponType"] = "Fixed",
            ["couponRate"] = 5m,
            ["dayCount"] = "30/360",
            ["par"] = 100m,
            ["paymentFrequency"] = "SemiAnnual",
            ["scheduleConventions"] = new { accrualAdjusted = false, couponRateUnit = "Percent", couponCashBasis = "Level" }
        };
        if (invalidSchedule is not null)
        {
            var rows = invalidSchedule switch
            {
                "factorScheduleEntries" => "[{\"asOfDate\":\"2027-01-15\",\"factor\":0.8},{\"asOfDate\":\"2027-01-15\",\"factor\":0.7}]",
                "principalSchedule" => "[{\"paymentDate\":\"2027-01-15\",\"amount\":0}]",
                "stepSchedule" => "[{\"effectiveDate\":\"2027-01-15\",\"rate\":0.5},{\"effectiveDate\":\"2027-01-15\",\"rate\":0.6}]",
                _ => throw new ArgumentOutOfRangeException(nameof(invalidSchedule))
            };
            terms["profileFields"] = new Dictionary<string, object?>
            {
                [invalidSchedule] = JsonSerializer.Deserialize<JsonElement>(rows)
            };
            terms[invalidSchedule] = Array.Empty<object>();
        }
        return new(SecurityId, "Bond", SecurityStatusDto.Active, "Synthetic analytics bond", "USD",
            JsonSerializer.SerializeToElement(new { }), JsonSerializer.SerializeToElement(terms), [], [], 7, AsOf, null);
    }

    private sealed class ValidatingPolicy : IBondCalculationPolicy
    {
        public Task<string?> GetBlockerAsync(SecurityDetailDto security, CancellationToken ct = default)
        {
            SecurityEconomicScheduleParser.Parse(security.AssetSpecificTerms, security.CommonTerms).ThrowIfInvalid();
            return Task.FromResult<string?>(null);
        }
    }
}
