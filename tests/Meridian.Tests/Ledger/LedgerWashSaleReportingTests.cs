using System.Globalization;
using FluentAssertions;
using Meridian.Ledger;
using Meridian.Storage.Ledger;
using Xunit;

namespace Meridian.Tests.Ledger;

[Trait("Category", "Unit")]
public sealed class LedgerWashSaleReportingTests
{
    private static LedgerAccount Account => LedgerAccounts.Securities("AAPL", "broker-1");

    [Fact]
    public void ReportPack_AttributesDeferralToLossLotWithoutChangingGainLotOrTaxCharacter()
    {
        // The long-term gain exceeds the short-term loss. Only the short-term lot's loss is
        // deferred; quantity-based spreading would incorrectly move half onto the gain row.
        var projection = LedgerTaxLotReliefProjector.Project(new LedgerTaxLotReliefInput(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 20m,
            salePrice: 100m,
            LedgerTaxLotReliefMethod.Fifo,
            [
                new LedgerTaxLot("gain", new DateOnly(2024, 1, 1), 10m, 60m),
                new LedgerTaxLot("loss", new DateOnly(2026, 1, 1), 10m, 120m),
            ],
            washSalePolicy: WashSalePolicy.UnitedStates,
            replacementAcquisitions:
            [new WashSaleReplacementAcquisition("replacement", new DateOnly(2026, 3, 5), 10m)]));

        var rows = BuildTaxRows(projection);

        rows["gain"]["DisallowedWashSaleLoss"].Should().Be("0");
        rows["gain"]["RecognizedGainOrLoss"].Should().Be("400");
        rows["gain"]["LongTermRecognizedGainOrLoss"].Should().Be("400");
        rows["loss"]["DisallowedWashSaleLoss"].Should().Be("200");
        rows["loss"]["RecognizedGainOrLoss"].Should().Be("0");
        rows["loss"]["ShortTermRecognizedGainOrLoss"].Should().Be("0");
        rows.Values.Sum(row => decimal.Parse(row["RecognizedGainOrLoss"], CultureInfo.InvariantCulture))
            .Should().Be(projection.RecognizedGainOrLoss);
    }

    [Fact]
    public void ReportPack_LegacyDeferralsKeepRetainedProportionalAttribution()
    {
        var projection = LedgerTaxLotReliefHistoryProjector.Project(new LedgerTaxLotDisposalHistory(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Account,
            new DateOnly(2026, 3, 1),
            LedgerTaxLotReliefMethod.Fifo,
            [
                new LedgerTaxLotDisposalHistoryLot(
                    "loss-a", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), 1m, 120m, 120m),
                new LedgerTaxLotDisposalHistoryLot(
                    "loss-b", new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 2), 2m, 120m, 240m),
            ],
            RecognizedGainOrLoss: -30m,
            WashSaleBasisIncreases:
            [new WashSaleBasisIncrease("replacement", 30m, new DateOnly(2026, 1, 1))],
            MatchedReplacementQuantity: 1.5m));

        projection.Should().NotBeNull();
        var rows = BuildTaxRows(projection!);

        rows["loss-a"]["DisallowedWashSaleLoss"].Should().Be("10");
        rows["loss-b"]["DisallowedWashSaleLoss"].Should().Be("20");
        rows.Values.Sum(row => decimal.Parse(row["RecognizedGainOrLoss"], CultureInfo.InvariantCulture))
            .Should().Be(-30m);
    }

    [Fact]
    public void HistoryProjector_MixedGainRetainsAllowedGrossLoss()
    {
        var projection = LedgerTaxLotReliefHistoryProjector.Project(new LedgerTaxLotDisposalHistory(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Account,
            new DateOnly(2026, 3, 1),
            LedgerTaxLotReliefMethod.Fifo,
            [
                new LedgerTaxLotDisposalHistoryLot(
                    "gain", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 1), 10m, 60m, 600m),
                new LedgerTaxLotDisposalHistoryLot(
                    "loss", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), 10m, 120m, 1_200m),
            ],
            RecognizedGainOrLoss: 300m,
            WashSaleBasisIncreases:
            [new WashSaleBasisIncrease("replacement", 100m, new DateOnly(2026, 1, 1))],
            MatchedReplacementQuantity: 5m));

        projection.Should().NotBeNull();
        projection!.RealizedGainOrLoss.Should().Be(200m);
        projection.RecognizedGainOrLoss.Should().Be(300m);
        projection.WashSale!.DisallowedLoss.Should().Be(100m);
        projection.WashSale.AllowedLoss.Should().Be(100m);
    }

    [Fact]
    public void ConfiguredPolicy_CarriesExistingGoverningRevisionIntoProjection()
    {
        var configuredPolicy = new LedgerAccountTaxLotPolicyRecord(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Account,
            LedgerTaxLotReliefMethod.Fifo,
            "account-tax-policy-revision-7",
            new DateOnly(2026, 1, 1),
            DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture),
            WashSalePolicy: WashSalePolicy.UnitedStates with
            {
                Scope = WashSaleReplacementScope.DisposingAccount,
                EffectiveDate = new DateOnly(2026, 1, 1),
            });
        var projection = LedgerTaxLotReliefProjector.Project(new LedgerTaxLotReliefInput(
            Account,
            new DateOnly(2026, 3, 1),
            quantitySold: 10m,
            salePrice: 100m,
            configuredPolicy.ReliefMethod,
            [new LedgerTaxLot("loss", new DateOnly(2026, 1, 1), 10m, 120m)],
            washSalePolicy: configuredPolicy.EffectiveWashSalePolicy,
            replacementAcquisitions:
            [new WashSaleReplacementAcquisition("replacement", new DateOnly(2026, 3, 5), 10m)]));

        var appliedPolicy = projection.WashSale!.BasisIncreases.Single().AppliedPolicy;
        appliedPolicy.Should().Be(configuredPolicy.EffectiveWashSalePolicy);
        appliedPolicy!.PolicyId.Should().Be("account-tax-policy-revision-7");
        configuredPolicy.WashSalePolicy!.PolicyId.Should().BeNull();
    }

    private static Dictionary<string, Dictionary<string, string>> BuildTaxRows(LedgerTaxLotReliefProjection projection)
    {
        var pack = LedgerReportPackBuilder.Build(
            LedgerReportPackTestData.BuildLedger(),
            LedgerReportPackTestData.BuildRequest(),
            taxLotReliefProjections: [projection]);
        var lines = pack.Artifacts.Single(artifact => artifact.Name == "tax-lot-realized-gains.csv")
            .Content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var headings = lines[0].Split(',');
        return lines.Skip(1)
            .Select(line => headings.Zip(line.Split(','))
                .ToDictionary(static pair => pair.First, static pair => pair.Second))
            .ToDictionary(static row => row["LotId"]);
    }
}
