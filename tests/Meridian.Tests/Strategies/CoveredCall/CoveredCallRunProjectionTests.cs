using FluentAssertions;
using Meridian.Backtesting.Sdk.Strategies.OptionsOverwrite;
using Meridian.Contracts.Domain.Enums;
using Meridian.Strategies.Models;
using Meridian.Ui.Shared.Contracts;
using Meridian.Ui.Shared.Services.CoveredCall;
using Xunit;

namespace Meridian.Tests.Strategies.CoveredCall;

public sealed class CoveredCallRunProjectionTests
{
    private static readonly CoveredCallRunScope Scope = new("tenant-alpha", "company-alpha", "strategy-operator");

    private static CoveredCallBacktestRequest ValidRequest() => new(
        UnderlyingSymbol: "SPY",
        From: new DateOnly(2024, 1, 1),
        To: new DateOnly(2024, 6, 30),
        MinStrike: 500m)
    {
        OperatorAcceptanceCriteria = ["Operator reviewed the retained covered-call backtest evidence."],
        RetainedEvidenceReferences = ["evidence://strategy-runs/covered-call-overwrite"]
    };

    [Fact]
    public void CreateEvidenceBackedRunEntry_NormalizesAndRetainsW6LineageBeforeExecution()
    {
        var request = ValidRequest() with
        {
            OperatorAcceptanceCriteria =
            [
                " Operator reviewed the retained covered-call backtest evidence. ",
                "operator reviewed the retained covered-call backtest evidence."
            ],
            RetainedEvidenceReferences =
            [
                " evidence://strategy-runs/covered-call-overwrite ",
                "evidence://strategy-runs/covered-call-overwrite"
            ],
            ApprovalReferences = ["approval://strategy-runs/covered-call-overwrite"]
        };

        var entry = CoveredCallRunProjection.CreateEvidenceBackedRunEntry(
            request,
            Scope,
            "covered-call-run-1",
            new Dictionary<string, string>
            {
                [CoveredCallBacktestService.TenantParameterKey] = Scope.TenantId,
                [CoveredCallBacktestService.CompanyParameterKey] = Scope.CompanyId,
                ["underlyingSymbol"] = "SPY"
            });

        entry.RunType.Should().Be(RunType.Backtest);
        entry.Engine.Should().Be("MeridianNative");
        entry.StrategyId.Should().Be(CoveredCallBacktestService.GetScopedStrategyId(Scope));
        entry.ActorId.Should().Be(Scope.Actor);
        entry.OperatorAcceptanceCriteria.Should()
            .ContainSingle("Operator reviewed the retained covered-call backtest evidence.");
        entry.RetainedEvidenceReferences.Should()
            .ContainSingle("evidence://strategy-runs/covered-call-overwrite");
        entry.ApprovalReferences.Should()
            .ContainSingle("approval://strategy-runs/covered-call-overwrite");
        entry.InputHashSha256.Should().Be(StrategyRunEntry.ComputeEvidenceBoundInputHash(
            entry.StrategyId,
            entry.StrategyName,
            entry.RunType,
            entry.DatasetReference,
            entry.FeedReference,
            entry.Engine,
            entry.ParameterSet,
            entry.ParentRunId,
            entry.PortfolioId,
            entry.LedgerReference,
            entry.AuditReference,
            entry.FundProfileId,
            entry.OperatorAcceptanceCriteria,
            entry.RetainedEvidenceReferences,
            entry.AccountingRecordReferences,
            entry.ApprovalReferences,
            entry.PaperValidationReferences,
            entry.GovernedReportReferences));
    }

    [Fact]
    public void RequireEvidenceLoop_MissingOperatorChecklist_RejectsBeforeQueueing()
    {
        var request = ValidRequest() with { OperatorAcceptanceCriteria = [] };

        var act = () => CoveredCallRunProjection.RequireEvidenceLoop(request);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*operator acceptance criterion*");
    }

    [Fact]
    public void ToParams_MapsAllWhitelistedFields()
    {
        var request = ValidRequest() with
        {
            OverwriteRatio = 0.5,
            MaxDelta = 0.25,
            MinDte = 14,
            MaxDte = 45,
            MinIvPercentile = 60,
            MinOpenInterest = 500,
            MinVolume = 50,
            MaxSpreadPct = 0.10,
            TakeProfitCapture = 0.75,
            RollDelta = 0.60,
            ExDivWindowDays = 5,
            ScoringMode = OverwriteScoringModeDto.Basic,
            DepthBonusWeight = 0.10,
            RiskFreeRate = 0.05
        };

        var p = CoveredCallRunProjection.ToParams(request);

        p.MinStrike.Should().Be(500m);
        p.OverwriteRatio.Should().Be(0.5);
        p.MaxDelta.Should().Be(0.25);
        p.MinDte.Should().Be(14);
        p.MaxDte.Should().Be(45);
        p.MinIvPercentile.Should().Be(60);
        p.MinOpenInterest.Should().Be(500);
        p.MinVolume.Should().Be(50);
        p.MaxSpreadPct.Should().Be(0.10);
        p.TakeProfitCapture.Should().Be(0.75);
        p.RollDelta.Should().Be(0.60);
        p.ExDivWindowDays.Should().Be(5);
        p.ScoringMode.Should().Be(OverwriteScoringMode.Basic);
        p.DepthBonusWeight.Should().Be(0.10);
        p.RiskFreeRate.Should().Be(0.05);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1.0)]
    [InlineData(1.5)]
    public void ToParams_RejectsInvalidOverwriteRatio(double ratio)
    {
        var request = ValidRequest() with { OverwriteRatio = ratio };

        var act = () => CoveredCallRunProjection.ToParams(request);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToParams_RejectsNonPositiveMinStrike()
    {
        var request = ValidRequest() with { MinStrike = 0m };

        var act = () => CoveredCallRunProjection.ToParams(request);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToMetrics_RoundTripsAllFields()
    {
        var metrics = new OptionsOverwriteMetrics(
            Cagr: 0.12,
            AnnualizedVolatility: 0.15,
            SharpeRatio: 0.80,
            SortinoRatio: 1.0,
            CalmarRatio: 2.0,
            MaxDrawdownPct: -0.06,
            WinRate: 0.7,
            AssignmentRate: 0.05,
            AverageHoldingDays: 18.5,
            TotalOptionTrades: 25,
            AssignedTrades: 1,
            TotalPremiumCollected: 12500m,
            TotalOptionPnl: 9000m,
            UpCapture: 0.55,
            DownCapture: 0.95,
            MonthlyVar1Pct: -0.08,
            MonthlyVar5Pct: -0.05,
            MonthlyCVar5Pct: -0.06,
            ReturnSkewness: -0.20,
            ReturnKurtosis: 3.2,
            AnnualizedTurnover: 11.0,
            EquityCurve: Array.Empty<(DateOnly, decimal, decimal)>());

        var dto = CoveredCallRunProjection.ToMetrics(metrics);

        dto.Cagr.Should().Be(0.12);
        dto.SharpeRatio.Should().Be(0.80);
        dto.TotalOptionTrades.Should().Be(25);
        dto.TotalPremiumCollected.Should().Be(12500m);
        dto.AnnualizedTurnover.Should().Be(11.0);
    }

    [Fact]
    public void ToTrade_PreservesExitReasonAsString()
    {
        var trade = new OptionsOverwriteTradeRecord(
            UnderlyingSymbol: "SPY",
            Strike: 525m,
            Expiration: new DateOnly(2024, 2, 16),
            Contracts: 1,
            Multiplier: 100,
            EntryDate: new DateOnly(2024, 1, 16),
            EntryCredit: 4.50m,
            ExitDate: new DateOnly(2024, 2, 16),
            ExitDebit: 0m,
            ExitReason: ShortCallExitReason.ExpiredWorthless,
            EntryImpliedVolatility: 0.18);

        var dto = CoveredCallRunProjection.ToTrade(trade);

        dto.ExitReason.Should().Be(nameof(ShortCallExitReason.ExpiredWorthless));
        dto.IsWin.Should().BeTrue();
        dto.WasAssigned.Should().BeFalse();
        dto.NetPnlPerContract.Should().Be(450m);
        dto.HoldingDays.Should().Be(31);
    }

    [Fact]
    public void ToOpenPosition_PreservesPosition()
    {
        var pos = new ShortCallPosition
        {
            UnderlyingSymbol = "SPY",
            Strike = 530m,
            Expiration = new DateOnly(2024, 3, 15),
            Contracts = 2,
            Multiplier = 100,
            Style = OptionStyle.American,
            EntryDate = new DateOnly(2024, 2, 15),
            EntryCredit = 3.20m,
            EntryImpliedVolatility = 0.22,
            MarkToClose = 2.10m,
            CurrentDelta = 0.30,
            CurrentDte = 29
        };

        var dto = CoveredCallRunProjection.ToOpenPosition(pos);

        dto.Strike.Should().Be(530m);
        dto.Contracts.Should().Be(2);
        dto.UnrealisedPnl.Should().Be((3.20m - 2.10m) * 2 * 100);
        dto.CurrentDelta.Should().Be(0.30);
        dto.PositionId.Should().Be(pos.PositionId);
    }

    [Fact]
    public void ToEquityCurve_EmptyListReturnsEmptyArray()
    {
        var result = CoveredCallRunProjection.ToEquityCurve(
            Array.Empty<(DateOnly, decimal, decimal)>());

        result.Should().BeEmpty();
    }
}
