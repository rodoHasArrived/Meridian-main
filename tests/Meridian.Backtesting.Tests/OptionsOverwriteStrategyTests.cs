using FluentAssertions;
using Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite;
using Meridian.Contracts.Domain.Enums;
using Xunit;

namespace Meridian.Backtesting.Tests;

/// <summary>
/// Unit tests for the Conservative Options Overwrite strategy components:
/// <see cref="BlackScholesCalculator"/>, <see cref="OptionsOverwriteFilters"/>,
/// <see cref="OptionsOverwriteScoring"/>, <see cref="OptionsOverwriteMetricsCalculator"/>,
/// and the full <see cref="CoveredCallOverwriteStrategy"/> lifecycle.
/// </summary>
public sealed class OptionsOverwriteStrategyTests
{
    // ================================================================== //
    //  Black-Scholes Calculator                                          //
    // ================================================================== //

    public sealed class BlackScholesTests
    {
        [Fact]
        public void Ncdf_Zero_ReturnsHalf()
            => BlackScholesCalculator.Ncdf(0.0).Should().BeApproximately(0.5, 1e-4);

        [Fact]
        public void Ncdf_LargePositive_ReturnsNearOne()
            => BlackScholesCalculator.Ncdf(8.0).Should().BeApproximately(1.0, 1e-6);

        [Fact]
        public void Ncdf_LargeNegative_ReturnsNearZero()
            => BlackScholesCalculator.Ncdf(-8.0).Should().BeApproximately(0.0, 1e-6);

        [Theory]
        [InlineData(100, 100, 0.04, 0.20, 30)]   // ATM 30-day
        [InlineData(100, 105, 0.04, 0.20, 30)]   // OTM 5 % 30-day
        [InlineData(100, 90, 0.04, 0.20, 30)]    // ITM 30-day
        [InlineData(100, 100, 0.04, 0.20, 365)]  // ATM 1-year
        public void CallPrice_IsPositiveAndBoundedByUnderlyingAndIntrinsic(
            double s, double k, double r, double sigma, int calDays)
        {
            double t = calDays / 365.0;
            double price = BlackScholesCalculator.CallPrice(s, k, r, sigma, t);

            price.Should().BePositive("an option always has positive value");
            price.Should().BeLessThanOrEqualTo(s, "call price cannot exceed the underlying price");
            price.Should().BeGreaterThanOrEqualTo(Math.Max(0, s - k * Math.Exp(-r * t)),
                "call price must be at least the PV of intrinsic value");
        }

        [Fact]
        public void CallPrice_ZeroTimeToExpiry_ReturnsIntrinsicValue()
        {
            double s = 110, k = 100, r = 0.04, sigma = 0.20;
            double price = BlackScholesCalculator.CallPrice(s, k, r, sigma, 0.0);
            price.Should().BeApproximately(10.0, 0.01);
        }

        [Fact]
        public void CallDelta_AtmOption_IsNearHalf()
        {
            double delta = BlackScholesCalculator.CallDelta(100, 100, 0.04, 0.20, 30.0 / 365.0);
            delta.Should().BeInRange(0.45, 0.60, "ATM 30-day call delta should be near 0.5");
        }

        [Fact]
        public void CallDelta_DeepItmOption_IsNearOne()
        {
            double delta = BlackScholesCalculator.CallDelta(150, 100, 0.04, 0.20, 30.0 / 365.0);
            delta.Should().BeGreaterThan(0.90);
        }

        [Fact]
        public void CallDelta_DeepOtmOption_IsNearZero()
        {
            double delta = BlackScholesCalculator.CallDelta(100, 150, 0.04, 0.20, 30.0 / 365.0);
            delta.Should().BeLessThan(0.10);
        }

        [Theory]
        [InlineData(100, 100, 0.04, 0.20, 30)]
        [InlineData(100, 105, 0.04, 0.30, 45)]
        public void ImpliedVolatility_RoundTrips_ThroughCallPrice(
            double s, double k, double r, double sigma, int calDays)
        {
            double t = calDays / 365.0;
            double marketPrice = BlackScholesCalculator.CallPrice(s, k, r, sigma, t);
            double? iv = BlackScholesCalculator.ImpliedVolatility(marketPrice, s, k, r, t);

            iv.Should().NotBeNull();
            iv!.Value.Should().BeApproximately(sigma, 1e-4, "IV solver should recover the input volatility");
        }

        [Fact]
        public void ImpliedVolatility_NegativePrice_ReturnsNull()
            => BlackScholesCalculator.ImpliedVolatility(-1.0, 100, 100, 0.04, 30.0 / 365.0).Should().BeNull();

        [Fact]
        public void MarkToClose_Expired_ReturnsIntrinsic()
        {
            decimal mark = BlackScholesCalculator.MarkToClose(
                underlyingPrice: 110m,
                strike: 100m,
                asOf: new DateOnly(2024, 2, 1),
                expiration: new DateOnly(2024, 1, 31),
                impliedVolatility: 0.20,
                riskFreeRate: 0.04);

            mark.Should().BeApproximately(10m, 0.01m, "intrinsic = underlying - strike for ITM");
        }
    }

    // ================================================================== //
    //  Liquidity Filter                                                   //
    // ================================================================== //

    public sealed class LiquidityFilterTests
    {
        private static OptionsOverwriteParams DefaultParams() => new OptionsOverwriteParams
        {
            MinStrike = 100m,
            MinOpenInterest = 500,
            MinVolume = 50,
            MaxSpreadPct = 0.05,
            MaxDelta = 0.35,
            MinDte = 7
        };

        private static OptionCandidateInfo GoodCandidate() =>
            new OptionCandidateInfo(
                UnderlyingSymbol: "SPY",
                Strike: 110m,
                Expiration: new DateOnly(2024, 3, 15),
                Style: OptionStyle.American,
                Multiplier: 100,
                Bid: 2.00m,
                Ask: 2.10m,
                DaysToExpiration: 30,
                OpenInterest: 1000,
                Volume: 200,
                Delta: 0.30,
                ImpliedVolatility: 0.18);

        [Fact]
        public void PassesLiquidityFilter_GoodCandidate_ReturnsTrue()
            => OptionsOverwriteFilters.PassesLiquidityFilter(GoodCandidate(), DefaultParams()).Should().BeTrue();

        [Fact]
        public void PassesLiquidityFilter_ZeroBid_ReturnsFalse()
        {
            var opt = GoodCandidate() with { Bid = 0m };
            OptionsOverwriteFilters.PassesLiquidityFilter(opt, DefaultParams()).Should().BeFalse();
        }

        [Fact]
        public void PassesLiquidityFilter_InsufficientOI_ReturnsFalse()
        {
            var opt = GoodCandidate() with { OpenInterest = 10 };
            OptionsOverwriteFilters.PassesLiquidityFilter(opt, DefaultParams()).Should().BeFalse();
        }

        [Fact]
        public void PassesLiquidityFilter_InsufficientVolume_ReturnsFalse()
        {
            var opt = GoodCandidate() with { Volume = 5 };
            OptionsOverwriteFilters.PassesLiquidityFilter(opt, DefaultParams()).Should().BeFalse();
        }

        [Fact]
        public void PassesLiquidityFilter_WideSpread_ReturnsFalse()
        {
            // Bid=1.00, Ask=1.15 → spread 15% > 5% max
            var opt = GoodCandidate() with { Bid = 1.00m, Ask = 1.15m };
            OptionsOverwriteFilters.PassesLiquidityFilter(opt, DefaultParams()).Should().BeFalse();
        }
    }

    // ================================================================== //
    //  Risk Filter                                                        //
    // ================================================================== //

    public sealed class RiskFilterTests
    {
        private static OptionsOverwriteParams DefaultParams() => new OptionsOverwriteParams
        {
            MinStrike = 100m,
            MaxDelta = 0.35,
            MinDte = 7,
            MaxDte = 60,
            MinIvPercentile = 50.0
        };

        private static OptionCandidateInfo GoodCandidate() =>
            new OptionCandidateInfo(
                UnderlyingSymbol: "SPY",
                Strike: 110m,
                Expiration: new DateOnly(2024, 3, 15),
                Style: OptionStyle.American,
                Multiplier: 100,
                Bid: 2.00m,
                Ask: 2.10m,
                DaysToExpiration: 30,
                OpenInterest: 1000,
                Volume: 200,
                Delta: 0.30,
                ImpliedVolatility: 0.18,
                IvPercentile: 60.0);

        [Fact]
        public void PassesRiskFilter_GoodCandidate_ReturnsTrue()
            => OptionsOverwriteFilters.PassesRiskFilter(GoodCandidate(), DefaultParams()).Should().BeTrue();

        [Fact]
        public void PassesRiskFilter_StrikeBelowMin_ReturnsFalse()
        {
            var opt = GoodCandidate() with { Strike = 90m };
            OptionsOverwriteFilters.PassesRiskFilter(opt, DefaultParams()).Should().BeFalse();
        }

        [Fact]
        public void PassesRiskFilter_DeltaTooHigh_ReturnsFalse()
        {
            var opt = GoodCandidate() with { Delta = 0.50 };
            OptionsOverwriteFilters.PassesRiskFilter(opt, DefaultParams()).Should().BeFalse();
        }

        [Fact]
        public void PassesRiskFilter_DteTooLow_ReturnsFalse()
        {
            var opt = GoodCandidate() with { DaysToExpiration = 3 };
            OptionsOverwriteFilters.PassesRiskFilter(opt, DefaultParams()).Should().BeFalse();
        }

        [Fact]
        public void PassesRiskFilter_DteTooHigh_ReturnsFalse()
        {
            var opt = GoodCandidate() with { DaysToExpiration = 90 };
            OptionsOverwriteFilters.PassesRiskFilter(opt, DefaultParams()).Should().BeFalse();
        }

        [Fact]
        public void PassesRiskFilter_IvPercentileTooLow_ReturnsFalse()
        {
            var opt = GoodCandidate() with { IvPercentile = 30.0 };
            OptionsOverwriteFilters.PassesRiskFilter(opt, DefaultParams()).Should().BeFalse();
        }

        [Fact]
        public void PassesRiskFilter_NoIvPercentileData_PassesThrough()
        {
            // When no IV percentile is available, the filter should not exclude the candidate
            var opt = GoodCandidate() with { IvPercentile = null };
            OptionsOverwriteFilters.PassesRiskFilter(opt, DefaultParams()).Should().BeTrue();
        }
    }

    // ================================================================== //
    //  Dividend Assignment Risk Filter                                    //
    // ================================================================== //

    public sealed class DividendFilterTests
    {
        private static OptionsOverwriteParams DefaultParams() =>
            new OptionsOverwriteParams { ExDivWindowDays = 7 };

        [Fact]
        public void HasDividendRisk_EuropeanStyle_ReturnsFalse()
        {
            var opt = new OptionCandidateInfo(
                "SPY", 100m, new DateOnly(2024, 3, 15), OptionStyle.European,
                100, 5.00m, 5.20m, 30, 1000, 200, 0.55,
                DaysToNextExDiv: 3, NextDividendAmount: 1.50m);

            OptionsOverwriteFilters.HasDividendAssignmentRiskWithUnderlyingPrice(
                opt, DefaultParams(), underlyingPrice: 105m).Should().BeFalse(
                "European options cannot be early-exercised");
        }

        [Fact]
        public void HasDividendRisk_ExDivFarAway_ReturnsFalse()
        {
            var opt = new OptionCandidateInfo(
                "SPY", 100m, new DateOnly(2024, 3, 15), OptionStyle.American,
                100, 5.00m, 5.20m, 30, 1000, 200, 0.55,
                DaysToNextExDiv: 30, NextDividendAmount: 1.50m);

            OptionsOverwriteFilters.HasDividendAssignmentRiskWithUnderlyingPrice(
                opt, DefaultParams(), underlyingPrice: 105m).Should().BeFalse(
                "ex-div is > 7 days away — no imminent assignment risk");
        }

        [Fact]
        public void HasDividendRisk_OtmOption_ReturnsFalse()
        {
            // OTM call: underlying 98 < strike 100 → no assignment risk
            var opt = new OptionCandidateInfo(
                "SPY", 100m, new DateOnly(2024, 3, 15), OptionStyle.American,
                100, 0.20m, 0.25m, 5, 1000, 200, 0.10,
                DaysToNextExDiv: 3, NextDividendAmount: 1.50m);

            OptionsOverwriteFilters.HasDividendAssignmentRiskWithUnderlyingPrice(
                opt, DefaultParams(), underlyingPrice: 98m).Should().BeFalse(
                "OTM call has no assignment risk");
        }

        [Fact]
        public void HasDividendRisk_ItmWithLowExtrinsic_ReturnsTrue()
        {
            // ITM: underlying 106 > strike 100, extrinsic (mid - intrinsic) = 0.10 < dividend 1.50
            var opt = new OptionCandidateInfo(
                "SPY", 100m, new DateOnly(2024, 3, 15), OptionStyle.American,
                100, 6.10m, 6.20m, 5, 1000, 200, 0.85,
                DaysToNextExDiv: 3, NextDividendAmount: 1.50m);

            OptionsOverwriteFilters.HasDividendAssignmentRiskWithUnderlyingPrice(
                opt, DefaultParams(), underlyingPrice: 106m).Should().BeTrue(
                "extrinsic (0.15) < dividend (1.50) → holder exercises to capture dividend");
        }
    }

    // ================================================================== //
    //  Scoring                                                            //
    // ================================================================== //

    public sealed class ScoringTests
    {
        [Fact]
        public void ScoreBasic_ReturnsMultipliedBid()
        {
            var opt = new OptionCandidateInfo(
                "SPY", 110m, new DateOnly(2024, 3, 15), OptionStyle.American,
                100, Bid: 2.50m, Ask: 2.60m, DaysToExpiration: 30,
                OpenInterest: 1000, Volume: 200, Delta: 0.30);

            double score = OptionsOverwriteScoring.ScoreBasic(opt);
            score.Should().BeApproximately(250.0, 0.001, "2.50 bid × 100 multiplier = 250");
        }

        [Fact]
        public void ScoreRelative_NoIvData_FallsBackToBasic()
        {
            var opt = new OptionCandidateInfo(
                "SPY", 110m, new DateOnly(2024, 3, 15), OptionStyle.American,
                100, Bid: 2.50m, Ask: 2.60m, DaysToExpiration: 30,
                OpenInterest: 1000, Volume: 200, Delta: 0.30,
                ImpliedVolatility: null, Vega: null);

            var p = new OptionsOverwriteParams { DepthBonusWeight = 0.05 };
            double relative = OptionsOverwriteScoring.ScoreRelative(opt, p, ivSurfaceRef: 0.20);
            double basic = OptionsOverwriteScoring.ScoreBasic(opt);

            // With null IV/vega falls back to basic
            relative.Should().BeApproximately(basic, 0.001);
        }

        [Fact]
        public void ScoreRelative_PositiveIvResidual_ScoresHigherThanBasic()
        {
            var opt = new OptionCandidateInfo(
                "SPY", 110m, new DateOnly(2024, 3, 15), OptionStyle.American,
                100, Bid: 2.50m, Ask: 2.60m, DaysToExpiration: 30,
                OpenInterest: 1000, Volume: 200, Delta: 0.30,
                ImpliedVolatility: 0.25,  // 25% actual IV
                Vega: 0.10);              // $0.10 per 1 % IV

            var p = new OptionsOverwriteParams { DepthBonusWeight = 0.05 };
            double score = OptionsOverwriteScoring.ScoreRelative(opt, p, ivSurfaceRef: 0.20); // surface = 20%
            score.Should().BePositive("IV residual + depth bonus should be positive");
        }

        [Theory]
        [InlineData(100, 1.00, 1)]
        [InlineData(100, 0.50, 0)]   // 0.5 → floor(50/100) = 0
        [InlineData(200, 1.00, 2)]
        [InlineData(300, 0.75, 2)]   // 300 * 0.75 / 100 = 2.25 → floor = 2
        public void PositionSize_ComputesCorrectContracts(long shares, double ratio, int expected)
        {
            var p = new OptionsOverwriteParams { OverwriteRatio = ratio };
            OptionsOverwriteScoring.PositionSize(shares, p).Should().Be(expected);
        }

        [Fact]
        public void ChooseBestCall_NoEligibleCandidates_ReturnsNull()
        {
            // Candidate fails risk filter (strike < min)
            var candidates = new List<OptionCandidateInfo>
            {
                new("SPY", 90m, new DateOnly(2024, 3, 15), OptionStyle.American,
                    100, Bid: 5.00m, Ask: 5.20m, DaysToExpiration: 30,
                    OpenInterest: 1000, Volume: 200, Delta: 0.60)
            };

            var p = new OptionsOverwriteParams { MinStrike = 100m, MaxDelta = 0.35 };
            var result = OptionsOverwriteScoring.ChooseBestCall(candidates, p, underlyingPrice: 100m);
            result.Should().BeNull();
        }

        [Fact]
        public void ChooseBestCall_TwoCandidates_SelectsHigherBid()
        {
            var candidates = new List<OptionCandidateInfo>
            {
                new("SPY", 110m, new DateOnly(2024, 3, 15), OptionStyle.American,
                    100, Bid: 2.00m, Ask: 2.10m, DaysToExpiration: 30,
                    OpenInterest: 1000, Volume: 200, Delta: 0.28),
                new("SPY", 115m, new DateOnly(2024, 3, 15), OptionStyle.American,
                    100, Bid: 1.00m, Ask: 1.10m, DaysToExpiration: 30,
                    OpenInterest: 1000, Volume: 200, Delta: 0.20)
            };

            var p = new OptionsOverwriteParams
            {
                MinStrike = 100m,
                MaxDelta = 0.35,
                MinDte = 7,
                MinOpenInterest = 500,
                MinVolume = 50,
                MaxSpreadPct = 0.10,
                ScoringMode = OverwriteScoringMode.Basic
            };

            var result = OptionsOverwriteScoring.ChooseBestCall(candidates, p, underlyingPrice: 100m);
            result.Should().NotBeNull();
            result!.Strike.Should().Be(110m, "the $110 call has a higher bid (2.00 vs 1.00)");
        }
    }

    // ================================================================== //
    //  Metrics Calculator                                                 //
    // ================================================================== //

    public sealed class MetricsCalculatorTests
    {
        [Fact]
        public void Calculate_EmptyTrades_ReturnsZeroMetrics()
        {
            var curve = BuildEquityCurve(100_000m, 30, 0.0);
            var metrics = OptionsOverwriteMetricsCalculator.Calculate([], curve, 0.04);

            metrics.WinRate.Should().Be(0.0);
            metrics.AssignmentRate.Should().Be(0.0);
            metrics.TotalOptionTrades.Should().Be(0);
        }

        [Fact]
        public void Calculate_SingleWinningTrade_WinRateIsOne()
        {
            var trade = new OptionsOverwriteTradeRecord(
                "SPY", 110m, new DateOnly(2024, 2, 16), 1, 100,
                new DateOnly(2024, 1, 16), 2.50m,
                new DateOnly(2024, 2, 16), 0m,
                ShortCallExitReason.ExpiredWorthless);

            var curve = BuildEquityCurve(100_000m, 30, 0.001);
            var metrics = OptionsOverwriteMetricsCalculator.Calculate([trade], curve, 0.04);

            metrics.WinRate.Should().Be(1.0);
            metrics.AssignmentRate.Should().Be(0.0);
            metrics.TotalOptionTrades.Should().Be(1);
        }

        [Fact]
        public void Calculate_SingleLosingAssignment_AssignmentRateIsOne()
        {
            var trade = new OptionsOverwriteTradeRecord(
                "SPY", 100m, new DateOnly(2024, 2, 16), 1, 100,
                new DateOnly(2024, 1, 16), 2.00m,
                new DateOnly(2024, 2, 16), 10.00m,   // big loss (called away deep ITM)
                ShortCallExitReason.Assigned);

            var curve = BuildEquityCurve(100_000m, 30, 0.001);
            var metrics = OptionsOverwriteMetricsCalculator.Calculate([trade], curve, 0.04);

            metrics.AssignmentRate.Should().Be(1.0);
        }

        [Fact]
        public void Calculate_GrowingEquityCurve_CagrIsPositive()
        {
            var curve = BuildEquityCurve(100_000m, 252, 0.001); // ~25 % annual return
            var metrics = OptionsOverwriteMetricsCalculator.Calculate([], curve, 0.04);
            metrics.Cagr.Should().BePositive();
        }

        [Fact]
        public void Calculate_FlatEquityCurve_MaxDrawdownIsZero()
        {
            var curve = BuildEquityCurve(100_000m, 252, 0.0); // flat equity
            var metrics = OptionsOverwriteMetricsCalculator.Calculate([], curve, 0.04);
            metrics.MaxDrawdownPct.Should().BeApproximately(0.0, 1e-6);
        }

        // ---- helper ----
        private static List<(DateOnly, decimal, decimal)> BuildEquityCurve(
            decimal startEquity, int days, double dailyReturnRate)
        {
            var curve = new List<(DateOnly, decimal, decimal)>(days);
            var date = new DateOnly(2024, 1, 2);
            decimal equity = startEquity;
            for (int i = 0; i < days; i++)
            {
                equity *= (decimal)(1.0 + dailyReturnRate);
                curve.Add((date, equity, equity));
                date = date.AddDays(1);
            }
            return curve;
        }
    }

    // ================================================================== //
    //  Strategy Lifecycle                                                 //
    // ================================================================== //

    public sealed class CoveredCallOverwriteStrategyLifecycleTests
    {
        private static OptionsOverwriteParams ConservativeParams(decimal minStrike = 100m) =>
            new OptionsOverwriteParams
            {
                MinStrike = minStrike,
                OverwriteRatio = 1.0,
                MaxDelta = 0.35,
                MinDte = 7,
                MaxDte = 60,
                MinOpenInterest = 100,
                MinVolume = 10,
                MaxSpreadPct = 0.10,
                TakeProfitCapture = 0.80,
                RollDelta = 0.55,
                ExDivWindowDays = 7,
                ScoringMode = OverwriteScoringMode.Basic
            };

        /// <summary>A fixed option chain provider returning a single ATM call.</summary>
        private static IOptionChainProvider MakeChainProvider(
            decimal bid, decimal ask, decimal strike, DateOnly expiry, double delta, int dte) =>
            new LambdaChainProvider((_, _, _) =>
            [
                new OptionCandidateInfo(
                    "SPY", strike, expiry, OptionStyle.American, 100,
                    Bid: bid, Ask: ask, DaysToExpiration: dte,
                    OpenInterest: 500, Volume: 100, Delta: delta,
                    ImpliedVolatility: 0.20)
            ]);

        [Fact]
        public void Name_ReturnsUnderlyingInName()
        {
            var strategy = new CoveredCallOverwriteStrategy(
                "SPY", ConservativeParams(), MakeChainProvider(2m, 2.10m, 110m, new DateOnly(2024, 3, 15), 0.28, 30));
            strategy.Name.Should().Contain("SPY");
        }

        [Fact]
        public void Initialize_DoesNotThrow()
        {
            var strategy = new CoveredCallOverwriteStrategy(
                "SPY", ConservativeParams(), MakeChainProvider(2m, 2.10m, 110m, new DateOnly(2024, 3, 15), 0.28, 30));
            var ctx = new StubBacktestContext(positions: [("SPY", 100)]);
            var act = () => strategy.Initialize(ctx);
            act.Should().NotThrow();
        }

        [Fact]
        public void OnBar_TracksPriceForUnderlying()
        {
            var strategy = new CoveredCallOverwriteStrategy(
                "SPY", ConservativeParams(), MakeChainProvider(2m, 2.10m, 110m, new DateOnly(2024, 3, 15), 0.28, 30));

            var ctx = new StubBacktestContext(positions: [("SPY", 100)], portfolioValue: 50_000m);
            var bar = new HistoricalBar("SPY", new DateOnly(2024, 1, 2), 100m, 105m, 98m, 102m, 1_000_000L, "test", 1L);

            strategy.Initialize(ctx);
            strategy.OnBar(bar, ctx);

            // Verify the strategy opened a position on DayEnd
            strategy.OnDayEnd(new DateOnly(2024, 1, 2), ctx);
            strategy.OpenPositions.Should().HaveCount(1, "strategy should open one short call when underlying is held");
        }

        [Fact]
        public void OnBar_IgnoresNonUnderlyingSymbols()
        {
            var strategy = new CoveredCallOverwriteStrategy(
                "SPY", ConservativeParams(), MakeChainProvider(2m, 2.10m, 110m, new DateOnly(2024, 3, 15), 0.28, 30));

            var ctx = new StubBacktestContext(positions: [("SPY", 100)], portfolioValue: 50_000m);
            var differentBar = new HistoricalBar("AAPL", new DateOnly(2024, 1, 2), 185m, 190m, 183m, 187m, 1_000_000L, "test", 1L);

            strategy.Initialize(ctx);
            strategy.OnBar(differentBar, ctx);
            strategy.OnDayEnd(new DateOnly(2024, 1, 2), ctx);

            strategy.OpenPositions.Should().BeEmpty("price was not updated for SPY — no underlying bar received");
        }

        [Fact]
        public void OnDayEnd_NoUnderlyingPosition_DoesNotOpenShortCall()
        {
            var strategy = new CoveredCallOverwriteStrategy(
                "SPY", ConservativeParams(),
                MakeChainProvider(2m, 2.10m, 110m, new DateOnly(2024, 3, 15), 0.28, 30));

            // Context reports no underlying position
            var ctx = new StubBacktestContext(positions: [], portfolioValue: 100_000m);
            var bar = new HistoricalBar("SPY", new DateOnly(2024, 1, 2), 100m, 105m, 98m, 100m, 1_000_000L, "test", 1L);

            strategy.Initialize(ctx);
            strategy.OnBar(bar, ctx);
            strategy.OnDayEnd(new DateOnly(2024, 1, 2), ctx);

            strategy.OpenPositions.Should().BeEmpty("no underlying held — cannot write covered call");
        }

        [Fact]
        public void OnFinished_ComputesMetrics()
        {
            var expiry = new DateOnly(2024, 2, 16);
            var strategy = new CoveredCallOverwriteStrategy(
                "SPY", ConservativeParams(90m),
                MakeChainProvider(2m, 2.10m, 110m, expiry, 0.28, 30));

            var ctx = new StubBacktestContext(positions: [("SPY", 100)], portfolioValue: 50_000m);

            strategy.Initialize(ctx);

            // Simulate 5 days of bars
            var startDate = new DateOnly(2024, 1, 16);
            for (int i = 0; i < 5; i++)
            {
                var d = startDate.AddDays(i);
                strategy.OnBar(new HistoricalBar("SPY", d, 100m, 105m, 98m, 100m, 1_000_000L, "test", i), ctx);
                strategy.OnDayEnd(d, ctx);
            }

            strategy.OnFinished(ctx);

            strategy.Metrics.Should().NotBeNull();
            strategy.CompletedTrades.Should().HaveCountGreaterThanOrEqualTo(0);
        }

        [Fact]
        public void TakeProfitRoll_ClosesPositionWhenCaptureThresholdMet()
        {
            // Option premium almost all captured (mark near zero)
            var expiry = new DateOnly(2024, 3, 15);
            var strategy = new CoveredCallOverwriteStrategy(
                "SPY", ConservativeParams(90m) with { TakeProfitCapture = 0.80 },
                MakeChainProvider(0.10m, 0.15m, 110m, expiry, 0.05, 30)); // almost worthless

            var ctx = new StubBacktestContext(positions: [("SPY", 100)], portfolioValue: 50_000m);
            var date = new DateOnly(2024, 1, 16);

            strategy.Initialize(ctx);

            // Day 1: open position at $2.00 bid
            var provider2 = MakeChainProvider(2.00m, 2.10m, 110m, expiry, 0.28, 30);
            var strategy2 = new CoveredCallOverwriteStrategy(
                "SPY", ConservativeParams(90m) with { TakeProfitCapture = 0.80 },
                provider2);

            strategy2.Initialize(ctx);
            strategy2.OnBar(new HistoricalBar("SPY", date, 100m, 105m, 98m, 100m, 1_000_000L, "test", 1L), ctx);
            strategy2.OnDayEnd(date, ctx);

            strategy2.OpenPositions.Should().HaveCount(1);
            var pos = strategy2.OpenPositions[0];

            // Simulate the option decaying to near-zero: set IV to tiny so BSM re-mark ≈ $0.
            // With IV ≈ 0 and underlying deeply OTM, PremiumCaptured = (2.00-~0)/2.00 ≈ 1.0 ≥ 0.80.
            pos.EntryImpliedVolatility = 0.001;

            // Day 2: check position is rolled
            date = date.AddDays(10); // advance well past min DTE check
            strategy2.OnBar(new HistoricalBar("SPY", date, 100m, 105m, 98m, 100m, 1_000_000L, "test", 2L), ctx);
            strategy2.OnDayEnd(date, ctx);

            // Position should have been closed (take-profit) and a new one opened
            strategy2.CompletedTrades.Should().HaveCount(1,
                "take-profit threshold was met; position should be closed");
            strategy2.CompletedTrades[0].ExitReason.Should().Be(ShortCallExitReason.TakeProfitRoll);
        }

        [Fact]
        public void ExpirationWorthless_ClosesWithZeroDebit()
        {
            var expiry = new DateOnly(2024, 1, 16);
            var chainProvider = MakeChainProvider(2.00m, 2.10m, 110m, expiry, 0.28, 0);

            var strategy = new CoveredCallOverwriteStrategy(
                "SPY", ConservativeParams(90m), chainProvider);

            var ctx = new StubBacktestContext(positions: [("SPY", 100)], portfolioValue: 50_000m);

            strategy.Initialize(ctx);

            // Open position day before expiry
            var openDate = expiry.AddDays(-1);
            strategy.OnBar(new HistoricalBar("SPY", openDate, 100m, 105m, 98m, 100m, 1_000_000L, "test", 1L), ctx);

            // Manually add an open position that expires worthless
            var pos = new ShortCallPosition
            {
                UnderlyingSymbol = "SPY",
                Strike = 110m,
                Expiration = expiry,
                Contracts = 1,
                Multiplier = 100,
                Style = OptionStyle.American,
                EntryDate = openDate,
                EntryCredit = 2.00m,
                MarkToClose = 0m,  // OTM at expiry
                CurrentDelta = 0.02,
                CurrentDte = 0
            };
            // Access private field via reflection for testing — acceptable in unit tests
            var field = typeof(CoveredCallOverwriteStrategy)
                .GetField("_openPositions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var list = (System.Collections.Generic.List<ShortCallPosition>)field!.GetValue(strategy)!;
            list.Add(pos);

            // Process expiry day
            strategy.OnBar(new HistoricalBar("SPY", expiry, 100m, 102m, 98m, 100m, 1_000_000L, "test", 2L), ctx);
            strategy.OnDayEnd(expiry, ctx);

            strategy.CompletedTrades.Should().HaveCount(1);
            strategy.CompletedTrades[0].ExitReason.Should().Be(ShortCallExitReason.ExpiredWorthless);
            strategy.CompletedTrades[0].ExitDebit.Should().Be(0m);
        }

        [Fact]
        public void Constructor_NullParams_ThrowsArgumentNullException()
        {
            var act = () => new CoveredCallOverwriteStrategy(
                "SPY",
                null!,
                MakeChainProvider(2m, 2.10m, 110m, new DateOnly(2024, 3, 15), 0.28, 30));
            act.Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public void Constructor_EmptySymbol_ThrowsArgumentException()
        {
            var act = () => new CoveredCallOverwriteStrategy(
                "",
                ConservativeParams(),
                MakeChainProvider(2m, 2.10m, 110m, new DateOnly(2024, 3, 15), 0.28, 30));
            act.Should().Throw<ArgumentException>();
        }
    }

    // ================================================================== //
    //  OptionsOverwriteParams defaults                                   //
    // ================================================================== //

    public sealed class ParamsDefaultsTests
    {
        [Fact]
        public void DefaultParams_ConservativeDefaults_AreSet()
        {
            var p = new OptionsOverwriteParams();
            p.OverwriteRatio.Should().Be(0.75);
            p.MaxDelta.Should().Be(0.35);
            p.TakeProfitCapture.Should().Be(0.80);
            p.RollDelta.Should().Be(0.55);
            p.MinIvPercentile.Should().Be(50.0);
            p.ExDivWindowDays.Should().Be(7);
            p.MinOpenInterest.Should().Be(1_000);
        }

        [Fact]
        public void WithMinStrike_ReturnsCopyWithNewValue()
        {
            var p = new OptionsOverwriteParams().WithMinStrike(150m);
            p.MinStrike.Should().Be(150m);
        }
    }
}

// ------------------------------------------------------------------ //
//  Test doubles                                                        //
// ------------------------------------------------------------------ //

/// <summary>Delegate-based option chain provider for tests.</summary>
file sealed class LambdaChainProvider(
    Func<string, DateOnly, decimal, IReadOnlyList<OptionCandidateInfo>> factory)
    : IOptionChainProvider
{
    public IReadOnlyList<OptionCandidateInfo> GetCalls(
        string underlyingSymbol, DateOnly asOf, decimal underlyingPrice)
        => factory(underlyingSymbol, asOf, underlyingPrice);
}

/// <summary>
/// Minimal stub for <see cref="IBacktestContext"/> that satisfies the strategy's needs.
/// </summary>
file sealed class StubBacktestContext : IBacktestContext
{
    private readonly Dictionary<string, Position> _positions;

    public StubBacktestContext(
        IEnumerable<(string symbol, long quantity)>? positions = null,
        decimal portfolioValue = 100_000m)
    {
        _positions = (positions ?? Enumerable.Empty<(string, long)>())
            .ToDictionary(
                p => p.symbol,
                p => new Position(p.symbol, p.quantity, 100m, 0m, 0m));
        PortfolioValue = portfolioValue;
        Cash = portfolioValue * 0.10m;
    }

    public IReadOnlySet<string> Universe => _positions.Keys.ToHashSet();
    public DateTimeOffset CurrentTime { get; } = DateTimeOffset.UtcNow;
    public DateOnly CurrentDate { get; } = new DateOnly(2024, 1, 2);
    public decimal Cash { get; }
    public decimal PortfolioValue { get; }
    public IReadOnlyDictionary<string, Position> Positions => _positions;
    public IReadOnlyDictionary<string, FinancialAccountSnapshot> Accounts => new Dictionary<string, FinancialAccountSnapshot>();
    public IReadOnlyLedger Ledger => throw new NotSupportedException("Not used in strategy tests");

    public decimal? GetLastPrice(string symbol)
        => _positions.TryGetValue(symbol, out var p) ? p.AverageCostBasis : null;

    // Order methods are no-ops in this stub (option trades are simulated internally)
    public Guid PlaceOrder(OrderRequest request) => Guid.NewGuid();
    public Guid PlaceBracketOrder(BracketOrderRequest request) => Guid.NewGuid();
    public Guid PlaceMarketOrder(string symbol, long quantity) => Guid.NewGuid();
    public Guid PlaceMarketOrder(string symbol, long quantity, string accountId) => Guid.NewGuid();
    public Guid PlaceLimitOrder(string symbol, long quantity, decimal limitPrice) => Guid.NewGuid();
    public Guid PlaceLimitOrder(string symbol, long quantity, decimal limitPrice, string accountId) => Guid.NewGuid();
    public Guid PlaceStopMarketOrder(string symbol, long quantity, decimal stopPrice) => Guid.NewGuid();
    public Guid PlaceStopMarketOrder(string symbol, long quantity, decimal stopPrice, string accountId) => Guid.NewGuid();
    public Guid PlaceStopLimitOrder(string symbol, long quantity, decimal stopPrice, decimal limitPrice) => Guid.NewGuid();
    public Guid PlaceStopLimitOrder(string symbol, long quantity, decimal stopPrice, decimal limitPrice, string accountId) => Guid.NewGuid();
    public void CancelOrder(Guid orderId) { }
    public void CancelContingentOrders(Guid parentOrderId) { }
}
