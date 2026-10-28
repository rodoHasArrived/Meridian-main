using System.Text.Json;
using FluentAssertions;
using Meridian.Contracts.AssetOperations;
using Meridian.Instruments.AssetOperations;

namespace Meridian.Tests.AssetOperations;

/// <summary>
/// Characterization pin for the cash ladder's early-call scenario, recorded against base 7d072638
/// before declared call and put schedules (ADR-025, increment 8) replace the scalar date probe.
/// </summary>
/// <remarks>
/// Tests tagged <c>Category=KnownDefect</c> assert TODAY'S defective behavior from the registry in
/// <c>tests/fixtures/schedules/KNOWN-DEFECTS.md</c> and name the TARGET behavior in a comment. When a
/// fix lands the pin fails; the fixing PR flips the assertion and updates the registry in the same
/// change.
/// </remarks>
public sealed class PortfolioCashLadderScheduleCharacterizationTests
{
    private static readonly DateOnly AsOf = new(2026, 7, 5);

    [Fact]
    [Trait("Category", "KnownDefect")]
    public void EarlyCall_MandatoryPutWithoutCallDate_CurrentBehavior_RedeemedAsIfCalled_SCH_DEF_009()
    {
        // SCH-DEF-009. TARGET: separate issuer-call and holder-put scenarios driven by declared call
        // and put schedules (increment 8); a put date is not an issuer call.
        var putDate = AsOf.AddDays(30);
        var position = BuildBondPosition(
            [
                ("Coupon", AsOf.AddDays(20), 2500m),
                ("Maturity", AsOf.AddDays(60), 100_000m),
                ("Coupon", AsOf.AddDays(80), 2500m)
            ],
            new { mandatoryPutDate = putDate.ToString("yyyy-MM-dd") });

        var ladder = PortfolioCashLadderEngine.Build(
            new PortfolioCashLadderInputs(
                AsOf,
                90,
                BaseCurrency: "USD",
                [position],
                [],
                [],
                0m,
                BucketDays: 7),
            PortfolioCashLadderEngine.EarlyCallScenarioId);

        ladder.Contributions.Should().HaveCount(2,
            "CURRENT defective behavior: the early-call probe reads mandatoryPutDate as a call date and drops later rows");
        ladder.Contributions.Should().ContainSingle(static row => row.FlowType == "Coupon")
            .Which.DueDate.Should().Be(AsOf.AddDays(20));
        var redemption = ladder.Contributions.Should().ContainSingle(static row => row.FlowType == "CallRedemption").Subject;
        redemption.DueDate.Should().Be(putDate);
        redemption.Amount.Should().Be(100_000m, "the outstanding principal is redeemed at par on the put date");
        ladder.Contributions.Should().NotContain(static row => row.DueDate > AsOf.AddDays(30));
    }

    private static PortfolioCashLadderPositionDto BuildBondPosition(
        IReadOnlyList<(string FlowType, DateOnly DueDate, decimal Amount)> flows,
        object termsPayload)
    {
        var securityId = Guid.Parse("5c4ed009-0000-4000-8000-000000000009");
        var projectionRunId = Guid.Parse("5c4ed009-0000-4000-8000-0000000000aa");
        var generatedAt = DateTimeOffset.Parse("2026-07-05T00:00:00Z");
        var subject = new AssetOperationSubjectDto(
            securityId,
            "Bond",
            "Puttable Bond",
            $"CUSIP:{securityId:N}",
            ["Identity", "TermsHistory", "ProjectedCashFlows", "LedgerProjection"]);
        var terms = new AssetTermsVersionDto(
            Guid.Parse("5c4ed009-0000-4000-8000-0000000000bb"),
            securityId,
            1,
            $"security-master:{securityId:N}",
            AsOf.AddYears(-1),
            generatedAt.AddDays(-1),
            "SecurityMaster",
            securityId.ToString("D"),
            "Puttable Bond retained terms",
            JsonSerializer.SerializeToElement(termsPayload));
        var run = new AssetCashFlowProjectionRunDto(
            projectionRunId,
            securityId,
            AsOf,
            "asset-obligation-projection-v1",
            "Completed",
            generatedAt,
            "SecurityMaster",
            securityId.ToString("D"));
        var projectedFlows = flows.Select((flow, index) => new AssetProjectedCashFlowDto(
            Guid.NewGuid(),
            projectionRunId,
            securityId,
            index + 1,
            flow.FlowType,
            flow.DueDate,
            flow.Amount,
            "USD",
            "Projected",
            SourceDomain: "SecurityMaster",
            SourceEntityId: securityId.ToString("D"))).ToArray();
        var readiness = new AssetOperationsReadinessDto(
            securityId,
            "Ready",
            subject.OperationalProfile,
            subject.OperationalProfile,
            [],
            [],
            generatedAt,
            "SecurityMaster",
            securityId.ToString("D"));

        var detail = new AssetOperationsDetailDto(
            subject,
            [terms],
            [],
            [run],
            projectedFlows,
            [],
            [],
            [],
            [],
            readiness,
            []);
        return new PortfolioCashLadderPositionDto(detail, 1m);
    }
}
