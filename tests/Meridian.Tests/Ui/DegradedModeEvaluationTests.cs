using FluentAssertions;
using Meridian;
using Meridian.Core.Config;
using Xunit;

namespace Meridian.Tests.Ui;

/// <summary>
/// Unit coverage for <see cref="UiServer.ResolveCandidateStreamingSources"/> and
/// <see cref="UiServer.IsSimulatedSource"/>, the static configuration facts behind the
/// <c>/api/status</c> degraded-mode banner. These assert the fail-closed posture: any
/// configuration under which <c>CollectorModeRunner</c> could feed the pipeline with a
/// simulated source must surface a simulated candidate here.
/// </summary>
public sealed class DegradedModeEvaluationTests
{
    [Fact]
    public void NoFailover_SyntheticTopLevel_IsSimulated()
    {
        var candidates = UiServer.ResolveCandidateStreamingSources(null, DataSourceKind.Synthetic);

        candidates.Should().Equal(DataSourceKind.Synthetic);
        candidates.Where(UiServer.IsSimulatedSource).Should().NotBeEmpty();
    }

    [Fact]
    public void NoFailover_RealTopLevel_IsNotSimulated()
    {
        var candidates = UiServer.ResolveCandidateStreamingSources(null, DataSourceKind.Alpaca);

        candidates.Should().Equal(DataSourceKind.Alpaca);
        candidates.Where(UiServer.IsSimulatedSource).Should().BeEmpty();
    }

    [Fact]
    public void Failover_WithUnbuildableRealSource_FallsBackToSyntheticTopLevel_IsSimulated()
    {
        // Regression for the P1: a Yahoo failover source has no registered streaming factory, so
        // every rule provider fails to construct and CollectorModeRunner falls back to
        // CreateStreamingClient(ctx.Config.DataSource). With a Synthetic top-level default that is
        // the simulator, yet the rule provider (Yahoo) is not itself simulated. The top-level
        // source must remain a candidate so the banner cannot report "live".
        var config = new DataSourcesConfig(
            Sources: new[] { new DataSourceConfig("yahoo-rt", "Yahoo Realtime", DataSourceKind.Yahoo) },
            EnableFailover: true,
            FailoverRules: new[] { new FailoverRuleConfig("rule-1", "yahoo-rt", System.Array.Empty<string>()) });

        var candidates = UiServer.ResolveCandidateStreamingSources(config, DataSourceKind.Synthetic);

        candidates.Should().Contain(DataSourceKind.Synthetic);
        candidates.Should().Contain(DataSourceKind.Yahoo);
        candidates.Where(UiServer.IsSimulatedSource).Should().Contain(DataSourceKind.Synthetic);
    }

    [Fact]
    public void Failover_WithDisabledSyntheticBackup_IsSimulated()
    {
        // CollectorModeRunner does not consult Enabled/Type, so a disabled synthetic backup named
        // in a rule still constructs and can publish fabricated prices — it must be flagged even
        // when the top-level source is real.
        var config = new DataSourcesConfig(
            Sources: new[]
            {
                new DataSourceConfig("primary", "Primary", DataSourceKind.Alpaca),
                new DataSourceConfig("backup", "Synthetic backup", DataSourceKind.Synthetic, Enabled: false)
            },
            EnableFailover: true,
            FailoverRules: new[] { new FailoverRuleConfig("rule-1", "primary", new[] { "backup" }) });

        var candidates = UiServer.ResolveCandidateStreamingSources(config, DataSourceKind.Alpaca);

        candidates.Should().Contain(DataSourceKind.Synthetic);
        candidates.Where(UiServer.IsSimulatedSource).Should().Contain(DataSourceKind.Synthetic);
    }

    [Fact]
    public void Failover_AllRealSourcesAndRealTopLevel_IsNotSimulated()
    {
        var config = new DataSourcesConfig(
            Sources: new[]
            {
                new DataSourceConfig("primary", "Primary", DataSourceKind.Alpaca),
                new DataSourceConfig("backup", "Backup", DataSourceKind.Polygon)
            },
            EnableFailover: true,
            FailoverRules: new[] { new FailoverRuleConfig("rule-1", "primary", new[] { "backup" }) });

        var candidates = UiServer.ResolveCandidateStreamingSources(config, DataSourceKind.Alpaca);

        candidates.Where(UiServer.IsSimulatedSource).Should().BeEmpty();
    }

    [Fact]
    public void FailoverDisabled_IgnoresRules_UsesTopLevelOnly()
    {
        // EnableFailover == false means CollectorModeRunner never reads the rules, so a synthetic
        // source named in a dormant rule does not feed the pipeline.
        var config = new DataSourcesConfig(
            Sources: new[] { new DataSourceConfig("syn", "Synthetic", DataSourceKind.Synthetic) },
            EnableFailover: false,
            FailoverRules: new[] { new FailoverRuleConfig("rule-1", "syn", System.Array.Empty<string>()) });

        var candidates = UiServer.ResolveCandidateStreamingSources(config, DataSourceKind.Alpaca);

        candidates.Should().Equal(DataSourceKind.Alpaca);
        candidates.Where(UiServer.IsSimulatedSource).Should().BeEmpty();
    }

    [Fact]
    public void IsSimulatedSource_ClassifiesKnownKinds()
    {
        UiServer.IsSimulatedSource(DataSourceKind.Synthetic).Should().BeTrue();
        UiServer.IsSimulatedSource(DataSourceKind.Alpaca).Should().BeFalse();
        UiServer.IsSimulatedSource(DataSourceKind.Polygon).Should().BeFalse();
        UiServer.IsSimulatedSource(DataSourceKind.Yahoo).Should().BeFalse();
        UiServer.IsSimulatedSource(DataSourceKind.NYSE).Should().BeFalse();

        // IB is a simulator only in builds without the IBAPI reference; assert it tracks that flag
        // rather than a fixed value so the test is stable across build configurations.
        UiServer.IsSimulatedSource(DataSourceKind.IB).Should().Be(
            Meridian.Infrastructure.Adapters.InteractiveBrokers.IBMarketDataClient.IsSimulationBuild);
    }
}
