using FluentAssertions;
using Meridian.Backtesting.Sdk;
using Meridian.Contracts.Workstation;
using Meridian.Strategies.Models;
using Xunit;

namespace Meridian.Tests.Strategies;

/// <summary>
/// Guards the v4 realism-bound input hash.
/// </summary>
/// <remarks>
/// Run identity previously covered strategy inputs, dataset, engine, and parameters but no
/// execution-realism setting. Two runs differing only in fill timing or cost model therefore
/// carried the same <c>InputHashSha256</c> while producing materially different P&amp;L, so run
/// diffing, sweep lineage, and promotion evidence treated them as the same experiment. These tests
/// pin both halves of the fix: realism now changes the digest, and the v4 scheme stays disjoint
/// from the earlier ones so a digest under one scheme is never read as agreement with another.
/// </remarks>
public sealed class StrategyRunRealismHashTests
{
    private const string StrategyId = "strat-1";
    private const string StrategyName = "Covered Call";
    private const string Dataset = "dataset://equities/2024";
    private const string Engine = "MeridianNative";

    private static ExecutionRealismDescriptor Realism(
        FillTiming fillTiming = FillTiming.NextBar,
        FillConservatism conservatism = FillConservatism.Conservative,
        BacktestCommissionKind commissionKind = BacktestCommissionKind.PerShare,
        decimal commissionRate = 0.005m,
        decimal slippageBasisPoints = 5m) => new(
            ExecutionModel.Auto,
            fillTiming,
            conservatism,
            DelistingPolicy.LiquidateAtLastPrice,
            DelistingHaircutPercent: 0m,
            DelistingGraceDays: 5,
            commissionKind,
            commissionRate,
            CommissionMinimum: 1.00m,
            CommissionMaximum: decimal.MaxValue,
            slippageBasisPoints,
            MaxParticipationRate: 0m,
            MarketImpactCoefficient: 0.1m,
            OrderBookQueueAheadFraction: 0m,
            AdjustForCorporateActions: true,
            RiskFreeRate: 0.04);

    private static string Hash(ExecutionRealismDescriptor? realism) =>
        StrategyRunEntry.ComputeRealismBoundInputHash(
            StrategyId,
            StrategyName,
            RunType.Backtest,
            Dataset,
            feedReference: null,
            Engine,
            parameterSet: null,
            executionRealism: realism);

    // ── Realism participates in run identity ─────────────────────────────────

    [Fact]
    public void FillConservatism_ChangesTheHash()
    {
        // The defect this fixes: these two runs produce different P&L and previously hashed alike.
        Hash(Realism(conservatism: FillConservatism.Conservative))
            .Should().NotBe(Hash(Realism(conservatism: FillConservatism.Optimistic)));
    }

    [Fact]
    public void FillTiming_ChangesTheHash()
    {
        Hash(Realism(fillTiming: FillTiming.NextBar))
            .Should().NotBe(Hash(Realism(fillTiming: FillTiming.SameBar)));
    }

    [Fact]
    public void CommissionModel_ChangesTheHash()
    {
        Hash(Realism(commissionKind: BacktestCommissionKind.PerShare))
            .Should().NotBe(Hash(Realism(commissionKind: BacktestCommissionKind.Free)));
    }

    [Fact]
    public void Slippage_ChangesTheHash()
    {
        Hash(Realism(slippageBasisPoints: 5m))
            .Should().NotBe(Hash(Realism(slippageBasisPoints: 25m)));
    }

    [Fact]
    public void IdenticalRealism_ProducesTheSameHash()
    {
        Hash(Realism()).Should().Be(Hash(Realism()));
    }

    [Fact]
    public void UnknownRealism_IsDistinctFromExplicitDefaults()
    {
        // "We do not know how this run was configured" is a different claim from "this run was
        // configured with the defaults", so the two must not collapse to one identity.
        Hash(realism: null).Should().NotBe(Hash(Realism()));
    }

    // ── Decimal scale must not affect identity ───────────────────────────────

    [Fact]
    public void EqualDecimalsWithDifferentScale_ProduceTheSameHash()
    {
        // 1.0m and 1.00m are equal numbers but render differently by default; if scale leaked into
        // the canonical form, one configuration would produce two identities.
        Hash(Realism(commissionRate: 1.0m)).Should().Be(Hash(Realism(commissionRate: 1.00m)));
    }

    [Fact]
    public void CanonicalString_IsStableForEqualDescriptors()
    {
        Realism().ToCanonicalString().Should().Be(Realism().ToCanonicalString());
    }

    [Fact]
    public void CanonicalString_NamesEveryField()
    {
        var canonical = Realism().ToCanonicalString();

        canonical.Should().Contain(nameof(ExecutionRealismDescriptor.FillTiming));
        canonical.Should().Contain(nameof(ExecutionRealismDescriptor.FillConservatism));
        canonical.Should().Contain(nameof(ExecutionRealismDescriptor.CommissionKind));
        canonical.Should().Contain(nameof(ExecutionRealismDescriptor.RiskFreeRate));
    }

    // ── Scheme isolation ─────────────────────────────────────────────────────

    [Fact]
    public void V4_IsDisjointFromV2AndV3ForIdenticalInputs()
    {
        var v2 = StrategyRunEntry.ComputeInputHash(
            StrategyId, StrategyName, RunType.Backtest, Dataset, null, Engine, parameterSet: null);
        var v3 = StrategyRunEntry.ComputeEvidenceBoundInputHash(
            StrategyId, StrategyName, RunType.Backtest, Dataset, null, Engine, parameterSet: null);
        var v4 = Hash(Realism());

        v4.Should().NotBe(v2);
        v4.Should().NotBe(v3);
        v2.Should().NotBe(v3);
    }

    [Fact]
    public void V4_WithUnknownRealism_StillDiffersFromV3()
    {
        // Even when realism is unknown, a v4 digest must not be mistaken for a v3 digest — the
        // schemes answer different questions and must never compare equal.
        var v3 = StrategyRunEntry.ComputeEvidenceBoundInputHash(
            StrategyId, StrategyName, RunType.Backtest, Dataset, null, Engine, parameterSet: null);

        Hash(realism: null).Should().NotBe(v3);
    }

    [Fact]
    public void Hash_IsACanonicalSha256Digest()
    {
        Meridian.Contracts.Integrity.Sha256Digest.IsCanonical(Hash(Realism())).Should().BeTrue();
    }

    // ── Existing inputs still matter ─────────────────────────────────────────

    [Fact]
    public void StrategyInputs_StillChangeTheHash()
    {
        var baseline = Hash(Realism());

        var differentDataset = StrategyRunEntry.ComputeRealismBoundInputHash(
            StrategyId, StrategyName, RunType.Backtest, "dataset://equities/2025", null, Engine,
            parameterSet: null, executionRealism: Realism());

        differentDataset.Should().NotBe(baseline);
    }

    [Fact]
    public void ParameterSet_StillChangesTheHash()
    {
        var baseline = Hash(Realism());

        var withParameters = StrategyRunEntry.ComputeRealismBoundInputHash(
            StrategyId, StrategyName, RunType.Backtest, Dataset, null, Engine,
            parameterSet: new Dictionary<string, string> { ["lookback"] = "20" },
            executionRealism: Realism());

        withParameters.Should().NotBe(baseline);
    }

    [Fact]
    public void RiskFreeRateSeries_ChangesTheRealismHash()
    {
        // The series overrides the scalar rate on the dates it covers and drives Sharpe/Sortino,
        // so two runs differing only here must not share an identity.
        var baseline = new BacktestRequest(
            new DateOnly(2024, 1, 1), new DateOnly(2024, 6, 30), RiskFreeRate: 0.04);
        var withSeries = baseline with
        {
            RiskFreeRateSeries = new Dictionary<DateOnly, double>
            {
                [new DateOnly(2024, 3, 1)] = 0.052,
                [new DateOnly(2024, 4, 1)] = 0.049
            }
        };

        baseline.ToRealismDescriptor().ToCanonicalString()
            .Should().NotBe(withSeries.ToRealismDescriptor().ToCanonicalString());
    }

    [Fact]
    public void RiskFreeRateSeries_HashesIndependentlyOfDictionaryOrder()
    {
        var a = new BacktestRequest(new DateOnly(2024, 1, 1), new DateOnly(2024, 6, 30)) with
        {
            RiskFreeRateSeries = new Dictionary<DateOnly, double>
            {
                [new DateOnly(2024, 3, 1)] = 0.052,
                [new DateOnly(2024, 4, 1)] = 0.049
            }
        };
        var b = new BacktestRequest(new DateOnly(2024, 1, 1), new DateOnly(2024, 6, 30)) with
        {
            RiskFreeRateSeries = new Dictionary<DateOnly, double>
            {
                [new DateOnly(2024, 4, 1)] = 0.049,
                [new DateOnly(2024, 3, 1)] = 0.052
            }
        };

        a.ToRealismDescriptor().ToCanonicalString()
            .Should().Be(b.ToRealismDescriptor().ToCanonicalString());
    }

    [Theory]
    [InlineData(0.05, 0.02, 0.09, 0.02)]
    [InlineData(0.05, 0.02, 0.05, 0.07)]
    public void FinancingRates_ChangeTheRealismHash(
        double baseMargin, double baseRebate, double otherMargin, double otherRebate)
    {
        // Margin interest and short rebate accrue into P&L, so they belong to run identity.
        var a = new BacktestRequest(
            new DateOnly(2024, 1, 1), new DateOnly(2024, 6, 30),
            AnnualMarginRate: baseMargin, AnnualShortRebateRate: baseRebate);
        var b = new BacktestRequest(
            new DateOnly(2024, 1, 1), new DateOnly(2024, 6, 30),
            AnnualMarginRate: otherMargin, AnnualShortRebateRate: otherRebate);

        a.ToRealismDescriptor().ToCanonicalString()
            .Should().NotBe(b.ToRealismDescriptor().ToCanonicalString());
    }

    // ── Default brokerage account routing ────────────────────────────────────

    [Fact]
    public void DefaultBrokerageAccount_ChangesTheRealismHash()
    {
        // With multiple accounts, the engine routes every order that names no account to the
        // default brokerage account, changing cash, financing, and potentially P&L. The account
        // list itself is identical here — only the routing default differs — so without the
        // dedicated field these two materially different runs would share one identity.
        IReadOnlyList<FinancialAccount> accounts =
        [
            new("brokerage-a", "Brokerage A", FinancialAccountKind.Brokerage, InitialCash: 50_000m),
            new("brokerage-b", "Brokerage B", FinancialAccountKind.Brokerage, InitialCash: 50_000m)
        ];
        var routedToA = new BacktestRequest(
            new DateOnly(2024, 1, 1), new DateOnly(2024, 6, 30),
            Accounts: accounts, DefaultBrokerageAccountId: "brokerage-a");
        var routedToB = routedToA with { DefaultBrokerageAccountId = "brokerage-b" };

        routedToA.ToRealismDescriptor().ToCanonicalString()
            .Should().NotBe(routedToB.ToRealismDescriptor().ToCanonicalString());
    }

    [Fact]
    public void DefaultBrokerageAccount_AbsentFromDescriptorsPersistedBeforeTheFieldExisted()
    {
        // Descriptors stored before the field existed deserialize with a null default account and
        // must recompute their original canonical form, or the durable store would reject every
        // lifecycle update on a previously recorded run. Only the null case may omit the field.
        var legacy = Realism() with { DefaultBrokerageAccountId = null };
        var current = Realism() with { DefaultBrokerageAccountId = "brokerage:default" };

        legacy.ToCanonicalString().Should().NotContain(nameof(ExecutionRealismDescriptor.DefaultBrokerageAccountId));
        current.ToCanonicalString().Should().Contain(
            $"{nameof(ExecutionRealismDescriptor.DefaultBrokerageAccountId)}=brokerage:default");
        legacy.ToCanonicalString().Should().NotBe(current.ToCanonicalString());
    }
}
